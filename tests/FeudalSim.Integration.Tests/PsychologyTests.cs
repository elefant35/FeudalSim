using FeudalSim.Content;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-01b: psychological needs, emotion decay and mood against 21 §5.2, §6.2 and §6.4's own numbers.</summary>
public sealed class PsychologyTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    /// <summary>One person with neutral facets and no traits (or the given ones); only the psychology system runs.</summary>
    private static SimWorld World(params string[] traits)
    {
        var w = new SimWorld(1) { Content = Content }.AddSystem(new PsychologySystem());
        w.Enqueue(new CommandEnvelope(1, 0, CommandSource.Scenario, new SpawnPerson("A", 0, 0)));
        w.Step();
        ref var p = ref w.People.Personality[0];
        (p.Curiosity, p.Diligence, p.Sociability, p.Warmth, p.Volatility) = (50, 50, 50, 50, 50);
        p.Traits = traits.Aggregate(0UL, (bits, t) => bits | (1UL << Content.TraitHandle(t)));
        return w;
    }

    private static int StepsPerGameHour(SimWorld w) => (int)(60 * 60 * 1000 / w.Clock.GameMsPerStep);

    private static void RunHours(SimWorld w, double hours)
    {
        var n = (int)Math.Round(hours * StepsPerGameHour(w));
        for (var i = 0; i < n; i++) { w.Step(); }
    }

    [Fact]
    public void Anger_HalvesIn4GameHours_AndEvenTemperedIn2_8()
    {
        var w = World();
        w.People.Emotions[0].Anger = 80;
        w.People.Emotions[0].Fear = 80;
        RunHours(w, 4);
        w.People.Emotions[0].Anger.ShouldBe(40f, 0.3f);
        w.People.Emotions[0].Fear.ShouldBe(80f / 16f, 0.1f);   // Fear half-life 1 h: four halvings

        var calm = World("trait.even_tempered");
        calm.People.Emotions[0].Anger = 80;
        RunHours(calm, 2.8);
        calm.People.Emotions[0].Anger.ShouldBe(40f, 0.3f);
    }

    [Fact]
    public void Social_DecaysAt4_2PerHourForSociability70()
    {
        var w = World();
        w.People.Personality[0].Sociability = 70;   // z = 1.33 → 3 × 1.40 = 4.2 per hour (21 §5.2 example)
        w.People.Needs[0].Social = 70;
        RunHours(w, 10);
        w.People.Needs[0].Social.ShouldBe(28f, 0.5f);
    }

    [Fact]
    public void Mood_MatchesTheLandfallWorkedExample()
    {
        // 21 §6.4: Satiety 45, Hydration 70, Energy 40, Warmth 55, Social 60, Comfort 28, Safety 50, Purpose 75, Status 40;
        // Grief 30, Joy 10 → needs −6.5, Grief −15, Joy +4 → −17.5 (the doc's ≈ −16 adds thoughts −4 and +5).
        var n = new Needs { Satiety = 45, Hydration = 70, Energy = 40, Warmth = 55, Social = 60, Comfort = 28, Safety = 50, Purpose = 75, Status = 40 };
        var e = new Emotions { Grief = 30, Joy = 10 };
        PsychologySystem.ComposeMood(n, e, zVol: 0f, baseline: 0f, thoughts: 0f).ShouldBe(-17.53f, 0.05f);
        PsychologySystem.ComposeMood(n, e, zVol: 0f, baseline: 0f, thoughts: 1f).ShouldBe(-16.53f, 0.05f);
        PsychologySystem.D(45).ShouldBe(-0.0316f, 0.0005f);
        PsychologySystem.D(90).ShouldBe(0.2f);
        PsychologySystem.D(60).ShouldBe(0f);
    }

    [Fact]
    public void SmoothedMood_IsAOneHourEma_AndTraitsShiftTheBaseline()
    {
        var w = World("trait.cheerful");   // mood baseline +10
        w.People.Needs[0] = new Needs { Satiety = 60, Hydration = 60, Energy = 60, Warmth = 60, Social = 60, Comfort = 60, Safety = 60, Purpose = 60, Status = 60 };
        w.People.Mood[0] = default;
        w.Step();
        w.People.Mood[0].Value.ShouldBe(10f, 0.5f);
        RunHours(w, 1);
        w.People.Mood[0].Smoothed.ShouldBe(10f * (1f - MathF.Exp(-1f)), 0.6f);   // ≈ 63% of the way after one game hour
    }

    [Fact]
    public void StatusTracksStandingMinusAspiration_AndAmbitionRaisesTheBar()
    {
        static float After(params string[] traits)
        {
            var w = World(traits);
            w.People.Personality[0].Values.Status = 75;
            RunHours(w, 48);
            return w.People.Needs[0].Status;
        }

        After("trait.ambitious").ShouldBeLessThan(After());   // +15 aspiration → lower satisfaction (21 §5.2 example)
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
