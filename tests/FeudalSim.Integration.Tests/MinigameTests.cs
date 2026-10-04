using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Crafting.Minigames;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Items;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-11a: the knapping primitives and their calibration (12 §6.5, 13 §7.2, §8.1, §13.3).</summary>
public sealed class MinigameTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly MinigameDef Shipped = Content.Minigame("minigame.knapping")!;

    [Fact]
    public void TheShippedCurve_GivesAnAttentivePlayerTheNpcLogistic_InEveryBand()   // 12 §6.5
    {
        for (var stage = 0; stage < 4; stage++)
        {
            for (var band = 0; band < Calibration.Bands; band++)
            {
                var ms = Calibration.Sample(BotPlayer.Attentive, stage, band, 1200, salt: 31).Select(x => Calibration.M(Shipped.Stages[stage], x.Band, x.Raw, x.Catastrophic)).OrderBy(x => x).ToArray();
                Calibration.Quantile(ms, 0.5).ShouldBe(0f, 0.08f, $"stage {stage} band {band}");
                Calibration.Quantile(ms, 0.25).ShouldBe(-0.35f, 0.12f, $"stage {stage} band {band}");
                Calibration.Quantile(ms, 0.75).ShouldBe(0.35f, 0.12f, $"stage {stage} band {band}");
            }
        }
    }

    [Fact]
    public void BetterHandsScoreHigher_InEveryStage()   // direction only: the proxies' bands are 13 Q11
    {
        for (var stage = 0; stage < 4; stage++)
        {
            var pra = Calibration.Quantile(Calibration.Ms(Shipped, BotPlayer.Practiced, stage, 400), 0.5);
            var att = Calibration.Quantile(Calibration.Ms(Shipped, BotPlayer.Attentive, stage, 400), 0.5);
            var nov = Calibration.Quantile(Calibration.Ms(Shipped, BotPlayer.Novice, stage, 400), 0.5);
            pra.ShouldBeGreaterThan(att);
            att.ShouldBeGreaterThan(nov);
        }
    }

    [Fact]
    public void M_IsMonotoneInRaw_AndACatastropheIsMinusOne()
    {
        var curve = Shipped.Stages[1];
        for (var band = 0; band < 5; band++)
        {
            var last = -2f;
            for (var raw = 0f; raw <= 1f; raw += 0.01f)
            {
                var m = Calibration.M(curve, band, raw, false);
                m.ShouldBeGreaterThanOrEqualTo(last);
                last = m;
            }
        }

        Calibration.M(curve, 2, 0.9f, catastrophic: true).ShouldBe(-1f);
    }

    [Fact]
    public void Overstriking_SnapsTheBlade()   // 13 §8.1 end-shock: a catastrophic input
    {
        var feel = new Feel(0.5f, 5f);
        var t = Knapping.Target(42, 7, 1, 0);
        Knapping.Hit(feel, t, new Strike(t.Point, t.AngleDeg, t.Force), 1f, 42, 7, 1, 0).Result.ShouldNotBe(FlakeResult.Snap);
        Knapping.Hit(feel, t, new Strike(t.Point, t.AngleDeg, t.Force + 0.6f), 1f, 42, 7, 1, 0).Result.ShouldBe(FlakeResult.Snap);
        Span<Strike> strikes = [new(t.Point, t.AngleDeg, t.Force + 0.6f)];
        Knapping.StrikeStage(feel, 0.8f, strikes, 42, 7, 1).Catastrophic.ShouldBeTrue();
    }

    [Fact]
    public void EverythingIsSeeded_PerProcessAndStage()
    {
        Knapping.Nodules(42, 7).ShouldBe(Knapping.Nodules(42, 7));
        Knapping.Nodules(42, 8).ShouldNotBe(Knapping.Nodules(42, 7));
        Knapping.Target(42, 7, 2, 3).ShouldBe(Knapping.Target(42, 7, 2, 3));
        Knapping.Platform(42, 7, 1).ShouldBe(Knapping.Platform(42, 7, 1));
        Knapping.TraceTarget(42, 7, 5).ShouldBe(Knapping.TraceTarget(42, 7, 5));
    }

    [Fact]
    public void ABotKnapsAKnife_ThroughTheWorkStageContract()
    {
        var w = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Player = [12f, -6f], Start = "Y0 Spring 2 10:00" })
            .CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        var me = w.PlayerId;
        w.Holdings.Give(me, Content.ItemHandle("item.hammerstone"), 1);
        w.Holdings.Give(me, Content.ItemHandle("item.antler_billet"), 1);
        w.Holdings.Give(me, Content.ItemHandle("item.pressure_flaker"), 1);
        w.Inventory.Add(me, Content.ItemHandle("item.flint_nodules"), 1, 60);
        var row = w.PlayerRow;
        var masonry = Content.SkillHandle("skill.masonry");
        w.People.SkillLevels(row)[masonry] = 30;
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, new StartProcess(me, "recipe.flint_knife")));
        w.Step();
        var p = w.Processes.Open.Single();
        var e = Sim.Skills.Skills.Effective(w, new Sim.Skills.CheckRequest(row, masonry, 20f, Sim.Skills.ToolTier.Stone, 50f, HasLight: true));
        var feel = Feel.For(e, 20f, Sim.Skills.Skills.Attribute(w, row, "dex"));
        var hand = new Rng(5);
        var predictability = Knapping.Nodules(w.WorldSeed, p.Id).Max();
        var resolved = new List<StageResolved>();
        for (var stage = 0; stage < 4; stage++)
        {
            while (w.Clock.GameMinute < p.BusyUntilMin) { w.Step(); }
            var (raw, cat) = BotPlayer.Attentive.Play(feel, stage, w.WorldSeed, p.Id, predictability, ref hand);
            var m = Calibration.M(Shipped.Stages[stage], feel.Band, raw, cat);
            w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, new WorkStage(p.Id, m, 40f)));
            var ev = w.Step().Events.Select(x => x.Payload).ToList();
            ev.OfType<CommandRejected>().ShouldBeEmpty();
            resolved.AddRange(ev.OfType<StageResolved>());
            if (ev.OfType<ProcessRuined>().Any()) { return; }   // a snap can still ruin a blade at this margin only if R < −30
        }

        var done = new List<ProcessCompleted>();
        for (var s = 0; s < 5000 && w.Processes.Count > 0; s++) { done.AddRange(w.Step().Events.Select(x => x.Payload).OfType<ProcessCompleted>()); }
        resolved.Select(r => r.M).ShouldAllBe(m => m >= -1f && m <= 1f);
        var ps = (0.10f * resolved[0].Ps) + (0.35f * resolved[1].Ps) + (0.30f * resolved[2].Ps) + (0.25f * resolved[3].Ps);
        done.Single().Q.ShouldBe(Quality.Process(ps, 60f, recipeMax: 80, flawCap: Quality.FlawCap(done.Single().Flaws, Content.Flaws)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
