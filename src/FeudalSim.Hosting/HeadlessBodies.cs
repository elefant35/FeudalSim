using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Hosting;

/// <summary>
/// Stands in for the client's bodies (ADR-0007) when a player is present but no Godot runs (headless drivers, CI): each
/// LOD0 person walks straight to its wander target at canon walking speed and is reported back as an
/// <see cref="EmbodimentReport"/>, as the client would. No navmesh, no collisions.
/// </summary>
public sealed class HeadlessBodies
{
    private readonly List<EmbodimentReport> _reports = [];

    /// <summary>The reports for this step (reuse the list's contents before the next call).</summary>
    public IReadOnlyList<EmbodimentReport> Step(SimWorld world, float dtSeconds = 0.1f)
    {
        _reports.Clear();
        var p = world.People;
        var playerRow = world.PlayerRow;
        var stepLen = WanderSystem.WalkSpeedMetresPerSecond * dtSeconds;
        for (var i = 0; i < p.Count; i++)
        {
            if (i == playerRow || p.Lod[i].Tier != LodTier.Lod0) { continue; }
            ref readonly var t = ref p.Transforms[i];
            var (x, z, yaw) = (t.X, t.Z, t.Yaw);
            if (p.Wander[i].HasTarget)
            {
                var dx = p.Wander[i].TargetX - x;
                var dz = p.Wander[i].TargetZ - z;
                var d = MathF.Sqrt((dx * dx) + (dz * dz));
                if (d > 0.001f)
                {
                    var s = MathF.Min(d, stepLen);
                    (x, z, yaw) = (x + (dx / d * s), z + (dz / d * s), MathF.Atan2(dx, -dz));
                }
            }

            _reports.Add(new EmbodimentReport(p.Ids[i], x, z, yaw));
        }

        return _reports;
    }
}
