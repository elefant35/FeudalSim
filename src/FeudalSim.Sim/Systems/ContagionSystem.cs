using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// 11 §7.3 contact spread at LOD0–2 (M2-07a subset): once a game hour, an infectious sleeper and a susceptible sleeper in
/// the same shelter (room = row / sleeps; sailcloth: 3 in 9 m²) share the night context, w = 1.0 × (6 / max(2, m² per
/// sleeper))^0.5; p = 1 − exp(−β × w × inf × sus × 1 h), sus = 11 §5.3 susceptibility (the immune and the ill are skipped).
/// Other contexts (dwelling, work, conversation, caregiving, meals) arrive with 14's buildings and 21's tasks.
/// </summary>
public sealed class ContagionSystem : ISimSystem
{
    /// <summary>11 §7.3 "same sleeping room" weight for the camp's shelters.</summary>
    public static float SleepContext(in CampRecord camp)
        => MathF.Sqrt(6f / MathF.Max(2f, camp.ShelterAreaM2 / MathF.Max(1, (int)camp.ShelterSleeps)));

    public string Name => "Contagion";

    public SimPhase Phase => SimPhase.World;

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (world.Camp.Active == 0 || ctx.GameMinute / 60 == (ctx.GameMs - ctx.DtGameMs) / Time.SimClock.MsPerGameHour) { return; }
        var people = world.People;
        var content = world.Content;
        var sleeps = Math.Max(1, (int)world.Camp.ShelterSleeps);
        var w = SleepContext(world.Camp);
        for (var d = 0; d < content.Diseases.Count; d++)
        {
            var beta = content.Diseases[d].Beta;
            if (beta <= 0f) { continue; }
            for (var i = 0; i < people.Count; i++)
            {
                if (!Sleeper(world, i)) { continue; }
                var inf = Infectiousness(world, i, d);
                if (inf <= 0f) { continue; }
                var room = i / sleeps * sleeps;
                for (var j = room; j < Math.Min(people.Count, room + sleeps); j++)
                {
                    if (j == i || !Sleeper(world, j) || world.Conditions.Has(people.Ids[j], d) || world.Conditions.Immune(people.Ids[j], d, ctx.GameMinute)) { continue; }
                    var age = (ctx.GameMinute - people.Core[j].BirthGameMinute) / Time.GameDate.MinutesPerYear;
                    var sus = Health.Treatment.Susceptibility(people.Needs[j].Satiety, age >= 65, people.Needs[j].Warmth) * Survival.Fitness.StarvationSusceptibility(people.Vitals[j].Starvation);
                    var p = 1f - MathF.Exp(-beta * w * inf * sus);
                    var rng = new Core.Rng(Core.SplitMix64.Mix(world.WorldSeed, (ulong)Core.RngStream.Health, people.Ids[j].Value, (ulong)ctx.GameMinute, ((ulong)(uint)d << 8) | Core.Salt.Contagion));
                    if (rng.Chance(p)) { Health.Conditions.Infect(world, j, d, (ulong)i); }
                }
            }
        }
    }

    private static bool Sleeper(SimWorld world, int row)
    {
        if (world.IsDead(row) || !world.People.Activity[row].Has(ActivityState.Asleep)) { return false; }
        ref readonly var t = ref world.People.Transforms[row];
        float dx = t.X - world.Camp.ShelterX, dz = t.Z - world.Camp.ShelterZ;
        return (dx * dx) + (dz * dz) <= 6f * 6f;
    }

    private static float Infectiousness(SimWorld world, int row, int disease)
    {
        foreach (var c in world.Conditions.Of(world.People.Ids[row]))
        {
            if (c.Disease == disease && c.Stage >= 1) { return world.Content.Diseases[disease].Stages[c.Stage - 1].Contagious; }
        }

        return 0f;
    }
}
