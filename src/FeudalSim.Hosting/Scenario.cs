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
        var world = new SimWorld(Seed, StartGameMs(), DayLengthMinutes) { Content = content, Jobs = jobs }
            .AddSystem(new WanderSystem())
            .AddSystem(new NeedsDecaySystem());
        if (AiPingStep is { } at) { world.AddSystem(new AiPingSystem(at, AiPingDeadlineSteps)); }
        for (var i = 0; i < Settlers; i++)
        {
            var command = new CommandEnvelope(i + 1, 0, CommandSource.Scenario,
                new SpawnPerson($"Settler {i + 1}", i % 6 * 4f, i / 6 * -4f));
            world.Enqueue(command);
        }

        return world;
    }
}

/// <summary>One row of <c>metrics_daily.csv</c>.</summary>
public sealed record DayMetrics(int Day, string Date, long Step, int People, double SatietyMean, double HydrationMean,
    double EnergyMean, double MeanDistanceFromHomeM, int Events, ulong StateHash);

public sealed record RunResult(long Steps, ulong FinalHash, IReadOnlyList<DayMetrics> Days, double WallSeconds);

/// <summary>Runs a scenario at max speed, collecting daily metrics (20 §13).</summary>
public static class ScenarioRunner
{
    public static RunResult Run(ScenarioDef scenario, ContentDatabase content, int threads, InputLogFile? inputLog = null, Stream? eventLog = null)
    {
        using var jobs = new JobRunner(threads);
        var world = scenario.CreateWorld(content, jobs);
        var stepsPerDay = SimClock.MsPerGameDay / world.Clock.GameMsPerStep;
        var days = new List<DayMetrics>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var events = 0;
        for (var day = 1; day <= scenario.Days; day++)
        {
            for (var s = 0; s < stepsPerDay; s++)
            {
                var output = world.Step();
                events += output.Events.Count;
                if (inputLog is not null) { foreach (var c in output.AppliedCommands) { inputLog.Append(c); } }
                if (eventLog is not null) { foreach (var e in output.Events) { Sim.Persistence.LogCodec.WriteEvent(eventLog, e); } }
            }

            days.Add(Measure(world, day, events));
            events = 0;
        }

        return new RunResult(world.Clock.Step, StateHasher.Hash(world), days, clock.Elapsed.TotalSeconds);
    }

    private static DayMetrics Measure(SimWorld world, int day, int events)
    {
        var p = world.People;
        double sat = 0, hyd = 0, en = 0, dist = 0;
        for (var i = 0; i < p.Count; i++)
        {
            sat += p.Needs[i].Satiety;
            hyd += p.Needs[i].Hydration;
            en += p.Needs[i].Energy;
            var dx = p.Transforms[i].X - p.Wander[i].HomeX;
            var dz = p.Transforms[i].Z - p.Wander[i].HomeZ;
            dist += Math.Sqrt((dx * dx) + (dz * dz));
        }

        var n = Math.Max(1, p.Count);
        return new DayMetrics(day, GameDate.FromGameMs(world.Clock.GameMs).ToString(), world.Clock.Step, p.Count,
            sat / n, hyd / n, en / n, dist / n, events, StateHasher.Hash(world));
    }
}
