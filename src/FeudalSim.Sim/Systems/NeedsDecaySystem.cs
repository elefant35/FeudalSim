namespace FeudalSim.Sim.Systems;

/// <summary>
/// M0 toy: physical needs fall with game time. Placeholder rates per game hour; the real model
/// (activity levels, thresholds, content-driven tuning) is owned by 11-survival and lands in M1.
/// </summary>
public sealed class NeedsDecaySystem : ISimSystem
{
    public const float SatietyPerHour = 2.0f;
    public const float HydrationPerHour = 4.0f;
    public const float EnergyPerHour = 6.25f;

    public string Name => "NeedsDecay";
    public SimPhase Phase => SimPhase.Resolve;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var hours = ctx.DtGameHours;
        foreach (ref var n in world.People.Needs)
        {
            n.Satiety = MathF.Max(0, n.Satiety - (SatietyPerHour * hours));
            n.Hydration = MathF.Max(0, n.Hydration - (HydrationPerHour * hours));
            n.Energy = MathF.Max(0, n.Energy - (EnergyPerHour * hours));
        }
    }
}
