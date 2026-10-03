using MessagePack;

namespace FeudalSim.Sim.Persistence;

/// <summary>Snapshot header (20 §9.4). Format v0: one LZ4-compressed MessagePack document.</summary>
[MessagePackObject]
public sealed class SaveHeader
{
    public const string ExpectedMagic = "FSNP";
    public const int CurrentFormatVersion = 0;
    public const int CurrentSaveSchemaVersion = 1;

    [Key(0)] public string Magic { get; set; } = ExpectedMagic;
    [Key(1)] public int FormatVersion { get; set; } = CurrentFormatVersion;
    [Key(2)] public int SaveSchemaVersion { get; set; } = CurrentSaveSchemaVersion;
    [Key(3)] public ulong WorldSeed { get; set; }
    [Key(4)] public long Step { get; set; }
    [Key(5)] public long GameMs { get; set; }
    [Key(6)] public int DayLengthMinutes { get; set; }
    [Key(7)] public ulong StateHash { get; set; }
    [Key(8)] public long EventSeq { get; set; }
    [Key(9)] public ulong ContentHash { get; set; }
}

/// <summary>One blittable column: <c>RowCount × ElementSize</c> little-endian bytes.</summary>
[MessagePackObject]
public sealed class ColumnBlock
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public int LayoutVersion { get; set; }
    [Key(2)] public int ElementSize { get; set; }
    [Key(3)] public byte[] Data { get; set; } = [];
}

[MessagePackObject]
public sealed class StringColumn
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public string[] Values { get; set; } = [];
}

/// <summary>A column-tolerant table: the loader maps columns by name (20 §9.4).</summary>
[MessagePackObject]
public sealed class TableChunk
{
    [Key(0)] public string Table { get; set; } = "";
    [Key(1)] public int RowCount { get; set; }
    [Key(2)] public List<ColumnBlock> Columns { get; set; } = [];
    [Key(3)] public List<StringColumn> Strings { get; set; } = [];
}

[MessagePackObject]
public sealed class SaveImage
{
    [Key(0)] public SaveHeader Header { get; set; } = new();

    /// <summary>Last issued id serial per <see cref="Core.EntityKind"/> (index = kind).</summary>
    [Key(1)] public ulong[] IdCounters { get; set; } = [];

    [Key(2)] public List<TableChunk> Tables { get; set; } = [];
}
