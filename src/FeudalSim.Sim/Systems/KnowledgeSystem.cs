using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// 10 §10 (M2-01c-ii): each due, living person who can act sees the tiles around them — sight 150 m in the open, 40 m in
/// fog (10 §6.3 fog sight 40–80 m), halved at night. Stateless (≈ 16 tiles a person per update; raising is idempotent),
/// so a restored save marks exactly what a continuous run marks. LOD0/1 mark once a game minute; LOD2 when due.
/// </summary>
public sealed class KnowledgeSystem : ISimSystem
{
    public const float OpenSightM = 150f, FogSightM = 40f;
    public string Name => "Knowledge";

    public SimPhase Phase => SimPhase.Sense;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var people = world.People;
        var due = world.Due;
        var fog = world.WeatherRef.Sky == Climate.Sky.Fog;
        var night = !Climate.Weather.IsDaylight(ctx.GameMinute);
        var sight = (fog ? FogSightM : OpenSightM) * (night ? 0.5f : 1f);
        var newMinute = ctx.GameMinute != (ctx.GameMs - ctx.DtGameMs) / Time.SimClock.MsPerGameMinute;   // LOD0/1 move ≈ 5 m a game minute
        for (var k = 0; k < due.Count; k++)
        {
            var i = due.Rows[k];
            var tier = people.Lod[i].Tier;
            if (tier == LodTier.Lod3 || (tier != LodTier.Lod2 && !newMinute) || !world.CanAct(i)) { continue; }
            ref readonly var t = ref people.Transforms[i];
            world.Knowledge.See(people.Ids[i], t.X, t.Z, sight);
        }
    }
}
