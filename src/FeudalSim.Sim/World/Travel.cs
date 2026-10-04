using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Sim.World;

public enum Surface : byte { None, Trail, Path, Track, Road }

/// <summary>
/// 10 §12.1–12.2 travel (M2-01c-ii): speed = base · biomeMult · surfaceMult · slopeMult · lightMult (snow: M3). Tobler's
/// slope term exp(−3.5·|s + 0.05|)/exp(−0.175) on the signed grade along travel; cliffs over 45° are impassable. Surfaces
/// replace the biome multiplier with their own (mud lowers track and path). Light: 0.6 off-path at night without light,
/// 0.8 on a path; a torch or lantern restores 1.
/// </summary>
public static class Travel
{
    public const float Walk = 1.6f, Jog = 4.0f, Sprint = 6.5f;

    /// <summary>10 §4 biome speed multipliers (beach 0.85; mature high forest 0.75 is a node-level refinement).</summary>
    public static float BiomeMult(Biome b) => b switch
    {
        Biome.CoastDunes => 0.85f, Biome.Meadow => 0.8f, Biome.Broadleaf => 0.65f, Biome.Pine => 0.75f, Biome.Wetland => 0.4f,
        Biome.RiverValley => 0.75f, Biome.HillsMoor => 0.65f, Biome.Highland => 0.5f, _ => 1f,
    };

    public static float SurfaceMult(Surface s, bool mud) => s switch
    {
        Surface.Road => 1f, Surface.Track => mud ? 0.8f : 1f, Surface.Path => mud ? 0.8f : 0.95f, Surface.Trail => 0.85f, _ => 1f,
    };

    /// <summary>Tobler, normalised to 1 at the flat (s is rise/run along travel, uphill positive).</summary>
    public static float SlopeMult(float grade) => MathF.Exp(-3.5f * MathF.Abs(grade + 0.05f)) / MathF.Exp(-0.175f);

    public static float LightMult(bool night, bool onPath, bool carriesLight) => !night || carriesLight ? 1f : onPath ? 0.8f : 0.6f;

    /// <summary>The speed (m/s) for a gait over this ground; 0 where a cliff (slope over 45°) blocks.</summary>
    public static float Speed(float baseMs, Biome biome, Surface surface, bool mud, float grade, float slopeDeg, bool night, bool carriesLight)
    {
        if (slopeDeg > 45f) { return 0f; }
        var ground = surface == Surface.None ? BiomeMult(biome) : SurfaceMult(surface, mud);
        return baseMs * ground * SlopeMult(grade) * LightMult(night, surface != Surface.None, carriesLight);
    }
}
