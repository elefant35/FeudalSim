using System.IO.Hashing;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Social;

public enum MemoryKind : ushort { Chat, Joke, Praise, Comfort, Help, Refused, Argue, Insult, Apology, Gift, Threat, Assault, Told, Warned, Stole }

/// <summary>An episodic memory (16 §3.1, §6). Times in game minutes; valence from the owner's point of view.</summary>
public struct MemoryRecord
{
    public const byte CoreFlag = 1;

    public MemoryKind Kind;
    public ulong Actor, Target;
    public long TimeMin, SalienceTimeMin;
    public sbyte Valence;
    public byte S0, Flags;
    public ushort Count;

    public readonly bool Core => (Flags & CoreFlag) != 0;
}

/// <summary>
/// Memories per person (16 §6): salience <c>S(t) = S0·0.5^(Δt/h)</c>, <c>h = 2 + 0.3·S0</c> days; core memories
/// (S0 ≥ 80, |valence| ≥ 60, self-relevant) keep a floor of 0.5·S0; nightly compaction deletes S &lt; 3, merges
/// same (kind, actor, target) into pattern memories (S = max + 5·log2(Count)) and caps at 160 (core 24).
/// </summary>
public sealed class MemoryStore
{
    public const int Cap = 160;
    public const int CoreCap = 24;
    private readonly SortedDictionary<ulong, List<MemoryRecord>> _byPerson = [];

    public int Count => _byPerson.Values.Sum(l => l.Count);

    public IReadOnlyList<MemoryRecord> Of(EntityId person) => _byPerson.TryGetValue(person.Value, out var l) ? l : [];

    /// <summary>Allocation-free view for sim hot paths (valid until the next Remember/Compact).</summary>
    public ReadOnlySpan<MemoryRecord> Span(EntityId person)
        => _byPerson.TryGetValue(person.Value, out var l) ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(l) : [];

    /// <summary>16 §6.2: S0 = clamp(base × relevance × (1 + intensity/100) × novelty); kept only if S0 ≥ 5.</summary>
    public void Remember(EntityId owner, MemoryKind kind, EntityId actor, EntityId target, long nowMin, int baseKind, float relevance, float intensity, sbyte valence)
    {
        var s0 = Math.Clamp(baseKind * relevance * (1f + (intensity / 100f)), 0f, 100f);
        if (s0 < 5f) { return; }
        if (!_byPerson.TryGetValue(owner.Value, out var list)) { _byPerson[owner.Value] = list = []; }
        var core = s0 >= 80f && Math.Abs(valence) >= 60 && relevance >= 0.75f;
        list.Add(new MemoryRecord
        {
            Kind = kind, Actor = actor.Value, Target = target.Value, TimeMin = nowMin, SalienceTimeMin = nowMin, Valence = valence,
            S0 = (byte)MathF.Round(s0), Flags = core ? MemoryRecord.CoreFlag : (byte)0, Count = 1,
        });
    }

    public static float Salience(in MemoryRecord m, long nowMin)
    {
        var h = (2f + (0.3f * m.S0)) * 1440f;
        var s = m.S0 * SimMath.Exp(-0.6931472f * (nowMin - m.SalienceTimeMin) / h);
        return m.Core ? MathF.Max(s, 0.5f * m.S0) : s;
    }

    /// <summary>16 §4.12 Enemy needs "a memory with salience ≥ 50, valence ≤ −50 about B".</summary>
    public bool HasGraveMemoryAbout(EntityId owner, EntityId about, long nowMin)
    {
        if (!_byPerson.TryGetValue(owner.Value, out var list)) { return false; }
        foreach (ref readonly var m in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list)) { if (m.Actor == about.Value && m.Valence <= -50 && Salience(m, nowMin) >= 50f) { return true; } }
        return false;
    }

    /// <summary>Nightly compaction (16 §6.3).</summary>
    public void Compact(long nowMin)
    {
        foreach (var list in _byPerson.Values)
        {
            list.RemoveAll(m => !m.Core && Salience(m, nowMin) < 3f);
            var merged = new List<MemoryRecord>(list.Count);
            foreach (var group in list.GroupBy(m => (m.Kind, m.Actor, m.Target)))
            {
                if (group.Count() == 1) { merged.Add(group.First()); continue; }
                var count = group.Sum(m => m.Count);
                var best = group.MaxBy(m => Salience(m, nowMin));
                var s = Math.Min(100f, Salience(best, nowMin) + (5f * MathF.Log2(count)));
                merged.Add(best with { Count = (ushort)Math.Min(ushort.MaxValue, count), S0 = (byte)MathF.Round(s), SalienceTimeMin = nowMin, TimeMin = group.Max(m => m.TimeMin) });
            }

            merged.Sort((a, b) => a.TimeMin != b.TimeMin ? a.TimeMin.CompareTo(b.TimeMin) : a.Actor.CompareTo(b.Actor));
            var cores = merged.Where(m => m.Core).OrderBy(m => m.S0).ToList();
            while (cores.Count > CoreCap) { merged.Remove(cores[0]); cores.RemoveAt(0); }
            while (merged.Count > Cap)
            {
                var weakest = merged.Where(m => !m.Core).MinBy(m => Salience(m, nowMin));
                merged.Remove(weakest);
            }

            list.Clear();
            list.AddRange(merged);
        }
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        foreach (var (person, list) in _byPerson)
        {
            BitConverter.TryWriteBytes(b, person); h.Append(b);
            foreach (var m in list)
            {
                BitConverter.TryWriteBytes(b, (long)m.Kind); h.Append(b);
                BitConverter.TryWriteBytes(b, m.Actor); h.Append(b);
                BitConverter.TryWriteBytes(b, m.Target); h.Append(b);
                BitConverter.TryWriteBytes(b, m.TimeMin); h.Append(b);
                BitConverter.TryWriteBytes(b, m.SalienceTimeMin); h.Append(b);
                BitConverter.TryWriteBytes(b, ((long)m.Valence << 32) | ((long)m.S0 << 16) | ((long)m.Flags << 8)); h.Append(b);
                BitConverter.TryWriteBytes(b, (long)m.Count); h.Append(b);
            }
        }
    }

    public struct Row
    {
        public ulong Owner;
        public MemoryRecord Memory;
    }

    internal Row[] Export() => [.. _byPerson.SelectMany(kv => kv.Value.Select(m => new Row { Owner = kv.Key, Memory = m }))];

    internal void Import(Row[] rows)
    {
        _byPerson.Clear();
        foreach (var r in rows)
        {
            if (!_byPerson.TryGetValue(r.Owner, out var list)) { _byPerson[r.Owner] = list = []; }
            list.Add(r.Memory);
        }
    }
}
