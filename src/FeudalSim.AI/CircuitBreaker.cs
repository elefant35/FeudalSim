namespace FeudalSim.AI;

/// <summary>
/// Per (provider, model) circuit breaker (22 §3.5): opens after 5 failures within 60 s; while open, calls are
/// refused and traffic falls to the next tier; after 30 s it lets one probe through at a time (half-open) and
/// closes after 3 consecutive successes — a failed probe re-opens it for another 30 s. HTTP errors (429s included),
/// timeouts and invalid answers count as failures. The latency trigger (rolling p95 TTFT &gt; 2× target for 2 min)
/// comes with S2's latency telemetry in M1.
/// </summary>
public sealed class CircuitBreaker(string name, Func<long>? nowMs = null)
{
    public const int FailuresToOpen = 5;
    public const long FailureWindowMs = 60_000;
    public const long ProbeAfterMs = 30_000;
    public const int SuccessesToClose = 3;

    public enum BreakerState { Closed, Open, HalfOpen }

    private readonly Func<long> _now = nowMs ?? (() => Environment.TickCount64);
    private readonly Queue<long> _failures = new();
    private readonly object _lock = new();
    private BreakerState _state;
    private long _openedAt;
    private bool _probeInFlight;
    private int _probeSuccesses;

    public string Name { get; } = name;

    public BreakerState State { get { lock (_lock) { return _state; } } }

    /// <summary>True if a call may go out now. A true from a half-open breaker reserves the single probe slot.</summary>
    public bool TryAcquire()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case BreakerState.Closed:
                    return true;
                case BreakerState.Open when _now() - _openedAt >= ProbeAfterMs:
                    (_state, _probeInFlight, _probeSuccesses) = (BreakerState.HalfOpen, true, 0);
                    return true;
                case BreakerState.HalfOpen when !_probeInFlight:
                    _probeInFlight = true;
                    return true;
                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state != BreakerState.HalfOpen) { return; }
            _probeInFlight = false;
            if (++_probeSuccesses >= SuccessesToClose)
            {
                _state = BreakerState.Closed;
                _failures.Clear();
            }
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            var now = _now();
            if (_state == BreakerState.HalfOpen)
            {
                Trip(now);
                return;
            }

            _failures.Enqueue(now);
            while (_failures.Count > 0 && now - _failures.Peek() > FailureWindowMs) { _failures.Dequeue(); }
            if (_failures.Count >= FailuresToOpen) { Trip(now); }
        }
    }

    private void Trip(long now)
    {
        (_state, _openedAt, _probeInFlight, _probeSuccesses) = (BreakerState.Open, now, false, 0);
        _failures.Clear();
    }
}
