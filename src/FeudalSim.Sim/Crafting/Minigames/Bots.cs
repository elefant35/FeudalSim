using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Crafting.Minigames;

/// <summary>
/// 13 §13.3 headless players: aim at the visible target with human hand error (absolute units, so skill's wider windows
/// make the same hand score better — which is why curves are fitted per grip band), scaled per session by a log-normal
/// "form" (people are inconsistent from one sitting to the next: σ 0.35; practiced hands are steadier, 0.15; novices
/// wobble, 0.45). Attentive defines the curve; practiced (×0.75 error, steadier) and novice (×1.25 error, overstrikes)
/// are the checks. Choosing a nodule is a noisy listen to each one's ring (EarSd).
/// </summary>
public readonly record struct BotPlayer(string Name, float PointSd, float AngleSd, float ForceSd, float ForceBias, float TraceSd, float EarSd, float FormSd)
{
    public static readonly BotPlayer Attentive = new("attentive", 0.05f, 7f, 0.09f, 0f, 0.05f, 0.20f, 0.35f);
    public static readonly BotPlayer Practiced = new("practiced", 0.0385f, 5.39f, 0.0693f, 0f, 0.0385f, 0.10f, 0.05f);
    public static readonly BotPlayer Novice = new("novice", 0.0625f, 8.75f, 0.1125f, 0.03f, 0.0625f, 0.35f, 0.45f);

    /// <summary>Plays one knapping stage and returns its raw score and whether a catastrophic input happened.</summary>
    public (float Raw, bool Catastrophic) Play(in Feel feel, int stage, ulong worldSeed, ulong process, float predictability, ref Rng hand)
    {
        var form = MathF.Exp(hand.Normal(0f, FormSd));   // this sitting's steadiness
        switch (stage)
        {
            case 0:
                // Tap each nodule and listen: the ring is heard with error; pick the best-sounding one.
                var nodules = Knapping.Nodules(worldSeed, process);
                var (pick, heard) = (0, float.NegativeInfinity);
                for (var k = 0; k < nodules.Length; k++)
                {
                    var ring = nodules[k] + hand.Normal(0f, EarSd * form);
                    if (ring > heard) { (pick, heard) = (k, ring); }
                }

                var mark = Knapping.Platform(worldSeed, process, pick) + hand.Normal(0f, PointSd * form);
                return (Knapping.ChooseScore(feel, nodules, pick, mark, worldSeed, process), false);
            case 1 or 2:
                Span<Strike> strikes = stackalloc Strike[Knapping.StrikesPerStage];
                for (var k = 0; k < strikes.Length; k++)
                {
                    var t = Knapping.Target(worldSeed, process, stage, k);
                    strikes[k] = new Strike(t.Point + hand.Normal(0f, PointSd * form), t.AngleDeg + hand.Normal(0f, AngleSd * form), t.Force + ForceBias + hand.Normal(0f, ForceSd * form));
                }

                return Knapping.StrikeStage(feel, predictability, strikes, worldSeed, process, stage);
            default:
                Span<float> path = stackalloc float[Knapping.TracePoints];
                for (var k = 0; k < path.Length; k++) { path[k] = Knapping.TraceTarget(worldSeed, process, k) + hand.Normal(0f, TraceSd * form); }
                return (Knapping.TraceStage(feel, path, worldSeed, process), false);
        }
    }
}
