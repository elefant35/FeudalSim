using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Decisions;

/// <summary>
/// Bounded influence (canon §13.4, 22 §6.3): skill and the listener's hard-coded susceptibility set how far acceptance and
/// concession options reach (the menu width); the classified words decide, in the policy, how much of that reach is granted.
/// <code>
/// s       = clamp(0.5 + 0.06·z_Warmth + Σ trait offsets + 0.002·Op + 0.002·(Trust − 50) − 0.003·Anger(at speaker), 0.05, 1)
/// Margin  = C_sys · s · (0.5 + 0.5·K_skill),  K_skill = Persuasion/100
/// L       = 0.5·L_words + 0.5·L_skill,  L_skill = clamp((Persuasion − 40)/60, −0.67, 1)
/// P(step k) ∝ exp(−(k − 3·clamp(L, 0, 1))² / 0.5),  k = 0..3;  shift_k = (k/3)·Margin
/// </code>
/// </summary>
public static class MenuWidth
{
    /// <summary>The canon default C_sys for prices, requests, promises, beliefs, stance, attraction, morale.</summary>
    public const float CDefault = 0.15f;

    /// <summary>Susceptibility s of <paramref name="listener"/> to <paramref name="speaker"/> (22 §6.3; trait offsets from content).</summary>
    public static float Susceptibility(SimWorld world, int listener, EntityId speaker)
    {
        var people = world.People;
        ref readonly var p = ref people.Personality[listener];
        var offsets = 0f;
        for (var bits = p.Traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < world.Content.Traits.Count) { offsets += world.Content.Traits[h].Effects?.Susceptibility ?? 0f; }
        }

        var me = people.Ids[listener];
        ref readonly var e = ref people.Emotions[listener];
        var anger = e.AngerTarget == speaker ? e.Anger : 0f;
        var s = 0.5f + (0.06f * DecisionNoise.Z(p.Warmth)) + offsets + (0.002f * world.Relationships.Opinion(me, speaker))
                + (0.002f * (world.Relationships.Trust(me, speaker) - 50f)) - (0.003f * anger);
        return Math.Clamp(s, 0.05f, 1f);
    }

    public static float Margin(float cSys, float susceptibility, float kSkill) => cSys * susceptibility * (0.5f + (0.5f * kSkill));

    public static float LSkill(float persuasion) => Math.Clamp((persuasion - 40f) / 60f, -0.67f, 1f);

    /// <summary>
    /// L_words from the fast decider's scores (22 §6.3): persuasiveness 1–7, hostility 1–5, politeness 1–5, and the appeal
    /// match (−1…+1). 0 for any score means "not classified" (a quick-intent chip, template mode): the neutral signal.
    /// </summary>
    public static float LWords(float persuasiveness, float appealMatch, float hostility, float politeness)
    {
        if (persuasiveness <= 0f && hostility <= 0f && politeness <= 0f) { return 0f; }
        var l = (persuasiveness > 0f ? (persuasiveness - 4f) / 3f : 0f) + (0.15f * appealMatch)
                - (hostility > 0f ? 0.20f * (hostility - 1f) / 4f : 0f) + (politeness > 0f ? 0.10f * (politeness - 3f) / 2f : 0f);
        return Math.Clamp(l, -1f, 1f);
    }

    /// <summary>The policy's distribution over steps none · ⅓ · ⅔ · full for a combined signal L (σ = 0.5 step).</summary>
    public static float[] Steps(float l)
    {
        var x = 3f * Math.Clamp(l, 0f, 1f);
        var p = new float[4];
        var sum = 0f;
        for (var k = 0; k < 4; k++) { p[k] = SimMath.Exp(-((k - x) * (k - x)) / 0.5f); sum += p[k]; }
        for (var k = 0; k < 4; k++) { p[k] /= sum; }
        return p;
    }

    /// <summary>
    /// A yes/no menu's yes propensity (22 §6.3): the expectation of clamp(A + shift_k, 0, 1) over the policy's steps for L ≥ 0;
    /// below 0 the words cost reach: A + max(L, −0.5)·Margin (16's G·Margin with G = clamp(L, −0.5, 1)).
    /// </summary>
    public static float Yes(float a, float margin, float l)
    {
        if (l < 0f) { return Math.Clamp(a + (MathF.Max(l, -0.5f) * margin), 0f, 1f); }
        var steps = Steps(l);
        var yes = 0f;
        for (var k = 0; k < 4; k++) { yes += steps[k] * Math.Clamp(a + (k / 3f * margin), 0f, 1f); }
        return yes;
    }

    /// <summary>The speaker's Persuasion skill (0–100).</summary>
    public static float Persuasion(SimWorld world, int speaker)
    {
        var h = world.Content.SkillHandle("skill.persuasion");
        return h < 0 ? 0f : world.People.SkillLevels(speaker)[h];
    }
}
