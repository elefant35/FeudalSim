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

    public void CopyFrom(SimWorld world)
    {
        Step = world.Clock.Step;
        GameMs = world.Clock.GameMs;
        var p = world.People;
        if (Ids.Length < p.Count)
        {
            var size = Math.Max(p.Count, Ids.Length * 2);
            Ids = new ulong[size]; X = new float[size]; Z = new float[size]; Yaw = new float[size];
        }

        Count = p.Count;
        for (var i = 0; i < p.Count; i++)
        {
            Ids[i] = p.Ids[i].Value;
            X[i] = p.Transforms[i].X;
            Z[i] = p.Transforms[i].Z;
            Yaw[i] = p.Transforms[i].Yaw;
        }
    }
}
