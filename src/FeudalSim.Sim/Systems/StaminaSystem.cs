using FeudalSim.Sim.Content;
using FeudalSim.Sim.Survival;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// 11 §3.1 / 18 §2.3 Stamina at LOD0 (M2-05b): a sprint spends 8/s; after 1 s without spending the pool refills at 15/s
/// (with 11's modifiers); empty means Winded for 1.5 s (no sprint). Each 100 spent costs 1 Energy. A reported gait sets
/// the walker's activity tier (11 §2: walk light, jog moderate, sprint heavy; standing still rest). At LOD1+ stamina
/// is not simulated (the activity tier's rates carry it).
/// </summary>
public sealed class StaminaSystem : ISimSystem
{
    private int _athletics = -2;

    public string Name => "Stamina";

    public SimPhase Phase => SimPhase.Resolve;

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (_athletics == -2) { _athletics = world.Content.SkillHandle("skill.athletics"); }
        var people = world.People;
        var dt = Time.SimClock.StepMs / 1000f;
        var step = ctx.Step;
        for (var i = 0; i < people.Count; i++)
        {
            if (people.Lod[i].Tier != LodTier.Lod0 || world.IsDead(i)) { continue; }
            ref var s = ref people.Stamina[i];
            ref var n = ref people.Needs[i];
            var injuries = world.Injuries.Of(people.Ids[i]);
            var blood = people.Vitals[i].Blood;
            var max = StaminaRules.Max(Skills.Skills.Attribute(world, i, "end"), _athletics >= 0 ? people.SkillLevels(i)[_athletics] : 0f, n.Energy, n.Satiety)
                      * Health.HealthRules.StaminaMaxMult(blood);
            var moving = step <= s.GaitUntilStep;
            var load = moving || s.Swimming != 0 ? Survival.Encumbrance.Ratio(world, i) : 0f;   // 11 §12.1 (only for movers)
            var athletics = _athletics >= 0 ? people.SkillLevels(i)[_athletics] : 0f;
            if (s.Swimming != 0 && Swim(world, i, ref s, moving, athletics, load, dt)) { continue; }
            if (s.Swimming == 0 && s.BreathUsed > 0f) { s.BreathUsed = MathF.Max(0f, s.BreathUsed - (5f * dt)); }   // breath back on land
            var sprinting = moving && s.Gait == 2 && step >= s.WindedUntilStep && world.CanAct(i);
            if (sprinting)
            {
                var spend = MathF.Min(s.Value, StaminaRules.SprintPerSecond * dt * (Survival.Encumbrance.Of(load) == Survival.Encumbrance.State.Burdened ? 1.25f : 1f));
                s.Value -= spend;
                s.Spent += spend;
                s.LastSpendStep = step;
                if (s.Value <= 0f) { (s.Value, s.WindedUntilStep) = (0f, step + StaminaRules.WindedSteps); }
                if (s.Spent >= 100f) { s.Spent -= 100f; n.Energy = MathF.Max(0f, n.Energy - 1f); }   // 18 §2.3
            }
            else if ((step - s.LastSpendStep) * dt >= StaminaRules.RegenDelaySeconds)
            {
                s.Value += StaminaRules.Regen(n.Energy, n.Warmth, n.Satiety) * Health.HealthRules.StaminaRegenMult(injuries, blood) * dt;
            }

            s.Value = MathF.Min(s.Value, max);
            if (world.IsPlayer(i) && world.CanAct(i))
            {
                var gaitTier = !moving ? ActivityLevel.Rest : sprinting ? ActivityLevel.Heavy : s.Gait == 1 || s.Gait == 2 ? ActivityLevel.Moderate : ActivityLevel.Light;
                people.Activity[i].Level = moving ? Survival.Encumbrance.Tier(gaitTier, load) : gaitTier;
            }
        }
    }

    /// <summary>11 §12.2 swim speed: 1.0 × (0.6 + Athletics/250) m/s.</summary>
    public static float SwimSpeed(float athletics) => 0.6f + (athletics / 250f);

    /// <summary>11 §12.2 breath: 30 + 2·END seconds.</summary>
    public static float BreathSeconds(float endurance) => 30f + (2f * endurance);

    /// <summary>
    /// 11 §12.2 in deep water (the body reports swimming): stamina drains 3/s + 6/s × load ratio (floundering ×2 below
    /// Athletics 10); floating in place with Athletics ≥ 20 regains 3/s. A load ratio over 0.6 sinks them. With no stamina
    /// left (or sinking) breath runs out: 10 s after the last of it they are unconscious, 60 s later dead. Swimming is Heavy.
    /// True when the step is fully handled here.
    /// </summary>
    private static bool Swim(SimWorld world, int i, ref World.Stamina s, bool moving, float athletics, float load, float dt)
    {
        var people = world.People;
        if (world.CanAct(i)) { people.Activity[i].Level = ActivityLevel.Heavy; }
        var sinking = load > 0.6f;
        if (!moving && athletics >= 20f && !sinking) { s.Value += 3f * dt; }
        else
        {
            var drain = (3f + (6f * load)) * (athletics < 10f ? 2f : 1f);
            s.Value = MathF.Max(0f, s.Value - (drain * dt));
        }

        var max = BreathSeconds(Skills.Skills.Attribute(world, i, "end"));
        if (s.Value <= 0f || sinking || !world.CanAct(i)) { s.BreathUsed += dt; }
        else if (s.BreathUsed > 0f) { s.BreathUsed = MathF.Max(0f, s.BreathUsed - (2f * dt)); }
        ref var v = ref people.Vitals[i];
        if (s.BreathUsed >= max + 70f) { HealthSystem.Kill(world, i, Health.VitalCause.Drowning); }
        else if (s.BreathUsed >= max + 10f && v.State < Health.VitalState.Downed)
        {
            (v.State, v.Cause) = (Health.VitalState.Downed, Health.VitalCause.Drowning);
            world.Emit(Events.Salience.Notable, people.Ids[i], new Events.PersonDowned(people.Ids[i], (byte)Health.VitalCause.Drowning));
        }

        return true;
    }
}
