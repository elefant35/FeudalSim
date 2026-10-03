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
public abstract record StateCommand;

/// <summary>Changes the real-minutes-per-game-day setting (canon §6). Logged; applies at the next step.</summary>
[MessagePackObject]
public sealed record SetDayLength([property: Key(0)] int Minutes) : StateCommand;

/// <summary>Scenario/dev command: create a person at a home position (metres, X east, −Z north).</summary>
[MessagePackObject]
public sealed record SpawnPerson(
    [property: Key(0)] string Name,
    [property: Key(1)] float X,
    [property: Key(2)] float Z) : StateCommand;

/// <summary>A command as logged: host-assigned <c>Seq</c>, and the step at which the sim applied it.</summary>
[MessagePackObject]
public readonly record struct CommandEnvelope(
    [property: Key(0)] long Seq,
    [property: Key(1)] long ApplyStep,
    [property: Key(2)] CommandSource Source,
    [property: Key(3)] StateCommand Payload);
