using FeudalSim.Sim.Core;
using FeudalSim.Sim.Health;

namespace FeudalSim.Sim.Survival;

/// <summary>
/// 11 §12.3 falling (M2-08). Effective height = h − Athletics/25 (a controlled landing), + 1 m per 0.25 load ratio; then
/// the table: &lt; 2 m nothing; 2–4 m 30 % a leg bruise or sprain (sev 10–30); 4–7 m 50 % a leg fracture (sev 40–70);
/// 7–12 m 80 % fractures (sev 50–80), concussion 40 %, internal 30 %; 12–20 m the same + 40 % fatal; > 20 m 90 % fatal.
/// Fatal falls are one of canon §12's instant deaths. Draws are keyed on the person and the minute.
/// </summary>
public static class Falling
{
    public static float Effective(float heightM, float athletics, float loadRatio) => heightM - (athletics / 25f) + (loadRatio / 0.25f);

    /// <summary>Applies a fall of <paramref name="heightM"/>; returns the effective height.</summary>
    public static float Apply(SimWorld world, int row, float heightM)
    {
        if (world.IsDead(row) || heightM <= 0f) { return 0f; }
        var skill = world.Content.SkillHandle("skill.athletics");
        var athletics = skill >= 0 ? world.People.SkillLevels(row)[skill] : 0;
        var h = Effective(heightM, athletics, Encumbrance.Ratio(world, row));
        if (h < 2f) { return h; }
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Health, world.People.Ids[row].Value, (ulong)world.Clock.GameMinute, Salt.Fall));
        var leg = rng.Chance(0.5f) ? BodyRegion.LegL : BodyRegion.LegR;
        if (h > 20f && rng.Chance(0.9f) || h > 12f && rng.Chance(0.4f))
        {
            Systems.HealthSystem.Kill(world, row, VitalCause.Fall);
            return h;
        }

        if (h < 4f) { if (rng.Chance(0.3f)) { HealthRules.Trauma(world, row, rng.Uniform(10f, 30f), DamageType.Blunt, leg, TraumaSource.Fall); } }
        else if (h < 7f) { if (rng.Chance(0.5f)) { HealthRules.Trauma(world, row, rng.Uniform(40f, 70f), DamageType.Blunt, leg, TraumaSource.Fall); } }
        else if (rng.Chance(0.8f))
        {
            HealthRules.Trauma(world, row, rng.Uniform(50f, 80f), DamageType.Blunt, leg, TraumaSource.Fall);
            if (rng.Chance(0.4f)) { HealthRules.Trauma(world, row, rng.Uniform(30f, 50f), DamageType.Blunt, BodyRegion.Head, TraumaSource.Fall); }   // concussion
            if (rng.Chance(0.3f)) { HealthRules.Trauma(world, row, rng.Uniform(30f, 60f), DamageType.Blunt, BodyRegion.Torso, TraumaSource.Fall); }   // internal
        }

        return h;
    }
}
