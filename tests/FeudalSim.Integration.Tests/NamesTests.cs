using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-30: settlers get real names from their culture's lists — unique in the camp, the same for the same seed.</summary>
public sealed class NamesTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static string[] Names(ulong seed)
    {
        var camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Seed = seed };
        var w = camp.CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w.People.Names.ToArray();
    }

    [Fact]
    public void SettlersHaveUniqueCultureNames_DeterminedBySeed()
    {
        var a = Names(42);
        a.Length.ShouldBe(24);
        a.ShouldAllBe(n => !n.StartsWith("Settler", StringComparison.Ordinal) && n.Contains(' '));
        a.Distinct().Count().ShouldBe(a.Length);
        var varrow = Content.Cultures.Single(c => c.Id == "culture.varrow");
        foreach (var n in a)
        {
            var parts = n.Split(' ');
            varrow.GivenNames!.ShouldContain(parts[0]);
            varrow.FamilyNames!.ShouldContain(parts[1]);
        }
        Names(42).ShouldBe(a);
        Names(43).ShouldNotBe(a);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
