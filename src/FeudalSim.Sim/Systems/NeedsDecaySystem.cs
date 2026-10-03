using FeudalSim.Sim.Content;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// M0: physical needs fall with game time at the "light" activity rate from content
/// (need.*.decay_per_hour, 11-survival §2.1). Activity levels, thresholds and recovery arrive with the
/// survival systems in M1/M2. Falls back to the 11 §2.1 light rates when no content is loaded.
/// </summary>
public sealed class NeedsDecaySystem : ISimSystem
{
    public const float DefaultSatietyPerHour = 4.0f;
    public const float DefaultHydrationPerHour = 3.5f;
    public const float DefaultEnergyPerHour = 3.5f;

    public string Name => "NeedsDecay";
    public SimPhase Phase => SimPhase.Resolve;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var content = world.Content;
        var satiety = Light(content, "need.satiety", DefaultSatietyPerHour) * ctx.DtGameHours;
        var hydration = Light(content, "need.hydration", DefaultHydrationPerHour) * ctx.DtGameHours;
        var energy = Light(content, "need.energy", DefaultEnergyPerHour) * ctx.DtGameHours;
        foreach (ref var n in world.People.Needs)
        {
            n.Satiety = MathF.Max(0, n.Satiety - satiety);
            n.Hydration = MathF.Max(0, n.Hydration - hydration);
            n.Energy = MathF.Max(0, n.Energy - energy);
        }
    }

    private static float Light(ContentDatabase content, string id, float fallback)
        => content.Need(id)?.DecayPerHour?.Light ?? fallback;
}
