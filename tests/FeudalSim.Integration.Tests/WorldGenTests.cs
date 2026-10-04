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
        var serial = WorldGenerator.Generate(Spec, 42, SerialJobScheduler.Instance);
        using var jobs = new JobRunner(4);
        var parallel = WorldGenerator.Generate(Spec, 42, jobs);
        serial.Valid.ShouldBeTrue(string.Join("; ", serial.Failures));
        (parallel.Attempt, parallel.Archetype).ShouldBe((serial.Attempt, serial.Archetype));
        Hash(parallel.Grid).ShouldBe(Hash(serial.Grid));
        Hash(WorldGenerator.Generate(Spec, 42, jobs).Grid).ShouldBe(Hash(serial.Grid));
        Hash(WorldGenerator.Attempt(Spec, 44, 0, 44, jobs).Grid).ShouldNotBe(Hash(serial.Grid));
    }

    [Fact]
    public void EverySeed_MeetsTheLandmassAndReliefAsserts()
    {
        using var jobs = new JobRunner(Environment.ProcessorCount);
        var archetypes = new HashSet<ReliefArchetype>();
        foreach (var seed in new ulong[] { 44, 49, 53 })   // 3 here; CI runs `worldgen --seeds 20` in Release
        {
            var w = WorldGenerator.Generate(Spec, seed, jobs);
            w.Valid.ShouldBeTrue($"seed {seed}: {string.Join("; ", w.Failures)}");
            w.LandAreaKm2.ShouldBeInRange(35f, 45f);
            w.PeakM.ShouldBeInRange(900f, 1250f);
            w.Islets.ShouldBeInRange(3, 10);
            w.AreaAbove800Km2.ShouldBeGreaterThanOrEqualTo(0.2f);
            w.Grid.Land[0].ShouldBe((byte)0);   // the region border is sea
            archetypes.Add(w.Archetype);
        }

        archetypes.Count.ShouldBeGreaterThan(1);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
