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

namespace FeudalSim.Integration.Tests;

/// <summary>M1-09a: rapport (16 §4.15) with the words budget (canon §13.4), apology (16 §4.14), menu width (22 §6.3).</summary>
public sealed class SocialDpTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"))
        with { Player = [12f, -6f], Start = "Y0 Spring 1 19:30" };

    [Theory]
    [InlineData(0f, 0.26f, 0.58f, 0.16f)]
    [InlineData(0.5f, 0.53f, 0.43f, 0.04f)]
    [InlineData(1f, 0.76f, 0.23f, 0.01f)]
    public void RapportSplit_Matches16(float z, float warm, float neutral, float cool)
    {
        var (w, n, c) = RapportOwner.Split(z);
        w.ShouldBe(warm, 0.01f);
        n.ShouldBe(neutral, 0.01f);
        c.ShouldBe(cool, 0.01f);
    }

    [Fact]
    public void MenuWidth_ReproducesThe22Example()
    {
        // A master persuader (K 0.9) to a warm listener (s 0.8): Margin 11.4 %; excellent words (L 0.865) → {⅓ 0.005, ⅔ 0.40, full 0.60}.
        MenuWidth.Margin(0.15f, 0.8f, 0.9f).ShouldBe(0.114f, 0.0005f);
        var steps = MenuWidth.Steps((0.5f * 0.9f) + (0.5f * MenuWidth.LSkill(90f)));
        steps[1].ShouldBe(0.005f, 0.003f);
        steps[2].ShouldBe(0.40f, 0.01f);
        steps[3].ShouldBe(0.60f, 0.01f);
        // A novice (Persuasion 10): L 0.2 → {none 0.40, ⅓ 0.59, ⅔ 0.02}.
        var novice = MenuWidth.Steps((0.5f * 0.9f) + (0.5f * MenuWidth.LSkill(10f)));
        novice[0].ShouldBe(0.40f, 0.01f);
        novice[1].ShouldBe(0.59f, 0.01f);
        novice[2].ShouldBe(0.02f, 0.01f);
        RapportOwner.Step(10f).ShouldBe(1);
        RapportOwner.Step(20f).ShouldBe(2);
        RapportOwner.Step(40f).ShouldBe(3);
        RapportOwner.Step(70f).ShouldBe(4);
    }

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

        public IEnumerable<DecisionPointOpened> Opened(string owner) => Outputs.SelectMany(o => o.OpenedDecisions).Where(d => d.Owner == owner);
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

    private static void Say(Harness h, Conversation c, string act, float persuasiveness = 0f, float sincerity = 0f)
        => h.Submit(new PlayerUtteranceClassified(c.Id, c.Turn + 1, act, 0.9f, 0f, "…", Persuasiveness: persuasiveness, Politeness: persuasiveness > 0 ? 4 : 0, Sincerity: sincerity));

    [Fact]
    public void WordsBudget_CapsOpinionFromTalkAtTenPerPairPerDay()
    {
        var h = new Harness { W = Camp.CreateWorld(Content, SerialJobScheduler.Instance) };
        h.Steps(20);
        var w = h.W;
        EntityId a = w.People.Ids[1], b = w.People.Ids[2];
        var before = w.Relationships.Opinion(a, b);
        for (var k = 0; k < 20; k++)
        {
            w.Relationships.ApplyModifier(a, b, "opinion.complimented_me");
            w.Relationships.ApplyModifier(a, b, "opinion.joked_together");
            w.Relationships.ApplyModifier(a, b, "opinion.comforted_me");
        }

        (w.Relationships.Opinion(a, b) - before).ShouldBeLessThanOrEqualTo(10.01f);
        w.Relationships.WordsBudgetLeft(a, b).ShouldBe(0f);
        w.Relationships.ApplyModifier(a, b, "opinion.helped_my_work");   // a deed, not words: uncapped
        (w.Relationships.Opinion(a, b) - before).ShouldBeGreaterThan(10.5f);
        h.Steps(18_000);   // next day
        w.Relationships.WordsBudgetLeft(a, b).ShouldBeGreaterThan(0f);   // a new day's budget (their own talk may already use some)
    }

    [Fact]
    public void AConversationClosesWithARapportDp_EveryEightTurnsAnother_AtMostThree()
    {
        var (h, npc, c) = TalkTo();
        for (var t = 0; t < 17; t++)
        {
            Say(h, c, "small_talk", persuasiveness: 6);
            var init = h.Opened(InitiativeOwner.Id).Last();   // the "model" keeps the NPC talking: just answer
            h.Submit(new DecisionMade(init.Id, init.MenuHash, "none", DeciderKind.Llm, "test", 400, null), CommandSource.Ai);
            h.Steps(45);
        }

        h.W.Conversations.Get(c.Id).ShouldNotBeNull();
        var mid = h.Opened(RapportOwner.Id).ToList();
        mid.Count.ShouldBe(2);   // after turns 8 and 16
        mid.ShouldAllBe(d => d.MaxDecider == DeciderKind.Llm);
        var dp = mid[0];
        dp.Options.Select(o => o.Id).ShouldBe(["cool_to_speaker", "stay_neutral", "warm_to_speaker"]);
        dp.Options.Sum(o => o.P).ShouldBe(1f, 0.001f);

        h.Submit(new EndConversation(c.Id));
        h.Opened(RapportOwner.Id).Count().ShouldBe(3);   // the close brings the third
        h.Opened(RapportOwner.Id).Last().MaxDecider.ShouldBe(DeciderKind.Llm);   // bundled into the goodbye
    }

    [Fact]
    public void WalkingOff_ThePolicyDecidesRapport_AndAWarmPickRaisesOpinionByTheStep()
    {
        var (h, npc, c) = TalkTo();
        Say(h, c, "praise", persuasiveness: 6);
        var op0 = h.W.Relationships.Opinion(h.W.People.Ids[npc], h.W.PlayerId);
        h.Submit(new PlayerMoved(200f, 200f, 0f), CommandSource.Embodiment);
        h.Step();
        h.Events<ConversationEnded>().Single().Reason.ShouldBe("left");
        var resolved = h.Events<DecisionResolved>().Single(r => r.Owner == RapportOwner.Id);
        resolved.Guard.ShouldBe(GuardOutcome.Inline);   // no reply to bundle it into: the policy, at once
        var op1 = h.W.Relationships.Opinion(h.W.People.Ids[npc], h.W.PlayerId);
        if (resolved.Chosen == "warm_to_speaker") { (op1 - op0).ShouldBeGreaterThan(0.5f); }
        if (resolved.Chosen == "cool_to_speaker") { (op1 - op0).ShouldBeLessThan(-0.5f); }
    }

    [Fact]
    public void AnAcceptedApology_HalvesTheGrievance_CalmsAnger_AndEndsTheQuarrel()
    {
        var (h, npc, c) = TalkTo();
        var w = h.W;
        EntityId me = w.People.Ids[npc], player = w.PlayerId;
        h.Submit(new PlayerUtteranceClassified(c.Id, c.Turn + 1, "insult", 0.9f, 0f, "…"));
        var hurt = w.Relationships.Opinion(me, player);
        var anger = w.People.Emotions[npc].Anger;
        Say(h, c, "apologize", sincerity: 5);
        var dp = h.Opened(ApologyOwner.Id).Single();
        var accept = dp.Options.Single(o => o.Id == "accept_apology");
        accept.P.ShouldBeInRange(0.05f, 0.95f);
        dp.Options.Single(o => o.Id == "demand_amends").Eligible.ShouldBeFalse();   // no material harm in M1
        h.Submit(new DecisionMade(dp.Id, dp.MenuHash, "accept_apology", DeciderKind.Llm, "test", 500, null), CommandSource.Ai);
        h.Events<DecisionResolved>().Single(r => r.Dp == dp.Id).Guard.ShouldBe(GuardOutcome.Passed);
        w.Relationships.Opinion(me, player).ShouldBeGreaterThan(hurt + 3f);   // insulted_me keeps 50 %
        w.People.Emotions[npc].Anger.ShouldBeLessThan(anger - 20f);
        w.Confrontations.Between(me, player).ShouldBeNull();
    }

    [Fact]
    public void RepeatedApologies_LoseForce_AndTheFourthReadsAsMockery()
    {
        var (h, npc, c) = TalkTo();
        var w = h.W;
        var p = new List<float>();
        for (var k = 0; k < 4; k++)
        {
            Say(h, c, "apologize");
            p.Add(h.Opened(ApologyOwner.Id).Last().Options.Single(o => o.Id == "accept_apology").P);
        }

        p[1].ShouldBeLessThan(p[0]);   // −0.10 per apology in the last 4 days
        p[2].ShouldBeLessThanOrEqualTo(p[1]);   // until the 0.05 floor
        var rude = w.Content.OpinionModifierHandle("opinion.rude_to_me");
        w.Relationships.TryGet(w.People.Ids[npc], w.PlayerId, out var edge).ShouldBeTrue();
        edge.Mods.ShouldContain(m => m.Modifier == rude);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
