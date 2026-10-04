using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Crafting.Minigames;

/// <summary>13 §7.2 feel parameters from the stage's grip g = 1/(1 + exp(−(E − D)/10)) and DEX.</summary>
public readonly record struct Feel(float Grip, float Dex)
{
    public static Feel For(float effective, float difficulty, float dex) => new(1f / (1f + MathF.Exp(-(effective - difficulty) / 10f)), dex);

    /// <summary>Tolerance width = ref × (0.6 + 0.9·g) × (0.8 + 0.04·DEX).</summary>
    public float Tolerance(float reference) => reference * (0.6f + (0.9f * Grip)) * (0.8f + (0.04f * Dex));

    /// <summary>Hand jitter σ = 8 % of the target size × (1 − 0.85·g).</summary>
    public float Jitter(float targetSize) => 0.08f * targetSize * (1f - (0.85f * Grip));

    /// <summary>The calibration band (five equal bands of g).</summary>
    public int Band => Math.Clamp((int)(Grip * 5f), 0, 4);
}

/// <summary>One strike's aim (13 §8.1 `strike`): a point along the edge (0–1), the platform angle (degrees) and the force (0–1).</summary>
public readonly record struct Strike(float Point, float AngleDeg, float Force);

/// <summary>A strike target, generated per stage and strike from the seed.</summary>
public readonly record struct StrikeTarget(float Point, float AngleDeg, float Force);

public enum FlakeResult : byte { Clean, StepHinge, Poor, Snap }

/// <summary>
/// 13 §8.1 flint knapping as headless primitives (M2-11a): choose a nodule (`inspect`), rough out and thin (`strike`),
/// pressure-flake (`trace`). Everything is seeded per (world, process, stage), so the client plays exactly what the sim
/// would replay; the stage's raw score (0–1) becomes m through the calibration curve (<see cref="Calibration"/>). A
/// strike far over its force is a catastrophic input (end-shock snap): m = −1, which ruins only if R &lt; −30 (13 §5.2).
/// </summary>
public static class Knapping
{
    public const int Candidates = 4;
    public const int StrikesPerStage = 6;
    public const int TracePoints = 24;

    // Reference tolerances (tuning knobs, 13 §18): fitted so the calibration targets of 13 §13.3 hold.
    public const float PointRef = 0.06f, AngleRef = 9f, ForceRef = 0.11f, TraceRef = 0.05f;

    public static Rng Seeded(ulong worldSeed, ulong process, int stage, int index)
        => new(SplitMix64.Mix(worldSeed, (ulong)RngStream.Crafting, process, ((ulong)(uint)stage << 16) | (uint)index, Salt.Minigame));

    /// <summary>The candidate nodules' predictability (0.3–1.0), seeded.</summary>
    public static float[] Nodules(ulong worldSeed, ulong process)
    {
        var rng = Seeded(worldSeed, process, 0, 0);
        var p = new float[Candidates];
        for (var k = 0; k < Candidates; k++) { p[k] = rng.Uniform(0.3f, 1.0f); }
        return p;
    }

    /// <summary>The chosen nodule's best striking platform (0–1 around its edge), seeded per nodule.</summary>
    public static float Platform(ulong worldSeed, ulong process, int nodule) => Seeded(worldSeed, process, 0, 10 + nodule).Uniform(0.1f, 0.9f);

    /// <summary>
    /// `inspect` raw score (13 §8.1): 0.3 for choosing well (the chosen nodule's predictability relative to the best on
    /// offer), 0.7 for marking its best platform (error in tolerance units). Predictability carries into the strikes.
    /// </summary>
    public static float ChooseScore(in Feel feel, float[] nodules, int chosen, float platformMark, ulong worldSeed, ulong process)
    {
        chosen = Math.Clamp(chosen, 0, nodules.Length - 1);
        var e = (platformMark - Platform(worldSeed, process, chosen)) / feel.Tolerance(PointRef);
        return (0.3f * nodules[chosen] / nodules.Max()) + (0.7f * MathF.Exp(-0.5f * e * e));
    }

    public static StrikeTarget Target(ulong worldSeed, ulong process, int stage, int strike)
    {
        var rng = Seeded(worldSeed, process, stage, 100 + strike);
        return new StrikeTarget(rng.Uniform(0.1f, 0.9f), 70f + rng.Normal(0f, 4f), rng.Uniform(stage == 1 ? 0.5f : 0.3f, stage == 1 ? 0.9f : 0.6f));
    }

    /// <summary>One strike: hand jitter on the point, then the error against the target in tolerance units.</summary>
    public static (FlakeResult Result, float Score) Hit(in Feel feel, in StrikeTarget t, in Strike s, float lipPenalty, ulong worldSeed, ulong process, int stage, int strike)
    {
        var rng = Seeded(worldSeed, process, stage, 200 + strike);
        var point = s.Point + rng.Normal(0f, feel.Jitter(1f));
        var tolP = feel.Tolerance(PointRef) * lipPenalty;
        var tolA = feel.Tolerance(AngleRef) * lipPenalty;
        var tolF = feel.Tolerance(ForceRef) * lipPenalty;
        if (s.Force > t.Force + (3f * tolF)) { return (FlakeResult.Snap, 0f); }   // end-shock: a catastrophic input
        var e = MathF.Sqrt(Sq((point - t.Point) / tolP) + Sq((s.AngleDeg - t.AngleDeg) / tolA) + Sq((s.Force - t.Force) / tolF));
        var score = MathF.Exp(-0.5f * e * e);   // smooth: every bit of accuracy counts
        return e <= 1f ? (FlakeResult.Clean, score) : e <= 2f ? (FlakeResult.StepHinge, score) : (FlakeResult.Poor, score);
    }

    /// <summary>A strike stage's raw score: the mean strike score × (0.85 + 0.15·predictability); a step or hinge leaves a lip (next tolerance ×0.85).</summary>
    public static (float Raw, bool Catastrophic) StrikeStage(in Feel feel, float predictability, ReadOnlySpan<Strike> strikes, ulong worldSeed, ulong process, int stage)
    {
        var total = 0f;
        var lip = 1f;
        for (var k = 0; k < strikes.Length; k++)
        {
            var (result, score) = Hit(feel, Target(worldSeed, process, stage, k), strikes[k], lip, worldSeed, process, stage, k);
            if (result == FlakeResult.Snap) { return (0f, true); }
            lip = result == FlakeResult.StepHinge ? 0.85f : 1f;
            total += score;
        }

        return (total / Math.Max(1, strikes.Length) * (0.85f + (0.15f * predictability)), false);
    }

    /// <summary>The pressure-flaking path: target offsets (serrations) along the edge.</summary>
    public static float TraceTarget(ulong worldSeed, ulong process, int k)
    {
        var phase = Seeded(worldSeed, process, 3, 0).Uniform(0f, 6.2832f);
        return 0.5f + (0.15f * MathF.Sin(phase + (k * 0.9f)));
    }

    /// <summary>`trace` raw score: 1 − mean |deviation| / (2 · tolerance), with hand jitter, clamped 0–1.</summary>
    public static float TraceStage(in Feel feel, ReadOnlySpan<float> path, ulong worldSeed, ulong process)
    {
        var tol = feel.Tolerance(TraceRef);
        var dev = 0f;
        var rng = Seeded(worldSeed, process, 3, 1);
        for (var k = 0; k < path.Length; k++) { dev += MathF.Abs(path[k] + rng.Normal(0f, feel.Jitter(0.3f)) - TraceTarget(worldSeed, process, k)); }
        return Math.Clamp(1f - (dev / Math.Max(1, path.Length) / (2f * tol)), 0f, 1f);
    }

    private static float Sq(float x) => x * x;
}
