namespace FeudalSim.Sim;

/// <summary>Processes rows [start, end) of a table. Must write only those rows (20 §7.3 R1).</summary>
public delegate void ChunkJob(int start, int end);

/// <summary>
/// Port for parallel phases. Rows are split into fixed chunks of <see cref="ChunkSize"/> regardless of
/// thread count (R2), so results never depend on scheduling. Threads live in Hosting, never in the Sim.
/// </summary>
public interface IJobScheduler
{
    public const int ChunkSize = 64;

    /// <summary>Below this many rows a phase runs serially.</summary>
    public const int ParallelThreshold = 128;

    void ForEachChunk(int count, ChunkJob job);
}

/// <summary>The default scheduler: every chunk in order on the calling thread.</summary>
public sealed class SerialJobScheduler : IJobScheduler
{
    public static readonly SerialJobScheduler Instance = new();

    public void ForEachChunk(int count, ChunkJob job)
    {
        for (var start = 0; start < count; start += IJobScheduler.ChunkSize)
        {
            job(start, Math.Min(count, start + IJobScheduler.ChunkSize));
        }
    }
}
