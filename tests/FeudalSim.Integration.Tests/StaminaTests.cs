using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Survival;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-05b: 18 §2.3 / 11 §3.1 Stamina for the embodied player — sprint, Winded, regen, Energy, activity tier.</summary>
public sealed class StaminaTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp()
    {
        var w = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Player = [12f, -6f] })
            .CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    private static void Move(SimWorld w, byte gait, int steps)
    {
        for (var k = 0; k < steps; k++)
        {
            var t = w.People.Transforms[w.PlayerRow];
            w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Embodiment, new PlayerMoved(t.X + 0.65f, t.Z, 0f, gait)));
            w.Step();
        }
    }

    [Fact]
    public void MaxStamina_IsTheAveragePersons81()   // 18 §2.3: End 5, Athletics 30
    {
        StaminaRules.Max(5f, 30f, 80f, 80f).ShouldBe(81f);
        StaminaRules.Max(5f, 30f, 20f, 80f).ShouldBe(60.75f);           // Exhausted ×0.75
        StaminaRules.Max(5f, 30f, 80f, 10f).ShouldBe(68.85f, 0.001f);   // Ravenous ×0.85
        StaminaRules.Regen(80f, 30f, 80f).ShouldBe(10.5f, 0.001f);      // Cold ×0.7
    }

    [Fact]
    public void Sprinting_Spends8PerSecond_EmptyMeansWinded_ThenItRefills()
    {
        var w = Camp();
        var row = w.PlayerRow;
        w.Step();
        var full = w.People.Stamina[row].Value;
        full.ShouldBeGreaterThan(60f);
        Move(w, 2, 10);   // 1 s sprint
        w.People.Stamina[row].Value.ShouldBe(full - 8f, 0.01f);
        w.People.Activity[row].Level.ShouldBe(ActivityLevel.Heavy);

        Move(w, 2, (int)MathF.Ceiling((full - 8f) / 0.8f) + 1);   // run it dry
        w.People.Stamina[row].Value.ShouldBe(0f);
        (w.Clock.Step < w.People.Stamina[row].WindedUntilStep).ShouldBeTrue();
        Move(w, 2, 3);
        w.People.Stamina[row].Value.ShouldBe(0f);                        // Winded: no sprint, no spend
        w.People.Activity[row].Level.ShouldBe(ActivityLevel.Moderate);   // dropped to a jog

        for (var k = 0; k < 30; k++) { w.Step(); }                       // standing still for 3 s
        w.People.Activity[row].Level.ShouldBe(ActivityLevel.Rest);
        w.People.Stamina[row].Value.ShouldBeGreaterThan(10f);           // regen after the 1 s pause
    }

    [Fact]
    public void Every100StaminaSpent_CostsOneEnergy()
    {
        var w = Camp();
        var row = w.PlayerRow;
        w.People.Needs[row].Energy = 90f;
        var e0 = w.People.Needs[row].Energy;
        var spent = 0f;
        for (var round = 0; round < 3 && spent < 100f; round++)
        {
            var before = w.People.Stamina[row].Value;
            Move(w, 2, (int)(before / 0.8f) + 1);
            spent += before;
            for (var k = 0; k < 120; k++) { w.Step(); }   // refill
        }

        spent.ShouldBeGreaterThanOrEqualTo(100f);
        (e0 - w.People.Needs[row].Energy).ShouldBeGreaterThan(1f);   // the point from stamina plus ordinary decay
    }

    [Fact]
    public void StaminaSurvivesSaveAndLoad()
    {
        var w = Camp();
        Move(w, 2, 25);
        var image = SaveCodec.Capture(w);
        Move(w, 1, 20);
        var restored = SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        Move(restored, 1, 20);
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(w));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
