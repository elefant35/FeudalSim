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

    /// <summary>12 §4.2 attribute weights (str, end, dex, per, int, cha), summing to 1: ±3 effective skill per point from 5.</summary>
    public IReadOnlyDictionary<string, float>? Attributes { get; init; }
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

    /// <summary>A toxin the item carries (11 §8.1): the disease id eating it starts (M2-07).</summary>
    public string? Toxin { get; init; }

    /// <summary>Clothing only (11 §9.2): how the item is worn and what it does against cold and rain.</summary>
    public WearDef? Wear { get; init; }

    /// <summary>13 §5.9: commodities stack (a quantity with a mean Q); unique items are instances. Unset: raw, metal,
    /// food, drink and misc stack; tools, weapons, clothing and containers don't.</summary>
    public bool? Stackable { get; init; }

    /// <summary>13 §4.5 / §16.1 tags a recipe can ask for (<c>tool.hammerstone</c>, <c>stone.flint</c>, <c>wood.bow_stave</c> …).</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    public bool HasTag(string tag) => Tags is { } t && t.Contains(tag);

    public bool IsStackable => Stackable ?? Category is ItemCategory.Raw or ItemCategory.Metal or ItemCategory.Food or ItemCategory.Drink or ItemCategory.Misc;
}

/// <summary>
/// 11 §7.1 disease (or toxin, or food poisoning): routes, incubation, staged course with effects, the grave branch and
/// immunity. Toxins are single-stage diseases entered by eating (11 §8.1).
/// </summary>
public sealed record DiseaseDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Routes { get; init; } = [];
    public required IReadOnlyList<float> IncubationH { get; init; }
    public required IReadOnlyList<DiseaseStage> Stages { get; init; }
    public GraveBranch? Grave { get; init; }
    public float ImmunityDays { get; init; }

    /// <summary>11 §7.3 contact transmission β (0 = not person to person).</summary>
    public float Beta { get; init; }
}

public sealed record DiseaseStage
{
    public required string Id { get; init; }
    public required IReadOnlyList<float> Hours { get; init; }
    public float Contagious { get; init; }

    /// <summary>hydration_decay, satiety_absorb, work, condition_load (11 §7.1 effect keys).</summary>
    public IReadOnlyDictionary<string, float> Effects { get; init; } = new Dictionary<string, float>();

    /// <summary>The grave branch (death roll) fires at the end of this stage.</summary>
    public bool Grave { get; init; }

    public bool Fever { get; init; }
}

/// <summary>11 §7.1 grave branch: base death chance and multipliers (child, elder, malnourished, starving).</summary>
public sealed record GraveBranch
{
    public required float Base { get; init; }
    public IReadOnlyDictionary<string, float> Mult { get; init; } = new Dictionary<string, float>();
}

public enum NodeKind : byte { Tree, Bush, Rock, Patch }

/// <summary>
/// 10 §3.8 / §7 resource node type: what grows or lies where (stems or patches per hectare by biome), and the facts the
/// consuming systems read (wood properties 1–10, seasons, toxicity and the foraging-ID lookalike).
/// </summary>
public sealed record NodeDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required NodeKind Kind { get; init; }

    /// <summary>Count per hectare by biome key (10 §4 biome keys).</summary>
    public required IReadOnlyDictionary<string, float> Density { get; init; }

    /// <summary>Only on biome edges (a cell whose 8 m neighbour is another biome): hawthorn, elder, nightshade.</summary>
    public bool EdgeOnly { get; init; }

    /// <summary>Only on this rock (lithology name, e.g. chalk for yew).</summary>
    public string? Lithology { get; init; }

    /// <summary>Size-class weights for trees: sapling, pole, timber, veteran (10 §7.2).</summary>
    public IReadOnlyList<float>? Sizes { get; init; }

    public IReadOnlyDictionary<string, int>? Wood { get; init; }

    /// <summary>Seasons when it can be gathered (spring, summer, autumn, winter); empty = all year.</summary>
    public IReadOnlyList<string> Seasons { get; init; } = [];

    public bool Toxic { get; init; }

    /// <summary>11's foraging-ID misidentification pairing (10 §7.3): the node it is mistaken for, and the risk.</summary>
    public string? Confusable { get; init; }

    public float ConfusableRisk { get; init; }

    /// <summary>What one gathering trip yields (13 §9.5; M2-13).</summary>
    public RecipeOutput? Forage { get; init; }

    public string? Notes { get; init; }
}

/// <summary>13 §13.3 minigame calibration: per scored stage and grip band, the attentive player's raw-score quantiles
/// (generated by <c>feudalsim minigame calibrate</c>; CI checks they are current).</summary>
public sealed record MinigameDef
{
    public required string Id { get; init; }
    public required string Recipe { get; init; }
    public required IReadOnlyList<MinigameStageCurve> Stages { get; init; }
}

public sealed record MinigameStageCurve
{
    public required string Stage { get; init; }

    /// <summary>Five grip bands (g in fifths): the attentive bot's raw-score quantiles at <c>Calibration.Probs</c>
    /// (2.5, 10, 25, 50, 75, 90, 97.5 %).</summary>
    public required IReadOnlyList<IReadOnlyList<float>> Bands { get; init; }
}

/// <summary>13 §16.1 recipe: a process from inputs to an output through scored and passive stages.</summary>
public sealed record RecipeDef
{
    public required string Id { get; init; }
    public required RecipeOutput Output { get; init; }
    public required string Skill { get; init; }
    public required int Difficulty { get; init; }

    /// <summary>Default min(100, 60 + D): a nail is never a masterwork (13 §5.2).</summary>
    public int? MaxQuality { get; init; }

    public string? Knowhow { get; init; }
    public required TechTier Tier { get; init; }
    public IReadOnlyList<RecipeTool> Tools { get; init; } = [];
    public IReadOnlyList<RecipeInput> Inputs { get; init; } = [];
    public required IReadOnlyList<RecipeStage> Stages { get; init; }
    public IReadOnlyList<RecipeOutput> SalvageOnRuin { get; init; } = [];
    public ActivityLevel Intensity { get; init; } = ActivityLevel.Moderate;

    /// <summary>Work on a world node (felling a tree): its kind and minimum size class (10 §7.2; M2-12).</summary>
    public RecipeSite? Site { get; init; }

    /// <summary>Output multipliers by the site node's size class (sapling, pole, timber, veteran).</summary>
    public IReadOnlyList<int>? SizeYield { get; init; }

    /// <summary>Extra outputs (brash to firewood …), also scaled by <see cref="ByproductSizeYield"/> when a site is worked.</summary>
    public IReadOnlyList<RecipeOutput> Byproducts { get; init; } = [];

    public IReadOnlyList<int>? ByproductSizeYield { get; init; }

    /// <summary>11 §12.4 work-accident class and what an accident does.</summary>
    public RecipeRisk? Risk { get; init; }

    public int Cap => MaxQuality ?? Math.Min(100, 60 + Difficulty);
}

public sealed record RecipeSite
{
    public required NodeKind Kind { get; init; }
    public int MinSize { get; init; }
}

/// <summary>11 §12.4: class low/medium/high; the injury an accident gives (18 damage type, region: arm/leg/head/torso).</summary>
public sealed record RecipeRisk
{
    public required string Class { get; init; }
    public required string Damage { get; init; }
    public required string Region { get; init; }
}

public sealed record RecipeOutput
{
    public required string Item { get; init; }
    public int Qty { get; init; } = 1;
}

/// <summary>A tool by tag; <c>CapWithout</c> caps quality when an optional tool is missing (13 §8.1).</summary>
public sealed record RecipeTool
{
    public required string Tag { get; init; }
    public bool Required { get; init; } = true;
    public int? CapWithout { get; init; }
}

/// <summary>An input slot: a specific item or any item with a tag; <c>Weight</c> is its share of material quality M.</summary>
public sealed record RecipeInput
{
    public required string Slot { get; init; }
    public string? Item { get; init; }
    public string? Tag { get; init; }
    public int Qty { get; init; } = 1;
    public float Weight { get; init; } = 1f;
}

public enum StageKind : byte { Active, Passive, Tend, Assembly }

/// <summary>13 §16.1 stage. Active and assembly stages are scored (weight); passive and tend stages are timers.</summary>
public sealed record RecipeStage
{
    public required string Id { get; init; }
    public required StageKind Kind { get; init; }
    public string? Primitive { get; init; }
    public float LaborMin { get; init; }
    public float Weight { get; init; }
    public bool Signature { get; init; }
    public bool Catastrophic { get; init; }
    public string? Flaw { get; init; }
    public int DOffset { get; init; }
    public StageDays? DurationDays { get; init; }

    /// <summary>Overrun past the ideal window: Q lost per day, and ruin after this many days over (13 §4.3).</summary>
    public float OverrunQPerDay { get; init; }
    public float? RuinAfterDays { get; init; }

    /// <summary>Tool durability consumed per labor-hour (13 §4.5).</summary>
    public float Wear { get; init; } = 1f;

    /// <summary>13 §3.1 TaskToolFactor by tool tier where material dominates the work (felling: stone 1.5, copper 1.2).</summary>
    public IReadOnlyDictionary<string, float>? ToolFactor { get; init; }

    public bool Scored => Kind is StageKind.Active or StageKind.Assembly;
}

public sealed record StageDays
{
    public required float Min { get; init; }
    public required float Ideal { get; init; }
}

/// <summary>13 §5.7 flaw: a partial success's tag — its quality cap, how hard it is to spot (13 §5.8) and its effects.</summary>
public sealed record FlawDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Cap { get; init; }
    public required int HiddenDifficulty { get; init; }

    /// <summary>Stat changes as fractions (durability −0.4, draw −0.1 …); read by the consuming system.</summary>
    public IReadOnlyDictionary<string, float>? Effects { get; init; }

    public string? Fix { get; init; }
    public string? Source { get; init; }
}

/// <summary>11 §9.2 slots; a shirt or shift sits under the tunic (Under), so the homeland kit fills six slots.</summary>
public enum WearSlot { Under, Torso, Legs, Feet, Cloak, Head, Hands }

/// <summary>11 §9.2 clothing values: insulation (°C), the share of it kept when soaked, and rain resistance (cloak slot).</summary>
public sealed record WearDef
{
    public required WearSlot Slot { get; init; }
    public required float Ins { get; init; }
    public required float WetRetention { get; init; }
    public required float RainResist { get; init; }
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

    /// <summary>How the trait reads in a persona card's Temperament line (22 §7.3), e.g. "quick to anger".</summary>
    public string? VoicePhrase { get; init; }
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

    /// <summary>Given names drawn for unnamed spawns of this culture (M1-30); with <see cref="FamilyNames"/>.</summary>
    public IReadOnlyList<string>? GivenNames { get; init; }

    public IReadOnlyList<string>? FamilyNames { get; init; }
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

/// <summary>
/// Template lines for one decision option (canon §13.5: every line has a template, so template mode stays playable;
/// 22 §4.12: the fallback when generation fails or misses the speech cutoff). Slots: {player}, {npc}, {price}, {item},
/// {task}, {claim}, {reason}. The gesture is the choice's public face (22 §4.2 <c>DecisionSurfaced</c>).
/// </summary>
public sealed record LineTemplateDef
{
    public required string Id { get; init; }

    /// <summary>The option id this voices (e.g. <c>counter_step_1</c>, <c>retort</c>), or <c>reply.*</c> for answers to a player act with no DP.</summary>
    public required string Option { get; init; }

    public required IReadOnlyList<string> Variants { get; init; }
    public string Gesture { get; init; } = "neutral";
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

/// <summary>
/// 10 §3.1 WorldSpec: the hard constraints and target bands for world generation (bands are [min, max]). Stages read
/// only what they need; later milestones fill in the rest (deposits, landing, expedition sites).
/// </summary>
public sealed record WorldSpecDef
{
    public required string Id { get; init; }
    public required int RegionM { get; init; }
    public required IReadOnlyList<float> LandAreaKm2 { get; init; }
    public required IReadOnlyDictionary<string, float> ReliefArchetypeWeights { get; init; }
    public required IReadOnlyList<float> PeakM { get; init; }
    public IReadOnlyList<int>? RiversMajor { get; init; }
    public EstuarySpec? Estuary { get; init; }
    public LakesSpec? Lakes { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<float>>? BiomeShare { get; init; }
    public IReadOnlyDictionary<string, DepositSpec>? Deposits { get; init; }
    public LandingSpec? Landing { get; init; }
    public ExpeditionSitesSpec? ExpeditionSites { get; init; }
    public int MaxAttempts { get; init; } = 16;
}

public sealed record EstuarySpec
{
    public int Primary { get; init; } = 1;
    public float SecondaryChance { get; init; }
    public IReadOnlyList<float>? MouthWidthM { get; init; }
    public IReadOnlyList<float>? TidalReachM { get; init; }
}

public sealed record LakesSpec
{
    public IReadOnlyList<int>? Count { get; init; }
    public float MinAreaHa { get; init; }
    public float AtLeastOneOverHa { get; init; }
}

public sealed record DepositSpec
{
    public IReadOnlyList<int>? Count { get; init; }
    public float Chance { get; init; } = 1f;
    public float MinPathMFromLanding { get; init; }
    public float MinMFromBreadbasket { get; init; }
    public float AtLeastOneWithinPathMOfLanding { get; init; }
}

public sealed record LandingSpec
{
    public float FreshWaterWithinM { get; init; }
    public float FlintWithinM { get; init; }
    public float ClayWithinM { get; init; }
    public float BroadleafWithinM { get; init; }
    public float FertileHaWithin1500m { get; init; }
    public IReadOnlyList<float>? WreckReefOffshoreM { get; init; }
}

public sealed record ExpeditionSitesSpec
{
    public int CandidatesMin { get; init; }
    public float MinPathMFromLanding { get; init; }
    public float MinPathMBetween { get; init; }
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
        IReadOnlyList<OverheardLineDef>? overheardLines = null, IReadOnlyList<DecisionDef>? decisions = null, IReadOnlyList<LineTemplateDef>? lines = null,
        IReadOnlyList<WorldSpecDef>? worldSpecs = null, IReadOnlyList<FlawDef>? flaws = null, IReadOnlyList<RecipeDef>? recipes = null,
        IReadOnlyList<MinigameDef>? minigames = null, IReadOnlyList<NodeDef>? nodes = null, IReadOnlyList<DiseaseDef>? diseases = null)
    {
        Diseases = diseases ?? [];
        Nodes = nodes ?? [];
        Minigames = minigames ?? [];
        Recipes = recipes ?? [];
        WorldSpecs = worldSpecs ?? [];
        Flaws = flaws ?? [];
        Lines = lines ?? [];
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

    /// <summary>10 §3.1 world-generation profiles (M2-01).</summary>
    public IReadOnlyList<WorldSpecDef> WorldSpecs { get; }

    /// <summary>13 §5.7 flaws (≤ 64: an instance holds them as a bit mask over handles).</summary>
    public IReadOnlyList<FlawDef> Flaws { get; }

    public int FlawHandle(string id) => HandleOf(Flaws, id, f => f.Id);

    /// <summary>13 §16.1 recipes.</summary>
    public IReadOnlyList<RecipeDef> Recipes { get; }

    public int RecipeHandle(string id) => HandleOf(Recipes, id, r => r.Id);

    /// <summary>13 §13.3 minigame calibration curves.</summary>
    public IReadOnlyList<MinigameDef> Minigames { get; }

    public MinigameDef? Minigame(string id) => Minigames.FirstOrDefault(m => m.Id == id);

    /// <summary>10 §3.8 / §7 resource node types (handles = node type ids in saved node deltas).</summary>
    public IReadOnlyList<NodeDef> Nodes { get; }

    public int NodeHandle(string id) => HandleOf(Nodes, id, n => n.Id);

    /// <summary>11 §7–8 diseases, toxins and food poisoning (M2-07a).</summary>
    public IReadOnlyList<DiseaseDef> Diseases { get; }

    public int DiseaseHandle(string id) => HandleOf(Diseases, id, d => d.Id);

    public int ItemHandle(string id) => HandleOf(Items, id, i => i.Id);

    public WorldSpecDef? WorldSpec(string id) => WorldSpecs.FirstOrDefault(w => w.Id == id);
    public IReadOnlyList<ProfessionDef> Professions { get; }
    public IReadOnlyList<ActionDef> Actions { get; }
    public IReadOnlyList<ScheduleDef> Schedules { get; }
    public IReadOnlyList<OpinionModifierDef> OpinionModifiers { get; }

    public int OpinionModifierHandle(string id) => HandleOf(OpinionModifiers, id, o => o.Id);

    /// <summary>Claim predicates in handle order (ordinal id).</summary>
    public IReadOnlyList<ClaimPredicateDef> ClaimPredicates { get; }

    public int ClaimHandle(string id) => HandleOf(ClaimPredicates, id, c => c.Id);

    /// <summary>Template lines in id order (<c>line.*</c>).</summary>
    public IReadOnlyList<LineTemplateDef> Lines { get; }

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
