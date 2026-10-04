using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.Content;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-22 (30 M1 exit): the camp runs 30 days without deadlocks and quarrels emerge on their own.</summary>
public sealed class SocialCheckTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    [Fact]
    public void ThirtyCampDays_NoDeadlocks_DisputesEmerge()
    {
        var camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Seed = 3 };
        var run = SocialCheck.Run(camp, Content, 30);
        run.NoDeadlock.ShouldBeTrue($"stuck {run.StuckPeople}, same {run.LongestSameHours:F1} h, travel {run.LongestTravelHours:F1} h");
        run.LongestSameHours.ShouldBeLessThan(SocialRun.StuckSameHours);
        run.DisputesPer10Days.ShouldBeGreaterThanOrEqualTo(1.0);
        run.TopRungs[2].ShouldBeGreaterThan(0);   // most quarrels stay at words
        SocialCheck.Run(camp, Content, 30).FinalHash.ShouldBe(run.FinalHash);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
