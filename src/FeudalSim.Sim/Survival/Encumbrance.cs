using FeudalSim.Sim.Content;

namespace FeudalSim.Sim.Survival;

/// <summary>
/// 11 §12.1 encumbrance (M2-08): capacity C = 20 + 4·STR kg; the load ratio r = carried mass / C sets the state: Free
/// (≤ 0.5), Burdened (≤ 1: speeds ×0.85, sprint ×0.7, stamina drain ×1.25, one activity tier up), Overloaded (≤ 1.5: walk
/// only at 0.9 m/s, Heavy) and Immobile (drag at 0.3 m/s). Worn clothing doesn't count. Carry aids (baskets, frames,
/// yokes, travois) arrive with their items.
/// </summary>
public static class Encumbrance
{
    public enum State : byte { Free, Burdened, Overloaded, Immobile }

    public static float CapacityKg(SimWorld world, int row) => 20f + (4f * Skills.Skills.Attribute(world, row, "str"));

    public static float Ratio(SimWorld world, int row)
        => world.Inventory.Mass(world.People.Ids[row], world.Content) / MathF.Max(1f, CapacityKg(world, row));

    public static State Of(float ratio) => ratio <= 0.5f ? State.Free : ratio <= 1f ? State.Burdened : ratio <= 1.5f ? State.Overloaded : State.Immobile;

    /// <summary>Speed for a gait (0 walk, 1 jog, 2 sprint) under a load; the client moves at it.</summary>
    public static float Speed(float walk, float jog, float sprint, byte gait, float ratio) => Of(ratio) switch
    {
        State.Free => gait switch { 2 => sprint, 1 => jog, _ => walk },
        State.Burdened => gait switch { 2 => sprint * 0.7f, 1 => jog * 0.85f, _ => walk * 0.85f },
        State.Overloaded => 0.9f,
        _ => 0.3f,
    };

    /// <summary>The activity tier a moving person's gait means, a tier higher when Burdened, Heavy when Overloaded or worse.</summary>
    public static ActivityLevel Tier(ActivityLevel gaitTier, float ratio) => Of(ratio) switch
    {
        State.Free => gaitTier,
        State.Burdened => gaitTier switch { ActivityLevel.Rest => ActivityLevel.Light, ActivityLevel.Light => ActivityLevel.Moderate, _ => ActivityLevel.Heavy },
        _ => ActivityLevel.Heavy,
    };
}
