using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Systems;

namespace FeudalSim.Integration.Tests;

/// <summary>16 §4.9a (owner direction, 2026-10-04): hours spent close together warm people to each other, unless animosity is brewing.</summary>
public sealed class TimeTogetherTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp()
    {
        var w = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")).CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        for (var i = 0; i < w.People.Count; i++) { (w.People.Transforms[i].X, w.People.Transforms[i].Z) = (1000f + (50f * i), 0f); }   // everyone apart
        return w;
    }

    private static float TimeMod(SimWorld w, int holder, int other)
    {
        var h = Content.OpinionModifierHandle("opinion.time_together");
        return w.Relationships.TryGet(w.People.Ids[holder], w.People.Ids[other], out var e) ? e.Mods.Where(m => m.Modifier == h).Sum(m => m.Value) : 0f;
    }

    [Fact]
    public void LongHoursClose_Warm_SaturatingAtTheCap_AndDistanceGivesNothing()
    {
        var w = Camp();
        (w.People.Transforms[1].X, w.People.Transforms[1].Z) = (w.People.Transforms[0].X + 3f, 0f);   // 3 m apart
        for (var h = 0; h < 40; h++) { SocialSystem.TimeTogether(w); }
        TimeMod(w, 0, 1).ShouldBeInRange(6f, 8f);   // saturating toward the cap of 8
        TimeMod(w, 1, 0).ShouldBeInRange(6f, 8f);
        TimeMod(w, 0, 2).ShouldBe(0f);   // 50 m away
    }

    [Fact]
    public void Animosity_BlocksIt_PerDirection()
    {
        var w = Camp();
        (w.People.Transforms[1].X, w.People.Transforms[1].Z) = (w.People.Transforms[0].X + 3f, 0f);
        w.Relationships.ApplyModifier(w.People.Ids[0], w.People.Ids[1], "opinion.insulted_me", 3f);   // 0 now dislikes 1
        w.Relationships.Opinion(w.People.Ids[0], w.People.Ids[1]).ShouldBeLessThan(0f);
        for (var h = 0; h < 10; h++) { SocialSystem.TimeTogether(w); }
        TimeMod(w, 0, 1).ShouldBe(0f);
        TimeMod(w, 1, 0).ShouldBeGreaterThan(0f);   // 1 holds nothing against 0

        w.People.Emotions[1].Anger = 60f;   // an angry person doesn't warm to anyone
        var before = TimeMod(w, 1, 0);
        (w.People.Transforms[2].X, w.People.Transforms[2].Z) = (w.People.Transforms[0].X - 3f, 0f);
        for (var h = 0; h < 10; h++) { SocialSystem.TimeTogether(w); }
        TimeMod(w, 1, 0).ShouldBe(before, 0.001f);
        TimeMod(w, 2, 0).ShouldBeGreaterThan(0f);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
