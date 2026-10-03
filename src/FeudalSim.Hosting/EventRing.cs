using System.Collections.Concurrent;
using FeudalSim.Sim.Events;

namespace FeudalSim.Hosting;

/// <summary>Bounded hand-off of domain events from the sim thread to presentation. Drops the oldest when full.</summary>
public sealed class EventRing
{
    private readonly ConcurrentQueue<EventEnvelope> _queue = new();
    private readonly int _capacity;
    private long _dropped;

    public EventRing(int capacity = 4096) => _capacity = capacity;

    public long Dropped => Interlocked.Read(ref _dropped);

    public void Push(in EventEnvelope e)
    {
        _queue.Enqueue(e);
        while (_queue.Count > _capacity && _queue.TryDequeue(out _)) { Interlocked.Increment(ref _dropped); }
    }

    public bool TryPop(out EventEnvelope e) => _queue.TryDequeue(out e);
}
