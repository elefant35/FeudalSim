using System.Globalization;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Time;

namespace FeudalSim.Hosting;

/// <summary>
/// Developer console commands (20 §15). Runner controls (pause, speed) are never logged; anything that
/// changes sim state goes through a logged <see cref="StateCommand"/>.
/// </summary>
public sealed class DevCommands
{
    private readonly SimRunner _runner;
    private readonly Dictionary<string, (string Help, Func<string[], string> Run)> _commands;

    public DevCommands(SimRunner runner)
    {
        _runner = runner;
        _commands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pause"] = ("pause — stop stepping", _ => { _runner.Pause(); return "paused"; }),
            ["resume"] = ("resume — continue in real time", _ => { _runner.Resume(); return "running"; }),
            ["step"] = ("step <n> — advance n steps while paused", a => $"step {_runner.StepWhilePaused(ParseInt(a, 0, 1)).Result}"),
            ["timescale"] = ("timescale <x> — real-time multiplier (not logged)", a => { _runner.SetTimeScale(double.Parse(a[0], CultureInfo.InvariantCulture)); return $"timescale {a[0]}"; }),
            ["daylength"] = ("daylength <minutes> — logged SetDayLength", a => { _runner.Submit(CommandSource.Dev, new SetDayLength(ParseInt(a, 0, 30))); return "queued"; }),
            ["spawn"] = ("spawn <name> <x> <z> — logged SpawnPerson", a => { _runner.Submit(CommandSource.Dev, new SpawnPerson(a[0], float.Parse(a[1], CultureInfo.InvariantCulture), float.Parse(a[2], CultureInfo.InvariantCulture))); return "queued"; }),
            ["hash"] = ("hash — current state hash", _ => $"hash {_runner.Invoke(StateHasher.Hash).Result:x16}"),
            ["time"] = ("time — current game date and step", _ => _runner.Invoke(w => $"{GameDate.FromGameMs(w.Clock.GameMs)} (step {w.Clock.Step})").Result),
            ["stats"] = ("stats — runner statistics", _ => $"steps {_runner.StepsExecuted}, dilation {_runner.TimeDilationEvents}, mode {_runner.Mode}, timescale {_runner.TimeScale}"),
        };
    }

    public IEnumerable<string> Help => _commands.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Value.Help);

    public int Count => _commands.Count;

    public string Execute(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return ""; }
        return _commands.TryGetValue(parts[0], out var c) ? c.Run(parts[1..]) : $"unknown command '{parts[0]}' (try: {string.Join(", ", _commands.Keys.Order(StringComparer.Ordinal))})";
    }

    private static int ParseInt(string[] args, int index, int fallback)
        => args.Length > index ? int.Parse(args[index], CultureInfo.InvariantCulture) : fallback;
}
