using FeudalSim.Sim.Ai;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// M0 plumbing check: at a fixed step the first settler asks the AI gateway for one line. The result
/// (or the fallback at the deadline) comes back as logged input, proving the end-to-end AI path.
/// </summary>
public sealed class AiPingSystem(long atStep, int deadlineSteps) : ISimSystem
{
    public string Name => "AiPing";
    public SimPhase Phase => SimPhase.World;

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (ctx.Step != atStep || world.People.Count == 0) { return; }
        world.RequestAi(AiTaskKind.Ping, AiPriority.Interactive, deadlineSteps, world.People.Ids[0], EntityId.None,
            "It is the first morning after the shipwreck. Greet the settler beside you at the fire.", "(nods, too tired to speak)");
    }
}
