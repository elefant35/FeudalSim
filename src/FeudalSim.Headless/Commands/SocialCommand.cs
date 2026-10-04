using System.ComponentModel;
using System.Globalization;
using FeudalSim.Content;
using FeudalSim.Hosting;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class SocialSettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    public string Scenario { get; init; } = "content/scenarios/m1_camp.yaml";

    [CommandOption("--seeds <N>")]
    public int Seeds { get; init; } = 10;

    [CommandOption("--days <N>")]
    public int Days { get; init; } = 30;

    [CommandOption("--parallel <N>")]
    [Description("Worlds run at once (default: processor count).")]
    public int Parallel { get; init; } = Environment.ProcessorCount;
}

/// <summary>M1-22 (30 M1 exit): the camp for 30 days per seed — no deadlocks, ≥ 1 emergent dispute per 10 days. Exit 0 = pass.</summary>
public sealed class SocialCommand : Command<SocialSettings>
{
    public override int Execute(CommandContext context, SocialSettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var scenario = ScenarioDef.Load(settings.Scenario);
        var runs = new SocialRun[settings.Seeds];
        System.Threading.Tasks.Parallel.For(0, settings.Seeds, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, settings.Parallel) },
            i => runs[i] = SocialCheck.Run(scenario with { Seed = (ulong)(i + 1) }, content, settings.Days));
        static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        foreach (var r in runs)
        {
            Console.WriteLine(F($"seed {r.Seed,2}: disputes {r.Disputes,3} ({r.DisputesPer10Days:F1}/10 d) · fights {r.Fights,2} · interventions {r.Interventions,2} · insults {r.Insults,3} · stuck {r.StuckPeople} (longest same activity awake {r.LongestSameHours:F1} h, travel {r.LongestTravelHours:F1} h) · activities {r.ActivitiesStarted} · {r.WallSeconds:F1} s"));
        }

        var rungs = Enumerable.Range(0, 8).Select(k => runs.Sum(r => r.TopRungs[k])).ToArray();
        Console.WriteLine($"disputes by top rung (0 calm … 2 argument · 3 threat · 4 shove · 5 brawl · 6 armed · 7 lethal): {string.Join(" · ", rungs.Select((n, k) => $"{k}:{n}"))}");
        var disputesOk = runs.All(r => r.DisputesPer10Days >= 1);
        var deadlockOk = runs.All(r => r.NoDeadlock);
        Console.WriteLine(F($"social: {settings.Seeds} worlds × {settings.Days} days · disputes per 10 days min {runs.Min(r => r.DisputesPer10Days):F1} / mean {runs.Average(r => r.DisputesPer10Days):F1} (≥ 1: {(disputesOk ? "PASS" : "FAIL")}) · deadlocks {runs.Sum(r => r.StuckPeople)} ({(deadlockOk ? "PASS" : "FAIL")})"));
        return disputesOk && deadlockOk ? 0 : 1;
    }
}
