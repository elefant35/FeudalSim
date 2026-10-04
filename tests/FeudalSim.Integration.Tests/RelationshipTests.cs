using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.Time;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-05: relationships per 16 §4 — the Mira worked example, stacking, trust, familiarity, tags.</summary>
public sealed class RelationshipTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    /// <summary>Two Varrow settlers (no systems, so reads are pure and time only advances). Mira: Warmth 65, Volatility 55, Honor 40.</summary>
    private static (SimWorld W, EntityId Mira, EntityId Player) Pair()
    {
        var w = new SimWorld(1, startGameMs: 0) { Content = Content };
        w.Enqueue(new CommandEnvelope(1, 0, CommandSource.Scenario, new SpawnPerson("Mira", 0, 0)));
        w.Enqueue(new CommandEnvelope(2, 0, CommandSource.Scenario, new SpawnPerson("Newcomer", 1, 0)));
        w.Step();
        ref var p = ref w.People.Personality[0];
        (p.Warmth, p.Volatility, p.Traits) = (65, 55, 0);
        p.Values.Honor = 40;
        w.People.Personality[1].Traits = 0;
        return (w, w.People.Ids[0], w.People.Ids[1]);
    }

    private static void ToDay(SimWorld w, double day)
    {
        var target = (long)(day * 1440 * SimClock.MsPerGameMinute);
        while (w.Clock.GameMs < target) { w.Step(); }
    }

    private static float Mods(SimWorld w, EntityId a, EntityId b) => w.Relationships.Opinion(a, b) - w.Relationships.Derived(a, b);

    [Fact]
    public void TheMiraWorkedExample()
    {
        var (w, mira, player) = Pair();
        var rel = w.Relationships;
        rel.Trust(mira, player).ShouldBe(39.5f, 0.01f);   // a stranger: T0 − 10 (49.5 − 10)
        rel.GetOrCreate(mira, player).Trust.ShouldBe(49.5f, 0.01f);   // 35 + 0.3·15 + 10 same homeland

        ToDay(w, 3);
        rel.ApplyModifier(mira, player, "opinion.helped_my_work").ShouldBe(5.3f, 0.01f);   // ×(0.8 + 0.4·0.65)
        rel.ApplyModifier(mira, player, "opinion.fed_me_hungry").ShouldBe(10.6f, 0.01f);
        Mods(w, mira, player).ShouldBe(15.9f, 0.05f);                                       // 26.9 − 11 derived

        ToDay(w, 10);
        Mods(w, mira, player).ShouldBe(11.5f, 0.1f);                                        // +2.4 +9.1 → 22.5 − 11
        rel.ApplyModifier(mira, player, "opinion.insulted_me", isPublic: true).ShouldBe(-16.5f, 0.05f);   // −12 × 1.5 × 0.9 × 1.02
        Mods(w, mira, player).ShouldBe(-5.1f, 0.1f);                                        // 5.9 − 11

        ToDay(w, 11);
        rel.ScaleModifier(mira, player, "opinion.insulted_me", keep: 0.5f);                 // apology accepted
        Mods(w, mira, player).ShouldBe(3.4f, 0.1f);                                         // 14.4 − 11

        // Day 20: the doc's 15.6 counts a 0.75 helped_my_work slot that 16 §4.2's own rule (< 1 → dropped) removes;
        // fed 7.3 − insult 3.5 = 3.9 (→ 14.9 with +11 derived). 16 §4.13 is corrected to match.
        ToDay(w, 20);
        Mods(w, mira, player).ShouldBe(3.9f, 0.1f);
    }

    [Fact]
    public void StackingRules_SaturateAddOnceFloors()
    {
        var (w, a, b) = Pair();
        var rel = w.Relationships;
        w.People.Personality[0].Warmth = 50;   // positive scaling × 1.0
        for (var k = 0; k < 40; k++) { rel.ApplyModifier(a, b, "opinion.chatted"); }
        Mods(w, a, b).ShouldBeInRange(11.5f, 12f);                                          // saturates toward the +12 cap

        for (var k = 0; k < 10; k++) { rel.ApplyModifier(a, b, "opinion.stood_by_me"); }
        Mods(w, a, b).ShouldBe(12f + 40f, 0.6f);                                            // add caps at +40

        rel.ApplyModifier(a, b, "opinion.first_impression", 0.5f);
        rel.ApplyModifier(a, b, "opinion.first_impression", 1.0f).ShouldBe(0f);           // once: second ignored

        var (w2, c, d) = Pair();
        w2.People.Personality[0].Volatility = 50;   // negative scaling × 1.0
        w2.Relationships.ApplyModifier(c, d, "opinion.wounded_me");                         // −40, floor 0.25 → −10 forever
        ToDay(w2, 2000);
        Mods(w2, c, d).ShouldBe(-10f, 0.1f);
        w2.Relationships.Fear(c, d).ShouldBeLessThan(1f);                                   // fear +30 decays (8-day half-life)
    }

    [Fact]
    public void Trust_IsAsymmetric_AndCappedByHonestyReputation()
    {
        var (w, a, b) = Pair();
        var rel = w.Relationships;
        rel.TrustEvidence(a, b, 10f);                       // kept a major promise: +10·(100 − 49.5)/100 = +5.05 (ceiling 80 at neutral reputation)
        rel.Trust(a, b).ShouldBe(54.55f, 0.01f);
        rel.TrustEvidence(a, b, 40f, honestyReputation: -60f);
        rel.Trust(a, b).ShouldBe(50f, 0.01f);                // a believed liar: ceiling 80 − 30
        rel.TrustEvidence(a, b, 40f, honestyReputation: 60f);
        rel.Trust(a, b).ShouldBeGreaterThan(60f);            // a reputation for honesty lifts the ceiling toward 100
        var before = rel.Trust(a, b);
        rel.TrustEvidence(a, b, -20f);                       // broke a major promise: −20·(0.5 + T/100)
        (before - rel.Trust(a, b)).ShouldBeGreaterThan(20f); // betrayal hurts more when trust was high
    }

    [Fact]
    public void Familiarity_ADailyAcquaintanceReaches50InAbout15Days()
    {
        var (w, a, b) = Pair();
        var rel = w.Relationships;
        for (var day = 0; day < 15; day++)
        {
            rel.Contact(a, b, 3f, social: true);     // one chat
            rel.Contact(a, b, 1.5f, social: false);  // co-working ≥ 2 h
            rel.Contact(a, b, 0.3f, social: false);  // co-resident in a small settlement
            ToDay(w, day + 1);
        }

        rel.Familiarity(a, b).ShouldBe(52f, 2.5f);   // 16 §4.9: ≈ 4.8 %/day → F 50 in ~15 days
        rel.Familiarity(b, a).ShouldBe(rel.Familiarity(a, b), 0.01f);
        rel.Contact(a, b, 3f, social: true);
        rel.Contact(a, b, 3f, social: true);
        rel.Contact(a, b, 3f, social: true);          // social contact capped at +6 per pair per day
        rel.GetOrCreate(a, b).SocialContactToday.ShouldBe(6f);
    }

    [Fact]
    public void FriendTag_NeedsTheThresholdsForTwoDays()
    {
        var (w, a, b) = Pair();
        var rel = w.Relationships;
        var e = rel.GetOrCreate(a, b);
        (e.Familiarity, e.Trust) = (40f, 45f);
        for (var k = 0; k < 3; k++) { rel.ApplyModifier(a, b, "opinion.stood_by_me"); }   // +45 → Op ≥ 30
        rel.DailyUpdate();
        rel.HasTag(a, b, RelTags.Friend).ShouldBeFalse();
        ToDay(w, 1); rel.DailyUpdate();
        rel.HasTag(a, b, RelTags.Friend).ShouldBeFalse();
        ToDay(w, 2); rel.DailyUpdate();
        rel.HasTag(a, b, RelTags.Friend).ShouldBeTrue();
        rel.HasTag(a, b, RelTags.Acquaintance).ShouldBeTrue();
    }

    [Fact]
    public void TheCampFormsRelationshipsDeterministically()
    {
        var scenario = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Days = 12 };
        var a = ScenarioRunner.Run(scenario, Content, threads: 1);
        var b = ScenarioRunner.Run(scenario, Content, threads: 1);
        a.FinalHash.ShouldBe(b.FinalHash);
        a.Days[^1].Camp!.MeanOpinion.ShouldBeGreaterThan(5);   // shipmates, same homeland, chats and shared meals
        a.Days[^1].Camp!.EnemiesPerPerson.ShouldBe(0);           // nobody harms anyone yet (M1-06/08 bring conflict)

        // Relationships differentiate: some pairs warm well beyond the derived baseline, familiarity grows.
        var w = scenario.CreateWorld(Content, SerialJobScheduler.Instance);
        while (w.Clock.GameMs < scenario.StartGameMs() + (12L * 1440 * SimClock.MsPerGameMinute)) { w.Step(); }
        var edges = w.Relationships.Edges.Select(kv => (Op: w.Relationships.Opinion(new EntityId(kv.Key.Holder), new EntityId(kv.Key.Other)),
            Derived: w.Relationships.Derived(new EntityId(kv.Key.Holder), new EntityId(kv.Key.Other)), kv.Value.Familiarity)).ToList();
        edges.Count.ShouldBe(24 * 23);
        edges.Max(e => e.Op - e.Derived).ShouldBeGreaterThan(8);      // seed 42, day 12: +10.6 (best pair Op 24.2, mean 12.6)
        edges.Average(e => e.Familiarity).ShouldBeGreaterThan(30);   // shipmates 15–35 + daily co-residence, chats, co-work
        // Friendships (Op ≥ 30) need M1-06's partner selection by relationship and its positive interactions (16 §5.3).
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
