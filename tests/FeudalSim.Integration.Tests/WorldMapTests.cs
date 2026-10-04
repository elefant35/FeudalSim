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
