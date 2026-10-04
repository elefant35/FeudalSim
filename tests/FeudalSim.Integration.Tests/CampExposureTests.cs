using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Survival;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-05a: the camp dresses everyone in the homeland kit, exposure acts on a winter night, and it all saves.</summary>
public sealed class CampExposureTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld WinterNight() => (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"))
        with { Player = [12f, -6f], Start = "Y0 Winter 5 20:00" }).CreateWorld(Content, SerialJobScheduler.Instance);

    [Fact]
    public void Everyone_including_the_player_lands_in_the_homeland_kit()
    {
        var w = WinterNight();
        w.Step();
        var wear = WearValues.For(Content);
        w.People.Count.ShouldBe(25);
        for (var i = 0; i < w.People.Count; i++) { Exposure.Insulation(w.People.Worn[i], wear, 0f).ShouldBe(12.5f, 0.001f); }
        w.IsPlayer(w.PlayerRow).ShouldBeTrue();
    }

    [Fact]
    public void A_winter_night_cools_people_and_exposure_survives_save_and_load()
    {
        var w = WinterNight();
        for (var s = 0; s < 750; s++) { w.Step(); }   // one game hour
        var image = SaveCodec.Capture(w);
        for (var s = 0; s < 1500; s++) { w.Step(); }
        var min = float.MaxValue;
        for (var i = 0; i < w.People.Count; i++) { min = MathF.Min(min, w.People.Needs[i].Warmth); }
        min.ShouldBeLessThan(100f);   // the cold reaches someone away from the fire

        var restored = SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        restored.People.Worn[0].ShouldBe(w.People.Worn[0]);
        for (var s = 0; s < 1500; s++) { restored.Step(); }
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(w));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
