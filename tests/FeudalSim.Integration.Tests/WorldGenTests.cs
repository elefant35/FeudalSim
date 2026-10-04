using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-01a (i): 10 §3 stages 1–2 — deterministic, parallel == serial, and the W1/W5 bands hold across seeds.</summary>
public sealed class WorldGenTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly WorldSpecDef Spec = Content.WorldSpec("worldspec.farstrand_default")!;

    private static ulong Hash(WorldGrid g)
    {
        var h = new System.IO.Hashing.XxHash64();
        h.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(g.Height.AsSpan()));
        h.Append(g.Land);
        return h.GetCurrentHashAsUInt64();
    }

    [Fact]
    public void SameSeed_SameWorld_InParallelOrNot()
    {
        // One attempt each (Debug is slow; `worldgen --seeds 20` in CI covers retries and spread).
        var serial = WorldGenerator.Attempt(Spec, 42, 0, 42, SerialJobScheduler.Instance);
        using var jobs = new JobRunner(4);
        var parallel = WorldGenerator.Attempt(Spec, 42, 0, 42, jobs);
        parallel.Archetype.ShouldBe(serial.Archetype);
        Hash(parallel.Grid).ShouldBe(Hash(serial.Grid));
        parallel.Grid.Water.ShouldBe(serial.Grid.Water);
        parallel.Grid.Lithology.ShouldBe(serial.Grid.Lithology);
        Hash(WorldGenerator.Attempt(Spec, 44, 0, 44, jobs).Grid).ShouldNotBe(Hash(serial.Grid));
    }

    [Fact]
    public void FirstAttempts_MeetTheFinishedStagesAsserts_AndHaveAnEstuary()
    {
        using var jobs = new JobRunner(Environment.ProcessorCount);
        var checkedWorlds = 0;
        for (var seed = 42UL; seed < 60 && checkedWorlds < 2; seed++)
        {
            var w = WorldGenerator.Attempt(Spec, seed, 0, seed, jobs);
            if (!w.CoreValid) { continue; }   // retries are worldgen's job
            checkedWorlds++;
            w.LandAreaKm2.ShouldBeInRange(35f, 45f);
            w.PeakM.ShouldBeInRange(900f, 1250f);
            w.Islets.ShouldBeInRange(3, 10);
            w.Grid.Land[0].ShouldBe((byte)0);   // the region border is sea
            w.Water!.Lakes.ShouldAllBe(l => l.AreaHa >= Hydrology.LakeMinHa && l.VolumeM3 >= Hydrology.LakeMinM3);
            w.Coast!.Estuaries.Single(e => e.Primary).MouthWidthM.ShouldBeInRange(300f, 900f);
            w.Grid.Water.ShouldContain((byte)WaterClass.Brackish);
        }

        checkedWorlds.ShouldBe(2);
    }

    [Fact]
    public void Tides_FollowTenSixHalfDailyWithLowWaterAtNineThirtyOnLandfall()
    {
        Tides.Level(9 * 60 + 30).ShouldBe(-Tides.SpringRangeM / 2, 0.01);                  // low water, springs on day 0
        Tides.Level((long)((9.5 + (Tides.PeriodH / 2)) * 60)).ShouldBe(Tides.SpringRangeM / 2, 0.02);   // high water half a period later
        var neapLow = Tides.Level((long)(((Tides.LunarDays / 2 * 24) + 9.5) * 60));
        Math.Abs(neapLow).ShouldBeLessThan(Tides.SpringRangeM / 2);                        // a smaller range at neaps
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
