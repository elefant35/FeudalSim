namespace FeudalSim.Sim.Systems;

/// <summary>13 §4.2: once a game minute, processes hold on interruption, advance into passive stages, ripen, overrun and complete.</summary>
public sealed class ProcessSystem : ISimSystem
{
    public string Name => "Processes";

    public SimPhase Phase => SimPhase.World;

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (world.Processes.Count == 0) { return; }
        var minute = ctx.GameMinute;
        var prev = (ctx.GameMs - ctx.DtGameMs) / Time.SimClock.MsPerGameMinute;
        if (minute == prev) { return; }
        Crafting.Processes.Tick(world, minute);
    }
}
