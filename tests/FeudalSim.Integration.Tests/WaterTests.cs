using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Survival;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-07c: 11 §11 water sources, boiling, the player's drink; §7.4 taint and background Flux.</summary>
public sealed class WaterTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly ScenarioDef Def = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"));

    private static SimWorld Camp(CampDef camp, float[]? player = null)
    {
        var w = (Def with { Camp = camp, Player = player }).CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    private static int FluxCases(SimWorld w, int days)
    {
        var flux = Content.DiseaseHandle("disease.flux");
        var cases = 0;
        for (var s = 0; s < days * 18_000; s++) { cases += w.Step().Events.Count(e => e.Payload is ConditionStarted c && c.Disease == flux); }
        return cases;
    }

    [Fact]
    public void Sources_HaveThe11_11Contamination()
    {
        Water.SourceContamination("spring").ShouldBe(0f);
        Water.SourceContamination("stream").ShouldBe(0.02f);
        Water.SourceContamination("river").ShouldBe(0.04f);
        Water.SourceContamination("lake").ShouldBe(0.05f);
        Water.SourceContamination("marsh").ShouldBe(0.30f);
        Water.BackgroundFluxPerDay(40).ShouldBe(0.002f * (1f + (20f / 30f)), 1e-6f);
        Water.BackgroundFluxPerDay(60).ShouldBe(0f);
    }

    [Fact]
    public void Boiling_CutsTheMarshWatersFlux()   // 11 §11.1: boiled ×0.02
    {
        var raw = FluxCases(Camp((Def.Camp ?? new CampDef()) with { WaterSource = "marsh", Pots = 0, Sanitation = 60 }), 3);
        var boiled = FluxCases(Camp((Def.Camp ?? new CampDef()) with { WaterSource = "marsh", Pots = 1, Sanitation = 60 }), 3);
        raw.ShouldBeGreaterThanOrEqualTo(3);
        boiled.ShouldBeLessThan(raw);   // ≈ 20–80 % of drinks boiled (Diligence), each at ×0.02
    }

    [Fact]
    public void ACorpseAtTheWater_TaintsItForThreeDays()   // 11 §7.4: +0.3
    {
        var w = Camp((Def.Camp ?? new CampDef()) with { WaterSource = "spring" });
        Water.CampContamination(w).ShouldBe(0f);
        const int row = 9;
        (w.People.Transforms[row].X, w.People.Transforms[row].Z) = (w.Camp.WaterX + 5f, w.Camp.WaterZ);
        Sim.Health.HealthRules.Trauma(w, row, 95f, Sim.Health.DamageType.Pierce, Sim.Health.BodyRegion.Torso, Sim.Health.TraumaSource.Animal);
        for (var s = 0; s < 750 * 6 && !w.IsDead(row); s++) { w.Step(); }
        w.IsDead(row).ShouldBeTrue();
        Water.CampContamination(w).ShouldBe(0.3f);
        w.Camp.WaterTaintUntilMin.ShouldBeGreaterThan(w.Clock.GameMinute + (2 * 1440));
    }

    [Fact]
    public void PoorSanitation_SeedsTheFlux_EvenFromASpring()   // 11 §7.4 background: 0.002 × (1 + (60 − S)/30) per person per day
    {
        // Sanitation 1 (the worst): 0.0059 per person per day → 24 × 40 days ≈ 5.7 seeds (P(none) ≈ 0.3 %).
        var cases = FluxCases(Camp((Def.Camp ?? new CampDef()) with { WaterSource = "spring", Sanitation = 1 }), 40);
        cases.ShouldBeGreaterThanOrEqualTo(2);
        FluxCases(Camp((Def.Camp ?? new CampDef()) with { WaterSource = "spring", Sanitation = 60 }), 10).ShouldBe(0);
    }

    [Fact]
    public void ThePlayer_DrinksAtTheWater_NotAwayFromIt()   // 11 §11.2: +20 a drink
    {
        var w = Camp(Def.Camp ?? new CampDef(), player: [0f, 0f]);
        var me = w.PlayerId;
        var row = w.PlayerRow;
        w.People.Needs[row].Hydration = 50f;
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, new Drink(me)));
        w.Step().Events.Select(e => e.Payload).OfType<CommandRejected>().ShouldContain(r => r.Reason.Contains("no water here", StringComparison.Ordinal));
        (w.People.Transforms[row].X, w.People.Transforms[row].Z) = (w.Camp.WaterX + 1f, w.Camp.WaterZ);
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Embodiment, new PlayerMoved(w.Camp.WaterX + 1f, w.Camp.WaterZ, 0f)));
        w.Step();
        w.People.Needs[row].Hydration = 50f;
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, new Drink(me)));
        var drank = w.Step().Events.Select(e => e.Payload).OfType<Drank>().ShouldHaveSingleItem();
        (drank.Hydration, drank.Source).ShouldBe((20f, "camp"));
        w.People.Needs[row].Hydration.ShouldBeGreaterThan(69f);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
