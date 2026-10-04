using System.ComponentModel;
using FeudalSim.Content;
using FeudalSim.Hosting;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class CompleteSettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    public string Scenario { get; init; } = "content/scenarios/m1_talk.yaml";

    [CommandOption("--seeds <N>")]
    [Description("Run the golden goals in N worlds (seeds 1…N); all must complete.")]
    public int Seeds { get; init; } = 1;

    [CommandOption("--verbose")]
    [Description("Print every turn and line.")]
    public bool Verbose { get; init; }
}

/// <summary>22 §13.3 / §17.2 #12: the golden goals in template mode — the policy decides every DP, templates speak. Exit 0 = completable.</summary>
public sealed class CompleteCommand : Command<CompleteSettings>
{
    public override int Execute(CommandContext context, CompleteSettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var scenario = ScenarioDef.Load(settings.Scenario);
        var failed = 0;
        for (var seed = 1UL; seed <= (ulong)Math.Max(1, settings.Seeds); seed++)
        {
            var report = new CompletabilityRun(content, scenario with { Seed = seed }, settings.Verbose ? Console.Out : null).Run();
            Console.WriteLine($"seed {seed}: {(report.Passed ? "PASS" : "FAIL")} · {report.Turns} player turns · {report.Lines} NPC lines ({report.TemplateLines} template) · {report.Decisions} decisions ({report.PolicyDecisions} by the policy) · {report.Rejected} rejected · hash {report.FinalHash:x16}");
            foreach (var r in report.RejectedReasons) { Console.WriteLine($"  rejected: {r}"); }
            foreach (var g in report.Goals) { Console.WriteLine($"  {(g.Done ? "PASS" : "FAIL")}  {g.Goal} — {g.Detail} (settlers tried: {g.Attempts})"); }
            if (!report.Passed) { failed++; }
        }

        Console.WriteLine(failed == 0 ? $"complete: PASS — completable in template mode ({settings.Seeds} worlds)" : $"complete: FAIL in {failed} of {settings.Seeds} worlds");
        return failed == 0 ? 0 : 1;
    }
}
