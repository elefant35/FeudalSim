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
        var runId = $"{scenario.Id.Replace("scenario.", "", StringComparison.Ordinal)}-s{scenario.Seed}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var dir = Path.Combine(settings.Out, runId);
        Directory.CreateDirectory(dir);

        RunResult result;
        using (var log = InputLogFile.OpenOrCreate(Path.Combine(dir, "inputs.fslog")))
        {
            result = ScenarioRunner.Run(scenario, compiled.Database!, settings.Threads, log);
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

    private static void WriteCsv(string path, IReadOnlyList<DayMetrics> days)
    {
        var sb = new StringBuilder("day,date,step,people,satiety_mean,hydration_mean,energy_mean,mean_dist_home_m,events,state_hash\n");
        foreach (var d in days)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{d.Day},{d.Date},{d.Step},{d.People},{d.SatietyMean:F3},{d.HydrationMean:F3},{d.EnergyMean:F3},{d.MeanDistanceFromHomeM:F3},{d.Events},{d.StateHash:x16}\n");
        }

        File.WriteAllText(path, sb.ToString());
    }
}
