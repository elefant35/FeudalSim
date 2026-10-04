using FeudalSim.Sim.Content;
using FeudalSim.Sim.Health;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// 11 §4–§5.2, §5.4, §14 for each due person (M2-06a): the Bruise pool recovers 10/h; open wounds bleed and clot by the
/// whole hour (Minor stops after 1 h, Moderate ×0.75, Severe ×0.9, Critical and arterial never); Blood regenerates
/// 0.8/h when nothing bleeds and Hydration ≥ 40 and Satiety ≥ 30 (×1.5 at rest); injuries heal by (50/baseDays)·M a day;
/// then Pain and Health, and the §14 states: Downed at Health ≤ 0, Blood &lt; 35 or Hypothermia ≥ 80; Dying while a
/// lethal track rises; conscious again after 2 h stable with no trigger left; dead at Blood 0 or Hypothermia 100.
/// </summary>
public sealed class HealthSystem : ISimSystem
{
    public const float StableMinutesToWake = 120f;

    public string Name => "Health";

    public SimPhase Phase => SimPhase.Resolve;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var people = world.People;
        var due = world.Due;
        var now = ctx.GameMinute;
        for (var k = 0; k < due.Count; k++)
        {
            var i = due.Rows[k];
            if (people.Lod[i].Tier == LodTier.Lod3) { continue; }
            var dtH = due.Dt(k) / (float)Time.SimClock.MsPerGameHour;
            Update(world, i, dtH, now);
        }
    }

    /// <summary>One person's health over <paramref name="dtH"/> game hours (also used by tests).</summary>
    public static void Update(SimWorld world, int i, float dtH, long now)
    {
        var people = world.People;
        ref var v = ref people.Vitals[i];
        if (v.Dead) { return; }
        ref readonly var n = ref people.Needs[i];
        var list = world.Injuries.ListOf(people.Ids[i]);
        var bleeding = 0f;
        var severitySum = 0f;
        if (list is not null)
        {
            var level = people.Activity[i].Level;
            var age = (now - people.Core[i].BirthGameMinute) / Time.GameDate.MinutesPerYear;
            var m = HealthRules.HealMultiplier(level, n.Satiety, age < 14, age >= 65, Skills.Skills.Attribute(world, i, "end"), n.Warmth);
            for (var k = list.Count - 1; k >= 0; k--)
            {
                var inj = list[k];
                if (inj.BleedRate > 0f)
                {
                    v.Blood = MathF.Max(0f, v.Blood - (inj.BleedRate * dtH));
                    var hours = (int)Math.Min(ushort.MaxValue, (now - inj.CreatedMin) / 60);
                    if (hours > inj.ClotHours && !inj.Arterial)
                    {
                        inj.BleedRate *= MathF.Pow(HealthRules.ClotFactor(HealthRules.Tier(inj.Severity)), hours - inj.ClotHours);
                        if (inj.BleedRate < 0.05f) { inj.BleedRate = 0f; }
                    }

                    inj.ClotHours = (ushort)hours;
                }

                inj.Severity -= 50f / HealthRules.BaseHealDays(inj.Type) * m * dtH / 24f;
                if (inj.Severity <= 0f && inj.BleedRate <= 0f) { list.RemoveAt(k); continue; }
                inj.Severity = MathF.Max(0f, inj.Severity);
                bleeding += inj.BleedRate;
                severitySum += inj.Severity;
                list[k] = inj;
            }
        }

        v.Bruise = MathF.Max(0f, v.Bruise - (10f * dtH));
        if (bleeding <= 0f && n.Hydration >= 40f && n.Satiety >= 30f)
        {
            var rest = people.Activity[i].Level is ActivityLevel.Sleep or ActivityLevel.Rest || v.Down ? 1.5f : 1f;
            v.Blood = MathF.Min(100f, v.Blood + (0.8f * rest * dtH));
        }

        var hypo = people.Body[i].Hypothermia;
        IReadOnlyList<Injury> injuries = list ?? (IReadOnlyList<Injury>)[];
        v.Pain = HealthRules.Pain(injuries);
        v.Health = HealthRules.Health(v.Bruise, severitySum, v.Blood, hypo);
        Transition(world, i, ref v, bleeding > 0f || people.Needs[i].Warmth < 25f, hypo, injuries, now);
    }

    private static void Transition(SimWorld world, int i, ref Vitals v, bool lethalRising, float hypothermia, IReadOnlyList<Injury> injuries, long now)
    {
        var people = world.People;
        var id = people.Ids[i];
        if (v.Blood <= 0f || hypothermia >= 100f)
        {
            (v.State, v.Cause) = (VitalState.Dead, v.Blood <= 0f ? VitalCause.BloodLoss : VitalCause.Hypothermia);
            people.Activity[i] = new ActivityState { Action = -1, Level = ActivityLevel.Rest };
            world.Emit(Events.Salience.Major, id, new Events.PersonDied(id, (byte)v.Cause));
            return;
        }

        var trigger = v.Health <= 0f ? VitalCause.Trauma : v.Blood < 35f ? VitalCause.BloodLoss : hypothermia >= 80f ? VitalCause.Hypothermia : VitalCause.None;
        if (!v.Down)
        {
            if (trigger != VitalCause.None)
            {
                (v.State, v.Cause, v.DownedSinceMin, v.StableSinceMin) = (lethalRising ? VitalState.Dying : VitalState.Downed, trigger, now, lethalRising ? -1 : now);
                people.Activity[i] = new ActivityState { Action = -1, Level = ActivityLevel.Rest };
                world.Emit(Events.Salience.Major, id, new Events.PersonDowned(id, (byte)trigger));
                return;
            }

            var impaired = v.Health < 50f || HealthRules.Mobility(injuries) < 0.5f || HealthRules.Capacity(injuries, BodyRegion.Head) < 0.5f
                || MathF.Min(HealthRules.Capacity(injuries, BodyRegion.ArmL), HealthRules.Capacity(injuries, BodyRegion.ArmR)) < 0.5f
                || HealthRules.Capacity(injuries, BodyRegion.Torso) < 0.5f;
            v.State = impaired ? VitalState.Impaired : VitalState.Active;
            return;
        }

        // Down: Dying while a lethal track rises; stable for 2 h with no trigger left → conscious (11 §14).
        if (lethalRising)
        {
            (v.State, v.StableSinceMin) = (VitalState.Dying, -1);
            return;
        }

        if (v.StableSinceMin < 0) { v.StableSinceMin = now; }
        v.State = now - v.StableSinceMin >= StableMinutesToWake ? VitalState.Recovering : VitalState.Downed;
        if (v.State == VitalState.Recovering && trigger == VitalCause.None)
        {
            (v.State, v.Cause, v.DownedSinceMin, v.StableSinceMin) = (VitalState.Impaired, VitalCause.None, -1, -1);
            v.Health = MathF.Max(5f, v.Health);
            world.Emit(Events.Salience.Notable, id, new Events.PersonRecovered(id));
        }
    }
}
