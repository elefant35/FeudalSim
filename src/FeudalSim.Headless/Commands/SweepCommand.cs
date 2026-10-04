using System.ComponentModel;
using System.Globalization;
using System.Text;
using FeudalSim.Content;
using FeudalSim.Hosting;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class SweepSettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    public string Scenario { get; init; } = "content/scenarios/m1_camp.yaml";

    [CommandOption("--seeds <N>")]
    [Description("Seeds 1..N (21 §19 runs 100 per scenario nightly).")]
    public int Seeds { get; init; } = 100;

    [CommandOption("--days <N>")]
    public int Days { get; init; } = 30;

    [CommandOption("--parallel <N>")]
    [Description("Worlds run at once (default: processor count). Each world is single-threaded and deterministic.")]
    public int Parallel { get; init; } = Environment.ProcessorCount;

    [CommandOption("--out <DIR>")]
    public string Out { get; init; } = "sim_runs";
}

/// <summary>Runs a scenario over many seeds and checks the 21 §19 camp metrics against their target bands.</summary>
public sealed class SweepCommand : Command<SweepSettings>
{
    private static readonly (string Name, Func<CampSummary, double> Get, double Lo, double Hi)[] Bands =
    [
        ("idle rate", s => s.IdleRate, 0.10, 0.25),
        ("need health (agent-hours with a need < 15)", s => s.LowNeedShare, 0, 0.03),
        ("mean mood", s => s.MoodMean, -10, 30),
        ("breaking-band agent-days", s => s.BreakingShare, 0, 0.05),
        ("behavior divergence (same role)", s => s.Divergence, 0.15, 1),
        ("task failure", s => s.TaskFailure, 0, 0.05),
    ];

    public override int Execute(CommandContext context, SweepSettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var scenario = ScenarioDef.Load(settings.Scenario) with { Days = settings.Days };
        var results = new (ulong Seed, RunResult Run)[settings.Seeds];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.For(0, settings.Seeds, new ParallelOptions { MaxDegreeOfParallelism = settings.Parallel, CancellationToken = cancellationToken }, i =>
        {
            var seed = (ulong)(i + 1);
            results[i] = (seed, ScenarioRunner.Run(scenario with { Seed = seed }, content, threads: 1));
        });

        var summaries = results.Where(r => r.Run.Camp is not null).Select(r => (r.Seed, S: r.Run.Camp!)).ToList();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"sweep: {scenario.Id} · {settings.Seeds} seeds × {settings.Days} days in {sw.Elapsed.TotalSeconds:F1} s"));
        if (summaries.Count == 0) { Console.WriteLine("sweep: no camp metrics (scenario is not ai: utility)"); return 1; }

        var ok = true;
        foreach (var (name, get, lo, hi) in Bands)
        {
            var values = summaries.Select(x => get(x.S)).OrderBy(v => v).ToArray();
            var inBand = values.Count(v => v >= lo && v <= hi) / (double)values.Length;
            ok &= inBand >= 0.9;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {name,-44} mean {values.Average(),8:F3} · p5 {values[(int)(0.05 * (values.Length - 1))],8:F3} · p95 {values[(int)(0.95 * (values.Length - 1))],8:F3} · band [{lo}, {hi}] · seeds in band {inBand,4:P0}"));
        }

        var food = summaries.Select(x => x.S.FinalFood).ToArray();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  camp: final food mean {food.Average():F0} (min {food.Min():F0}) · fire burning {summaries.Average(x => x.S.FireShare):P0} of the time · seeds with food left {food.Count(f => f > 0) / (double)food.Length:P0}"));

        Directory.CreateDirectory(settings.Out);
        var csv = Path.Combine(settings.Out, $"sweep-{scenario.Id.Replace("scenario.", "", StringComparison.Ordinal)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        var sb = new StringBuilder("seed,idle_rate,low_need_share,mood_mean,breaking_share,divergence,task_failure,final_food,fire_share,final_hash\n");
        foreach (var (seed, run) in results)
        {
            var s = run.Camp!;
            sb.Append(CultureInfo.InvariantCulture, $"{seed},{s.IdleRate:F4},{s.LowNeedShare:F5},{s.MoodMean:F2},{s.BreakingShare:F4},{s.Divergence:F4},{s.TaskFailure:F4},{s.FinalFood:F0},{s.FireShare:F3},{run.FinalHash:x16}\n");
        }

        File.WriteAllText(csv, sb.ToString());
        Console.WriteLine($"sweep: {(ok ? "all metrics in band for ≥ 90% of seeds" : "SOME METRICS OUT OF BAND")}; wrote {csv}");
        return ok ? 0 : 2;
    }
}
