using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.Time;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>Spike S6: tier cadences (20 §5.2, 21 §15) — LOD2 hourly, LOD3 daily, per-row dt — and their determinism.</summary>
public sealed class LodTierTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    /// <summary>A small mixed camp: 40 people, the last 16 at LOD2 and the last 8 of those at LOD3.</summary>
    private static ScenarioDef Mixed(int lod2 = 8, int lod3 = 8) => ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"))
        with { Settlers = 40, Tiers = new TierMix { Lod2 = lod2, Lod3 = lod3 } };

    private static void RunHours(SimWorld w, double hours)
    {
        var target = w.Clock.GameMs + (long)(hours * SimClock.MsPerGameHour);
        while (w.Clock.GameMs < target) { w.Step(); }
    }

    [Fact]
    public void Lod2RowsUpdateOncePerGameHour_Lod3OncePerDay_AtOffsetsSpreadOverTheHour()
    {
        var w = Mixed().CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();   // spawns + tier pins
        var people = w.People;
        people.Lod.ToArray().Count(l => l.Tier == LodTier.Lod2).ShouldBe(8);
        people.Lod.ToArray().Count(l => l.Tier == LodTier.Lod3).ShouldBe(8);

        var lod2Updates = new int[people.Count];
        var lod3Updates = new int[people.Count];
        var lod2Dt = new List<long>();
        var dueSteps = new HashSet<long>();
        var end = w.Clock.GameMs + (2 * SimClock.MsPerGameDay);
        while (w.Clock.GameMs < end)
        {
            w.Step();
            var due = w.Due;
            for (var k = 0; k < due.Count; k++)
            {
                var i = due.Rows[k];
                if (people.Lod[i].Tier == LodTier.Lod2) { lod2Updates[i]++; dueSteps.Add(w.Clock.Step); if (lod2Updates[i] > 1) { lod2Dt.Add(due.Dt(k)); } }
                if (people.Lod[i].Tier == LodTier.Lod3) { lod3Updates[i]++; }
            }
        }

        for (var i = 0; i < people.Count; i++)
        {
            if (people.Lod[i].Tier == LodTier.Lod2) { lod2Updates[i].ShouldBeInRange(47, 49); }   // 48 game hours ± the partial ends
            if (people.Lod[i].Tier == LodTier.Lod3) { lod3Updates[i].ShouldBe(2); }
        }

        lod2Dt.ShouldAllBe(dt => dt == SimClock.MsPerGameHour);   // whole hours, independent of the step size
        dueSteps.Count.ShouldBeGreaterThan(8 * 40);               // spread across the hour, not bunched on its boundary
    }

    [Fact]
    public void PromotionIntegratesTheGapSinceTheLastUpdate()
    {
        var w = Mixed(lod2: 16, lod3: 0).CreateWorld(Content, SerialJobScheduler.Instance);
        RunHours(w, 3.3);
        var row = w.People.Count - 1;
        var last = w.People.Lod[row].LastUpdateGameMs;
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Dev, new SetLodTier(w.People.Ids[row], LodTier.Lod1)));
        w.Step();
        var k = w.Due.Rows.IndexOf(row);
        k.ShouldBeGreaterThanOrEqualTo(0);
        w.Due.Dt(k).ShouldBe(w.Clock.GameMs - last);
        w.Due.Dt(k).ShouldBeGreaterThan(w.Clock.GameMsPerStep);
    }

    [Fact]
    public void MixedTiers_AreDeterministic_AcrossThreadCounts_AndSaveLoad()
    {
        var scenario = Mixed();
        var a = scenario.CreateWorld(Content, SerialJobScheduler.Instance);
        using var jobs = new JobRunner(4);
        var b = scenario.CreateWorld(Content, jobs);
        RunHours(a, 30);
        RunHours(b, 30);
        StateHasher.Hash(a).ShouldBe(StateHasher.Hash(b));

        var image = SaveCodec.Capture(a);
        RunHours(a, 20);
        var restored = SaveCodec.Restore(image, out _);
        restored.Content = Content;
        restored.AddSystem(new LodSystem()).AddSystem(new ActivitySystem()).AddSystem(new NeedsDecaySystem()).AddSystem(new PsychologySystem())
            .AddSystem(new Lod3System()).AddSystem(new SocialSystem()).AddSystem(new InteractionSystem());
        RunHours(restored, 20);
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(a));
    }

    [Fact]
    public void Lod3MacroDays_FeedPeopleFromTheStores_AndKeepNeedsAndMoodInRange()
    {
        var scenario = Mixed(lod2: 0, lod3: 40) with { MacroStep = "day" };
        var w = scenario.CreateWorld(Content, SerialJobScheduler.Instance);
        var food0 = w.Camp.Food;
        for (var d = 0; d < 6; d++) { w.StepMacro(SimClock.MsPerGameDay); }
        w.Clock.Step.ShouldBe(6);
        w.Camp.Food.ShouldNotBe(food0);
        foreach (var n in w.People.Needs.ToArray())
        {
            n.Satiety.ShouldBe(Lod3System.FedSatiety);   // the stores cover everyone
            n.Social.ShouldBeInRange(0f, 100f);
            n.Purpose.ShouldBeInRange(0f, 100f);
        }

        w.People.Mood.ToArray().Average(m => m.Smoothed).ShouldBeInRange(-10f, 30f);   // 21 §19 band
        w.Relationships.Count.ShouldBeGreaterThan(0);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
