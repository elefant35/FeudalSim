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

    [Fact]
    public void FreshFish_GoesStaleInHalfADayAt10C_AndRotsInADay_FasterWhenWarm()   // 11 §10.4
    {
        Sim.Systems.SpoilageSystem.TempFactor(10f).ShouldBe(1f);
        Sim.Systems.SpoilageSystem.TempFactor(20f).ShouldBe(2f, 1e-4f);
        Sim.Systems.SpoilageSystem.TempFactor(0f).ShouldBe(0.5f, 1e-4f);
        Sim.Systems.SpoilageSystem.TempFactor(-5f).ShouldBe(0.05f);
        Sim.Systems.SpoilageSystem.TempFactor(40f).ShouldBe(3f);

        var w = Camp();
        const int row = 3;
        var id = w.People.Ids[row];
        var rotted = new List<(ulong Container, int Item, int Qty)>();
        w.Inventory.Add(id, H("item.fish_fresh"), 3);
        w.Inventory.AgeFood(Content, 1f, 12f, rotted);
        w.Inventory.Of(id).Single(x => x.Item == H("item.fish_fresh")).Freshness.ShouldBe(0.5f, 0.001f);   // stale
        w.People.Needs[row].Satiety = 0f;
        Eating.Feed(w, row, H("item.fish_fresh")).ShouldBeNull();
        w.People.Needs[row].Satiety.ShouldBe(55f * 0.85f * 0.9f, 0.01f);   // stale ×0.9

        w.Inventory.Add(id, H("item.fish_fresh"), 1);   // a fresh one joins: the stack's spoil averages (2 × 0.5 + 1 × 0) / 3
        w.Inventory.Of(id).Single(x => x.Item == H("item.fish_fresh")).Freshness.ShouldBe(2f / 3f, 0.001f);
        w.Inventory.AgeFood(Content, 1f, 15f, rotted);   // 2/3 of a day's freshness left: rots after 16 h
        rotted.ShouldBeEmpty();
        w.Inventory.AgeFood(Content, 1f, 1f, rotted);
        rotted.ShouldHaveSingleItem().Qty.ShouldBe(3);
        w.Inventory.Count(id, H("item.fish_fresh")).ShouldBe(0);
    }

    [Fact]
    public void SpoiledBerries_OnlyTheRavenousEat_AtHalfValue()   // 11 §10.4: spoiled non-flesh inedible except to the Ravenous
    {
        var w = Camp();
        const int row = 5;
        var id = w.People.Ids[row];
        w.Inventory.Add(id, H("item.blackberries"), 2, spoil: 60000);   // F ≈ 0.08
        w.People.Needs[row].Satiety = 50f;
        Eating.Feed(w, row, H("item.blackberries"))!.ShouldStartWith("it has spoiled");
        w.People.Needs[row].Satiety = 10f;   // Ravenous
        Eating.Feed(w, row, H("item.blackberries")).ShouldBeNull();
        w.People.Needs[row].Satiety.ShouldBe(10f + (16f * 0.5f), 0.01f);
    }

    [Fact]
    public void TheCampOnHalfRations_DrawsHalfAFullRationEachADay()   // 11 §10.5
    {
        var def = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"));
        float Drawn(int ration)
        {
            var w = (def with { Camp = (def.Camp ?? new CampDef()) with { Ration = ration } }).CreateWorld(Content, SerialJobScheduler.Instance);
            w.Step();
            var perDay = new Dictionary<(int Row, int Day), float>();
            for (var s = 0; s < 18_000 * 2; s++)   // from Spring 1 16:00 through the whole of Spring 2
            {
                w.Step();
                for (var r = 0; r < w.People.Count; r++) { perDay[(r, w.People.Diet[r].StoreDay)] = w.People.Diet[r].StoreSatToday; }
            }

            var day = (int)(w.Clock.GameMinute / 1440) - 1;   // the last complete day
            var drawn = Enumerable.Range(0, w.People.Count).Select(r => perDay.GetValueOrDefault((r, day))).ToList();
            drawn.ShouldAllBe(x => x <= (Eating.FullRationSat * Math.Min(ration, 99) / 100f) + 0.01f || ration >= 100);
            return drawn.Average();
        }

        var full = Drawn(100);
        var half = Drawn(50);
        full.ShouldBeGreaterThan(70f);
        half.ShouldBeInRange(40f, 47.6f);   // the cap (47.5) binds
    }

    [Fact]
    public void Starving_HalvesWork_AndHealing_AndDoublesSusceptibility()   // 11 §6.1
    {
        var w = Camp();
        const int row = 7;
        w.People.Needs[row] = Sim.World.Needs.Full;
        Fitness.WorkMult(w, row).ShouldBe(1f);
        w.People.Vitals[row].Starvation = 80f;
        Fitness.WorkMult(w, row).ShouldBe(0.5f);
        Fitness.HealMult(w, row).ShouldBe(0.2f);
        Fitness.StarvationSusceptibility(80f).ShouldBe(2f);
        w.People.Vitals[row].Starvation = 55f;
        Fitness.WorkMult(w, row).ShouldBe(0.8f);
        Fitness.StarvationSusceptibility(30f).ShouldBe(1.2f);
        w.People.Vitals[row].Starvation = 0f;
        w.People.Needs[row].Satiety = 10f;   // Ravenous (§2.2)
        Fitness.WorkMult(w, row).ShouldBe(0.8f);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
