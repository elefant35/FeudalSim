using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Skills;

public enum Outcome : byte { CritFail, Fail, Success, CritSuccess }

/// <summary>12 §6.2 tool tiers (TierMod / speed). Iron is the reference.</summary>
public enum ToolTier : byte { None, Stone, Copper, Bronze, Iron, Steel }

/// <summary>What a check needs beyond the actor and skill (12 §6.1, M2-04 subset: tool, conditions the sim knows, minigame).</summary>
public readonly record struct CheckRequest(int Actor, int Skill, float Difficulty, ToolTier Tool = ToolTier.Iron, float ToolQuality = 50f, bool Outdoors = false,
    bool Raining = false, bool HasLight = false, bool Rushing = false, float? MinigameM = null);

public readonly record struct CheckResult(Outcome Outcome, float Margin, float PerformanceScore, float WorkRate, float EffectiveSkill);

/// <summary>
/// 12 §5–6, the capability model shared by the player and every NPC (tenet 2): attributes (potential + training + age),
/// the XP curve and XP per action (difficulty factor, outcome, aptitude, age, daily cap), rust, and <see cref="Resolve"/>
/// — effective skill, logistic noise (or the player's minigame), outcome bands, performance score and work rate.
/// Pure functions of sim state and the caller's RNG.
/// </summary>
public static class Skills
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<Content.SkillDef>, float[]> WeightCache = [];
    private static readonly string[] Keys = ["str", "end", "dex", "per", "int", "cha"];

    /// <summary>Attribute weights as [skill × 6] (str, end, dex, per, int, cha) — allocation-free reads on the hot path.</summary>
    public static float[] Weights(IReadOnlyList<Content.SkillDef> skills) => WeightCache.GetValue(skills, static list =>
    {
        var w = new float[list.Count * 6];
        for (var s = 0; s < list.Count; s++)
        {
            for (var k = 0; k < 6; k++) { w[(s * 6) + k] = list[s].Attributes?.GetValueOrDefault(Keys[k]) ?? 0f; }
        }

        return w;
    });

    public const float DailyCap = 150f, CapMultiplier = 0.25f;
    public const int RustGraceDays = 16, RustEveryDays = 8;

    /// <summary>12 §5.1: XP from level L to L+1.</summary>
    public static float XpRequired(int level) => 11f * MathF.Pow(1.055f, level);

    /// <summary>12 §5.1: cumulative XP from 0 to <paramref name="level"/>.</summary>
    public static float XpCumulative(int level) => 200f * (MathF.Pow(1.055f, level) - 1f);

    /// <summary>12 §3.2/§3.4: the attribute a check uses — potential + training + age, unrounded, clamped 1–10.</summary>
    public static float Attribute(SimWorld world, int row, string key)
    {
        var a = world.People.Attributes[row];
        var t = world.People.Training[row];
        var age = (int)((world.Clock.GameMinute - world.People.Core[row].BirthGameMinute) / Time.GameDate.MinutesPerYear);
        var (baseV, trained) = key switch
        {
            "str" => (a.Strength, t.Strength), "end" => (a.Endurance, t.Endurance), "dex" => (a.Dexterity, t.Dexterity),
            "per" => (a.Perception, t.Perception), "int" => (a.Intellect, t.Intellect), _ => (a.Charisma, t.Charisma),
        };
        return Math.Clamp(baseV + trained + AgeMod(key, age), 1f, 10f);
    }

    /// <summary>12 §3.4 AgeMod table.</summary>
    public static float AgeMod(string key, int age)
    {
        var band = age switch { < 10 => 0, < 14 => 1, < 16 => 2, < 30 => 3, < 45 => 4, < 55 => 5, < 65 => 6, < 75 => 7, _ => 8 };
        float[] row = key switch
        {
            "str" => [-4, -2, -1, 0, 0, -0.5f, -1, -2, -3],
            "end" => [-3, -1.5f, -0.5f, 0, 0, -0.5f, -1, -2, -3],
            "dex" => [-2, -1, 0, 0, 0, 0, -0.5f, -1, -2],
            "per" => [-1, 0, 0, 0, 0, 0, -0.5f, -1, -2],
            "int" => [-2, -1, 0, 0, 0, 0, 0, 0, -1],
            _ => [-1, -1, -0.5f, 0, 0.5f, 0.5f, 0.5f, 0.5f, 0],
        };
        return row[band];
    }

    /// <summary>12 §5.5 AgeMult for XP.</summary>
    public static float AgeMult(int age) => age switch { < 14 => 1.2f, < 16 => 1.25f, < 25 => 1.1f, < 40 => 1f, < 55 => 0.85f, < 65 => 0.7f, _ => 0.55f };

    /// <summary>12 §5.2 DF(Δ) = clamp(1 + Δ/30, 0.05, 1.6), Δ = D − (Level − Rust).</summary>
    public static float DifficultyFactor(float delta) => Math.Clamp(1f + (delta / 30f), 0.05f, 1.6f);

    public static float Level(SimWorld world, int row, int skill) => world.People.SkillLevels(row)[skill] + (world.People.SkillProgress(row)[skill].Xp / XpRequired(world.People.SkillLevels(row)[skill]));

    /// <summary>12 §6.2 effective skill (M2-04: level − rust, attributes, tool, the conditions the sim tracks).</summary>
    public static float Effective(SimWorld world, in CheckRequest r)
    {
        var people = world.People;
        var progress = people.SkillProgress(r.Actor)[r.Skill];
        float e = people.SkillLevels(r.Actor)[r.Skill] - progress.Rust;
        var weights = Weights(world.Content.Skills);
        for (var k = 0; k < 6; k++)
        {
            var w = weights[(r.Skill * 6) + k];
            if (w > 0f) { e += 3f * w * (Attribute(world, r.Actor, Keys[k]) - 5f); }
        }

        e += r.Tool switch { ToolTier.None => -10f, ToolTier.Stone => -8f, ToolTier.Copper => -5f, ToolTier.Bronze => -2f, ToolTier.Steel => 3f, _ => 0f };
        if (r.Tool != ToolTier.None) { e += Math.Clamp((r.ToolQuality - 50f) / 12.5f, -4f, 4f); }
        ref readonly var n = ref people.Needs[r.Actor];
        e += n.Energy < 15 ? -10f : n.Energy < 30 ? -5f : 0f;
        if (n.Satiety < 20) { e -= 5f; }
        if (n.Warmth < 30 && weights[(r.Skill * 6) + 2] > 0.25f) { e -= 5f; }
        var minute = world.Clock.GameMinute % 1440;
        var dark = minute is < 300 or >= 1260;
        if (dark) { e += r.HasLight ? -4f : -10f; }
        if (r.Outdoors && r.Raining) { e -= 5f; }
        var mood = people.Mood[r.Actor].Smoothed;
        e += mood <= -50 ? -3f : mood >= 50 ? 2f : 0f;
        if (r.Rushing) { e -= 8f; }
        return e;
    }

    /// <summary>
    /// 12 §6.3: R = (E − D) + ε, ε ~ Logistic(0, 8) clamped ±31 — or 24·m from the player's minigame; outcome bands
    /// CritFail &lt; −30 ≤ Fail &lt; −10 ≤ Success &lt; +25 ≤ CritSuccess; PS = clamp(50 + 1.25·R); work rate 12 §6.4.
    /// </summary>
    public static CheckResult Resolve(SimWorld world, in CheckRequest r, ref Rng rng)
    {
        var e = Effective(world, r);
        float eps;
        if (r.MinigameM is { } m) { eps = 24f * Math.Clamp(m, -1f, 1f); }
        else
        {
            var u = Math.Clamp(rng.NextFloat01(), 1e-6f, 1f - 1e-6f);
            eps = Math.Clamp(8f * MathF.Log(u / (1f - u)), -31f, 31f);
        }

        var margin = e - r.Difficulty + eps;
        var outcome = margin < -30 ? Outcome.CritFail : margin < -10 ? Outcome.Fail : margin < 25 ? Outcome.Success : Outcome.CritSuccess;
        var speed = r.Tool switch { ToolTier.Stone => 0.6f, ToolTier.Copper => 0.75f, ToolTier.Bronze => 0.85f, ToolTier.Steel => 1.1f, ToolTier.None => 0.5f, _ => 1f };
        var workRate = Math.Clamp(0.5f + (e / 100f), 0.4f, 1.6f) * speed * (r.Rushing ? 1.5f : 1f);
        return new CheckResult(outcome, margin, Math.Clamp(50f + (1.25f * margin), 0f, 100f), workRate, e);
    }

    /// <summary>
    /// 12 §5.2–5.3: awards XP for an action — BaseXP × DF × OutcomeF × Aptitude × AgeMult (× TeachMult), the 150/day cap
    /// (×0.25 beyond) — levels up, marks practice, clears rust by practice hours (12 §5.6) and trains the skill's attributes
    /// by load hours (12 §3.3). Returns the XP awarded.
    /// </summary>
    public static float AwardXp(SimWorld world, int row, int skill, float baseXp, float difficulty, Outcome outcome, float hours, float teachMult = 1f)
    {
        var people = world.People;
        ref var p = ref people.SkillProgress(row)[skill];
        var level = people.SkillLevels(row)[skill];
        var delta = difficulty - (level - p.Rust);
        if (outcome is Outcome.Fail or Outcome.CritFail && delta < -10f) { return 0f; }   // failing a trivial task teaches nothing
        var outcomeF = outcome switch { Outcome.Fail => 0.6f, Outcome.CritFail => 0.8f, Outcome.CritSuccess => 1.1f, _ => 1f };
        var age = (int)((world.Clock.GameMinute - people.Core[row].BirthGameMinute) / Time.GameDate.MinutesPerYear);
        var xp = baseXp * DifficultyFactor(delta) * outcomeF * (people.SkillAptitude(row)[skill] / 100f) * AgeMult(age) * teachMult;
        var day = (ushort)(world.Clock.GameMinute / 1440);
        if (p.Day != day) { (p.Day, p.DayXp) = (day, 0f); }
        var full = MathF.Max(0f, MathF.Min(xp, DailyCap - p.DayXp));
        xp = full + ((xp - full) * CapMultiplier);
        p.DayXp += xp;
        p.LastPracticeDay = day;
        p.Rust = (byte)Math.Max(0, p.Rust - (int)MathF.Floor(hours));   // each practice hour removes 1 Rust
        p.Xp += xp;
        var cap = age < 14 ? 25 : 100;   // children are capped at 25 (12 §5.5)
        while (level < cap && p.Xp >= XpRequired(level)) { p.Xp -= XpRequired(level); level++; }
        if (level >= cap) { p.Xp = 0f; }
        people.SkillLevels(row)[skill] = (byte)level;
        if (hours > 0f) { Train(world, row, skill, hours); }
        return xp;
    }

    /// <summary>12 §3.3: Training_A += 0.0048 × loadHours_A × (1 − Training_A / 2).</summary>
    private static void Train(SimWorld world, int row, int skill, float hours)
    {
        var weights = Weights(world.Content.Skills);
        ref var t = ref world.People.Training[row];
        Grow(ref t.Strength, weights[(skill * 6) + 0] * hours);
        Grow(ref t.Endurance, weights[(skill * 6) + 1] * hours);
        Grow(ref t.Dexterity, weights[(skill * 6) + 2] * hours);
        Grow(ref t.Perception, weights[(skill * 6) + 3] * hours);
        Grow(ref t.Intellect, weights[(skill * 6) + 4] * hours);
        Grow(ref t.Charisma, weights[(skill * 6) + 5] * hours);

        static void Grow(ref float value, float load) { if (load > 0f) { value += 0.0048f * load * (1 - (value / 2)); } }
    }

    /// <summary>12 §5.6 daily rust: after 16 days' grace, +1 per 8 days, capped at floor(0.15 × Level).</summary>
    public static void DailyRust(SimWorld world, int row) => DailyRust(world, row, (int)(world.Clock.GameMinute / 1440));

    public static void DailyRust(SimWorld world, int row, int today)
    {
        var people = world.People;
        var levels = people.SkillLevels(row);
        var progress = people.SkillProgress(row);
        for (var s = 0; s < levels.Length; s++)
        {
            if (levels[s] == 0) { continue; }
            ref var p = ref progress[s];
            if (p.RustDay == today) { continue; }
            p.RustDay = (ushort)today;
            var idle = today - p.LastPracticeDay;
            if (idle <= RustGraceDays || (idle - RustGraceDays) % RustEveryDays != 0) { continue; }
            p.Rust = (byte)Math.Min(p.Rust + 1, (int)MathF.Floor(0.15f * levels[s]));
        }
    }
}
