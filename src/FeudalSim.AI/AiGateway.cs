using System.Collections.Concurrent;
using FeudalSim.Sim.Ai;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;

namespace FeudalSim.AI;

/// <summary>
/// Transports AI requests and decision points from the sim's outbox to providers and returns answers as logged
/// commands (20 §11): <see cref="AiResultCommand"/> via <see cref="Completed"/>, <see cref="DecisionMade"/> via
/// <see cref="Decided"/>. Strict priority with a concurrency limit; real-time timeouts; a session spend cap; a
/// circuit breaker per (provider, model); template mode when no key is configured or <c>LLM_MODE=template</c>.
/// The gateway never judges a result or a choice — the sim validates, guards and owns every deadline.
/// </summary>
public sealed class AiGateway : IDisposable
{
    private readonly AiConfig _config;
    private readonly IChatProvider? _chat;
    private readonly IDecider? _decider;
    private readonly SemaphoreSlim _slots;
    private readonly PriorityQueue<AiRequest, (byte, long)> _queue = new();
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _decisions = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TimeSpan _timeout;
    private double _spentUsd;

    public AiGateway(AiConfig config, IChatProvider? chat, IDecider? decider = null, TimeSpan? timeout = null, Func<long>? clockMs = null)
    {
        _config = config;
        _chat = config.TemplateMode ? null : chat;
        _decider = config.TemplateMode ? null : decider;
        _slots = new SemaphoreSlim(Math.Max(1, config.MaxConcurrency));
        _timeout = timeout ?? TimeSpan.FromSeconds(8);
        ChatBreaker = new CircuitBreaker($"{chat?.Tag ?? "none"}:{config.DialogueModel}", clockMs);
        DeciderBreaker = new CircuitBreaker(decider?.ProviderId ?? "none", clockMs);
    }

    /// <summary>Raised on a thread-pool thread; the host must enqueue it as a logged command.</summary>
    public event Action<AiResultCommand>? Completed;

    /// <summary>Raised on a thread-pool thread (or inline for "policy, now"); the host must enqueue it as a logged command.</summary>
    public event Action<DecisionMade>? Decided;

    public double SpentUsd { get { lock (_lock) { return _spentUsd; } } }

    public bool TemplateMode => _chat is null;

    public CircuitBreaker ChatBreaker { get; }

    public CircuitBreaker DeciderBreaker { get; }

    public void Submit(AiRequest request)
    {
        if (_chat is null)
        {
            Completed?.Invoke(new AiResultCommand(request.RequestId, AiOutcome.Unavailable, "", "template", 0, 0, 0));
            return;
        }

        lock (_lock) { _queue.Enqueue(request, ((byte)request.Priority, request.RequestId)); }
        _ = PumpAsync();
    }

    /// <summary>
    /// Routes a decision point (20 §11). M0 has no decision-first dialogue yet, so <c>Llm</c> DPs also go to the fast
    /// decider. With no decider, the budget spent or the breaker open, the answer is "policy, now" at once, so no DP
    /// waits for its deadline when no model can answer.
    /// </summary>
    public void Open(DecisionPointOpened dp)
    {
        var policyNow = _decider is null ? "template"
            : SpentUsd >= _config.MaxSpendUsdPerSession ? "budget"
            : !DeciderBreaker.TryAcquire() ? $"breaker:{DeciderBreaker.Name}"
            : null;
        if (policyNow is not null)
        {
            Decided?.Invoke(new DecisionMade(dp.Id, dp.MenuHash, null, DeciderKind.Policy, policyNow, 0, null));
            return;
        }

        _ = DecideAsync(dp);
    }

    /// <summary>The sim closed the DP early; an in-flight answer is dropped (the sim would reject it anyway).</summary>
    public void Cancel(ulong dpId)
    {
        if (_decisions.TryRemove(dpId, out var cts)) { cts.Cancel(); }
    }

    /// <summary>
    /// The order options are shown to the decider: the pre-cleared options shuffled by a pure function of the DP id
    /// (position-bias control, 21 §8.9), so the order is reproducible from the log without being stored.
    /// </summary>
    public static string[] PresentationOrder(DecisionPointOpened dp)
    {
        var order = dp.PreCleared.ToArray();
        var rng = new Rng(SplitMix64.Avalanche(dp.Id ^ 0x5F0F_D1CE_0000_0001UL));
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = rng.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    private async Task DecideAsync(DecisionPointOpened dp)
    {
        var order = PresentationOrder(dp);
        var glosses = order.Select(id => dp.Options.First(o => o.Id == id).Gloss).ToArray();
        var request = new DecisionRequest(
            $"Decision '{dp.Context.Kind}' for {dp.Context.Chooser}.",
            "Which does this character do?", glosses);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        cts.CancelAfter(_config.DeciderTimeoutMs);
        _decisions[dp.Id] = cts;
        DecisionResult result;
        try
        {
            result = await _decider!.DecideAsync(request, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new DecisionResult([], 0, _decider!.ProviderId, _config.DeciderTimeoutMs, 0, 0, "timeout");
        }

        if (!_decisions.TryRemove(dp.Id, out _)) { return; }   // cancelled by the sim meanwhile
        if (!result.Ok)
        {
            DeciderBreaker.RecordFailure();
            Decided?.Invoke(new DecisionMade(dp.Id, dp.MenuHash, null, DeciderKind.Policy, $"{result.ProviderId}:{result.Failure}", result.LatencyMs, null));
            return;
        }

        DeciderBreaker.RecordSuccess();
        lock (_lock) { _spentUsd += result.CostUsd; }
        var probabilities = new float[dp.PreCleared.Length];   // telemetry, reported in PreCleared order
        for (var i = 0; i < order.Length; i++) { probabilities[Array.IndexOf(dp.PreCleared, order[i])] = result.Probabilities[i]; }
        Decided?.Invoke(new DecisionMade(dp.Id, dp.MenuHash, order[result.Chosen], DeciderKind.Fast, result.ProviderId, result.LatencyMs, probabilities));
    }

    private async Task PumpAsync()
    {
        await _slots.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        AiRequest? request;
        lock (_lock) { _queue.TryDequeue(out request, out _); }
        try
        {
            if (request is not null) { Completed?.Invoke(await RunAsync(request).ConfigureAwait(false)); }
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<AiResultCommand> RunAsync(AiRequest request)
    {
        if (SpentUsd >= _config.MaxSpendUsdPerSession)
        {
            return new AiResultCommand(request.RequestId, AiOutcome.Refused, "", "budget", 0, 0, 0);
        }

        if (!ChatBreaker.TryAcquire())
        {
            return new AiResultCommand(request.RequestId, AiOutcome.Unavailable, "", $"breaker:{ChatBreaker.Name}", 0, 0, 0);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        cts.CancelAfter(_timeout);
        try
        {
            var result = await _chat!.CompleteAsync(new ChatRequest(_config.DialogueModel,
                [new("system", "You are a settler in a medieval village game. Reply in character, in one or two short sentences."),
                 new("user", request.Context)]), cts.Token).ConfigureAwait(false);
            ChatBreaker.RecordSuccess();
            lock (_lock) { _spentUsd += result.CostUsd; }
            return new AiResultCommand(request.RequestId, AiOutcome.Ok, result.Text, result.ProviderTag, result.LatencyMs, result.TokensIn, result.TokensOut);
        }
        catch (OperationCanceledException)
        {
            ChatBreaker.RecordFailure();
            return new AiResultCommand(request.RequestId, AiOutcome.Timeout, "", _chat!.Tag, (int)_timeout.TotalMilliseconds, 0, 0);
        }
        catch (Exception ex) when (ex is AiProviderException or HttpRequestException or System.Text.Json.JsonException)
        {
            ChatBreaker.RecordFailure();
            return new AiResultCommand(request.RequestId, AiOutcome.ProviderError, "", _chat!.Tag, 0, 0, 0);
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _slots.Dispose();
    }
}
