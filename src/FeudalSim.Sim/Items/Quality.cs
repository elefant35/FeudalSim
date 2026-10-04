using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Items;

/// <summary>13 §5.1 grades.</summary>
public enum Grade : byte { Crude, Poor, Common, Fine, Superior, Masterwork }

/// <summary>
/// 13 §5 quality (M2-09): grades, what Q does (§5.6), the process-quality function with its ceilings (§5.2; the process
/// model that computes PS_proc from staged checks is M2-10's) and appraisal (§5.8). Pure functions.
/// </summary>
public static class Quality
{
    public static Grade GradeOf(int q) => q >= 90 ? Grade.Masterwork : q >= 75 ? Grade.Superior : q >= 60 ? Grade.Fine : q >= 40 ? Grade.Common : q >= 20 ? Grade.Poor : Grade.Crude;

    /// <summary>13 §5.6 StatMult(Q) = 0.85 + 0.003·Q (11 §9.1's clothing m(Q) is the same curve).</summary>
    public static float StatMult(int q) => 0.85f + (0.003f * q);

    /// <summary>13 §5.6 maxDur = baseDur × (0.5 + Q/100).</summary>
    public static float MaxDurability(float baseDurability, int q) => baseDurability * (0.5f + (q / 100f));

    /// <summary>15 §2.4 / 13 §5.6 value multiplier clamp(2^((Q − 50)/25), 0.25, 4).</summary>
    public static float ValueMult(int q) => Math.Clamp(MathF.Pow(2f, (q - 50) / 25f), 0.25f, 4f);

    /// <summary>13 §5.6 food preservation shelf-life multiplier 0.7 + 0.006·Q.</summary>
    public static float ShelfLifeMult(int q) => 0.7f + (0.006f * q);

    /// <summary>
    /// 13 §5.2: Q = clamp(round(PS_proc · Mat + PerkBonus), 0, Cap), Mat = 0.70 + 0.30·M/100, and
    /// Cap = min(30 + 0.7·M, recipe max, station cap, flaw caps, 89 unless a masterwork attempt, 50 if know-how was only Aware).
    /// Rounding is half away from zero (13's table: 42.5 → 43).
    /// </summary>
    public static int Process(float psProc, float material, int recipeMax = 100, int stationCap = 100, int flawCap = 100,
        bool masterworkAttempt = false, bool knowHowAware = false, float perkBonus = 0f)
    {
        var cap = Math.Min(Math.Min((int)MathF.Floor(30f + (0.7f * material)), recipeMax), Math.Min(stationCap, flawCap));
        if (!masterworkAttempt) { cap = Math.Min(cap, 89); }
        if (knowHowAware) { cap = Math.Min(cap, 50); }
        var raw = (psProc * (0.70f + (0.30f * material / 100f))) + perkBonus;
        return Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), 0, cap);
    }

    /// <summary>The lowest cap among an instance's flaws (100 if none).</summary>
    public static int FlawCap(ulong flaws, IReadOnlyList<Content.FlawDef> defs)
    {
        var cap = 100;
        for (var f = 0; f < defs.Count && f < 64; f++) { if ((flaws & (1UL << f)) != 0) { cap = Math.Min(cap, defs[f].Cap); } }
        return cap;
    }

    /// <summary>13 §5.8 appraisal spread σ_a = 2 + 13·(1 − s/100).</summary>
    public static float AppraisalSigma(float skill) => 2f + (13f * (1f - (Math.Clamp(skill, 0f, 100f) / 100f)));

    /// <summary>13 §5.8 perceived quality, seeded per (viewer, item) so re-inspection gives the same answer.</summary>
    public static int Appraise(ulong worldSeed, EntityId viewer, ulong itemKey, float skill, int q)
    {
        var rng = new Rng(SplitMix64.Mix(worldSeed, (ulong)RngStream.Economy, viewer.Value, itemKey, Salt.Appraisal));
        return Math.Clamp((int)Math.Round(q + rng.Normal(0f, AppraisalSigma(skill)), MidpointRounding.AwayFromZero), 0, 100);
    }

    /// <summary>13 §5.8 chance to see a flaw: 1 / (1 + exp(−(s − hiddenDifficulty + 10)/8)).</summary>
    public static float SeeFlawChance(float skill, int hiddenDifficulty) => 1f / (1f + MathF.Exp(-(skill - hiddenDifficulty + 10f) / 8f));

    /// <summary>Whether this viewer sees this flaw on this item (seeded per viewer, item and flaw: stable).</summary>
    public static bool SeesFlaw(ulong worldSeed, EntityId viewer, ulong itemKey, int flaw, float skill, int hiddenDifficulty)
    {
        var rng = new Rng(SplitMix64.Mix(worldSeed, (ulong)RngStream.Economy, viewer.Value, itemKey, ((ulong)(uint)flaw << 8) | Salt.Appraisal));
        return rng.Chance(SeeFlawChance(skill, hiddenDifficulty));
    }

    /// <summary>A stable appraisal key for a commodity stack (container and item; instances use their own id).</summary>
    public static ulong StackKey(EntityId container, int item) => SplitMix64.Mix(container.Value, (ulong)item, 0x57AC, 0, 0);
}
