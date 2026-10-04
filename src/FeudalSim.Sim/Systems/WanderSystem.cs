using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// M0 toy behavior: walk to a random point near home, pause, repeat. Exercises keyed randomness,
/// embodied-time movement and canonical iteration order. Replaced by the NPC AI in M1.
/// </summary>
public sealed class WanderSystem : ISimSystem
{
    public const float WalkSpeedMetresPerSecond = 1.6f;   // canon §10.9
    public const float WanderRadiusMetres = 30f;

    /// <summary>An embodied body counts as arrived within this distance of its target (navmesh paths stop short).</summary>
    public const float ArrivalToleranceM = 0.75f;

    public string Name => "Wander";
    public SimPhase Phase => SimPhase.Decide;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var c = ctx;   // copy: in-parameters can't be captured
        world.Jobs.ForEachChunk(world.People.Count, (start, end) => RunRows(c, world, start, end));
    }

    private static void RunRows(in StepContext ctx, SimWorld world, int start, int end)
    {
        var ids = world.People.Ids;
        var transforms = world.People.Transforms;
        var wander = world.People.Wander;
        var lod = world.People.Lod;
        var stepDistance = WalkSpeedMetresPerSecond * ctx.DtEmbodiedSeconds;

        for (var i = start; i < end; i++)
        {
            ref var w = ref wander[i];
            ref var t = ref transforms[i];

            if (!w.HasTarget)
            {
                if (ctx.Step < w.PauseUntilStep) { continue; }
                var rng = SimRandom.For(ctx, RngStream.Ai, ids[i], Salt.WanderTarget);
                var angle = rng.NextFloat01() * MathF.Tau;
                var distance = rng.NextFloat01() * WanderRadiusMetres;
                w.TargetX = w.HomeX + (SimMath.Cos(angle) * distance);
                w.TargetZ = w.HomeZ + (SimMath.Sin(angle) * distance);
                w.HasTarget = true;
            }

            var dx = w.TargetX - t.X;
            var dz = w.TargetZ - t.Z;
            var remaining = MathF.Sqrt((dx * dx) + (dz * dz));
            var embodied = lod[i].Tier == World.LodTier.Lod0;
            if (embodied && remaining > ArrivalToleranceM) { continue; }   // the client's body is walking there (ADR-0007)
            if (remaining <= stepDistance || embodied)
            {
                if (!embodied)
                {
                    t.X = w.TargetX;
                    t.Z = w.TargetZ;
                }

                w.HasTarget = false;
                var pause = SimRandom.For(ctx, RngStream.Ai, ids[i], Salt.WanderPause);
                w.PauseUntilStep = ctx.Step + pause.Range(50, 300);
                continue;
            }

            t.X += dx / remaining * stepDistance;
            t.Z += dz / remaining * stepDistance;
            t.Yaw = MathF.Atan2(dx, -dz);
        }
    }
}
