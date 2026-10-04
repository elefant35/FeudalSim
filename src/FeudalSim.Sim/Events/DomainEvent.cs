using FeudalSim.Sim.Core;
using MessagePack;

namespace FeudalSim.Sim.Events;

public enum Salience : byte { Trace, Minor, Notable, Major, Historic }

/// <summary>Ground-truth facts emitted by the sim (20 §9.2). Union keys are append-only.</summary>
[Union(0, typeof(CommandRejected))]
[Union(1, typeof(DayLengthChanged))]
[Union(2, typeof(PersonSpawned))]
[Union(3, typeof(DayStarted))]
[Union(4, typeof(AiResultApplied))]
[Union(5, typeof(LodChanged))]
[Union(6, typeof(Embodied))]
[Union(7, typeof(DecisionResolved))]
[Union(8, typeof(DecisionPointCancelled))]
[Union(9, typeof(IntegrityMismatch))]
[Union(10, typeof(InteractionResolved))]
[Union(11, typeof(GossipExchanged))]
[Union(12, typeof(ConversationStarted))]
[Union(13, typeof(ConversationEnded))]
[Union(14, typeof(InitiativeTaken))]
[Union(15, typeof(ConfrontationEscalated))]
[Union(16, typeof(FightResolved))]
[Union(17, typeof(BystanderIntervened))]
[Union(18, typeof(RequestAnswered))]
[Union(19, typeof(FavorDone))]
[Union(20, typeof(LieCaught))]
[Union(21, typeof(TradeOffered))]
[Union(22, typeof(TradeSettled))]
[Union(23, typeof(NegotiationEnded))]
[Union(24, typeof(TheftCommitted))]
[Union(25, typeof(HypothermiaRose))]
public abstract record DomainEvent;

[MessagePackObject]
public sealed record CommandRejected([property: Key(0)] long Seq, [property: Key(1)] string Reason) : DomainEvent;

[MessagePackObject]
public sealed record DayLengthChanged([property: Key(0)] int Minutes) : DomainEvent;

[MessagePackObject]
public sealed record PersonSpawned([property: Key(0)] EntityId Person, [property: Key(1)] string Name) : DomainEvent;

/// <summary>Emitted when the game clock crosses midnight. <c>DayIndex</c> counts days since Y0 Spring 1.</summary>
[MessagePackObject]
public sealed record DayStarted([property: Key(0)] long DayIndex) : DomainEvent;

/// <summary>An AI request was resolved — by the model's result, or by the sim's fallback at its deadline.</summary>
[MessagePackObject]
public sealed record AiResultApplied(
    [property: Key(0)] long RequestId,
    [property: Key(1)] bool UsedFallback,
    [property: Key(2)] string Text,
    [property: Key(3)] string ProviderTag) : DomainEvent;

[MessagePackObject]
public sealed record LodChanged([property: Key(0)] EntityId Person, [property: Key(1)] World.LodTier From, [property: Key(2)] World.LodTier To) : DomainEvent;

/// <summary>The first body report after promotion; <c>SnapDistance</c> is how far the body landed from the sim's pose (m).</summary>
[MessagePackObject]
public sealed record Embodied([property: Key(0)] EntityId Person, [property: Key(1)] float SnapDistance) : DomainEvent;

/// <summary>How a decision point ended: who chose, which option, and the guard's verdict (22 §6). <c>Rejected</c> is the
/// decider's choice when a guard turned it down and the policy chose instead.</summary>
[MessagePackObject]
public sealed record DecisionResolved(
    [property: Key(0)] ulong Dp,
    [property: Key(1)] string Owner,
    [property: Key(2)] EntityId Chooser,
    [property: Key(3)] string Chosen,
    [property: Key(4)] Decisions.DeciderKind Decider,
    [property: Key(5)] Decisions.GuardOutcome Guard,
    [property: Key(6)] string? Rejected,
    [property: Key(7)] string ProviderTag) : DomainEvent;

[MessagePackObject]
public sealed record DecisionPointCancelled([property: Key(0)] ulong Dp, [property: Key(1)] string Reason) : DomainEvent;

/// <summary>A logged integrity record did not match what the sim recomputed (20 §8.5) — a desync.</summary>
[MessagePackObject]
public sealed record IntegrityMismatch([property: Key(0)] long Seq, [property: Key(1)] string Reason) : DomainEvent;

[MessagePackObject]
public readonly record struct EventEnvelope(
    [property: Key(0)] long Seq,
    [property: Key(1)] long Step,
    [property: Key(2)] long GameMinute,
    [property: Key(3)] Salience Salience,
    [property: Key(4)] EntityId Primary,
    [property: Key(5)] DomainEvent Payload);

/// <summary>An NPC↔NPC interaction resolved (16 §5; ground truth for claims, hook for overheard talk).</summary>
[MessagePackObject]
public sealed record InteractionResolved(
    [property: Key(0)] EntityId Actor,
    [property: Key(1)] EntityId Target,
    [property: Key(2)] string Kind,
    [property: Key(3)] bool Success) : DomainEvent;

/// <summary>A claim passed from teller to listener (16 §7.5). <c>Root</c> is the original claim a variant derives from.</summary>
[MessagePackObject]
public sealed record GossipExchanged(
    [property: Key(0)] EntityId Teller,
    [property: Key(1)] EntityId Listener,
    [property: Key(2)] int Claim,
    [property: Key(3)] int Root,
    [property: Key(4)] bool Mutated,
    [property: Key(5)] Social.ToldOption Option) : DomainEvent;

/// <summary>A conversation with the player opened (21 §14.4, 22 §4.11).</summary>
[MessagePackObject]
public sealed record ConversationStarted([property: Key(0)] ulong Conversation, [property: Key(1)] EntityId Npc, [property: Key(2)] EntityId Player) : DomainEvent;

/// <summary>A conversation ended: <c>player</c>, <c>npc</c> (end_conversation), <c>left</c> (out of range), <c>p0</c> (interrupt), <c>asleep</c>.</summary>
[MessagePackObject]
public sealed record ConversationEnded([property: Key(0)] ulong Conversation, [property: Key(1)] EntityId Npc, [property: Key(2)] string Reason) : DomainEvent;

/// <summary>The NPC acted on its own initiative in a conversation (21 §14.5): an offer the player may answer, or a telling.</summary>
[MessagePackObject]
public sealed record InitiativeTaken(
    [property: Key(0)] ulong Conversation,
    [property: Key(1)] EntityId Npc,
    [property: Key(2)] string Option,
    [property: Key(3)] Decisions.OptionParam[] Params) : DomainEvent;

/// <summary>16 §9.5 hand-off to 18: a quarrel turned physical (rung 4 shove, 5 brawl; 6–7 from M2).</summary>
[MessagePackObject]
public sealed record ConfrontationEscalated(
    [property: Key(0)] EntityId A, [property: Key(1)] EntityId B, [property: Key(2)] byte Rung,
    [property: Key(3)] Social.FightIntent Intent, [property: Key(4)] int Witnesses) : DomainEvent;

/// <summary>18 → 16: a fight's outcome (M1: the placeholder brawl — a winner, the loser yields, no injuries).</summary>
[MessagePackObject]
public sealed record FightResolved([property: Key(0)] ulong Fight, [property: Key(1)] EntityId Winner, [property: Key(2)] EntityId Loser, [property: Key(3)] EntityId Starter) : DomainEvent;

/// <summary>A bystander stepped into a quarrel (16 §9.4): both parties' next pressure falls by <c>Calm</c>.</summary>
[MessagePackObject]
public sealed record BystanderIntervened([property: Key(0)] ulong Confrontation, [property: Key(1)] EntityId Who, [property: Key(2)] float Calm) : DomainEvent;

/// <summary>A request DP resolved (16 §5.4): the helper's answer about a task and hours.</summary>
[MessagePackObject]
public sealed record RequestAnswered([property: Key(0)] EntityId Helper, [property: Key(1)] EntityId Asker, [property: Key(2)] string Answer, [property: Key(3)] short Task, [property: Key(4)] float Hours) : DomainEvent;

/// <summary>An agreed favor was carried out.</summary>
[MessagePackObject]
public sealed record FavorDone([property: Key(0)] EntityId Doer, [property: Key(1)] EntityId For, [property: Key(2)] short Task) : DomainEvent;

/// <summary>A theft happened (16 §10): who took what from whom, and how many saw it (ground truth; beliefs are separate).</summary>
[MessagePackObject]
public sealed record TheftCommitted([property: Key(0)] EntityId Thief, [property: Key(1)] EntityId Victim, [property: Key(2)] int Item, [property: Key(3)] int Qty, [property: Key(4)] int Seen) : DomainEvent;

/// <summary>A listener caught a lie (16 §5.2): <c>lied_to_me</c>, trust lost; no being-told DP.</summary>
[MessagePackObject]
public sealed record LieCaught([property: Key(0)] EntityId Listener, [property: Key(1)] EntityId Liar, [property: Key(2)] int Claim) : DomainEvent;

/// <summary>The NPC's standing offer in a haggle (15 §5): the opening ask, a counter, or the same offer after a refuse.</summary>
[MessagePackObject]
public sealed record TradeOffered([property: Key(0)] ulong Negotiation, [property: Key(1)] EntityId Npc, [property: Key(2)] long PriceF, [property: Key(3)] string Move) : DomainEvent;

/// <summary>A deal settled: goods and coin moved at the agreed price.</summary>
[MessagePackObject]
public sealed record TradeSettled([property: Key(0)] ulong Negotiation, [property: Key(1)] EntityId Seller, [property: Key(2)] EntityId Buyer, [property: Key(3)] int Item, [property: Key(4)] int Qty, [property: Key(5)] long PriceF) : DomainEvent;

[MessagePackObject]
public sealed record NegotiationEnded([property: Key(0)] ulong Negotiation, [property: Key(1)] EntityId Npc, [property: Key(2)] string Reason) : DomainEvent;

/// <summary>11 §9.4: a person's hypothermia crossed 50 (confused) or 80 (Downed — incapacitation is M2-06).</summary>
[MessagePackObject]
public sealed record HypothermiaRose([property: Key(0)] EntityId Person, [property: Key(1)] int Level) : DomainEvent;
