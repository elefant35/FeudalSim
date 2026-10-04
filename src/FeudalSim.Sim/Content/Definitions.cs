namespace FeudalSim.Sim.Content;

// Content definition types (20 §10): the single source of truth. YAML in /content is validated
// against JSON Schemas generated from these records and compiled into a ContentDatabase.
// Property names map to snake_case in YAML; enum values to snake_case strings.

public enum SkillDomain { Land, Craft, Social, MartialBody }

/// <summary>One of the 28 canonical skills (canon §10.2).</summary>
public sealed record SkillDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required SkillDomain Domain { get; init; }
    public string? Description { get; init; }
}

public enum ItemCategory { Raw, Metal, Food, Drink, Tool, Weapon, Clothing, Container, Misc }

public enum TechTier { T0, T1, T2, T3, T4 }

/// <summary>An item type. Values in farthings (canon §11); prices are owned by 15-economy-and-trade.</summary>
public sealed record ItemDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ItemCategory Category { get; init; }
    public required TechTier Tier { get; init; }
    public required string TradeUnit { get; init; }
    public required double MassKg { get; init; }
    public required int BaseValueF { get; init; }
    public int? Durability { get; init; }
    public IReadOnlyList<string>? Aliases { get; init; }
}

public enum NeedKind { Physical, Psychological }

/// <summary>Decay per game hour by activity level (11-survival §2.1).</summary>
public sealed record NeedDecay
{
    public required float Sleep { get; init; }
    public required float Rest { get; init; }
    public required float Light { get; init; }
    public required float Moderate { get; init; }
    public required float Heavy { get; init; }
}

/// <summary>A canonical need (canon §10.5).</summary>
public sealed record NeedDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required NeedKind Kind { get; init; }
    public NeedDecay? DecayPerHour { get; init; }
    public string? Description { get; init; }
}

public enum TraitGroup { Temper, Morality, Social, Courage, Work, Ambition, Faith, Appetite }

/// <summary>
/// Mechanical trait effects (21 §4.3) as data for their consuming systems: multipliers on emotion gain, emotion
/// half-life, action-group utility and need decay; absolute hijack thresholds; additive τ, susceptibility and
/// mood baseline; floors, caps and shifts on the nine values. <c>Notes</c> carries hooks owned by other systems.
/// </summary>
public sealed record TraitEffects
{
    public IReadOnlyDictionary<string, float>? EmotionGain { get; init; }
    public IReadOnlyDictionary<string, float>? HalfLife { get; init; }
    public IReadOnlyDictionary<string, float>? HijackThreshold { get; init; }
    public IReadOnlyDictionary<string, float>? Utility { get; init; }
    public IReadOnlyDictionary<string, float>? NeedDecay { get; init; }
    public float? Tau { get; init; }
    public float? Susceptibility { get; init; }
    public float? MoodBaseline { get; init; }

    /// <summary>Additive shift of the Status-need aspiration (21 §5.2: Ambitious +15, Humble −15).</summary>
    public float? StatusAspiration { get; init; }
    public IReadOnlyDictionary<string, int>? ValueFloor { get; init; }
    public IReadOnlyDictionary<string, int>? ValueCap { get; init; }
    public IReadOnlyDictionary<string, int>? ValueShift { get; init; }
    public string? Notes { get; init; }
}

/// <summary>A personality trait (canon §10.4; catalog and generation: 21 §4.3–4.4).</summary>
public sealed record TraitDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required TraitGroup Group { get; init; }

    /// <summary>One of the 16 traits named in canon §10.4.</summary>
    public bool Canon { get; init; }

    /// <summary>Traits that cannot be held together (made symmetric at compile time).</summary>
    public IReadOnlyList<string>? Incompatible { get; init; }

    /// <summary>Base prevalence weight in generation (mean 1.0).</summary>
    public float Prevalence { get; init; } = 1f;

    /// <summary>Generation weight × exp(Σ coefficient · z_facet), keyed by facet name (21 §4.4).</summary>
    public IReadOnlyDictionary<string, float>? FacetAffinity { get; init; }

    public TraitEffects? Effects { get; init; }
    public string? Description { get; init; }
}

/// <summary>A homeland culture (canon §5): value means and trait-generation multipliers (21 §4.2, §4.4).</summary>
public sealed record CultureDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Faith { get; init; }

    /// <summary>Mean importance per value (0–100); unlisted values mean 50.</summary>
    public IReadOnlyDictionary<string, int>? ValueMeans { get; init; }

    public IReadOnlyDictionary<string, float>? TraitMultipliers { get; init; }
    public string? Description { get; init; }
}

/// <summary>A profession (12 §10): the skills a homeland trade turns into (12 §8.5).</summary>
public sealed record ProfessionDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Primary { get; init; }
    public IReadOnlyList<string>? Secondary { get; init; }
    public required TechTier Tier { get; init; }
    public required int FirstEra { get; init; }
    public required int Prestige { get; init; }
    public bool Critical { get; init; }
    public string? Workplace { get; init; }
}

public enum AssetKind { Model, Animation, Texture, Vfx, Ui, Sfx, Ambience, Music, Vocal }

public enum AssetStatus { Placeholder, Draft, Review, Approved, Final }

public enum AssetSourceType { Generator, Hand, External, Recorded, Synthesized, Commissioned }

public sealed record AssetSource
{
    public required AssetSourceType Type { get; init; }
    public string? Generator { get; init; }
    public IReadOnlyDictionary<string, object>? Params { get; init; }
    public string? Url { get; init; }
    public string? Tool { get; init; }
}

public sealed record AssetLicense
{
    public required string Name { get; init; }
    public required string Author { get; init; }
    public required bool AttributionRequired { get; init; }
    public string? Credit { get; init; }
    public string? Url { get; init; }
}

public sealed record AssetMeasured
{
    public int? TrisLod0 { get; init; }
    public int? Materials { get; init; }
    public double? LoudnessLufs { get; init; }
    public double? PeakDbfs { get; init; }
    public double? DurationS { get; init; }
}

/// <summary>
/// One shippable asset's provenance (32-art-and-audio-production §3, §14): who made it, how, under what
/// license, and its review status. No entry, no merge. Only the owner sets <c>approved</c>.
/// </summary>
public sealed record AssetDef
{
    public required string Id { get; init; }
    public required AssetKind Kind { get; init; }
    public required AssetStatus Status { get; init; }
    public required string Milestone { get; init; }
    public required AssetSource Source { get; init; }
    public required IReadOnlyList<string> Outputs { get; init; }
    public string? BudgetClass { get; init; }
    public AssetMeasured? Measured { get; init; }
    public required AssetLicense License { get; init; }
    public string? Notes { get; init; }
}

public enum AudioBus { SfxWorld, SfxCombat, Ui, Ambience, Music, Vocal }

/// <summary>
/// Maps a sim/presentation event id to a sound bank (32 §11): the client picks one file at random,
/// applying the pitch and volume jitter. Every audible sim event must have a mapping.
/// </summary>
public sealed record AudioEventDef
{
    public required string Id { get; init; }
    public required AudioBus Bus { get; init; }
    public required IReadOnlyList<string> Files { get; init; }
    public required bool Spatial { get; init; }
    public float PitchJitter { get; init; }
    public float VolumeJitterDb { get; init; }
    public bool Loop { get; init; }
}

/// <summary>Canonical id lists the content must match exactly (canon §10.2, §10.5).</summary>
public static class CanonLists
{
    public static readonly IReadOnlyList<string> SkillIds =
    [
        // Land (7)
        "skill.farming", "skill.husbandry", "skill.foraging", "skill.hunting", "skill.fishing", "skill.woodcutting", "skill.mining",
        // Craft (11)
        "skill.carpentry", "skill.masonry", "skill.metallurgy", "skill.smithing", "skill.bowyery", "skill.leatherworking",
        "skill.textiles", "skill.pottery", "skill.cooking", "skill.brewing", "skill.healing",
        // Social (5)
        "skill.persuasion", "skill.commerce", "skill.leadership", "skill.stewardship", "skill.letters",
        // Martial & Body (5)
        "skill.melee", "skill.archery", "skill.athletics", "skill.stealth", "skill.tactics",
    ];

    public static readonly IReadOnlyList<string> Facets = ["curiosity", "diligence", "sociability", "warmth", "volatility"];

    public static readonly IReadOnlyList<string> Values = ["family", "wealth", "status", "honor", "tradition", "faith", "fairness", "freedom", "loyalty"];

    public static readonly IReadOnlyList<string> Emotions = ["anger", "fear", "grief", "joy", "shame", "jealousy"];

    public static readonly IReadOnlyList<string> NeedIds =
    [
        "need.satiety", "need.hydration", "need.energy", "need.warmth",
        "need.social", "need.comfort", "need.safety", "need.purpose", "need.status",
    ];
}

/// <summary>Compiled content. Handles are indices in ordinal id order (deterministic).</summary>
public sealed class ContentDatabase
{
    public ContentDatabase(IReadOnlyList<SkillDef> skills, IReadOnlyList<ItemDef> items, IReadOnlyList<NeedDef> needs, ulong hash,
        IReadOnlyList<AssetDef>? assets = null, IReadOnlyList<AudioEventDef>? audio = null,
        IReadOnlyList<TraitDef>? traits = null, IReadOnlyList<CultureDef>? cultures = null, IReadOnlyList<ProfessionDef>? professions = null)
    {
        Traits = traits ?? [];
        Cultures = cultures ?? [];
        Professions = professions ?? [];
        Audio = audio ?? [];
        Skills = skills;
        Items = items;
        Needs = needs;
        Hash = hash;
        Assets = assets ?? [];
    }

    public static ContentDatabase Empty { get; } = new([], [], [], 0);

    /// <summary>Asset provenance manifest. Not part of <see cref="Hash"/> (it isn't gameplay data).</summary>
    public IReadOnlyList<AssetDef> Assets { get; }

    /// <summary>Sound-bank mappings for the client. Not part of <see cref="Hash"/>.</summary>
    public IReadOnlyList<AudioEventDef> Audio { get; }

    public IReadOnlyList<SkillDef> Skills { get; }
    public IReadOnlyList<ItemDef> Items { get; }
    public IReadOnlyList<NeedDef> Needs { get; }

    /// <summary>Traits in handle order (ordinal id); a person's trait set is a bitset over these handles (≤ 64).</summary>
    public IReadOnlyList<TraitDef> Traits { get; }

    public IReadOnlyList<CultureDef> Cultures { get; }
    public IReadOnlyList<ProfessionDef> Professions { get; }

    /// <summary>Handle of a definition id in a sorted list, or −1.</summary>
    public static int HandleOf<T>(IReadOnlyList<T> defs, string id, Func<T, string> idOf)
    {
        int lo = 0, hi = defs.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var c = string.CompareOrdinal(idOf(defs[mid]), id);
            if (c == 0) { return mid; }
            if (c < 0) { lo = mid + 1; } else { hi = mid - 1; }
        }

        return -1;
    }

    public int TraitHandle(string id) => HandleOf(Traits, id, t => t.Id);
    public int CultureHandle(string id) => HandleOf(Cultures, id, c => c.Id);
    public int ProfessionHandle(string id) => HandleOf(Professions, id, p => p.Id);
    public int SkillHandle(string id) => HandleOf(Skills, id, s => s.Id);

    /// <summary>XxHash64 of the canonical compiled content; recorded in saves and run outputs.</summary>
    public ulong Hash { get; }

    public NeedDef? Need(string id) => Needs.FirstOrDefault(n => n.Id == id);
}
