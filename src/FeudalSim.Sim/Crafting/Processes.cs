using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Items;
using FeudalSim.Sim.Skills;

namespace FeudalSim.Sim.Crafting;

/// <summary>
/// 13 §3–4 and §12–13 (M2-10): the process model and the minigame contract. A recipe starts when its tools and inputs
/// are present (inputs consumed, material quality M their weighted mean Q); each scored stage is one 12 §6.3
/// <c>Resolve()</c> — the player's minigame supplies m ∈ [−1, +1] (ε = 24·m), everyone else (and any auto-resolved
/// stage) draws the logistic NPC noise, so a median player equals the median NPC. Stage outcomes: a critical failure on a
/// catastrophic stage ruins the work (salvage); a failure attaches the stage's flaw. Labor follows §3.1
/// (L_eff = L / WorkRate × (1.10 − 0.002·PS), ×1.5 masterwork; the player's real seconds × 0.8 already passed count
/// against it); tools wear per labor-hour. Passive and tend stages are timers with an ideal window and overrun
/// penalties. Completion makes the item with 13 §5.2's quality. Batch (§12) resolves whole items as NPC draws.
/// </summary>
public static class Processes
{
    public const int ShortcutCompletions = 3;   // Quick Work and Batch unlock after 3 completions (13 §12)

    public static ToolTier TierOf(TechTier tier) => tier switch
    {
        TechTier.T0 => ToolTier.Stone, TechTier.T1 => ToolTier.Copper, TechTier.T2 => ToolTier.Bronze, TechTier.T3 => ToolTier.Iron, _ => ToolTier.Steel,
    };

    /// <summary>The worker's best carried tool with a tag (highest tier, then Q, then lowest id), or Instance 0.</summary>
    public static (ulong Instance, ToolTier Tier, int Q) BestTool(SimWorld world, EntityId holder, string tag)
    {
        var best = (Instance: 0UL, Tier: ToolTier.None, Q: -1);
        foreach (var s in world.Inventory.Of(holder))
        {
            var def = world.Content.Items[s.Item];
            if (!def.HasTag(tag)) { continue; }
            var tier = TierOf(def.Tier);
            var q = s.Q;
            if (best.Instance == 0 || tier > best.Tier || (tier == best.Tier && q > best.Q)) { best = (s.Instance != 0 ? s.Instance : ulong.MaxValue, tier, q); }
        }

        return best;
    }

    /// <summary>Starts a recipe for <paramref name="worker"/> (row) with inputs from <paramref name="container"/>; returns why not, or null.</summary>
    public const byte NodeFelled = 2;

    public static string? Start(SimWorld world, int worker, int recipe, EntityId container, bool masterwork, out Process? process, int siteChunk = -1, int siteIndex = -1)
    {
        process = null;
        var people = world.People;
        var content = world.Content;
        if (recipe < 0 || recipe >= content.Recipes.Count) { return "unknown recipe"; }
        if (!world.CanAct(worker)) { return "the worker can't act"; }
        var def = content.Recipes[recipe];
        var who = people.Ids[worker];
        var cap = def.Cap;
        foreach (var t in def.Tools)
        {
            var tool = BestTool(world, who, t.Tag);
            if (tool.Instance == 0 && t.Required) { return $"needs a tool ({t.Tag})"; }
            if (tool.Instance == 0 && t.CapWithout is { } c) { cap = Math.Min(cap, c); }
        }

        byte siteSize = 0;
        if (def.Site is { } site)
        {
            if (SiteProblem(world, worker, site, siteChunk, siteIndex, out siteSize) is { } why) { return why; }
        }

        Span<int> chosen = stackalloc int[def.Inputs.Count];
        for (var k = 0; k < def.Inputs.Count; k++)
        {
            var input = def.Inputs[k];
            chosen[k] = input.Item is { } id ? content.ItemHandle(id) : FirstWithTag(world, container, input.Tag!, input.Qty);
            if (chosen[k] < 0 || world.Inventory.Count(container, chosen[k]) < input.Qty) { return $"needs {input.Qty} × {input.Item ?? input.Tag}"; }
        }

        var material = 0f;
        for (var k = 0; k < def.Inputs.Count; k++)
        {
            material += def.Inputs[k].Weight * InputQ(world, container, chosen[k]);
            world.Inventory.Remove(container, chosen[k], def.Inputs[k].Qty);
        }

        process = new Process
        {
            Id = world.Processes.NextId(), Recipe = recipe, Worker = who, Container = container, Stage = 0, State = ProcessState.Active,
            StagePs = new float[def.Stages.Count], Material = def.Inputs.Count == 0 ? 50f : material, Masterwork = masterwork,
            StartedMin = world.Clock.GameMinute, BusyUntilMin = world.Clock.GameMinute, Cap = cap,
            SiteChunk = def.Site is null ? -1 : siteChunk, SiteIndex = def.Site is null ? -1 : siteIndex, SiteSize = siteSize,
        };
        world.Processes.Add(process);
        world.Emit(Salience.Minor, who, new ProcessStarted(process.Id, who, recipe));
        return null;
    }

    /// <summary>Why the worker can't work this node (M2-12): it must exist in the attached world, be of the recipe's kind and
    /// size, still stand, be within 4 m, and not be someone else's open work.</summary>
    private static string? SiteProblem(SimWorld world, int worker, Content.RecipeSite site, int chunk, int index, out byte size)
    {
        size = 0;
        if (world.Map is not { } map) { return "no world here"; }
        var per = WorldGen.NodeScatter.ChunksPerSide(map.Grid);
        if (chunk < 0 || chunk >= per * per || index < 0) { return "no such node"; }
        var nodes = new List<WorldGen.ResourceNode>();
        WorldGen.NodeScatter.Chunk(map.Grid, map.AttemptSeed, world.NodeTable, chunk % per, chunk / per, nodes);
        world.NodeDeltas.Apply(chunk, nodes);
        if (index >= nodes.Count) { return "no such node"; }
        var node = nodes[index];
        if (world.Content.Nodes[node.Type].Kind != site.Kind) { return "not the right kind of thing"; }
        if (node.State != 0) { return "already worked"; }
        if (node.Size < site.MinSize) { return "too small"; }
        var (x, z) = WorldGen.NodeScatter.Position(map.Grid, chunk % per, chunk / per, node);
        ref readonly var t = ref world.People.Transforms[worker];
        if (((t.X - x) * (t.X - x)) + ((t.Z - z) * (t.Z - z)) > 4f * 4f) { return "out of reach"; }
        foreach (var other in world.Processes.Open) { if (other.SiteChunk == chunk && other.SiteIndex == index) { return "someone is already at it"; } }
        size = node.Size;
        return null;
    }

    private static int FirstWithTag(SimWorld world, EntityId container, string tag, int qty)
    {
        foreach (var s in world.Inventory.Of(container))
        {
            if (world.Content.Items[s.Item].HasTag(tag) && world.Inventory.Count(container, s.Item) >= qty) { return s.Item; }
        }

        return -1;
    }

    private static int InputQ(SimWorld world, EntityId container, int item)
    {
        var stack = world.Inventory.StackQ(container, item);
        if (stack >= 0) { return stack; }
        foreach (var s in world.Inventory.Of(container)) { if (s.Item == item) { return s.Q; } }
        return 50;
    }

    /// <summary>
    /// Works the process's next scored stage (taking up a ready passive stage first). <paramref name="minigameM"/> is the
    /// player's minigame result (null: auto-resolve as an NPC draw); <paramref name="realSeconds"/> is how long the player
    /// played, which the world clock already covered (13 §3.1). Returns why not, or null.
    /// </summary>
    public static string? Work(SimWorld world, Process p, float? minigameM, float realSeconds = 0f, long? startAt = null, float laborScale = 1f)
    {
        var now = startAt ?? world.Clock.GameMinute;
        var def = world.Content.Recipes[p.Recipe];
        var worker = world.People.IndexOf(p.Worker);
        if (worker < 0 || !world.CanAct(worker)) { return "the worker can't act"; }
        if (p.State == ProcessState.Held) { return "labor is owed: resume first"; }
        if (p.State == ProcessState.Waiting) { return "still waiting"; }
        if (p.State is ProcessState.Ready or ProcessState.Overrun) { (p.Stage, p.State) = (p.Stage + 1, ProcessState.Active); }
        if (p.State != ProcessState.Active || now < p.BusyUntilMin) { return "busy"; }
        if (p.Stage >= def.Stages.Count || !def.Stages[p.Stage].Scored) { return "no stage to work"; }

        var stage = def.Stages[p.Stage];
        var skill = world.Content.SkillHandle(def.Skill);
        var (_, tier, toolQ) = def.Tools.Count == 0 ? (0UL, ToolTier.None, 50) : BestTool(world, p.Worker, def.Tools[0].Tag);
        var difficulty = def.Difficulty + stage.DOffset + (p.Masterwork ? 15 : 0);
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Crafting, p.Id, (ulong)p.Stage, Salt.CraftStage));
        var res = Skills.Skills.Resolve(world, new CheckRequest(worker, skill, difficulty, tier == ToolTier.None ? ToolTier.Iron : tier, Math.Max(0, toolQ),
            HasLight: true, MinigameM: minigameM), ref rng);

        var toolFactor = stage.ToolFactor?.GetValueOrDefault(tier.ToString().ToLowerInvariant()) is { } tf and > 0f ? tf : 1f;   // 13 §3.1 TaskToolFactor
        var labor = stage.LaborMin * toolFactor / (res.WorkRate * Survival.Fitness.WorkMult(world, worker)) * (1.10f - (0.002f * res.PerformanceScore)) * (p.Masterwork ? 1.5f : 1f) * laborScale;
        if (def.Risk is { } risk) { Accident(world, p, worker, risk, labor / 60f, skill); }
        var remaining = MathF.Max(0f, labor - (0.8f * realSeconds));
        Wear(world, p, def, stage, labor);
        Skills.Skills.AwardXp(world, worker, skill, 10f * labor / 60f, difficulty, res.Outcome, labor / 60f);   // 12 §5.2: 10 XP per labor-hour
        world.Emit(Salience.Minor, p.Worker, new StageResolved(p.Id, p.Stage, (byte)res.Outcome, res.PerformanceScore, minigameM ?? float.NaN));

        if (res.Outcome == Outcome.CritFail && stage.Catastrophic)
        {
            Ruin(world, p, def);
            return null;
        }

        if (res.Outcome <= Outcome.Fail && stage.Flaw is { } flaw && world.Content.FlawHandle(flaw) is var fh and >= 0) { p.Flaws |= 1UL << fh; }
        p.StagePs[p.Stage] = res.Outcome == Outcome.CritFail ? 0f : res.PerformanceScore;
        p.LaborMin += labor;
        p.BusyUntilMin = now + (long)MathF.Ceiling(remaining);
        p.Stage++;
        return null;
    }

    /// <summary>
    /// 11 §12.4 work accidents: p per labor-hour = base (low 0.0005 · medium 0.0015 · high 0.004) × (1 + 1.5·(1 − skill/100))
    /// × fatigue (Exhausted ×2, Collapsing ×3) × weather (Storm ×1.5); severity 70 % Minor, 25 % Moderate, 5 % Severe.
    /// </summary>
    /// <summary>11 §12.4 accident chance per labor-hour.</summary>
    public static float AccidentPerHour(string riskClass, float skill, float energy, bool storm)
        => (riskClass switch { "high" => 0.004f, "medium" => 0.0015f, _ => 0.0005f }) * (1f + (1.5f * (1f - (skill / 100f))))
           * (energy < 12f ? 3f : energy < 25f ? 2f : 1f) * (storm ? 1.5f : 1f);

    private static void Accident(SimWorld world, Process p, int worker, Content.RecipeRisk risk, float hours, int skill)
    {
        var people = world.People;
        var level = skill >= 0 ? people.SkillLevels(worker)[skill] : 0;
        var perHour = AccidentPerHour(risk.Class, level, people.Needs[worker].Energy, world.WeatherRef.Sky == Climate.Sky.Storm);
        var chance = 1f - MathF.Pow(1f - MathF.Min(perHour, 0.99f), hours);
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Crafting, p.Id, (ulong)p.Stage, Salt.WorkAccident));
        if (!rng.Chance(chance)) { return; }
        var band = rng.NextFloat01();
        var severity = band < 0.70f ? rng.Uniform(8f, 19f) : band < 0.95f ? rng.Uniform(20f, 34f) : rng.Uniform(35f, 54f);
        var left = rng.Chance(0.5f);
        var region = risk.Region switch
        {
            "arm" => left ? Health.BodyRegion.ArmL : Health.BodyRegion.ArmR, "leg" => left ? Health.BodyRegion.LegL : Health.BodyRegion.LegR,
            "head" => Health.BodyRegion.Head, _ => Health.BodyRegion.Torso,
        };
        var damage = Enum.TryParse<Health.DamageType>(risk.Damage, true, out var d) ? d : Health.DamageType.Blunt;
        Health.HealthRules.Trauma(world, worker, severity, damage, region, Health.TraumaSource.Tool);
    }

    private static void Wear(SimWorld world, Process p, RecipeDef def, RecipeStage stage, float laborMin)
    {
        foreach (var t in def.Tools)
        {
            var (instance, _, _) = BestTool(world, p.Worker, t.Tag);
            if (instance is 0 or ulong.MaxValue || world.Inventory.Instance(instance) is not { } inst) { continue; }
            inst.Condition -= stage.Wear * laborMin / 60f;
            if (inst.Condition > 0f) { world.Inventory.Update(inst); continue; }
            world.Inventory.Destroy(p.Worker, instance);
            world.Emit(Salience.Minor, p.Worker, new ToolBroke(p.Worker, inst.Item, instance));
        }
    }

    /// <summary>Resumes a held process: the owed labor is paid from now (13 §3.1).</summary>
    public static string? Resume(SimWorld world, Process p)
    {
        if (p.State != ProcessState.Held) { return "not held"; }
        var worker = world.People.IndexOf(p.Worker);
        if (worker < 0 || !world.CanAct(worker)) { return "the worker can't act"; }
        (p.State, p.BusyUntilMin, p.LaborOwedMin) = (ProcessState.Active, world.Clock.GameMinute + (long)MathF.Ceiling(p.LaborOwedMin), 0f);
        return null;
    }

    /// <summary>The per-minute process clock: held on interruption, stages advance, passive timers, overrun, completion.</summary>
    internal static void Tick(SimWorld world, long now)
    {
        List<Process>? done = null;
        foreach (var p in world.Processes.Open)
        {
            var def = world.Content.Recipes[p.Recipe];
            var worker = world.People.IndexOf(p.Worker);
            switch (p.State)
            {
                case ProcessState.Active when now < p.BusyUntilMin:
                    if (worker < 0 || !world.CanAct(worker)) { (p.State, p.LaborOwedMin) = (ProcessState.Held, p.BusyUntilMin - now); }
                    break;
                case ProcessState.Active:
                    if (p.Stage >= def.Stages.Count) { (done ??= []).Add(p); }
                    else if (!def.Stages[p.Stage].Scored)
                    {
                        var d = def.Stages[p.Stage].DurationDays!;
                        (p.State, p.BusyUntilMin, p.IdealUntilMin) = (ProcessState.Waiting, now + (long)(d.Min * 1440f), now + (long)(d.Ideal * 1440f));
                    }

                    break;
                case ProcessState.Waiting when now >= p.BusyUntilMin:
                    p.State = ProcessState.Ready;
                    if (p.Stage == def.Stages.Count - 1) { p.Stage++; p.State = ProcessState.Active; }   // a final passive stage completes on its own
                    break;
                case ProcessState.Ready or ProcessState.Overrun when now > p.IdealUntilMin:
                    var stage = def.Stages[p.Stage];
                    var daysOver = (now - p.IdealUntilMin) / 1440f;
                    (p.State, p.OverrunQ) = (ProcessState.Overrun, daysOver * stage.OverrunQPerDay);
                    if (stage.RuinAfterDays is { } r && daysOver > r) { (done ??= []).Add(p); p.State = ProcessState.Ruined; }
                    break;
            }
        }

        if (done is null) { return; }
        foreach (var p in done)
        {
            if (p.State == ProcessState.Ruined) { Ruin(world, p, world.Content.Recipes[p.Recipe]); } else { Complete(world, p); }
        }
    }

    /// <summary>13 §5.2: PS_proc = Σ w·PS over scored stages; Q from material, caps and flaws; the item goes to the container.</summary>
    internal static void Complete(SimWorld world, Process p)
    {
        var content = world.Content;
        var def = content.Recipes[p.Recipe];
        var ps = 0f;
        for (var k = 0; k < def.Stages.Count; k++) { if (def.Stages[k].Scored) { ps += def.Stages[k].Weight * p.StagePs[k]; } }
        var q = Quality.Process(ps, p.Material, recipeMax: Math.Min(def.Cap, p.Cap), flawCap: Quality.FlawCap(p.Flaws, content.Flaws), masterworkAttempt: p.Masterwork);
        q = Math.Max(0, q - (int)MathF.Round(p.OverrunQ, MidpointRounding.AwayFromZero));
        var item = content.ItemHandle(def.Output.Item);
        var itemDef = content.Items[item];
        ulong instance = 0;
        var yield = def.Site is not null && def.SizeYield is { Count: 4 } sy ? sy[p.SiteSize] : 1;
        var output = p.Container;
        if (p.SiteChunk >= 0 && world.Map is { } map)   // M2-08: a site's products (logs, firewood) land on the ground there, not in the carry
        {
            var per = WorldGen.NodeScatter.ChunksPerSide(map.Grid);
            var nodes = new List<WorldGen.ResourceNode>();
            WorldGen.NodeScatter.Chunk(map.Grid, map.AttemptSeed, world.NodeTable, p.SiteChunk % per, p.SiteChunk / per, nodes);
            if (p.SiteIndex < nodes.Count)
            {
                var (sx, sz) = WorldGen.NodeScatter.Position(map.Grid, p.SiteChunk % per, p.SiteChunk / per, nodes[p.SiteIndex]);
                output = world.Piles.At(world, sx + 1.2f, sz);
            }
        }
        var qty = def.Output.Qty * yield;
        foreach (var by in def.Byproducts)
        {
            var byYield = def.Site is not null && def.ByproductSizeYield is { Count: 4 } by4 ? by4[p.SiteSize] : 1;
            if (by.Qty * byYield > 0) { world.Inventory.Add(output, content.ItemHandle(by.Item), by.Qty * byYield, 50); }
        }

        if (p.SiteChunk >= 0) { world.NodeDeltas.Set(p.SiteChunk, p.SiteIndex, NodeFelled); }   // the tree is down (20 §6.6 delta)
        if (itemDef.IsStackable) { if (qty > 0) { world.Inventory.Add(output, item, qty, q); } }
        else
        {
            var durability = Quality.MaxDurability(itemDef.Durability ?? 100, q) * DurabilityAfterFlaws(p.Flaws, content.Flaws);
            for (var k = 0; k < qty; k++)
            {
                instance = world.Inventory.Create(p.Container, item, q, durability, world.Ids.Next(EntityKind.ItemInstance).Value, p.Worker.Value,
                    world.Clock.GameMinute, (short)p.Recipe, p.Flaws);
            }
        }

        p.State = ProcessState.Complete;
        world.Processes.Remove(p.Id);
        world.Processes.Completed(p.Worker, p.Recipe);
        world.Emit(Salience.Notable, p.Worker, new ProcessCompleted(p.Id, p.Worker, item, q, instance, p.Flaws));
    }

    private static float DurabilityAfterFlaws(ulong flaws, IReadOnlyList<FlawDef> defs)
    {
        var mult = 1f;
        for (var f = 0; f < defs.Count && f < 64; f++)
        {
            if ((flaws & (1UL << f)) != 0 && defs[f].Effects?.GetValueOrDefault("durability") is { } d and not 0f) { mult *= 1f + d; }
        }

        return mult;
    }

    private static void Ruin(SimWorld world, Process p, RecipeDef def)
    {
        foreach (var s in def.SalvageOnRuin) { world.Inventory.Add(p.Container, world.Content.ItemHandle(s.Item), s.Qty, 10); }
        p.State = ProcessState.Ruined;
        world.Processes.Remove(p.Id);
        world.Emit(Salience.Notable, p.Worker, new ProcessRuined(p.Id, p.Worker, p.Stage));
    }

    /// <summary>
    /// 13 §12 Batch: n items, each stage an NPC draw, labor ×0.9 per item for n ≥ 5, one after another. Unlocked by 3
    /// completions and an effective margin E − D ≥ +10 (12's rule). Returns why not, or null.
    /// </summary>
    public static string? Batch(SimWorld world, int worker, int recipe, int n, EntityId container)
    {
        if (n < 1 || n > 50) { return "batch size is 1–50"; }
        var who = world.People.Ids[worker];
        if (world.Processes.Completions(who, recipe) < ShortcutCompletions) { return "batch needs 3 completions"; }
        var def = world.Content.Recipes[recipe];
        var skill = world.Content.SkillHandle(def.Skill);
        var (_, tier, toolQ) = def.Tools.Count == 0 ? (0UL, ToolTier.None, 50) : BestTool(world, who, def.Tools[0].Tag);
        var e = Skills.Skills.Effective(world, new CheckRequest(worker, skill, def.Difficulty, tier == ToolTier.None ? ToolTier.Iron : tier, Math.Max(0, toolQ), HasLight: true));
        if (e - def.Difficulty < 10f) { return "batch needs E − D ≥ +10"; }
        var cursor = world.Clock.GameMinute;
        for (var k = 0; k < n; k++)
        {
            if (Start(world, worker, recipe, container, masterwork: false, out var p) is { } why) { return k == 0 ? why : null; }
            while (p!.State == ProcessState.Active && p.Stage < def.Stages.Count && def.Stages[p.Stage].Scored)
            {
                if (Work(world, p, null, 0f, startAt: Math.Max(cursor, p.BusyUntilMin), laborScale: n >= 5 ? 0.9f : 1f) is not null) { break; }
                cursor = p.BusyUntilMin;
            }
        }

        return null;
    }
}
