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

/// <summary>M2-13: 13 §9.5 foraging, 11 §8.2 misidentification and second chances, 10 §7.4 regrowth.</summary>
public sealed class ForagingTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly Lazy<WorldMap> Map = new(() =>
    {
        using var jobs = new JobRunner(Environment.ProcessorCount);
        return WorldCache.GetOrGenerate(Content, "worldspec.farstrand_default", 49, Path.Combine(Path.GetTempPath(), "feudalsim-worldmap-tests"), jobs);
    });

    private static SimWorld OnTheMap(string start = "Y0 Summer 3 10:00")
    {
        var w = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Start = start }).CreateWorld(Content, SerialJobScheduler.Instance);
        w.AttachMap(Map.Value);
        w.Step();
        return w;
    }

    /// <summary>Every node of a type in the map's middle third (chunk, index, position).</summary>
    private static List<(int Chunk, int Index, float X, float Z)> Find(SimWorld w, string nodeId, int max = 400)
    {
        var g = Map.Value.Grid;
        var per = NodeScatter.ChunksPerSide(g);
        var type = Content.NodeHandle(nodeId);
        var found = new List<(int, int, float, float)>();
        var nodes = new List<ResourceNode>();
        for (var cz = per / 3; cz < 2 * per / 3 && found.Count < max; cz++)
        {
            for (var cx = per / 3; cx < 2 * per / 3 && found.Count < max; cx++)
            {
                NodeScatter.Chunk(g, Map.Value.AttemptSeed, w.NodeTable, cx, cz, nodes);
                for (var k = 0; k < nodes.Count; k++)
                {
                    if (nodes[k].Type != type) { continue; }
                    var (x, z) = NodeScatter.Position(g, cx, cz, nodes[k]);
                    found.Add(((cz * per) + cx, k, x, z));
                }
            }
        }

        return found;
    }

    private static List<object> GoAndGather(SimWorld w, int row, (int Chunk, int Index, float X, float Z) n)
    {
        ref var t = ref w.People.Transforms[row];
        (t.X, t.Z) = (n.X + 1f, n.Z);
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Scenario, new Forage(w.People.Ids[row], n.Chunk, n.Index)));
        return [.. w.Step().Events.Select(e => e.Payload)];
    }

    [Fact]
    public void TheIdChance_FollowsTheFormula()   // 11 §8.2
    {
        Foraging.CorrectId(0f, 5f, 0.15f).ShouldBe(0.5f);            // clamped: a novice is a coin toss
        Foraging.CorrectId(70f, 6f, 0.15f).ShouldBe(0.75f, 0.001f);
        Foraging.CorrectId(100f, 10f, 0f).ShouldBe(0.995f);
    }

    [Fact]
    public void Gathering_YieldsProduce_HarvestsTheNode_AndRespectsTheSeason()
    {
        var w = OnTheMap();
        var nettle = Find(w, "node.nettle", 1).Single();
        GoAndGather(w, 2, nettle).OfType<Foraged>().ShouldHaveSingleItem();
        w.Inventory.Count(w.People.Ids[2], Content.ItemHandle("item.nettles")).ShouldBe(2);
        w.NodeDeltas.State(nettle.Chunk, nettle.Index).ShouldBe(Foraging.NodeHarvested);
        GoAndGather(w, 2, nettle).OfType<CommandRejected>().Single().Reason.ShouldContain("picked over");
        var mushroom = Find(w, "node.field_mushroom", 1);
        if (mushroom.Count > 0) { GoAndGather(w, 2, mushroom[0]).OfType<CommandRejected>().Single().Reason.ShouldContain("out of season"); }   // autumn only
    }

    [Fact]
    public void PickingHemlock_ANoviceOftenTakesItForWildCarrot_AndAnExpertCookCatchesIt()
    {
        var w = OnTheMap();
        var hemlock = Find(w, "node.hemlock", 80);
        hemlock.Count.ShouldBeGreaterThan(20);
        var forager = 4;
        w.People.SkillLevels(forager)[Content.SkillHandle("skill.foraging")] = 0;
        foreach (var n in hemlock) { GoAndGather(w, forager, n); }
        var who = w.People.Ids[forager];
        var asCarrot = w.Inventory.CountSeen(who, Content.ItemHandle("item.wild_carrot"));
        var trueHemlock = w.Inventory.Count(who, Content.ItemHandle("item.hemlock"));
        trueHemlock.ShouldBe(2 * hemlock.Count);
        (asCarrot / (double)trueHemlock).ShouldBeInRange(0.3, 0.7);   // 1 − p_correct ≈ 0.5 for a novice
        w.Inventory.Count(who, Content.ItemHandle("item.wild_carrot")).ShouldBe(0);   // none of it is really carrot

        var cook = 5;
        w.People.SkillLevels(cook)[Content.SkillHandle("skill.foraging")] = 90;
        ref var tc = ref w.People.Transforms[cook];
        tc = w.People.Transforms[forager];
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Scenario, new InspectItem(w.People.Ids[cook], who, "item.wild_carrot")));
        var inspected = w.Step().Events.Select(e => e.Payload).OfType<ItemInspected>().Single();
        (inspected.Corrected == asCarrot || inspected.Corrected == 0).ShouldBeTrue();   // one roll for the stack
        if (inspected.Corrected > 0) { w.Inventory.CountSeen(who, Content.ItemHandle("item.wild_carrot")).ShouldBe(0); }
    }

    [Fact]
    public void PickedPlants_GrowBackOnSpring1()
    {
        var w = OnTheMap("Y0 Winter 8 23:55");
        w.NodeDeltas.Set(10, 3, Foraging.NodeHarvested);
        w.NodeDeltas.Set(10, 4, Processes.NodeFelled);
        for (var s = 0; s < 200; s++) { w.Step(); }   // past midnight into Y1 Spring 1
        w.NodeDeltas.State(10, 3).ShouldBe((byte)0);
        w.NodeDeltas.State(10, 4).ShouldBe(Processes.NodeFelled);   // felled trees don't regrow in a year
    }

    [Fact]
    public void StacksDontMerge_AcrossLabels()
    {
        var w = OnTheMap();
        var who = w.People.Ids[1];
        int carrot = Content.ItemHandle("item.wild_carrot"), hemlock = Content.ItemHandle("item.hemlock");
        w.Inventory.Add(who, hemlock, 2, 50, carrot);
        w.Inventory.Add(who, hemlock, 3, 50);
        w.Inventory.Of(who).Count(s => s.Item == hemlock).ShouldBe(2);
        w.Inventory.CountSeen(who, carrot).ShouldBe(2);
        w.Inventory.Relabel(who, hemlock, carrot, -1).ShouldBe(2);
        w.Inventory.Of(who).Single(s => s.Item == hemlock).Qty.ShouldBe(5);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
