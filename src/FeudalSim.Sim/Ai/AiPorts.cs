using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using MessagePack;

namespace FeudalSim.Sim.Ai;

// Pure DTOs for the AI boundary (20 §11). No HTTP and no prompts here: the gateway lives in
// FeudalSim.AI and talks to the sim only through these records and logged commands.

public enum AiPriority : byte { Interactive = 0, Proximate = 1, Background = 2, Batch = 3 }

/// <summary>What a request is for. Catalog owned by 22-llm-integration; M0 only needs the ping.</summary>
public enum AiTaskKind : byte { Ping, Dialogue, Bark, Chronicle, Overheard }

public enum AiOutcome : byte { Ok, Timeout, ProviderError, Refused, Cancelled, Unavailable }

/// <summary>A request the sim puts in its outbox. Missing <see cref="DeadlineStep"/> → the sim applies <see cref="FallbackText"/>.</summary>
public sealed record AiRequest(
    long RequestId, AiTaskKind Kind, AiPriority Priority, long IssuedStep, long DeadlineStep,
    EntityId Speaker, EntityId Listener, string Context, string FallbackText);

/// <summary>A gateway result, returned to the sim as a logged command. The sim validates it.</summary>
[MessagePackObject]
public sealed record AiResultCommand(
    [property: Key(0)] long RequestId,
    [property: Key(1)] AiOutcome Outcome,
    [property: Key(2)] string Text,
    [property: Key(3)] string ProviderTag,
    [property: Key(4)] int LatencyMs,
    [property: Key(5)] int TokensIn,
    [property: Key(6)] int TokensOut) : StateCommand;
