using System.Buffers.Binary;
using System.IO.Hashing;
using FeudalSim.Sim.Commands;
using MessagePack;

namespace FeudalSim.Sim.Persistence;

/// <summary>
/// Append-only log records (20 §9.3): <c>[u32 length][u32 crc32][msgpack]</c>. A torn final record is
/// detected by length or CRC, and everything after the last good record is reported for truncation.
/// </summary>
public static class LogCodec
{
    public const int HeaderSize = 8;

    public static void WriteCommand(Stream destination, in CommandEnvelope command)
        => WriteRecord(destination, MessagePackSerializer.Serialize(command));

    public static void WriteRecord(Stream destination, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], Crc32.HashToUInt32(payload));
        destination.Write(header);
        destination.Write(payload);
    }

    /// <summary>Reads commands until the end or the first torn record. <paramref name="validLength"/> is the byte length of the good prefix.</summary>
    public static List<CommandEnvelope> ReadCommands(Stream source, out long validLength)
    {
        var commands = new List<CommandEnvelope>();
        validLength = 0;
        Span<byte> header = stackalloc byte[HeaderSize];
        while (true)
        {
            if (!TryReadExactly(source, header)) { return commands; }
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (length > 64 * 1024 * 1024) { return commands; }
            var payload = new byte[length];
            if (!TryReadExactly(source, payload) || Crc32.HashToUInt32(payload) != crc) { return commands; }
            commands.Add(MessagePackSerializer.Deserialize<CommandEnvelope>(payload));
            validLength += HeaderSize + length;
        }
    }

    private static bool TryReadExactly(Stream source, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = source.Read(buffer[read..]);
            if (n == 0) { return false; }
            read += n;
        }

        return true;
    }
}
