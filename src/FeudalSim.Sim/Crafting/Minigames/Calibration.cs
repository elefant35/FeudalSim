using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Crafting.Minigames;

/// <summary>
/// 13 §7.2 / §13.3 calibration: raw stage score → m ∈ [−1, +1], per grip band, by quantile matching — F is the attentive
/// player's raw-score CDF (interpolated through seven stored quantiles) and m = logit(F)/3, so an attentive player's m
/// has exactly the NPC logistic's shape (ε = 24·m ~ Logistic(0, 8): median 0, quartiles ±0.37, 12 §6.5). A
/// catastrophic input is m = −1.
/// </summary>
public static class Calibration
{
    public const int Bands = 5;

    public static readonly float[] Probs = [0.025f, 0.10f, 0.25f, 0.50f, 0.75f, 0.90f, 0.975f];

    public static float M(MinigameStageCurve curve, int band, float raw, bool catastrophic)
    {
        if (catastrophic) { return -1f; }
        var q = curve.Bands[Math.Clamp(band, 0, Bands - 1)];
        float f;
        if (raw <= q[0]) { f = Probs[0] * Math.Clamp((raw + 1f) / MathF.Max(1e-4f, q[0] + 1f), 0f, 1f); }   // raw −1 is a catastrophe
        else if (raw >= q[^1]) { f = Probs[^1] + ((1f - Probs[^1]) * (raw - q[^1]) / MathF.Max(1e-4f, 1f - q[^1])); }
        else
        {
            var k = 0;
            while (k < q.Count - 2 && raw > q[k + 1]) { k++; }
            var span = q[k + 1] - q[k];
            f = Probs[k] + ((Probs[k + 1] - Probs[k]) * (span <= 1e-6f ? 0.5f : (raw - q[k]) / span));
        }

        f = Math.Clamp(f, 0.0005f, 0.9995f);
        return Math.Clamp(MathF.Log(f / (1f - f)) / 3f, -1f, 1f);
    }

    /// <summary>Fits the knapping curves from the attentive bot: <paramref name="samples"/> plays per stage and band.</summary>
    public static MinigameDef FitKnapping(int samples = 4000)
    {
        var stages = new List<MinigameStageCurve>();
        string[] ids = ["choose", "rough", "thin", "pressure"];
        for (var stage = 0; stage < ids.Length; stage++)
        {
            var bands = new List<IReadOnlyList<float>>();
            for (var band = 0; band < Bands; band++)
            {
                // Catastrophic plays sit at the bottom (raw −1): they are part of the attentive population (m = −1).
                var raws = Sample(BotPlayer.Attentive, stage, band, samples).Select(x => x.Catastrophic ? -1f : x.Raw).OrderBy(x => x).ToArray();
                bands.Add([.. Probs.Select(p => Round(Quantile(raws, p)))]);
            }

            stages.Add(new MinigameStageCurve { Stage = ids[stage], Bands = bands });
        }

        return new MinigameDef { Id = "minigame.knapping", Recipe = "recipe.flint_knife", Stages = stages };
    }

    /// <summary>Bot plays of one stage across a grip band (g uniform in the band, DEX 3–8, fresh process seeds).</summary>
    public static IEnumerable<(float Raw, bool Catastrophic, int Band)> Sample(BotPlayer bot, int stage, int band, int samples, ulong salt = 0)
    {
        var hand = new Rng(SplitMix64.Mix(0xB07, (ulong)stage, (ulong)band, salt, (ulong)bot.Name.Length));
        for (var k = 0; k < samples; k++)
        {
            var g = (band + hand.NextFloat01()) / Bands;
            var feel = new Feel(Math.Clamp(g, 0.001f, 0.999f), hand.Uniform(3f, 8f));
            var process = SplitMix64.Mix(salt, (ulong)k, (ulong)stage, (ulong)band, 77);
            var (raw, cat) = bot.Play(feel, stage, 42, process, hand.Uniform(0.3f, 1f), ref hand);
            yield return (raw, cat, feel.Band);
        }
    }

    /// <summary>m for a bot across all bands of a stage under a fitted curve (the 13 §13.3 target check).</summary>
    public static float[] Ms(MinigameDef def, BotPlayer bot, int stage, int samplesPerBand, ulong salt = 1)
    {
        var curve = def.Stages[stage];
        var ms = new List<float>();
        for (var band = 0; band < Bands; band++)
        {
            foreach (var (raw, cat, b) in Sample(bot, stage, band, samplesPerBand, salt)) { ms.Add(M(curve, b, raw, cat)); }
        }

        ms.Sort();
        return [.. ms];
    }

    public static float Quantile(float[] sorted, double q) => sorted.Length == 0 ? 0f : sorted[(int)Math.Round(q * (sorted.Length - 1), MidpointRounding.AwayFromZero)];

    private static float Round(float x) => MathF.Round(x, 4, MidpointRounding.AwayFromZero);
}
