using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.Time;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FeudalSim.Hosting;

/// <summary>A headless/test scenario (20 §13): seed, start time, length and initial population.</summary>
public sealed record ScenarioDef
{
    public string Id { get; init; } = "scenario.unnamed";
    public ulong Seed { get; init; } = 1;
    public string Start { get; init; } = "Y0 Spring 1 05:30";
    public int Days { get; init; } = 1;
    public int Settlers { get; init; } = 24;
    public int DayLengthMinutes { get; init; } = SimClock.DefaultDayLengthMinutes;

    /// <summary>If set, the first settler requests one AI line at this step (M0 end-to-end check).</summary>
    public long? AiPingStep { get; init; }

    /// <summary>Deadline for the AI ping, in steps (10 steps = 1 s).</summary>
    public int AiPingDeadlineSteps { get; init; } = 150;

    /// <summary>If set, the first settler faces a decision point at this step (M0 decision-point check, 22 §17.1).</summary>
    public long? DecisionPingStep { get; init; }

    /// <summary>Who may decide the ping DP: <c>policy</c> (inline), <c>fast</c> or <c>llm</c> (M0 routes llm to the fast decider).</summary>
    public string DecisionPingDecider { get; init; } = "llm";

    public int DecisionPingDeadlineSteps { get; init; } = Sim.Decisions.DecisionRulesEngine.ConversationDeadlineSteps;

    /// <summary>Behavior: <c>wander</c> (the M0 toy) or <c>utility</c> (M1 utility AI on a camp).</summary>
    public string Ai { get; init; } = "wander";

    /// <summary>The graybox camp for <c>ai: utility</c> (places in metres; stocks; sleep bedding; schedule id).</summary>
    public CampDef? Camp { get; init; }

    /// <summary>
    /// Optional player position <c>[x, z]</c> (metres): spawns the player's own character (a Person row flagged as the
    /// player, M1-04b) and sends a logged <c>PlayerMoved</c>, so headless runs can overhear and hold conversations.
    /// </summary>
    public float[]? Player { get; init; }

    public string PlayerName { get; init; } = "Tam";

    /// <summary>
    /// Optional tier mix for headless scale runs (S6): how many settlers, from the end of the roster, are pinned to LOD2
    /// and LOD3 with logged <c>SetLodTier</c> commands (the rest stay LOD1). Settlements and the relevance set that assign
    /// tiers in play arrive in M2–M4.
    /// </summary>
    public TierMix? Tiers { get; init; }

    /// <summary>Macro-step length for headless runs: <c>hour</c> or <c>day</c> (20 §5.4; for LOD3 populations). Default: fine steps.</summary>
    public string? MacroStep { get; init; }

    public long MacroStepGameMs => MacroStep?.ToLowerInvariant() switch
    {
        null or "" or "none" => 0,
        "hour" => SimClock.MsPerGameHour,
        "day" => SimClock.MsPerGameDay,
        var other => throw new FormatException($"macro_step must be hour or day, not '{other}'."),
    };

    public static ScenarioDef Load(string path)
    {
        var yaml = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        using var reader = new StreamReader(path);
        return yaml.Deserialize<ScenarioDef>(reader) ?? throw new InvalidDataException($"Empty scenario: {path}");
    }

    /// <summary>Parses <c>Y0 Spring 1 05:30</c> into game-ms since the epoch.</summary>
    public long StartGameMs()
    {
        var parts = Start.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !parts[0].StartsWith('Y')) { throw new FormatException($"Bad start time '{Start}'."); }
        var hm = parts[3].Split(':');
        var date = new GameDate(int.Parse(parts[0][1..], System.Globalization.CultureInfo.InvariantCulture),
            Enum.Parse<Season>(parts[1]), int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(hm[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(hm[1], System.Globalization.CultureInfo.InvariantCulture));
        return date.ToGameMinute() * SimClock.MsPerGameMinute;
    }

    /// <summary>Builds the world, registers the M0 systems and queues the spawn commands (all logged).</summary>
    public SimWorld CreateWorld(ContentDatabase content, IJobScheduler jobs, Action<CommandEnvelope>? log = null)
    {
        var utility = string.Equals(Ai, "utility", StringComparison.OrdinalIgnoreCase);
        var world = new SimWorld(Seed, StartGameMs(), DayLengthMinutes) { Content = content, Jobs = jobs }
            .AddSystem(new LodSystem());
        if (utility)
        {
            world.AddSystem(new Sim.Dialogue.ConversationSystem());   // Sense, after LOD: holds conversing NPCs before the AI decides
            world.Decisions.Register(new Sim.Dialogue.InitiativeOwner());
            world.Decisions.Register(new Sim.Social.EscalationOwner());
            world.Decisions.Register(new Sim.Social.BystanderOwner());
        }

        world
            .AddSystem(utility ? new ActivitySystem() : new WanderSystem())
            .AddSystem(new NeedsDecaySystem())
            .AddSystem(new PsychologySystem());
        if (utility)
        {
            world.Camp = (Camp ?? new CampDef()).ToRecord(content);
            world.AddSystem(new Lod3System());
            world.AddSystem(new SocialSystem());
            world.AddSystem(new InteractionSystem());
        }
        if (AiPingStep is { } at) { world.AddSystem(new AiPingSystem(at, AiPingDeadlineSteps)); }
        if (DecisionPingStep is { } dpAt)
        {
            world.Decisions.Register(new DecisionPingOwner());
            world.AddSystem(new DecisionPingSystem(dpAt, Enum.Parse<Sim.Decisions.DeciderKind>(DecisionPingDecider, ignoreCase: true), DecisionPingDeadlineSteps));
        }

        for (var i = 0; i < Settlers; i++)
        {
            var command = new CommandEnvelope(i + 1, 0, CommandSource.Scenario,
                new SpawnPerson($"Settler {i + 1}", i % 6 * 4f, i / 6 * -4f));
            world.Enqueue(command);
        }

        var seq = (long)Settlers;
        if (Player is [var px, var pz])
        {
            world.Enqueue(new CommandEnvelope(++seq, 0, CommandSource.Scenario, new SpawnPerson(PlayerName, px, pz, IsPlayer: true)));
            world.Enqueue(new CommandEnvelope(++seq, 0, CommandSource.Scenario, new PlayerMoved(px, pz, 0f)));
        }
        if (Tiers is { } mix)
        {
            // Spawns apply in step 1, so the tier pins go in with them (same step, later Seq): ids are 1..Settlers in order.
            var lod3From = Settlers - mix.Lod3;
            var lod2From = lod3From - mix.Lod2;
            for (var i = Math.Max(0, lod2From); i < Settlers; i++)
            {
                var id = Sim.Core.EntityId.Make(Sim.Core.EntityKind.Person, (ulong)(i + 1));
                world.Enqueue(new CommandEnvelope(++seq, 0, CommandSource.Scenario, new SetLodTier(id, i >= lod3From ? Sim.World.LodTier.Lod3 : Sim.World.LodTier.Lod2)));
            }
        }

        return world;
    }
}

/// <summary>Scenario <c>tiers:</c> — settlers pinned to the abstract tiers (S6).</summary>
public sealed record TierMix
{
    public int Lod2 { get; init; }
    public int Lod3 { get; init; }
}

/// <summary>The M1 graybox camp configuration (scenario YAML <c>camp:</c>).</summary>
public sealed record CampDef
{
    public float Food { get; init; } = 32_000f;          // ≈ 14 days for 24 settlers (95 Satiety each per day)
    public float Firewood { get; init; } = 10f;
    public float FireFuelMin { get; init; } = 240f;
    public float Bedding { get; init; } = 0.85f;         // bough bed (11 §3.2)
    public string Schedule { get; init; } = "schedule.landfall_communal";
    public Dictionary<string, float[]> Places { get; init; } = new()
    {
        ["fire"] = [10, -6], ["stores"] = [14, -2], ["shelter"] = [2, -16],
        ["water"] = [-25, 12], ["woods"] = [45, -35], ["forage_ground"] = [-35, -30],
    };

    public Sim.World.CampRecord ToRecord(ContentDatabase content)
    {
        (float X, float Z) P(string k) => Places.TryGetValue(k, out var v) && v.Length == 2 ? (v[0], v[1]) : (0f, 0f);
        var handle = ContentDatabase.HandleOf(content.Schedules, Schedule, s => s.Id);
        return new Sim.World.CampRecord
        {
            Active = 1, Food = Food, Firewood = Firewood, FireFuelMin = FireFuelMin, Bedding = Bedding,
            Schedule = handle < 0 ? (ushort)0xFFFF : (ushort)handle,
            FireX = P("fire").X, FireZ = P("fire").Z, StoresX = P("stores").X, StoresZ = P("stores").Z,
            ShelterX = P("shelter").X, ShelterZ = P("shelter").Z, WaterX = P("water").X, WaterZ = P("water").Z,
            WoodsX = P("woods").X, WoodsZ = P("woods").Z, ForageX = P("forage_ground").X, ForageZ = P("forage_ground").Z,
        };
    }
}

/// <summary>One row of <c>metrics_daily.csv</c>.</summary>
public sealed record DayMetrics(int Day, string Date, long Step, int People, double SatietyMean, double HydrationMean,
    double EnergyMean, double MeanDistanceFromHomeM, int Events, ulong StateHash,
    double MoodMean = 0, double SocialMean = 0, double ComfortMean = 0, double PurposeMean = 0, double StatusMean = 0,
    CampDay? Camp = null);

public sealed record RunResult(long Steps, ulong FinalHash, IReadOnlyList<DayMetrics> Days, double WallSeconds, CampSummary? Camp = null);

/// <summary>21 §19 camp metrics over a whole run (means of the daily values; task failure over all activities).</summary>
public sealed record CampSummary(double IdleRate, double LowNeedShare, double MoodMean, double BreakingShare, double Divergence, double TaskFailure,
    double FinalFood, double FireShare, double FinalFriends = 0, double FinalEnemies = 0, double InteractionsPerDay = 0, IReadOnlyDictionary<string, double>? InteractionMix = null, double[]? InteractionFunnel = null, double[]? FriendGates = null);

/// <summary>Runs a scenario at max speed, collecting daily metrics (20 §13).</summary>
public static class ScenarioRunner
{
    public static RunResult Run(ScenarioDef scenario, ContentDatabase content, int threads, InputLogFile? inputLog = null, Stream? eventLog = null)
    {
        using var jobs = new JobRunner(threads);
        var world = scenario.CreateWorld(content, jobs);
        var macro = scenario.MacroStepGameMs;
        var stepsPerDay = SimClock.MsPerGameDay / (macro > 0 ? macro : world.Clock.GameMsPerStep);
        var days = new List<DayMetrics>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var events = 0;
        var camp = world.Camp.Active != 0 ? new CampMetrics(content) : null;
        for (var day = 1; day <= scenario.Days; day++)
        {
            for (var s = 0; s < stepsPerDay; s++)
            {
                var output = macro > 0 ? world.StepMacro(macro) : world.Step();
                camp?.Sample(world);
                LogOpenedDecisions(world, output);
                events += output.Events.Count;
                if (inputLog is not null) { foreach (var c in output.AppliedCommands) { inputLog.Append(c); } }
                if (eventLog is not null) { foreach (var e in output.Events) { Sim.Persistence.LogCodec.WriteEvent(eventLog, e); } }
            }

            days.Add(Measure(world, day, events) with { Camp = camp?.EndDay(world) });
            events = 0;
        }

        CampSummary? summary = null;
        if (camp is { Days.Count: > 0 } cm)
        {
            summary = new CampSummary(cm.Days.Average(d => d.IdleRate), cm.Days.Average(d => d.LowNeedShare), cm.Days.Average(d => d.MoodMean),
                cm.Days.Average(d => d.BreakingShare), cm.Days.Average(d => d.Divergence), CampMetrics.TaskFailure(world), world.Camp.Food, cm.Days.Average(d => d.FireShare),
                cm.Days[^1].FriendsPerPerson, cm.Days[^1].EnemiesPerPerson, FriendGates: CampMetrics.FriendGates(world));
            if (world.Systems.OfType<InteractionSystem>().FirstOrDefault() is { } ix)
            {
                var total = ix.Counts.Sum();
                summary = summary with
                {
                    InteractionsPerDay = total / (double)Math.Max(1, world.People.Count * cm.Days.Count),
                    InteractionFunnel = [ix.Eligible, ix.Initiated, ix.NoPartner, ix.NoKind],
                    InteractionMix = Enum.GetValues<InteractionSystem.Kind>().ToDictionary(k => k.ToString(), k => total == 0 ? 0 : ix.Counts[(int)k] / (double)total),
                };
            }
        }

        return new RunResult(world.Clock.Step, StateHasher.Hash(world), days, clock.Elapsed.TotalSeconds, summary);
    }

    /// <summary>
    /// DPs a model may decide are logged as integrity commands (20 §8.5) exactly as <see cref="SimRunner"/> does; with
    /// no gateway here, the policy decides them at their deadline.
    /// </summary>
    public static void LogOpened(SimWorld world, StepOutput output) => LogOpenedDecisions(world, output);

    internal static void LogOpenedDecisions(SimWorld world, StepOutput output)
    {
        foreach (var dp in output.OpenedDecisions)
        {
            world.Enqueue(new CommandEnvelope(world.LastCommandSeq + 1, 0, CommandSource.Integrity, dp));
        }
    }

    private static DayMetrics Measure(SimWorld world, int day, int events)
    {
        var p = world.People;
        double sat = 0, hyd = 0, en = 0, dist = 0, mood = 0, social = 0, comfort = 0, purpose = 0, status = 0;
        for (var i = 0; i < p.Count; i++)
        {
            mood += p.Mood[i].Smoothed;
            social += p.Needs[i].Social;
            comfort += p.Needs[i].Comfort;
            purpose += p.Needs[i].Purpose;
            status += p.Needs[i].Status;
            sat += p.Needs[i].Satiety;
            hyd += p.Needs[i].Hydration;
            en += p.Needs[i].Energy;
            var dx = p.Transforms[i].X - p.Wander[i].HomeX;
            var dz = p.Transforms[i].Z - p.Wander[i].HomeZ;
            dist += Math.Sqrt((dx * dx) + (dz * dz));
        }

        var n = Math.Max(1, p.Count);
        return new DayMetrics(day, GameDate.FromGameMs(world.Clock.GameMs).ToString(), world.Clock.Step, p.Count,
            sat / n, hyd / n, en / n, dist / n, events, StateHasher.Hash(world), mood / n, social / n, comfort / n, purpose / n, status / n);
    }
}
