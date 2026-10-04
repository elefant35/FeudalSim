using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Dialogue;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-08: the escalation ladder (16 §9), the response and bystander DPs (§9.6), the placeholder brawl.</summary>
public sealed class EscalationTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"))
        with { Player = [12f, -6f], Start = "Y0 Spring 1 19:30" };

    // ---- 16 §9.3 worked example: Hobb the smith ------------------------------------------------------------------

    private static PressureInputs Hobb(float anger, int s, float op) => new(
        s, anger, Volatility: 70, HotTempered: true, Honor: 65, IsPublic: true, Drunk: 1, Witnesses: 4,
        FearOfProvoker: 0, OpinionOfProvoker: op, Brave: false, Coward: false, Outranked: false, AuthorityPresent: false, ChildOrElder: false);

    [Fact]
    public void Pressure_ReproducesHobbsInsult()
    {
        Escalation.AngerGain(3, 70, hotTempered: true).ShouldBe(39.6f, 0.01f);
        Escalation.Pressure(Hobb(49.6f, 3, 5f)).ShouldBe(69.53f, 0.01f);   // 16: "E = 69.5"
        Escalation.NoiseSd(70).ShouldBe(11f);
        Escalation.Cap(0, hotTempered: true, drunk: 1, severity: 3).ShouldBe(3);
    }

    [Theory]
    // Step 1: from Calm, cap 3 (Hot-tempered); his friends are present (c = 0.10).
    [InlineData(69.53f, 0, 3, 3, new[] { "threaten", "call_others", "retort" }, new[] { 0.89f, 0.10f, 0.013f })]
    // Step 3: "and your father was a fraud" (s 4) at rung 3: cap 6, but rung 6 needs a weapon → brawl is the ceiling.
    [InlineData(91.7f, 3, 6, 4, new[] { "attack_brawl", "call_others", "shove", "threaten" }, new[] { 0.87f, 0.10f, 0.03f, 0.002f })]
    // Step 4: had Gerd stepped in, E falls by 16.
    [InlineData(75.7f, 3, 6, 4, new[] { "attack_brawl", "shove", "call_others", "threaten" }, new[] { 0.57f, 0.26f, 0.10f, 0.07f })]
    public void ResponseMenu_ReproducesTheWorkedExample(float e, int current, int cap, int s, string[] ids, float[] expected)
    {
        var menu = Escalation.ResponseMenu(e, 11f, current, cap, s, canCall: true, callShare: 0.10f, apologyHolds: false);
        menu.Values.Sum(v => v.P).ShouldBe(1f, 0.002f);
        for (var k = 0; k < ids.Length; k++) { menu[ids[k]].P.ShouldBe(expected[k], 0.006f, ids[k]); }
        menu["attack_armed"].Eligible.ShouldBeFalse();   // no weapon at hand (and rung 6–7 wait for M2)
    }

    [Fact]
    public void ATypicalVillager_Retorts_OrLaughsItOff()
    {
        // "A typical villager (E = 36, sd 9) would answer retort 0.89 … or laugh_off 0.11" — from Calm the cap is rung 2.
        var menu = Escalation.ResponseMenu(36f, 9f, 0, 2, 3, canCall: false, callShare: 0f, apologyHolds: false);
        menu["retort"].P.ShouldBe(0.89f, 0.01f);
        menu["laugh_off"].P.ShouldBe(0.11f, 0.01f);
        menu["threaten"].Eligible.ShouldBeFalse();   // threaten needs 3 ≤ cap: its 16 % collapses onto retort
    }

    // ---- In the camp -----------------------------------------------------------------------------------------------

    private sealed class Harness
    {
        public required SimWorld W { get; init; }
        public List<StepOutput> Outputs { get; } = [];

        public StepOutput Step()
        {
            var o = W.Step();
            ScenarioRunner.LogOpened(W, o);
            Outputs.Add(o);
            return o;
        }

        public StepOutput Submit(StateCommand c, CommandSource source = CommandSource.Player)
        {
            W.Enqueue(new CommandEnvelope(W.LastCommandSeq + 1, 0, source, c));
            return Step();
        }

        public void Steps(int n) { for (var i = 0; i < n; i++) { Step(); } }

        public IEnumerable<T> Events<T>() where T : DomainEvent => Outputs.SelectMany(o => o.Events).Select(e => e.Payload).OfType<T>();
    }

    private static (Harness H, int Npc, Conversation C) TalkTo(int npcRow = 4, Action<SimWorld, int>? prepare = null)
    {
        var h = new Harness { W = Camp.CreateWorld(Content, SerialJobScheduler.Instance) };
        h.Steps(20);
        prepare?.Invoke(h.W, npcRow);
        var t = h.W.People.Transforms[npcRow];
        h.Submit(new PlayerMoved(t.X + 1f, t.Z, 0f), CommandSource.Embodiment);
        h.Submit(new StartConversation(h.W.People.Ids[npcRow]));
        return (h, npcRow, h.W.Conversations.Of(h.W.People.Ids[npcRow])!);
    }

    private static DecisionPointOpened Insult(Harness h, Conversation c, int severity = 0)
        => h.Submit(new PlayerUtteranceClassified(c.Id, c.Turn + 1, "insult", 0.9f, 0f, "…", severity))
            .OpenedDecisions.Single(d => d.Owner == EscalationOwner.Id);

    private static void Decide(Harness h, DecisionPointOpened dp, string choice, DeciderKind decider = DeciderKind.Llm)
        => h.Submit(new DecisionMade(dp.Id, dp.MenuHash, choice, decider, "test", 500, null), CommandSource.Ai);

    private static void Hothead(SimWorld w, int row)
    {
        ref var p = ref w.People.Personality[row];
        p.Volatility = 85;
        p.Values.Honor = 75;
        p.Traits |= 1UL << w.Content.TraitHandle("trait.hot_tempered");
        p.Traits &= ~(1UL << w.Content.TraitHandle("trait.even_tempered"));
    }

    [Fact]
    public void InsultInConversation_CommitsTheAct_AndOpensTheResponseDpForTheLlm()
    {
        var (h, npc, c) = TalkTo();
        var anger0 = h.W.People.Emotions[npc].Anger;
        var dp = Insult(h, c);
        dp.MaxDecider.ShouldBe(DeciderKind.Llm);
        dp.Options.Sum(o => o.P).ShouldBe(1f, 0.002f);
        dp.Options.Select(o => o.Id).ShouldBe(["attack_armed", "attack_brawl", "attack_to_kill", "call_others", "deescalate", "laugh_off", "retort", "shove", "threaten", "walk_away"]);
        h.W.People.Emotions[npc].Anger.ShouldBeGreaterThan(anger0 + 15f);
        h.W.People.Emotions[npc].AngerTarget.ShouldBe(h.W.PlayerId);
        h.W.Relationships.Opinion(h.W.People.Ids[npc], h.W.PlayerId).ShouldBeLessThan(0f);   // insulted_me
        var w = h.W;
        var aboutPlayer = w.Beliefs.Span(w.People.Ids[npc]).ToArray().Count(b => w.Claims[b.Claim].Subject == w.PlayerId.Value);
        aboutPlayer.ShouldBeGreaterThan(0);
        c.Dps.ShouldContain(dp.Id);   // cancelled with the conversation; Deliberating while open
    }

    [Fact]
    public void InsultShoveBrawl_TheOwnersExampleEndToEnd()
    {
        var (h, npc, c) = TalkTo(prepare: Hothead);
        var first = Insult(h, c);
        first.Options.Single(o => o.Id == "threaten").P.ShouldBeGreaterThan(0.3f);
        Decide(h, first, "threaten");
        h.W.Confrontations.Between(h.W.PlayerId, h.W.People.Ids[npc])!.Rung.ShouldBe((byte)3);
        h.W.Relationships.Fear(h.W.PlayerId, h.W.People.Ids[npc]).ShouldBeGreaterThan(10f);   // threatened_me: Fear +15

        var second = Insult(h, c, severity: 4);
        var brawl = second.Options.Single(o => o.Id == "attack_brawl");
        brawl.P.ShouldBeGreaterThan(0.05f);   // above the high-stakes floor, so the LLM may pick it
        Decide(h, second, "attack_brawl");
        var escalated = h.Events<ConfrontationEscalated>().Last();
        (escalated.Rung, escalated.Intent).ShouldBe(((byte)5, FightIntent.Subdue));
        var fight = h.Events<FightResolved>().Single();
        fight.Starter.ShouldBe(h.W.People.Ids[npc]);
        h.Events<ConversationEnded>().Single().Reason.ShouldBe("fight");
        h.W.Relationships.Opinion(fight.Loser, fight.Winner).ShouldBeLessThan(-5f);   // beat_me
        h.W.Confrontations.Between(h.W.PlayerId, h.W.People.Ids[npc]).ShouldBeNull();
        var assault = h.W.Content.ClaimHandle("claim.assaulted");
        var witnesses = Enumerable.Range(0, h.W.People.Count).Count(k => h.W.Beliefs.Span(h.W.People.Ids[k]).ToArray()
            .Any(b => h.W.Claims[b.Claim].Predicate == assault && h.W.Claims[b.Claim].Subject == h.W.People.Ids[npc].Value));
        witnesses.ShouldBeGreaterThanOrEqualTo(2);   // the two of them, plus whoever was within 25 m
    }

    [Fact]
    public void ABystanderStepsIn_AndTheNextEscalationIsLessLikely()
    {
        double Brawl(bool stepIn)
        {
            var (h, npc, c) = TalkTo(prepare: Hothead);
            Decide(h, Insult(h, c), "threaten");
            var bystanders = h.Outputs.SelectMany(o => o.OpenedDecisions).Where(d => d.Owner == BystanderOwner.Id).ToList();
            bystanders.Count.ShouldBeInRange(1, BystanderOwner.FastPerExchange);   // the fast decider asks at most 3
            bystanders.ShouldAllBe(d => d.MaxDecider == DeciderKind.Fast && d.DeadlineStep - d.OpenStep == DecisionRulesEngine.FastDeadlineSteps);
            foreach (var b in bystanders)
            {
                var choice = stepIn && b.Options.Single(o => o.Id == "step_in").Eligible ? "step_in" : "ignore";
                Decide(h, b, choice, DeciderKind.Fast);
            }

            if (stepIn) { h.Events<BystanderIntervened>().ShouldNotBeEmpty(); }
            return Insult(h, c, severity: 4).Options.Single(o => o.Id == "attack_brawl").P;
        }

        var without = Brawl(stepIn: false);
        var with = Brawl(stepIn: true);
        with.ShouldBeLessThan(without - 0.05);   // 16 §9.3 step 4: Gerd stepping in takes 16 off E
    }

    [Fact]
    public void NpcQuarrels_RunTheSameLadderByPolicy_AndAlwaysSettle()
    {
        var h = new Harness { W = Camp.CreateWorld(Content, SerialJobScheduler.Instance) };
        h.Steps(20);
        var w = h.W;
        var outcomes = new Dictionary<string, int>();
        for (var pair = 0; pair < 10; pair++)
        {
            int a = pair, b = pair + 10;
            Hothead(w, b);
            Escalation.Provoke(w, a, b, 3, DeciderKind.Policy, DecisionRulesEngine.ConversationDeadlineSteps);
            var conf = w.Confrontations.Between(w.People.Ids[a], w.People.Ids[b]);
            (conf?.Exchanges ?? 0).ShouldBeLessThanOrEqualTo(Escalation.MaxExchangesPerQuarrel);
            var key = conf is null ? "settled" : $"rung {conf.Rung}";
            outcomes[key] = outcomes.GetValueOrDefault(key) + 1;
        }

        w.Decisions.OpenCount.ShouldBe(0);   // policy-only: nothing waits for a model (21 §15.3)
        h.Step();
        h.Outputs.SelectMany(o => o.OpenedDecisions).ShouldBeEmpty();
        outcomes.Sum(kv => kv.Value).ShouldBe(10);
    }

    [Fact]
    public void SaveLoadMidQuarrel_ContinuesIdentically()
    {
        var (h, npc, c) = TalkTo(prepare: Hothead);
        Decide(h, Insult(h, c), "threaten");
        Insult(h, c, severity: 4);
        var image = SaveCodec.Capture(h.W);
        h.Steps(120);

        var restored = SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        restored.Confrontations.Count.ShouldBe(1);
        var r = new Harness { W = restored };
        r.Steps(120);
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(h.W));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
