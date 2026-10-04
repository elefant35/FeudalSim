using MessagePack;

namespace FeudalSim.Sim.Commands;

/// <summary>Who issued a command (20 §9.1).</summary>
public enum CommandSource : byte { Player, Embodiment, Ai, Settings, Scenario, Dev, Integrity }

/// <summary>
/// A state-changing input. Always validated, applied in <c>Seq</c> order at the start of a step,
/// and logged. Union keys are append-only and never reused.
/// </summary>
[Union(0, typeof(SetDayLength))]
[Union(1, typeof(SpawnPerson))]
[Union(2, typeof(Ai.AiResultCommand))]
[Union(3, typeof(PlayerMoved))]
[Union(4, typeof(EmbodimentReport))]
[Union(5, typeof(Decisions.DecisionPointOpened))]
[Union(6, typeof(Decisions.DecisionMade))]
[Union(7, typeof(HoldAiRequests))]
[Union(8, typeof(SetLodTier))]
[Union(9, typeof(StartConversation))]
[Union(10, typeof(EndConversation))]
[Union(11, typeof(PlayerUtteranceClassified))]
[Union(12, typeof(SetHoldings))]
[Union(13, typeof(TradeOpen))]
[Union(14, typeof(TradeOffer))]
[Union(15, typeof(TradeAccept))]
[Union(16, typeof(TradeWalkAway))]
[Union(17, typeof(DialogueLineRendered))]
[Union(18, typeof(Steal))]
[Union(19, typeof(InflictTrauma))]
[Union(20, typeof(TreatWound))]
[Union(21, typeof(StartProcess))]
[Union(22, typeof(WorkStage))]
[Union(23, typeof(ResumeProcess))]
[Union(24, typeof(StartBatch))]
[Union(25, typeof(Forage))]
[Union(26, typeof(InspectItem))]
[Union(27, typeof(Eat))]
[Union(28, typeof(Drink))]
[Union(29, typeof(PlayerFell))]
[Union(30, typeof(Drop))]
[Union(31, typeof(PickUp))]
public abstract record StateCommand;

/// <summary>Changes the real-minutes-per-game-day setting (canon §6). Logged; applies at the next step.</summary>
[MessagePackObject]
public sealed record SetDayLength([property: Key(0)] int Minutes) : StateCommand;

/// <summary>
/// Scenario/dev command: create a person at a home position (metres, X east, −Z north). The sim generates their
/// personality, attributes and skills from the world seed and their id (M1-01). Optional: culture and profession ids
/// (default <c>culture.varrow</c>; a drawn homeland trade) and age in years (0 = drawn adult age). Keys 3–5 were added
/// in M1; logs written before them read as the defaults. Key 6 (M1-04b) spawns the player's own character: at most one.
/// </summary>
[MessagePackObject]
public sealed record SpawnPerson(
    [property: Key(0)] string Name,
    [property: Key(1)] float X,
    [property: Key(2)] float Z,
    [property: Key(3)] string? Culture = null,
    [property: Key(4)] string? Profession = null,
    [property: Key(5)] int AgeYears = 0,
    [property: Key(6)] bool IsPlayer = false) : StateCommand;

/// <summary>
/// While held, new AI requests resolve at once with their template instead of going out (logged, so replays match).
/// A save holds requests and waits for those in flight to finish or time out (31 R27).
/// </summary>
[MessagePackObject]
public sealed record HoldAiRequests([property: Key(0)] bool Hold) : StateCommand;

/// <summary>
/// Scenario/dev command: pins a person to simulation tier LOD1, LOD2 or LOD3 (21 §15.1). Until settlements and the
/// relevance set exist (M2–M4), headless scale runs place people in the abstract tiers with this; LOD0 stays the
/// LodSystem's (it needs a player and a body).
/// </summary>
[MessagePackObject]
public sealed record SetLodTier([property: Key(0)] Core.EntityId Person, [property: Key(1)] World.LodTier Tier) : StateCommand;

/// <summary>The player starts talking to an NPC within 6 m (22 §4.11). The NPC is held in <c>action.converse</c> until it ends.</summary>
[MessagePackObject]
public sealed record StartConversation([property: Key(0)] Core.EntityId Npc) : StateCommand;

/// <summary>The player ends a conversation (walks off, says goodbye).</summary>
[MessagePackObject]
public sealed record EndConversation([property: Key(0)] ulong Conversation) : StateCommand;

/// <summary>
/// One player turn, after sanitizing and classification by the gateway (22 §4.4–4.6): the classified act (catalog v2
/// id), its probability, the injection probability (≥ 0.3 → this turn's DPs go to the policy) and the sanitized text;
/// then the severity of a provocation (1–5), the score questions behind L_words (persuasiveness 1–7, hostility and
/// politeness 1–5, the appeal, apology sincerity 1–5; 0 or "" = not classified, the neutral signal, 22 §6.3); for a
/// request, the task (an action id) and hours (22 §4.5 extraction); for a told claim, its predicate, subject, object and
/// whether the player claims to have seen it (cS 0.9, else hearsay 0.7; 16 §7.10).
/// The M1-04b form; extraction records and full probabilities are appended as keys in M1-11. Applying it advances the
/// turn and opens the NPC's decision points for that turn.
/// </summary>
[MessagePackObject]
public sealed record PlayerUtteranceClassified(
    [property: Key(0)] ulong Conversation,
    [property: Key(1)] int TurnIndex,
    [property: Key(2)] string Act,
    [property: Key(3)] float ActP,
    [property: Key(4)] float Injection,
    [property: Key(5)] string Text,
    [property: Key(6)] int Severity = 0,
    [property: Key(7)] float Persuasiveness = 0f,
    [property: Key(8)] float Hostility = 0f,
    [property: Key(9)] float Politeness = 0f,
    [property: Key(10)] string Appeal = "",
    [property: Key(11)] float Sincerity = 0f,
    [property: Key(12)] string RequestTask = "",
    [property: Key(13)] float RequestHours = 0f,
    [property: Key(14)] string ClaimPredicate = "",
    [property: Key(15)] Core.EntityId ClaimSubject = default,
    [property: Key(16)] Core.EntityId ClaimObject = default,
    [property: Key(17)] bool ClaimFirstHand = false) : StateCommand;

/// <summary>Scenario/dev only (11 §4.1): a hit of <c>Effective</c> damage to one region — Landfall's injured (S3) and
/// tests; 18's combat calls the same rule directly. Enum values as bytes: <c>Health.DamageType</c>, <c>BodyRegion</c>, <c>TraumaSource</c>.</summary>
[MessagePackObject]
public sealed record InflictTrauma([property: Key(0)] Core.EntityId Person, [property: Key(1)] float Effective, [property: Key(2)] byte Damage,
    [property: Key(3)] byte Region, [property: Key(4)] byte Source) : StateCommand;

/// <summary>11 §5.2–5.3 / 13 §8: <c>Healer</c> performs a procedure (<c>Health.Procedure</c> as a byte) on one of
/// <c>Patient</c>'s wounds. From the player the healer must be the player, within arm's reach; the AI and scenarios may
/// name any healer. The healer's Healing check decides q.</summary>
[MessagePackObject]
public sealed record TreatWound([property: Key(0)] Core.EntityId Healer, [property: Key(1)] Core.EntityId Patient, [property: Key(2)] ulong Injury,
    [property: Key(3)] byte Procedure) : StateCommand;

/// <summary>13 §4: start a recipe (inputs from <c>Container</c>, default the worker's own carry).</summary>
[MessagePackObject]
public sealed record StartProcess([property: Key(0)] Core.EntityId Worker, [property: Key(1)] string Recipe, [property: Key(2)] bool Masterwork = false,
    [property: Key(3)] Core.EntityId Container = default, [property: Key(4)] int SiteChunk = -1, [property: Key(5)] int SiteIndex = -1) : StateCommand;

/// <summary>13 §7.2 minigame contract: work a process's next stage. <c>MinigameM</c> ∈ [−1, +1] is the player's result
/// (NaN = auto-resolve as an NPC draw); <c>RealSeconds</c> is how long it was played (13 §3.1 paid labor).</summary>
[MessagePackObject]
public sealed record WorkStage([property: Key(0)] ulong Process, [property: Key(1)] float MinigameM, [property: Key(2)] float RealSeconds = 0f) : StateCommand;

/// <summary>13 §3.1: resume a held process, paying the owed labor.</summary>
[MessagePackObject]
public sealed record ResumeProcess([property: Key(0)] ulong Process) : StateCommand;

/// <summary>13 §12 Batch: n items resolved as NPC draws, from the worker's own carry.</summary>
[MessagePackObject]
public sealed record StartBatch([property: Key(0)] Core.EntityId Worker, [property: Key(1)] string Recipe, [property: Key(2)] int Count) : StateCommand;

/// <summary>13 §9.5: a gathering trip to one plant or bush node (chunk, index in the chunk's generated list).</summary>
[MessagePackObject]
public sealed record Forage([property: Key(0)] Core.EntityId Worker, [property: Key(1)] int Chunk, [property: Key(2)] int Index) : StateCommand;

/// <summary>11 §10 (M2-07b): eat one unit of something the eater carries, chosen by what they believe it is (<c>Item</c> is the
/// item as seen; a mislabelled stack is eaten for what it truly is).</summary>
[MessagePackObject]
public sealed record Eat([property: Key(0)] Core.EntityId Eater, [property: Key(1)] string Item) : StateCommand;

/// <summary>11 §11 (M2-07c): drink where you stand — the camp's water, a brook, river, lake or spring within reach, or the sea.</summary>
[MessagePackObject]
public sealed record Drink([property: Key(0)] Core.EntityId Drinker) : StateCommand;

/// <summary>11 §12.3 (M2-08): the player's body fell <c>HeightM</c> (an embodiment report, like a pose); the sim applies the injuries.</summary>
[MessagePackObject]
public sealed record PlayerFell([property: Key(0)] float HeightM) : StateCommand;

/// <summary>M2-08: put down <c>Qty</c> of an item (as the person sees it) where they stand, on a ground pile.</summary>
[MessagePackObject]
public sealed record Drop([property: Key(0)] Core.EntityId Person, [property: Key(1)] string Item, [property: Key(2)] int Qty = 1) : StateCommand;

/// <summary>M2-08: pick <c>Qty</c> of an item up from a ground pile within reach.</summary>
[MessagePackObject]
public sealed record PickUp([property: Key(0)] Core.EntityId Person, [property: Key(1)] Core.EntityId Pile, [property: Key(2)] string Item, [property: Key(3)] int Qty = 1) : StateCommand;

/// <summary>11 §8.2 second chances: <c>Inspector</c> looks over what <c>Container</c> holds as <c>Item</c> (the label).</summary>
[MessagePackObject]
public sealed record InspectItem([property: Key(0)] Core.EntityId Inspector, [property: Key(1)] Core.EntityId Container, [property: Key(2)] string Item) : StateCommand;

/// <summary>Scenario/dev: sets a person's holding of one item (−1 for none) and their coin (farthings). M1 has no other source of goods.</summary>
[MessagePackObject]
public sealed record SetHoldings([property: Key(0)] Core.EntityId Person, [property: Key(1)] string Item, [property: Key(2)] int Qty, [property: Key(3)] long CoinF) : StateCommand;

/// <summary>The trade UI opens a haggle with the NPC the player is talking to (15 §5): buying from them, or pitching to them.</summary>
[MessagePackObject]
public sealed record TradeOpen([property: Key(0)] Core.EntityId Npc, [property: Key(1)] string Item, [property: Key(2)] int Qty, [property: Key(3)] bool PlayerSells) : StateCommand;

/// <summary>
/// The player's offer in farthings — a bid when buying, an ask when selling — with optional structured arguments
/// (quality_flaw, competitor_price, hardship, relationship, future_business, flattery, bulk_deal; at most 2) and their
/// quality W 0–1 from the fast decider (0 = template mode, W 0.5). Numbers come from the extractor or the UI, never a model.
/// </summary>
[MessagePackObject]
public sealed record TradeOffer([property: Key(0)] ulong Negotiation, [property: Key(1)] long PriceF, [property: Key(2)] string[]? Arguments = null, [property: Key(3)] float Quality = 0f) : StateCommand;

/// <summary>The player takes the NPC's standing offer.</summary>
[MessagePackObject]
public sealed record TradeAccept([property: Key(0)] ulong Negotiation) : StateCommand;

[MessagePackObject]
public sealed record TradeWalkAway([property: Key(0)] ulong Negotiation) : StateCommand;

/// <summary>
/// 22 §4.10: the line an NPC actually said this turn (LLM, regenerated or template), as a logged input. Cosmetic for
/// mechanics — the world changed when the decision executed — but kept in the conversation's transcript for prompts,
/// the journal and gists.
/// </summary>
[MessagePackObject]
public sealed record DialogueLineRendered(
    [property: Key(0)] ulong Conversation, [property: Key(1)] int TurnIndex, [property: Key(2)] Core.EntityId Speaker,
    [property: Key(3)] string Text, [property: Key(4)] string Source, [property: Key(5)] string Flags) : StateCommand;

/// <summary>The player's body position as reported by the client each step (the player is always embodied), with the
/// gait it moved at (0 walk · 1 jog · 2 sprint; 10 §12.1): the sim spends Stamina on a sprint and sets the activity tier.</summary>
[MessagePackObject]
public sealed record PlayerMoved([property: Key(0)] float X, [property: Key(1)] float Z, [property: Key(2)] float Yaw, [property: Key(3)] byte Gait = 0) : StateCommand;

/// <summary>
/// The embodiment boundary (ADR-0007, 20 §3): for an LOD0 person, the client's physics body is authoritative
/// for pose. Godot reports where the body ended up; the sim adopts it. Logged, so replays reproduce LOD0 sessions.
/// </summary>
[MessagePackObject]
public sealed record EmbodimentReport(
    [property: Key(0)] Core.EntityId Person,
    [property: Key(1)] float X,
    [property: Key(2)] float Z,
    [property: Key(3)] float Yaw) : StateCommand;

/// <summary>A command as logged: host-assigned <c>Seq</c>, and the step at which the sim applied it.</summary>
[MessagePackObject]
public readonly record struct CommandEnvelope(
    [property: Key(0)] long Seq,
    [property: Key(1)] long ApplyStep,
    [property: Key(2)] CommandSource Source,
    [property: Key(3)] StateCommand Payload);

/// <summary>
/// M1-29 placeholder theft (16 §10): the player takes an item from a person's holdings within arm's reach. Bystanders
/// may see it (§10.1); what they believe spreads as rumor, and believers grow wary.
/// </summary>
[MessagePackObject]
public sealed record Steal([property: Key(0)] Core.EntityId From, [property: Key(1)] string Item, [property: Key(2)] int Qty) : StateCommand;
