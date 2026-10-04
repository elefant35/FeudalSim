using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Skills;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-04: 12 §5–6 — the XP curve, difficulty factor, Resolve()'s outcome table, the daily cap, rust, and learning by doing in the camp.</summary>
public sealed class SkillsTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp()
    {
        var w = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")).CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    [Fact]
    public void TheCurve_MatchesTwelveFiveOne()
    {
        Skills.XpRequired(10).ShouldBe(18.8f, 0.1f);
        Skills.XpRequired(50).ShouldBe(160.3f, 0.5f);
        Skills.XpCumulative(20).ShouldBe(383.6f, 1f);
        Skills.XpCumulative(80).ShouldBe(14295f, 10f);
        Skills.DifficultyFactor(-40).ShouldBe(0.05f);
        Skills.DifficultyFactor(0).ShouldBe(1f);
        Skills.DifficultyFactor(30).ShouldBe(1.6f);
    }

    [Fact]
    public void Resolve_FollowsTheOutcomeTable()
    {
        var w = Camp();
        var woodcutting = Content.SkillHandle("skill.woodcutting");
        var e = Skills.Effective(w, new CheckRequest(1, woodcutting, 0));
        foreach (var (delta, pSuccess, pCritFail) in new[] { (0f, 0.78, 0.02), (-20f, 0.22, 0.22), (10f, 0.92, 0.0) })
        {
            var rng = new Rng(SplitMix64.Mix(7, 1, (ulong)(delta + 100), 0, 0));
            int success = 0, critFail = 0;
            const int n = 20_000;
            for (var i = 0; i < n; i++)
            {
                var r = Skills.Resolve(w, new CheckRequest(1, woodcutting, e - delta), ref rng);
                if (r.Outcome >= Outcome.Success) { success++; }
                if (r.Outcome == Outcome.CritFail) { critFail++; }
            }

            (success / (double)n).ShouldBe(pSuccess, 0.025, $"E − D = {delta}");
            (critFail / (double)n).ShouldBe(pCritFail, 0.025, $"E − D = {delta}");
        }

        // The minigame replaces the noise: m = +1 adds 24.
        var noise = new Rng(1);
        Skills.Resolve(w, new CheckRequest(1, woodcutting, e, MinigameM: 1f), ref noise).Margin.ShouldBe(24f, 0.01f);
    }

    [Fact]
    public void Xp_HasADailyCap_LevelsUp_AndRustComesAndGoes()
    {
        var w = Camp();
        var s = Content.SkillHandle("skill.letters");
        var total = 0f;
        for (var i = 0; i < 40; i++) { total += Skills.AwardXp(w, 2, s, 10f, 30f, Outcome.Success, 1f); }
        total.ShouldBeLessThan(400f * 1.6f);                                  // 40 h at full rate would be ~640
        w.People.SkillProgress(2)[s].DayXp.ShouldBe(total, 0.01f);
        w.People.SkillLevels(2)[s].ShouldBeGreaterThan((byte)5);

        // Rust (12 §5.6) through DailyRust itself: 16 days' grace, then +1 on every 8th idle day, capped at 15% of the level.
        w.People.SkillLevels(2)[s] = 40;
        byte Day(int idle)
        {
            const int today = 200;   // a day with room behind it for the practice stamp
            ref var q = ref w.People.SkillProgress(2)[s];
            (q.RustDay, q.LastPracticeDay) = (0, (ushort)(today - idle));
            Skills.DailyRust(w, 2, today);
            return w.People.SkillProgress(2)[s].Rust;
        }

        w.People.SkillProgress(2)[s].Rust = 0;
        Day(16).ShouldBe((byte)0);    // still in grace
        Day(24).ShouldBe((byte)1);    // 16 + 8
        Day(25).ShouldBe((byte)1);    // not an accrual day
        w.People.SkillProgress(2)[s].Rust = 6;
        Day(32).ShouldBe((byte)6);    // capped at floor(0.15 × 40)
        Skills.AwardXp(w, 2, s, 10f, 45f, Outcome.Success, 4f);
        w.People.SkillProgress(2)[s].Rust.ShouldBe((byte)2);                   // 4 practice hours remove 4
        Skills.DailyRust(w, 2);
        Skills.DailyRust(w, 2);                                                // idempotent within a day
        w.People.SkillProgress(2)[s].Rust.ShouldBe((byte)2);
    }

    [Fact]
    public void CampWork_TeachesTheSkillItUses()
    {
        var w = Camp();
        var wood = Content.SkillHandle("skill.woodcutting");
        var food = Content.SkillHandle("skill.foraging");
        var before = Enumerable.Range(0, w.People.Count).Sum(i => w.People.SkillLevels(i)[wood] + w.People.SkillLevels(i)[food]);
        for (var i = 0; i < 4 * 18_000; i++) { w.Step(); }   // four game days
        var after = Enumerable.Range(0, w.People.Count).Sum(i => w.People.SkillLevels(i)[wood] + w.People.SkillLevels(i)[food]);
        after.ShouldBeGreaterThan(before);
        Enumerable.Range(0, w.People.Count).Max(i => w.People.Training[i].Strength).ShouldBeGreaterThan(0f);   // woodcutting trains strength
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
