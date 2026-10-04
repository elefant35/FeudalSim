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
        var dtH = ctx.DtGameHours;
        if (world.Camp.Active == 0)
        {
            var satiety = Light(content, "need.satiety", DefaultSatietyPerHour) * dtH;
            var hydration = Light(content, "need.hydration", DefaultHydrationPerHour) * dtH;
            var energy = Light(content, "need.energy", DefaultEnergyPerHour) * dtH;
            foreach (ref var n in world.People.Needs)
            {
                n.Satiety = MathF.Max(0, n.Satiety - satiety);
                n.Hydration = MathF.Max(0, n.Hydration - hydration);
                n.Energy = MathF.Max(0, n.Energy - energy);
            }

            return;
        }

        // M1: the rate follows each person's current activity level (11 §2.1 table in content).
        Span<float> sat = stackalloc float[5];
        Span<float> hyd = stackalloc float[5];
        Span<float> en = stackalloc float[5];
        for (var level = 0; level < 5; level++)
        {
            sat[level] = Rate(content, "need.satiety", (ActivityLevel)level, DefaultSatietyPerHour) * dtH;
            hyd[level] = Rate(content, "need.hydration", (ActivityLevel)level, DefaultHydrationPerHour) * dtH;
            en[level] = Rate(content, "need.energy", (ActivityLevel)level, DefaultEnergyPerHour) * dtH;
        }

        var people = world.People;
        for (var i = 0; i < people.Count; i++)
        {
            ref var n = ref people.Needs[i];
            var level = (int)people.Activity[i].Level;
            n.Satiety = MathF.Max(0, n.Satiety - sat[level]);
            n.Hydration = MathF.Max(0, n.Hydration - hyd[level]);
            n.Energy = MathF.Max(0, n.Energy - en[level]);
        }
    }

    private static float Rate(ContentDatabase content, string id, ActivityLevel level, float fallback)
    {
        var d = content.Need(id)?.DecayPerHour;
        return d is null ? fallback : level switch
        {
            ActivityLevel.Sleep => d.Sleep,
            ActivityLevel.Rest => d.Rest,
            ActivityLevel.Light => d.Light,
            ActivityLevel.Moderate => d.Moderate,
            _ => d.Heavy,
        };
    }

    private static float Light(ContentDatabase content, string id, float fallback)
        => content.Need(id)?.DecayPerHour?.Light ?? fallback;
}
