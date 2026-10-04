using System.Runtime.CompilerServices;
using FeudalSim.AI;
using FeudalSim.AI.Dialogue;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;

namespace FeudalSim.AI.Tests;

/// <summary>M1-14 (22 §17.2 #9): a cut network or a stalled model never delays the sim; the policy decides and templates speak within the turn.</summary>
public sealed class ResilienceTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    [Fact]
    public void Breaker_OpensOnSustainedSlowFirstTokens_NotOnABriefSpike()
    {
        long now = 0;
        var slow = new CircuitBreaker("dialogue", () => now);
        for (var i = 0; i < 20; i++) { slow.RecordLatency(i == 0 ? 300 : 2_600, 1_000); now += 10_000; }   // > 2 min of p95 2.6 s > 2 s
        slow.State.ShouldBe(CircuitBreaker.BreakerState.Open);

        now = 0;
        var spike = new CircuitBreaker("dialogue", () => now);
        for (var i = 0; i < 6; i++) { spike.RecordLatency(2_600, 1_000); now += 10_000; }                // only a minute of it
        spike.State.ShouldBe(CircuitBreaker.BreakerState.Closed);
        for (var i = 0; i < 12; i++) { spike.RecordLatency(900, 1_000); now += 10_000; }                 // then healthy
        spike.State.ShouldBe(CircuitBreaker.BreakerState.Closed);
    }

    private sealed class Chat(TimeSpan firstTokenDelay, bool fail = false, string reply = "CHOICE: none\nRAPPORT: none\nSAY: Evening.") : IChatProvider
    {
        public string Tag => "test";
        public int Calls;

        public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (fail) { throw new HttpRequestException("network down"); }
            await Task.Delay(firstTokenDelay, ct);
            return new ChatResult("SAY: Evening.", 1, 1, 1, 0, Tag);
        }

        public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(firstTokenDelay, ct);
            if (fail) { throw new HttpRequestException("network down"); }
            yield return reply;
        }
    }

    private static TurnBundle Bundle()
    {
        var dp = new DecisionPointOpened(7, "21.initiative", new DpContext("conv.initiative", new EntityId(1), new EntityId(2), 1), 99,
            [new MenuOption("none", "f", [], true, 0.9f, 0f, Stakes.Low, false, "just answer"), new MenuOption("end_conversation", "f", [], true, 0.1f, 0f, Stakes.Low, false, "end it")],
            ["end_conversation", "none"], 1, 41, DeciderKind.Llm);
        return new TurnBundle(1, 1, new EntityId(1), "Bram", "Tam", "Evening.", "greet_farewell", dp, dp, null,
            new PromptFacts("Name: Bram.", "Evening.", "TOWARD TAM: likes him.", [], [], ["Bram", "Tam"]), new Dictionary<string, IReadOnlyDictionary<string, string>>());
    }

    [Fact]
    public async Task AnOpenBreaker_SendsTheTurnToThePolicyAtOnce_WithoutCallingTheModel()
    {
        var chat = new Chat(TimeSpan.Zero);
        var router = new DialogueReplyRouter(chat, null, new AiConfig(), new TemplateBank(Content));
        for (var i = 0; i < CircuitBreaker.FailuresToOpen; i++) { router.Breaker.RecordFailure(); }
        var decided = new List<DecisionMade>();
        DialogueLineRendered? line = null;
        router.Decided += decided.Add;
        router.Line += l => line = l;
        var run = router.RunAsync(Bundle(), TestContext.Current.CancellationToken);
        decided.Single().Choice.ShouldBeNull();   // policy, now
        router.OnResolved(7, "none");
        await run;
        chat.Calls.ShouldBe(0);
        (line!.Source, line.Flags).ShouldBe(("template", "breaker_open"));
    }

    [Fact]
    public async Task NoFirstTokenWithinThreeSeconds_FailsOver()
    {
        var router = new DialogueReplyRouter(new Chat(TimeSpan.FromSeconds(10)), null, new AiConfig(), new TemplateBank(Content));
        DialogueLineRendered? line = null;
        router.Line += l => line = l;
        router.Decided += d => { if (d.Choice is null) { router.OnResolved(d.Id, "none"); } };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await router.RunAsync(Bundle(), TestContext.Current.CancellationToken);
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(7));   // within the turn (the 6 s speech cutoff, give or take)
        line!.Flags.ShouldContain("ttft_timeout");
    }

    /// <summary>The real-time runner holds 10 steps/s while a turn waits on a stalled or dead model, and the turn still gets its line.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSimNeverWaitsOnLanguage(bool networkCut)
    {
        var scenario = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_talk.yaml"));
        using var jobs = new JobRunner(1);
        var world = scenario.CreateWorld(Content, jobs);
        var chat = new Chat(networkCut ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(10), fail: networkCut);
        using var runner = new SimRunner(world, null, RunMode.Running);
        var host = new DialogueHost(runner.Submit, chat, null, new AiConfig { ChatKey = new Secret("test-key") }, Content);
        runner.Dialogue = host;
        DialogueLineRendered? line = null;
        host.Partial += p => { if (p.Final) { line = new DialogueLineRendered(0, 0, default, p.Text, "", ""); } };
        await Task.Delay(800, TestContext.Current.CancellationToken);
        var (npc, at) = await runner.Invoke(w => (w.People.Ids[4], w.People.Transforms[4]));
        runner.Submit(CommandSource.Embodiment, new PlayerMoved(at.X + 1, at.Z, 0));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        runner.Submit(CommandSource.Player, new StartConversation(npc));
        for (var i = 0; i < 30 && host.Conversation is null; i++) { await Task.Delay(100, TestContext.Current.CancellationToken); }

        await host.SayAsync("Good evening.", TestContext.Current.CancellationToken);
        var steps0 = runner.StepsExecuted;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(3_000, TestContext.Current.CancellationToken);
        var rate = (runner.StepsExecuted - steps0) / sw.Elapsed.TotalSeconds;
        rate.ShouldBeInRange(9.0, 11.0);                 // 10 Hz while the model stalls or the network is down
        runner.TimeDilationEvents.ShouldBe(0);
        for (var i = 0; i < 80 && line is null; i++) { await Task.Delay(100, TestContext.Current.CancellationToken); }
        line.ShouldNotBeNull();                          // policy decided, a template spoke, within the turn
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
