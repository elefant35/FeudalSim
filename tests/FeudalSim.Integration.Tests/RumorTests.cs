using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.Systems;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-07a: claims, beliefs and rumors (16 §7).</summary>
public sealed class RumorTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"));

    [Theory]
    [InlineData(0.44f, 0.0f, 0.9f)]
    [InlineData(0.20f, 0.3f, 0.7f)]
    [InlineData(0.80f, 0.6f, 0.9f)]
    public void BeingTold_WithNoWordsTerm_IsSection72InExpectation(float cred, float c0, float cS)
    {
        // 16 §7.10: every taking option applies c ← c + (1 − c)·cS, doubt leaves c — so E[Δc] = p_take·(1 − c)·cS = §7.2's update.
        var m = Rumors.BeingToldMasses(cred, g: 0f, margin: 0f, pShareListener: 0.5f, jEff: 0.7f, discreet: true, secret: false, loyalAboutOwn: false, canRepeat: true, canKeepQuiet: true);
        (m.Doubt + m.Take).ShouldBe(1f, 1e-5f);
        m.Take.ShouldBe(Math.Clamp(cred, 0.05f, 0.95f), 1e-5f);
        var expected = m.Take * (1f - c0) * cS;
        expected.ShouldBe(cred * (1f - c0) * cS, 1e-5f);
        m.Repeat.ShouldBe(m.Take * 0.7f / 1.1f, 1e-5f);      // ρ = clamp(2·0.5·0.7) = 0.7, q = 0.1 + 0.3 Discreet = 0.4: past 1, both ÷ 1.1
        m.KeepQuiet.ShouldBe(m.Take * 0.4f / 1.1f, 1e-5f);
    }

    [Fact]
    public void FirstHandBeliefs_LoseAtMost020ToHearsay()
    {
        var b = new Belief { C = 0.9f, FirstHandC = 0.9f, FirstHand = true };
        for (var k = 0; k < 5; k++) { Rumors.Update(b, 0.9f, supports: false); }
        b.C.ShouldBe(0.7f, 1e-5f);

        var hearsay = new Belief { C = 0.9f };
        Rumors.Update(hearsay, 0.9f, supports: false);
        hearsay.C.ShouldBe(0.9f * (1f - 0.72f), 1e-5f);
    }

    [Fact]
    public void Claims_AreInternedAndTheSimKnowsWhichAreTrue()
    {
        var store = new ClaimStore();
        var stole = Content.ClaimHandle("claim.stole");
        var truth = store.Observe(stole, new EntityId(1), new EntityId(2), 10f, 600);
        store.Intern(store[truth] with { True = 0 }).ShouldBe(truth);                                    // same statement, same id
        var exaggerated = store.Intern(store[truth] with { Magnitude = 25f, DerivedFrom = truth, True = 0 });
        store[exaggerated].True.ShouldBe((byte)0);                                                       // "a sack of flour", not a loaf
        var blurred = store.Intern(store[truth] with { TimeMin = -1, Qualifiers = ClaimQualifiers.Blurred, DerivedFrom = truth, True = 0 });
        store[blurred].True.ShouldBe((byte)1);                                                           // vaguer, still true
        var shifted = store.Intern(store[truth] with { Subject = 3, DerivedFrom = truth, True = 0 });
        store[shifted].True.ShouldBe((byte)0);
        store.Root(shifted).ShouldBe(truth);
    }

    [Fact]
    public void Witnesses_GetFirstHandBeliefs_InvolvedSureBystandersNearlySo()
    {
        var w = new SimWorld(1, startGameMs: 0) { Content = Content };
        for (var k = 0; k < 4; k++) { w.Enqueue(new CommandEnvelope(k + 1, 0, CommandSource.Scenario, new SpawnPerson($"P{k}", k * 1.5f, 0))); }
        w.Step();
        for (var k = 0; k < 4; k++) { w.People.Activity[k].Action = 0; }   // awake, doing something
        w.People.Transforms[3].X = 50f;                                    // out of earshot

        var id = Rumors.Witness(w, "claim.insulted", 0, 1, 1f, static (world, x, y) => InteractionSystem.InRange(world, x, y));
        w.Claims[id].True.ShouldBe((byte)1);
        w.Beliefs.Get(w.People.Ids[0], id)!.C.ShouldBe(1f);
        w.Beliefs.Get(w.People.Ids[1], id)!.C.ShouldBe(1f);
        w.Beliefs.Get(w.People.Ids[2], id)!.C.ShouldBe(0.9f);
        w.Beliefs.Get(w.People.Ids[2], id)!.FirstHand.ShouldBeTrue();
        w.Beliefs.Get(w.People.Ids[3], id).ShouldBeNull();
    }

    [Fact]
    public void APublicEventAtTheFire_ReachesTheCampWithinThreeDays()
    {
        // M1 exit criterion (30 §5): rumors about a public event reach ≥ 80 % of the camp within 3 days.
        var runs = Enumerable.Range(1, 4).Select(s => RumorProbe.Run(Camp with { Seed = (ulong)s }, Content, "claim.assaulted", days: 3, witnesses: 0)).ToArray();
        runs.ShouldAllBe(r => r.Witnesses >= 3);
        runs.Count(r => r.HeardDay3 >= 0.8).ShouldBeGreaterThanOrEqualTo(3);
        runs.Average(r => r.HeardDay3).ShouldBeGreaterThan(0.85);
        runs.ShouldAllBe(r => r.Exchanges > 0 && r.Taken < r.Exchanges);   // some listeners doubt
    }

    [Fact]
    public void Gossip_SpreadsAndSometimesMutates_AndVariantsDeriveFromTheTruth()
    {
        var r = RumorProbe.Run(Camp with { Seed = 3 }, Content, "claim.dead", days: 4, witnesses: 3);
        r.FinalHeard.ShouldBeGreaterThan(0.5);
        r.Exchanges.ShouldBeGreaterThan(20);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
