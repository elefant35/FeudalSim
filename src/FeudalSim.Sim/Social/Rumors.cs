using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Time;

namespace FeudalSim.Sim.Social;

/// <summary>The four options of the being-told decision point (16 §7.10).</summary>
public enum ToldOption : byte { Doubt, Believe, Repeat, KeepQuiet }

/// <summary>Base propensities of the being-told DP (16 §7.10); entries are 0 when ineligible.</summary>
public readonly record struct ToldMasses(float Doubt, float Believe, float Repeat, float KeepQuiet)
{
    public float Take => Believe + Repeat + KeepQuiet;
}

/// <summary>
/// Claims, beliefs and gossip (16 §7): witnessing, the §7.2 belief update, §7.4 tellability, §7.6 mutation and
/// the being-told policy (§7.10, G = 0 between NPCs). Pure functions of world state plus the caller's RNG.
/// Reputation terms (plaus via R, J_eff via Renown) are neutral until M1-07b.
/// </summary>
public static class Rumors
{
    private sealed record Handles(ContentDatabase For, int Gossip, int Discreet, int Honest, int Paranoid, int Priest, int Heretic, int Drunk);

    // One reference, swapped atomically: parallel sweeps share it across worlds.
    private static Handles? _cache;

    private static Handles H(ContentDatabase content)
    {
        var cache = Volatile.Read(ref _cache);
        if (cache is not null && ReferenceEquals(cache.For, content)) { return cache; }
        cache = new Handles(content, content.TraitHandle("trait.gossip"), content.TraitHandle("trait.discreet"), content.TraitHandle("trait.honest"),
            content.TraitHandle("trait.paranoid"), content.ProfessionHandle("profession.priest"), content.ClaimHandle("claim.heretic"), content.ClaimHandle("claim.drunk"));
        Volatile.Write(ref _cache, cache);
        return cache;
    }

    // ---- witnessing ----------------------------------------------------------------------------------------

    /// <summary>
    /// An event happened: the sim interns the true claim; the people it involves and everyone awake in range get a
    /// first-hand belief at identification × clarity (involved 1.0, bystanders 0.9; 16 §7.2, §10.1).
    /// </summary>
    public static int Witness(SimWorld world, string predicate, int actorRow, int objRow, float magnitude, Func<SimWorld, int, int, bool> inRange)
    {
        var people = world.People;
        var now = world.Clock.GameMinute;
        var p = world.Content.ClaimHandle(predicate);
        if (p < 0) { return -1; }
        var id = world.Claims.Observe(p, people.Ids[actorRow], objRow >= 0 ? people.Ids[objRow] : EntityId.None, magnitude, now);
        for (var k = 0; k < people.Count; k++)
        {
            var involved = k == actorRow || k == objRow;
            if (!involved && (people.Activity[k].Action < 0 || people.Activity[k].Has(World.ActivityState.Asleep) || !inRange(world, actorRow, k))) { continue; }
            var b = world.Beliefs.GetOrCreate(people.Ids[k], id, now);
            var c = involved ? 1.0f : 0.9f;
            if (c > b.C) { (b.C, b.FirstHandC) = (c, c); }
            b.FirstHand = true;
            b.NovSinceMin = now;
        }

        return id;
    }

    /// <summary>Seeds a true claim held first-hand (c 0.9) by the given witnesses — the 16 §7.8 propagation probe.</summary>
    public static int SeedWitnessed(SimWorld world, string predicate, int subjectRow, ReadOnlySpan<int> witnessRows)
    {
        var now = world.Clock.GameMinute;
        var id = world.Claims.Observe(world.Content.ClaimHandle(predicate), world.People.Ids[subjectRow], EntityId.None, 1f, now);
        foreach (var w in witnessRows)
        {
            var b = world.Beliefs.GetOrCreate(world.People.Ids[w], id, now);
            (b.C, b.FirstHandC, b.FirstHand) = (0.9f, 0.9f, true);
        }

        return id;
    }

    // ---- 16 §7.2 belief update ---------------------------------------------------------------------------

    /// <summary><c>cred = (0.15 + 0.75·T/100) · 0.9^hop · plaus · bias · speaker_factor</c>.</summary>
    public static float Credibility(SimWorld world, int listener, int teller, in Claim claim, int hop)
    {
        var people = world.People;
        var rel = world.Relationships;
        var h = H(world.Content);
        var def = world.Content.ClaimPredicates[claim.Predicate];
        EntityId l = people.Ids[listener], s = people.Ids[teller], subject = new(claim.Subject);
        var cred = (0.15f + (0.75f * rel.Trust(l, s) / 100f)) * MathF.Pow(0.9f, hop);
        const float plaus = 1f;   // 16 §7.2 plaus needs R_full (M1-07b)
        var bias = 1f;
        var op = subject == l ? 0f : rel.Opinion(l, subject);
        if (def.Valence == ClaimValence.Negative)
        {
            if (op >= 50f) { bias = 0.6f; } else if (op <= -30f) { bias = 1.3f; }
            if (people.Personality[listener].HasTrait(h.Paranoid)) { bias *= 1.2f; }
        }
        else if (def.Valence == ClaimValence.Positive && op <= -30f) { bias = 0.7f; }

        if (def.Id == "claim.absurd") { bias *= 0.7f; }
        var age = (world.Clock.GameMinute - people.Core[teller].BirthGameMinute) / (float)GameDate.MinutesPerYear;
        var speaker = age < 16f ? 0.6f : age >= 60f ? 1.1f : 1f;
        if (def.Moral && people.Personality[teller].Profession == h.Priest) { speaker *= 1f + (0.3f * people.Personality[listener].Values.Faith / 100f); }
        return cred * plaus * bias * speaker;
    }

    /// <summary>Applies one hearing to a belief; first-hand beliefs lose at most 0.20 in total to hearsay.</summary>
    public static void Update(Belief b, float amount, bool supports)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        if (supports) { b.C += (1f - b.C) * amount; }
        else
        {
            b.C *= 1f - (0.8f * amount);
            if (b.FirstHand) { b.C = MathF.Max(b.C, b.FirstHandC - 0.20f); }
        }
    }

    // ---- 16 §7.4 tellability ---------------------------------------------------------------------------------

    public static float JEff(SimWorld world, in Claim claim) => world.Content.ClaimPredicates[claim.Predicate].Juiciness * (0.6f + (0.4f * Renown(world, new EntityId(claim.Subject)) / 100f));

    /// <summary>
    /// 16 §8.2 Renown over the whole world as one community: <c>100 · mean_m knows(m, B)</c>,
    /// <c>knows = min(1, F/30 + 0.5·[holds a belief about B at c ≥ 0.5])</c>. Opinion-leader weights w_m arrive with M1-07b (w = 1 here).
    /// </summary>
    public static float Renown(SimWorld world, EntityId subject)
    {
        var people = world.People;
        float sum = 0f;
        var n = 0;
        for (var m = 0; m < people.Count; m++)
        {
            var id = people.Ids[m];
            if (id == subject) { continue; }
            n++;
            var knows = world.Relationships.Familiarity(id, subject) / 30f;
            if (knows < 1f && HoldsAbout(world, id, subject)) { knows += 0.5f; }
            sum += MathF.Min(1f, knows);
        }

        return n == 0 ? 0f : 100f * sum / n;
    }

    private static bool HoldsAbout(SimWorld world, EntityId holder, EntityId subject)
    {
        foreach (var b in world.Beliefs.Span(holder)) { if (b.C >= BeliefStore.Hold && world.Claims[b.Claim].Subject == subject.Value) { return true; } }
        return false;
    }

    public static float Nov(SimWorld world, Belief b, in Claim claim)
    {
        var j = world.Content.ClaimPredicates[claim.Predicate].Juiciness;
        var ageDays = (world.Clock.GameMinute - b.NovSinceMin) / 1440f;
        return SimMath.Pow(0.5f, ageDays / (1f + (5f * j)));
    }

    /// <summary><c>P_share = 0.25 + 0.004·(Soc − 50) + 0.30·Gossip + 0.35·J·nov</c> (responder: × 0.5).</summary>
    public static float PShare(SimWorld world, int row, float jNov)
    {
        ref readonly var p = ref world.People.Personality[row];
        return Math.Clamp(0.25f + (0.004f * (p.Sociability - 50f)) + (p.HasTrait(H(world.Content).Gossip) ? 0.30f : 0f) + (0.35f * jNov), 0f, 1f);
    }

    /// <summary><c>Tell(a, b, l) = J_eff · nov · interest · c · discretion</c> (eager beliefs × 2). 0 = not tellable to l.</summary>
    public static float Tell(SimWorld world, int teller, int listener, Belief b)
    {
        if (b.C < BeliefStore.Hold) { return 0f; }
        var people = world.People;
        var now = world.Clock.GameMinute;
        var h = H(world.Content);
        ref readonly var claim = ref world.Claims[b.Claim];
        EntityId a = people.Ids[teller], l = people.Ids[listener], subject = new(claim.Subject);
        if (l == subject || b.QuietUntilMin > now) { return 0f; }
        foreach (var t in b.ToldTo) { if (t == l.Value) { return 0f; } }

        ref readonly var pa = ref people.Personality[teller];
        var def = world.Content.ClaimPredicates[claim.Predicate];
        var discretion = 1f;
        if (def.Valence == ClaimValence.Negative && pa.Values.Loyalty >= 60 && world.Relationships.TryGet(a, subject, out var mine) && (mine.Tags & RelTags.CloseFriend) != 0) { discretion *= 0.3f; }
        if (pa.HasTrait(h.Honest) && b.C < 0.7f) { discretion *= 0.5f; }
        if ((claim.Qualifiers & ClaimQualifiers.Secret) != 0 && !pa.HasTrait(h.Gossip)) { discretion *= 0.2f; }

        var tie = world.Relationships.TryGet(l, subject, out var theirs) && (theirs.Tags & (RelTags.Friend | RelTags.CloseFriend | RelTags.Enemy)) != 0 ? 1f : 0f;
        var interest = 0.5f + (0.5f * world.Relationships.Familiarity(l, subject) / 100f) + (0.5f * tie) + (people.Personality[listener].HasTrait(h.Gossip) ? 0.3f : 0f);
        var eager = b.EagerUntilMin > now ? 2f : 1f;
        return JEff(world, claim) * Nov(world, b, claim) * interest * b.C * discretion * eager;
    }

    /// <summary>The teller's best topic for this listener (argmax Tell; ties go to the earlier belief — the policy's order).</summary>
    public static (Belief? Belief, float Tell) BestTellable(SimWorld world, int teller, int listener)
    {
        Belief? best = null;
        var bestTell = 0f;
        foreach (var b in world.Beliefs.Span(world.People.Ids[teller]))
        {
            var t = Tell(world, teller, listener, b);
            if (t > bestTell) { (best, bestTell) = (b, t); }
        }

        return (best, bestTell);
    }

    // ---- 16 §7.10 being told (policy) -------------------------------------------------------------------------

    /// <summary>
    /// <c>p_take = clamp(cred + G·Margin, 0.05, 0.95)</c>; doubt the rest; take split into repeat ρ, keep_quiet q and
    /// believe. Ineligible options' mass goes to believe. With G = 0, E[Δc] = p_take·(1 − c)·cS — §7.2's update.
    /// </summary>
    public static ToldMasses BeingToldMasses(float cred, float g, float margin, float pShareListener, float jEff, bool discreet, bool secret,
        bool loyalAboutOwn, bool canRepeat, bool canKeepQuiet)
    {
        var take = Math.Clamp(cred + (g * margin), 0.05f, 0.95f);
        var rho = canRepeat ? Math.Clamp(2f * pShareListener * jEff, 0f, 0.7f) : 0f;
        var q = canKeepQuiet ? Math.Clamp(0.1f + (discreet ? 0.3f : 0f) + (secret ? 0.3f : 0f) + (loyalAboutOwn ? 0.4f : 0f), 0f, 0.7f) : 0f;
        if (rho + q > 1f) { (rho, q) = (rho / (rho + q), q / (rho + q)); }
        return new ToldMasses(1f - take, take * (1f - rho - q), take * rho, take * q);
    }

    // ---- 16 §7.5 exchange -----------------------------------------------------------------------------------

    /// <summary>
    /// One gossip exchange: optional mutation (§7.6), the being-told policy draw, telling state, the listener's Told
    /// memory and a <see cref="GossipExchanged"/> event. Returns the option the listener took.
    /// </summary>
    public static ToldOption Exchange(SimWorld world, int teller, int listener, Belief told, ref Rng rng)
    {
        var people = world.People;
        var now = world.Clock.GameMinute;
        var h = H(world.Content);
        EntityId a = people.Ids[teller], l = people.Ids[listener];
        var claimId = told.Claim;
        var cS = told.C;
        var mutated = false;
        var secondary = -1;
        if (rng.Chance(PMut(world, teller, told)))
        {
            (claimId, cS, secondary) = Mutate(world, teller, listener, told, ref rng);
            mutated = claimId != told.Claim;
        }

        told.ToldTo.Add(l.Value);
        var option = Hear(world, listener, teller, claimId, cS, told.Hop + 1, ref rng);
        if (secondary >= 0) { Hear(world, listener, teller, secondary, cS * 0.8f, told.Hop + 1, ref rng); }

        ref readonly var claim = ref world.Claims[claimId];
        var j = world.Content.ClaimPredicates[claim.Predicate].Juiciness;
        world.Memories.Remember(l, MemoryKind.Told, a, new EntityId(claim.Subject), now, (int)(8 + (40 * j)), 1f, 0f, 0);
        world.Emit(Salience.Trace, a, new GossipExchanged(a, l, claimId, world.Claims.Root(claimId), mutated, option));
        return option;
    }

    /// <summary>The listener hears a claim and the policy draws the being-told option (16 §7.10).</summary>
    public static ToldOption Hear(SimWorld world, int listener, int teller, int claimId, float cS, int hop, ref Rng rng)
    {
        var people = world.People;
        var now = world.Clock.GameMinute;
        var h = H(world.Content);
        ref readonly var claim = ref world.Claims[claimId];
        var existing = world.Beliefs.Get(people.Ids[listener], claimId);
        var c0 = existing?.C ?? 0f;
        var cAfter = c0 + ((1f - c0) * cS);
        var jEff = JEff(world, claim);
        var subject = new EntityId(claim.Subject);
        var def = world.Content.ClaimPredicates[claim.Predicate];
        ref readonly var pl = ref people.Personality[listener];
        var loyalAboutOwn = def.Valence == ClaimValence.Negative && pl.Values.Loyalty >= 60
            && world.Relationships.TryGet(people.Ids[listener], subject, out var e) && (e.Tags & RelTags.CloseFriend) != 0;
        var masses = BeingToldMasses(Credibility(world, listener, teller, claim, hop), 0f, 0f,
            PShare(world, listener, world.Content.ClaimPredicates[claim.Predicate].Juiciness), jEff, pl.HasTrait(h.Discreet), (claim.Qualifiers & ClaimQualifiers.Secret) != 0, loyalAboutOwn,   // P_share of this fresh news (nov = 1)
            canRepeat: cAfter >= BeliefStore.Hold, canKeepQuiet: cAfter >= BeliefStore.Hold);

        var x = rng.NextFloat01();
        var option = x < masses.Doubt ? ToldOption.Doubt
            : x < masses.Doubt + masses.Believe ? ToldOption.Believe
            : x < masses.Doubt + masses.Believe + masses.Repeat ? ToldOption.Repeat : ToldOption.KeepQuiet;
        if (option == ToldOption.Doubt) { return option; }

        var b = existing ?? world.Beliefs.GetOrCreate(people.Ids[listener], claimId, now);
        Update(b, cS, supports: true);
        if (existing is null) { (b.Source, b.Hop) = (people.Ids[teller].Value, (byte)Math.Min(255, hop)); }
        if (option == ToldOption.Repeat) { (b.EagerUntilMin, b.NovSinceMin) = (now + (2 * 1440), now); }
        if (option == ToldOption.KeepQuiet) { b.QuietUntilMin = now + (8 * 1440); }
        return option;
    }

    // ---- 16 §7.6 distortion ---------------------------------------------------------------------------------

    public static float PMut(SimWorld world, int teller, Belief b)
    {
        ref readonly var p = ref world.People.Personality[teller];
        var h = H(world.Content);
        ref readonly var claim = ref world.Claims[b.Claim];
        var dislikes = world.Relationships.Opinion(world.People.Ids[teller], new EntityId(claim.Subject)) <= -30f;
        return Math.Clamp(0.06f + (p.HasTrait(h.Gossip) ? 0.10f : 0f) + (0.03f * Math.Min((int)b.Hop, 4)) - (p.HasTrait(h.Honest) ? 0.05f : 0f) + (dislikes ? 0.04f : 0f), 0.01f, 0.35f);
    }

    private enum Mutation { Exaggerate, DropHedge, Escalate, ShiftSubject, AddMotive, Blur, Soften }

    private static readonly (Mutation M, float W)[] Mutations =
        [(Mutation.Exaggerate, 35), (Mutation.DropHedge, 20), (Mutation.Escalate, 15), (Mutation.ShiftSubject, 10), (Mutation.AddMotive, 10), (Mutation.Blur, 5), (Mutation.Soften, 5)];

    /// <summary>Structured mutation (§7.6): a new interned claim with DerivedFrom set. Ineligible mutations are skipped in the draw.</summary>
    private static (int Claim, float C, int Secondary) Mutate(SimWorld world, int teller, int listener, Belief b, ref Rng rng)
    {
        var people = world.People;
        var h = H(world.Content);
        var src = world.Claims[b.Claim];
        var def = world.Content.ClaimPredicates[src.Predicate];
        var opSubject = world.Relationships.Opinion(people.Ids[teller], new EntityId(src.Subject));
        var shiftTo = ShiftTarget(world, teller, listener, src.Subject);

        Span<float> w = stackalloc float[Mutations.Length];
        var total = 0f;
        for (var k = 0; k < Mutations.Length; k++)
        {
            var ok = Mutations[k].M switch
            {
                Mutation.DropHedge => (src.Qualifiers & ClaimQualifiers.Hedged) != 0 || b.C < 0.8f,
                Mutation.Escalate => def.EscalatesTo is not null,
                Mutation.ShiftSubject => shiftTo >= 0,
                Mutation.Blur => src.TimeMin >= 0,
                Mutation.Soften => opSubject >= 30f || people.Personality[teller].Warmth >= 70,
                _ => true,
            };
            w[k] = ok ? Mutations[k].W : 0f;
            total += w[k];
        }

        var x = rng.NextFloat01() * total;
        var m = Mutation.Exaggerate;
        for (var k = 0; k < Mutations.Length; k++) { if (w[k] > 0f) { x -= w[k]; if (x < 0f) { m = Mutations[k].M; break; } } }

        var c = src with { DerivedFrom = b.Claim, True = 0 };
        var conf = b.C;
        var secondary = -1;
        switch (m)
        {
            case Mutation.Exaggerate: c.Magnitude = MathF.Max(1f, src.Magnitude) * (1.5f + (1.5f * rng.NextFloat01())); break;
            case Mutation.DropHedge: c.Qualifiers &= ~ClaimQualifiers.Hedged; conf = MathF.Max(b.C, 0.8f); break;
            case Mutation.Escalate: c.Predicate = (ushort)world.Content.ClaimHandle(def.EscalatesTo!); break;
            case Mutation.ShiftSubject: c.Subject = people.Ids[shiftTo].Value; break;
            case Mutation.Blur: c.TimeMin = -1; c.Qualifiers |= ClaimQualifiers.Blurred; break;
            case Mutation.Soften: c.Magnitude = MathF.Max(1f, src.Magnitude) / 2f; break;
            case Mutation.AddMotive:
                var motive = people.Personality[teller].Values.Faith >= 60 ? h.Heretic : h.Drunk;
                if (motive >= 0 && motive != src.Predicate)
                {
                    secondary = world.Claims.Intern(new Claim { Predicate = (ushort)motive, Subject = src.Subject, Magnitude = 1f, TimeMin = -1, DerivedFrom = b.Claim, Qualifiers = ClaimQualifiers.Blurred });
                }

                return (b.Claim, conf, secondary);
        }

        return (world.Claims.Intern(c), conf, secondary);
    }

    /// <summary>Shift subject: someone the teller dislikes (Op ≤ −30), else a same-profession person; never the listener or teller.</summary>
    private static int ShiftTarget(SimWorld world, int teller, int listener, ulong subject)
    {
        var people = world.People;
        int disliked = -1, colleague = -1;
        var subjectRow = -1;
        for (var k = 0; k < people.Count; k++) { if (people.Ids[k].Value == subject) { subjectRow = k; break; } }
        for (var k = 0; k < people.Count; k++)
        {
            if (k == teller || k == listener || k == subjectRow) { continue; }
            if (disliked < 0 && world.Relationships.Opinion(people.Ids[teller], people.Ids[k]) <= -30f) { disliked = k; }
            if (colleague < 0 && subjectRow >= 0 && people.Personality[k].Profession == people.Personality[subjectRow].Profession) { colleague = k; }
        }

        return disliked >= 0 ? disliked : colleague;
    }
}
