using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;

namespace FeudalSim.Sim.Health;

/// <summary>
/// 11 §4–§5.2, §5.4 and §14 as rules (M2-06a): trauma → injury records, severity tiers, bleeding and clotting, Blood
/// regeneration, daily healing, Pain, capacities, the derived Health score and the combat-penalty interface for 18.
/// Infection, treatment and permanent effects are M2-06b's (wounds start Clean and untreated).
/// </summary>
public static class HealthRules
{
    /// <summary>18's severity tiers: 0 Minor 8–19 · 1 Moderate 20–34 · 2 Severe 35–54 · 3 Critical ≥ 55.</summary>
    public static int Tier(float severity) => severity >= 55f ? 3 : severity >= 35f ? 2 : severity >= 20f ? 1 : 0;

    /// <summary>11 §5.1 bleed per game hour by type and tier.</summary>
    public static float Bleed(InjuryType type, int tier) => type switch
    {
        InjuryType.Cut => tier switch { 0 => 1f, 1 => 4f, 2 => 10f, _ => 25f },
        InjuryType.Puncture => tier switch { 0 => 1f, 1 => 5f, 2 => 12f, _ => 30f },
        _ => 0f,
    };

    public const float ArterialBleed = 40f;
    public const float CompoundBleed = 8f;

    /// <summary>11 §5.2 natural clotting per whole hour: Minor stops after 1 h, Moderate ×0.75, Severe ×0.9, Critical none.</summary>
    public static float ClotFactor(int tier) => tier switch { 0 => 0f, 1 => 0.75f, 2 => 0.9f, _ => 1f };

    /// <summary>11 §5.1 base heal days (severity 50, treated, rested).</summary>
    public static float BaseHealDays(InjuryType type) => type switch
    {
        InjuryType.Cut => 1.5f, InjuryType.Puncture => 2.5f, InjuryType.Bruise => 0.5f, InjuryType.Fracture => 4f,
        InjuryType.Burn => 2f, InjuryType.Frostbite => 2f, InjuryType.Concussion => 1f, _ => 3f,
    };

    /// <summary>11 §4.3 pain factors (Internal is not listed: taken as a puncture's 0.6).</summary>
    public static float PainFactor(InjuryType type, bool splinted) => type switch
    {
        InjuryType.Cut => 0.5f, InjuryType.Puncture => 0.6f, InjuryType.Fracture => splinted ? 0.5f : 1f, InjuryType.Burn => 0.9f,
        InjuryType.Frostbite => 0.3f, InjuryType.Concussion => 0.4f, InjuryType.Bruise => 0.4f, _ => 0.6f,
    };

    /// <summary>11 §4.3 impairment factors (Internal is not listed: taken as a puncture's 0.7).</summary>
    public static float Impair(InjuryType type, bool splinted) => type switch
    {
        InjuryType.Cut => 0.6f, InjuryType.Puncture => 0.7f, InjuryType.Bruise => 0.3f, InjuryType.Burn => 0.6f,
        InjuryType.Frostbite => 0.5f, InjuryType.Concussion => 1f, InjuryType.Fracture => splinted ? 0.6f : 1.5f, _ => 0.7f,
    };

    /// <summary>11 §4.3 capacity(region) = clamp(1 − Σ sev × impair/100, 0, 1).</summary>
    public static float Capacity(IReadOnlyList<Injury> injuries, BodyRegion region)
    {
        var loss = 0f;
        for (var k = 0; k < injuries.Count; k++)
        {
            var i = injuries[k];
            if (i.Region == region) { loss += i.Severity * Impair(i.Type, (i.Treated & Treated.Splinted) != 0) / 100f; }
        }

        return Math.Clamp(1f - loss, 0f, 1f);
    }

    public static float Mobility(IReadOnlyList<Injury> injuries) => MathF.Min(Capacity(injuries, BodyRegion.LegL), Capacity(injuries, BodyRegion.LegR));

    /// <summary>11 §4.3 Pain = Σ sev × painF, clamped 0–100 (analgesia: 06b).</summary>
    public static float Pain(IReadOnlyList<Injury> injuries)
    {
        var pain = 0f;
        for (var k = 0; k < injuries.Count; k++)
        {
            var i = injuries[k];
            pain += (i.Severity * PainFactor(i.Type, (i.Treated & Treated.Splinted) != 0)) + (i.Infection != InfectionState.Clean ? 10f : 0f);   // Inflamed+: +10
        }

        return Math.Clamp(pain, 0f, 100f);
    }

    /// <summary>11 §4.1 Health = clamp(100 − Bruise − Σ sev − 0.8·(100 − Blood) − ConditionLoad, 0, 100); load: hypothermia 0.6, infection 0.5.</summary>
    public static float Health(float bruise, float severitySum, float blood, float hypothermia, float infection = 0f)
        => Math.Clamp(100f - bruise - severitySum - (0.8f * (100f - blood)) - (0.6f * hypothermia) - (0.5f * infection), 0f, 100f);

    /// <summary>The §4.3 interface 18 queries (1 Hz and on change).</summary>
    public readonly record struct CombatPenalties(float AttackSpeedMult, float DamageMult, float MoveSpeedMult, float StaminaRegenMult, float PerceptionMod, bool CanUseTwoHanded);

    public static CombatPenalties Penalties(IReadOnlyList<Injury> injuries, float blood, bool rightHanded = true)
    {
        var manipL = Capacity(injuries, BodyRegion.ArmL);
        var manipR = Capacity(injuries, BodyRegion.ArmR);
        var weapon = rightHanded ? manipR : manipL;
        return new CombatPenalties(0.5f + (0.5f * weapon), 0.6f + (0.4f * weapon), MoveSpeedMult(injuries, blood),
            StaminaRegenMult(injuries, blood), -3f * (1f - Capacity(injuries, BodyRegion.Head)), MathF.Min(manipL, manipR) >= 0.6f);
    }

    /// <summary>MoveSpeedMult = 0.3 + 0.7·Mobility × bloodF (Blood &lt; 60: ×0.8).</summary>
    public static float MoveSpeedMult(IReadOnlyList<Injury> injuries, float blood) => (0.3f + (0.7f * Mobility(injuries))) * (blood < 60f ? 0.8f : 1f);

    /// <summary>StaminaRegenMult = (0.5 + 0.5·Breathing) × (Blood &lt; 60 ? 0.6 : 1); Breathing is the torso's capacity.</summary>
    public static float StaminaRegenMult(IReadOnlyList<Injury> injuries, float blood)
        => (0.5f + (0.5f * Capacity(injuries, BodyRegion.Torso))) * (blood < 60f ? 0.6f : 1f);

    /// <summary>11 §5.2 Max Stamina: Blood &lt; 80 ×0.85 (and &lt; 60 ×0.6, per 18's table row).</summary>
    public static float StaminaMaxMult(float blood) => blood < 60f ? 0.6f : blood < 80f ? 0.85f : 1f;

    /// <summary>11 §5.4 healing multiplier M without the wound's own terms (pass treatment 1 and apply <see cref="Treatment.HealFactor"/>
    /// and the infection factor per wound; the default 0.6 is the untreated value).</summary>
    public static float HealMultiplier(ActivityLevel activity, float satiety, bool child, bool elder, float endurance, float warmth, float treatment = 0.6f)
    {
        var rest = activity switch { ActivityLevel.Sleep or ActivityLevel.Rest => 1.4f, ActivityLevel.Light => 1f, ActivityLevel.Moderate => 0.75f, _ => 0.5f };
        var nutrition = satiety <= 0f ? 0.2f : satiety < 20f ? 0.75f : 1f;
        var age = child ? 1.25f : elder ? 0.7f : 1f;
        return rest * nutrition * treatment * age * (0.8f + (0.04f * endurance)) * (warmth < 40f ? 0.8f : 1f);
    }

    /// <summary>
    /// 11 §4.1–4.2 trauma: hits under 8 go to the Bruise pool; otherwise an injury of severity = effective, typed by damage
    /// (cut → Cut; pierce → Puncture, +Internal at 50 % on a Severe torso hit; blunt → Bruise, a Fracture on a limb with
    /// p = clamp((sev − 25)/30, 0, 0.9) or a Concussion on the head with p 0.4 at ≥ 20), with §5.1 bleeding (25 % of
    /// Critical limb cuts arterial; compound fracture p 0.3 at ≥ 55) and §5.3 contamination. Returns the injury id or 0.
    /// </summary>
    public static ulong Trauma(SimWorld world, int row, float effective, DamageType damage, BodyRegion region, TraumaSource source)
    {
        var people = world.People;
        ref var v = ref people.Vitals[row];
        if (effective <= 0f || v.Dead) { return 0; }
        if (effective < 8f)
        {
            v.Bruise = MathF.Min(100f, v.Bruise + effective);
            return 0;
        }

        var person = people.Ids[row];
        var id = world.Injuries.NextId();
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Health, person.Value, id, Salt.Trauma));
        var limb = region is BodyRegion.ArmL or BodyRegion.ArmR or BodyRegion.LegL or BodyRegion.LegR;
        var tier = Tier(effective);
        var type = damage switch
        {
            DamageType.Cut => InjuryType.Cut,
            DamageType.Pierce => InjuryType.Puncture,
            DamageType.Burn => InjuryType.Burn,
            _ => limb && rng.Chance(Math.Clamp((effective - 25f) / 30f, 0f, 0.9f)) ? InjuryType.Fracture
                : region == BodyRegion.Head && effective >= 20f && rng.Chance(0.4f) ? InjuryType.Concussion : InjuryType.Bruise,
        };
        var injury = new Injury
        {
            Id = id, CreatedMin = world.Clock.GameMinute, Severity = MathF.Min(100f, effective), Region = region, Type = type,
            BleedRate = Bleed(type, tier),
            Contamination = source switch { TraumaSource.CleanBlade => 0.2f, TraumaSource.Animal => 0.8f, TraumaSource.Fire => 0.3f, _ => 0.5f },
            TourniquetMin = -1,
        };
        if (type is InjuryType.Cut or InjuryType.Puncture || (type == InjuryType.Burn && effective >= 20f)) { injury.Flags |= Injury.OpenFlag; }
        if (type == InjuryType.Cut && tier == 3 && limb && rng.Chance(0.25f)) { (injury.BleedRate, injury.Flags) = (ArterialBleed, (byte)(injury.Flags | Injury.ArterialFlag)); }
        if (type == InjuryType.Fracture && effective >= 55f && rng.Chance(0.3f)) { (injury.BleedRate, injury.Flags) = (CompoundBleed, (byte)(injury.Flags | Injury.OpenFlag)); }
        world.Injuries.Add(person, injury);
        world.Emit(Salience.Notable, person, new InjuryTaken(person, id, (byte)region, (byte)type, injury.Severity));

        if (type == InjuryType.Puncture && region == BodyRegion.Torso && tier >= 2 && rng.Chance(0.5f))
        {
            var internalId = world.Injuries.NextId();
            world.Injuries.Add(person, new Injury
            {
                Id = internalId, CreatedMin = injury.CreatedMin, Severity = injury.Severity * 0.5f, Region = region, Type = InjuryType.Internal,
                BleedRate = rng.Uniform(3f, 8f), Contamination = 0f, TourniquetMin = -1,   // hidden: diagnosed only by Healing ≥ 40 (13 §8)
            });
        }

        return id;
    }
}
