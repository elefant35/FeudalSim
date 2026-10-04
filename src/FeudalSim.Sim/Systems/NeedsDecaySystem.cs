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
        var people = world.People;
        var due = world.Due;
        if (world.Camp.Active == 0)
        {
            var satiety = Light(content, "need.satiety", DefaultSatietyPerHour);
            var hydration = Light(content, "need.hydration", DefaultHydrationPerHour);
            var energy = Light(content, "need.energy", DefaultEnergyPerHour);
            for (var k = 0; k < due.Count; k++)
            {
                var dtH = due.Dt(k) / (float)Time.SimClock.MsPerGameHour;
                ref var n = ref people.Needs[due.Rows[k]];
                n.Satiety = MathF.Max(0, n.Satiety - (satiety * dtH));
                n.Hydration = MathF.Max(0, n.Hydration - (hydration * dtH));
                n.Energy = MathF.Max(0, n.Energy - (energy * dtH));
            }

            return;
        }

        // M1: the rate follows each person's current activity level (11 §2.1 table in content). LOD3 rows: Lod3System.
        Span<float> sat = stackalloc float[5];
        Span<float> hyd = stackalloc float[5];
        Span<float> en = stackalloc float[5];
        for (var level = 0; level < 5; level++)
        {
            sat[level] = Rate(content, "need.satiety", (ActivityLevel)level, DefaultSatietyPerHour);
            hyd[level] = Rate(content, "need.hydration", (ActivityLevel)level, DefaultHydrationPerHour);
            en[level] = Rate(content, "need.energy", (ActivityLevel)level, DefaultEnergyPerHour);
        }

        for (var k = 0; k < due.Count; k++)
        {
            var i = due.Rows[k];
            if (people.Lod[i].Tier == World.LodTier.Lod3) { continue; }
            var dtH = due.Dt(k) / (float)Time.SimClock.MsPerGameHour;
            ref var n = ref people.Needs[i];
            var level = (int)people.Activity[i].Level;
            n.Satiety = MathF.Max(0, n.Satiety - (sat[level] * dtH));
            n.Hydration = MathF.Max(0, n.Hydration - (hyd[level] * dtH));
            n.Energy = MathF.Max(0, n.Energy - (en[level] * dtH));
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
