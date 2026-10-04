using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// M1-06: NPC↔NPC interactions as the policy (16 §5.1–5.4). Every game quarter-hour, each awake person in a social
/// setting may start an interaction with probability I/64 (I = 2 + Sociability/16, +1 if lonely, −Grief/30; half rate
/// while working): partner by 16 §5.3 weights, type by softmax at temperature 0.5 + Volatility/100, then the type's
/// success test and effects (modifiers, familiarity, emotions, memories). ≤ 4 interactions per pair per day.
/// Gossip and Warn need claims (M1-07); Insult enters the escalation ladder in M1-08 (here: modifier + anger).
/// </summary>
public sealed class InteractionSystem : ISimSystem
{
    public enum Kind : byte { Chat, Joke, Praise, Comfort, Request, Argue, Insult, Apologize, Gossip, Warn }

    private static readonly Kind[] Kinds = Enum.GetValues<Kind>();
    private static readonly string[] Forgivable = ["opinion.insulted_me", "opinion.argued_with_me", "opinion.mocked_me", "opinion.rude_to_me"];
    private ContentDatabase? _cachedFor;
    private int _gossip = -1, _hotTempered = -1, _stubborn = -1, _charitable = -1, _honest = -1, _vengeful = -1, _healing = -1, _persuasion = -1;

    /// <summary>Counts per kind (metrics: 16 §5.6 frequency targets). Not state.</summary>
    public long[] Counts { get; } = new long[Kinds.Length];

    /// <summary>Diagnostics (not state): eligible quarter-hours, initiation draws that passed, and of those with no one in range / no type.</summary>
    public long Eligible, Initiated, NoPartner, NoKind;

    /// <summary>
    /// Per-quarter draw is I / BudgetQuarters. 64 waking quarters would realize only ~0.5·I (work halves the rate and
    /// ~28 % of draws find no one in range), so this is calibrated by the camp sweep to realize ≈ I (16 §5.1).
    /// </summary>
    public const float BudgetQuarters = 32f;

    // The topic chosen with the type (not state: set and used within one initiation).
    private Belief? _warning;

    private static readonly Func<SimWorld, int, int, bool> Range = static (w, x, y) => InRange(w, x, y);

    public string Name => "Interactions";
    public SimPhase Phase => SimPhase.World;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var people = world.People;
        if (world.Camp.Active == 0 || people.Count < 2) { return; }
        var prevQuarter = (ctx.GameMs - ctx.DtGameMs) / 60_000 / 15;
        var quarter = ctx.GameMs / 60_000 / 15;
        if (quarter == prevQuarter) { return; }
        if (!ReferenceEquals(_cachedFor, world.Content))
        {
            _cachedFor = world.Content;
            (_hotTempered, _stubborn, _charitable, _honest) = (world.Content.TraitHandle("trait.hot_tempered"), world.Content.TraitHandle("trait.stubborn"),
                world.Content.TraitHandle("trait.charitable"), world.Content.TraitHandle("trait.honest"));
            _gossip = world.Content.TraitHandle("trait.gossip");
            (_vengeful, _healing, _persuasion) = (world.Content.TraitHandle("trait.vengeful"), world.Content.SkillHandle("skill.healing"), world.Content.SkillHandle("skill.persuasion"));
        }

        Span<int> partners = stackalloc int[people.Count];
        Span<float> w = stackalloc float[people.Count];
        for (var i = 0; i < people.Count; i++)
        {
            ref readonly var act = ref people.Activity[i];
            if (act.Action < 0 || act.Has(ActivityState.Asleep)) { continue; }   // walking counts: speaking range below
            ref readonly var p = ref people.Personality[i];
            ref readonly var e = ref people.Emotions[i];
            var budget = 2f + (p.Sociability / 16f) + (people.Needs[i].Social < 40f ? 1f : 0f) - (e.Grief / 30f);
            Eligible++;
            var chance = MathF.Max(0f, budget) / BudgetQuarters * (act.Has(ActivityState.Purposeful) ? 0.5f : 1f);
            var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Social, people.Ids[i].Value, (ulong)quarter, Salt.Interaction));
            if (!rng.Chance(chance)) { continue; }
            Initiated++;

            // Partner: co-located awake people (same action's place), 16 §5.3 weights.
            var n = 0;
            var total = 0f;
            for (var j = 0; j < people.Count; j++)
            {
                ref readonly var b = ref people.Activity[j];
                if (j == i || b.Action < 0 || b.Has(ActivityState.Asleep) || !InRange(world, i, j)) { continue; }
                if (InteractionsToday(world, i, j) >= 4) { continue; }
                var f = world.Relationships.Familiarity(people.Ids[i], people.Ids[j]);
                var op = world.Relationships.Opinion(people.Ids[i], people.Ids[j]);
                var weight = (0.2f + (f / 100f)) * (1f + (MathF.Max(0f, op) / 50f)) * (InteractionsToday(world, i, j) == 0 ? 1.3f : 1f);
                if (world.Relationships.Fear(people.Ids[i], people.Ids[j]) >= 30f || op <= -30f) { weight *= 0.2f; }
                partners[n] = j;
                w[n++] = weight;
                total += weight;
            }

            if (n == 0) { NoPartner++; continue; }
            var x = rng.NextFloat01() * total;
            var partner = partners[n - 1];
            for (var k = 0; k < n; k++) { x -= w[k]; if (x < 0f) { partner = partners[k]; break; } }

            var kind = ChooseKind(world, i, partner, ref rng);
            if (kind is { } chosen) { Resolve(world, i, partner, chosen, ref rng); }
            else { NoKind++; }
        }
    }

    private Kind? ChooseKind(SimWorld world, int i, int j, ref Rng rng)
    {
        var people = world.People;
        ref readonly var p = ref people.Personality[i];
        ref readonly var e = ref people.Emotions[i];
        ref readonly var target = ref people.Emotions[j];
        var rel = world.Relationships;
        var op = rel.Opinion(people.Ids[i], people.Ids[j]);
        Span<float> weight = stackalloc float[Kinds.Length];
        float Z(byte facet) => (facet - 50f) / 15f;
        weight[(int)Kind.Chat] = 10f * (1f + (0.3f * Z(p.Sociability))) * (1f + (e.Joy / 100f));
        weight[(int)Kind.Joke] = 3f * (p.Curiosity + p.Sociability) / 100f * (1f + (e.Joy / 100f));
        weight[(int)Kind.Praise] = op >= 20f ? 2f * (1f + (0.3f * Z(p.Warmth))) : 0f;
        var distress = MathF.Max(target.Grief, MathF.Max(target.Fear, target.Shame));
        weight[(int)Kind.Comfort] = distress >= 30f && p.Warmth >= 40 && op >= 10f ? 4f * p.Warmth / 50f : 0f;
        // Request help: camp work is social-solvable (a hand hauling, foraging, feeding the fire); urgency = the asker's fatigue, pride holds it back.
        weight[(int)Kind.Request] = people.Activity[i].Has(ActivityState.Purposeful)
            ? 2f * (1f + ((100f - people.Needs[i].Energy) / 100f)) * Math.Clamp(1f - ((p.Values.Status - 50f) / 150f), 0.5f, 1.5f) : 0f;
        weight[(int)Kind.Argue] = HasTopic(people.Personality[i].Values, people.Personality[j].Values)
            ? 2f * p.Volatility / 50f * (p.HasTrait(_stubborn) ? 2f : 1f) * (1f + (e.Anger / 50f)) * 0.15f : 0f;
        weight[(int)Kind.Insult] = op <= -20f || e.Anger >= 40f ? p.Volatility / 50f * (p.HasTrait(_hotTempered) ? 2f : 1f) * (1f + (e.Anger / 50f)) : 0f;
        weight[(int)Kind.Apologize] = OwesApology(world, i, j) ? 3f : 0f;

        // Warn: Op(listener) ≥ 40 and a held negative claim about the listener they haven't been told yet.
        _warning = op >= 40f ? WarningFor(world, i, j) : null;
        weight[(int)Kind.Warn] = _warning is not null ? 3f * (0.5f + (p.Values.Loyalty / 100f)) : 0f;

        // Softmax at temperature 0.5 + Vo/100 (volatile people pick less "sensibly"), over log-weights.
        var temperature = 0.5f + (p.Volatility / 100f);
        Span<float> soft = stackalloc float[Kinds.Length];
        var sum = 0f;
        for (var k = 0; k < Kinds.Length; k++)
        {
            soft[k] = weight[k] > 0f ? SimMath.Pow(weight[k], 1f / temperature) : 0f;
            sum += soft[k];
        }

        if (sum <= 0f) { return null; }
        var x = rng.NextFloat01() * sum;
        for (var k = 0; k < Kinds.Length; k++) { if (soft[k] > 0f) { x -= soft[k]; if (x < 0f) { return Kinds[k]; } } }
        return null;
    }

    private void Resolve(SimWorld world, int i, int j, Kind kind, ref Rng rng)
    {
        var people = world.People;
        var rel = world.Relationships;
        var mem = world.Memories;
        var now = world.Clock.GameMinute;
        EntityId a = people.Ids[i], b = people.Ids[j];
        var opBa = rel.Opinion(b, a);
        CountToday(world, i, j);
        // Gossip rides on friendly talk (16 §7.4–7.5): the initiator shares with P_share, the topic with Tell/(Tell + 0.6);
        // the responder may share back at half the base rate. A chat that carried news counts as gossip in the §5.6 mix.
        var gossiped = kind is Kind.Chat or Kind.Joke or Kind.Praise or Kind.Comfort or Kind.Request && Gossip(world, i, j, 1f, ref rng);
        if (kind is Kind.Chat or Kind.Joke or Kind.Praise or Kind.Comfort or Kind.Request) { gossiped |= Gossip(world, j, i, 0.5f, ref rng); }
        if (gossiped && kind == Kind.Chat) { kind = Kind.Gossip; }
        Counts[(int)kind]++;
        rel.Contact(a, b, 3f, social: true);
        var success = true;
        switch (kind)
        {
            case Kind.Gossip:
                goto case Kind.Chat;

            case Kind.Warn:
                // The listener learns what is said about them (16 §5.2): the claim at the warner's word, and a loyal act.
                _warning!.ToldTo.Add(b.Value);
                Rumors.Hear(world, j, i, _warning.Claim, _warning.C, _warning.Hop + 1, ref rng);
                rel.TrustEvidence(b, a, 2f);
                mem.Remember(b, MemoryKind.Warned, a, b, now, 20, 1f, 0f, 30);
                break;

            case Kind.Chat:
                success = rng.Chance(0.75f + (0.002f * opBa));
                if (success)
                {
                    var lonelyA = people.Needs[i].Social < 40f ? 1.5f : 1f;
                    var lonelyB = people.Needs[j].Social < 40f ? 1.5f : 1f;
                    rel.ApplyModifier(a, b, "opinion.chatted", lonelyA);
                    rel.ApplyModifier(b, a, "opinion.chatted", lonelyB);
                    Both(mem, a, b, MemoryKind.Chat, now, 5, 10);
                }
                else if (people.Personality[j].Volatility >= 65)
                {
                    rel.ApplyModifier(b, a, "opinion.rude_to_me");
                }

                break;

            case Kind.Joke:
                var cha = people.Attributes[i].Charisma;
                success = rng.Chance(0.5f + (0.03f * (cha - 5f)) + (0.002f * opBa));
                if (success)
                {
                    rel.ApplyModifier(a, b, "opinion.joked_together");
                    rel.ApplyModifier(b, a, "opinion.joked_together");
                    people.Emotions[i].Joy = MathF.Min(100f, people.Emotions[i].Joy + 5f);
                    people.Emotions[j].Joy = MathF.Min(100f, people.Emotions[j].Joy + 5f);
                    Both(mem, a, b, MemoryKind.Joke, now, 5, 30);
                }
                else
                {
                    rel.ApplyModifier(b, a, "opinion.rude_to_me");
                }

                break;

            case Kind.Praise:
                rel.ApplyModifier(b, a, "opinion.complimented_me", rel.Trust(b, a) < 30f ? 0.5f : 1f);
                mem.Remember(b, MemoryKind.Praise, a, b, now, 8, 1f, 0f, 30);
                break;

            case Kind.Comfort:
                success = rng.Chance(0.5f + (0.004f * opBa) + (people.SkillLevels(i)[_healing] / 400f));
                if (success)
                {
                    ref var em = ref people.Emotions[j];
                    if (em.Grief >= em.Fear && em.Grief >= em.Shame) { em.Grief = MathF.Max(0f, em.Grief - 15f); }
                    else if (em.Fear >= em.Shame) { em.Fear = MathF.Max(0f, em.Fear - 15f); }
                    else { em.Shame = MathF.Max(0f, em.Shame - 15f); }
                    rel.ApplyModifier(b, a, "opinion.comforted_me");
                    mem.Remember(b, MemoryKind.Comfort, a, b, now, 15, 1f, 15f, 60);
                }

                break;

            case Kind.Request:
                success = rng.Chance(Willingness(world, j, i));
                if (success)
                {
                    Rumors.Witness(world, "claim.helped", j, i, 1f, Range);
                    rel.ApplyModifier(a, b, "opinion.granted_my_request");
                    rel.ApplyModifier(a, b, "opinion.helped_my_work");
                    rel.Contact(a, b, 3f, social: false);
                    mem.Remember(a, MemoryKind.Help, b, a, now, 15, 1f, 0f, 50);
                }
                else
                {
                    rel.ApplyModifier(a, b, "opinion.refused_my_request");
                    mem.Remember(a, MemoryKind.Refused, b, a, now, 10, 1f, 0f, -30);
                }

                break;

            case Kind.Argue:
                rel.ApplyModifier(a, b, "opinion.argued_with_me");
                rel.ApplyModifier(b, a, "opinion.argued_with_me");
                Anger(world, i, j, 10f);
                Anger(world, j, i, 10f);
                Both(mem, a, b, MemoryKind.Argue, now, 20, 10, valence: -40);
                break;

            case Kind.Insult:
                var witnesses = CountAtPlace(world, i) - 2;
                rel.ApplyModifier(b, a, "opinion.insulted_me", isPublic: witnesses >= 3);
                Anger(world, j, i, 25f * (witnesses >= 3 ? 1.4f : 1f), honorTouched: true);
                mem.Remember(b, MemoryKind.Insult, a, b, now, 30, 1f, 25f, -60);
                Rumors.Witness(world, "claim.insulted", i, j, 1f, Range);
                break;

            case Kind.Apologize:
                var pAccept = Math.Clamp(0.40f + (0.005f * opBa) + (0.003f * (people.Personality[j].Warmth - 50f))
                    - (people.Personality[j].HasTrait(_stubborn) ? 0.20f : 0f) - (people.Personality[j].HasTrait(_vengeful) ? 0.25f : 0f)
                    + (0.15f * 0.57f * (0.5f + (0.5f * people.SkillLevels(i)[_persuasion] / 100f)) * Math.Clamp(people.Emotions[i].Shame / 100f, -0.5f, 1f)),
                    0.05f, 0.95f);
                success = rng.Chance(pAccept);
                if (success)
                {
                    Rumors.Witness(world, "claim.made_amends", i, j, 1f, Range);
                    var keep = people.Personality[j].HasTrait(_stubborn) ? 0.75f : 0.5f;
                    foreach (var mod in Forgivable) { rel.ScaleModifier(b, a, mod, keep); }
                    people.Emotions[j].Anger = MathF.Max(0f, people.Emotions[j].Anger - 30f);
                    people.Emotions[i].Shame = MathF.Max(0f, people.Emotions[i].Shame - 20f);
                    mem.Remember(b, MemoryKind.Apology, a, b, now, 15, 1f, 10f, 40);
                }

                break;
        }

        world.Emit(Salience.Trace, a, new InteractionResolved(a, b, KindNames[(int)kind], success));
    }

    /// <summary>One side of §7.5's exchange: P_share × rate, then the best topic with Tell/(Tell + 0.6). True if a claim passed.</summary>
    private bool Gossip(SimWorld world, int teller, int listener, float rate, ref Rng rng)
    {
        var (topic, tell) = Rumors.BestTellable(world, teller, listener);
        if (topic is null) { return false; }
        ref readonly var claim = ref world.Claims[topic.Claim];
        var jNov = world.Content.ClaimPredicates[claim.Predicate].Juiciness * Rumors.Nov(world, topic, claim);
        if (!rng.Chance(rate * Rumors.PShare(world, teller, jNov)) || !rng.Chance(tell / (tell + 0.6f))) { return false; }
        Rumors.Exchange(world, teller, listener, topic, ref rng);
        ref readonly var pl = ref world.People.Personality[listener];
        if (pl.HasTrait(_gossip) || pl.Sociability >= 65) { world.Relationships.ApplyModifier(world.People.Ids[listener], world.People.Ids[teller], "opinion.chatted"); }
        return true;
    }

    private static readonly string[] KindNames = [.. Enum.GetNames<Kind>().Select(n => n.ToLowerInvariant())];

    /// <summary>A held (c ≥ 0.5) negative claim about the listener that this warner hasn't passed to them yet.</summary>
    private static Belief? WarningFor(SimWorld world, int warner, int listener)
    {
        var about = world.People.Ids[listener].Value;
        foreach (var b in world.Beliefs.Span(world.People.Ids[warner]))
        {
            if (b.C < BeliefStore.Hold) { continue; }
            ref readonly var c = ref world.Claims[b.Claim];
            if (c.Subject != about || world.Content.ClaimPredicates[c.Predicate].Valence != ClaimValence.Negative || b.ToldTo.Contains(about)) { continue; }
            return b;
        }

        return null;
    }

    /// <summary>16 §5.4 without the words term (NPC↔NPC requests draw it with neutral words).</summary>
    private float Willingness(SimWorld world, int helper, int asker)
    {
        var people = world.People;
        EntityId h = people.Ids[helper], a = people.Ids[asker];
        ref readonly var p = ref people.Personality[helper];
        var w = 0.25f + (0.006f * world.Relationships.Opinion(h, a)) + (0.003f * world.Relationships.Trust(h, a)) + (0.004f * (p.Warmth - 50f))
                + (p.HasTrait(_charitable) ? 0.10f : 0f) + Reciprocity(world, h, a) - (0.004f * 1f * (100f - p.Diligence) / 50f) + (world.Relationships.Fear(h, a) / 500f);
        return Math.Clamp(w, 0.02f, 0.98f);
    }

    /// <summary>+0.05 per favor the asker did for the helper that the helper still remembers (max +0.2; 16 §5.4).</summary>
    private static float Reciprocity(SimWorld world, EntityId helper, EntityId asker)
    {
        var favors = 0;
        foreach (ref readonly var m in world.Memories.Span(helper)) { if (m.Kind == MemoryKind.Help && m.Actor == asker.Value) { favors += m.Count; } }
        return MathF.Min(0.2f, 0.05f * favors);
    }

    /// <summary>A live issue: some value differs by ≥ 30 between them (16 §5.2 Argue precondition, values-based form).</summary>
    private static bool HasTopic(in ValueBlock x, in ValueBlock y)
        => Math.Abs(x.Family - y.Family) >= 30 || Math.Abs(x.Wealth - y.Wealth) >= 30 || Math.Abs(x.Status - y.Status) >= 30
        || Math.Abs(x.Honor - y.Honor) >= 30 || Math.Abs(x.Tradition - y.Tradition) >= 30 || Math.Abs(x.Faith - y.Faith) >= 30
        || Math.Abs(x.Fairness - y.Fairness) >= 30 || Math.Abs(x.Freedom - y.Freedom) >= 30 || Math.Abs(x.Loyalty - y.Loyalty) >= 30;

    /// <summary>Own offense toward them ≤ 8 days old, and Shame ≥ 20 or a fond view of the victim (16 §5.2 Apologize).</summary>
    private static bool OwesApology(SimWorld world, int i, int j)
    {
        var people = world.People;
        var now = world.Clock.GameMinute;
        var offense = false;
        foreach (ref readonly var m in world.Memories.Span(people.Ids[j]))
        {
            if (m.Actor == people.Ids[i].Value && m.Valence <= -30 && now - m.TimeMin <= 8 * 1440) { offense = true; break; }
        }

        return offense && (people.Emotions[i].Shame >= 20f || world.Relationships.Opinion(people.Ids[i], people.Ids[j]) >= 20f);
    }

    /// <summary>21 §6.1 appraisal for anger: base × g_vol × g_trait × g_value(Honor) × g_rel, soft-saturated.</summary>
    private void Anger(SimWorld world, int self, int source, float baseMagnitude, bool honorTouched = false)
    {
        var people = world.People;
        ref readonly var p = ref people.Personality[self];
        var gVol = 1f + (0.25f * (p.Volatility - 50f) / 15f);
        var gTrait = 1f;
        for (var bits = p.Traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < world.Content.Traits.Count && world.Content.Traits[h].Effects?.EmotionGain is { } g && g.TryGetValue("anger", out var v)) { gTrait *= v; }
        }

        var gValue = honorTouched ? 0.5f + (p.Values.Honor / 100f) : 1f;
        var gRel = 1f + (0.3f * -world.Relationships.Opinion(people.Ids[self], people.Ids[source]) / 100f);
        ref var e = ref people.Emotions[self];
        var delta = baseMagnitude * gVol * gTrait * gValue * MathF.Max(0.1f, gRel);
        e.Anger = MathF.Min(100f, e.Anger + (delta * (1f - (e.Anger / 150f))));
        e.AngerTarget = people.Ids[source];
    }

    private static void Both(MemoryStore mem, EntityId a, EntityId b, MemoryKind kind, long now, int baseKind, sbyte valence, sbyte? valenceOverride = null)
    {
        mem.Remember(a, kind, b, a, now, baseKind, 1f, 0f, valenceOverride ?? valence);
        mem.Remember(b, kind, a, b, now, baseKind, 1f, 0f, valenceOverride ?? valence);
    }

    private static void Both(MemoryStore mem, EntityId a, EntityId b, MemoryKind kind, long now, int baseKind, int intensity, sbyte valence)
    {
        mem.Remember(a, kind, b, a, now, baseKind, 1f, intensity, valence);
        mem.Remember(b, kind, a, b, now, baseKind, 1f, intensity, valence);
    }

    private static bool SamePlace(SimWorld world, short a, short b)
        => a >= 0 && b >= 0 && world.Content.Actions[a].Place == world.Content.Actions[b].Place && world.Content.Actions[a].Place != PlaceKind.Home;

    /// <summary>16 §5.1 social opportunity: the same task site (fire, stores, water, woods…), or within speaking range (≤ 4 m).</summary>
    public static bool InRange(SimWorld world, int i, int j)
    {
        var people = world.People;
        if (people.Activity[i].Phase == 1 && people.Activity[j].Phase == 1 && SamePlace(world, people.Activity[i].Action, people.Activity[j].Action)) { return true; }
        ref readonly var a = ref people.Transforms[i];
        ref readonly var b = ref people.Transforms[j];
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz) <= 16f;
    }

    private static int CountAtPlace(SimWorld world, int i)
    {
        var people = world.People;
        var n = 0;
        for (var k = 0; k < people.Count; k++)
        {
            if (k == i || (people.Activity[k].Action >= 0 && !people.Activity[k].Has(ActivityState.Asleep) && InRange(world, i, k))) { n++; }
        }

        return n;
    }

    private static int InteractionsToday(SimWorld world, int i, int j)
    {
        var day = world.Clock.GameMinute / 1440;
        return world.Relationships.TryGet(world.People.Ids[i], world.People.Ids[j], out var e) && e.InteractionDay == day ? e.InteractionsToday : 0;
    }

    private static void CountToday(SimWorld world, int i, int j)
    {
        var day = world.Clock.GameMinute / 1440;
        Count(world.Relationships.GetOrCreate(world.People.Ids[i], world.People.Ids[j]), day);
        Count(world.Relationships.GetOrCreate(world.People.Ids[j], world.People.Ids[i]), day);

        static void Count(RelationshipEdge e, long day)
        {
            if (e.InteractionDay != day) { (e.InteractionDay, e.InteractionsToday) = (day, 0); }
            e.InteractionsToday++;
        }
    }
}
