using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Dialogue;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Social;

/// <summary>
/// The provoked person's response DP (16 §9.6, 18 §4.1; content <c>dp.confrontation</c>). Base propensities are §9.2's own
/// escalation probabilities at the pressure E fixed when the DP opened; options below the floor stay on the menu but the
/// guard blocks a model from picking them. Execution: the rung moves, verbal rungs apply their modifiers and count as a
/// provocation back (the loop continues by policy for NPC↔NPC quarrels; the player answers in their own turn), a shove or
/// brawl hands off to 18 (<see cref="BrawlStub"/> in M1), and at rung ≥ 2 the bystanders within 25 m decide whether to
/// step in (<see cref="BystanderOwner"/>).
/// </summary>
public sealed class EscalationOwner : IDecisionPointOwner
{
    public const string Id = "16.escalation";
    public const string Kind = "escalation.respond";
    public const string MenuId = "dp.confrontation";

    public string OwnerId => Id;

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context)
    {
        var def = world.Content.Decision(MenuId) ?? throw new InvalidOperationException($"Content has no {MenuId}.");
        var conf = world.Confrontations.Get((ulong)context.Subject);
        var people = world.People;
        var r = people.IndexOf(context.Chooser);
        var p = people.IndexOf(context.Counterpart);
        if (conf is null || r < 0 || p < 0)
        {
            return [.. def.Options.Select(o => Option(world, o, o.Id == "walk_away", o.Id == "walk_away" ? 1f : 0f, p))];
        }

        ref readonly var pr = ref people.Personality[r];
        var hot = pr.HasTrait(world.Content.TraitHandle("trait.hot_tempered"));
        var cap = Escalation.Cap(conf.Rung, hot, drunk: 0, conf.Severity);
        var coward = pr.HasTrait(world.Content.TraitHandle("trait.coward"));
        var fear = world.Relationships.Fear(context.Chooser, context.Counterpart);
        var callShare = 0.10f + (coward || fear >= 40f ? 0.30f : 0f);   // + 0.20 if P outranks R (status bands: 17)
        var canCall = Allies(world, r, p).Count > 0;
        var apology = OwesApology(world, r, p);
        var menu = Escalation.ResponseMenu(conf.E, Escalation.NoiseSd(pr.Volatility), conf.Rung, cap, conf.Severity, canCall, callShare, apology);
        return [.. def.Options.Select(o => menu.TryGetValue(o.Id, out var m) ? Option(world, o, m.Eligible, m.P, p) : Option(world, o, false, 0f, p))];
    }

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
        if (world.Confrontations.Get((ulong)dp.Context.Subject) is not { } conf) { return; }
        var people = world.People;
        var r = people.IndexOf(conf.Responder);
        var p = people.IndexOf(conf.Provoker);
        if (r < 0 || p < 0) { return; }
        var rel = world.Relationships;
        EntityId rid = conf.Responder, pid = conf.Provoker;
        var witnesses = Escalation.Witnesses(world, p, r, 10f);
        var counter = 0;   // severity of the response as a provocation back
        switch (chosen.Id)
        {
            case "laugh_off":
                conf.Rung = 1;
                people.Emotions[r].Anger = MathF.Max(0f, people.Emotions[r].Anger - 5f);
                break;

            case "retort":
                conf.Rung = 2;
                rel.ApplyModifier(rid, pid, "opinion.argued_with_me");
                rel.ApplyModifier(pid, rid, "opinion.argued_with_me");
                people.Emotions[r].Anger = MathF.Min(100f, people.Emotions[r].Anger + 10f);
                counter = 2;
                break;

            case "threaten":
                conf.Rung = 3;
                rel.ApplyModifier(pid, rid, "opinion.threatened_me", isPublic: witnesses >= 3);   // Fear +15 with it
                Rumors.Witness(world, "claim.threatened", r, p, 1f, Earshot);
                counter = 3;
                break;

            case "shove":
                conf.Rung = 4;
                Escalation.Emit(world, conf, 4, FightIntent.Subdue, witnesses);   // 18: a stagger, no damage
                rel.ApplyModifier(pid, rid, "opinion.struck_me");
                counter = 5;
                break;

            case "attack_brawl":
                conf.Rung = 5;
                Escalation.Emit(world, conf, 5, FightIntent.Subdue, witnesses);
                EndConversationsOf(world, conf, "fight");
                BrawlStub.Resolve(world, conf, starter: r, other: p);
                conf.Peak = Math.Max(conf.Peak, conf.Rung);
                world.Confrontations.Remove(conf.Id);   // the fight settles it
                Mediation.AfterQuarrel(world, conf);
                return;

            case "walk_away":
                conf.Rung = 0;
                EndConversationsOf(world, conf, "npc");
                world.Confrontations.Remove(conf.Id);
                Mediation.AfterQuarrel(world, conf);
                break;

            case "deescalate":
                conf.Rung = 0;
                people.Emotions[r].Anger = MathF.Max(0f, people.Emotions[r].Anger - 15f);
                world.Confrontations.Remove(conf.Id);
                Mediation.AfterQuarrel(world, conf);
                break;

            case "call_others":
                foreach (var ally in Allies(world, r, p).Take(3)) { conf.CalledAllies.Add(people.Ids[ally].Value); }
                break;
        }

        conf.Peak = Math.Max(conf.Peak, conf.Rung);
        if (conf.Rung >= 2 && world.Confrontations.Get(conf.Id) is not null) { BystanderOwner.OpenFor(world, conf, r, p); }

        // Each response is a provocation back (16 §9.2). With the player on the other side it is the player's turn.
        if (counter > 0 && !world.IsPlayer(p) && world.Confrontations.Get(conf.Id) is { Exchanges: < Escalation.MaxExchangesPerQuarrel })
        {
            Escalation.Provoke(world, r, p, counter, DeciderKind.Policy, DecisionRulesEngine.ConversationDeadlineSteps);
        }
    }

    internal static readonly Func<SimWorld, int, int, bool> Earshot = static (w, a, k) => Escalation.Within(w, a, k, Escalation.RaisedVoicesM);

    /// <summary>Friends of R within 25 m (Op ≥ 40 toward R; kin with 16 §11) who can be called to stand with R.</summary>
    internal static List<int> Allies(SimWorld world, int r, int p)
    {
        var people = world.People;
        var found = new List<int>();
        for (var k = 0; k < people.Count; k++)
        {
            if (k == r || k == p || world.IsPlayer(k) || !world.CanAct(k) || people.Activity[k].Has(ActivityState.Asleep) || !Escalation.Within(world, r, k, Escalation.RaisedVoicesM)) { continue; }
            if (world.Relationships.Opinion(people.Ids[k], people.Ids[r]) >= 40f) { found.Add(k); }
        }

        return found;
    }

    /// <summary>16 §5.2 Apologize conditions: R wronged P within 8 days and R feels Shame ≥ 20 or likes P.</summary>
    private static bool OwesApology(SimWorld world, int r, int p)
    {
        var people = world.People;
        var now = world.Clock.GameMinute;
        foreach (ref readonly var m in world.Memories.Span(people.Ids[p]))
        {
            if (m.Actor == people.Ids[r].Value && m.Valence <= -30 && now - m.TimeMin <= 8 * 1440)
            {
                return people.Emotions[r].Shame >= 20f || world.Relationships.Opinion(people.Ids[r], people.Ids[p]) >= 20f;
            }
        }

        return false;
    }

    private static void EndConversationsOf(SimWorld world, Confrontation conf, string reason)
    {
        foreach (var c in world.Conversations.Open.ToList())
        {
            if (c.Npc == conf.A || c.Npc == conf.B) { ConversationSystem.End(world, c, reason); }
        }
    }

    private static MenuOption Option(SimWorld world, DecisionOptionDef o, bool eligible, float p, int provoker)
        => new(o.Id, o.Family, [], eligible, p, 0f, o.Stakes, o.FavorsPlayer,
            o.Gloss.Replace("{player}", provoker >= 0 ? world.People.Names[provoker] : "them", StringComparison.Ordinal));
}

/// <summary>
/// 16 §9.4/§9.6 bystander DP (content <c>dp.bystander</c>): a witness within 25 m of a quarrel at rung ≥ 2 may step in
/// (both parties' next E −(10 + Leadership/5); a shove is pulled apart), shout for help (everyone else's step_in +0.2) or
/// watch. With the player as a party the three likeliest interveners are asked by the fast decider (0.5 s deadline); the
/// rest, and every NPC↔NPC quarrel, use the policy.
/// </summary>
public sealed class BystanderOwner : IDecisionPointOwner
{
    public const string Id = "16.bystander";
    public const string Kind = "escalation.bystander";
    public const string MenuId = "dp.bystander";
    public const int FastPerExchange = 3;

    public string OwnerId => Id;

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context)
    {
        var def = world.Content.Decision(MenuId) ?? throw new InvalidOperationException($"Content has no {MenuId}.");
        var conf = world.Confrontations.Get((ulong)context.Subject);
        var w = world.People.IndexOf(context.Chooser);
        var (step, call) = conf is null || w < 0 ? (0f, 0f) : Propensities(world, conf, w);
        var stepOk = step > 0f;
        var callOk = call > 0f;
        var ignore = MathF.Max(0f, 1f - step - call);
        return
        [
            new("call_others", "intervention", [], callOk, call, 0f, Stakes.Low, false, Gloss(def, "call_others")),
            new("ignore", "intervention", [], true, ignore, 0f, Stakes.Low, false, Gloss(def, "ignore")),
            new("step_in", "intervention", [], stepOk, step, 0f, Stakes.Medium, true, Gloss(def, "step_in")),
        ];
    }

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
        if (world.Confrontations.Get((ulong)dp.Context.Subject) is not { } conf) { return; }
        var w = world.People.IndexOf(dp.Context.Chooser);
        switch (chosen.Id)
        {
            case "step_in" when w >= 0:
                var leadership = world.Content.SkillHandle("skill.leadership");
                var calm = 10f + ((leadership >= 0 ? world.People.SkillLevels(w)[leadership] : 0) / 5f);
                conf.CalmA += calm;
                conf.CalmB += calm;
                if (conf.Rung == 4) { conf.Rung = 3; }   // pulled apart
                world.Emit(Salience.Minor, dp.Context.Chooser, new BystanderIntervened(conf.Id, dp.Context.Chooser, calm));
                break;

            case "call_others":
                conf.Called = true;
                break;
        }
    }

    /// <summary>16 §9.6 base propensities of step_in and call_others for one witness (0 when ineligible).</summary>
    public static (float Step, float Call) Propensities(SimWorld world, Confrontation conf, int w)
    {
        var people = world.People;
        ref readonly var p = ref people.Personality[w];
        var id = people.Ids[w];
        var coward = p.HasTrait(world.Content.TraitHandle("trait.coward"));
        var rel = world.Relationships;
        var opA = rel.Opinion(id, conf.A);
        var opB = rel.Opinion(id, conf.B);
        var eligible = opA >= 30f || opB >= 30f || (p.Warmth >= 65 && !coward);   // + authority or kin (17, 16 §11)
        var called = conf.CalledAllies.Contains(id.Value);
        var step = eligible ? Math.Clamp(0.1f + (0.004f * (p.Warmth - 50f)) + (called ? 0.3f : 0f) + (conf.Called ? 0.2f : 0f), 0f, 0.95f) : 0f;
        var fearful = coward || rel.Fear(id, conf.A) >= 40f || rel.Fear(id, conf.B) >= 40f;
        var call = MathF.Min(0.5f, 0.05f + (conf.Rung >= 3 ? 0.15f : 0f) + (fearful ? 0.15f : 0f) + ((p.Values.Tradition + p.Values.Fairness) / 2f >= 60f ? 0.10f : 0f));
        if (step + call > 1f) { call = 1f - step; }
        return (step, call);
    }

    /// <summary>Opens bystander DPs for one exchange at rung ≥ 2: the fast decider for up to 3 when the player is a party, else the policy.</summary>
    internal static void OpenFor(SimWorld world, Confrontation conf, int r, int p)
    {
        var people = world.People;
        var playerParty = world.IsPlayer(r) || world.IsPlayer(p);
        var candidates = new List<(int Row, float Step)>();
        for (var k = 0; k < people.Count; k++)
        {
            if (k == r || k == p || world.IsPlayer(k) || !world.CanAct(k) || people.Activity[k].Action < 0 || people.Activity[k].Has(ActivityState.Asleep)) { continue; }
            if (!Escalation.Within(world, r, k, Escalation.RaisedVoicesM) && !Escalation.Within(world, p, k, Escalation.RaisedVoicesM)) { continue; }
            candidates.Add((k, Propensities(world, conf, k).Step));
        }

        var fast = 0;
        foreach (var (row, _) in candidates.OrderByDescending(c => c.Step).ThenBy(c => c.Row))
        {
            var decider = playerParty && fast < FastPerExchange ? DeciderKind.Fast : DeciderKind.Policy;
            if (decider == DeciderKind.Fast) { fast++; }
            world.Decisions.Open(Id, new DpContext(Kind, people.Ids[row], people.Ids[r], (long)conf.Id), decider, DecisionRulesEngine.FastDeadlineSteps);
            if (world.Confrontations.Get(conf.Id) is null) { return; }
        }
    }

    private static string Gloss(DecisionDef def, string id) => def.Options.First(o => o.Id == id).Gloss;
}

/// <summary>
/// Placeholder for 18's fistfight (18 §4.2 arrives in M2): resolved at once. P(starter wins) = logistic(0.8·(power_s −
/// power_o)), power = Strength + 0.5·Endurance + 0.25·Dexterity; the loser yields; no injuries. 16's consequences apply as
/// specified (§9.5): <c>beat_me</c> held by the loser (×0.5 if they started it, with its Fear), <c>struck_me</c> held by
/// the winner, and every witness within 25 m gets a first-hand <c>assaulted</c> claim about the starter.
/// </summary>
public static class BrawlStub
{
    public static void Resolve(SimWorld world, Confrontation conf, int starter, int other)
    {
        var people = world.People;
        static float Power(in Attributes a) => a.Strength + (0.5f * a.Endurance) + (0.25f * a.Dexterity);
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Combat, conf.Id, (ulong)world.Clock.Step, Salt.Brawl));
        var pStarter = 1f / (1f + SimMath.Exp(-0.8f * (Power(people.Attributes[starter]) - Power(people.Attributes[other]))));
        var (winner, loser) = rng.Chance(pStarter) ? (starter, other) : (other, starter);
        EntityId w = people.Ids[winner], l = people.Ids[loser];
        world.Relationships.ApplyModifier(l, w, "opinion.beat_me", loser == starter ? 0.5f : 1f);
        world.Relationships.ApplyModifier(w, l, "opinion.struck_me");
        Rumors.Witness(world, "claim.assaulted", starter, other, 1f, EscalationOwner.Earshot);
        world.Emit(Salience.Notable, people.Ids[starter], new FightResolved(conf.Id, w, l, people.Ids[starter]));
    }
}
