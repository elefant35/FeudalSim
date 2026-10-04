using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Dialogue;
using FeudalSim.Sim.Economy;
using FeudalSim.Sim.Events;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-10: the haggle prototype (15 §5) — values and curves, the trade DP, settlement at the menu price.</summary>
public sealed class TradeTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    // ---- 15 §5.8 worked example A: buying an axe from Aldric ---------------------------------------------------------

    [Fact]
    public void Aldric_ReachAndCurve()
    {
        Haggle.Firmness(40, stubborn: false, greedy: true, charitable: false).ShouldBe(1.5f);
        Haggle.InsultTolerance(false, false, false, greedy: true).ShouldBe(0.30f, 0.0001f);
        var s = Haggle.Susceptibility(40, 20, 45, 10, 55, 25, stubborn: false, greedy: true, paranoid: false, anger: 0);
        s.ShouldBe(0.29f, 0.005f);
        Haggle.KSkill(35, 25).ShouldBe(0.30f, 0.0001f);
        Haggle.Margin(s, 0.30f).ShouldBe(0.028f, 0.0005f);
        // Words: competitor price (+1) and a false flaw claim (−1) at W 0.65 → Σσ 0.37; later "future business" at W 0.60 → +0.27.
        Haggle.Utterance([new("quality_flaw", -1f), new("competitor_price", 1f)], 0.65f, 0.30f, 0, new HashSet<string>()).ShouldBe(0.37f, 0.005f);
        Haggle.Utterance([new("future_business", 1f)], 0.60f, 0.30f, 1, new HashSet<string> { "quality_flaw", "competitor_price" }).ShouldBe(0.27f, 0.005f);
        // A 40f opening bid instead: g 0.45 > τ 0.30 → Anger +19, Opinion −8.
        var (anger, opinion) = Haggle.Lowball((72.5f - 40f) / 72.5f, 0.30f);
        anger.ShouldBe(18.47f, 0.01f);   // 15 §5.8 rounds it to "+19"
        opinion.ShouldBe(-8f, 0.1f);
    }

    private static Haggle.Round Aldric(int k, int granted, float sigma, long bid, long ask)
        => new(72.5f, 103f, 4, 1.5f, 0.15f * 0.2875f * 0.65f, k, granted, sigma, bid, ask, 0f, false, false, CanSettle: true);

    private static IReadOnlyDictionary<string, TradeOption> Seller(Haggle.Round r) => Haggle.Menu(r, seller: true).ToDictionary(o => o.Id);

    [Fact]
    public void Aldric_Round0_TheMenuAndItsPropensities()
    {
        var m = Seller(Aldric(0, 0, 0.37f, 60, 103));
        m["accept_offer"].Eligible.ShouldBeFalse();   // 60 < the 70.4f floor
        (m["counter_step_0"].PriceF, m["counter_step_1"].PriceF, m["counter_step_2"].PriceF, m["counter_step_3"].PriceF).ShouldBe((99L, 98L, 97L, 96L));
        m["counter_step_0"].P.ShouldBe(0.10f, 0.005f);
        m["counter_step_1"].P.ShouldBe(0.53f, 0.005f);
        m["counter_step_2"].P.ShouldBe(0.18f, 0.005f);
        m["counter_step_3"].P.ShouldBe(0.004f, 0.002f);
        m["refuse"].P.ShouldBe(0.17f, 0.005f);
        m["walk_away"].P.ShouldBe(0.02f, 0.001f);
    }

    [Fact]
    public void Aldric_LaterRounds_WhimAcceptanceAndTheRatchet()
    {
        // Round 1: bid 75 after a ⅓-step counter at 98: accept is eligible but only the 0.03 whim (under the high floor).
        var r1 = Seller(Aldric(1, 1, 0.37f, 75, 98));
        r1["accept_offer"].P.ShouldBe(0.03f, 0.002f);
        r1["counter_step_0"].Eligible.ShouldBeFalse();   // the granted step ratchets
        (r1["counter_step_1"].PriceF, r1["counter_step_2"].PriceF, r1["counter_step_3"].PriceF).ShouldBe((91L, 90L, 90L));
        r1["counter_step_1"].P.ShouldBe(0.58f, 0.01f);
        r1["counter_step_2"].P.ShouldBe(0.20f, 0.01f);
        // Round 2: bid 78 with "future business" (Σσ 0.64).
        var r2 = Seller(Aldric(2, 1, 0.64f, 78, 91));
        (r2["counter_step_1"].PriceF, r2["counter_step_2"].PriceF, r2["counter_step_3"].PriceF).ShouldBe((82L, 82L, 81L));
        r2["counter_step_1"].P.ShouldBe(0.16f, 0.01f);
        r2["counter_step_2"].P.ShouldBe(0.52f, 0.01f);
        r2["counter_step_3"].P.ShouldBe(0.10f, 0.01f);
        // Round 3: bid 80, against O_4 = RV_j at the remaining steps → accept 0.98.
        Seller(Aldric(3, 2, 0.64f, 80, 82))["accept_offer"].P.ShouldBe(0.98f, 0.005f);
    }

    [Fact]
    public void ExampleB_TemplateModeExpectsTheWordsSignal()
    {
        // 15 §5.9: "Mention another seller" at W 0.5 → Σσ 0.40 → counters 0.07 / 0.51 / 0.22 / 0.006, refuse 0.17, walk 0.02.
        var m = Seller(Aldric(0, 0, 0.40f, 60, 103));
        m["counter_step_0"].P.ShouldBe(0.07f, 0.005f);
        m["counter_step_1"].P.ShouldBe(0.51f, 0.005f);
        m["counter_step_2"].P.ShouldBe(0.22f, 0.005f);
        m["refuse"].P.ShouldBe(0.17f, 0.005f);
        var expected = (m["counter_step_1"].P + (2 * m["counter_step_2"].P) + (3 * m["counter_step_3"].P)) / (1f - m["refuse"].P - m["walk_away"].P) / 3f;
        expected.ShouldBe(0.40f, 0.03f);   // template mode grants what words and skill say (canon §13.4)
    }

    // ---- In the camp ------------------------------------------------------------------------------------------------

    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"))
        with { Player = [12f, -6f], Start = "Y0 Spring 1 19:30" };

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

        public DecisionPointOpened LastDp => Outputs.SelectMany(o => o.OpenedDecisions).Last(d => d.Owner == TradeOwner.Id);
    }

    private static (Harness H, int Npc) Market(int npcRow = 4)
    {
        var h = new Harness { W = Camp.CreateWorld(Content, SerialJobScheduler.Instance) };
        h.Steps(20);
        var w = h.W;
        var t = w.People.Transforms[npcRow];
        h.Submit(new PlayerMoved(t.X + 1f, t.Z, 0f), CommandSource.Embodiment);
        h.Submit(new SetHoldings(w.PlayerId, "item.iron_axe", 1, 40), CommandSource.Scenario);
        h.Submit(new SetHoldings(w.People.Ids[npcRow], "item.iron_knife", 2, 200), CommandSource.Scenario);
        h.Submit(new StartConversation(w.People.Ids[npcRow]));
        return (h, npcRow);
    }

    [Fact]
    public void TalkingSomeoneIntoBuying_TheTradeSystemExecutesAtTheMenuPrice()
    {
        var (h, npc) = Market();
        var w = h.W;
        h.Submit(new TradeOpen(w.People.Ids[npc], "item.iron_axe", 1, PlayerSells: true));
        var n = h.Events<TradeOffered>().Single();
        h.Submit(new TradeOffer(n.Negotiation, 45, ["relationship"]));
        var dp = h.LastDp;
        dp.MaxDecider.ShouldBe(DeciderKind.Llm);
        dp.Options.Sum(o => o.P).ShouldBe(1f, 0.001f);
        var buy = dp.Options.Single(o => o.Id == "buy_at_ask");
        buy.Eligible.ShouldBeTrue();
        buy.Params.ShouldContain(p => p.Key == "price_f" && p.Value == 45);
        buy.Gloss.ShouldContain("11 pennies and 1 farthing");   // 45f in words; the line never invents a number
        h.Submit(new DecisionMade(dp.Id, dp.MenuHash, "buy_at_ask", DeciderKind.Llm, "test", 600, null), CommandSource.Ai);

        var deal = h.Events<TradeSettled>().Single();
        (deal.Seller, deal.Buyer, deal.PriceF).ShouldBe((w.PlayerId, w.People.Ids[npc], 45L));
        w.Holdings.Goods(w.People.Ids[npc], Content.Items.ToList().FindIndex(i => i.Id == "item.iron_axe")).ShouldBe(1);
        (w.Holdings.Coin(w.PlayerId), w.Holdings.Coin(w.People.Ids[npc])).ShouldBe((85L, 155L));
        w.Negotiations.Count.ShouldBe(0);
    }

    [Fact]
    public void BuyingFromAnNpc_CountersRatchet_AndThePolicyDecidesAtTheDeadline()
    {
        var (h, npc) = Market();
        var w = h.W;
        h.Submit(new TradeOpen(w.People.Ids[npc], "item.iron_knife", 1, PlayerSells: false));
        var opening = h.Events<TradeOffered>().Single();
        opening.PriceF.ShouldBeGreaterThan(14L);   // the deterministic opening ask is above value (Asp)
        h.Submit(new TradeOffer(opening.Negotiation, 12));
        var dp = h.LastDp;
        while (h.W.Clock.Step <= dp.DeadlineStep) { h.Step(); }
        var resolved = h.Events<DecisionResolved>().Single(r => r.Dp == dp.Id);
        (resolved.Decider, resolved.Guard).ShouldBe((DeciderKind.Policy, GuardOutcome.Deadline));   // template mode: the policy decides
        var neg = w.Negotiations.Get(opening.Negotiation);
        if (resolved.Chosen.StartsWith("counter_step_", StringComparison.Ordinal))
        {
            neg!.NpcOffer.ShouldBeLessThanOrEqualTo(opening.PriceF);   // counters never go back up
            neg.Round.ShouldBe(1);
        }
    }

    [Fact]
    public void Lowballs_AreActs_TheSecondEndsTheHaggle()
    {
        var (h, npc) = Market();
        var w = h.W;
        h.Submit(new TradeOpen(w.People.Ids[npc], "item.iron_knife", 1, PlayerSells: false));
        var neg = h.Events<TradeOffered>().Single().Negotiation;
        var patience = w.Negotiations.Get(neg)!.Patience;
        var op0 = w.Relationships.Opinion(w.People.Ids[npc], w.PlayerId);
        h.Submit(new TradeOffer(neg, 2));   // far below its reservation value
        w.People.Emotions[npc].AngerTarget.ShouldBe(w.PlayerId);
        w.Relationships.Opinion(w.People.Ids[npc], w.PlayerId).ShouldBeLessThan(op0 - 4f);   // lowballed_me
        w.Negotiations.Get(neg)!.Patience.ShouldBe(Math.Max(0, patience - 2));
        var dp = h.LastDp;
        h.Submit(new DecisionMade(dp.Id, dp.MenuHash, null, DeciderKind.Policy, "test", 0, null), CommandSource.Ai);
        if (w.Negotiations.Get(neg) is not null)
        {
            h.Submit(new TradeOffer(neg, 2));
            h.Events<NegotiationEnded>().Single().Reason.ShouldBe("insulted");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
