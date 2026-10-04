using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// M0 plumbing check for decision points (22 §17.1): at a fixed step the player offers the first settler an iron
/// pot. The settler's answer is a DP — decided by the fast decider or LLM when allowed, by the policy at the
/// deadline otherwise — and the result comes back as logged input. The trade system that would move the pot and
/// the coin arrives in M3; here the <see cref="Events.DecisionResolved"/> event is the whole effect.
/// </summary>
public sealed class DecisionPingOwner : IDecisionPointOwner
{
    public const string Id = "dev.trade_ping";

    public string OwnerId => Id;

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context) =>
    [
        new("accept_at_price", "trade", [new("price_f", 60), new("qty", 1)], true, 0.30f, 0f, Stakes.High, true, "buy the iron pot for 60 farthings"),
        new("counter_step_1", "trade", [new("price_f", 52), new("qty", 1)], true, 0.25f, 0f, Stakes.High, true, "offer 52 farthings for the pot instead"),
        new("refuse", "trade", [], true, 0.45f, 0f, Stakes.Low, false, "decline; they have a pot already"),
    ];

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
    }
}

public sealed class DecisionPingSystem(long atStep, DeciderKind maxDecider, int deadlineSteps) : ISimSystem
{
    public string Name => "DecisionPing";
    public SimPhase Phase => SimPhase.World;

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (ctx.Step != atStep || world.People.Count == 0) { return; }
        world.Decisions.Open(DecisionPingOwner.Id, new DpContext("trade.respond", world.People.Ids[0], EntityId.None, 0), maxDecider, deadlineSteps);
    }
}
