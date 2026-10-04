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
            var sprinting = moving && s.Gait == 2 && step >= s.WindedUntilStep && world.CanAct(i);
            if (sprinting)
            {
                var spend = MathF.Min(s.Value, StaminaRules.SprintPerSecond * dt);
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
                people.Activity[i].Level = !moving ? ActivityLevel.Rest : sprinting ? ActivityLevel.Heavy : s.Gait == 1 || s.Gait == 2 ? ActivityLevel.Moderate : ActivityLevel.Light;
            }
        }
    }
}
