using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Health;
using FeudalSim.Sim.Survival;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-07b-i: 11 §10.2–10.3 eating and nutrition, §8.1 toxins by dose, raw food's poisoning.</summary>
public sealed class EatingTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp()
    {
        var w = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")).CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    private static int H(string id) => Content.ItemHandle(id);

    [Fact]
    public void Bread_FillsSatiety_CappedAt100_AndCountsAsStaple()   // 11 §10.2: 1 kg bread = 100 Sat
    {
        var w = Camp();
        const int row = 4;
        var id = w.People.Ids[row];
        w.Inventory.Add(id, H("item.bread_loaf"), 2);
        w.People.Needs[row].Satiety = 30f;
        Eating.Feed(w, row, H("item.bread_loaf")).ShouldBeNull();
        w.People.Needs[row].Satiety.ShouldBe(100f);   // 30 + 100, the surplus wasted
        w.Inventory.Count(id, H("item.bread_loaf")).ShouldBe(1);
        w.People.Diet[row].Staple.ShouldBeGreaterThan(0f);
        Eating.Groups(w.People.Diet[row]).ShouldBe(1);   // only bread so far (beyond the camp's provisions, none yet this step)
    }

    [Fact]
    public void Hemlock_LabelledWildCarrot_IsEatenForWhatItIs()   // 11 §8.2: the stack gives up what it truly is
    {
        var w = Camp();
        const int row = 6;
        var id = w.People.Ids[row];
        w.Inventory.Add(id, H("item.hemlock"), 1, label: H("item.wild_carrot"));
        w.Inventory.CountSeen(id, H("item.wild_carrot")).ShouldBe(1);
        w.People.Needs[row].Satiety = 50f;
        Eating.Feed(w, row, H("item.wild_carrot")).ShouldBeNull();
        var c = w.Conditions.Of(id).ShouldHaveSingleItem();
        c.Disease.ShouldBe(Content.DiseaseHandle("disease.toxin_hemlock"));
        c.DosePct.ShouldBe((byte)100);
        w.People.Needs[row].Satiety.ShouldBe(61f);   // it still fills you: 11 Sat (the root's own food value)
    }

    [Fact]
    public void ToxinFatality_ScalesWithDose_BelowALethalDose()   // 11 §8.1: severity = 100 × amount / lethal dose
    {
        var w = Camp();
        const int row = 2;
        var toxin = Content.DiseaseHandle("disease.toxin_death_cap");
        Conditions.Poison(w, row, toxin, 0.5f, 1).ShouldBeTrue();
        var half = w.Conditions.Of(w.People.Ids[row])[0];
        half.DosePct.ShouldBe((byte)50);
        Conditions.DoseFactor(half).ShouldBe(0.5f);
        Conditions.Poison(w, row, toxin, 0.75f, 2).ShouldBeTrue();   // a second helping adds to the course
        var more = w.Conditions.Of(w.People.Ids[row]).ShouldHaveSingleItem();
        more.DosePct.ShouldBe((byte)125);
        Conditions.DoseFactor(more).ShouldBe(1f);
    }

    [Fact]
    public void RawFish_GivesLess_AndPoisonsAbout8Percent()   // 11 §10.2: raw ×0.85, food poisoning p 0.08
    {
        var (poisoned, n) = (0, 0);
        var poison = Content.DiseaseHandle("disease.food_poisoning");
        for (var world = 0; world < 10; world++)
        {
            var w = Camp();
            for (var k = 0; k < world * 7; k++) { w.Step(); }   // a different minute per world keys different draws
            for (var row = 0; row < w.People.Count; row++)
            {
                var id = w.People.Ids[row];
                w.Inventory.Add(id, H("item.fish_fresh"), 1);
                w.People.Needs[row].Satiety = 10f;
                Eating.Feed(w, row, H("item.fish_fresh")).ShouldBeNull();
                w.People.Needs[row].Satiety.ShouldBe(10f + (55f * 0.85f), 0.01f);
                n++;
                if (w.Conditions.Has(id, poison)) { poisoned++; }
            }
        }

        n.ShouldBe(240);
        ((float)poisoned / n).ShouldBeInRange(0.03f, 0.14f);   // 0.08 ± 3σ
    }

    [Fact]
    public void TheCampsMeals_ArePlain_AndGreensMakeThemVaried()   // 11 §10.3: groups with ≥ 15 % share
    {
        var w = Camp();
        for (var s = 0; s < 750 * 12; s++) { w.Step(); }   // half a day of communal meals (provisions: staple + protein)
        var fed = Enumerable.Range(0, w.People.Count).Where(r => w.People.Diet[r].Staple > 0f).ToList();
        fed.Count.ShouldBeGreaterThan(10);
        foreach (var r in fed) { Eating.Groups(w.People.Diet[r]).ShouldBe(2); }

        var row = fed[0];
        var id = w.People.Ids[row];
        w.Inventory.Add(id, H("item.blackberries"), 20);
        var d = w.People.Diet[row];
        while (d.Fresh / (d.Staple + d.Protein + d.Fresh) < Eating.GroupShare)
        {
            w.People.Needs[row].Satiety = 0f;
            Eating.Feed(w, row, H("item.blackberries")).ShouldBeNull();
            d = w.People.Diet[row];
        }

        Eating.Groups(d).ShouldBe(3);
    }

    [Fact]
    public void YouCantEat_WhatIsntFood_OrWhatYouDontHave_OrAsSomeoneElse()
    {
        var w = Camp();
        const int row = 1;
        var id = w.People.Ids[row];
        w.Inventory.Add(id, H("item.comfrey"), 1);
        Eating.Feed(w, row, H("item.comfrey")).ShouldBe("that isn't food");
        Eating.Feed(w, row, H("item.bread_loaf")).ShouldBe("you have none");

        var p = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Player = [12f, -6f] }).CreateWorld(Content, SerialJobScheduler.Instance);
        p.Step();
        p.Inventory.Add(p.People.Ids[0], H("item.bread_loaf"), 1);
        p.Enqueue(new CommandEnvelope(p.LastCommandSeq + 1, 0, CommandSource.Player, new Eat(p.People.Ids[0], "item.bread_loaf")));
        p.Step().Events.Select(e => e.Payload).OfType<CommandRejected>().ShouldContain(r => r.Reason.StartsWith("Eat: the player eats as themselves", StringComparison.Ordinal));
        p.Inventory.Add(p.PlayerId, H("item.bread_loaf"), 1);
        p.Enqueue(new CommandEnvelope(p.LastCommandSeq + 1, 0, CommandSource.Player, new Eat(p.PlayerId, "item.bread_loaf")));
        p.Step().Events.Select(e => e.Payload).OfType<Ate>().ShouldHaveSingleItem().Item.ShouldBe(H("item.bread_loaf"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
