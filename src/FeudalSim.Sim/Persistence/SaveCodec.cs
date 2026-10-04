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
    public const string CampTable = "camp";

    public const string RelationshipsTable = "relationships";

    private static TableChunk RelationshipsChunk(Social.RelationshipStore store)
    {
        var (edges, mods) = store.Export();
        var chunk = new TableChunk { Table = RelationshipsTable, RowCount = edges.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "edges", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Social.RelationshipStore.EdgeRecord>(), Data = MemoryMarshal.AsBytes(edges.AsSpan()).ToArray() });
        chunk.Columns.Add(new ColumnBlock { Name = "mods", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Social.ModSlot>(), Data = MemoryMarshal.AsBytes(mods.AsSpan()).ToArray() });
        chunk.Columns.Add(new ColumnBlock { Name = "meta", LayoutVersion = 1, ElementSize = 1, Data = [store.ShipmatesSeeded ? (byte)1 : (byte)0] });
        return chunk;
    }

    public const string MemoriesTable = "memories";
    public const string ClaimsTable = "claims";
    public const string BeliefsTable = "beliefs";
    public const string RenownTable = "renown";

    public const string DecisionsTable = "decisions";
    public const string AiTable = "ai";

    /// <summary>Open DPs (MessagePack: menus carry strings), long-shot counters (blittable) and the DP ordinal.</summary>
    private static TableChunk DecisionsChunk(Decisions.DecisionRulesEngine dre)
    {
        var (open, longShots, ordinalStep, ordinal) = dre.Export();
        var chunk = new TableChunk { Table = DecisionsTable, RowCount = open.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "open", LayoutVersion = 1, ElementSize = 0, Data = MessagePackSerializer.Serialize(open) });
        chunk.Columns.Add(new ColumnBlock { Name = "long_shots", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Decisions.DecisionRulesEngine.LongShotRow>(), Data = MemoryMarshal.AsBytes(longShots.AsSpan()).ToArray() });
        chunk.Columns.Add(new ColumnBlock { Name = "ordinal", LayoutVersion = 1, ElementSize = 12, Data = [.. BitConverter.GetBytes(ordinalStep), .. BitConverter.GetBytes(ordinal)] });
        return chunk;
    }

    /// <summary>Pending AI requests (MessagePack) and the request counter (31 R27: a save normally waits for none in flight).</summary>
    private static TableChunk AiChunk(SimWorld world)
    {
        var (pending, seq) = world.ExportAi();
        var chunk = new TableChunk { Table = AiTable, RowCount = pending.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "pending", LayoutVersion = 1, ElementSize = 0, Data = MessagePackSerializer.Serialize(pending) });
        chunk.Columns.Add(new ColumnBlock { Name = "seq", LayoutVersion = 1, ElementSize = 8, Data = BitConverter.GetBytes(seq) });
        return chunk;
    }

    private static TableChunk RenownChunk(Social.ReputationStore store)
    {
        var rows = store.Export();
        var chunk = new TableChunk { Table = RenownTable, RowCount = rows.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "rows", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Social.ReputationStore.Row>(), Data = MemoryMarshal.AsBytes(rows.AsSpan()).ToArray() });
        return chunk;
    }

    private static TableChunk ClaimsChunk(Social.ClaimStore store)
    {
        var rows = store.Export();
        var chunk = new TableChunk { Table = ClaimsTable, RowCount = rows.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "rows", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Social.Claim>(), Data = MemoryMarshal.AsBytes(rows.AsSpan()).ToArray() });
        return chunk;
    }

    private static TableChunk BeliefsChunk(Social.BeliefStore store)
    {
        var (rows, told) = store.Export();
        var chunk = new TableChunk { Table = BeliefsTable, RowCount = rows.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "rows", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Social.BeliefStore.Row>(), Data = MemoryMarshal.AsBytes(rows.AsSpan()).ToArray() });
        chunk.Columns.Add(new ColumnBlock { Name = "told_to", LayoutVersion = 1, ElementSize = 8, Data = MemoryMarshal.AsBytes(told.AsSpan()).ToArray() });
        return chunk;
    }

    private static TableChunk MemoriesChunk(Social.MemoryStore store)
    {
        var rows = store.Export();
        var chunk = new TableChunk { Table = MemoriesTable, RowCount = rows.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "rows", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Social.MemoryStore.Row>(), Data = MemoryMarshal.AsBytes(rows.AsSpan()).ToArray() });
        return chunk;
    }

    private static TableChunk CampChunk(in CampRecord camp)
    {
        var chunk = new TableChunk { Table = CampTable, RowCount = 1 };
        chunk.Columns.Add(new ColumnBlock
        {
            Name = "camp", LayoutVersion = 1, ElementSize = Marshal.SizeOf<CampRecord>(),
            Data = MemoryMarshal.AsBytes(new ReadOnlySpan<CampRecord>(in camp)).ToArray(),
        });
        return chunk;
    }

    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

    /// <summary>Persisted Person columns: name, layout version, element size. Bump the version when a struct changes.</summary>
    public static readonly IReadOnlyList<(string Name, int LayoutVersion, int ElementSize)> PeopleColumns =
    [
        ("id", 1, Marshal.SizeOf<EntityId>()),
        ("core", 1, Marshal.SizeOf<PersonCore>()),
        ("transform", 1, Marshal.SizeOf<Transform>()),
        ("needs", 1, Marshal.SizeOf<Needs>()),
        ("lod", 2, Marshal.SizeOf<LodState>()),   // v2: Embodied, FarSinceStep (M0-13)
        ("wander", 1, Marshal.SizeOf<WanderState>()),
        ("attributes", 1, Marshal.SizeOf<Attributes>()),     // M1-01
        ("personality", 1, Marshal.SizeOf<Personality>()),
        ("emotions", 1, Marshal.SizeOf<Emotions>()),
        ("mood", 1, Marshal.SizeOf<Mood>()),
        ("activity", 1, Marshal.SizeOf<ActivityState>()),     // M1-02a
        ("skill_levels", 1, PersonTable.SkillCount),          // 28 bytes per row, skill-handle order
        ("skill_aptitude", 1, PersonTable.SkillCount),
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
        people.Columns.Add(Column("attributes", (ReadOnlySpan<Attributes>)p.Attributes));
        people.Columns.Add(Column("personality", (ReadOnlySpan<Personality>)p.Personality));
        people.Columns.Add(Column("emotions", (ReadOnlySpan<Emotions>)p.Emotions));
        people.Columns.Add(Column("mood", (ReadOnlySpan<Mood>)p.Mood));
        people.Columns.Add(Column("activity", (ReadOnlySpan<ActivityState>)p.Activity));
        people.Columns.Add(Column("skill_levels", (ReadOnlySpan<byte>)p.SkillLevelsAll));
        people.Columns.Add(Column("skill_aptitude", (ReadOnlySpan<byte>)p.SkillAptitudeAll));
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
                ContentHash = world.Content.Hash,
                LastCommandSeq = world.LastCommandSeq,
            },
            IdCounters = counters,
            Tables = [people, CampChunk(world.Camp), RelationshipsChunk(world.Relationships), MemoriesChunk(world.Memories), ClaimsChunk(world.Claims), BeliefsChunk(world.Beliefs), RenownChunk(world.Reputation), DecisionsChunk(world.Decisions), AiChunk(world)],
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
        world.RestoreLastCommandSeq(h.LastCommandSeq);
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
        CopyOrDefault(chunk, "attributes", p.Attributes, notes, static _ => new Attributes { Strength = 5, Endurance = 5, Dexterity = 5, Perception = 5, Intellect = 5, Charisma = 5 });
        CopyOrDefault(chunk, "personality", p.Personality, notes, static _ => new Personality
        {
            Curiosity = 50, Diligence = 50, Sociability = 50, Warmth = 50, Volatility = 50,
            Values = new ValueBlock { Family = 50, Wealth = 50, Status = 50, Honor = 50, Tradition = 50, Faith = 50, Fairness = 50, Freedom = 50, Loyalty = 50 },
            Culture = Personality.None, Profession = Personality.None,
        });
        CopyOrDefault(chunk, "emotions", p.Emotions, notes, _ => new Emotions { UpdatedGameMs = h.GameMs });
        CopyOrDefault(chunk, "mood", p.Mood, notes, static _ => default);
        CopyOrDefault(chunk, "activity", p.Activity, notes, static _ => new ActivityState { Action = -1, Level = Content.ActivityLevel.Light });
        CopyBytesOrDefault(chunk, "skill_levels", p.SkillLevelsAll, notes, 0);
        CopyBytesOrDefault(chunk, "skill_aptitude", p.SkillAptitudeAll, notes, 100);

        foreach (var c in chunk.Columns.Where(c => PeopleColumns.All(k => k.Name != c.Name)))
        {
            notes.Add($"Dropped unknown column people.{c.Name}.");
        }

        if (image.Tables.FirstOrDefault(t => t.Table == CampTable) is { } camp && camp.Columns.FirstOrDefault(c => c.Name == "camp") is { } block
            && block.ElementSize == Marshal.SizeOf<CampRecord>() && block.Data.Length == block.ElementSize)
        {
            world.Camp = MemoryMarshal.Read<CampRecord>(block.Data);
        }

        if (image.Tables.FirstOrDefault(t => t.Table == RelationshipsTable) is { } rel)
        {
            var edges = rel.Columns.FirstOrDefault(c => c.Name == "edges");
            var mods = rel.Columns.FirstOrDefault(c => c.Name == "mods");
            var meta = rel.Columns.FirstOrDefault(c => c.Name == "meta");
            if (edges is { ElementSize: var es } && es == Marshal.SizeOf<Social.RelationshipStore.EdgeRecord>() && mods is { ElementSize: var ms } && ms == Marshal.SizeOf<Social.ModSlot>())
            {
                world.Relationships.Import(MemoryMarshal.Cast<byte, Social.RelationshipStore.EdgeRecord>(edges.Data).ToArray(),
                    MemoryMarshal.Cast<byte, Social.ModSlot>(mods.Data).ToArray(), meta?.Data is [1]);
            }
            else
            {
                notes.Add("Relationships table has an unknown layout; relationships reset.");
            }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == MemoriesTable)?.Columns.FirstOrDefault(c => c.Name == "rows") is { } mrows)
        {
            if (mrows.ElementSize == Marshal.SizeOf<Social.MemoryStore.Row>()) { world.Memories.Import(MemoryMarshal.Cast<byte, Social.MemoryStore.Row>(mrows.Data).ToArray()); }
            else { notes.Add("Memories table has an unknown layout; memories reset."); }
        }

        var claims = image.Tables.FirstOrDefault(t => t.Table == ClaimsTable);
        var beliefs = image.Tables.FirstOrDefault(t => t.Table == BeliefsTable);
        if (claims?.Columns.FirstOrDefault(c => c.Name == "rows") is { } crows && beliefs?.Columns.FirstOrDefault(c => c.Name == "rows") is { } brows
            && beliefs.Columns.FirstOrDefault(c => c.Name == "told_to") is { } told)
        {
            if (crows.ElementSize == Marshal.SizeOf<Social.Claim>() && brows.ElementSize == Marshal.SizeOf<Social.BeliefStore.Row>())
            {
                world.Claims.Import(MemoryMarshal.Cast<byte, Social.Claim>(crows.Data).ToArray());
                world.Beliefs.Import(MemoryMarshal.Cast<byte, Social.BeliefStore.Row>(brows.Data).ToArray(), MemoryMarshal.Cast<byte, ulong>(told.Data).ToArray());
            }
            else { notes.Add("Claims/beliefs tables have an unknown layout; rumors reset."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == RenownTable)?.Columns.FirstOrDefault(c => c.Name == "rows") is { } rrows)
        {
            if (rrows.ElementSize == Marshal.SizeOf<Social.ReputationStore.Row>()) { world.Reputation.Import(MemoryMarshal.Cast<byte, Social.ReputationStore.Row>(rrows.Data).ToArray()); }
            else { notes.Add("Renown table has an unknown layout; renown recomputes tonight."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == DecisionsTable) is { } dec)
        {
            var open = dec.Columns.FirstOrDefault(c => c.Name == "open");
            var shots = dec.Columns.FirstOrDefault(c => c.Name == "long_shots");
            var ord = dec.Columns.FirstOrDefault(c => c.Name == "ordinal");
            if (open is { LayoutVersion: 1 } && shots is { ElementSize: var ss } && ss == Marshal.SizeOf<Decisions.DecisionRulesEngine.LongShotRow>() && ord is { Data.Length: 12 })
            {
                world.Decisions.Import(MessagePackSerializer.Deserialize<Decisions.DecisionRulesEngine.SavedDp[]>(open.Data),
                    MemoryMarshal.Cast<byte, Decisions.DecisionRulesEngine.LongShotRow>(shots.Data).ToArray(), BitConverter.ToInt64(ord.Data, 0), BitConverter.ToInt32(ord.Data, 8));
            }
            else { notes.Add("Decisions table has an unknown layout; open decision points dropped."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == AiTable) is { } ai)
        {
            if (ai.Columns.FirstOrDefault(c => c.Name == "pending") is { LayoutVersion: 1 } pending && ai.Columns.FirstOrDefault(c => c.Name == "seq") is { Data.Length: 8 } seq)
            {
                world.ImportAi(MessagePackSerializer.Deserialize<Ai.AiRequest[]>(pending.Data), BitConverter.ToInt64(seq.Data, 0));
            }
            else { notes.Add("AI table has an unknown layout; pending AI requests dropped."); }
        }

        warnings = notes;
        return world;
    }

    public static void Serialize(Stream destination, SaveImage image)
        => MessagePackSerializer.Serialize(destination, image, Options);

    public static SaveImage Deserialize(Stream source)
        => MessagePackSerializer.Deserialize<SaveImage>(source, Options);

    /// <summary>Byte columns with several bytes per row (skills): the element size is the per-row byte count.</summary>
    private static void CopyBytesOrDefault(TableChunk chunk, string name, Span<byte> target, List<string> notes, byte fallback)
    {
        var block = chunk.Columns.FirstOrDefault(c => c.Name == name);
        var spec = PeopleColumns.First(c => c.Name == name);
        if (block is null || block.LayoutVersion != spec.LayoutVersion || block.ElementSize != spec.ElementSize)
        {
            notes.Add(block is null ? $"Missing column people.{name}; using defaults." : $"Column people.{name} has layout v{block.LayoutVersion}/{block.ElementSize} B, expected v{spec.LayoutVersion}/{spec.ElementSize} B; using defaults.");
            target.Fill(fallback);
            return;
        }

        if (block.Data.Length != target.Length) { throw new InvalidDataException($"Column people.{name} has {block.Data.Length} bytes for {chunk.RowCount} rows."); }
        block.Data.CopyTo(target);
    }

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
