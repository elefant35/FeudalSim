using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-07b: reputation (16 §8) — impressions, D9, no double counting, trust ceiling, plausibility, Renown.</summary>
public sealed class ReputationTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"));

    /// <summary>Observer A (row 0), thief B (row 1), victim C (row 2); A's values all 50, Warmth 50.</summary>
    private static (SimWorld W, EntityId A, EntityId B, EntityId C) Three()
    {
        var w = new SimWorld(1, startGameMs: 0) { Content = Content };
        foreach (var (name, k) in new[] { ("A", 0), ("B", 1), ("C", 2) }) { w.Enqueue(new CommandEnvelope(k + 1, 0, CommandSource.Scenario, new SpawnPerson(name, k, 0))); }
        w.Step();
        ref var p = ref w.People.Personality[0];
        p.Values = new ValueBlock { Family = 50, Wealth = 50, Status = 50, Honor = 50, Tradition = 50, Faith = 50, Fairness = 50, Freedom = 50, Loyalty = 50 };
        p.Warmth = 50;
        return (w, w.People.Ids[0], w.People.Ids[1], w.People.Ids[2]);
    }

    private static Belief Believe(SimWorld w, EntityId holder, int claim, float c, bool firstHand = false)
    {
        var b = w.Beliefs.GetOrCreate(holder, claim, w.Clock.GameMinute);
        (b.C, b.FirstHand, b.FirstHandC) = (c, firstHand, firstHand ? c : 0f);
        w.Beliefs.Touch();
        return b;
    }

    [Fact]
    public void ABelievedTheft_DentsHonestyAndLowersOpinionThroughD9()
    {
        var (w, a, b, c) = Three();
        var stole = w.Claims.Observe(Content.ClaimHandle("claim.stole"), b, c, 1f, w.Clock.GameMinute);
        var before = w.Relationships.Opinion(a, b);
        Believe(w, a, stole, 0.9f);

        w.Reputation.R(a, b, RepAxis.Honesty).ShouldBe(100f * MathF.Tanh(-12f * 0.9f / 100f), 0.01f);       // −10.76
        w.Reputation.R(a, b, RepAxis.Lawfulness).ShouldBe(100f * MathF.Tanh(-15f * 0.9f / 100f), 0.01f);    // −13.42, no M1 effect
        w.Reputation.D9(0, a, b).ShouldBe(-10.76f * 50f / 100f, 0.01f);                                     // Honesty × Fairness only
        (w.Relationships.Opinion(a, b) - before).ShouldBe(-5.38f, 0.01f);
    }

    [Fact]
    public void WhatHappenedToMe_ReachesOpinionOnlyAsAModifier_NotThroughD9()
    {
        var (w, a, b, _) = Three();
        var robbedMe = w.Claims.Observe(Content.ClaimHandle("claim.stole"), b, a, 1f, w.Clock.GameMinute);
        Believe(w, a, robbedMe, 1f, firstHand: true);
        w.Reputation.R(a, b, RepAxis.Honesty).ShouldBeLessThan(-15f);                     // R_full: wariness, trust ceilings
        w.Reputation.R(a, b, RepAxis.Honesty, thirdParty: true).ShouldBe(0f);           // R³ᵖ: excluded (16 §4.7)
        w.Reputation.D9(0, a, b).ShouldBe(0f);
    }

    [Fact]
    public void OnlyTheStrongestVariantCounts()
    {
        var (w, a, b, c) = Three();
        var truth = w.Claims.Observe(Content.ClaimHandle("claim.stole"), b, c, 1f, w.Clock.GameMinute);
        Believe(w, a, truth, 0.9f);
        var alone = w.Reputation.R(a, b, RepAxis.Honesty);
        var bigger = w.Claims.Intern(w.Claims[truth] with { Magnitude = 3f, DerivedFrom = truth, True = 0 });
        Believe(w, a, bigger, 0.6f);   // weaker variant of the same story
        w.Reputation.R(a, b, RepAxis.Honesty).ShouldBe(alone, 1e-4f);
    }

    [Fact]
    public void ABelievedLiar_CannotBeDeeplyTrusted()
    {
        var (w, a, b, c) = Three();
        for (var k = 0; k < 6; k++) { Believe(w, a, w.Claims.Observe(Content.ClaimHandle("claim.lied"), b, c, 1f, w.Clock.GameMinute + k), 1f, firstHand: true); }
        var honesty = w.Reputation.R(a, b, RepAxis.Honesty);
        honesty.ShouldBeLessThan(-60f);   // ceiling ≤ 50 for a believed liar
        for (var k = 0; k < 20; k++) { w.Relationships.TrustEvidence(a, b, 10f); }
        w.Relationships.Trust(a, b).ShouldBe(RelationshipStore.TrustCeilingBase + (0.5f * honesty), 0.01f);   // 16 §4.8 ceiling
    }

    [Fact]
    public void ClaimsThatFitWhatIThink_AreMoreCredible()
    {
        var (w, a, b, c) = Three();
        var cheat = w.Claims.Observe(Content.ClaimHandle("claim.cheated"), b, c, 1f, w.Clock.GameMinute);
        var neutral = Rumors.Credibility(w, 0, 2, w.Claims[cheat], hop: 1);
        for (var k = 0; k < 4; k++) { Believe(w, a, w.Claims.Observe(Content.ClaimHandle("claim.lied"), b, c, 1f, w.Clock.GameMinute + 10 + k), 0.9f); }
        Rumors.Credibility(w, 0, 2, w.Claims[cheat], hop: 1).ShouldBeGreaterThan(neutral * 1.1f);   // plaus = 1 + 0.5·(−R)/100
    }

    [Fact]
    public void Renown_IsHighInASmallCamp_AndSaved()
    {
        var w = Camp.CreateWorld(Content, SerialJobScheduler.Instance);
        while (w.Clock.GameMinute < (3 * 1440) + 600) { w.Step(); }
        w.Reputation.HasRenown.ShouldBeTrue();
        for (var k = 0; k < w.People.Count; k++) { w.Reputation.Renown(w.People.Ids[k]).ShouldBeInRange(60f, 100f); }   // 24 shipmates know each other
        var restored = Sim.Persistence.SaveCodec.Restore(Sim.Persistence.SaveCodec.Capture(w), out _);
        restored.Reputation.Renown(w.People.Ids[3]).ShouldBe(w.Reputation.Renown(w.People.Ids[3]));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
