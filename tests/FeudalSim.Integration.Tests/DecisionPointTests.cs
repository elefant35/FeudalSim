using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.Time;

namespace FeudalSim.Integration.Tests;

/// <summary>M0-16/17: decision-point records, guards, the policy decider and the DP watchdog (canon §13.1, 22 §6).</summary>
public sealed class DecisionPointTests
{
    private static readonly EntityId Player = new(0x0100_0000_0000_FFFFUL);

    private static MenuOption Opt(string id, float p, Stakes stakes = Stakes.Low, bool favors = false, bool eligible = true, float pCrit = 0f, params OptionParam[] ps)
        => new(id, "test", ps, eligible, p, pCrit, stakes, favors, id.Replace('_', ' '));

    /// <summary>A configurable owner; eligibility may read world state so logged commands can change it.</summary>
    private sealed class TestOwner(Func<SimWorld, MenuOption[]> menu) : IDecisionPointOwner
    {
        public const string Id = "test.owner";
        public List<string> Executed { get; } = [];
        public string OwnerId => Id;
        public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context) => menu(world);
        public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen) => Executed.Add(chosen.Id);
    }

    /// <summary>Drives a world the way SimRunner does: Seq numbering, integrity records for opened DPs, a full log.</summary>
    private sealed class Harness
    {
        public Harness(Func<SimWorld, MenuOption[]> menu, ulong seed = 7, int people = 2)
        {
            Owner = new TestOwner(menu);
            World = NewWorld(Owner, seed);
            for (var i = 0; i < people; i++) { Submit(new SpawnPerson($"P{i}", i, 0), CommandSource.Scenario); }
            Step();
        }

        public SimWorld World { get; }
        public TestOwner Owner { get; }
        public List<CommandEnvelope> Log { get; } = [];
        public List<EventEnvelope> Events { get; } = [];
        public List<DecisionPointOpened> Opened { get; } = [];
        public EntityId Chooser => World.People.Ids[0];

        public static SimWorld NewWorld(TestOwner owner, ulong seed)
        {
            var w = new SimWorld(seed);
            w.Decisions.Register(owner);
            return w;
        }

        public void Submit(StateCommand c, CommandSource source = CommandSource.Ai)
            => World.Enqueue(new CommandEnvelope(World.LastCommandSeq + 1, 0, source, c));

        public StepOutput Step()
        {
            var o = World.Step();
            Log.AddRange(o.AppliedCommands);
            Events.AddRange(o.Events);
            foreach (var dp in o.OpenedDecisions)
            {
                Opened.Add(dp);
                Submit(dp, CommandSource.Integrity);
            }

            return o;
        }

        public void Steps(int n) { for (var i = 0; i < n; i++) { Step(); } }

        public ulong Open(DeciderKind decider = DeciderKind.Llm, int deadline = 40, EntityId? counterpart = null)
            => World.Decisions.Open(TestOwner.Id, new DpContext("test.respond", Chooser, counterpart ?? Player, 0), decider, deadline);

        public DecisionResolved Resolved(ulong dp) => Events.Select(e => e.Payload).OfType<DecisionResolved>().Single(r => r.Dp == dp);

        public void Decide(ulong dp, string? choice, ulong? hash = null)
            => Submit(new DecisionMade(dp, hash ?? Opened.Single(o => o.Id == dp).MenuHash, choice, DeciderKind.Llm, "test-llm", 10, null));
    }

    private static readonly Func<SimWorld, MenuOption[]> TradeMenu = _ =>
    [
        Opt("refuse", 0.45f),
        Opt("accept_at_price", 0.30f, Stakes.High, favors: true, ps: [new("qty", 1), new("price_f", 60)]),
        Opt("counter_step_1", 0.25f, Stakes.High, favors: true, ps: [new("price_f", 52)]),
    ];

    [Fact]
    public void MenuHash_IsCanonical_AndCoversRulesButNotGloss()
    {
        var a = DecisionRulesEngine.Canonical(TradeMenu(null!));
        var shuffled = DecisionRulesEngine.Canonical([.. TradeMenu(null!).Reverse()
            .Select(o => o with { Params = [.. o.Params.Reverse()] })]);
        DecisionRulesEngine.HashMenu(shuffled).ShouldBe(DecisionRulesEngine.HashMenu(a));
        DecisionRulesEngine.HashMenu(DecisionRulesEngine.Canonical([.. a.Select(o => o with { Gloss = "x" })])).ShouldBe(DecisionRulesEngine.HashMenu(a));
        DecisionRulesEngine.HashMenu(DecisionRulesEngine.Canonical([a[0] with { P = a[0].P + 0.001f }, a[1], a[2]])).ShouldNotBe(DecisionRulesEngine.HashMenu(a));
        Should.Throw<InvalidOperationException>(() => DecisionRulesEngine.Canonical([a[0], a[0]]));
    }

    [Fact]
    public void PolicyDecider_SamplesPropensities_AndIsKeyed()
    {
        static List<string> Run()
        {
            var h = new Harness(TradeMenu);
            for (var i = 0; i < 4000; i++)
            {
                h.Open(DeciderKind.Policy);
                if (i % 50 == 49) { h.Step(); }   // vary the step as well as the ordinal
            }

            h.Step();
            h.World.Decisions.OpenCount.ShouldBe(0);   // policy-only DPs resolve inline
            return h.Owner.Executed;
        }

        var first = Run();
        first.ShouldBe(Run());   // same seed → identical picks
        ((double)first.Count(x => x == "refuse")).ShouldBeInRange(4000 * 0.42, 4000 * 0.48);
        ((double)first.Count(x => x == "accept_at_price")).ShouldBeInRange(4000 * 0.27, 4000 * 0.33);
        ((double)first.Count(x => x == "counter_step_1")).ShouldBeInRange(4000 * 0.22, 4000 * 0.28);
    }

    [Fact]
    public void Deadline_PolicyDecides_LateDecisionRejected_AndDeadlineStepStillApplies()
    {
        var g = new Harness(TradeMenu);
        var late = g.Open(deadline: 5);
        g.Steps(5);   // reaches the deadline step
        var r = g.Resolved(late);
        (r.Guard, r.Decider, r.ProviderTag).ShouldBe((GuardOutcome.Deadline, DeciderKind.Policy, "policy:deadline"));
        g.Decide(late, "refuse");
        g.Step();
        g.Events.Select(e => e.Payload).OfType<CommandRejected>().ShouldContain(c => c.Reason.Contains("Late or unknown decision"));

        var onTime = g.Open(deadline: 3);
        g.Steps(2);
        g.Decide(onTime, "counter_step_1");   // applies at step open+3 = the deadline step: still in time
        g.Step();
        var ok = g.Resolved(onTime);
        (ok.Guard, ok.Chosen, ok.Decider).ShouldBe((GuardOutcome.Passed, "counter_step_1", DeciderKind.Llm));
    }

    [Theory]
    [InlineData("fly_away", GuardOutcome.OffMenu)]
    [InlineData("insult_low", GuardOutcome.BelowFloor)]          // low stakes, p 0.01 < 0.02
    [InlineData("shove_high", GuardOutcome.BelowFloor)]          // high stakes, p 0.04 < 0.05
    [InlineData("draw_weapon", GuardOutcome.CriticalCheck)]      // critical, PCrit 0.20 < 0.25
    [InlineData("swear_fealty", GuardOutcome.Passed)]            // critical, PCrit 0.30
    [InlineData("closed_door", GuardOutcome.Ineligible)]
    [InlineData("chat", GuardOutcome.Passed)]
    public void Guards_RejectExploitsNotCharacter(string choice, GuardOutcome expected)
    {
        var h = new Harness(_ =>
        [
            Opt("chat", 0.50f), Opt("insult_low", 0.01f), Opt("shove_high", 0.04f, Stakes.High),
            Opt("draw_weapon", 0.20f, Stakes.Critical, pCrit: 0.20f), Opt("swear_fealty", 0.24f, Stakes.Critical, pCrit: 0.30f),
            Opt("closed_door", 0.01f, eligible: false),
        ]);
        var dp = h.Open();
        h.Step();
        h.Opened.Single().PreCleared.ShouldBe(["chat", "swear_fealty"], ignoreOrder: true);
        h.Decide(dp, choice);
        h.Step();
        var r = h.Resolved(dp);
        r.Guard.ShouldBe(expected);
        if (expected == GuardOutcome.Passed) { r.Chosen.ShouldBe(choice); r.Decider.ShouldBe(DeciderKind.Llm); }
        else { r.Decider.ShouldBe(DeciderKind.Policy); r.Rejected.ShouldBe(choice); r.Chosen.ShouldBe(h.Owner.Executed.Single()); }
    }

    [Fact]
    public void NullChoice_IsPolicyNow_AndWrongMenuHash_IsStale()
    {
        var h = new Harness(TradeMenu);
        var a = h.Open();
        var b = h.Open();
        h.Step();
        h.Submit(new DecisionMade(a, h.Opened[0].MenuHash, null, DeciderKind.Policy, "template", 0, null));
        h.Decide(b, "refuse", hash: 12345);
        h.Step();
        (h.Resolved(a).Guard, h.Resolved(a).ProviderTag).ShouldBe((GuardOutcome.ModePolicy, "template"));
        h.Resolved(b).Guard.ShouldBe(GuardOutcome.StaleMenu);
    }

    [Fact]
    public void LongShotBudget_TwoPlayerFavoringLongShotsPerPairPerDay()
    {
        var h = new Harness(_ => [Opt("refuse", 0.85f), Opt("give_discount", 0.10f, favors: true), Opt("insult_player", 0.05f)]);
        string Choose(string choice)
        {
            var dp = h.Open();
            h.Step();
            h.Decide(dp, choice);
            h.Step();
            return $"{h.Resolved(dp).Guard}";
        }

        Choose("give_discount").ShouldBe("Passed");
        Choose("insult_player").ShouldBe("Passed");     // a long shot against the player is not budgeted
        Choose("give_discount").ShouldBe("Passed");
        h.World.Decisions.LongShotsUsed(h.Chooser, Player).ShouldBe(2);
        Choose("give_discount").ShouldBe("LongShotBudget");
        h.Opened[^1].PreCleared.ShouldNotContain("give_discount");   // spent budget → not shown to a decider
        Choose("insult_player").ShouldBe("Passed");

        var stepsPerDay = (int)(SimClock.MsPerGameDay / h.World.Clock.GameMsPerStep);
        h.Steps(stepsPerDay);   // a new game day resets the pair's budget
        Choose("give_discount").ShouldBe("Passed");
    }

    [Fact]
    public void StaleMenu_PolicyReSamplesStillEligibleOptions_DrivenByLoggedCommands()
    {
        // "take_last_bread" is only eligible while fewer than 3 people exist; a logged SpawnPerson makes it stale.
        var h = new Harness(w => [Opt("take_last_bread", 0.90f, eligible: w.People.Count < 3), Opt("share", 0.10f)]);
        ulong dp;
        while (true)
        {
            dp = h.Open(deadline: 4);
            h.Step();
            if (h.World.Decisions.PolicyChoiceOf(dp) == "take_last_bread") { break; }
            h.World.Decisions.Cancel(dp, "test: want a DP whose policy pick goes stale");
        }

        h.Submit(new SpawnPerson("Latecomer", 5, 5), CommandSource.Scenario);
        h.Steps(4);
        var r = h.Resolved(dp);
        (r.Guard, r.Chosen).ShouldBe((GuardOutcome.Deadline, "share"));

        // A model choosing the now-ineligible option is turned down the same way.
        var dp2 = h.Open();
        h.Step();
        h.Decide(dp2, "take_last_bread");
        h.Step();
        (h.Resolved(dp2).Guard, h.Resolved(dp2).Chosen).ShouldBe((GuardOutcome.Ineligible, "share"));
    }

    [Fact]
    public void StateHash_CoversOpenDecisionPoints()
    {
        var a = new Harness(TradeMenu);
        var b = new Harness(TradeMenu);
        StateHasher.Hash(a.World).ShouldBe(StateHasher.Hash(b.World));
        a.Open();
        StateHasher.Hash(a.World).ShouldNotBe(StateHasher.Hash(b.World));
    }

    [Fact]
    public void Replay_ReproducesDecisions_AndDetectsATamperedIntegrityRecord()
    {
        var live = new Harness(TradeMenu, seed: 99);
        var ids = new List<ulong>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add(live.Open(deadline: 8));
            live.Steps(2);
            if (i % 2 == 0) { live.Decide(ids[^1], i % 4 == 0 ? "accept_at_price" : "refuse"); }   // others hit the deadline
            live.Steps(3);
        }

        live.Steps(10);
        var liveResolved = live.Events.Select(e => e.Payload).OfType<DecisionResolved>().ToList();
        liveResolved.Count.ShouldBe(6);
        liveResolved.Select(r => r.Guard).ShouldContain(GuardOutcome.Deadline);
        liveResolved.Select(r => r.Guard).ShouldContain(GuardOutcome.Passed);

        (SimWorld world, List<EventEnvelope> events) Replay(IReadOnlyList<CommandEnvelope> log)
        {
            // Replay re-opens each DP itself — here the "owning system" is the test, which opened them between steps.
            var w = Harness.NewWorld(new TestOwner(TradeMenu), 99);
            var evs = new List<EventEnvelope>();
            var opens = live.Opened.Select(o => o.OpenStep).ToHashSet();
            Replayer.Run(w, log, live.World.Clock.Step, o =>
            {
                evs.AddRange(o.Events);
                if (opens.Contains(w.Clock.Step)) { w.Decisions.Open(TestOwner.Id, new DpContext("test.respond", w.People.Ids[0], Player, 0), DeciderKind.Llm, 8); }
            });
            return (w, evs);
        }

        // The live harness opened DPs between steps (after step N), so replay opens them after the same step.
        var (replayed, replayEvents) = Replay(live.Log);
        StateHasher.Hash(replayed).ShouldBe(StateHasher.Hash(live.World));
        replayEvents.Select(e => e.Payload).OfType<DecisionResolved>().ShouldBe(liveResolved);
        replayEvents.Select(e => e.Payload).OfType<IntegrityMismatch>().ShouldBeEmpty();

        var tampered = live.Log.Select(c => c.Payload is DecisionPointOpened d && d.Id == ids[1] ? c with { Payload = d with { MenuHash = d.MenuHash ^ 1 } } : c).ToList();
        var (_, tamperedEvents) = Replay(tampered);
        tamperedEvents.Select(e => e.Payload).OfType<IntegrityMismatch>().ShouldHaveSingleItem().Reason.ShouldContain("menu hash differs");
    }

    [Fact]
    public void HeadlessDpPingScenario_PolicyDecidesAtDeadline_Deterministically()
    {
        var scenario = new ScenarioDef { Id = "scenario.dp_test", Seed = 42, Settlers = 3, Days = 1, DecisionPingStep = 20, DecisionPingDeadlineSteps = 40 };
        var content = FeudalSim.Content.ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
        var path = Path.Combine(Path.GetTempPath(), $"fs-dp-{Guid.NewGuid():N}.fslog");
        try
        {
            using var events = new MemoryStream();
            using (var log = InputLogFile.OpenOrCreate(path)) { ScenarioRunner.Run(scenario, content, 1, log, events); }
            events.Position = 0;
            var all = Sim.Persistence.LogCodec.ReadEvents(events, out _);
            var resolved = all.Select(e => e.Payload).OfType<DecisionResolved>().ShouldHaveSingleItem();
            (resolved.Guard, resolved.Decider, resolved.Owner).ShouldBe((GuardOutcome.Deadline, DeciderKind.Policy, DecisionPingOwner.Id));
            all.Single(e => e.Payload is DecisionResolved).Step.ShouldBe(60);   // opened at step 20, deadline +40
            var logged = InputLogFile.ReadAll(path);
            logged.Where(c => c.Source == CommandSource.Integrity).ShouldHaveSingleItem().Payload.ShouldBeOfType<DecisionPointOpened>();
            all.Select(e => e.Payload).OfType<IntegrityMismatch>().ShouldBeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
