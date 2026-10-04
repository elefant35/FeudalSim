using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// M1-01b: psychological needs (21 §5.2), emotion decay (§6.2) and mood (§6.4), per step with exact per-hour rates.
/// Inputs that later systems own are placeholders here, marked where used: activity state (sleep, interaction,
/// purposeful work — M1-02), the Comfort target from shelter/bed/clothing (11, 14; Landfall lean-to = 25), Safety
/// threats (18, 16) and Status standing (renown, office, wealth, reputation — 16, 17; profession prestige for now),
/// and timed thoughts (§6.4; emitted by the systems that author them).
/// </summary>
public sealed class PsychologySystem(float comfortTarget = PsychologySystem.LandfallComfortTarget) : ISimSystem
{
    /// <summary>21 §5.2 worked example: lean-to 8 + ground 0 + homeland clothes 10 + rations 5 + few possessions 2.</summary>
    public const float LandfallComfortTarget = 25f;

    private ContentDatabase? _cachedFor;
    private TraitTerms[] _terms = [];

    public string Name => "Psychology";
    public SimPhase Phase => SimPhase.Resolve;   // after NeedsDecay (registration order within the phase)

    public void Run(in StepContext ctx, SimWorld world)
    {
        var content = world.Content;
        if (!ReferenceEquals(_cachedFor, content)) { (_terms, _cachedFor) = (TraitTerms.Build(content), content); }
        var dtH = ctx.DtGameHours;
        var comfortK = 1f - SimMath.Pow(0.85f, dtH);   // 15 %/h toward the target
        var safetyUpK = 1f - SimMath.Pow(0.95f, dtH);  // 5 %/h when rising
        var safetyDownK = 1f - SimMath.Pow(0.80f, dtH);// 20 %/h when falling
        var statusK = 1f - SimMath.Pow(0.97f, dtH);    // 3 %/h
        var smoothK = 1f - SimMath.Exp(-dtH);          // EMA, τ = 1 game hour
        var people = world.People;
        for (var i = 0; i < people.Count; i++)
        {
            ref var n = ref people.Needs[i];
            ref var e = ref people.Emotions[i];
            ref var m = ref people.Mood[i];
            ref readonly var p = ref people.Personality[i];
            var t = Fold(p.Traits);
            float zSoc = Z(p.Sociability), zDil = Z(p.Diligence), zVol = Z(p.Volatility);

            // Decay needs: Social only while awake and not interacting; Purpose only while awake and not purposeful (21 §5.2).
            // Gains while interacting or working come from the ActivitySystem. Without a camp (M0), always decaying.
            ref readonly var act = ref people.Activity[i];
            var performing = world.Camp.Active != 0 && act.Phase == 1;
            var asleep = performing && act.Has(ActivityState.Asleep);
            if (!asleep && !(performing && act.Has(ActivityState.Interacting)))
            {
                n.Social = MathF.Max(0f, n.Social - (3.0f * (1f + (0.3f * zSoc)) * t.SocialDecay * dtH));
            }

            if (!asleep && !(performing && act.Has(ActivityState.Purposeful)))
            {
                n.Purpose = MathF.Max(0f, n.Purpose - (2.0f * (1f + (0.3f * zDil)) * t.PurposeDecay * dtH));
            }

            // Tracking needs.
            n.Comfort += (comfortTarget - n.Comfort) * comfortK;
            const float safetyTarget = 100f;   // placeholder: no threat terms until 16/18
            n.Safety += (safetyTarget - n.Safety) * (n.Safety < safetyTarget ? safetyUpK : safetyDownK);
            var standing = p.Profession < content.Professions.Count ? content.Professions[p.Profession].Prestige : 0f;   // placeholder for 16/17's standing
            var aspiration = 20f + (0.6f * p.Values.Status) + t.Aspiration;
            var statusTarget = 50f + (50f * SimMath.Tanh((standing - aspiration) / 25f));
            n.Status += (statusTarget - n.Status) * statusK;

            // Emotion decay: E · 2^(−Δ/HL), half-lives in game hours with facet and trait modifiers.
            var volHl = 1f + (0.15f * zVol);
            e.Anger *= Decay(dtH, 4f * volHl * t.HalfLife0);
            e.Fear *= Decay(dtH, 1f * volHl * t.HalfLife1);
            e.Grief *= Decay(dtH, 72f * t.HalfLife2);
            e.Joy *= Decay(dtH, 6f * t.HalfLife3);
            e.Shame *= Decay(dtH, 24f * t.HalfLife4);
            e.Jealousy *= Decay(dtH, 24f * t.HalfLife5);
            e.UpdatedGameMs = ctx.GameMs;

            // Mood (21 §6.4); thoughts are added by the systems that emit them (none yet).
            m.Value = ComposeMood(n, e, zVol, t.MoodBaseline, thoughts: 0f);
            m.Smoothed += (m.Value - m.Smoothed) * smoothK;
        }
    }

    /// <summary>21 §6.4: baseline + Σ w·d(need) + Σ c·E + clamp(thoughts, ±40); negative terms × (1 + 0.1·z_Vol).</summary>
    public static float ComposeMood(in Needs n, in Emotions e, float zVol, float baseline, float thoughts)
    {
        var neg = 1f + (0.1f * zVol);
        float Term(float v) => v < 0f ? v * neg : v;
        var raw = Term(baseline)
            + Term(25f * D(n.Satiety)) + Term(25f * D(n.Hydration)) + Term(15f * D(n.Energy)) + Term(20f * D(n.Warmth))
            + Term(12f * D(n.Social)) + Term(12f * D(n.Comfort)) + Term(20f * D(n.Safety)) + Term(12f * D(n.Purpose)) + Term(10f * D(n.Status))
            + Term(-0.25f * e.Anger) + Term(-0.35f * e.Fear) + Term(-0.50f * e.Grief) + (0.40f * e.Joy) + Term(-0.30f * e.Shame) + Term(-0.25f * e.Jealousy)
            + Term(Math.Clamp(thoughts, -40f, 40f));
        return Math.Clamp(raw, -100f, 100f);
    }

    /// <summary>d(n) = +0.2 if n ≥ 80; 0 if 50 ≤ n &lt; 80; −((50 − n)/50)^1.5 below 50 (loss-weighted).</summary>
    public static float D(float need) => need >= 80f ? 0.2f : need >= 50f ? 0f : -SimMath.Pow((50f - need) / 50f, 1.5f);

    private static float Decay(float dtHours, float halfLifeHours) => SimMath.Exp(-dtHours * 0.6931472f / halfLifeHours);

    private static float Z(byte facet) => (facet - 50f) / 15f;

    private TraitTerms Fold(ulong traits)
    {
        var r = TraitTerms.Neutral;
        for (var bits = traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < _terms.Length) { r = r.Combine(_terms[h]); }
        }

        return r;
    }

    /// <summary>The trait effects this system reads, per trait handle (multipliers combine by product, shifts by sum).</summary>
    private readonly record struct TraitTerms(
        float SocialDecay, float PurposeDecay, float HalfLife0, float HalfLife1, float HalfLife2, float HalfLife3, float HalfLife4, float HalfLife5,
        float MoodBaseline, float Aspiration)
    {
        public static readonly TraitTerms Neutral = new(1, 1, 1, 1, 1, 1, 1, 1, 0, 0);

        public TraitTerms Combine(in TraitTerms o) => new(
            SocialDecay * o.SocialDecay, PurposeDecay * o.PurposeDecay,
            HalfLife0 * o.HalfLife0, HalfLife1 * o.HalfLife1, HalfLife2 * o.HalfLife2, HalfLife3 * o.HalfLife3, HalfLife4 * o.HalfLife4, HalfLife5 * o.HalfLife5,
            MoodBaseline + o.MoodBaseline, Aspiration + o.Aspiration);

        public static TraitTerms[] Build(ContentDatabase content) => [.. content.Traits.Select(def =>
        {
            var e = def.Effects;
            float Get(IReadOnlyDictionary<string, float>? map, string key) => map is not null && map.TryGetValue(key, out var v) ? v : 1f;
            return new TraitTerms(
                Get(e?.NeedDecay, "social"), Get(e?.NeedDecay, "purpose"),
                Get(e?.HalfLife, "anger"), Get(e?.HalfLife, "fear"), Get(e?.HalfLife, "grief"), Get(e?.HalfLife, "joy"), Get(e?.HalfLife, "shame"), Get(e?.HalfLife, "jealousy"),
                e?.MoodBaseline ?? 0f, e?.StatusAspiration ?? 0f);
        })];
    }
}
