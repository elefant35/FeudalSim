using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.World;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class BenchSettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    public string Scenario { get; init; } = "content/scenarios/s6_mixed.yaml";

    [CommandOption("--days <N>")]
    [Description("Game days to run (default: the scenario's).")]
    public int? Days { get; init; }

    [CommandOption("--warmup-days <N>")]
    [Description("Game days run before timing starts (JIT, first-day setup).")]
    public int WarmupDays { get; init; }

    [CommandOption("--max-step-ms <MS>")]
    [Description("Fail (exit 1) if the average step exceeds this.")]
    public double? MaxStepMs { get; init; }

    [CommandOption("--max-seconds <S>")]
    [Description("Fail (exit 1) if the timed run takes longer than this.")]
    public double? MaxSeconds { get; init; }
}

/// <summary>
/// 20 §19 sim budgets (spike S6): runs a scenario single-threaded, timing every step and every system, and reports step
/// time (avg / p99 / max), per-system totals and per-agent cost by tier (time ÷ agent updates; meaningful when one tier
/// dominates the scenario).
/// </summary>
public sealed class BenchCommand : Command<BenchSettings>
{
    public override int Execute(CommandContext context, BenchSettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var scenario = ScenarioDef.Load(settings.Scenario);
        var days = settings.Days ?? scenario.Days;
        using var jobs = new JobRunner(1);
        var world = scenario.CreateWorld(content, jobs);
        var macro = scenario.MacroStepGameMs;
        var stepsPerDay = (int)(Sim.Time.SimClock.MsPerGameDay / (macro > 0 ? macro : world.Clock.GameMsPerStep));
        StepOutput Step() => macro > 0 ? world.StepMacro(macro) : world.Step();
        for (var s = 0; s < settings.WarmupDays * stepsPerDay; s++) { ScenarioRunner.LogOpened(world, Step()); }

        var observer = new TimingObserver(world.Systems.Count);
        world.Observer = observer;
        var total = days * stepsPerDay;
        var stepTicks = new long[total];
        var updates = new long[5];
        var wall = Stopwatch.StartNew();
        for (var s = 0; s < total; s++)
        {
            var t0 = Stopwatch.GetTimestamp();
            var output = Step();
            stepTicks[s] = Stopwatch.GetTimestamp() - t0;
            observer.EndStep(stepTicks[s], world.Clock.GameMs);
            ScenarioRunner.LogOpened(world, output);
            var due = world.Due;
            for (var k = 0; k < due.Count; k++) { updates[(int)world.People.Lod[due.Rows[k]].Tier]++; }
        }

        wall.Stop();
        world.Observer = null;
        var ms = stepTicks.Select(t => t * 1000.0 / Stopwatch.Frequency).OrderBy(x => x).ToArray();
        var tiers = new int[5];
        foreach (var l in world.People.Lod) { tiers[(int)l.Tier]++; }
        var simSeconds = ms.Sum() / 1000.0;
        static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        Console.WriteLine(F($"bench: {scenario.Id} · {world.People.Count} people (LOD0 {tiers[0]}, LOD1 {tiers[1]}, LOD2 {tiers[2]}, LOD3 {tiers[3]}) · {days} days · {total:N0} {(macro > 0 ? "macro" : "fine")} steps"));
        Console.WriteLine(F($"  wall {wall.Elapsed.TotalSeconds:F2} s (sim {simSeconds:F2} s) · step avg {ms.Average():F3} ms · p99 {ms[(int)(0.99 * (ms.Length - 1))]:F3} ms · max {ms[^1]:F3} ms"));
        Console.WriteLine(F($"  agent updates: LOD0/1 {updates[0] + updates[1]:N0} · LOD2 {updates[2]:N0} · LOD3 {updates[3]:N0}"));
        foreach (var (tier, n, unit) in new[] { (1, updates[0] + updates[1], "update (step)"), (2, updates[2], "agent-hour"), (3, updates[3], "agent-day") })
        {
            if (n > 0) { Console.WriteLine(F($"  per {unit,-14} {simSeconds * 1e6 / n,10:F2} µs (all sim time ÷ LOD{tier} updates; exact when LOD{tier} dominates)")); }
        }

        Console.WriteLine("  systems (total ms · share · µs/step):");
        var systemsTotal = observer.Ticks.Sum();
        for (var i = 0; i < world.Systems.Count; i++)
        {
            var sysMs = observer.Ticks[i] * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine(F($"    {world.Systems[i].Name,-14} {sysMs,10:F1} · {(systemsTotal == 0 ? 0 : observer.Ticks[i] / (double)systemsTotal),6:P1} · {sysMs * 1000 / total,8:F2}"));
        }

        Console.WriteLine(F($"  slowest step at {Sim.Time.GameDate.FromGameMs(observer.WorstGameMs)}: {observer.WorstTicks * 1000.0 / Stopwatch.Frequency:F1} ms — ")
            + string.Join(" · ", Enumerable.Range(0, world.Systems.Count).Where(i => observer.Worst[i] * 1000.0 / Stopwatch.Frequency >= 0.1)
                .Select(i => F($"{world.Systems[i].Name} {observer.Worst[i] * 1000.0 / Stopwatch.Frequency:F1}"))));
        var ok = true;
        if (settings.MaxStepMs is { } maxStep && ms.Average() > maxStep) { Console.WriteLine(F($"  FAIL: average step {ms.Average():F3} ms > {maxStep} ms")); ok = false; }
        if (settings.MaxSeconds is { } maxS && wall.Elapsed.TotalSeconds > maxS) { Console.WriteLine(F($"  FAIL: {wall.Elapsed.TotalSeconds:F2} s > {maxS} s")); ok = false; }
        Console.WriteLine(F($"  final hash {StateHasher.Hash(world):x16} · food {world.Camp.Food:F0} · firewood {world.Camp.Firewood:F1} · mean mood {Mean(world, i => world.People.Mood[i].Smoothed):F1} · satiety {Mean(world, i => world.People.Needs[i].Satiety):F1} · relationships {world.Relationships.Count:N0}"));
        Console.WriteLine(ok ? "bench: OK" : "bench: FAILED");
        return ok ? 0 : 1;
    }

    private static double Mean(SimWorld world, Func<int, float> get)
    {
        double sum = 0;
        for (var i = 0; i < world.People.Count; i++) { sum += get(i); }
        return world.People.Count == 0 ? 0 : sum / world.People.Count;
    }

    private sealed class TimingObserver(int systems) : ISystemObserver
    {
        private long _start;
        private readonly long[] _step = new long[systems];

        public long[] Ticks { get; } = new long[systems];

        /// <summary>Per-system ticks of the slowest step so far.</summary>
        public long[] Worst { get; } = new long[systems];

        public long WorstTicks { get; private set; }

        public long WorstGameMs { get; private set; }

        public void Begin(int systemIndex) => _start = Stopwatch.GetTimestamp();

        public void End(int systemIndex)
        {
            var t = Stopwatch.GetTimestamp() - _start;
            Ticks[systemIndex] += t;
            _step[systemIndex] += t;
        }

        public void EndStep(long stepTicks, long gameMs)
        {
            if (stepTicks > WorstTicks)
            {
                (WorstTicks, WorstGameMs) = (stepTicks, gameMs);
                _step.CopyTo(Worst, 0);
            }

            Array.Clear(_step);
        }
    }
}
