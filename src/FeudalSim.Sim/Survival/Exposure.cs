using FeudalSim.Sim.Climate;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Survival;

/// <summary>What a body is exposed to this hour (11 §9.1): the inputs the warmth, wetness and hypothermia rules read.</summary>
public readonly record struct ExposureInputs(float AirC, float WindMs, Sky Sky, float WindBlock, float RainBlock, float ShelterInsC,
    float FireDistM, ActivityLevel Activity, float BeddingInsC, bool Vulnerable);

/// <summary>
/// 11 §9 temperature and exposure (M2-05a): the thermal balance C, warmth dynamics, wetness gain and drying, and
/// hypothermia. Pure functions; <see cref="Systems.ExposureSystem"/> applies them per person.
/// </summary>
public static class Exposure
{
    /// <summary>11 §9.1 wind chill: 0.6 × max(0, wind − 2) × (1 − windBlock), capped at 8 °C.</summary>
    public static float WindChill(float windMs, float windBlock) => MathF.Min(8f, 0.6f * MathF.Max(0f, windMs - 2f) * (1f - windBlock));

    /// <summary>11 §9.1 campfire bonus by distance: ≤ 1.5 m +10 · ≤ 3 m +6 · ≤ 4 m +3.</summary>
    public static float FireBonus(float distM) => distM <= 1.5f ? 10f : distM <= 3f ? 6f : distM <= 4f ? 3f : 0f;

    /// <summary>11 §9.1 activity heat: sleep −2 · rest 0 · light +2 · moderate +4 · heavy +7.</summary>
    public static float ActivityHeat(ActivityLevel level) => level switch
    {
        ActivityLevel.Sleep => -2f, ActivityLevel.Rest => 0f, ActivityLevel.Light => 2f, ActivityLevel.Moderate => 4f, _ => 7f,
    };

    /// <summary>11 §9.1 Ins_eff = Σ Ins × m(Q) × (1 − (1 − wetRet) × Wetness/100), m(Q 50) = 1 until items carry quality.</summary>
    public static float Insulation(in Worn worn, ReadOnlySpan<WearValues> wear, float wetness)
    {
        var total = 0f;
        for (var s = 0; s < Worn.SlotCount; s++)
        {
            var item = worn.Slot(s);
            if (item < 0 || item >= wear.Length) { continue; }
            ref readonly var w = ref wear[item];
            total += w.Ins * (1f - ((1f - w.WetRetention) * wetness / 100f));
        }

        return total;
    }

    /// <summary>The cloak slot's rain resistance (11 §9.3).</summary>
    public static float RainResist(in Worn worn, ReadOnlySpan<WearValues> wear) => worn.Cloak >= 0 && worn.Cloak < wear.Length ? wear[worn.Cloak].RainResist : 0f;

    /// <summary>11 §9.1 C = T_amb + Ins_eff + activityHeat, with T_amb = T_air (+ shelter insulation) − windChill + fireBonus.</summary>
    public static float CoreC(in ExposureInputs x, float insulation)
        => x.AirC + x.ShelterInsC - WindChill(x.WindMs, x.WindBlock) + FireBonus(x.FireDistM) + insulation + x.BeddingInsC + ActivityHeat(x.Activity);

    /// <summary>11 §9.1 warmth dynamics over <paramref name="dtH"/> hours: C ≥ 18 or within 3 m of a fire → +10/h toward 100;
    /// 14 ≤ C &lt; 18 → toward 80 at ±6/h; C &lt; 14 → −(14 − C)/h (children and elders ×1.25).</summary>
    public static float StepWarmth(float warmth, float c, bool nearFire, bool vulnerable, float dtH)
    {
        if (c >= 18f || nearFire) { return MathF.Min(100f, warmth + (10f * dtH)); }
        if (c >= 14f) { return warmth < 80f ? MathF.Min(80f, warmth + (6f * dtH)) : MathF.Max(80f, warmth - (6f * dtH)); }
        return MathF.Max(0f, warmth - ((14f - c) * (vulnerable ? 1.25f : 1f) * dtH));
    }

    /// <summary>11 §9.3 precipitation wetting per hour before cloak and shelter.</summary>
    public static float SkyWetting(Sky sky) => sky switch { Sky.Drizzle => 8f, Sky.Rain => 20f, Sky.Storm => 35f, _ => 0f };

    /// <summary>11 §9.3 wetness over <paramref name="dtH"/>: rain gain × (1 − cloak rain resist) × (1 − rainBlock), sweat +6 when
    /// working heavy at C &gt; 24; out of the rain (rainBlock ≥ 0.9 counts as out) drying 4 + 0.4·max(0, T) + 0.8·wind, +20 within 3 m of a fire.</summary>
    public static float StepWetness(float wetness, in ExposureInputs x, float cloakRainResist, float c, float dtH)
    {
        var sky = SkyWetting(x.Sky);
        var inRain = sky > 0f && x.RainBlock < 0.9f;
        var gain = (inRain ? sky * (1f - cloakRainResist) * (1f - x.RainBlock) : 0f) + (x.Activity == ActivityLevel.Heavy && c > 24f ? 6f : 0f);
        var dry = inRain ? 0f : 4f + (0.4f * MathF.Max(0f, x.AirC)) + (0.8f * x.WindMs * (1f - x.WindBlock)) + (x.FireDistM <= 3f ? 20f : 0f);
        return Math.Clamp(wetness + ((gain - dry) * dtH), 0f, 100f);
    }

    /// <summary>11 §9.4 hypothermia over <paramref name="dtH"/>: Freezing (1–24) +(25 − W)/2.5 per hour; Exposed (0)
    /// +8 + 0.75·max(0, 14 − C); recovers −15/h once Warmth ≥ 40.</summary>
    public static float StepHypothermia(float hypothermia, float warmth, float c, float dtH)
    {
        var rate = warmth >= 40f ? -15f : warmth <= 0f ? 8f + (0.75f * MathF.Max(0f, 14f - c)) : warmth < 25f ? (25f - warmth) / 2.5f : 0f;
        return Math.Clamp(hypothermia + (rate * dtH), 0f, 100f);
    }

    /// <summary>11 §9.1 immersion: C = T_water − 6 + 0.2·Ins + activity heat (the §9.1 −20/h example is a soaked, clothed
    /// swimmer at moderate effort), and cold loss is ×3 (<see cref="ImmersionLossFactor"/>).</summary>
    public static float ImmersionC(float waterC, float insulation, ActivityLevel effort) => waterC - 6f + (0.2f * insulation) + ActivityHeat(effort);

    public const float ImmersionLossFactor = 3f;

    /// <summary>11 §3.2 sleep warmthF: ≥ 60 1.0 · 40–59 0.8 · 25–39 0.5 · &lt; 25 0.2.</summary>
    public static float SleepWarmthFactor(float warmth) => warmth >= 60f ? 1f : warmth >= 40f ? 0.8f : warmth >= 25f ? 0.5f : 0.2f;

    /// <summary>11 §9.4 warmth tier: 0 Comfortable · 1 Chilly · 2 Cold · 3 Freezing · 4 Exposed.</summary>
    public static int Tier(float warmth) => warmth >= 60f ? 0 : warmth >= 40f ? 1 : warmth >= 25f ? 2 : warmth > 0f ? 3 : 4;

    /// <summary>11 §2.1 satiety multiplier for cold (×1.25 Cold, ×1.4 Freezing or Exposed).</summary>
    public static float SatietyColdFactor(float warmth) => Tier(warmth) switch { 2 => 1.25f, >= 3 => 1.4f, _ => 1f };

    /// <summary>11 §2.1 energy multiplier for cold (×1.1 Cold or worse).</summary>
    public static float EnergyColdFactor(float warmth) => Tier(warmth) >= 2 ? 1.1f : 1f;
}

/// <summary>Per-item wear values cached by item handle (11 §9.2); zero for items that aren't worn.</summary>
public readonly record struct WearValues(float Ins, float WetRetention, float RainResist)
{
    public static WearValues[] For(ContentDatabase content)
    {
        var values = new WearValues[content.Items.Count];
        for (var i = 0; i < values.Length; i++)
        {
            if (content.Items[i].Wear is { } w) { values[i] = new WearValues(w.Ins, w.WetRetention, w.RainResist); }
        }

        return values;
    }
}
