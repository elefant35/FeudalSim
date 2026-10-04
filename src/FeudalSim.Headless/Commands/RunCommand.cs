using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FeudalSim.Content;
using FeudalSim.Hosting;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class RunSettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    [Description("Scenario YAML (e.g. content/scenarios/m0_smoke.yaml).")]
    public string Scenario { get; init; } = "content/scenarios/m0_smoke.yaml";

    [CommandOption("--threads <N>")]
    [Description("Sim worker threads (results must not depend on this).")]
    [DefaultValue(1)]
    public int Threads { get; init; } = 1;

    [CommandOption("--days <N>")]
    [Description("Override the scenario's length in game days.")]
    public int? Days { get; init; }

    [CommandOption("--seed <N>")]
    [Description("Override the scenario's world seed.")]
    public ulong? Seed { get; init; }

    [CommandOption("--out <DIR>")]
    [Description("Output root (default sim_runs/).")]
    public string Out { get; init; } = "sim_runs";

    [CommandOption("--realtime")]
    [Description("Run on the real-time SimRunner (10 steps/s at 1×) instead of max speed, and report the step rate.")]
    public bool Realtime { get; init; }

    [CommandOption("--seconds <N>")]
    [Description("With --realtime: how many real seconds to run.")]
    [DefaultValue(60)]
    public int Seconds { get; init; } = 60;

    [CommandOption("--write-events")]
    [Description("Also write every domain event to events.fslog (CRC'd MessagePack records).")]
    public bool WriteEvents { get; init; }

    [CommandOption("--verify-determinism")]
    [Description("Run again (in-process, and with 1 thread if --threads > 1) and require identical hashes.")]
    public bool VerifyDeterminism { get; init; }
}

public sealed class RunCommand : Command<RunSettings>
{
    public override int Execute(CommandContext context, RunSettings settings, CancellationToken cancellationToken)
    {
        var contentRoot = RepoPaths.FindContentRoot(Directory.GetCurrentDirectory());
        var compiled = ContentCompiler.Compile(contentRoot);
        if (!compiled.Ok)
        {
            foreach (var e in compiled.Errors) { Console.Error.WriteLine($"content/{e}"); }
            return 1;
        }

        var scenario = ScenarioDef.Load(settings.Scenario);
        scenario = scenario with { Days = settings.Days ?? scenario.Days, Seed = settings.Seed ?? scenario.Seed };
        if (settings.Realtime) { return RunRealtime(scenario, compiled.Database!, settings); }
        var runId = $"{scenario.Id.Replace("scenario.", "", StringComparison.Ordinal)}-s{scenario.Seed}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var dir = Path.Combine(settings.Out, runId);
        Directory.CreateDirectory(dir);

        RunResult result;
        using (var log = InputLogFile.OpenOrCreate(Path.Combine(dir, "inputs.fslog")))
        using (var events = settings.WriteEvents ? File.Create(Path.Combine(dir, "events.fslog")) : null)
        {
            result = ScenarioRunner.Run(scenario, compiled.Database!, settings.Threads, log, events);
        }

        var verified = (bool?)null;
        if (settings.VerifyDeterminism)
        {
            var again = ScenarioRunner.Run(scenario, compiled.Database!, settings.Threads);
            var serial = settings.Threads > 1 ? ScenarioRunner.Run(scenario, compiled.Database!, 1) : again;
            verified = again.FinalHash == result.FinalHash && serial.FinalHash == result.FinalHash;
            Console.WriteLine($"determinism: rerun {again.FinalHash:x16}, 1-thread {serial.FinalHash:x16} → {(verified.Value ? "IDENTICAL" : "MISMATCH")}");
        }

        WriteCsv(Path.Combine(dir, "metrics_daily.csv"), result.Days);
        var summary = new
        {
            run_id = runId,
            scenario = scenario.Id,
            seed = scenario.Seed,
            days = scenario.Days,
            settlers = scenario.Settlers,
            threads = settings.Threads,
            steps = result.Steps,
            final_hash = result.FinalHash.ToString("x16", CultureInfo.InvariantCulture),
            content_hash = compiled.Database!.Hash.ToString("x16", CultureInfo.InvariantCulture),
            wall_seconds = Math.Round(result.WallSeconds, 3),
            steps_per_second = Math.Round(result.Steps / Math.Max(result.WallSeconds, 1e-9)),
            determinism_verified = verified,
        };
        File.WriteAllText(Path.Combine(dir, "run.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() }) + "\n");

        Console.WriteLine($"run: {scenario.Id} seed {scenario.Seed}, {scenario.Days} days, {result.Steps:N0} steps in {result.WallSeconds:F2} s; final hash {result.FinalHash:x16}");
        Console.WriteLine($"run: wrote {dir}/metrics_daily.csv, run.json, inputs.fslog");
        return verified == false ? 2 : 0;
    }

    private static int RunRealtime(ScenarioDef scenario, FeudalSim.Sim.Content.ContentDatabase content, RunSettings settings)
    {
        var dir = Path.Combine(settings.Out, $"{scenario.Id.Replace("scenario.", "", StringComparison.Ordinal)}-realtime-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(dir);
        using var jobs = new JobRunner(settings.Threads);
        var world = scenario.CreateWorld(content, jobs);
        FeudalSim.AI.AiStack? stack = null;
        FeudalSim.AI.AiGateway? gateway = null;
        if (scenario.AiPingStep is not null || scenario.DecisionPingStep is not null)
        {
            var config = FeudalSim.AI.AiConfig.Load(FeudalSim.AI.AiConfig.FindEnvFile(Directory.GetCurrentDirectory()));
            Console.WriteLine($"ai: key {(config.ChatKey.IsSet ? "set" : "missing")}, mode {(config.TemplateMode ? "template" : "live")}, gateway {config.GatewayMode}, model {config.DialogueModel}, decider {config.DeciderModel}");
            stack = FeudalSim.AI.AiStack.Create(config);
            gateway = stack.CreateGateway();
        }

        var events = new List<FeudalSim.Sim.Events.EventEnvelope>();
        using (var log = InputLogFile.OpenOrCreate(Path.Combine(dir, "inputs.fslog")))
        using (var runner = new SimRunner(world, log, RunMode.Running, gateway))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lastReport = 0L;
            while (sw.Elapsed.TotalSeconds < settings.Seconds)
            {
                Thread.Sleep(1_000);
                while (runner.Events.TryPop(out var e)) { events.Add(e); }
                if (sw.Elapsed.TotalSeconds >= lastReport + 10)
                {
                    lastReport = (long)sw.Elapsed.TotalSeconds;
                    Console.WriteLine($"realtime: t={sw.Elapsed.TotalSeconds:F0}s steps={runner.StepsExecuted}");
                }
            }

            var (measuredSteps, measuredSeconds) = (runner.StepsExecuted, sw.Elapsed.TotalSeconds);   // before the pause and drain
            runner.Pause();
            Thread.Sleep(200);
            while (runner.Events.TryPop(out var e)) { events.Add(e); }
            var (finalStep, finalHash) = runner.Invoke(w => (w.Clock.Step, FeudalSim.Sim.StateHasher.Hash(w))).GetAwaiter().GetResult();
            Console.WriteLine($"realtime: final step {finalStep} hash {finalHash:x16}");
            var rate = measuredSteps / measuredSeconds;
            var ok = Math.Abs(rate - 10.0) <= 0.2;
            Console.WriteLine($"realtime: {measuredSteps} steps in {measuredSeconds:F1} s = {rate:F2} steps/s (target 10.0 ± 0.2) → {(ok ? "OK" : "OUT OF RANGE")}; dilation events {runner.TimeDilationEvents}");
            foreach (var e in events.Where(e => e.Payload is FeudalSim.Sim.Events.AiResultApplied or FeudalSim.Sim.Events.CommandRejected
                         or FeudalSim.Sim.Events.DecisionResolved or FeudalSim.Sim.Events.IntegrityMismatch))
            {
                Console.WriteLine($"event @step {e.Step}: {e.Payload}");
            }

            using var eventLog = File.Create(Path.Combine(dir, "events.fslog"));
            foreach (var e in events) { FeudalSim.Sim.Persistence.LogCodec.WriteEvent(eventLog, e); }
            gateway?.Dispose();
            stack?.Dispose();
            Console.WriteLine($"realtime: wrote {dir}/inputs.fslog, events.fslog");
            return ok ? 0 : 3;
        }
    }

    private static void WriteCsv(string path, IReadOnlyList<DayMetrics> days)
    {
        var sb = new StringBuilder("day,date,step,people,satiety_mean,hydration_mean,energy_mean,mean_dist_home_m,events,state_hash,mood_mean,social_mean,comfort_mean,purpose_mean,status_mean,idle_rate,low_need_share,breaking_share,divergence,food,firewood,fire_share\n");
        foreach (var d in days)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{d.Day},{d.Date},{d.Step},{d.People},{d.SatietyMean:F3},{d.HydrationMean:F3},{d.EnergyMean:F3},{d.MeanDistanceFromHomeM:F3},{d.Events},{d.StateHash:x16},{d.MoodMean:F2},{d.SocialMean:F2},{d.ComfortMean:F2},{d.PurposeMean:F2},{d.StatusMean:F2},{d.Camp?.IdleRate:F3},{d.Camp?.LowNeedShare:F4},{d.Camp?.BreakingShare:F3},{d.Camp?.Divergence:F3},{d.Camp?.Food:F0},{d.Camp?.Firewood:F1},{d.Camp?.FireShare:F2}\n");
        }

        File.WriteAllText(path, sb.ToString());
    }
}
