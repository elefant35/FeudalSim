using System.Collections.Concurrent;
using System.Diagnostics;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;

namespace FeudalSim.Hosting;

public enum RunMode { Running, Paused, MaxSpeed }

/// <summary>
/// Runs a <see cref="SimWorld"/> on a dedicated thread at a fixed 100 ms step (20 §5.3): time scale,
/// pause, bounded catch-up and a time-dilation counter. Other threads talk to it only through
/// commands, control actions, the snapshot triple buffer and the event ring.
/// </summary>
public sealed class SimRunner : IDisposable
{
    public const int MaxCatchUpSteps = 5;

    private readonly SimWorld _world;
    private readonly InputLogFile? _log;
    private readonly ConcurrentQueue<(CommandSource Source, StateCommand Command)> _inbox = new();
    private readonly ConcurrentQueue<Action<SimWorld>> _control = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _stop;
    private volatile RunMode _mode;
    private double _timeScale = 1.0;
    private long _seq;
    private long _steps;
    private long _dilationEvents;

    public SimRunner(SimWorld world, InputLogFile? log = null, RunMode mode = RunMode.Running)
    {
        _world = world;
        _log = log;
        _mode = mode;
        _seq = world.LastCommandSeq;   // continue the world's command numbering
        Snapshots = new TripleBuffer<RenderSnapshot>(() => new RenderSnapshot());
        _thread = new Thread(Loop) { IsBackground = true, Name = "sim" };
        _thread.Start();
    }

    public TripleBuffer<RenderSnapshot> Snapshots { get; }
    public EventRing Events { get; } = new();
    public RunMode Mode => _mode;
    public long StepsExecuted => Interlocked.Read(ref _steps);
    public long TimeDilationEvents => Interlocked.Read(ref _dilationEvents);
    public double TimeScale => Volatile.Read(ref _timeScale);

    /// <summary>Queues a state command (logged). Applied at the start of the next step.</summary>
    public void Submit(CommandSource source, StateCommand command) => _inbox.Enqueue((source, command));

    /// <summary>Runs an action on the sim thread between steps (also while paused) and returns its result.</summary>
    public Task<T> Invoke<T>(Func<SimWorld, T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _control.Enqueue(w =>
        {
            try { tcs.SetResult(action(w)); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        _wake.Set();
        return tcs.Task;
    }

    public void Pause() { _mode = RunMode.Paused; _wake.Set(); }

    public void Resume() { _mode = RunMode.Running; _wake.Set(); }

    public void SetMaxSpeed() { _mode = RunMode.MaxSpeed; _wake.Set(); }

    /// <summary>Real-time multiplier (0 &lt; scale ≤ 8). Not a sim input: never logged, never affects determinism.</summary>
    public void SetTimeScale(double scale)
    {
        if (scale is <= 0 or > 8) { throw new ArgumentOutOfRangeException(nameof(scale)); }
        Volatile.Write(ref _timeScale, scale);
    }

    /// <summary>Advances exactly <paramref name="steps"/> steps while paused.</summary>
    public Task<long> StepWhilePaused(int steps) => Invoke(w =>
    {
        for (var i = 0; i < steps; i++) { StepOnce(); }
        return w.Clock.Step;
    });

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        var accumulator = 0.0;
        while (!_stop)
        {
            while (_control.TryDequeue(out var action)) { action(_world); }

            switch (_mode)
            {
                case RunMode.Paused:
                    _wake.WaitOne(50);
                    last = clock.Elapsed;
                    accumulator = 0;
                    continue;
                case RunMode.MaxSpeed:
                    StepOnce();
                    continue;
            }

            var now = clock.Elapsed;
            accumulator += (now - last).TotalMilliseconds * Volatile.Read(ref _timeScale) / Sim.Time.SimClock.StepMs;
            last = now;

            var n = 0;
            while (accumulator >= 1.0 && n < MaxCatchUpSteps)
            {
                StepOnce();
                accumulator -= 1.0;
                n++;
            }

            if (accumulator >= 1.0)
            {
                accumulator = 0;
                Interlocked.Increment(ref _dilationEvents);
            }

            var sleepMs = (1.0 - accumulator) * Sim.Time.SimClock.StepMs / Volatile.Read(ref _timeScale);
            _wake.WaitOne(TimeSpan.FromMilliseconds(Math.Max(1, sleepMs)));
        }
    }

    private void StepOnce()
    {
        while (_inbox.TryDequeue(out var item))
        {
            _world.Enqueue(new CommandEnvelope(++_seq, 0, item.Source, item.Command));
        }

        var output = _world.Step();
        foreach (var c in output.AppliedCommands) { _log?.Append(c); }
        foreach (var e in output.Events) { Events.Push(e); }
        Snapshots.Back.CopyFrom(_world);
        Snapshots.Publish();
        Interlocked.Increment(ref _steps);
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread.Join();
        _wake.Dispose();
    }
}
