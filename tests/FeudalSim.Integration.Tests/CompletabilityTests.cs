using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-17 (22 §13.3, §17.2 #12): the golden goals complete in template mode — the policy decides every DP, templates speak.</summary>
public sealed class CompletabilityTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly ScenarioDef Talk = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_talk.yaml"));

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(9UL)]
    public void TheGoldenGoals_CompleteInTemplateMode_AndReplayIdentically(ulong seed)
    {
        var a = new CompletabilityRun(Content, Talk with { Seed = seed }).Run();
        foreach (var g in a.Goals) { g.Done.ShouldBeTrue($"{g.Goal}: {g.Detail}"); }
        a.Decisions.ShouldBeGreaterThan(0);
        a.PolicyDecisions.ShouldBe(a.Decisions);          // every DP decided by the policy
        a.TemplateLines.ShouldBe(a.Lines);                 // every line a template
        a.Lines.ShouldBeGreaterThanOrEqualTo(a.Turns);     // every player turn answered
        a.Passed.ShouldBeTrue();
        a.Timeouts.ShouldBe(0);                            // the host answered everything it owed
        new CompletabilityRun(Content, Talk with { Seed = seed }).Run().FinalHash.ShouldBe(a.FinalHash);
    }

    [Fact]
    public void Claims_ComeFromThePredicatePhrases_WithNamesOrMeAndYou()
    {
        var x = new ClaimExtractor(Content.ClaimPredicates);
        (string, EntityId)[] people = [("Bram Hollis", new EntityId(1)), ("Ada Marsh", new EntityId(2)), ("Tam", new EntityId(9))];
        var (player, listener) = (new EntityId(9), new EntityId(5));
        x.Extract("I saw Bram stole from Ada last night.", people, player, listener).ShouldBe(new ExtractedClaim("claim.stole", new EntityId(1), new EntityId(2), true));
        x.Extract("Did you hear? Ada Marsh cheated me.", people, player, listener).ShouldBe(new ExtractedClaim("claim.cheated", new EntityId(2), player, false));
        x.Extract("Bram has been stealing from you again and again", people, player, listener)!.Predicate.ShouldBe("claim.stole_repeatedly");
        x.Extract("Bram is a fine smith.", people, player, listener).ShouldBeNull();
        x.Extract("Someone stole from Ada.", people, player, listener).ShouldBeNull();   // no named subject
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
