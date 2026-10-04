using System.IO.Hashing;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;
using MessagePack;

namespace FeudalSim.Sim.Social;

public enum FightIntent : byte { Subdue, Wound, Kill }

/// <summary>
/// A quarrel between two people (16 §9): the current rung of the ladder, pending calm from people who stepped in, and the
/// response being decided (who answers, the provocation's severity and the pressure E fixed when its DP opened). Saved and
/// hashed. A quarrel left alone for an hour is over.
/// </summary>
[MessagePackObject]
public sealed class Confrontation
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public EntityId A { get; set; }
    [Key(2)] public EntityId B { get; set; }
    [Key(3)] public byte Rung { get; set; }
    [Key(4)] public long LastMin { get; set; }

    /// <summary>16 §9.4: subtracted from A's / B's next E (someone stepped in), then cleared.</summary>
    [Key(5)] public float CalmA { get; set; }

    [Key(6)] public float CalmB { get; set; }

    /// <summary>A bystander shouted for help: every other bystander's step_in +0.2 (16 §9.6).</summary>
    [Key(7)] public bool Called { get; set; }

    [Key(8)] public int Exchanges { get; set; }

    [Key(9)] public EntityId Responder { get; set; }
    [Key(10)] public EntityId Provoker { get; set; }
    [Key(11)] public int Severity { get; set; }
    [Key(12)] public float E { get; set; }

    /// <summary>Allies the responder called (each gets a bystander DP with <c>called</c> set).</summary>
    [Key(13)] public List<ulong> CalledAllies { get; set; } = [];
}

/// <summary>Open quarrels by id. State: saved (<c>confrontations</c>) and hashed.</summary>
public sealed class ConfrontationStore
{
    private readonly SortedDictionary<ulong, Confrontation> _open = [];
    private ulong _lastId;

    public int Count => _open.Count;

    public IEnumerable<Confrontation> Open => _open.Values;

    public Confrontation? Get(ulong id) => _open.TryGetValue(id, out var c) ? c : null;

    public Confrontation? Between(EntityId x, EntityId y)
    {
        var (a, b) = x.Value < y.Value ? (x, y) : (y, x);
        foreach (var c in _open.Values) { if (c.A == a && c.B == b) { return c; } }
        return null;
    }

    internal Confrontation GetOrCreate(EntityId x, EntityId y, long nowMin)
    {
        if (Between(x, y) is { } c) { return c; }
        var (a, b) = x.Value < y.Value ? (x, y) : (y, x);
        c = new Confrontation { Id = ++_lastId, A = a, B = b, LastMin = nowMin };
        _open.Add(c.Id, c);
        return c;
    }

    internal bool Remove(ulong id) => _open.Remove(id);

    /// <summary>Quarrels idle for over an hour are over (rung back to calm).</summary>
    internal void Expire(long nowMin)
    {
        List<ulong>? stale = null;
        foreach (var c in _open.Values) { if (nowMin - c.LastMin > Escalation.QuietAfterMin) { (stale ??= []).Add(c.Id); } }
        if (stale is not null) { foreach (var id in stale) { _open.Remove(id); } }
    }

    internal (Confrontation[] Open, ulong LastId) Export() => ([.. _open.Values], _lastId);

    internal void Import(Confrontation[] open, ulong lastId)
    {
        _open.Clear();
        foreach (var c in open) { _open.Add(c.Id, c); }
        _lastId = lastId;
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        void Put(long v, Span<byte> buf) { BitConverter.TryWriteBytes(buf, v); h.Append(buf); }
        Put((long)_lastId, b);
        foreach (var c in _open.Values)
        {
            Put((long)c.Id, b); Put((long)c.A.Value, b); Put((long)c.B.Value, b); Put(c.Rung, b); Put(c.LastMin, b);
            Put(BitConverter.SingleToInt32Bits(c.CalmA), b); Put(BitConverter.SingleToInt32Bits(c.CalmB), b); Put(c.Called ? 1 : 0, b);
            Put(c.Exchanges, b); Put((long)c.Responder.Value, b); Put((long)c.Provoker.Value, b); Put(c.Severity, b);
            Put(BitConverter.SingleToInt32Bits(c.E), b);
            foreach (var ally in c.CalledAllies) { Put((long)ally, b); }
        }
    }
}

/// <summary>Inputs to 16 §9.2's escalation pressure for responder R provoked by P.</summary>
public readonly record struct PressureInputs(
    int Severity, float Anger, float Volatility, bool HotTempered, float Honor, bool IsPublic, int Drunk, int Witnesses,
    float FearOfProvoker, float OpinionOfProvoker, bool Brave, bool Coward, bool Outranked, bool AuthorityPresent, bool ChildOrElder);

/// <summary>
/// 16 §9 provocation and escalation: pressure E, the response menu's base propensities (the policy, §9.6) and the loop
/// through the ladder. The player's provocations in conversation open the response DP for the LLM; NPC↔NPC quarrels run
/// the same menu with the policy deciding, each response being a provocation back (retort s 2, threaten s 3, shove s 5).
/// M1 runs the ladder through rung 5: rung 6 needs a weapon and rung 7 lethal context (M2, 18), and the brawl is a
/// placeholder (<see cref="BrawlStub"/>).
/// </summary>
public static class Escalation
{
    /// <summary>Rung thresholds θ_0…θ_7 (16 §9.1).</summary>
    public static ReadOnlySpan<float> Theta => [0f, 10f, 25f, 45f, 60f, 72f, 88f, 100f];

    public const int Rungs = 8;

    /// <summary>The highest rung M1 can reach (brawl); 6–7 are gated until weapons and lethal intent exist.</summary>
    public const int MaxRungM1 = 5;

    public const long QuietAfterMin = 60;

    /// <summary>Safety stop for NPC↔NPC loops inside one tick (each exchange is a provocation back).</summary>
    public const int MaxExchangesPerQuarrel = 12;

    public const float RaisedVoicesM = 25f;   // canon §10.6: bystanders within 25 m hear a quarrel at rung ≥ 2

    /// <summary>16 §9.2: <c>ΔAnger = 8·s·(0.75 + Vo/200)·(1.5 if Hot-tempered)</c>, applied with 21 §6.1 saturation.</summary>
    public static float AngerGain(int severity, float volatility, bool hotTempered)
        => 8f * severity * (0.75f + (volatility / 200f)) * (hotTempered ? 1.5f : 1f);

    /// <summary>16 §9.2 pressure E (without the noise ε).</summary>
    public static float Pressure(in PressureInputs x)
        => (8f * x.Severity) + (0.3f * x.Anger) + (0.3f * (x.Volatility - 50f)) + (x.HotTempered ? 10f : 0f) + (x.IsPublic ? 0.1f * x.Honor : 0f)
         + (5f * x.Drunk) + (1.5f * Math.Min(x.Witnesses, 5) * x.Honor / 100f) - (0.4f * x.FearOfProvoker)
         - (0.15f * MathF.Max(0f, x.OpinionOfProvoker)) + (0.2f * MathF.Max(0f, -x.OpinionOfProvoker))
         + (x.Brave ? 10f : 0f) - (x.Coward ? 20f : 0f) - (x.Outranked ? 15f : 0f) - (x.AuthorityPresent ? 10f : 0f) - (x.ChildOrElder ? 15f : 0f);

    /// <summary>Noise sd: 4 + Vo/10.</summary>
    public static float NoiseSd(float volatility) => 4f + (volatility / 10f);

    /// <summary>Response cap: current + 2, or + 3 if Hot-tempered, drunk ≥ 2 or s ≥ 4 (16 §9.2).</summary>
    public static int Cap(int current, bool hotTempered, int drunk, int severity) => current + (hotTempered || drunk >= 2 || severity >= 4 ? 3 : 2);

    /// <summary>
    /// π(r) = P(θ_r ≤ E + ε &lt; θ_{r+1}), with mass above <paramref name="maxEligible"/> collapsed onto it (16 §9.6).
    /// </summary>
    public static float[] RungMass(float e, float sd, int maxEligible)
    {
        var p = new float[Rungs];
        var theta = Theta;
        for (var r = 0; r < Rungs; r++)
        {
            var lo = r == 0 ? 0f : Phi((theta[r] - e) / sd);
            var hi = r == Rungs - 1 ? 1f : Phi((theta[r + 1] - e) / sd);
            p[r] = MathF.Max(0f, hi - lo);
        }

        for (var r = Rungs - 1; r > maxEligible; r--) { p[maxEligible] += p[r]; p[r] = 0f; }
        return p;
    }

    /// <summary>Standard normal CDF (Abramowitz–Stegun 7.1.26 via erf; |error| &lt; 1.5e-7). Deterministic float math.</summary>
    public static float Phi(float z)
    {
        var x = MathF.Abs(z) / MathF.Sqrt(2f);
        var t = 1f / (1f + (0.3275911f * x));
        var y = 1f - ((((((1.061405429f * t) - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * SimMath.Exp(-x * x);
        return z >= 0f ? 0.5f * (1f + y) : 0.5f * (1f - y);
    }

    /// <summary>The menu options and their rungs after (16 §9.6).</summary>
    public static readonly (string Id, int Rung)[] Responses =
    [
        ("laugh_off", 1), ("retort", 2), ("threaten", 3), ("shove", 4), ("attack_brawl", 5), ("attack_armed", 6), ("attack_to_kill", 7),
    ];

    /// <summary>
    /// 16 §9.6 base propensities. <paramref name="callShare"/> is c (0 if no one is callable); <paramref name="apologyHolds"/>
    /// gives deescalate half of π(0) (else 0.2). Returns option id → (eligible, p).
    /// </summary>
    public static SortedDictionary<string, (bool Eligible, float P)> ResponseMenu(float e, float sd, int current, int cap, int severity,
        bool canCall, float callShare, bool apologyHolds)
    {
        var maxRung = Math.Min(Math.Min(cap, MaxRungM1), Rungs - 1);
        var mass = RungMass(e, sd, Math.Max(maxRung, 0));
        var menu = new SortedDictionary<string, (bool, float)>(StringComparer.Ordinal);
        var eligible = new bool[Rungs];
        eligible[1] = current <= 2 && severity <= 4;   // laugh_off
        eligible[2] = current <= 3;                     // retort
        for (var r = 3; r < Rungs; r++) { eligible[r] = r <= maxRung; }

        // Mass on an ineligible rung below the current one goes to walking away / backing down.
        var toZero = mass[0];
        for (var r = 1; r < Rungs; r++)
        {
            if (!eligible[r] && mass[r] > 0f) { toZero += mass[r]; mass[r] = 0f; }
        }

        // call_others takes c of the mass at rungs ≥ 3.
        var call = 0f;
        if (canCall)
        {
            for (var r = 3; r < Rungs; r++) { var take = mass[r] * callShare; call += take; mass[r] -= take; }
        }

        var deShare = apologyHolds ? 0.5f : 0.2f;
        var deEligible = current >= 1 || apologyHolds;
        menu["walk_away"] = (true, toZero * (deEligible ? 1f - deShare : 1f));
        menu["deescalate"] = (deEligible, deEligible ? toZero * deShare : 0f);
        menu["call_others"] = (canCall, call);
        foreach (var (id, rung) in Responses) { menu[id] = (eligible[rung], eligible[rung] ? mass[rung] : 0f); }
        return menu;
    }

    /// <summary>
    /// P provokes R with severity s (1 rude … 5 a blow): R's anger rises (§9.2, 21 §6.1 saturation), E is fixed, and R's
    /// response DP opens. The caller has already committed the act itself (its opinion modifier, memory, claim).
    /// </summary>
    public static ulong Provoke(SimWorld world, int provoker, int responder, int severity, DeciderKind decider, int deadlineSteps)
    {
        var people = world.People;
        var now = world.Clock.GameMinute;
        EntityId p = people.Ids[provoker], r = people.Ids[responder];
        var conf = world.Confrontations.GetOrCreate(p, r, now);
        if (now - conf.LastMin > QuietAfterMin) { (conf.Rung, conf.CalmA, conf.CalmB, conf.Called, conf.Exchanges) = (0, 0f, 0f, false, 0); conf.CalledAllies.Clear(); }
        conf.LastMin = now;
        conf.Exchanges++;

        ref readonly var pr = ref people.Personality[responder];
        var hot = pr.HasTrait(world.Content.TraitHandle("trait.hot_tempered"));
        ref var em = ref people.Emotions[responder];
        var delta = AngerGain(severity, pr.Volatility, hot);
        em.Anger = MathF.Min(100f, em.Anger + (delta * (1f - (em.Anger / 150f))));
        em.AngerTarget = p;

        var witnesses = Witnesses(world, provoker, responder, 10f);
        var inputs = new PressureInputs(
            severity, em.Anger, pr.Volatility, hot, pr.Values.Honor, witnesses >= 3, Drunk: 0, witnesses,
            world.Relationships.Fear(r, p), world.Relationships.Opinion(r, p),
            pr.HasTrait(world.Content.TraitHandle("trait.brave")), pr.HasTrait(world.Content.TraitHandle("trait.coward")),
            Outranked: false, AuthorityPresent: false, ChildOrElder: ChildOrElder(world, responder));   // status bands, offices: 17 (M4–M5)
        var calm = r == conf.A ? conf.CalmA : conf.CalmB;
        if (r == conf.A) { conf.CalmA = 0f; } else { conf.CalmB = 0f; }
        (conf.Responder, conf.Provoker, conf.Severity, conf.E) = (r, p, severity, Pressure(inputs) - calm);
        return world.Decisions.Open(EscalationOwner.Id, new DpContext(EscalationOwner.Kind, r, p, (long)conf.Id), decider, deadlineSteps);
    }

    /// <summary>Awake people other than the two within <paramref name="rangeM"/> of the responder (the audience).</summary>
    public static int Witnesses(SimWorld world, int a, int b, float rangeM)
    {
        var people = world.People;
        var n = 0;
        ref readonly var at = ref people.Transforms[b];
        for (var k = 0; k < people.Count; k++)
        {
            if (k == a || k == b || people.Activity[k].Has(ActivityState.Asleep) || (people.Activity[k].Action < 0 && !world.IsPlayer(k))) { continue; }
            float dx = people.Transforms[k].X - at.X, dz = people.Transforms[k].Z - at.Z;
            if ((dx * dx) + (dz * dz) <= rangeM * rangeM) { n++; }
        }

        return n;
    }

    public static bool Within(SimWorld world, int a, int b, float rangeM)
    {
        ref readonly var x = ref world.People.Transforms[a];
        ref readonly var y = ref world.People.Transforms[b];
        float dx = x.X - y.X, dz = x.Z - y.Z;
        return (dx * dx) + (dz * dz) <= rangeM * rangeM;
    }

    private static bool ChildOrElder(SimWorld world, int row)
    {
        var age = (world.Clock.GameMinute - world.People.Core[row].BirthGameMinute) / Time.GameDate.MinutesPerYear;
        return age < 14 || age >= 65;
    }

    /// <summary>The severity of a provocation by catalog act (16 §9.6 defaults: rude 1 · mocking 2 · insult 3 · kin/faith/honor or accusation 4 · a blow 5).</summary>
    public static int DefaultSeverity(string act) => act switch
    {
        "insult" => 3,
        "threaten" => 3,
        _ => 0,
    };

    /// <summary>Opinion modifier for a spoken provocation of severity s (16 §4.5).</summary>
    public static string ModifierFor(int severity) => severity switch
    {
        <= 1 => "opinion.rude_to_me",
        2 => "opinion.mocked_me",
        3 => "opinion.insulted_me",
        4 => "opinion.insulted_my_kin_or_faith",
        _ => "opinion.humiliated_me",
    };

    internal static void Emit(SimWorld world, Confrontation c, int rung, FightIntent intent, int witnesses)
        => world.Emit(Salience.Notable, c.Responder, new ConfrontationEscalated(c.Responder, c.Provoker, (byte)rung, intent, witnesses));
}
