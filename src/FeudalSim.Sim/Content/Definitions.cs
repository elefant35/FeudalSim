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

public enum PriorityClass { P0, P1, P2, P3, P4 }

/// <summary>Activity levels for physical-need decay (11 §2.1).</summary>
public enum ActivityLevel : byte { Sleep, Rest, Light, Moderate, Heavy }

/// <summary>Where an action happens in the M1 graybox camp; <c>Home</c> is the person's own spot.</summary>
public enum PlaceKind { Fire, Water, Stores, Shelter, Woods, ForageGround, Home, Here }

public sealed record ConsumeSpec
{
    public required string Stock { get; init; }
    public required float Ratio { get; init; }
}

public sealed record StockPressureSpec
{
    public required string Stock { get; init; }

    /// <summary>Stock level at which the work feels unnecessary: consideration = clamp(1 − stock/comfortable, 0.1, 1).</summary>
    public required float Comfortable { get; init; }
}

/// <summary>An action the utility AI can choose (21 §7.1–7.3; M1 camp catalog).</summary>
public sealed record ActionDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public required PriorityClass Class { get; init; }   // YAML `class` (snake_case of Class)

    public float BaseWeight { get; init; } = 1f;

    /// <summary>Need whose urgency sets W (21 §7.2): satiety, hydration, energy, warmth or social.</summary>
    public string? WeightNeed { get; init; }

    public required PlaceKind Place { get; init; }
    public required int DurationMin { get; init; }
    public required ActivityLevel Activity { get; init; }
    public IReadOnlyDictionary<string, float>? NeedPerHour { get; init; }
    public ConsumeSpec? Consumes { get; init; }
    public IReadOnlyDictionary<string, float>? StockPerHour { get; init; }
    public IReadOnlyDictionary<string, float>? Requires { get; init; }
    public IReadOnlyDictionary<string, float>? UntilNeed { get; init; }

    /// <summary>Outside its own schedule block, the action only starts while every listed need is below its level.</summary>
    public IReadOnlyDictionary<string, float>? StartBelow { get; init; }
    public string? Skill { get; init; }
    public bool Purposeful { get; init; }
    public bool Social { get; init; }
    public string ScheduleBlock { get; init; } = "any";
    public IReadOnlyList<string>? UtilityKeys { get; init; }
    public IReadOnlyDictionary<string, float>? FacetK { get; init; }

    /// <summary>Started by a system (a conversation holds the NPC in <c>action.converse</c>, 21 §14.4), never by the scorer.</summary>
    public bool ChosenBySystem { get; init; }
    public StockPressureSpec? StockPressure { get; init; }
}

public sealed record ScheduleBlockDef
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Block { get; init; }
}

/// <summary>A soft daily schedule (21 §9): blocks set schedule fit, they never move people on rails.</summary>
public sealed record ScheduleDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<ScheduleBlockDef> Blocks { get; init; }
}

/// <summary>
/// The fixed menu of one decision-point kind (canon §13.1, 22 §6.1): each option's family, stakes and the character terms
/// the propensity layer (21 §7.8) applies. Owning systems decide which options are generated, eligible and how strongly
/// motivated (sources); the numbers that shape them live here.
/// </summary>
public sealed record DecisionDef
{
    public required string Id { get; init; }

    /// <summary>The owning system (e.g. <c>21.initiative</c>, <c>16.escalation</c>, <c>15.trade</c>).</summary>
    public required string Owner { get; init; }

    public required IReadOnlyList<DecisionOptionDef> Options { get; init; }
    public string? Description { get; init; }
}

public sealed record DecisionOptionDef
{
    public required string Id { get; init; }

    /// <summary>Calibration bucket (22 §6): rapport, request, trade, escalation, belief, disclosure, promise, intervention, stance…</summary>
    public required string Family { get; init; }

    public required Decisions.Stakes Stakes { get; init; }

    /// <summary>Base weight before source strength and character terms.</summary>
    public float BaseWeight { get; init; } = 1f;

    /// <summary>Trait utility group (21 §4.3 <c>U[x]</c>), e.g. help, confront, chat, gather.</summary>
    public string? UtilityKey { get; init; }

    /// <summary>Facet multiplier <c>1 + k·z</c>: facet name → k (one entry).</summary>
    public IReadOnlyDictionary<string, float>? FacetK { get; init; }

    /// <summary>Signed value tags −1…+1 (21 §4.2).</summary>
    public IReadOnlyDictionary<string, float>? ValueTags { get; init; }

    /// <summary>Emotion the option is an outlet for (hijack fold-in, 21 §8.3).</summary>
    public string? Outlet { get; init; }

    /// <summary>Need the option serves (N = 1 + 0.5·u).</summary>
    public string? Need { get; init; }

    /// <summary>+1 favors the counterpart, −1 hostile, 0 neutral (21 §7.8 R term).</summary>
    public int Direction { get; init; }

    /// <summary>Counts against the long-shot budget when chosen at p &lt; 0.20 (canon §13.1).</summary>
    public bool FavorsPlayer { get; init; }

    /// <summary>Plain-English gloss for prompts; <c>{param}</c> slots are filled from the option's fixed parameters.</summary>
    public required string Gloss { get; init; }
}

public enum OpinionStacking { Add, Saturate, Refresh, Once }

public enum OpinionScaling { None, Honor, FamilyFaith }

/// <summary>An opinion modifier type (16 §4.5): one decaying slot per type per directed edge, merged exactly (§4.3).</summary>
public sealed record OpinionModifierDef
{
    public required string Id { get; init; }
    public required int Number { get; init; }

    /// <summary>Pre-scaling value; triggering systems pass a multiplier for variable ones (importance, stakes, gift…).</summary>
    public required float Value { get; init; }

    public required float HalfLifeDays { get; init; }
    public required OpinionStacking Stacking { get; init; }
    public float? Cap { get; init; }
    public float FloorFraction { get; init; }
    public IReadOnlyDictionary<string, float>? ExtendsTo { get; init; }

    /// <summary>× this when the act was public (≥ 3 witnesses) — status-relevant harms.</summary>
    public float PublicMultiplier { get; init; } = 1f;

    /// <summary>Opinion from words (talk, praise, rapport): positive gains count toward the +10 per pair per day budget (canon §13.4, 16 §4.15).</summary>
    public bool Words { get; init; }

    public OpinionScaling Scaling { get; init; }

    /// <summary>Event fear added toward the actor (16 §4.10).</summary>
    public float Fear { get; init; }

    public string? Notes { get; init; }
}

public enum ClaimValence { Negative, Neutral, Positive }

/// <summary>A claim predicate (16 §7.3): juiciness, reputation axis impacts, mutation ladder.</summary>
public sealed record ClaimPredicateDef
{
    public required string Id { get; init; }

    /// <summary>J (0–1): the base pull of the claim in conversation (16 §7.4).</summary>
    public required float Juiciness { get; init; }

    /// <summary>J when subject or object is married (an affair); null = <see cref="Juiciness"/>.</summary>
    public float? JuicinessIfMarried { get; init; }

    public required ClaimValence Valence { get; init; }

    /// <summary>Reputation impact per unit magnitude by axis (16 §8.1).</summary>
    public IReadOnlyDictionary<string, float>? Axes { get; init; }

    /// <summary>Next rung of the mutation ladder (16 §7.6 "escalate predicate").</summary>
    public string? EscalatesTo { get; init; }

    /// <summary>Accusation-grade when about a named person (16 §7.10 stakes).</summary>
    public bool Accusation { get; init; }

    /// <summary>A moral claim: a priest's word weighs more (16 §7.2 speaker factor).</summary>
    public bool Moral { get; init; }

    /// <summary>Template wording with <c>{subject}</c> / <c>{object}</c> for subtitles and rumor lines (22 §9).</summary>
    public required string Phrase { get; init; }
}

/// <summary>A template subtitle for overheard NPC↔NPC talk (22 §9.2): the fallback and template-mode wording.</summary>
public sealed record OverheardLineDef
{
    public required string Id { get; init; }

    /// <summary>Interaction kind (16 §5.2), lower-case: chat, joke, praise, comfort, request, argue, insult, apologize, gossip, warn.</summary>
    public required string Interaction { get; init; }

    /// <summary>Narrows the line to a success or a failure; null = either.</summary>
    public bool? Success { get; init; }

    /// <summary>Text with <c>{a}</c>, <c>{b}</c> and (gossip, warn) <c>{claim}</c>.</summary>
    public required string Text { get; init; }
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

    /// <summary>Reputation axes (16 §8.1).</summary>
    public static readonly IReadOnlyList<string> ReputationAxes = ["honesty", "lawfulness", "peaceableness", "courage", "generosity", "piety", "competence"];

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
        IReadOnlyList<TraitDef>? traits = null, IReadOnlyList<CultureDef>? cultures = null, IReadOnlyList<ProfessionDef>? professions = null,
        IReadOnlyList<ActionDef>? actions = null, IReadOnlyList<ScheduleDef>? schedules = null,
        IReadOnlyList<OpinionModifierDef>? opinionModifiers = null, IReadOnlyList<ClaimPredicateDef>? claimPredicates = null,
        IReadOnlyList<OverheardLineDef>? overheardLines = null, IReadOnlyList<DecisionDef>? decisions = null)
    {
        Decisions = decisions ?? [];
        OverheardLines = overheardLines ?? [];
        ClaimPredicates = claimPredicates ?? [];
        OpinionModifiers = opinionModifiers ?? [];
        Actions = actions ?? [];
        Schedules = schedules ?? [];
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
    public IReadOnlyList<ActionDef> Actions { get; }
    public IReadOnlyList<ScheduleDef> Schedules { get; }
    public IReadOnlyList<OpinionModifierDef> OpinionModifiers { get; }

    public int OpinionModifierHandle(string id) => HandleOf(OpinionModifiers, id, o => o.Id);

    /// <summary>Claim predicates in handle order (ordinal id).</summary>
    public IReadOnlyList<ClaimPredicateDef> ClaimPredicates { get; }

    public int ClaimHandle(string id) => HandleOf(ClaimPredicates, id, c => c.Id);

    /// <summary>Decision-point menus in id order (<c>dp.*</c>).</summary>
    public IReadOnlyList<DecisionDef> Decisions { get; }

    public DecisionDef? Decision(string id) => HandleOf(Decisions, id, d => d.Id) is var h and >= 0 ? Decisions[h] : null;

    /// <summary>Overheard-talk template subtitles in id order.</summary>
    public IReadOnlyList<OverheardLineDef> OverheardLines { get; }

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
