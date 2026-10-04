using System.Net;
using System.Text;
using FeudalSim.AI;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Systems;

namespace FeudalSim.AI.Tests;

/// <summary>M0-18: circuit breaker, AI_GATEWAY_MODE recorder, and decision-point routing through the gateway.</summary>
public sealed class GatewayResilienceTests : IDisposable
{
    private const string FakeKey = "sk-or-test-SECRET-91b2";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "feudalsim-transcripts", Guid.NewGuid().ToString("N"));

    private const string DecideBody = """
        {"choices":[{"logprobs":{"content":[{"token":"B","top_logprobs":[
          {"token":"B","logprob":-0.10},{"token":"A","logprob":-2.5},{"token":"C","logprob":-3.0}]}]}}],
         "usage":{"prompt_tokens":80,"cost":0.00001}}
        """;

    private sealed class FakeHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("replay must not reach the network");
    }

    private sealed class FakeDecider(Func<DecisionRequest, DecisionResult> answer) : IDecider
    {
        public string ProviderId => "fake-decider";
        public List<DecisionRequest> Requests { get; } = [];

        public ValueTask<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return ValueTask.FromResult(answer(request));
        }
    }

    private AiConfig Config(string mode = "live", bool log = false, string? key = FakeKey, string llmMode = "auto")
        => new() { ChatKey = new Secret(key), DeciderKey = new Secret(key), GatewayMode = mode, LogTranscripts = log, TranscriptDir = _dir, LlmMode = llmMode };

    public void Dispose()
    {
        if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); }
    }

    [Fact]
    public void Breaker_OpensAfterFiveFailuresIn60s_ProbesAfter30s_ClosesAfterThreeSuccesses()
    {
        var now = 0L;
        var b = new CircuitBreaker("test", () => now);
        for (var i = 0; i < 4; i++) { b.RecordFailure(); now += 1_000; }
        b.State.ShouldBe(CircuitBreaker.BreakerState.Closed);
        now += 61_000;   // the first four age out of the 60 s window
        b.RecordFailure();
        b.State.ShouldBe(CircuitBreaker.BreakerState.Closed);
        for (var i = 0; i < 4; i++) { b.RecordFailure(); }
        b.State.ShouldBe(CircuitBreaker.BreakerState.Open);
        b.TryAcquire().ShouldBeFalse();

        now += 30_000;
        b.TryAcquire().ShouldBeTrue();    // the half-open probe
        b.TryAcquire().ShouldBeFalse();   // one probe at a time
        b.RecordFailure();                 // failed probe → open again for 30 s
        b.State.ShouldBe(CircuitBreaker.BreakerState.Open);
        b.TryAcquire().ShouldBeFalse();

        now += 30_000;
        for (var i = 0; i < 3; i++)
        {
            b.TryAcquire().ShouldBeTrue();
            b.RecordSuccess();
        }

        b.State.ShouldBe(CircuitBreaker.BreakerState.Closed);
        b.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public async Task Recorder_StoresPairsWithoutTheKey_AndReplayNeverTouchesTheNetwork()
    {
        var live = new FakeHandler(_ => DecideBody);
        DecisionResult recorded;
        using (var stack = AiStack.Create(Config("record", log: true), live))
        {
            recorded = await stack.Decider!.DecideAsync(new DecisionRequest("s", "q?", ["x", "y", "z"]), TestContext.Current.CancellationToken);
        }

        live.Calls.ShouldBe(1);
        var file = Directory.GetFiles(_dir, "*.json").ShouldHaveSingleItem();
        var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        text.ShouldContain("qwen/qwen3.5-9b");
        text.ShouldNotContain(FakeKey);
        text.ShouldNotContain("Bearer");

        using (var replay = AiStack.Create(Config("replay", key: null), new NoNetwork()))   // no key needed to replay
        {
            replay.Config.TemplateMode.ShouldBeFalse();
            var again = await replay.Decider!.DecideAsync(new DecisionRequest("s", "q?", ["x", "y", "z"]), TestContext.Current.CancellationToken);
            again.Ok.ShouldBeTrue();
            again.Probabilities.ShouldBe(recorded.Probabilities);
            var miss = await replay.Decider.DecideAsync(new DecisionRequest("other", "q?", ["x", "y"]), TestContext.Current.CancellationToken);
            miss.Ok.ShouldBeFalse();   // a replay miss is a provider failure → normal fallback
        }
    }

    [Fact]
    public async Task RecordMode_WritesNothingUnlessTranscriptLoggingIsOn()
    {
        using var stack = AiStack.Create(Config("record", log: false), new FakeHandler(_ => DecideBody));
        (await stack.Decider!.DecideAsync(new DecisionRequest("s", "q?", ["x", "y"]), TestContext.Current.CancellationToken)).Ok.ShouldBeTrue();
        Directory.Exists(_dir).ShouldBeFalse();
    }

    private static DecisionPointOpened Dp(ulong id, params string[] preCleared)
    {
        MenuOption[] options = [.. new[] { "accept_at_price", "counter_step_1", "refuse", "shove" }
            .Select(o => new MenuOption(o, "trade", [], true, 0.25f, 0f, Stakes.Low, false, $"gloss {o}"))];
        return new DecisionPointOpened(id, "test", new DpContext("trade.respond", new EntityId(1), EntityId.None, 0), 42UL,
            options, preCleared.Length > 0 ? preCleared : [.. options.Select(o => o.Id)], 10, 50, DeciderKind.Llm);
    }

    [Fact]
    public void PresentationOrder_IsADeterministicShuffleOfPreCleared()
    {
        var orders = Enumerable.Range(1, 40).Select(i => string.Join(",", AiGateway.PresentationOrder(Dp((ulong)i)))).ToList();
        orders.ShouldAllBe(o => o.Split(',', StringSplitOptions.None).Order().SequenceEqual(new[] { "accept_at_price", "counter_step_1", "refuse", "shove" }));
        orders.Distinct().Count().ShouldBeGreaterThan(5);   // ids give different orders
        AiGateway.PresentationOrder(Dp(7)).ShouldBe(AiGateway.PresentationOrder(Dp(7)));
    }

    [Fact]
    public void Open_MapsTheDecidersLabelBackToTheOptionShownAtThatPosition()
    {
        for (ulong id = 1; id <= 12; id++)
        {
            var dp = Dp(id, "accept_at_price", "refuse", "shove");
            var shown = AiGateway.PresentationOrder(dp);
            for (var chosen = 0; chosen < shown.Length; chosen++)
            {
                var pick = chosen;
                var decider = new FakeDecider(r => new DecisionResult([.. r.Options.Select((_, i) => i == pick ? 0.8f : 0.1f)], pick, "fake-decider", 5, 10, 0));
                using var gw = new AiGateway(Config(), chat: null, decider);
                DecisionMade? made = null;
                gw.Decided += d => made = d;
                gw.Open(dp);
                made.ShouldNotBeNull();
                decider.Requests.Single().Options[pick].ShouldBe($"gloss {shown[chosen]}");
                (made.Choice, made.Decider, made.MenuHash).ShouldBe((shown[chosen], DeciderKind.Fast, 42UL));
                made.Probabilities![Array.IndexOf(dp.PreCleared, shown[chosen])].ShouldBe(0.8f);
            }
        }
    }

    [Fact]
    public void Open_AnswersPolicyNow_InTemplateMode_AndWhenTheBreakerIsOpen()
    {
        using (var template = new AiGateway(Config(llmMode: "template"), chat: null, new FakeDecider(_ => throw new InvalidOperationException())))
        {
            DecisionMade? made = null;
            template.Decided += d => made = d;
            template.Open(Dp(3));
            (made!.Choice, made.Decider, made.ProviderTag).ShouldBe((null, DeciderKind.Policy, "template"));
        }

        var now = 0L;
        var failing = new FakeDecider(r => new DecisionResult([.. r.Options.Select(_ => 0f)], 0, "fake-decider", 5, 0, 0, "HTTP 429"));
        using var gw = new AiGateway(Config(), chat: null, failing, clockMs: () => now);
        var answers = new List<DecisionMade>();
        gw.Decided += answers.Add;
        for (ulong i = 1; i <= 6; i++) { gw.Open(Dp(i)); }
        answers.Count.ShouldBe(6);
        answers.Take(5).ShouldAllBe(a => a.Choice == null && a.ProviderTag == "fake-decider:HTTP 429");
        answers[5].ProviderTag.ShouldBe("breaker:fake-decider");   // open after 5 failures: no call made
        failing.Requests.Count.ShouldBe(5);
        gw.DeciderBreaker.State.ShouldBe(CircuitBreaker.BreakerState.Open);
    }

    [Fact]
    public async Task LiveSessionWithAFastDecider_ReplaysHeadlessToTheSameHash_WithoutCallingAModel()
    {
        var scenario = new ScenarioDef { Id = "scenario.dp_gateway", Seed = 5, Settlers = 2, DecisionPingStep = 3, DecisionPingDeadlineSteps = 40 };
        var content = Sim.Content.ContentDatabase.Empty;
        var log = Path.Combine(_dir, "inputs.fslog");
        Directory.CreateDirectory(_dir);
        var decider = new FakeDecider(r => new DecisionResult([.. r.Options.Select((o, i) => o.Contains("decline") ? 0.7f : 0.1f)],
            Array.FindIndex([.. r.Options], o => o.Contains("decline")), "fake-decider", 5, 10, 0));
        ulong liveHash;
        long liveStep;
        List<DecisionResolved> liveDecisions = [];
        using (var jobs = new JobRunner(1))
        using (var file = InputLogFile.OpenOrCreate(log))
        using (var gw = new AiGateway(Config(), chat: null, decider))
        using (var runner = new SimRunner(scenario.CreateWorld(content, jobs), file, RunMode.Paused, gw))
        {
            await runner.StepWhilePaused(4);   // DP opens at step 3; integrity + decision are queued
            await runner.StepWhilePaused(6);
            (liveStep, liveHash) = await runner.Invoke(w => (w.Clock.Step, StateHasher.Hash(w)));
            while (runner.Events.TryPop(out var e)) { if (e.Payload is DecisionResolved r) { liveDecisions.Add(r); } }
        }

        var resolved = liveDecisions.ShouldHaveSingleItem();
        (resolved.Chosen, resolved.Decider, resolved.Guard, resolved.ProviderTag).ShouldBe(("refuse", DeciderKind.Fast, GuardOutcome.Passed, "fake-decider"));
        decider.Requests.Count.ShouldBe(1);

        var logged = InputLogFile.ReadAll(log);
        logged.Select(c => c.Payload.GetType().Name).ShouldContain(nameof(DecisionPointOpened));
        logged.Select(c => c.Payload.GetType().Name).ShouldContain(nameof(DecisionMade));
        using var replayJobs = new JobRunner(1);
        var world = scenario.CreateWorld(content, replayJobs);
        var replayEvents = new List<EventEnvelope>();
        Replayer.Run(world, [.. logged.Where(c => c.Source != CommandSource.Scenario)], liveStep, o => replayEvents.AddRange(o.Events));
        StateHasher.Hash(world).ShouldBe(liveHash);
        replayEvents.Select(e => e.Payload).OfType<DecisionResolved>().ShouldHaveSingleItem().ShouldBe(resolved);
        replayEvents.Select(e => e.Payload).OfType<IntegrityMismatch>().ShouldBeEmpty();
        decider.Requests.Count.ShouldBe(1);   // replay asked no model
    }
}
