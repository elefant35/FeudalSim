using FeudalSim.Sim.Core;
using MessagePack;

namespace FeudalSim.Sim.Events;

public enum Salience : byte { Trace, Minor, Notable, Major, Historic }

/// <summary>Ground-truth facts emitted by the sim (20 §9.2). Union keys are append-only.</summary>
[Union(0, typeof(CommandRejected))]
[Union(1, typeof(DayLengthChanged))]
[Union(2, typeof(PersonSpawned))]
[Union(3, typeof(DayStarted))]
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

[MessagePackObject]
public readonly record struct EventEnvelope(
    [property: Key(0)] long Seq,
    [property: Key(1)] long Step,
    [property: Key(2)] long GameMinute,
    [property: Key(3)] Salience Salience,
    [property: Key(4)] EntityId Primary,
    [property: Key(5)] DomainEvent Payload);
