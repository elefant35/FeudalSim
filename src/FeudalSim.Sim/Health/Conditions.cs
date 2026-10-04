using System.IO.Hashing;
using System.Runtime.InteropServices;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;

namespace FeudalSim.Sim.Health;

/// <summary>One running disease, toxin or food poisoning (11 §7): stage 0 is incubation, k ≥ 1 the k-th stage.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
[MessagePack.MessagePackObject]
public struct Condition
{
    [MessagePack.Key(0)] public long StageEndsMin;
    [MessagePack.Key(1)] public int Disease;
    [MessagePack.Key(2)] public byte Stage;
    [MessagePack.Key(3)] public byte Reserved0;
    [MessagePack.Key(4)] public byte Reserved1;
    [MessagePack.Key(5)] public byte Reserved2;
}

/// <summary>Conditions and immunities by person (M2-07a). Saved and hashed.</summary>
public sealed class ConditionStore
{
    private readonly SortedDictionary<ulong, List<Condition>> _byPerson = [];
    private readonly SortedDictionary<(ulong Person, int Disease), long> _immuneUntil = [];

    public IReadOnlyList<Condition> Of(EntityId person) => _byPerson.TryGetValue(person.Value, out var l) ? l : [];

    internal List<Condition>? ListOf(EntityId person) => _byPerson.TryGetValue(person.Value, out var l) ? l : null;

    public bool Has(EntityId person, int disease) => Of(person).Any(c => c.Disease == disease);

    public bool Immune(EntityId person, int disease, long nowMin) => _immuneUntil.GetValueOrDefault((person.Value, disease), long.MinValue) > nowMin;

    internal void Add(EntityId person, Condition c)
    {
        if (!_byPerson.TryGetValue(person.Value, out var list)) { _byPerson[person.Value] = list = []; }
        list.Add(c);
    }

    internal void Immunize(EntityId person, int disease, long untilMin) => _immuneUntil[(person.Value, disease)] = untilMin;

    internal void Prune(EntityId person)
    {
        if (_byPerson.TryGetValue(person.Value, out var list) && list.Count == 0) { _byPerson.Remove(person.Value); }
    }

    [MessagePack.MessagePackObject]
    public sealed class Snapshot
    {
        [MessagePack.Key(0)] public ulong[] People { get; set; } = [];
        [MessagePack.Key(1)] public Condition[] Conditions { get; set; } = [];
        [MessagePack.Key(2)] public ulong[] ImmunePeople { get; set; } = [];
        [MessagePack.Key(3)] public int[] ImmuneDiseases { get; set; } = [];
        [MessagePack.Key(4)] public long[] ImmuneUntil { get; set; } = [];
    }

    internal Snapshot Export() => new()
    {
        People = [.. _byPerson.SelectMany(kv => kv.Value.Select(_ => kv.Key))], Conditions = [.. _byPerson.SelectMany(kv => kv.Value)],
        ImmunePeople = [.. _immuneUntil.Keys.Select(k => k.Person)], ImmuneDiseases = [.. _immuneUntil.Keys.Select(k => k.Disease)], ImmuneUntil = [.. _immuneUntil.Values],
    };

    internal void Import(Snapshot s)
    {
        _byPerson.Clear();
        _immuneUntil.Clear();
        for (var k = 0; k < s.People.Length; k++)
        {
            if (!_byPerson.TryGetValue(s.People[k], out var list)) { _byPerson[s.People[k]] = list = []; }
            list.Add(s.Conditions[k]);
        }

        for (var k = 0; k < s.ImmunePeople.Length; k++) { _immuneUntil[(s.ImmunePeople[k], s.ImmuneDiseases[k])] = s.ImmuneUntil[k]; }
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        foreach (var (person, list) in _byPerson)
        {
            BitConverter.TryWriteBytes(b, person);
            h.Append(b);
            h.Append(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list)));
        }

        foreach (var ((person, disease), until) in _immuneUntil)
        {
            BitConverter.TryWriteBytes(b, person ^ ((ulong)(uint)disease << 40));
            h.Append(b);
            BitConverter.TryWriteBytes(b, until);
            h.Append(b);
        }
    }
}

/// <summary>
/// 11 §7 conditions (M2-07a): infection (incubation drawn from the disease's range), stages with effects, the grave branch
/// at the end of a <c>grave</c> stage (base × child/elder/malnourished/starving multipliers), recovery into immunity, and
/// the per-person effect sums the needs and health systems read. Draws are keyed per person, disease and stage.
/// </summary>
public static class Conditions
{
    /// <summary>Starts a disease unless the person has it or is immune. Returns whether it started.</summary>
    public static bool Infect(SimWorld world, int row, int disease, ulong salt = 0)
    {
        var people = world.People;
        var id = people.Ids[row];
        var now = world.Clock.GameMinute;
        if (disease < 0 || world.IsDead(row) || world.Conditions.Has(id, disease) || world.Conditions.Immune(id, disease, now)) { return false; }
        var def = world.Content.Diseases[disease];
        var rng = Draw(world, id, disease, 0, salt ^ (ulong)now);
        world.Conditions.Add(id, new Condition { Disease = disease, Stage = 0, StageEndsMin = now + (long)MathF.Round(rng.Uniform(def.IncubationH[0], def.IncubationH[1]) * 60f) });
        world.Emit(Salience.Notable, id, new ConditionStarted(id, disease));
        return true;
    }

    private static Rng Draw(SimWorld world, EntityId id, int disease, int stage, ulong salt)
        => new(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Health, id.Value, ((ulong)(uint)disease << 8) | (uint)stage, salt ^ Salt.Condition));

    /// <summary>Advances one person's conditions to <paramref name="now"/>; returns true if a grave branch killed them.</summary>
    internal static bool Advance(SimWorld world, int row, long now)
    {
        var people = world.People;
        var id = people.Ids[row];
        if (world.Conditions.ListOf(id) is not { } list) { return false; }
        for (var k = list.Count - 1; k >= 0; k--)
        {
            var c = list[k];
            var def = world.Content.Diseases[c.Disease];
            while (now >= c.StageEndsMin)
            {
                if (c.Stage >= 1 && def.Stages[c.Stage - 1].Grave && Dies(world, row, def, c))
                {
                    list.RemoveAt(k);
                    world.Conditions.Prune(id);
                    return true;
                }

                if (c.Stage >= def.Stages.Count)
                {
                    list.RemoveAt(k);
                    if (def.ImmunityDays > 0f) { world.Conditions.Immunize(id, c.Disease, now + (long)(def.ImmunityDays * 1440f)); }
                    world.Emit(Salience.Minor, id, new ConditionEnded(id, c.Disease));
                    goto next;
                }

                var stage = def.Stages[c.Stage];
                var rng = Draw(world, id, c.Disease, c.Stage + 1, (ulong)c.StageEndsMin);
                c.StageEndsMin += (long)MathF.Round(rng.Uniform(stage.Hours[0], stage.Hours[1]) * 60f);
                c.Stage++;
            }

            list[k] = c;
        next:;
        }

        world.Conditions.Prune(id);
        return false;
    }

    private static bool Dies(SimWorld world, int row, DiseaseDef def, Condition c)
        => def.Grave is not null && Draw(world, world.People.Ids[row], c.Disease, 99, (ulong)c.StageEndsMin).Chance(GraveChance(world, row, def));

    /// <summary>
    /// The grave branch's death chance for this person now: base × child (&lt; 14) / adult / elder (≥ 65) × malnourished
    /// (Starvation ≥ 50) or starving (≥ 75) multipliers, capped at 1 (11 §7.2, §8.1).
    /// </summary>
    internal static float GraveChance(SimWorld world, int row, DiseaseDef def)
    {
        if (def.Grave is not { } g) { return 0f; }
        var people = world.People;
        var age = (world.Clock.GameMinute - people.Core[row].BirthGameMinute) / Time.GameDate.MinutesPerYear;
        var starvation = people.Vitals[row].Starvation;
        var ageMult = g.Mult.GetValueOrDefault(age < 14 ? "child" : age >= 65 ? "elder" : "adult", 1f);
        var p = g.Base * ageMult * (starvation >= 75f ? g.Mult.GetValueOrDefault("starving", 1f) : starvation >= 50f ? g.Mult.GetValueOrDefault("malnourished", 1f) : 1f);
        return Math.Min(1f, p);
    }

    /// <summary>The sums the needs and health systems read for one person (11 §7.1 effect keys).</summary>
    public readonly record struct Effects(float HydrationMult, float SatietyAbsorb, float Work, float LoadPoints, bool Fever, float Contagious);

    public static Effects Of(SimWorld world, EntityId person)
    {
        var (hyd, absorb, work, load, fever, contagious) = (1f, 1f, 1f, 0f, false, 0f);
        if (world.Conditions.ListOf(person) is not { } list) { return new Effects(hyd, absorb, work, load, fever, contagious); }
        for (var k = 0; k < list.Count; k++)   // index loop: no enumerator on the needs hot path
        {
            var c = list[k];
            if (c.Stage == 0) { continue; }
            var stage = world.Content.Diseases[c.Disease].Stages[c.Stage - 1];
            hyd *= stage.Effects.GetValueOrDefault("hydration_decay", 1f);
            absorb *= stage.Effects.GetValueOrDefault("satiety_absorb", 1f);
            work *= stage.Effects.GetValueOrDefault("work", 1f);
            load += 50f * stage.Effects.GetValueOrDefault("condition_load", 0f);
            fever |= stage.Fever;
            contagious = MathF.Max(contagious, stage.Contagious);
        }

        return new Effects(hyd, absorb, work, load, fever, contagious);
    }
}
