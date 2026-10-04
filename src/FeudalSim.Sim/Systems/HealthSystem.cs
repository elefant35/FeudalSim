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
/// M2-06b: each 6-hour slot runs every wound's infection step (11 §5.3); treatment speeds healing (0.6 + 0.6q, stitched
/// ×1.3), Inflamed halves it and Infected stops it; Septic ≥ 90 downs and 100 kills; Infected or worse is a fever.
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
        if (Conditions.Advance(world, i, now))
        {
            Die(world, i, ref v, VitalCause.Disease);
            return;
        }

        var cond = Conditions.Of(world, people.Ids[i]);
        v.DiseaseFever = cond.Fever ? (byte)1 : (byte)0;
        Tracks(world, i, ref v, dtH);
        ref readonly var n = ref people.Needs[i];
        var list = world.Injuries.ListOf(people.Ids[i]);
        var bleeding = 0f;
        var severitySum = 0f;
        var infection = 0f;
        var septicRising = false;
        if (list is not null)
        {
            var level = people.Activity[i].Level;
            var age = (now - people.Core[i].BirthGameMinute) / Time.GameDate.MinutesPerYear;
            var endurance = Skills.Skills.Attribute(world, i, "end");
            var m = HealthRules.HealMultiplier(level, n.Satiety, age < 14, age >= 65, endurance, n.Warmth, treatment: 1f) * Survival.Fitness.HealMult(world, i);   // M2-07b-ii: starvation tiers, varied diet
            var bedRest = level is ActivityLevel.Sleep or ActivityLevel.Rest || v.Down;
            var slotTo = now / 360;
            var slotFrom = (now - (long)MathF.Round(dtH * 60f)) / 360;
            var susceptibility = Treatment.Susceptibility(n.Satiety, age >= 65, n.Warmth) * Survival.Fitness.StarvationSusceptibility(people.Vitals[i].Starvation);
            for (var k = list.Count - 1; k >= 0; k--)
            {
                var inj = list[k];
                for (var slot = slotFrom + 1; slot <= slotTo; slot++)   // 11 §5.3: one infection step per 6-hour slot
                {
                    var before = inj.InfectionSev;
                    Treatment.Slot(world, people.Ids[i], ref inj, slot, susceptibility, endurance, bedRest);
                    septicRising |= inj.Infection == InfectionState.Septic && inj.InfectionSev > before;
                }

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

                var infectionF = inj.Infection switch { InfectionState.Clean => 1f, InfectionState.Inflamed => 0.5f, _ => 0f };
                inj.Severity -= 50f / HealthRules.BaseHealDays(inj.Type) * m * Treatment.HealFactor(inj) * infectionF * dtH / 24f;
                if (inj.Severity <= 0f && inj.BleedRate <= 0f && inj.Infection == InfectionState.Clean) { list.RemoveAt(k); continue; }
                inj.Severity = MathF.Max(0f, inj.Severity);
                bleeding += inj.BleedRate;
                severitySum += inj.Severity;
                infection = MathF.Max(infection, inj.InfectionSev);
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
        v.Health = Math.Clamp(HealthRules.Health(v.Bruise, severitySum, v.Blood, hypo, infection) - cond.LoadPoints - (0.5f * v.Dehydration) - (0.3f * v.Starvation), 0f, 100f);
        v.Fever = infection > 30f ? (byte)1 : (byte)0;
        Transition(world, i, ref v, bleeding > 0f || people.Needs[i].Warmth < 25f || septicRising, hypo, infection, injuries, now);
    }

    private static void Die(SimWorld world, int i, ref Vitals v, VitalCause cause)
    {
        var id = world.People.Ids[i];
        (v.State, v.Cause) = (VitalState.Dead, cause);
        world.People.Activity[i] = new ActivityState { Action = -1, Level = ActivityLevel.Rest };
        world.Emit(Events.Salience.Major, id, new Events.PersonDied(id, (byte)cause));
        Survival.Water.OnDeath(world, i);   // 11 §7.4: a body at the water taints it
    }

    /// <summary>
    /// 11 §6.2 dehydration (Hydration &lt; 20: +2/h; empty: +5/h, +2 when heavy or hot; recovers −10/h once Hydration ≥ 40)
    /// and §6.1 starvation (while Satiety = 0: +0.25 × the hour's Satiety demand, children ×1.3, elders ×1.2; recovers
    /// −0.25/h once Satiety ≥ 60).
    /// </summary>
    private static void Tracks(SimWorld world, int i, ref Vitals v, float dtH)
    {
        var people = world.People;
        ref readonly var n = ref people.Needs[i];
        var heavy = people.Activity[i].Level == ActivityLevel.Heavy;
        var rate = n.Hydration <= 0f ? 5f + (heavy ? 2f : 0f) : n.Hydration < 20f ? 2f : n.Hydration >= 40f ? -10f : 0f;
        v.Dehydration = Math.Clamp(v.Dehydration + (rate * dtH), 0f, 100f);
        var age = (world.Clock.GameMinute - people.Core[i].BirthGameMinute) / Time.GameDate.MinutesPerYear;
        if (n.Satiety <= 0f)
        {
            var demand = NeedsDecaySystem.Rate(world.Content, "need.satiety", people.Activity[i].Level, NeedsDecaySystem.DefaultSatietyPerHour) * Survival.Exposure.SatietyColdFactor(n.Warmth);   // the hour's unmet demand (11 §2.1)
            v.Starvation = MathF.Min(100f, v.Starvation + (0.25f * demand * (age < 14 ? 1.3f : age >= 65 ? 1.2f : 1f) * dtH));
        }
        else if (n.Satiety >= 60f) { v.Starvation = MathF.Max(0f, v.Starvation - (0.25f * dtH)); }
    }

    private static void Transition(SimWorld world, int i, ref Vitals v, bool lethalRising, float hypothermia, float infection, IReadOnlyList<Injury> injuries, long now)
    {
        var people = world.People;
        var id = people.Ids[i];
        if (v.Blood <= 0f || hypothermia >= 100f || infection >= 100f || v.Dehydration >= 100f || v.Starvation >= 100f)
        {
            Die(world, i, ref v, v.Blood <= 0f ? VitalCause.BloodLoss : hypothermia >= 100f ? VitalCause.Hypothermia : infection >= 100f ? VitalCause.Infection
                : v.Dehydration >= 100f ? VitalCause.Dehydration : VitalCause.Starvation);
            return;
        }

        var trigger = v.Health <= 0f ? VitalCause.Trauma : v.Blood < 35f ? VitalCause.BloodLoss : hypothermia >= 80f ? VitalCause.Hypothermia
            : infection >= 90f ? VitalCause.Infection : v.Dehydration >= 85f ? VitalCause.Dehydration : v.Starvation >= 90f ? VitalCause.Starvation : VitalCause.None;
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
