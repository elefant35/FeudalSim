using FeudalSim.Sim;

namespace FeudalSim.Hosting;

/// <summary>What the view needs each step (20 §2.4). M0: clock and positions; grows with the client.</summary>
public sealed class RenderSnapshot
{
    public long Step;
    public long GameMs;
    public int Count;
    public ulong[] Ids = new ulong[64];
    public float[] X = new float[64];
    public float[] Z = new float[64];
    public float[] Yaw = new float[64];
    public byte[] Tier = new byte[64];
    public float[] TargetX = new float[64];
    public float[] TargetZ = new float[64];
    public bool[] HasTarget = new bool[64];

    /// <summary>Current action handle per settler (−1 none); M1 camp view colours by it.</summary>
    public short[] Action = new short[64];

    /// <summary>The player's own character (not drawn as a settler); activity flags (conversing, deliberating…).</summary>
    public bool[] IsPlayer = new bool[64];
    public byte[] ActivityFlags = new byte[64];

    /// <summary>Camp stocks (M1 graybox camp): food in Satiety points, firewood bundles, minutes of fire left.</summary>
    public float Food, Firewood, FireFuelMin;
    public bool CampActive;

    /// <summary>The region's weather (10 §6.3; M2-03) for the sky, light and HUD.</summary>
    public Sim.Climate.WeatherState Weather;

    public void CopyFrom(SimWorld world)
    {
        Step = world.Clock.Step;
        GameMs = world.Clock.GameMs;
        var p = world.People;
        if (Ids.Length < p.Count)
        {
            var size = Math.Max(p.Count, Ids.Length * 2);
            Ids = new ulong[size]; X = new float[size]; Z = new float[size]; Yaw = new float[size];
            Tier = new byte[size]; TargetX = new float[size]; TargetZ = new float[size]; HasTarget = new bool[size];
            Action = new short[size];
            IsPlayer = new bool[size];
            ActivityFlags = new byte[size];
        }

        Count = p.Count;
        for (var i = 0; i < p.Count; i++)
        {
            Ids[i] = p.Ids[i].Value;
            X[i] = p.Transforms[i].X;
            Z[i] = p.Transforms[i].Z;
            Yaw[i] = p.Transforms[i].Yaw;
            Tier[i] = (byte)p.Lod[i].Tier;
            TargetX[i] = p.Wander[i].TargetX;
            TargetZ[i] = p.Wander[i].TargetZ;
            HasTarget[i] = p.Wander[i].HasTarget;
            Action[i] = p.Activity[i].Action;
            IsPlayer[i] = world.IsPlayer(i);
            ActivityFlags[i] = p.Activity[i].Flags;
        }

        CampActive = world.Camp.Active != 0;
        Weather = world.Weather;
        (Food, Firewood, FireFuelMin) = (world.Camp.Food, world.Camp.Firewood, world.Camp.FireFuelMin);
    }
}
