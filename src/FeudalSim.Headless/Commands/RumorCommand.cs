using System.ComponentModel;
using System.Globalization;
using FeudalSim.Content;
using FeudalSim.Hosting;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class RumorSettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    public string Scenario { get; init; } = "content/scenarios/m1_camp.yaml";

    [CommandOption("--seeds <N>")]
    public int Seeds { get; init; } = 40;

    [CommandOption("--days <N>")]
    [Description("Days to follow each seeded claim.")]
    public int Days { get; init; } = 10;

    [CommandOption("--claims <IDS>")]
    [Description("Comma-separated predicates (default: mundane, notable and juicy classes of 16 §7.8).")]
    public string Claims { get; init; } = "claim.helped,claim.cheated,claim.dead";

    [CommandOption("--witnesses <N>")]
    [Description("First-hand witnesses seeded on day 2 08:00 (16 §7.8 uses 3). 0 = a public event at the evening fire, witnessed by whoever is in range.")]
    public int Witnesses { get; init; } = 3;

    [CommandOption("--parallel <N>")]
    public int Parallel { get; init; } = Environment.ProcessorCount;
}

/// <summary>16 §7.8 propagation speeds: seed one claim with 3 witnesses per run and report t50 / t90 / reach / variant share.</summary>
public sealed class RumorCommand : Command<RumorSettings>
{
    public override int Execute(CommandContext context, RumorSettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var scenario = ScenarioDef.Load(settings.Scenario);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"rumor: {scenario.Id} · {settings.Seeds} seeds × {settings.Days} days per claim · {settings.Witnesses} witnesses (t50/t90 on heard; heard = witnessed or told, any variant; held = c ≥ 0.5)"));
        foreach (var predicate in settings.Claims.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var runs = new RumorRun[settings.Seeds];
            System.Threading.Tasks.Parallel.For(0, settings.Seeds, new ParallelOptions { MaxDegreeOfParallelism = settings.Parallel, CancellationToken = cancellationToken },
                i => runs[i] = RumorProbe.Run(scenario with { Seed = (ulong)(i + 1) }, content, predicate, settings.Days, settings.Witnesses));
            string Median(Func<RumorRun, double?> get)
            {
                var hits = runs.Select(get).Where(v => v is not null).Select(v => v!.Value).OrderBy(v => v).ToArray();
                return hits.Length * 2 < runs.Length ? $"rarely ({hits.Length}/{runs.Length})" : string.Create(CultureInfo.InvariantCulture, $"{hits[hits.Length / 2]:F1} d");
            }

            var reached = runs.Where(r => r.T50Days is not null).ToArray();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {predicate,-16} J {runs[0].Juiciness:F1} · witnesses {runs.Average(r => r.Witnesses):F1} · t50 {Median(r => r.T50Days),-14} · t90 {Median(r => r.T90Days),-14} · tellings {runs.Average(r => r.Exchanges):F0} (taken {runs.Sum(r => r.Taken) / (double)Math.Max(1, runs.Sum(r => r.Exchanges)):P0}) · variants at t50 {(reached.Length == 0 ? 0 : reached.Average(r => r.VariantShareAtT50)):P0}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {"",-16} day 3: heard {runs.Average(r => r.HeardDay3),4:P0} (≥ 80 % in {runs.Count(r => r.HeardDay3 >= 0.8) / (double)runs.Length:P0} of seeds) · held {runs.Average(r => r.HeldDay3),4:P0} · end: heard {runs.Average(r => r.FinalHeard):P0} · held {runs.Average(r => r.FinalHeld):P0}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"rumor: done in {sw.Elapsed.TotalSeconds:F1} s"));
        return 0;
    }
}
