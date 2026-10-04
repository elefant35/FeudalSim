using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-01b: biomes (stage 8) and resource nodes + T0 deposits (stage 9) on one generated world.</summary>
public sealed class NodesTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly Lazy<WorldGenResult> World = new(() =>
    {
        using var jobs = new JobRunner(Environment.ProcessorCount);
        return WorldGenerator.Attempt(Content.WorldSpec("worldspec.farstrand_default")!, 42, 0, 42, jobs);
    });

    private static readonly NodeScatter.Table Table = new(Content);

    [Fact]
    public void EveryLandCellHasABiome_AndSoilsFollowTheirBiomes()
    {
        var g = World.Value.Grid;
        for (var i = 0; i < g.Height.Length; i++)
        {
            if (g.Land[i] == 0 || g.Water[i] == (byte)WaterClass.Lake) { continue; }
            g.Biome[i].ShouldBeInRange((byte)1, (byte)8);
            if ((Biome)g.Biome[i] == Biome.RiverValley) { ((Soil)g.Soil[i]).ShouldBe(Soil.Alluvial); }
            if ((Biome)g.Biome[i] == Biome.Wetland) { ((Soil)g.Soil[i]).ShouldBe(Soil.PeatGley); }
        }

        World.Value.Biomes!.Shares.Values.Sum().ShouldBe(1f, 0.01f);
        World.Value.Biomes.BreadbasketHa.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void AChunkRegeneratesIdentically_InAnyOrder()
    {
        var w = World.Value;
        var a = new List<ResourceNode>();
        var b = new List<ResourceNode>();
        NodeScatter.Chunk(w.Grid, w.AttemptSeed, Table, 60, 64, a);
        NodeScatter.Chunk(w.Grid, w.AttemptSeed, Table, 10, 10, b);
        NodeScatter.Chunk(w.Grid, w.AttemptSeed, Table, 60, 64, b);
        b.ShouldBe(a);
        NodeScatter.Chunk(w.Grid, w.AttemptSeed ^ 1, Table, 60, 64, b);
        b.ShouldNotBe(a);
    }

    [Fact]
    public void Densities_EdgeOnlyAndRockRules_Hold()
    {
        var w = World.Value;
        var g = w.Grid;
        var per = NodeScatter.ChunksPerSide(g);
        var buffer = new List<ResourceNode>();
        var counts = new long[Content.Nodes.Count];
        var expected = new double[Content.Nodes.Count];
        var oak = Content.NodeHandle("node.oak");
        var yew = Content.NodeHandle("node.yew");
        var hawthorn = Content.NodeHandle("node.hawthorn");
        for (var cz = 0; cz < per; cz += 2)
        {
            for (var cx = 0; cx < per; cx += 2)
            {
                NodeScatter.Chunk(g, w.AttemptSeed, Table, cx, cz, buffer);
                foreach (var node in buffer)
                {
                    counts[node.Type]++;
                    var (x, z) = NodeScatter.Position(g, cx, cz, node);
                    var col = (int)MathF.Round((x + ((g.Size - 1) * g.CellM / 2f) - (node.X % (8 * NodeScatter.Units) / (float)NodeScatter.Units)) / g.CellM);
                    var row = (int)MathF.Round((z + ((g.Size - 1) * g.CellM / 2f) - (node.Z % (8 * NodeScatter.Units) / (float)NodeScatter.Units)) / g.CellM);
                    var i = (row * g.Size) + col;
                    if (node.Type == yew) { ((Lithology)g.Lithology[i]).ShouldBe(Lithology.Chalk); }
                    if (node.Type == hawthorn)
                    {
                        var b = g.Biome[i];
                        new[] { g.Biome[i - 1], g.Biome[i + 1], g.Biome[i - g.Size], g.Biome[i + g.Size] }.ShouldContain(x2 => x2 != 0 && x2 != b);
                    }
                }

                // The expectation for oak over the same cells (no edge or rock rules for oak).
                for (var lr = 0; lr < 8; lr++)
                {
                    for (var lc = 0; lc < 8; lc++)
                    {
                        int col = (cx * 8) + lc, row = (cz * 8) + lr;
                        if (col >= g.Size - 1 || row >= g.Size - 1) { continue; }
                        var i = (row * g.Size) + col;
                        if (g.Land[i] == 0 || g.Biome[i] == 0 || g.Water[i] is (byte)WaterClass.Lake or (byte)WaterClass.River) { continue; }
                        expected[oak] += Content.Nodes[oak].Density.GetValueOrDefault(Biomes.Keys[g.Biome[i]]) * 0.0064;
                    }
                }
            }
        }

        expected[oak].ShouldBeGreaterThan(1000);
        (counts[oak] / expected[oak]).ShouldBe(1.0, 0.03);   // the fixed-point draws match the content density
    }

    [Fact]
    public void T0Deposits_FallInTheirRanges_RulesAndSpacing()
    {
        var w = World.Value;
        var g = w.Grid;
        var flint = w.Deposits.Where(d => d.Kind == DepositKind.FlintBed).ToList();
        var clay = w.Deposits.Where(d => d.Kind == DepositKind.ClayPit).ToList();
        var quarry = w.Deposits.Where(d => d.Kind == DepositKind.Quarry).ToList();
        flint.Count.ShouldBeInRange(3, 8);
        clay.Count.ShouldBeInRange(4, 10);
        quarry.Count.ShouldBeInRange(2, 5);
        int Cell(Deposit d) => ((int)MathF.Round((d.Z + ((g.Size - 1) * g.CellM / 2f)) / g.CellM) * g.Size) + (int)MathF.Round((d.X + ((g.Size - 1) * g.CellM / 2f)) / g.CellM);
        foreach (var d in flint) { ((Lithology)g.Lithology[Cell(d)]).ShouldBe(Lithology.Chalk); }
        foreach (var group in new[] { flint, clay, quarry })
        {
            foreach (var a in group)
            {
                foreach (var b in group.Where(b => b.Id != a.Id)) { MathF.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z))).ShouldBeGreaterThanOrEqualTo(400f); }
            }
        }
    }

    [Fact]
    public void TheW14Helper_CountsHarvestableTreesAndForage()
    {
        var w = World.Value;
        var g = w.Grid;
        var (x, z) = (0f, 0f);
        for (var i = 0; i < g.Height.Length; i++)   // the first broadleaf cell near the middle
        {
            if ((Biome)g.Biome[i] == Biome.Broadleaf && Math.Abs(g.X(i % g.Size)) < 1500 && Math.Abs(g.Z(i / g.Size)) < 1500) { (x, z) = (g.X(i % g.Size), g.Z(i / g.Size)); break; }
        }

        var (trees, forage) = NodeScatter.Around(g, w.AttemptSeed, Table, x, z, 600f, Content);
        trees.ShouldBeGreaterThanOrEqualTo(200);   // 10 §3.11 W14 near broadleaf
        forage.ShouldBeGreaterThan(0);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
