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
        chunk.Columns.Add(new ColumnBlock { Name = "edges", LayoutVersion = 3, ElementSize = Marshal.SizeOf<Social.RelationshipStore.EdgeRecord>(), Data = MemoryMarshal.AsBytes(edges.AsSpan()).ToArray() });
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

    public const string ConversationsTable = "conversations";
    public const string PlayerTable = "player";
    public const string WeatherTable = "weather";
    public const string InjuriesTable = "injuries";
    public const string InventoryTable = "inventory";
    public const string ProcessesTable = "processes";
    public const string ConfrontationsTable = "confrontations";
    public const string FavorsTable = "favors";
    public const string HoldingsTable = "holdings";
    public const string NegotiationsTable = "negotiations";

    private static TableChunk HoldingsChunk(Economy.Holdings store)
    {
        var coin = store.Export();
        var chunk = new TableChunk { Table = HoldingsTable, RowCount = coin.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "coin", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Economy.Holdings.CoinRow>(), Data = MemoryMarshal.AsBytes(coin.AsSpan()).ToArray() });
        return chunk;
    }

    private static TableChunk ProcessesChunk(Crafting.ProcessStore store)
    {
        var chunk = new TableChunk { Table = ProcessesTable, RowCount = store.Count };
        chunk.Columns.Add(new ColumnBlock { Name = "state", LayoutVersion = 1, ElementSize = 0, Data = MessagePackSerializer.Serialize(store.Export()) });
        return chunk;
    }

    private static TableChunk InventoryChunk(Items.InventoryStore store)
    {
        var (slots, instances) = store.Export();
        var chunk = new TableChunk { Table = InventoryTable, RowCount = slots.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "slots", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Items.InventoryStore.Row>(), Data = MemoryMarshal.AsBytes(slots.AsSpan()).ToArray() });
        chunk.Columns.Add(new ColumnBlock { Name = "instances", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Items.ItemInstance>(), Data = MemoryMarshal.AsBytes(instances.AsSpan()).ToArray() });
        return chunk;
    }

    private static TableChunk NegotiationsChunk(Economy.NegotiationStore store)
    {
        var (open, lastId) = store.Export();
        var chunk = new TableChunk { Table = NegotiationsTable, RowCount = open.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "open", LayoutVersion = 1, ElementSize = 0, Data = MessagePackSerializer.Serialize(open) });
        chunk.Columns.Add(new ColumnBlock { Name = "last_id", LayoutVersion = 1, ElementSize = 8, Data = BitConverter.GetBytes(lastId) });
        return chunk;
    }

    private static TableChunk FavorsChunk(Social.FavorStore store)
    {
        var (open, lastId) = store.Export();
        var chunk = new TableChunk { Table = FavorsTable, RowCount = open.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "open", LayoutVersion = 1, ElementSize = 0, Data = MessagePackSerializer.Serialize(open) });
        chunk.Columns.Add(new ColumnBlock { Name = "last_id", LayoutVersion = 1, ElementSize = 8, Data = BitConverter.GetBytes(lastId) });
        return chunk;
    }

    private static TableChunk ConfrontationsChunk(Social.ConfrontationStore store)
    {
        var (open, lastId) = store.Export();
        var chunk = new TableChunk { Table = ConfrontationsTable, RowCount = open.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "open", LayoutVersion = 1, ElementSize = 0, Data = MessagePackSerializer.Serialize(open) });
        chunk.Columns.Add(new ColumnBlock { Name = "last_id", LayoutVersion = 1, ElementSize = 8, Data = BitConverter.GetBytes(lastId) });
        return chunk;
    }

    private static TableChunk PlayerChunk(in PlayerState player)
    {
        var chunk = new TableChunk { Table = PlayerTable, RowCount = 1 };
        chunk.Columns.Add(new ColumnBlock { Name = "pose", LayoutVersion = 1, ElementSize = Marshal.SizeOf<PlayerState>(), Data = MemoryMarshal.AsBytes(new ReadOnlySpan<PlayerState>(in player)).ToArray() });
        return chunk;
    }

    private static TableChunk InjuriesChunk(Health.InjuryStore store)
    {
        var (rows, lastId) = store.Export();
        var chunk = new TableChunk { Table = InjuriesTable, RowCount = rows.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "rows", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Health.InjuryStore.Row>(), Data = MemoryMarshal.AsBytes(rows.AsSpan()).ToArray() });
        chunk.Columns.Add(new ColumnBlock { Name = "last_id", LayoutVersion = 1, ElementSize = 8, Data = BitConverter.GetBytes(lastId) });
        return chunk;
    }

    private static TableChunk WeatherChunk(in Climate.WeatherState weather)
    {
        var chunk = new TableChunk { Table = WeatherTable, RowCount = 1 };
        chunk.Columns.Add(new ColumnBlock { Name = "state", LayoutVersion = 1, ElementSize = Marshal.SizeOf<Climate.WeatherState>(), Data = MemoryMarshal.AsBytes(new ReadOnlySpan<Climate.WeatherState>(in weather)).ToArray() });
        return chunk;
    }

    private static TableChunk ConversationsChunk(Dialogue.ConversationStore store)
    {
        var (open, lastId) = store.Export();
        var chunk = new TableChunk { Table = ConversationsTable, RowCount = open.Length };
        chunk.Columns.Add(new ColumnBlock { Name = "open", LayoutVersion = 1, ElementSize = 0, Data = MessagePackSerializer.Serialize(open) });
        chunk.Columns.Add(new ColumnBlock { Name = "last_id", LayoutVersion = 1, ElementSize = 8, Data = BitConverter.GetBytes(lastId) });
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
        ("skill_progress", 1, PersonTable.SkillCount * Marshal.SizeOf<SkillProgress>()),   // M2-04
        ("attribute_training", 1, Marshal.SizeOf<AttributeTraining>()),                 // M2-04
        ("worn", 1, Marshal.SizeOf<Worn>()),                                             // M2-05a
        ("body", 1, Marshal.SizeOf<Body>()),                                             // M2-05a
        ("stamina", 1, Marshal.SizeOf<Stamina>()),                                       // M2-05b
        ("vitals", 1, Marshal.SizeOf<Health.Vitals>()),                                  // M2-06a
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
        people.Columns.Add(Column("skill_progress", (ReadOnlySpan<SkillProgress>)p.SkillProgressAll));
        people.Columns.Add(Column("attribute_training", (ReadOnlySpan<AttributeTraining>)p.Training));
        people.Columns.Add(Column("worn", (ReadOnlySpan<Worn>)p.Worn));
        people.Columns.Add(Column("body", (ReadOnlySpan<Body>)p.Body));
        people.Columns.Add(Column("stamina", (ReadOnlySpan<Stamina>)p.Stamina));
        people.Columns.Add(Column("vitals", (ReadOnlySpan<Health.Vitals>)p.Vitals));
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
            Tables = [people, CampChunk(world.Camp), RelationshipsChunk(world.Relationships), MemoriesChunk(world.Memories), ClaimsChunk(world.Claims), BeliefsChunk(world.Beliefs), RenownChunk(world.Reputation), DecisionsChunk(world.Decisions), AiChunk(world), ConversationsChunk(world.Conversations), PlayerChunk(world.Player), ConfrontationsChunk(world.Confrontations), FavorsChunk(world.Favors), HoldingsChunk(world.Holdings), NegotiationsChunk(world.Negotiations), WeatherChunk(world.WeatherRef), InjuriesChunk(world.Injuries), InventoryChunk(world.Inventory), ProcessesChunk(world.Processes)],
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
        CopyOrDefault(chunk, "skill_progress", p.SkillProgressAll, notes, static _ => default);
        CopyOrDefault(chunk, "attribute_training", p.Training, notes, static _ => default);
        CopyOrDefault(chunk, "worn", p.Worn, notes, static _ => Worn.None);
        CopyOrDefault(chunk, "body", p.Body, notes, static _ => default);
        CopyOrDefault(chunk, "stamina", p.Stamina, notes, static _ => new Stamina { Value = Survival.StaminaRules.Full });
        CopyOrDefault(chunk, "vitals", p.Vitals, notes, static _ => Health.Vitals.Healthy);

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

        if (image.Tables.FirstOrDefault(t => t.Table == ConversationsTable) is { } convs)
        {
            if (convs.Columns.FirstOrDefault(c => c.Name == "open") is { LayoutVersion: 1 } open && convs.Columns.FirstOrDefault(c => c.Name == "last_id") is { Data.Length: 8 } last)
            {
                world.Conversations.Import(MessagePackSerializer.Deserialize<Dialogue.Conversation[]>(open.Data), BitConverter.ToUInt64(last.Data, 0));
            }
            else { notes.Add("Conversations table has an unknown layout; conversations dropped."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == PlayerTable)?.Columns.FirstOrDefault(c => c.Name == "pose") is { } pose)
        {
            if (pose.ElementSize == Marshal.SizeOf<PlayerState>() && pose.Data.Length == pose.ElementSize) { world.Player = MemoryMarshal.Read<PlayerState>(pose.Data); }
            else { notes.Add("Player table has an unknown layout; the player's pose is reset."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == InjuriesTable) is { } inj
            && inj.Columns.FirstOrDefault(c => c.Name == "rows") is { } irows && inj.Columns.FirstOrDefault(c => c.Name == "last_id") is { } ilast)
        {
            if (irows.ElementSize == Marshal.SizeOf<Health.InjuryStore.Row>()) { world.Injuries.Import(MemoryMarshal.Cast<byte, Health.InjuryStore.Row>(irows.Data).ToArray(), BitConverter.ToUInt64(ilast.Data, 0)); }
            else { notes.Add("Injuries table has an unknown layout; injuries dropped."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == WeatherTable)?.Columns.FirstOrDefault(c => c.Name == "state") is { } weather)
        {
            if (weather.ElementSize == Marshal.SizeOf<Climate.WeatherState>() && weather.Data.Length == weather.ElementSize) { world.RestoreWeather(MemoryMarshal.Read<Climate.WeatherState>(weather.Data)); }
            else { notes.Add("Weather table has an unknown layout; the weather restarts from the current slot."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == ConfrontationsTable) is { } quarrels)
        {
            if (quarrels.Columns.FirstOrDefault(c => c.Name == "open") is { LayoutVersion: 1 } open && quarrels.Columns.FirstOrDefault(c => c.Name == "last_id") is { Data.Length: 8 } last)
            {
                world.Confrontations.Import(MessagePackSerializer.Deserialize<Social.Confrontation[]>(open.Data), BitConverter.ToUInt64(last.Data, 0));
            }
            else { notes.Add("Confrontations table has an unknown layout; quarrels dropped."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == FavorsTable) is { } favors)
        {
            if (favors.Columns.FirstOrDefault(c => c.Name == "open") is { LayoutVersion: 1 } open && favors.Columns.FirstOrDefault(c => c.Name == "last_id") is { Data.Length: 8 } last)
            {
                world.Favors.Import(MessagePackSerializer.Deserialize<Social.Favor[]>(open.Data), BitConverter.ToUInt64(last.Data, 0));
            }
            else { notes.Add("Favors table has an unknown layout; favors dropped."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == HoldingsTable)?.Columns.FirstOrDefault(c => c.Name == "coin") is { } coinCol)
        {
            if (coinCol.ElementSize == Marshal.SizeOf<Economy.Holdings.CoinRow>()) { world.Holdings.Import(MemoryMarshal.Cast<byte, Economy.Holdings.CoinRow>(coinCol.Data).ToArray()); }
            else { notes.Add("Holdings table has an unknown layout; coin reset."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == ProcessesTable)?.Columns.FirstOrDefault(c => c.Name == "state") is { LayoutVersion: 1 } procState)
        {
            world.Processes.Import(MessagePackSerializer.Deserialize<Crafting.ProcessStore.Snapshot>(procState.Data));
        }

        if (image.Tables.FirstOrDefault(t => t.Table == InventoryTable) is { } inv
            && inv.Columns.FirstOrDefault(c => c.Name == "slots") is { } slotCol && inv.Columns.FirstOrDefault(c => c.Name == "instances") is { } instCol)
        {
            if (slotCol.ElementSize == Marshal.SizeOf<Items.InventoryStore.Row>() && instCol.ElementSize == Marshal.SizeOf<Items.ItemInstance>())
            {
                world.Inventory.Import(MemoryMarshal.Cast<byte, Items.InventoryStore.Row>(slotCol.Data).ToArray(), MemoryMarshal.Cast<byte, Items.ItemInstance>(instCol.Data).ToArray());
            }
            else { notes.Add("Inventory table has an unknown layout; goods dropped."); }
        }

        if (image.Tables.FirstOrDefault(t => t.Table == NegotiationsTable) is { } negs)
        {
            if (negs.Columns.FirstOrDefault(c => c.Name == "open") is { LayoutVersion: 1 } open && negs.Columns.FirstOrDefault(c => c.Name == "last_id") is { Data.Length: 8 } last)
            {
                world.Negotiations.Import(MessagePackSerializer.Deserialize<Economy.Negotiation[]>(open.Data), BitConverter.ToUInt64(last.Data, 0));
            }
            else { notes.Add("Negotiations table has an unknown layout; haggles dropped."); }
        }

        world.RestorePlayerId();
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
