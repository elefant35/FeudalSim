using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// M1-02a utility AI (21 §7): chooses an action from the content catalog, walks to its place and performs it.
/// <c>score = W × C × clamp(P × V, 0.4, 2.0) × E × S × M</c> (§7.2); selection takes the highest priority class with a
/// viable score, keeps the top band (≥ 75% of best) and picks by Gumbel-max over score/τ with noise fixed per
/// (agent, action, game hour); momentum keeps the current action unless beaten. Reconsiders when the activity ends,
/// every 8 game-min (LOD1 cadence, §7.6), or at once for a critical need. Value alignment V = 1 until actions carry
/// value tags; emotion biases follow §6.3 for the camp actions.
/// </summary>
public sealed class ActivitySystem : ISimSystem
{
    public const float WalkSpeedMetresPerSecond = 1.6f;   // canon §10.9
    public const long ReconsiderGameMs = 8 * 60 * 1000;
    public const float KIrr = 1.0f;                       // the Drama knob (canon §10.4)
    public const float ArriveM = 1.5f;

    private ContentDatabase? _cachedFor;
    private ActionDef[] _actions = [];
    private int[] _skillHandle = [];
    private (int From, int To, string Block)[] _schedule = [];
    private float[][] _traitUtility = [];   // [action][trait] multiplier from the action's utility keys

    /// <summary>Metrics (21 §19 task failure): activities started, and those that failed because a requirement ran
    /// out mid-task (e.g. the stores emptied during a meal). Interruptions by a more urgent need are not failures. Not state.</summary>
    public long Started { get; private set; }

    public long Abandoned { get; private set; }

    /// <summary>One scored candidate in a decision trace: the 21 §7.2 factors and the result.</summary>
    public readonly record struct TraceCandidate(short Action, byte Class, float Score, float W, float C, float PV, float E, float S, float M);

    /// <summary>The last decision for a row (21 §7.2 "trace: top-5 with factor breakdown"); preallocated, not state.</summary>
    public readonly record struct DecisionTrace(long GameMs, float Tau, short Chosen, bool Kept, int Count);

    private TraceCandidate[] _trace = [];
    private DecisionTrace[] _traceHead = [];
    public const int TraceTop = 5;

    /// <summary>The last decision trace for a row and its top candidates (best first).</summary>
    public (DecisionTrace Head, ReadOnlyMemory<TraceCandidate> Top) Trace(int row)
        => row < _traceHead.Length ? (_traceHead[row], _trace.AsMemory(row * TraceTop, _traceHead[row].Count)) : (default, ReadOnlyMemory<TraceCandidate>.Empty);

    public ActionDef ActionAt(int handle) => _actions[handle];

    public string Name => "Activity";
    public SimPhase Phase => SimPhase.Decide;

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (world.Camp.Active == 0) { return; }
        Cache(world.Content, world.Camp.Schedule);
        if (_actions.Length == 0) { return; }

        var people = world.People;
        var block = BlockAt(GameMinuteOfDay(ctx.GameMs));
        var socialCount = 0;
        for (var i = 0; i < people.Count; i++)
        {
            ref readonly var a = ref people.Activity[i];
            if (a.Phase == 1 && a.Has(ActivityState.Interacting)) { socialCount++; }
        }

        var due = world.Due;
        var playerRow = world.PlayerRow;
        for (var k = 0; k < due.Count; k++)
        {
            var i = due.Rows[k];
            if (i == playerRow) { continue; }   // the human chooses (21 §16)
            ref var act = ref people.Activity[i];
            if (!world.CanAct(i))
            {
                if (act.Action >= 0) { act = new ActivityState { Action = -1, Level = ActivityLevel.Rest }; }   // 11 §14: down — no action
                continue;
            }

            switch (people.Lod[i].Tier)
            {
                case LodTier.Lod3:
                    continue;   // Lod3System

                case LodTier.Lod2:
                    // 21 §15.5 hourly step: the past hour is spent on the current action (travel inside the camp takes
                    // under a minute, so arrival is immediate), then the same scorer picks the next hour's action.
                    Perform(ctx, world, i, socialCount, due.Dt(k), coarse: true);
                    Decide(ctx, world, i, block);
                    break;

                default:
                    if (act.Has(ActivityState.Conversing))
                    {
                        Perform(ctx, world, i, socialCount, due.Dt(k), coarse: false);   // held by the conversation (21 §14.4)
                        break;
                    }

                    var decide = act.Action < 0 || ctx.GameMs >= act.EndGameMs || ctx.GameMs >= act.NextDecideGameMs || CriticalElsewhere(people.Needs[i], act);
                    if (decide && !DoFavor(ctx, world, i)) { Decide(ctx, world, i, block); }
                    Perform(ctx, world, i, socialCount, due.Dt(k), coarse: false);
                    if (world.Favors.Count > 0) { CountFavor(world, i, due.Dt(k)); }
                    break;
            }
        }

        // The fire burns down whether or not anyone is there.
        world.Camp.AddStock("fire_fuel_min", -ctx.DtGameMs / 60000f);
    }

    private void Decide(in StepContext ctx, SimWorld world, int i, string block)
    {
        var people = world.People;
        ref var act = ref people.Activity[i];
        ref readonly var p = ref people.Personality[i];
        ref readonly var n = ref people.Needs[i];
        ref readonly var e = ref people.Emotions[i];
        var current = act.Action >= 0 && ctx.GameMs < act.EndGameMs ? act.Action : -1;

        Span<float> score = stackalloc float[_actions.Length];
        Span<int> cls = stackalloc int[_actions.Length];
        Span<Factors> factors = stackalloc Factors[_actions.Length];
        var bestRank = int.MaxValue;
        for (var k = 0; k < _actions.Length; k++)
        {
            score[k] = Score(world, i, k, block, current, out cls[k], out factors[k]);
            if (score[k] >= 0.15f && cls[k] < bestRank) { bestRank = cls[k]; }
        }

        if (bestRank == int.MaxValue) { return; }
        var best = 0f;
        for (var k = 0; k < _actions.Length; k++) { if (cls[k] == bestRank && score[k] > best) { best = score[k]; } }

        var tau = Decisions.DecisionNoise.Tau(p, people.Mood[i], e, world.Content, KIrr);
        var bucket = (ulong)(ctx.GameMs / (60 * 60 * 1000));
        var choice = -1;
        var bestKey = float.NegativeInfinity;
        for (var k = 0; k < _actions.Length; k++)
        {
            if (cls[k] != bestRank || score[k] < 0.75f * best) { continue; }
            var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Ai, people.Ids[i].Value, ((ulong)k << 40) | bucket, Salt.AiDecide));
            var gumbel = -SimMath.Log(-SimMath.Log(MathF.Max(1e-7f, rng.NextFloat01())));
            var key = (score[k] / best / tau) + gumbel;
            if (key > bestKey) { (bestKey, choice) = (key, k); }
        }

        var keep = current >= 0 && choice != current && cls[current] == cls[choice] && score[choice] < score[current];
        RecordTrace(people.Count, i, ctx.GameMs, tau, keep ? (short)current : (short)choice, keep, score, cls, factors);
        if (keep)
        {
            act.NextDecideGameMs = ctx.GameMs + ReconsiderGameMs;   // momentum: keep (score[current] includes M = 1.15)
            return;
        }

        if (choice == current)
        {
            act.NextDecideGameMs = ctx.GameMs + ReconsiderGameMs;
            return;
        }

        Start(ctx, world, i, choice, score[choice]);
    }

    /// <summary>
    /// An agreed favor (16 §5.4) is an obligation: while one has started, its task wins over routine (P2) unless a need is
    /// critical or the task can't be done. True if the favor's task is (now) the activity.
    /// </summary>
    private bool DoFavor(in StepContext ctx, SimWorld world, int i)
    {
        if (world.Favors.Count == 0 || world.Favors.ActiveFor(world.People.Ids[i], ctx.GameMinute) is not { } favor) { return false; }
        ref readonly var n = ref world.People.Needs[i];
        if (n.Satiety < 20f || n.Hydration < 20f || n.Energy < 12f || favor.Task < 0 || favor.Task >= _actions.Length) { return false; }
        var def = _actions[favor.Task];
        if (def.Requires is { } req) { foreach (var (stock, min) in req) { if (world.Camp.Stock(stock) < min) { return false; } } }
        ref var act = ref world.People.Activity[i];
        if (act.Action == favor.Task && ctx.GameMs < act.EndGameMs) { act.NextDecideGameMs = ctx.GameMs + ReconsiderGameMs; return true; }
        Start(ctx, world, i, favor.Task, 1f);
        return true;
    }

    private static void CountFavor(SimWorld world, int i, long dtMs)
    {
        ref readonly var act = ref world.People.Activity[i];
        if (act.Phase != 1 || world.Favors.ActiveFor(world.People.Ids[i], world.Clock.GameMinute) is not { } favor || favor.Task != act.Action) { return; }
        favor.RemainingMin -= dtMs / 60_000f;
        if (favor.RemainingMin > 0f) { return; }
        world.Favors.Remove(favor.Id);
        world.Emit(Events.Salience.Minor, favor.Doer, new Events.FavorDone(favor.Doer, favor.For, favor.Task));
    }

    private void Start(in StepContext ctx, SimWorld world, int i, int choice, float score)
    {
        var people = world.People;
        ref var act = ref people.Activity[i];
        Started++;
        var def = _actions[choice];
        var (tx, tz) = def.Place == PlaceKind.Home ? (people.Wander[i].HomeX, people.Wander[i].HomeZ) : world.Camp.Place(def.Place);
        var spread = (people.Ids[i].Value * 2654435761UL) % 628 / 100f;   // stand around the spot, not on it
        act = new ActivityState
        {
            Action = (short)choice, Phase = 0, Level = ActivityLevel.Light,
            Flags = (byte)((def.Activity == ActivityLevel.Sleep ? ActivityState.Asleep : 0) | (def.Social ? ActivityState.Interacting : 0) | (def.Purposeful ? ActivityState.Purposeful : 0)),
            StartedGameMs = ctx.GameMs, EndGameMs = long.MaxValue, NextDecideGameMs = ctx.GameMs + ReconsiderGameMs,
            Score = score, TargetX = tx + (1.5f * SimMath.Cos(spread)), TargetZ = tz + (1.5f * SimMath.Sin(spread)),
        };
    }

    /// <summary>
    /// 21 §14.4 stay utility for a conversing person: the best other action's score over the score of staying in
    /// <c>action.converse</c> (with momentum). Read-only. 1 when the content has no converse action.
    /// </summary>
    public float StayRatio(SimWorld world, int row)
    {
        var (best, stay, _) = StayScores(world, row);
        return stay <= 0f ? 10f : best / stay;
    }

    /// <summary>The handle of the best action other than conversing (the NPC's reason to leave), or −1.</summary>
    public int BestAlternative(SimWorld world, int row) => StayScores(world, row).Best;

    private (float BestScore, float Stay, int Best) StayScores(SimWorld world, int row)
    {
        Cache(world.Content, world.Camp.Schedule);
        var converse = Array.FindIndex(_actions, a => a.Id == "action.converse");
        if (converse < 0) { return (1f, 1f, -1); }
        var block = BlockAt(GameMinuteOfDay(world.Clock.GameMs));
        var stay = Score(world, row, converse, block, converse, out var stayClass, out _);
        float best = 0f;
        var bestK = -1;
        for (var k = 0; k < _actions.Length; k++)
        {
            if (k == converse) { continue; }
            var s = Score(world, row, k, block, converse, out var cls, out _);
            if (cls < stayClass) { s *= 4f; }   // a more urgent class (a critical need) dominates staying
            if (s > best) { (best, bestK) = (s, k); }
        }

        return (best, stay, bestK);
    }

    private readonly record struct Factors(float W, float C, float PV, float E, float S, float M);

    private void RecordTrace(int rows, int i, long gameMs, float tau, short chosen, bool kept, Span<float> score, Span<int> cls, Span<Factors> f)
    {
        if (_traceHead.Length < rows)
        {
            Array.Resize(ref _traceHead, Math.Max(rows, _traceHead.Length * 2));
            Array.Resize(ref _trace, _traceHead.Length * TraceTop);
        }

        // Top-5 by score, without allocating: repeated selection over the small action set.
        Span<bool> used = stackalloc bool[score.Length];
        var n = 0;
        for (; n < TraceTop; n++)
        {
            var best = -1;
            for (var k = 0; k < score.Length; k++) { if (!used[k] && (best < 0 || score[k] > score[best])) { best = k; } }
            if (best < 0 || score[best] <= 0f) { break; }
            used[best] = true;
            _trace[(i * TraceTop) + n] = new TraceCandidate((short)best, (byte)cls[best], score[best], f[best].W, f[best].C, f[best].PV, f[best].E, f[best].S, f[best].M);
        }

        _traceHead[i] = new DecisionTrace(gameMs, tau, chosen, kept, n);
    }

    /// <summary>21 §7.2 for one action; <paramref name="cls"/> is its priority class (P1 when it answers a critical need).</summary>
    private float Score(SimWorld world, int i, int k, string block, int current, out int cls, out Factors factors)
    {
        factors = default;
        var def = _actions[k];
        var people = world.People;
        ref readonly var n = ref people.Needs[i];
        ref readonly var p = ref people.Personality[i];
        ref readonly var e = ref people.Emotions[i];
        ref readonly var t = ref people.Transforms[i];
        cls = (int)def.Class;
        if (def.ChosenBySystem && k != current) { return 0f; }   // e.g. converse: started by a conversation, never by the scorer

        // Availability and satiation are hard gates (the store's ration too, 11 §10.5).
        if (def.Requires is { } req) { foreach (var (stock, min) in req) { if (world.Camp.Stock(stock) < min) { return 0f; } } }
        if (def.Consumes is { Stock: "food" } && Survival.Eating.StoreLeft(world, i) < 1f) { return 0f; }
        if (def.UntilNeed is { } until) { foreach (var (need, level) in until) { if (Need(n, need) >= level) { return 0f; } } }
        if (def.StartBelow is { } below && k != current && def.ScheduleBlock != block)
        {
            foreach (var (need, level) in below) { if (Need(n, need) >= level) { return 0f; } }
        }

        // W: need urgency (21 §5.1 curves) or base weight; a critical need lifts its satisfier to P1.
        var w = def.BaseWeight;
        if (def.WeightNeed is { } wn)
        {
            var u = Urgency(wn, Need(n, wn));
            w = wn == "social" ? 0.6f + (2f * u) : 1f + (4f * u);
            if (IsCritical(wn, Need(n, wn))) { cls = (int)PriorityClass.P1; w = MathF.Max(w, 4f); }
        }

        // C: compensated considerations — distance falloff and stock pressure.
        var (px, pz) = def.Place == PlaceKind.Home ? (people.Wander[i].HomeX, people.Wander[i].HomeZ) : world.Camp.Place(def.Place);
        var dist = MathF.Sqrt(((px - t.X) * (px - t.X)) + ((pz - t.Z) * (pz - t.Z)));
        Span<float> c = stackalloc float[2];
        var nc = 0;
        c[nc++] = Math.Clamp(1f - (0.004f * dist), 0.3f, 1f);
        if (def.StockPressure is { } sp) { c[nc++] = Math.Clamp(1f - (world.Camp.Stock(sp.Stock) / sp.Comfortable), 0.20f, 1f); }
        var cProduct = 1f;
        for (var j = 0; j < nc; j++) { cProduct *= c[j] + ((1f - c[j]) * (1f - (1f / nc)) * c[j]); }

        // P × V: facet multipliers (1 + k·z, clamp 0.25–2) and trait utility multipliers.
        var pv = 1f;
        if (def.FacetK is { } fk)
        {
            foreach (var (facet, kk) in fk) { pv *= Math.Clamp(1f + (kk * Z(Facet(p, facet))), 0.25f, 2f); }
        }

        for (var bits = p.Traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < _traitUtility[k].Length) { pv *= _traitUtility[k][h]; }
        }

        pv = Math.Clamp(pv, 0.4f, 2f);

        // E: emotion biases (21 §6.3) for the camp actions.
        var em = 1f;
        if (def.Social) { em *= (1f + (1.0f * e.Joy / 100f)) * (1f - (0.8f * e.Shame / 100f)); }
        if (def.Purposeful) { em *= 1f - (0.5f * e.Grief / 100f); }
        if (def.Place == PlaceKind.Home) { em *= 1f + (1.5f * e.Grief / 100f); }   // withdraw

        // S: schedule fit, amplified by Diligence.
        var fit = def.ScheduleBlock == "any" ? 0f : def.ScheduleBlock == block ? 1f : -0.5f;
        var s = Math.Clamp(1f + (0.35f * (1f + (0.3f * Z(p.Diligence))) * fit), 0.5f, 1.5f);

        // M: momentum.
        var m = k == current ? 1.15f : 1f;
        factors = new Factors(w, cProduct, pv, em, s, m);
        return w * cProduct * pv * em * s * m;
    }

    private void Perform(in StepContext ctx, SimWorld world, int i, int socialCount, long dtGameMs, bool coarse)
    {
        var people = world.People;
        ref var act = ref people.Activity[i];
        if (act.Action < 0) { return; }
        var def = _actions[act.Action];
        ref var t = ref people.Transforms[i];
        var embodied = people.Lod[i].Tier == LodTier.Lod0;

        if (act.Phase == 0)
        {
            var dx = act.TargetX - t.X;
            var dz = act.TargetZ - t.Z;
            var remaining = MathF.Sqrt((dx * dx) + (dz * dz));
            ref var w = ref people.Wander[i];
            (w.TargetX, w.TargetZ, w.HasTarget) = (act.TargetX, act.TargetZ, !coarse);   // LOD0 bodies walk here (ADR-0007)
            if (coarse) { (t.X, t.Z, remaining) = (act.TargetX, act.TargetZ, 0f); }
            if (remaining > ArriveM)
            {
                if (!embodied)
                {
                    var step = MathF.Min(remaining, WalkSpeedMetresPerSecond * ctx.DtEmbodiedSeconds);
                    t.X += dx / remaining * step;
                    t.Z += dz / remaining * step;
                    t.Yaw = SimMath.Atan2(dx, -dz);
                }

                return;
            }

            w.HasTarget = false;
            act.Phase = 1;
            act.Level = def.Activity;
            act.StartedGameMs = ctx.GameMs;
            act.EndGameMs = ctx.GameMs + (def.DurationMin * 60_000L);
        }

        var dtH = dtGameMs / (float)Time.SimClock.MsPerGameHour;
        ref var n = ref people.Needs[i];
        if (def.NeedPerHour is { } gains)
        {
            foreach (var (need, perHour) in gains)
            {
                var rate = perHour;
                if (need == "energy" && def.Activity == ActivityLevel.Sleep) { rate *= world.Camp.Bedding * Survival.Exposure.SleepWarmthFactor(n.Warmth); }   // 11 §3.2
                if (def.Social && socialCount < 2 && !act.Has(ActivityState.Conversing)) { rate *= 0.25f; }   // nobody else to talk to
                var gain = MathF.Min(rate * dtH, 100f - Need(n, need));
                if (def.Consumes is { } cons && gain > 0f)
                {
                    gain = MathF.Min(gain, world.Camp.Stock(cons.Stock) / cons.Ratio);
                    if (cons.Stock == "food") { gain = MathF.Min(gain, Survival.Eating.StoreLeft(world, i)); }   // 11 §10.5 rations
                    world.Camp.AddStock(cons.Stock, -gain * cons.Ratio);
                    if (cons.Stock == "food" && gain > 0f)
                    {
                        Survival.Eating.Record(world, i, 0.6f * gain, 0.4f * gain, 0f);   // 11 §10.3: provisions (biscuit, salt pork)
                        Survival.Eating.CountStore(world, i, gain);
                    }
                }

                SetNeed(ref n, need, Need(n, need) + MathF.Max(0f, gain));
                if (need == "hydration" && def.Place == PlaceKind.Water && gain > 0f) { WaterExposure(ctx, world, i, gain); }
            }
        }

        if (def.StockPerHour is { } stocks)
        {
            var yield = (def.Skill is null ? 1f : 0.6f + (0.8f * people.SkillLevels(i)[_skillHandle[act.Action]] / 100f)) * Survival.Fitness.WorkMult(world, i);   // M2-07b-ii
            foreach (var (stock, perHour) in stocks) { world.Camp.AddStock(stock, perHour * dtH * (perHour > 0f ? yield : 1f)); }
        }

        // 12 §5.2: learn by doing — 10 XP per labour-hour of the action's skill (camp work at difficulty 20), for every actor alike.
        if (def.Skill is not null && _skillHandle[act.Action] >= 0)
        {
            Skills.Skills.AwardXp(world, i, _skillHandle[act.Action], 10f * dtH, CampWorkDifficulty, Skills.Outcome.Success, dtH);
        }

        if (def.Purposeful)
        {
            var primary = PrimarySkillMatches(world, i, def);
            n.Purpose = MathF.Min(100f, n.Purpose + ((primary ? 10f : 6f) * dtH));   // 21 §5.2 purpose gains
        }

        var finished = false;
        if (def.UntilNeed is { } until)
        {
            finished = true;
            foreach (var (need, level) in until) { finished &= Need(n, need) >= level; }
        }

        if (def.Requires is { } req)
        {
            foreach (var (stock, min) in req)
            {
                if (world.Camp.Stock(stock) < min) { finished = true; Abandoned++; }
            }
        }
        if (def.Activity == ActivityLevel.Sleep && n.Warmth < 20f) { finished = true; }   // 11 §3.2: the cold wakes them
        if (def.Consumes is { Stock: "food" } && Survival.Eating.StoreLeft(world, i) < 0.01f) { finished = true; }   // the day's ration is eaten
        if (finished) { act.EndGameMs = ctx.GameMs; }
    }

    /// <summary>
    /// 11 §11.1: each 0.5 L drunk (+20 Hydration) from the camp's water carries Flux exposure c_src × 0.04 (§7.4's taint
    /// included); a settler who boils it (a pot, a lit fire, their Diligence — M2-07c) cuts that ×0.02. The boiling choice is
    /// keyed per person per game hour, so one drinking spell is boiled or not as a whole.
    /// </summary>
    private static void WaterExposure(in StepContext ctx, SimWorld world, int i, float hydrationGain)
    {
        var boiled = Survival.Water.Boils(world, i, (ulong)(ctx.GameMinute / 60));
        Survival.Water.Exposure(world, i, hydrationGain, Survival.Water.CampContamination(world), boiled, (ulong)ctx.Step);
    }

    /// <summary>The difficulty of routine camp work (gathering, tending the fire) for XP's difficulty factor (12 §5.2).</summary>
    public const float CampWorkDifficulty = 20f;

    private bool PrimarySkillMatches(SimWorld world, int i, ActionDef def)
    {
        var prof = world.People.Personality[i].Profession;
        return def.Skill is not null && prof < world.Content.Professions.Count && world.Content.Professions[prof].Primary.Contains(def.Skill);
    }

    private static bool CriticalElsewhere(in Needs n, in ActivityState act)
        => (n.Satiety < 20f || n.Hydration < 20f || n.Energy < 12f) && act.Phase == 1 && !act.Has(ActivityState.Asleep) && act.Level != ActivityLevel.Rest;

    /// <summary>21 §5.1 urgency: logistic on deprivation x = (100 − need)/100.</summary>
    public static float Urgency(string need, float value)
    {
        var (k, x0) = need switch
        {
            "satiety" => (12f, 0.60f),
            "hydration" => (14f, 0.55f),
            "energy" => (10f, 0.65f),
            "warmth" => (12f, 0.55f),
            _ => (10f, 0.60f),   // social, purpose: 21 §7.7's chat example implies u_social(38) = 0.55
        };
        var x = (100f - value) / 100f;
        return 1f / (1f + SimMath.Exp(-k * (x - x0)));
    }

    private static bool IsCritical(string need, float value) => need switch
    {
        "satiety" or "hydration" => value < 20f,
        "energy" => value < 12f,
        "warmth" => value < 25f,
        _ => false,
    };

    public static float Need(in Needs n, string id) => id switch
    {
        "satiety" => n.Satiety, "hydration" => n.Hydration, "energy" => n.Energy, "warmth" => n.Warmth,
        "social" => n.Social, "comfort" => n.Comfort, "safety" => n.Safety, "purpose" => n.Purpose, "status" => n.Status,
        _ => 0f,
    };

    private static void SetNeed(ref Needs n, string id, float v)
    {
        v = Math.Clamp(v, 0f, 100f);
        switch (id)
        {
            case "satiety": n.Satiety = v; break;
            case "hydration": n.Hydration = v; break;
            case "energy": n.Energy = v; break;
            case "warmth": n.Warmth = v; break;
            case "social": n.Social = v; break;
            case "comfort": n.Comfort = v; break;
            case "safety": n.Safety = v; break;
            case "purpose": n.Purpose = v; break;
            case "status": n.Status = v; break;
        }
    }

    private static byte Facet(in Personality p, string facet) => facet switch
    {
        "curiosity" => p.Curiosity, "diligence" => p.Diligence, "sociability" => p.Sociability, "warmth" => p.Warmth, _ => p.Volatility,
    };

    private static float Z(byte facet) => Math.Clamp((facet - 50f) / 15f, -2.5f, 2.5f);

    private static int GameMinuteOfDay(long gameMs) => (int)(gameMs / 60_000 % 1440);

    private string BlockAt(int minuteOfDay)
    {
        foreach (var (from, to, b) in _schedule)
        {
            if (from <= to ? minuteOfDay >= from && minuteOfDay < to : minuteOfDay >= from || minuteOfDay < to) { return b; }
        }

        return "any";
    }

    private void Cache(ContentDatabase content, ushort schedule)
    {
        if (ReferenceEquals(_cachedFor, content)) { return; }
        _cachedFor = content;
        _actions = [.. content.Actions];
        _skillHandle = [.. _actions.Select(a => a.Skill is null ? -1 : content.SkillHandle(a.Skill))];
        _schedule = schedule < content.Schedules.Count
            ? [.. content.Schedules[schedule].Blocks.Select(b => (Minutes(b.From), Minutes(b.To), b.Block))]
            : [];
        _traitUtility = [.. _actions.Select(a => content.Traits.Select(t =>
            (a.UtilityKeys ?? []).Aggregate(1f, (acc, key) => t.Effects?.Utility is { } u && u.TryGetValue(key, out var v) ? acc * v : acc)).ToArray())];

        static int Minutes(string hhmm) => (int.Parse(hhmm[..2], System.Globalization.CultureInfo.InvariantCulture) * 60) + int.Parse(hhmm[3..], System.Globalization.CultureInfo.InvariantCulture);
    }
}
