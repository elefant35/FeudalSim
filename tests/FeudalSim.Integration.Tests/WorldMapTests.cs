using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.World;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-02: the generated region in the sim — codec, cache, save and hash, node deltas.</summary>
public sealed class WorldMapTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "feudalsim-worldmap-tests");
    private static readonly Lazy<WorldMap> Map = new(() =>
    {
        using var jobs = new JobRunner(Environment.ProcessorCount);
        return WorldCache.GetOrGenerate(Content, "worldspec.farstrand_default", 49, CacheDir, jobs);
    });

    [Fact]
    public void TheCamp_AnchorsAboveTheLandingBeach_OnDryLand_WithFreshWaterNearby()   // M2-FP1 (Hosting/CampAnchor)
    {
        var map = Map.Value;
        var g = map.Grid;
        var scenario = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m2_landfall.yaml"));
        var a = scenario.Anchor(map).ShouldNotBeNull();
        var landing = map.Landing.ShouldNotBeNull();
        var fire = CampAnchor.Cell(g, a.FireX, a.FireZ);
        g.Land[fire].ShouldBe((byte)1);
        g.CoastDistM[fire].ShouldBeInRange(CampAnchor.MinCoastM, CampAnchor.MaxCoastM);
        g.Slope[fire].ShouldBeLessThanOrEqualTo((byte)CampAnchor.MaxSlopeDeg);
        MathF.Sqrt(((a.FireX - landing.BeachX) * (a.FireX - landing.BeachX)) + ((a.FireZ - landing.BeachZ) * (a.FireZ - landing.BeachZ))).ShouldBeLessThan(600f);
        foreach (var (key, p) in a.Places) { g.Land[CampAnchor.Cell(g, p[0], p[1])].ShouldBe((byte)1, key); }
        a.WaterSnapped.ShouldBeTrue();   // 10 §3.9 guarantees fresh water ≤ 400 m of the beach
        var w = a.Places["water"];
        MathF.Sqrt(((w[0] - a.FireX) * (w[0] - a.FireX)) + ((w[1] - a.FireZ) * (w[1] - a.FireZ))).ShouldBeLessThanOrEqualTo(CampAnchor.WaterSnapM);
        scenario.PlayerStart(a).ShouldBe([12f + a.Dx, -6f + a.Dz]);
        var again = scenario.Anchor(map)!;   // a pure function of the map
        (again.FireX, again.FireZ, again.Dx, again.Dz).ShouldBe((a.FireX, a.FireZ, a.Dx, a.Dz));
        foreach (var (key, p) in a.Places) { again.Places[key].ShouldBe(p, key); }
    }

    [Fact]
    public void TheCodec_RoundTripsEveryGridAndRecord()
    {
        var map = Map.Value;
        var back = WorldMap.Decode(map.Encode());
        back.Fingerprint.ShouldBe(map.Fingerprint);
        back.Grid.CoastDistM.ShouldBe(map.Grid.CoastDistM);
        back.Landing.ShouldBe(map.Landing);
        back.Pois.ShouldBe(map.Pois);
        back.Deposits.ShouldBe(map.Deposits);
        Should.Throw<InvalidDataException>(() => WorldMap.Decode([1, 2, 3, 4]));
    }

    [Fact]
    public void TheCache_ServesTheSameWorld_WithoutRegenerating()
    {
        var first = Map.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var again = WorldCache.GetOrGenerate(Content, "worldspec.farstrand_default", 49, CacheDir);
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(3);   // a world takes ≈ 17 s to generate
        again.Fingerprint.ShouldBe(first.Fingerprint);
    }

    [Fact]
    public void AWorldWithAMap_SavesItsGridsAndDeltas_AndRestoresIdentically()
    {
        var w = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")).CreateWorld(Content, SerialJobScheduler.Instance);
        w.AttachMap(Map.Value);
        w.NodeDeltas.Set(4242, 3, 2);
        w.NodeDeltas.Set(4242, 7, 1);
        w.NodeDeltas.Set(4242, 7, 0);   // a cleared delta disappears
        w.NodeDeltas.Count.ShouldBe(1);
        for (var s = 0; s < 300; s++) { w.Step(); }
        var image = SaveCodec.Capture(w);
        var restored = SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        restored.Map!.Fingerprint.ShouldBe(Map.Value.Fingerprint);
        restored.NodeDeltas.State(4242, 3).ShouldBe((byte)2);
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(w));
        var nodes = new List<ResourceNode>();
        NodeScatter.Chunk(restored.Map.Grid, restored.Map.AttemptSeed, new NodeScatter.Table(Content), 4242 % 128, 4242 / 128, nodes);
        restored.NodeDeltas.Apply(4242, nodes);
        if (nodes.Count > 3) { nodes[3].State.ShouldBe((byte)2); }
        Should.Throw<InvalidOperationException>(() => restored.AttachMap(WorldMap.Decode(TamperedCopy(Map.Value))));
    }

    private static byte[] TamperedCopy(WorldMap map)
    {
        var bytes = map.Encode();
        bytes[200] ^= 0x40;   // inside the height grid
        return bytes;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
