using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// Simulation level of detail (canon §8.2, 21 §15): people within 80 m of the player are embodied (LOD0,
/// cap 48, nearest first by id order for M0); an LOD0 person beyond 100 m for 5 s is demoted to LOD1.
/// Without a player (headless), everyone stays LOD1.
/// </summary>
public sealed class LodSystem : ISimSystem
{
    public const float PromoteRadiusM = 80f;
    public const float DemoteRadiusM = 100f;
    public const int DemoteAfterSteps = 50;   // 5 s at 10 Hz
    public const int MaxEmbodied = 48;

    public string Name => "Lod";
    public SimPhase Phase => SimPhase.Sense;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var player = world.Player;
        var p = world.People;
        var lod = p.Lod;
        var embodied = 0;
        for (var i = 0; i < p.Count; i++) { if (lod[i].Tier == LodTier.Lod0) { embodied++; } }

        for (var i = 0; i < p.Count; i++)
        {
            ref var l = ref lod[i];
            if (!player.Present)
            {
                if (l.Tier == LodTier.Lod0) { Demote(world, i, ref l); }
                continue;
            }

            var dx = p.Transforms[i].X - player.X;
            var dz = p.Transforms[i].Z - player.Z;
            var d = MathF.Sqrt((dx * dx) + (dz * dz));
            if (l.Tier == LodTier.Lod1 && d <= PromoteRadiusM && embodied < MaxEmbodied)
            {
                l.Tier = LodTier.Lod0;
                l.Embodied = false;
                l.FarSinceStep = 0;
                embodied++;
                world.Emit(Salience.Trace, p.Ids[i], new LodChanged(p.Ids[i], LodTier.Lod1, LodTier.Lod0));
            }
            else if (l.Tier == LodTier.Lod0)
            {
                if (d < DemoteRadiusM) { l.FarSinceStep = 0; }
                else if (l.FarSinceStep == 0) { l.FarSinceStep = ctx.Step; }
                else if (ctx.Step - l.FarSinceStep >= DemoteAfterSteps)
                {
                    Demote(world, i, ref l);
                    embodied--;
                }
            }
        }
    }

    private static void Demote(SimWorld world, int i, ref LodState l)
    {
        l.Tier = LodTier.Lod1;
        l.Embodied = false;
        l.FarSinceStep = 0;
        world.Emit(Salience.Trace, world.People.Ids[i], new LodChanged(world.People.Ids[i], LodTier.Lod0, LodTier.Lod1));
    }
}
