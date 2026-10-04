using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Crafting;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Items;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Skills;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-10: 13 §3–4, §7.2, §12–13 — the process model and the minigame contract, with the flint knife.</summary>
public sealed class CraftingTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp(ContentDatabase? content = null)
    {
        var w = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Player = [12f, -6f], Start = "Y0 Spring 2 10:00" })
            .CreateWorld(content ?? Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    private static void Kit(SimWorld w, EntityId who, bool optional = true, int nodules = 1, int masonry = 30)
    {
        w.Holdings.Give(who, Content.ItemHandle("item.hammerstone"), 1);
        if (optional)
        {
            w.Holdings.Give(who, Content.ItemHandle("item.antler_billet"), 1);
            w.Holdings.Give(who, Content.ItemHandle("item.pressure_flaker"), 1);
        }

        w.Inventory.Add(who, Content.ItemHandle("item.flint_nodules"), nodules, 50);
        w.People.SkillLevels(w.People.IndexOf(who))[Content.SkillHandle("skill.masonry")] = (byte)masonry;
    }

    private static List<object> Run(SimWorld w, CommandSource source, StateCommand c)
    {
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, source, c));
        return [.. w.Step().Events.Select(e => e.Payload)];
    }

    private static List<object> Until(SimWorld w, Func<bool> done, int maxSteps = 20_000)
    {
        var events = new List<object>();
        for (var s = 0; s < maxSteps && !done(); s++) { events.AddRange(w.Step().Events.Select(e => e.Payload)); }
        return events;
    }

    [Fact]
    public void TheMinigameContract_m0IsTheMedianNpc_AndPlus1IsPlus24()   // 13 §7.2, 12 §6.5
    {
        var w = Camp();
        var row = 3;
        var skill = Content.SkillHandle("skill.masonry");
        w.People.SkillLevels(row)[skill] = 30;
        var req = new CheckRequest(row, skill, 20f, ToolTier.Stone, 50f, HasLight: true);
        var e = Skills.Effective(w, req);
        var rng = new Rng(1);
        Skills.Resolve(w, req with { MinigameM = 0f }, ref rng).Margin.ShouldBe(e - 20f, 0.001f);
        Skills.Resolve(w, req with { MinigameM = 1f }, ref rng).Margin.ShouldBe(e - 20f + 24f, 0.001f);
        var draws = Enumerable.Range(0, 4001).Select(k => { var r = new Rng((ulong)k + 7); return Skills.Resolve(w, req, ref r).Margin; }).OrderBy(x => x).ToList();
        draws[2000].ShouldBe(e - 20f, 0.5f);   // the NPC median equals the attentive player's m = 0
    }

    [Fact]
    public void AFlintKnife_FromNoduleToTool_ThroughFourStages()   // 13 §8.1 through the process model
    {
        var w = Camp();
        var me = w.PlayerId;
        Kit(w, me);
        Run(w, CommandSource.Player, new StartProcess(me, "recipe.flint_knife")).OfType<ProcessStarted>().ShouldHaveSingleItem();
        var p = w.Processes.Open.Single();
        w.Inventory.Count(me, Content.ItemHandle("item.flint_nodules")).ShouldBe(0);   // consumed at start
        var stageEvents = new List<StageResolved>();
        for (var stage = 0; stage < 4; stage++)
        {
            Until(w, () => w.Clock.GameMinute >= p.BusyUntilMin);
            var ev = Run(w, CommandSource.Player, new WorkStage(p.Id, 0f, 30f));
            ev.OfType<CommandRejected>().ShouldBeEmpty();
            stageEvents.AddRange(ev.OfType<StageResolved>());
        }

        var done = Until(w, () => w.Processes.Count == 0).OfType<ProcessCompleted>().Single();
        stageEvents.Count.ShouldBe(4);
        stageEvents.ShouldAllBe(s => s.M == 0f);
        var ps = (0.10f * stageEvents[0].Ps) + (0.35f * stageEvents[1].Ps) + (0.30f * stageEvents[2].Ps) + (0.25f * stageEvents[3].Ps);
        done.Q.ShouldBe(Quality.Process(ps, 50f, recipeMax: Math.Min(80, 100)));   // D 20 → recipe cap min(100, 60 + 20)
        var knife = w.Inventory.Instance(done.Instance)!.Value;
        (knife.Item, knife.Maker, knife.Q).ShouldBe((Content.ItemHandle("item.flint_knife"), me.Value, (byte)done.Q));
        knife.MaxDurability.ShouldBe(Quality.MaxDurability(60, done.Q), 0.01f);
        w.Processes.Completions(me, Content.RecipeHandle("recipe.flint_knife")).ShouldBe(1);
    }

    [Fact]
    public void ToolsGateAndCap()
    {
        var w = Camp();
        var me = w.PlayerId;
        w.Inventory.Add(me, Content.ItemHandle("item.flint_nodules"), 1, 50);
        Run(w, CommandSource.Player, new StartProcess(me, "recipe.flint_knife")).OfType<CommandRejected>().Single().Reason.ShouldContain("tool.hammerstone");
        w.Holdings.Give(me, Content.ItemHandle("item.hammerstone"), 1);
        Run(w, CommandSource.Player, new StartProcess(me, "recipe.flint_knife")).OfType<ProcessStarted>().ShouldHaveSingleItem();
        w.Processes.Open.Single().Cap.ShouldBe(55);   // no billet or flaker: 13 §8.1
        Run(w, CommandSource.Player, new StartProcess(w.People.Ids[2], "recipe.flint_knife")).OfType<CommandRejected>().ShouldHaveSingleItem();   // only as yourself
    }

    [Fact]
    public void ACriticalFailureOnACatastrophicStage_RuinsTheBlade_IntoFlakes()   // 13 §5.2, §8.1
    {
        var w = Camp();
        var me = w.PlayerId;
        Kit(w, me, masonry: 0);
        Run(w, CommandSource.Player, new StartProcess(me, "recipe.flint_knife"));
        var p = w.Processes.Open.Single();
        Run(w, CommandSource.Player, new WorkStage(p.Id, 0f));
        Until(w, () => w.Clock.GameMinute >= p.BusyUntilMin);
        Run(w, CommandSource.Player, new WorkStage(p.Id, -1f)).OfType<ProcessRuined>().ShouldHaveSingleItem();   // R = E − 20 − 24 < −30
        w.Processes.Count.ShouldBe(0);
        w.Inventory.Count(me, Content.ItemHandle("item.flint_flake")).ShouldBe(3);
    }

    [Fact]
    public void Interrupted_Labor_IsHeld_AndPaidOnResume()   // 13 §3.1
    {
        var w = Camp();
        var npc = 5;
        var who = w.People.Ids[npc];
        Kit(w, who);
        Run(w, CommandSource.Scenario, new StartProcess(who, "recipe.flint_knife"));
        var p = w.Processes.Open.Single();
        Run(w, CommandSource.Scenario, new WorkStage(p.Id, float.NaN));
        Run(w, CommandSource.Scenario, new WorkStage(p.Id, float.NaN)).OfType<CommandRejected>().Single().Reason.ShouldContain("busy");
        w.People.Vitals[npc].Bruise = 100f;   // knocked down mid-work
        Sim.Systems.HealthSystem.Update(w, npc, 0f, w.Clock.GameMinute);
        Until(w, () => p.State == ProcessState.Held, 2000);
        p.LaborOwedMin.ShouldBeGreaterThan(0f);
        Run(w, CommandSource.Scenario, new WorkStage(p.Id, float.NaN)).OfType<CommandRejected>().ShouldHaveSingleItem();
        w.People.Vitals[npc] = Sim.Health.Vitals.Healthy;
        var owed = p.LaborOwedMin;
        Run(w, CommandSource.Scenario, new ResumeProcess(p.Id)).OfType<CommandRejected>().ShouldBeEmpty();
        ((double)(p.BusyUntilMin - w.Clock.GameMinute)).ShouldBe(MathF.Ceiling(owed), 1.0);
    }

    [Fact]
    public void PassiveStages_WaitRipenAndOverrun()   // 13 §4.2–4.3
    {
        var knife = Content.Recipes.Single(r => r.Id == "recipe.flint_knife");
        var cured = knife with
        {
            Id = "recipe.test_cured_knife",
            Stages = [.. knife.Stages.Take(2), new RecipeStage { Id = "cure", Kind = StageKind.Passive, DurationDays = new StageDays { Min = 1, Ideal = 2 }, OverrunQPerDay = 10, RuinAfterDays = 3 }, .. knife.Stages.Skip(2)],
        };
        var content = new ContentDatabase(Content.Skills, Content.Items, Content.Needs, Content.Hash, Content.Assets, Content.Audio, Content.Traits,
            Content.Cultures, Content.Professions, Content.Actions, Content.Schedules, Content.OpinionModifiers, Content.ClaimPredicates, Content.OverheardLines,
            Content.Decisions, Content.Lines, Content.WorldSpecs, Content.Flaws, [.. Content.Recipes, cured]);
        var w = Camp(content);
        var who = w.People.Ids[6];
        Kit(w, who, masonry: 60);
        Run(w, CommandSource.Scenario, new StartProcess(who, "recipe.test_cured_knife"));
        var p = w.Processes.Open.Single();
        for (var k = 0; k < 2; k++)
        {
            Until(w, () => w.Clock.GameMinute >= p.BusyUntilMin);
            Run(w, CommandSource.Scenario, new WorkStage(p.Id, float.NaN));
        }

        Until(w, () => p.State == ProcessState.Waiting);
        Run(w, CommandSource.Scenario, new WorkStage(p.Id, float.NaN)).OfType<CommandRejected>().Single().Reason.ShouldContain("waiting");
        Until(w, () => p.State == ProcessState.Ready, 40_000);
        Until(w, () => p.State == ProcessState.Overrun, 40_000);
        Until(w, () => p.OverrunQ >= 5f, 40_000);
        Run(w, CommandSource.Scenario, new WorkStage(p.Id, float.NaN)).OfType<StageResolved>().ShouldHaveSingleItem();   // taken up late
        Until(w, () => w.Clock.GameMinute >= p.BusyUntilMin);
        Run(w, CommandSource.Scenario, new WorkStage(p.Id, float.NaN));
        var done = Until(w, () => w.Processes.Count == 0).OfType<ProcessCompleted>().Single();
        done.Q.ShouldBeLessThan(80);   // the overrun penalty came off
    }

    [Fact]
    public void Batch_NeedsThreeCompletionsAndAMargin_ThenMakesTheItemsAsNpcDraws()   // 13 §12
    {
        var w = Camp();
        var who = w.People.Ids[7];
        var row = 7;
        Kit(w, who, nodules: 10, masonry: 70);
        var recipe = Content.RecipeHandle("recipe.flint_knife");
        Run(w, CommandSource.Scenario, new StartBatch(who, "recipe.flint_knife", 5)).OfType<CommandRejected>().Single().Reason.ShouldContain("3 completions");
        for (var k = 0; k < 3; k++) { w.Processes.Completed(who, recipe); }
        w.People.SkillLevels(row)[Content.SkillHandle("skill.masonry")] = 5;
        Run(w, CommandSource.Scenario, new StartBatch(who, "recipe.flint_knife", 5)).OfType<CommandRejected>().Single().Reason.ShouldContain("E − D");
        w.People.SkillLevels(row)[Content.SkillHandle("skill.masonry")] = 70;
        Run(w, CommandSource.Scenario, new StartBatch(who, "recipe.flint_knife", 5)).OfType<CommandRejected>().ShouldBeEmpty();
        var made = Until(w, () => w.Processes.Count == 0, 60_000).OfType<ProcessCompleted>().ToList();
        made.Count.ShouldBe(5);
        w.Inventory.Count(who, Content.ItemHandle("item.flint_knife")).ShouldBe(5);
        w.Inventory.Count(who, Content.ItemHandle("item.flint_nodules")).ShouldBe(5);
    }

    [Fact]
    public void AProcessMidWork_SurvivesSaveAndLoad()
    {
        var w = Camp();
        var who = w.People.Ids[8];
        Kit(w, who);
        Run(w, CommandSource.Scenario, new StartProcess(who, "recipe.flint_knife"));
        Run(w, CommandSource.Scenario, new WorkStage(w.Processes.Open.Single().Id, float.NaN));
        var image = SaveCodec.Capture(w);
        for (var s = 0; s < 600; s++) { w.Step(); }
        var restored = SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        restored.Processes.Count.ShouldBe(1);
        for (var s = 0; s < 600; s++) { restored.Step(); }
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(w));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
