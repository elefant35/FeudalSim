using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Crafting;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-12: 13 §9.6 felling a world tree and splitting logs; 11 §12.4 work accidents.</summary>
public sealed class WoodcuttingTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly Lazy<WorldMap> Map = new(() =>
    {
        using var jobs = new JobRunner(Environment.ProcessorCount);
        return WorldCache.GetOrGenerate(Content, "worldspec.farstrand_default", 49, Path.Combine(Path.GetTempPath(), "feudalsim-worldmap-tests"), jobs);
    });

    /// <summary>A camp world on the generated map, a settler standing at a timber tree, with an axe.</summary>
    private static (SimWorld W, int Row, int Chunk, int Index) AtATree(string axe = "item.iron_axe", byte size = NodeScatter.Timber)
    {
        var w = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")).CreateWorld(Content, SerialJobScheduler.Instance);
        w.AttachMap(Map.Value);
        w.Step();
        var g = Map.Value.Grid;
        var per = NodeScatter.ChunksPerSide(g);
        var nodes = new List<ResourceNode>();
        for (var chunk = (per * per / 2) + (per / 2); ; chunk++)
        {
            NodeScatter.Chunk(g, Map.Value.AttemptSeed, w.NodeTable, chunk % per, chunk / per, nodes);
            var k = nodes.FindIndex(n => Content.Nodes[n.Type].Kind == NodeKind.Tree && n.Size == size);
            if (k < 0) { continue; }
            var (x, z) = NodeScatter.Position(g, chunk % per, chunk / per, nodes[k]);
            const int row = 3;
            ref var t = ref w.People.Transforms[row];
            (t.X, t.Z) = (x + 1f, z);
            w.Holdings.Give(w.People.Ids[row], Content.ItemHandle(axe), 1);
            w.People.SkillLevels(row)[Content.SkillHandle("skill.woodcutting")] = 40;
            return (w, row, chunk, k);
        }
    }

    private static List<object> Run(SimWorld w, StateCommand c)
    {
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Scenario, c));
        return [.. w.Step().Events.Select(e => e.Payload)];
    }

    private static List<object> FinishAuto(SimWorld w, Process p)
    {
        var events = new List<object>();
        for (var guard = 0; guard < 200_000 && w.Processes.Get(p.Id) is not null; guard++)
        {
            if (p.State == ProcessState.Active && w.Clock.GameMinute >= p.BusyUntilMin && p.Stage < 4)
            {
                w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Scenario, new WorkStage(p.Id, float.NaN)));
            }

            events.AddRange(w.Step().Events.Select(e => e.Payload));
        }

        return events;
    }

    [Fact]
    public void FellingATimberTree_YieldsLogsAndBrash_AndTheTreeStaysDown()
    {
        var (w, row, chunk, index) = AtATree();
        var who = w.People.Ids[row];
        Run(w, new StartProcess(who, "recipe.fell_tree", SiteChunk: chunk, SiteIndex: index)).OfType<CommandRejected>().ShouldBeEmpty();
        var p = w.Processes.Open.Single();
        p.SiteSize.ShouldBe(NodeScatter.Timber);
        var events = FinishAuto(w, p);
        events.OfType<ProcessCompleted>().ShouldHaveSingleItem();
        w.Inventory.Count(who, Content.ItemHandle("item.rough_log")).ShouldBe(3);
        w.Inventory.Count(who, Content.ItemHandle("item.firewood")).ShouldBe(2);
        w.NodeDeltas.State(chunk, index).ShouldBe(Processes.NodeFelled);
        Run(w, new StartProcess(who, "recipe.fell_tree", SiteChunk: chunk, SiteIndex: index)).OfType<CommandRejected>().Single().Reason.ShouldContain("already worked");
    }

    [Fact]
    public void TheSiteIsChecked_KindReachAndNoWorld()
    {
        var (w, row, chunk, index) = AtATree();
        var who = w.People.Ids[row];
        Run(w, new StartProcess(who, "recipe.fell_tree")).OfType<CommandRejected>().Single().Reason.ShouldContain("no such node");
        ref var t = ref w.People.Transforms[row];
        t.X += 20f;
        Run(w, new StartProcess(who, "recipe.fell_tree", SiteChunk: chunk, SiteIndex: index)).OfType<CommandRejected>().Single().Reason.ShouldContain("out of reach");
        var camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")).CreateWorld(Content, SerialJobScheduler.Instance);
        camp.Step();
        camp.Holdings.Give(camp.People.Ids[1], Content.ItemHandle("item.iron_axe"), 1);
        Run(camp, new StartProcess(camp.People.Ids[1], "recipe.fell_tree", SiteChunk: chunk, SiteIndex: index)).OfType<CommandRejected>().Single().Reason.ShouldContain("no world");
    }

    [Fact]
    public void AStoneAxe_FellsSlower()   // 13 §9.6: stone ≈ ×2.5 (ToolSpeed 0.6 × TaskToolFactor 1.5 on the cuts)
    {
        float Labor(string axe)
        {
            var (w, row, chunk, index) = AtATree(axe);
            Run(w, new StartProcess(w.People.Ids[row], "recipe.fell_tree", SiteChunk: chunk, SiteIndex: index));
            var p = w.Processes.Open.Single();
            FinishAuto(w, p);
            return p.LaborMin;
        }

        (Labor("item.stone_axe") / Labor("item.iron_axe")).ShouldBeInRange(1.6f, 3.0f);
    }

    [Fact]
    public void SplittingALog_MakesFiveBundles()
    {
        var (w, row, _, _) = AtATree();
        var who = w.People.Ids[row];
        w.Inventory.Add(who, Content.ItemHandle("item.rough_log"), 1, 50);
        Run(w, new StartProcess(who, "recipe.split_firewood"));
        var p = w.Processes.Open.Single();
        Run(w, new WorkStage(p.Id, float.NaN));
        for (var s = 0; s < 20_000 && w.Processes.Count > 0; s++) { w.Step(); }
        w.Inventory.Count(who, Content.ItemHandle("item.firewood")).ShouldBe(5);
    }

    [Fact]
    public void WorkAccidents_FollowTheFormula()   // 11 §12.4 example: an expert feller, rested, ≈ 0.0058 per hour
    {
        Processes.AccidentPerHour("high", 70f, 80f, storm: false).ShouldBe(0.0058f, 0.0001f);
        Processes.AccidentPerHour("high", 0f, 20f, storm: false).ShouldBe(0.004f * 2.5f * 2f, 0.0001f);   // a novice, Exhausted
        Processes.AccidentPerHour("low", 100f, 80f, storm: true).ShouldBe(0.0005f * 1.5f, 0.00001f);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
