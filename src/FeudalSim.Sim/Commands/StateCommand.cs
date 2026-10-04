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
public abstract record StateCommand;

/// <summary>Changes the real-minutes-per-game-day setting (canon §6). Logged; applies at the next step.</summary>
[MessagePackObject]
public sealed record SetDayLength([property: Key(0)] int Minutes) : StateCommand;

/// <summary>
/// Scenario/dev command: create a person at a home position (metres, X east, −Z north). The sim generates their
/// personality, attributes and skills from the world seed and their id (M1-01). Optional: culture and profession ids
/// (default <c>culture.varrow</c>; a drawn homeland trade) and age in years (0 = drawn adult age). Keys 3–5 were added
/// in M1; logs written before them read as the defaults.
/// </summary>
[MessagePackObject]
public sealed record SpawnPerson(
    [property: Key(0)] string Name,
    [property: Key(1)] float X,
    [property: Key(2)] float Z,
    [property: Key(3)] string? Culture = null,
    [property: Key(4)] string? Profession = null,
    [property: Key(5)] int AgeYears = 0) : StateCommand;

/// <summary>The player's body position as reported by the client each step (the player is always embodied).</summary>
[MessagePackObject]
public sealed record PlayerMoved([property: Key(0)] float X, [property: Key(1)] float Z, [property: Key(2)] float Yaw) : StateCommand;

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
