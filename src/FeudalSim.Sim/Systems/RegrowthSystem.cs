namespace FeudalSim.Sim.Systems;

/// <summary>10 §7.4 (M2-13): picked plants and bushes grow back each Spring 1 — their "harvested" deltas clear.</summary>
public sealed class RegrowthSystem : ISimSystem
{
    public string Name => "Regrowth";

    public SimPhase Phase => SimPhase.World;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var day = ctx.GameMinute / 1440;
        var prev = (ctx.GameMs - ctx.DtGameMs) / Time.SimClock.MsPerGameDay;
        if (day == prev || day % Time.GameDate.DaysPerYear != 0 || world.NodeDeltas.Count == 0) { return; }
        world.NodeDeltas.ClearState(Crafting.Foraging.NodeHarvested);
    }
}
