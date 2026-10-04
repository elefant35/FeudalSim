using System.ComponentModel;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class ReplaySettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    [Description("The scenario the log was recorded with.")]
    public string Scenario { get; init; } = "";

    [CommandOption("--log <PATH>")]
    [Description("The recorded inputs.fslog.")]
    public string Log { get; init; } = "";

    [CommandOption("--until-step <N>")]
    [Description("Replay up to this step (default: the last logged ApplyStep).")]
    public long? UntilStep { get; init; }
}

/// <summary>Replays a recorded session headless (20 §8.8) and prints the final state hash.</summary>
public sealed class ReplayCommand : Command<ReplaySettings>
{
    public override int Execute(CommandContext context, ReplaySettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var scenario = ScenarioDef.Load(settings.Scenario);
        var log = InputLogFile.ReadAll(settings.Log).Where(c => c.Source != CommandSource.Scenario).ToList();
        var until = settings.UntilStep ?? log.Max(c => c.ApplyStep);
        using var jobs = new JobRunner(1);
        var world = scenario.CreateWorld(content, jobs);
        var lodEvents = 0;
        Replayer.Run(world, log, until, o => lodEvents += o.Events.Count(e => e.Payload is Sim.Events.LodChanged or Sim.Events.Embodied));
        Console.WriteLine($"replay: {log.Count} commands → step {world.Clock.Step}; LOD/embodiment events {lodEvents}; final hash {StateHasher.Hash(world):x16}");
        return 0;
    }
}
