using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Survival;

/// <summary>
/// M2-07b-ii: how hunger, thirst, starvation, illness and diet change what a body can do — one place for 11's multipliers.
/// Work rate (§2.2 needs tiers, §6.1 starvation tiers, §6.2 dehydration, §7 a condition's <c>work</c>), healing (§6.1,
/// §10.3 a varied diet ×1.1) and infection susceptibility (§6.1). Parity: the player and NPCs read the same numbers.
/// </summary>
public static class Fitness
{
    /// <summary>Work-rate multiplier (labor time divides by it; camp yields multiply).</summary>
    public static float WorkMult(SimWorld world, int row)
    {
        var people = world.People;
        ref readonly var n = ref people.Needs[row];
        ref readonly var v = ref people.Vitals[row];
        var m = n.Satiety <= 0f ? 0.8f : n.Satiety < 20f ? 0.8f : n.Satiety < 40f ? 0.95f : 1f;   // §2.2 Ravenous / Hungry
        m *= n.Hydration < 20f ? 0.7f : n.Hydration < 40f ? 0.9f : 1f;                              // §2.2 Dehydrating / Parched
        m *= v.Starvation >= 75f ? 0.5f : v.Starvation >= 50f ? 0.8f : 1f;                           // §6.1 Starving / Malnourished
        m *= v.Dehydration >= 60f ? 0.5f : v.Dehydration >= 30f ? 0.8f : 1f;                         // §6.2
        m *= Health.Conditions.Of(world, people.Ids[row]).Work;                                       // §7 the Flux, poisoning …
        return MathF.Max(0.05f, m);
    }

    /// <summary>Healing multiplier: §6.1 Underfed ×0.75, Malnourished ×0.5, Starving ×0.2; §10.3 a varied diet ×1.1.</summary>
    public static float HealMult(SimWorld world, int row)
    {
        var s = world.People.Vitals[row].Starvation;
        var m = s >= 75f ? 0.2f : s >= 50f ? 0.5f : s >= 25f ? 0.75f : 1f;
        return Eating.Groups(world.People.Diet[row]) >= 3 ? m * 1.1f : m;
    }

    /// <summary>§6.1 infection susceptibility from starvation: Underfed ×1.2, Malnourished ×1.5, Starving ×2.</summary>
    public static float StarvationSusceptibility(float starvation)
        => starvation >= 75f ? 2f : starvation >= 50f ? 1.5f : starvation >= 25f ? 1.2f : 1f;
}
