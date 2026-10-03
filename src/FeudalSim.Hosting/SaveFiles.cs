using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Persistence;

namespace FeudalSim.Hosting;

/// <summary>File IO for saves and logs (20 §9). The Sim only ever sees streams.</summary>
public static class SaveFiles
{
    /// <summary>Writes a snapshot atomically: temp file → flush to disk → rename over the target.</summary>
    public static void WriteSnapshotAtomic(string path, SaveImage image)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            SaveCodec.Serialize(stream, image);
            stream.Flush(flushToDisk: true);
        }

        File.Move(tmp, path, overwrite: true);
    }

    public static SaveImage ReadSnapshot(string path)
    {
        using var stream = File.OpenRead(path);
        return SaveCodec.Deserialize(stream);
    }
}

/// <summary>
/// An append-only input-log segment. Opening it recovers from a crash by truncating a torn final
/// record (detected by length or CRC).
/// </summary>
public sealed class InputLogFile : IDisposable
{
    private readonly FileStream _stream;

    private InputLogFile(FileStream stream, IReadOnlyList<CommandEnvelope> existing, long truncatedBytes)
    {
        _stream = stream;
        Existing = existing;
        TruncatedBytes = truncatedBytes;
    }

    /// <summary>Commands already in the file when it was opened (after recovery).</summary>
    public IReadOnlyList<CommandEnvelope> Existing { get; }

    /// <summary>Bytes removed from a torn tail during recovery (0 if the file was intact).</summary>
    public long TruncatedBytes { get; }

    public static InputLogFile OpenOrCreate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var commands = LogCodec.ReadCommands(stream, out var valid);
        var truncated = stream.Length - valid;
        if (truncated > 0) { stream.SetLength(valid); }
        stream.Seek(0, SeekOrigin.End);
        return new InputLogFile(stream, commands, truncated);
    }

    public static IReadOnlyList<CommandEnvelope> ReadAll(string path)
    {
        using var stream = File.OpenRead(path);
        return LogCodec.ReadCommands(stream, out _);
    }

    public void Append(in CommandEnvelope command) => LogCodec.WriteCommand(_stream, command);

    public void Flush() => _stream.Flush(flushToDisk: true);

    public void Dispose()
    {
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
    }
}
