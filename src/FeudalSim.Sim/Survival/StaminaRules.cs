namespace FeudalSim.Sim.Survival;

/// <summary>18 §2.3 and 11 §2.2/§3.1/§9.4 stamina numbers (M2-05b). Seconds are sim seconds (one step = 0.1 s).</summary>
public static class StaminaRules
{
    /// <summary>A fresh person's pool before the first update clamps it to their max.</summary>
    public const float Full = 100f;

    public const float RegenPerSecond = 15f;
    public const float RegenDelaySeconds = 1f;
    public const float SprintPerSecond = 8f;
    public const int WindedSteps = 15;          // 1.5 s (18 §2.3)
    public const int GaitHoldSteps = 3;         // a reported gait lasts 0.3 s unless the client reports again

    /// <summary>18 §2.3 Max = 50 + 5·End + 0.2·Athletics; ×0.75 Exhausted (Energy &lt; 25), ×0.85 Ravenous (Satiety &lt; 20).</summary>
    public static float Max(float endurance, float athletics, float energy, float satiety)
        => (50f + (5f * endurance) + (0.2f * athletics)) * (energy < 25f ? 0.75f : 1f) * (satiety < 20f ? 0.85f : 1f);

    /// <summary>15/s regen × Exhausted 0.75 × Cold-or-worse 0.7 × Hungry 0.9 (11 §2.2, §9.4; 18 §2.3).</summary>
    public static float Regen(float energy, float warmth, float satiety)
        => RegenPerSecond * (energy < 25f ? 0.75f : 1f) * (warmth < 40f ? 0.7f : 1f) * (satiety is >= 20f and < 40f ? 0.9f : 1f);
}
