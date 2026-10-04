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
