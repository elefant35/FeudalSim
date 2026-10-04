using System.ComponentModel;
using FeudalSim.Sim.Persistence;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class LogSettings : CommandSettings
{
    [CommandArgument(0, "<FILE>")]
    [Description("An .fslog file.")]
    public string File { get; init; } = "";

    [CommandOption("--filter <TEXT>")]
    [Description("Only print records whose text contains this.")]
    public string? Filter { get; init; }
}

/// <summary>Prints input-log commands (20 §15 tooling).</summary>
public sealed class LogInputsCommand : Command<LogSettings>
{
    public override int Execute(CommandContext context, LogSettings settings, CancellationToken cancellationToken)
    {
        using var stream = System.IO.File.OpenRead(settings.File);
        var records = LogCodec.ReadCommands(stream, out var valid);
        var shown = 0;
        foreach (var c in records)
        {
            var line = $"seq {c.Seq} @step {c.ApplyStep} [{c.Source}] {c.Payload}";
            if (settings.Filter is null || line.Contains(settings.Filter, StringComparison.OrdinalIgnoreCase)) { Console.WriteLine(line); shown++; }
        }

        Console.WriteLine($"log: {records.Count} commands ({shown} shown); {valid} of {stream.Length} bytes valid");
        return 0;
    }
}

/// <summary>Prints history/event-log records.</summary>
public sealed class LogEventsCommand : Command<LogSettings>
{
    public override int Execute(CommandContext context, LogSettings settings, CancellationToken cancellationToken)
    {
        using var stream = System.IO.File.OpenRead(settings.File);
        var records = LogCodec.ReadEvents(stream, out var valid);
        var shown = 0;
        foreach (var e in records)
        {
            var line = $"seq {e.Seq} @step {e.Step} [{e.Salience}] {e.Payload}";
            if (settings.Filter is null || line.Contains(settings.Filter, StringComparison.OrdinalIgnoreCase)) { Console.WriteLine(line); shown++; }
        }

        Console.WriteLine($"log: {records.Count} events ({shown} shown); {valid} of {stream.Length} bytes valid");
        return 0;
    }
}
