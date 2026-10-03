using System.Runtime.InteropServices;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;
using MessagePack;

namespace FeudalSim.Sim.Persistence;

/// <summary>
/// Captures and restores sim state as a <see cref="SaveImage"/>, and (de)serializes it to a stream.
/// Pure: no file IO here — Hosting owns files and atomic writes.
/// </summary>
public static class SaveCodec
{
    public const string PeopleTable = "people";

    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

    /// <summary>Persisted Person columns: name, layout version, element size. Bump the version when a struct changes.</summary>
    public static readonly IReadOnlyList<(string Name, int LayoutVersion, int ElementSize)> PeopleColumns =
    [
        ("id", 1, Marshal.SizeOf<EntityId>()),
        ("core", 1, Marshal.SizeOf<PersonCore>()),
        ("transform", 1, Marshal.SizeOf<Transform>()),
        ("needs", 1, Marshal.SizeOf<Needs>()),
        ("lod", 1, Marshal.SizeOf<LodState>()),
        ("wander", 1, Marshal.SizeOf<WanderState>()),
    ];

    public static SaveImage Capture(SimWorld world)
    {
        var p = world.People;
        var people = new TableChunk { Table = PeopleTable, RowCount = p.Count };
        people.Columns.Add(Column("id", p.Ids));
        people.Columns.Add(Column("core", (ReadOnlySpan<PersonCore>)p.Core));
        people.Columns.Add(Column("transform", (ReadOnlySpan<Transform>)p.Transforms));
        people.Columns.Add(Column("needs", (ReadOnlySpan<Needs>)p.Needs));
        people.Columns.Add(Column("lod", (ReadOnlySpan<LodState>)p.Lod));
        people.Columns.Add(Column("wander", (ReadOnlySpan<WanderState>)p.Wander));
        people.Strings.Add(new StringColumn { Name = "name", Values = p.Names.ToArray() });

        var counters = new ulong[256];
        for (var k = 0; k < counters.Length; k++) { counters[k] = world.Ids.Peek((EntityKind)k); }

        return new SaveImage
        {
            Header = new SaveHeader
            {
                WorldSeed = world.WorldSeed,
                Step = world.Clock.Step,
                GameMs = world.Clock.GameMs,
                DayLengthMinutes = world.Clock.DayLengthMinutes,
                StateHash = StateHasher.Hash(world),
                EventSeq = world.EventSeq,
            },
            IdCounters = counters,
            Tables = [people],
        };
    }

    /// <summary>Rebuilds a world from an image. The caller registers systems (configuration is not state).</summary>
    public static SimWorld Restore(SaveImage image, out IReadOnlyList<string> warnings)
    {
        var h = image.Header;
        if (h.Magic != SaveHeader.ExpectedMagic) { throw new InvalidDataException("Not a FeudalSim snapshot."); }
        if (h.FormatVersion != SaveHeader.CurrentFormatVersion) { throw new InvalidDataException($"Unsupported snapshot format {h.FormatVersion}."); }

        var world = new SimWorld(h.WorldSeed, h.GameMs, h.DayLengthMinutes);
        world.Clock.Restore(h.Step, h.GameMs, h.DayLengthMinutes);
        world.RestoreEventSeq(h.EventSeq);
        for (var k = 0; k < image.IdCounters.Length; k++) { world.Ids.Restore((EntityKind)k, image.IdCounters[k]); }

        var notes = new List<string>();
        var chunk = image.Tables.FirstOrDefault(t => t.Table == PeopleTable)
            ?? throw new InvalidDataException("Snapshot has no people table.");
        var names = chunk.Strings.FirstOrDefault(s => s.Name == "name")?.Values ?? new string[chunk.RowCount];
        var ids = ReadColumn<EntityId>(chunk, "id", notes) ?? throw new InvalidDataException("People table has no id column.");

        var p = world.People;
        p.ResetRows(ids, names);
        CopyOrDefault(chunk, "core", p.Core, notes, static _ => default);
        CopyOrDefault(chunk, "transform", p.Transforms, notes, static _ => default);
        CopyOrDefault(chunk, "needs", p.Needs, notes, static _ => Needs.Full);
        CopyOrDefault(chunk, "lod", p.Lod, notes, static _ => new LodState { Tier = LodTier.Lod1 });
        var transforms = p.Transforms.ToArray();
        CopyOrDefault(chunk, "wander", p.Wander, notes, i => new WanderState { HomeX = transforms[i].X, HomeZ = transforms[i].Z });

        foreach (var c in chunk.Columns.Where(c => PeopleColumns.All(k => k.Name != c.Name)))
        {
            notes.Add($"Dropped unknown column people.{c.Name}.");
        }

        warnings = notes;
        return world;
    }

    public static void Serialize(Stream destination, SaveImage image)
        => MessagePackSerializer.Serialize(destination, image, Options);

    public static SaveImage Deserialize(Stream source)
        => MessagePackSerializer.Deserialize<SaveImage>(source, Options);

    private static ColumnBlock Column<T>(string name, ReadOnlySpan<T> values) where T : struct
    {
        var spec = PeopleColumns.First(c => c.Name == name);
        return new ColumnBlock
        {
            Name = name,
            LayoutVersion = spec.LayoutVersion,
            ElementSize = spec.ElementSize,
            Data = MemoryMarshal.AsBytes(values).ToArray(),
        };
    }

    private static T[]? ReadColumn<T>(TableChunk chunk, string name, List<string> notes) where T : struct
    {
        var block = chunk.Columns.FirstOrDefault(c => c.Name == name);
        if (block is null) { return null; }
        var spec = PeopleColumns.First(c => c.Name == name);
        if (block.LayoutVersion != spec.LayoutVersion || block.ElementSize != spec.ElementSize)
        {
            // No column migrations are registered yet (M0–M2 saves are dev artifacts, 20 §9.5).
            notes.Add($"Column people.{name} has layout v{block.LayoutVersion}/{block.ElementSize} B, expected v{spec.LayoutVersion}/{spec.ElementSize} B; using defaults.");
            return null;
        }

        if (block.Data.Length != chunk.RowCount * block.ElementSize)
        {
            throw new InvalidDataException($"Column people.{name} has {block.Data.Length} bytes for {chunk.RowCount} rows.");
        }

        return MemoryMarshal.Cast<byte, T>(block.Data).ToArray();
    }

    private static void CopyOrDefault<T>(TableChunk chunk, string name, Span<T> target, List<string> notes, Func<int, T> fallback)
        where T : struct
    {
        var values = ReadColumn<T>(chunk, name, notes);
        if (values is null)
        {
            if (chunk.Columns.All(c => c.Name != name)) { notes.Add($"Missing column people.{name}; using defaults."); }
            for (var i = 0; i < target.Length; i++) { target[i] = fallback(i); }
            return;
        }

        values.AsSpan().CopyTo(target);
    }
}
