using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.Time;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-02a: the utility AI on the graybox camp (21 §7, §9; 11 §2.1, §3.2).</summary>
public sealed class UtilityAiTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"));

    private static SimWorld NewCamp(ulong? seed = null) => (seed is { } s ? Camp with { Seed = s } : Camp).CreateWorld(Content, SerialJobScheduler.Instance);

    private static void RunUntilMinute(SimWorld w, int day, int minuteOfDay)
    {
        var target = ((day * 1440L) + minuteOfDay) * SimClock.MsPerGameMinute;
        while (w.Clock.GameMs < target) { w.Step(); }
    }

    private static string ActionOf(SimWorld w, int row) => w.People.Activity[row].Action < 0 ? "-" : Content.Actions[w.People.Activity[row].Action].Id;

    [Fact]
    public void Urgency_MatchesThe21WorkedExample()
    {
        ActivitySystem.Urgency("satiety", 42).ShouldBe(0.44f, 0.01f);   // eat_meal W = 1 + 4·0.44 = 2.76
        ActivitySystem.Urgency("social", 38).ShouldBe(0.55f, 0.01f);    // chat W = 0.6 + 2·0.55 = 1.70
    }

    [Fact]
    public void TheCampLivesOnItsOwn_NeedsHealthy_MoodInBand_Deterministic()
    {
        var w = NewCamp();
        var startFood = w.Camp.Food;
        var lowNeedSamples = 0;
        var samples = 0;
        for (var day = 0; day < 4; day++)
        {
            for (var h = 0; h < 24; h++)
            {
                RunUntilMinute(w, day, (h * 60) + 30);
                foreach (var n in w.People.Needs.ToArray())
                {
                    samples++;
                    if (n.Satiety < 15 || n.Hydration < 15 || n.Energy < 15) { lowNeedSamples++; }
                }
            }
        }

        (lowNeedSamples / (double)samples).ShouldBeLessThan(0.03);                 // 21 §19 need health: < 3% of agent-hours
        w.People.Mood.ToArray().Average(m => m.Smoothed).ShouldBeInRange(-10, 30); // 21 §19 mean mood
        w.Camp.Food.ShouldBeGreaterThan(0f);
        w.Camp.Food.ShouldBeLessThan(startFood + 20_000f);
        w.Camp.Firewood.ShouldBeGreaterThan(0f);                                   // people went out for wood

        var again = NewCamp();
        RunUntilMinute(again, 3, (23 * 60) + 30);   // the same moment the loop above ended
        StateHasher.Hash(again).ShouldBe(StateHasher.Hash(w));
    }

    [Fact]
    public void PeopleFollowTheScheduleSoftly_SleepAtNight_WorkMidMorning()
    {
        var w = NewCamp();
        RunUntilMinute(w, 1, 23 * 60);   // 23:00, sleep block
        var asleep = Enumerable.Range(0, w.People.Count).Count(i => ActionOf(w, i) == "action.sleep");
        (asleep / 24.0).ShouldBeGreaterThanOrEqualTo(0.75);

        RunUntilMinute(w, 2, (10 * 60) + 30);   // 10:30, work block
        var working = Enumerable.Range(0, w.People.Count).Count(i => w.People.Activity[i].Has(ActivityState.Purposeful));
        (working / 24.0).ShouldBeGreaterThanOrEqualTo(0.5);
    }

    [Fact]
    public void ACriticalNeedInterruptsWork()
    {
        var w = NewCamp();
        RunUntilMinute(w, 1, (10 * 60) + 30);
        var row = Enumerable.Range(0, w.People.Count).First(i => w.People.Activity[i].Has(ActivityState.Purposeful) && w.People.Activity[i].Phase == 1);
        w.People.Needs[row].Hydration = 10;   // critical (< 20) → drink becomes P1
        RunUntilMinute(w, 1, (10 * 60) + 35);
        ActionOf(w, row).ShouldBe("action.drink");
    }

    [Fact]
    public void LazyPeopleWorkLessThanIndustriousOnes()
    {
        var w = NewCamp(7);
        w.Step();   // spawns apply on the first step
        w.People.Count.ShouldBe(24);
        var lazy = Content.TraitHandle("trait.lazy");
        var industrious = Content.TraitHandle("trait.industrious");
        for (var i = 0; i < w.People.Count; i++)
        {
            ref var p = ref w.People.Personality[i];
            p.Traits = i % 2 == 0 ? 1UL << lazy : 1UL << industrious;
            p.Diligence = 50;
        }

        var work = new double[2];
        for (var day = 0; day < 3; day++)
        {
            for (var m = 8 * 60; m < 18 * 60; m += 20)
            {
                RunUntilMinute(w, day, m);
                for (var i = 0; i < w.People.Count; i++) { if (w.People.Activity[i].Has(ActivityState.Purposeful)) { work[i % 2]++; } }
            }
        }

        work[0].ShouldBeLessThan(work[1] * 0.8);   // lazy (work ×0.6, idle ×1.6) vs industrious (work ×1.4)
    }

    [Fact]
    public void SaveLoadMidCamp_ContinuesIdentically()
    {
        var straight = NewCamp();
        RunUntilMinute(straight, 1, 12 * 60);
        var image = SaveCodec.Capture(straight);
        RunUntilMinute(straight, 2, 6 * 60);

        var restored = SaveCodec.Restore(image, out _);
        restored.Content = Content;
        restored.AddSystem(new LodSystem()).AddSystem(new ActivitySystem()).AddSystem(new NeedsDecaySystem()).AddSystem(new PsychologySystem());
        restored.Camp.Active.ShouldBe((byte)1);
        RunUntilMinute(restored, 2, 6 * 60);
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(straight));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}

/// <summary>M1-02b: the 21 §19 camp metrics, on a few seeds (the full 100-seed sweep runs via `feudalsim sweep` and CI).</summary>
public sealed class CampMetricsTests
{
    [Fact]
    public void ThreeSeeds_TenDays_MetricsInTheirBands()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "FeudalSim.sln"))) { root = root.Parent!; }
        var content = ContentCompiler.Compile(Path.Combine(root.FullName, "content")).Database!;
        var camp = ScenarioDef.Load(Path.Combine(root.FullName, "content", "scenarios", "m1_camp.yaml")) with { Days = 10 };
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var run = ScenarioRunner.Run(camp with { Seed = seed }, content, threads: 1);
            var s = run.Camp.ShouldNotBeNull();
            s.LowNeedShare.ShouldBeLessThan(0.03);
            s.MoodMean.ShouldBeInRange(-10, 30);
            s.BreakingShare.ShouldBeLessThan(0.05);
            s.Divergence.ShouldBeGreaterThanOrEqualTo(0.15);
            s.TaskFailure.ShouldBeLessThan(0.05);
            s.IdleRate.ShouldBeInRange(0.05, 0.35);   // band 0.10–0.25 holds for ≥ 90% of seeds; a single seed may sit just outside
            run.Days.ShouldAllBe(d => d.Camp != null);
        }
    }
}
