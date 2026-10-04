using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Ai;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-07c: overheard NPC↔NPC talk is requested near the player, never changes outcomes, and falls back to templates.</summary>
public sealed class OverheardTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly ScenarioDef Fire = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_overheard.yaml"));

    private static (SimWorld W, List<AiRequest> Requests, List<AiResultApplied> Applied) Run(ScenarioDef scenario, int hours)
    {
        var w = scenario.CreateWorld(Content, SerialJobScheduler.Instance);
        var requests = new List<AiRequest>();
        var applied = new List<AiResultApplied>();
        var end = w.Clock.GameMs + (hours * 60L * Sim.Time.SimClock.MsPerGameMinute);
        while (w.Clock.GameMs < end)
        {
            var output = w.Step();
            requests.AddRange(output.AiRequests);
            applied.AddRange(output.Events.Select(e => e.Payload).OfType<AiResultApplied>());
        }

        return (w, requests, applied);
    }

    [Fact]
    public void TalkNearThePlayer_IsRequested_WithFacts_AndPlaysTheTemplateWithoutAModel()
    {
        var (w, requests, applied) = Run(Fire, 2);
        requests.ShouldNotBeEmpty();
        requests.ShouldAllBe(r => r.Kind == AiTaskKind.Overheard && r.Priority == AiPriority.Proximate);
        requests.ShouldAllBe(r => r.DeadlineStep - r.IssuedStep == Sim.Systems.InteractionSystem.OverheardDeadlineSteps);
        requests.ShouldAllBe(r => r.Context.Contains("\"setting\":") && r.Context.Contains("\"names\":["));
        foreach (var r in requests)   // the hand-built facts must be valid JSON, or every render would fail as a provider error
        {
            var facts = System.Text.Json.Nodes.JsonNode.Parse(r.Context)!.AsObject();
            facts["names"]!.AsArray().Select(n => (string)n!).ShouldContain((string)facts["a"]!["name"]!);
        }
        applied.Count.ShouldBe(requests.Count - w.PendingAiRequests);
        applied.ShouldAllBe(a => a.UsedFallback && a.ProviderTag == "fallback:deadline");
        applied.ShouldAllBe(a => a.Text.Contains("Settler "));   // a filled template subtitle

        // At most one overheard render in flight.
        requests.Zip(requests.Skip(1)).ShouldAllBe(p => p.Second.IssuedStep >= p.First.DeadlineStep);
    }

    [Fact]
    public void ARenderedLine_AndTheTemplate_LeaveTheWorldIdentical()
    {
        // The policy decided the exchange before any render was asked for (22 §9.2): a model reply that lands in time and
        // the template at the deadline must leave the same state.
        var template = Run(Fire, 3).W;

        var w = Fire.CreateWorld(Content, SerialJobScheduler.Instance);
        var end = w.Clock.GameMs + (3 * 60L * Sim.Time.SimClock.MsPerGameMinute);
        var rendered = 0;
        long seq = 10_000;
        while (w.Clock.GameMs < end)
        {
            foreach (var r in w.Step().AiRequests)
            {
                rendered++;
                w.Enqueue(new Sim.Commands.CommandEnvelope(++seq, w.Clock.Step + 5, Sim.Commands.CommandSource.Ai,
                    new AiResultCommand(r.RequestId, AiOutcome.Ok, """[{"speaker":"a","line":"Cold night."},{"speaker":"b","line":"Aye."}]""", "test", 500, 100, 20)));
            }
        }

        rendered.ShouldBeGreaterThan(0);
        Sim.StateHasher.Hash(w).ShouldBe(Sim.StateHasher.Hash(template));
    }

    [Fact]
    public async Task ASave_WaitsForRendersInFlight_ThenReleasesTheHold()
    {
        // 31 R27: with the player at the fire, overheard renders are almost always in flight. A save holds new ones
        // (they play their template at once) and captures only when none is pending.
        var w = Fire.CreateWorld(Content, SerialJobScheduler.Instance);
        using var runner = new SimRunner(w, mode: RunMode.MaxSpeed);
        await runner.StepWhilePaused(0);
        while (runner.StepsExecuted < 600) { await Task.Delay(5, TestContext.Current.CancellationToken); }
        var image = await runner.SaveWhenSettled().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        var restored = Sim.Persistence.SaveCodec.Restore(image, out _);
        restored.PendingAiRequests.ShouldBe(0);
        restored.AiHeld.ShouldBeFalse();
        var held = await runner.Invoke(world => world.AiHeld);
        for (var k = 0; k < 50 && held; k++) { await Task.Delay(10, TestContext.Current.CancellationToken); held = await runner.Invoke(world => world.AiHeld); }
        held.ShouldBeFalse();   // the hold lifts after the capture
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
