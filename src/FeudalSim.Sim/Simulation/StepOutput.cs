using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Events;

namespace FeudalSim.Sim;

/// <summary>Everything one step hands to Hosting (20 §2.4). Snapshots and AI outboxes arrive in later milestones.</summary>
public sealed class StepOutput
{
    public long Step { get; init; }
    public long GameMs { get; init; }
    public IReadOnlyList<CommandEnvelope> AppliedCommands { get; init; } = [];
    public IReadOnlyList<EventEnvelope> Events { get; init; } = [];

    /// <summary>New AI requests for the gateway (outbox).</summary>
    public IReadOnlyList<Ai.AiRequest> AiRequests { get; init; } = [];

    /// <summary>State hash after this step, when hashing was requested (20 §8.7); otherwise 0.</summary>
    public ulong StateHash { get; init; }
}
