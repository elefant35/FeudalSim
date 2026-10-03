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

    public string Name => "Wander";
    public SimPhase Phase => SimPhase.Decide;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var ids = world.People.Ids;
        var transforms = world.People.Transforms;
        var wander = world.People.Wander;
        var stepDistance = WalkSpeedMetresPerSecond * ctx.DtEmbodiedSeconds;

        for (var i = 0; i < ids.Length; i++)
        {
            ref var w = ref wander[i];
            ref var t = ref transforms[i];

            if (!w.HasTarget)
            {
                if (ctx.Step < w.PauseUntilStep) { continue; }
                var rng = SimRandom.For(ctx, RngStream.Ai, ids[i], Salt.WanderTarget);
                var angle = rng.NextFloat01() * MathF.Tau;
                var distance = rng.NextFloat01() * WanderRadiusMetres;
                w.TargetX = w.HomeX + (MathF.Cos(angle) * distance);
                w.TargetZ = w.HomeZ + (MathF.Sin(angle) * distance);
                w.HasTarget = true;
            }

            var dx = w.TargetX - t.X;
            var dz = w.TargetZ - t.Z;
            var remaining = MathF.Sqrt((dx * dx) + (dz * dz));
            if (remaining <= stepDistance)
            {
                t.X = w.TargetX;
                t.Z = w.TargetZ;
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
