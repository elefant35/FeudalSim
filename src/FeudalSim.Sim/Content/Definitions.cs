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
        IReadOnlyList<AssetDef>? assets = null)
    {
        Skills = skills;
        Items = items;
        Needs = needs;
        Hash = hash;
        Assets = assets ?? [];
    }

    public static ContentDatabase Empty { get; } = new([], [], [], 0);

    /// <summary>Asset provenance manifest. Not part of <see cref="Hash"/> (it isn't gameplay data).</summary>
    public IReadOnlyList<AssetDef> Assets { get; }

    public IReadOnlyList<SkillDef> Skills { get; }
    public IReadOnlyList<ItemDef> Items { get; }
    public IReadOnlyList<NeedDef> Needs { get; }

    /// <summary>XxHash64 of the canonical compiled content; recorded in saves and run outputs.</summary>
    public ulong Hash { get; }

    public NeedDef? Need(string id) => Needs.FirstOrDefault(n => n.Id == id);
}
