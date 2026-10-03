using FeudalSim.Content;
using FeudalSim.Hosting;

namespace FeudalSim.Integration.Tests;

/// <summary>M0-08 (20 §20 step 8): the headless runner on the M0 smoke scenario.</summary>
public class ScenarioRunnerTests
{
    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { return dir.FullName; }
        }

        throw new DirectoryNotFoundException();
    }

    [Fact]
    public void Smoke_scenario_runs_three_days_deterministically_with_content()
    {
        var content = ContentCompiler.Compile(Path.Combine(Repo(), "content")).Database!;
        var scenario = ScenarioDef.Load(Path.Combine(Repo(), "content", "scenarios", "m0_smoke.yaml"));
        scenario.StartGameMs().ShouldBe(((5 * 60) + 30) * 60_000L);

        var a = ScenarioRunner.Run(scenario, content, threads: 1);
        var b = ScenarioRunner.Run(scenario, content, threads: 4);

        a.Days.Count.ShouldBe(3);
        a.Steps.ShouldBe(3 * 18_000);
        a.Days[0].Date.ShouldBe("Y0 Spring 2 05:30");
        a.Days[0].People.ShouldBe(24);
        a.Days[0].SatietyMean.ShouldBe(100 - (24 * 4.0), 0.05);   // content: need.satiety light = 4.0/h
        b.FinalHash.ShouldBe(a.FinalHash);
    }
}
