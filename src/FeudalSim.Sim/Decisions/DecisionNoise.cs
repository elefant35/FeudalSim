using FeudalSim.Sim.Content;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Decisions;

/// <summary>
/// Decision temperature τ (21 §8.2), shared by action selection (§7.2) and DP propensities (§7.8, where T = τ/τ0).
/// <c>kIrr</c> is the Drama knob (canon §10.4, default 1).
/// </summary>
public static class DecisionNoise
{
    public const float Tau0 = 0.08f;

    public static float Tau(in Personality p, in Mood mood, in Emotions e, ContentDatabase content, float kIrr = 1f)
    {
        var tau = Tau0 * kIrr * (1f + (0.35f * Z(p.Volatility)) - (0.25f * Z(p.Diligence)))
                  * (1f + (0.5f * MathF.Max(0f, -mood.Smoothed) / 100f))
                  * (1f + (0.5f * e.Fear / 100f));
        for (var bits = p.Traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < content.Traits.Count) { tau += content.Traits[h].Effects?.Tau ?? 0f; }
        }

        return Math.Clamp(tau, 0.02f, 0.30f);
    }

    public static float Z(byte facet) => Math.Clamp((facet - 50f) / 15f, -2.5f, 2.5f);
}
