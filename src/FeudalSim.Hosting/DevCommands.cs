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
            ["why"] = ("why <name|#row> — the person's last action decision: top candidates with W·C·P×V·E·S·M (21 §7.2)", a => _runner.Invoke(w => Why(w, string.Join(' ', a))).Result),
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

    /// <summary>Formats the DecisionTrace of one person (M1-03 inspector).</summary>
    public static string Why(SimWorld world, string who)
    {
        var p = world.People;
        var row = who.StartsWith('#') && int.TryParse(who[1..], CultureInfo.InvariantCulture, out var r) ? r : -1;
        for (var i = 0; row < 0 && i < p.Count; i++) { if (string.Equals(p.Names[i], who, StringComparison.OrdinalIgnoreCase)) { row = i; } }
        if (row < 0 || row >= p.Count) { return $"no person '{who}'"; }
        if (world.Systems.OfType<Sim.Systems.ActivitySystem>().FirstOrDefault() is not { } ai) { return "no utility AI in this world"; }
        var (head, top) = ai.Trace(row);
        if (head.Count == 0) { return $"{p.Names[row]}: no decision yet"; }
        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{p.Names[row]} at {GameDate.FromGameMs(head.GameMs)}: {(head.Kept ? "kept" : "chose")} {ai.ActionAt(head.Chosen).Id} (τ {head.Tau:0.000})");
        foreach (var c in top.Span)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n  P{c.Class} {ai.ActionAt(c.Action).Id,-22} {c.Score,6:0.000} = W {c.W:0.00} · C {c.C:0.00} · P×V {c.PV:0.00} · E {c.E:0.00} · S {c.S:0.00} · M {c.M:0.00}");
        }

        return sb.ToString();
    }

    private static int ParseInt(string[] args, int index, int fallback)
        => args.Length > index ? int.Parse(args[index], CultureInfo.InvariantCulture) : fallback;
}
