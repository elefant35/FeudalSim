namespace FeudalSim.Sim.World;

/// <summary>Identity and life facts (rules owned by 16). Names live in a parallel column.</summary>
public struct PersonCore
{
    public long BirthGameMinute;
    public byte Sex;
    public byte LifeStage;
    public uint Flags;
}

/// <summary>Position (metres; X east, −Z north, +Y up) and facing.</summary>
public struct Transform
{
    public float X, Y, Z, Yaw;
}

/// <summary>Needs, 0–100 where 100 = fully satisfied (canon §10.5).</summary>
public struct Needs
{
    public float Satiety, Hydration, Energy, Warmth;
    public float Social, Comfort, Safety, Purpose, Status;

    public static Needs Full => new()
    {
        Satiety = 100, Hydration = 100, Energy = 100, Warmth = 100,
        Social = 100, Comfort = 100, Safety = 100, Purpose = 100, Status = 100,
    };
}

public enum LodTier : byte { Lod0, Lod1, Lod2, Lod3, Lod0Battle }

/// <summary>Simulation level of detail (canon §8.2).</summary>
public struct LodState
{
    public LodTier Tier;
    public long LastUpdateGameMs;

    /// <summary>LOD0 only: false from promotion until the client's first body report (pending embodiment).</summary>
    public bool Embodied;

    /// <summary>LOD0 only: the step at which the person was first seen beyond the demotion radius (0 = not far).</summary>
    public long FarSinceStep;
}

/// <summary>M0 toy behavior state for <c>WanderSystem</c>; replaced by the NPC AI in M1.</summary>
public struct WanderState
{
    public float HomeX, HomeZ, TargetX, TargetZ;
    public long PauseUntilStep;
    public bool HasTarget;
}
