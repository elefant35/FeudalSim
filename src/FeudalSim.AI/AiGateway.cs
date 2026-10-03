using FeudalSim.Sim.Ai;

namespace FeudalSim.AI;

/// <summary>
/// Transports AI requests from the sim's outbox to providers and returns results as
/// <see cref="AiResultCommand"/>s (20 §11). Strict priority with a concurrency limit; a real-time timeout;
/// a session spend cap; template mode when no key is configured or <c>LLM_MODE=template</c>.
/// The gateway never judges results — the sim validates them and owns every deadline.
/// </summary>
public sealed class AiGateway : IDisposable
{
    private readonly AiConfig _config;
    private readonly IChatProvider? _chat;
    private readonly SemaphoreSlim _slots;
    private readonly PriorityQueue<AiRequest, (byte, long)> _queue = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TimeSpan _timeout;
    private double _spentUsd;

    public AiGateway(AiConfig config, IChatProvider? chat, TimeSpan? timeout = null)
    {
        _config = config;
        _chat = config.TemplateMode ? null : chat;
        _slots = new SemaphoreSlim(Math.Max(1, config.MaxConcurrency));
        _timeout = timeout ?? TimeSpan.FromSeconds(8);
    }

    /// <summary>Raised on a thread-pool thread; the host must enqueue it as a logged command.</summary>
    public event Action<AiResultCommand>? Completed;

    public double SpentUsd { get { lock (_lock) { return _spentUsd; } } }

    public bool TemplateMode => _chat is null;

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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        cts.CancelAfter(_timeout);
        try
        {
            var result = await _chat!.CompleteAsync(new ChatRequest(_config.DialogueModel,
                [new("system", "You are a settler in a medieval village game. Reply in character, in one or two short sentences."),
                 new("user", request.Context)]), cts.Token).ConfigureAwait(false);
            lock (_lock) { _spentUsd += result.CostUsd; }
            return new AiResultCommand(request.RequestId, AiOutcome.Ok, result.Text, result.ProviderTag, result.LatencyMs, result.TokensIn, result.TokensOut);
        }
        catch (OperationCanceledException)
        {
            return new AiResultCommand(request.RequestId, AiOutcome.Timeout, "", _chat!.Tag, (int)_timeout.TotalMilliseconds, 0, 0);
        }
        catch (Exception ex) when (ex is AiProviderException or HttpRequestException or System.Text.Json.JsonException)
        {
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
