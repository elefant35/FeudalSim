using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Items;
using FeudalSim.Sim.Persistence;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-09: 13 §5 quality, flaws and appraisal, and the 20 §6.5 inventory store (conservation, determinism).</summary>
public sealed class ItemsTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp()
    {
        var w = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Player = [12f, -6f] })
            .CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    [Theory]   // 13 §5.3 calibration table: NPC median PS by margin, material 50 and 90
    [InlineData(50.0, 50, 43)]
    [InlineData(87.5, 50, 65)]    // the material ceiling 30 + 0.7·50
    [InlineData(68.8, 90, 67)]
    [InlineData(100.0, 90, 89)]   // 89 without a masterwork attempt
    [InlineData(12.5, 50, 11)]
    [InlineData(31.3, 90, 30)]
    public void ProcessQuality_MatchesTheCalibrationTable(double ps, int material, int q) => Quality.Process((float)ps, material).ShouldBe(q);

    [Fact]
    public void TheYewBow_WorkedExample()   // 13 §5.5
    {
        Quality.Process(55.3f, 71.6f).ShouldBe(51);   // NPC Bryn: Common
        Quality.Process(68.8f, 71.6f).ShouldBe(63);   // the player: Fine
        var hinge = Content.FlawHandle("flaw.hinge");
        var cap = Quality.FlawCap(1UL << hinge, Content.Flaws);
        cap.ShouldBe(55);
        Quality.Process(44.8f, 71.6f, flawCap: cap).ShouldBe(41);   // hinged: the lower PS binds, not the cap
        Quality.Process(80f, 71.6f, flawCap: cap).ShouldBe(55);     // …but a hinged bow never rises past 55
        Quality.Process(100f, 100f, masterworkAttempt: true).ShouldBe(100);
        Quality.Process(100f, 100f, knowHowAware: true).ShouldBe(50);
    }

    [Fact]
    public void WhatQualityDoes()   // 13 §5.6, §5.1
    {
        Quality.StatMult(0).ShouldBe(0.85f, 0.0001f);
        Quality.StatMult(50).ShouldBe(1f, 0.0001f);
        Quality.StatMult(100).ShouldBe(1.15f, 0.0001f);
        Quality.MaxDurability(100f, 50).ShouldBe(100f);
        Quality.MaxDurability(100f, 100).ShouldBe(150f);
        Quality.ValueMult(50).ShouldBe(1f);
        Quality.ValueMult(100).ShouldBe(4f);
        Quality.ShelfLifeMult(50).ShouldBe(1f, 0.0001f);
        (Quality.GradeOf(19), Quality.GradeOf(40), Quality.GradeOf(60), Quality.GradeOf(90)).ShouldBe((Grade.Crude, Grade.Common, Grade.Fine, Grade.Masterwork));
    }

    [Fact]
    public void Appraisal_IsStablePerViewer_AndSharperWithSkill()   // 13 §5.8
    {
        (Quality.AppraisalSigma(0f), Quality.AppraisalSigma(100f)).ShouldBe((15f, 2f));
        var viewer = EntityId.Make(EntityKind.Person, 7);
        Quality.Appraise(42, viewer, 1234, 20f, 60).ShouldBe(Quality.Appraise(42, viewer, 1234, 20f, 60));
        double Spread(float skill) => Enumerable.Range(0, 2000).Select(k => (double)Quality.Appraise(42, viewer, (ulong)k, skill, 60) - 60).Select(d => d * d).Average();
        Math.Sqrt(Spread(0f)).ShouldBe(15, 1.0);
        Math.Sqrt(Spread(100f)).ShouldBe(2, 0.3);
        Quality.SeeFlawChance(35f, 35).ShouldBe(0.777f, 0.001f);   // s − d + 10 = 10 → σ(1.25)
    }

    [Fact]
    public void StacksMerge_ByQuantityWeightedQuality()   // 13 §5.9
    {
        var inv = new InventoryStore();
        var c = EntityId.Make(EntityKind.Container, 1);
        inv.Add(c, 3, 10, 60);
        inv.Add(c, 3, 30, 40);
        (inv.Count(c, 3), inv.StackQ(c, 3)).ShouldBe((40, 45));
        InventoryStore.MergeQ(51, 1, 50, 1).ShouldBe((byte)51);   // 50.5 rounds away from zero, never banker's
    }

    [Fact]
    public void Inventory_ConservesGoods_StaysSorted_AndSurvivesSaveAndLoad()   // 20 §19 conservation
    {
        var w = Camp();
        var holders = new[] { w.People.Ids[0], w.People.Ids[1], EntityId.Make(EntityKind.Container, 1) };
        var items = new[] { Content.ItemHandle("item.firewood"), Content.ItemHandle("item.clay"), Content.ItemHandle("item.iron_knife"), Content.ItemHandle("item.wool_cloak") };
        var expected = new Dictionary<int, int>();
        var rng = new Rng(SplitMix64.Mix(99, 1, 2, 3, 4));
        SaveImage? image = null;
        for (var op = 0; op < 3000; op++)
        {
            var item = items[rng.Range(0, items.Length)];
            var a = holders[rng.Range(0, 3)];
            var b = holders[rng.Range(0, 3)];
            var qty = rng.Range(1, 6);
            switch (rng.Range(0, 4))
            {
                case 0:
                    if (Content.Items[item].IsStackable) { w.Inventory.Add(a, item, qty, rng.Range(0, 101)); }
                    else { for (var k = 0; k < qty; k++) { w.Inventory.Create(a, item, rng.Range(0, 101), 100f, w.Ids.Next(EntityKind.ItemInstance).Value); } }
                    expected[item] = expected.GetValueOrDefault(item) + qty;
                    break;
                case 1:
                    if (w.Inventory.Remove(a, item, qty)) { expected[item] -= qty; }
                    break;
                default:
                    w.Inventory.Move(a, b, item, qty);
                    break;
            }

            if (op == 1500) { image = SaveCodec.Capture(w); }
        }

        foreach (var item in items) { holders.Sum(h => w.Inventory.Count(h, item)).ShouldBe(expected.GetValueOrDefault(item), Content.Items[item].Id); }
        foreach (var h in holders)
        {
            var slots = w.Inventory.Of(h);
            slots.ShouldAllBe(s => s.Qty > 0);
            slots.Zip(slots.Skip(1)).ShouldAllBe(p => p.First.Item < p.Second.Item || (p.First.Item == p.Second.Item && p.First.Instance < p.Second.Instance));
        }

        var restored = SaveCodec.Restore(image!, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        StateHasher.Hash(restored).ShouldBe(SaveCodec.Capture(restored).Header.StateHash);
        restored.Inventory.InstanceCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Holdings_KeepTradeGoods_AsInstancesForToolsAndStacksForCommodities()
    {
        var w = Camp();
        var knife = Content.ItemHandle("item.iron_knife");
        var wood = Content.ItemHandle("item.firewood");
        var a = w.People.Ids[2];
        w.Holdings.Set(a, knife, 2, 100);
        w.Holdings.Set(a, wood, 5, 100);
        w.Holdings.Goods(a, knife).ShouldBe(2);
        var slots = w.Inventory.Of(a);
        slots.Count(s => s.Item == knife && s.Instance != 0).ShouldBe(2);
        var inst = w.Inventory.Instance(slots.First(s => s.Item == knife).Instance)!.Value;
        (inst.Q, inst.MaxDurability).ShouldBe(((byte)50, 300f));   // iron knife durability 300 at Q 50
        slots.Single(s => s.Item == wood).Qty.ShouldBe(5);
        w.Holdings.Take(a, w.PlayerId, knife, 1).ShouldBeTrue();
        (w.Holdings.Goods(a, knife), w.Holdings.Goods(w.PlayerId, knife)).ShouldBe((1, 1));
        w.Inventory.Mass(a, Content).ShouldBe((float)(0.25 + (5 * 15)), 0.001f);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
