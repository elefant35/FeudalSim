using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Dialogue;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.Time;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-09b: the request DP with the favor carried out (16 §5.4) and the being-told DP with the lie test (16 §7.10, §5.2).</summary>
public sealed class RequestAndToldTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"))
        with { Player = [12f, -6f], Start = "Y0 Spring 1 09:00" };

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

        public DecisionPointOpened Last(string owner) => Outputs.SelectMany(o => o.OpenedDecisions).Last(d => d.Owner == owner);

        public void Decide(DecisionPointOpened dp, string choice)
            => Submit(new DecisionMade(dp.Id, dp.MenuHash, choice, DeciderKind.Llm, "test", 500, null), CommandSource.Ai);
    }

    private static (Harness H, int Npc, Conversation C) TalkTo(int npcRow = 4)
    {
        var h = new Harness { W = Camp.CreateWorld(Content, SerialJobScheduler.Instance) };
        h.Steps(20);
        var t = h.W.People.Transforms[npcRow];
        h.Submit(new PlayerMoved(t.X + 1f, t.Z, 0f), CommandSource.Embodiment);
        h.Submit(new StartConversation(h.W.People.Ids[npcRow]));
        return (h, npcRow, h.W.Conversations.Of(h.W.People.Ids[npcRow])!);
    }

    private static DecisionPointOpened Ask(Harness h, Conversation c, string task = "action.gather_wood", float hours = 1f)
    {
        h.Submit(new PlayerUtteranceClassified(c.Id, c.Turn + 1, "request", 0.9f, 0f, "…", RequestTask: task, RequestHours: hours));
        return h.Last(RequestOwner.Id);
    }

    [Fact]
    public void AnAcceptedFavor_IsCarriedOut_AfterTheTalk()
    {
        var (h, npc, c) = TalkTo();
        var dp = Ask(h, c);
        dp.Options.Select(o => o.Id).ShouldBe(["accept_request", "accept_with_condition", "defer", "refuse_request"]);
        dp.Options.Sum(o => o.P).ShouldBe(1f, 0.001f);
        dp.Options.Single(o => o.Id == "accept_request").Gloss.ShouldContain("firewood");
        h.Decide(dp, "accept_request");
        h.W.Favors.Count.ShouldBe(1);
        h.Events<RequestAnswered>().Single().Answer.ShouldBe("accept_request");
        h.Submit(new EndConversation(c.Id));
        h.Submit(new PlayerMoved(200f, 200f, 0f), CommandSource.Embodiment);   // headless: no client bodies, so let the NPC drop to LOD1

        var wood = (short)ContentDatabase.HandleOf(Content.Actions, "action.gather_wood", a => a.Id);
        var worked = false;
        for (var s = 0; s < 18_000 / 8 && h.W.Favors.Count > 0; s++)   // up to three game hours
        {
            h.Step();
            worked |= h.W.People.Activity[npc].Action == wood && h.W.People.Activity[npc].Phase == 1;
        }

        worked.ShouldBeTrue();
        h.Events<FavorDone>().Single().Task.ShouldBe(wood);
        h.W.Favors.Count.ShouldBe(0);
        var granted = h.W.Content.OpinionModifierHandle("opinion.granted_my_request");
        h.W.Relationships.TryGet(h.W.PlayerId, h.W.People.Ids[npc], out var edge).ShouldBeTrue();
        edge.Mods.ShouldContain(m => m.Modifier == granted);
    }

    [Fact]
    public void AskingAgainTheSameDay_HalvesAcceptance_AngersThenRankles()
    {
        var (h, npc, c) = TalkTo();
        var p = new List<float>();
        for (var k = 0; k < 3; k++)
        {
            var dp = Ask(h, c);
            p.Add(dp.Options.Single(o => o.Id == "accept_request").P);
            h.Decide(dp, "refuse_request");
        }

        p[1].ShouldBeLessThan(p[0] * 0.6f);   // × 0.5 (and the refusal's opinion cost)
        p[2].ShouldBeLessThan(p[1] * 0.6f);
        h.W.People.Emotions[npc].AngerTarget.ShouldBe(h.W.PlayerId);
        var rude = Content.OpinionModifierHandle("opinion.rude_to_me");
        h.W.Relationships.TryGet(h.W.People.Ids[npc], h.W.PlayerId, out var e).ShouldBeTrue();
        e.Mods.ShouldContain(m => m.Modifier == rude);
        var refused = Content.OpinionModifierHandle("opinion.refused_my_request");
        h.W.Relationships.TryGet(h.W.PlayerId, h.W.People.Ids[npc], out var mine).ShouldBeTrue();
        mine.Mods.ShouldContain(m => m.Modifier == refused);
    }

    [Fact]
    public void ADeferredFavor_WaitsForTomorrowsWorkBlock()
    {
        var (h, _, c) = TalkTo();
        h.Decide(Ask(h, c, "action.gather_food", 0.5f), "defer");
        var f = h.W.Favors.Open.Single();
        f.Kind.ShouldBe(FavorKind.Deferred);
        GameDate.FromGameMinute(f.StartMin).ToString().ShouldContain("Spring 2 07:30");
        f.RemainingMin.ShouldBe(30f);
    }

    [Fact]
    public void ATrueClaimOpensTheBeingToldDp_AndBelievingRaisesConfidence()
    {
        var (h, npc, c) = TalkTo();
        var w = h.W;
        int a = 7, b = 8;
        Rumors.SeedWitnessed(w, "claim.stole", a, [w.PlayerRow]);   // the player saw it happen (ground truth: true)
        h.Submit(new PlayerUtteranceClassified(c.Id, c.Turn + 1, "tell", 0.9f, 0f, "…",
            ClaimPredicate: "claim.stole", ClaimSubject: w.People.Ids[a], ClaimObject: w.People.Ids[b], ClaimFirstHand: true));
        h.Events<LieCaught>().ShouldBeEmpty();
        var dp = h.Last(BeingToldOwner.Id);
        dp.Options.Sum(o => o.P).ShouldBe(1f, 0.001f);
        dp.Options.Single(o => o.Id == "believe").Stakes.ShouldBe(Stakes.Medium);   // accusation-grade about a named person
        h.Decide(dp, "believe");
        var claim = (int)dp.Options[0].Params[0].Value;
        w.Beliefs.Get(w.People.Ids[npc], claim)!.C.ShouldBeGreaterThanOrEqualTo(0.85f);   // 0 + (1 − 0)·0.9
    }

    [Fact]
    public void ALieAgainstWhatTheListenerSaw_IsCaught_AndOpensNoDp()
    {
        var (h, npc, c) = TalkTo();
        var w = h.W;
        int thief = 7, victim = 8, framed = 9;
        var stole = Content.ClaimHandle("claim.stole");
        var truth = w.Claims.Observe(stole, w.People.Ids[thief], w.People.Ids[victim], 1f, w.Clock.GameMinute);
        void Saw(int k) { var b = w.Beliefs.GetOrCreate(w.People.Ids[k], truth, w.Clock.GameMinute); (b.C, b.FirstHand, b.FirstHandC) = (0.9f, true, 0.9f); }
        Saw(npc);   // the listener saw who really did it
        w.People.Attributes[npc].Perception = 10f;
        var (lie, caught) = (false, false);
        var claimId = w.Claims.Intern(new Claim { Predicate = (ushort)stole, Subject = w.People.Ids[framed].Value, Object = w.People.Ids[victim].Value, Magnitude = 1f, TimeMin = -1, DerivedFrom = -1, Qualifiers = ClaimQualifiers.Blurred });
        (lie, caught) = BeingToldOwner.LieTest(w, npc, w.PlayerRow, claimId);
        lie.ShouldBeTrue();
        h.Submit(new PlayerUtteranceClassified(c.Id, c.Turn + 1, "tell", 0.9f, 0f, "…",
            ClaimPredicate: "claim.stole", ClaimSubject: w.People.Ids[framed], ClaimObject: w.People.Ids[victim], ClaimFirstHand: true));
        if (caught)
        {
            h.Events<LieCaught>().Single().Liar.ShouldBe(w.PlayerId);
            h.Outputs.Last().OpenedDecisions.ShouldNotContain(d => d.Owner == BeingToldOwner.Id);
            w.Relationships.Opinion(w.People.Ids[npc], w.PlayerId).ShouldBeLessThan(0f);   // lied_to_me
        }
        else
        {
            h.Outputs.Last().OpenedDecisions.ShouldContain(d => d.Owner == BeingToldOwner.Id);
        }

        // Over many listeners the detection rate matches clamp(0.1 + 0.02·10 + F/400 + 0.5, 0, 0.9) ≈ 0.86–0.9.
        var hits = 0;
        for (var k = 0; k < 24; k++)
        {
            if (k == w.PlayerRow) { continue; }
            Saw(k);
            w.People.Attributes[k].Perception = 10f;
            hits += BeingToldOwner.LieTest(w, k, w.PlayerRow, claimId).Detected ? 1 : 0;
        }

        hits.ShouldBeGreaterThanOrEqualTo(17);   // ≥ 17 of 24 (binomial, p ≈ 0.88)
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
