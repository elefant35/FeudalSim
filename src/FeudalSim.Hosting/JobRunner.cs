using FeudalSim.Sim;

namespace FeudalSim.Hosting;

/// <summary>
/// Runs fixed-size chunks on dedicated worker threads (not the ThreadPool, so it never competes with
/// AI gateway I/O). Chunk boundaries never depend on thread count, so results are identical for any
/// <c>threads</c> value (20 §7.3 R2, R5).
/// </summary>
public sealed class JobRunner : IJobScheduler, IDisposable
{
    private readonly Thread[] _workers;
    private readonly SemaphoreSlim _start;
    private readonly CountdownEvent _done;
    private ChunkJob? _job;
    private int _count;
    private int _nextChunk;
    private volatile bool _stopping;

    public JobRunner(int threads)
    {
        if (threads < 1) { throw new ArgumentOutOfRangeException(nameof(threads)); }
        Threads = threads;
        _start = new SemaphoreSlim(0);
        _done = new CountdownEvent(1);
        _workers = new Thread[threads - 1];
        for (var i = 0; i < _workers.Length; i++)
        {
            _workers[i] = new Thread(WorkerLoop) { IsBackground = true, Name = $"sim-worker-{i + 1}" };
            _workers[i].Start();
        }
    }

    public int Threads { get; }

    public void ForEachChunk(int count, ChunkJob job)
    {
        if (Threads == 1 || count < IJobScheduler.ParallelThreshold)
        {
            SerialJobScheduler.Instance.ForEachChunk(count, job);
            return;
        }

        _job = job;
        _count = count;
        _nextChunk = -1;
        _done.Reset(_workers.Length + 1);
        _start.Release(_workers.Length);
        DrainChunks();
        _done.Signal();
        _done.Wait();
        _job = null;
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _start.Wait();
            if (_stopping) { return; }
            DrainChunks();
            _done.Signal();
        }
    }

    private void DrainChunks()
    {
        var job = _job!;
        var chunks = (_count + IJobScheduler.ChunkSize - 1) / IJobScheduler.ChunkSize;
        int chunk;
        while ((chunk = Interlocked.Increment(ref _nextChunk)) < chunks)
        {
            var start = chunk * IJobScheduler.ChunkSize;
            job(start, Math.Min(_count, start + IJobScheduler.ChunkSize));
        }
    }

    public void Dispose()
    {
        _stopping = true;
        if (_workers.Length > 0) { _start.Release(_workers.Length); }
        foreach (var w in _workers) { w.Join(); }
        _start.Dispose();
        _done.Dispose();
    }
}
