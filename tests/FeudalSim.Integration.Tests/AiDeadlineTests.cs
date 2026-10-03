using FeudalSim.AI;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Ai;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Systems;

namespace FeudalSim.Integration.Tests;

/// <summary>M0-10: the sim never waits on a model; late results are rejected identically in replay.</summary>
public sealed class AiDeadlineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "feudalsim-ai", Guid.NewGuid().ToString("N"));

    /// <summary>Deterministically asks for one AI line at step 2 with a 10-step deadline.</summary>
    private sealed class PingAtStepTwo : ISimSystem
    {
        public string Name => "PingAtStepTwo";
        public SimPhase Phase => SimPhase.World;

        public void Run(in StepContext ctx, SimWorld world)
        {
            if (ctx.Step == 2) { world.RequestAi(AiTaskKind.Ping, AiPriority.Interactive, 10, world.People.Ids[0], EntityId.None, "Greet the player.", "Hm. (nods)"); }
        }
    }

    private sealed class SlowChat : IChatProvider
    {
        public string Tag => "slow-fake";

        public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            await Task.Delay(3_000, CancellationToken.None);
            return new ChatResult("Good morrow!", 10, 3, 3_000, 0, Tag);
        }

        public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield break;
        }
    }

    private static SimWorld World()
    {
        var world = new SimWorld(9).AddSystem(new WanderSystem()).AddSystem(new NeedsDecaySystem()).AddSystem(new PingAtStepTwo());
        world.Enqueue(new CommandEnvelope(1, 0, CommandSource.Scenario, new SpawnPerson("Hedda", 0, 0)));
        return world;
    }

    private static string Describe(EventEnvelope e) => $"{e.Step}:{e.Payload}";

    [Fact]
    public async Task Slow_provider_misses_the_deadline_fallback_applies_and_the_late_result_is_rejected_in_replay_too()
    {
        var logPath = Path.Combine(_dir, "inputs.fslog");
        var config = new AiConfig { ChatKey = new Secret("test-key"), MaxConcurrency = 2 };
        using var gateway = new AiGateway(config, new SlowChat(), timeout: TimeSpan.FromSeconds(10));
        var live = new List<string>();
        long finalStep;
        ulong liveHash;
        using (var log = InputLogFile.OpenOrCreate(logPath))
        using (var runner = new SimRunner(World(), log, RunMode.Running, gateway))
        {
            await Task.Delay(4_500, TestContext.Current.CancellationToken);   // 10 steps ≈ 1 s; the result lands ≈ step 32
            runner.Pause();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            (finalStep, liveHash) = await runner.Invoke(w => (w.Clock.Step, StateHasher.Hash(w)));
            while (runner.Events.TryPop(out var e)) { live.Add(Describe(e)); }
        }

        live.ShouldContain(e => e.StartsWith("12:AiResultApplied") && e.Contains("UsedFallback = True") && e.Contains("fallback:deadline"));
        live.ShouldContain(e => e.Contains("CommandRejected") && e.Contains("Late or unknown AI result 1"));
        var rejectedAt = long.Parse(live.Single(e => e.Contains("CommandRejected")).Split(':')[0], System.Globalization.CultureInfo.InvariantCulture);
        rejectedAt.ShouldBeGreaterThan(12);

        var replayed = new List<string>();
        var replay = World();
        var commands = InputLogFile.ReadAll(logPath);
        commands.ShouldContain(c => c.Payload is AiResultCommand);
        Replayer.Run(replay, commands.Where(c => c.Source != CommandSource.Scenario).ToList(), finalStep, o => replayed.AddRange(o.Events.Select(Describe)));

        replayed.ShouldBe(live);
        StateHasher.Hash(replay).ShouldBe(liveHash);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); }
    }
}
