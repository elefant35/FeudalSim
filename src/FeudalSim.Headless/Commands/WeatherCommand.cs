using System.ComponentModel;
using System.Globalization;
using FeudalSim.Sim.Climate;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class WeatherSettings : CommandSettings
{
    [CommandOption("--seed <N>")]
    public ulong Seed { get; init; } = 42;

    [CommandOption("--years <N>")]
    [Description("Years of slots to run (32 days × 4 slots each).")]
    public int Years { get; init; } = 3000;
}

/// <summary>M2-03: run the 10 §6.3 weather chain and report shares by season against the table, and beach temperatures.</summary>
public sealed class WeatherCommand : Command<WeatherSettings>
{
    private static readonly string[] Seasons = ["Spring", "Summer", "Autumn", "Winter"];

    public override int Execute(CommandContext context, WeatherSettings settings, CancellationToken cancellationToken)
    {
        var counts = new long[7, 6];
        var temps = new List<float>[4];
        for (var s = 0; s < 4; s++) { temps[s] = []; }
        var w = default(WeatherState);
        var slots = (long)settings.Years * 32 * 4;
        for (long slot = 0; slot < slots; slot++)
        {
            Weather.Advance(ref w, settings.Seed, slot);
            var minute = slot * Weather.SlotMinutes;
            var season = (Weather.DayOfYear(minute) - 1) / 8;
            counts[Weather.Regime(season, w.SummerType), (int)w.Sky]++;
            for (var h = 0; h < 6; h++) { temps[season].Add(Weather.AirTempC(w, minute + (h * 60), 3f, nearCoast: true)); }
        }

        var worst = 0.0;
        string[] names = ["Spring", "Summer", "Autumn", "Winter", "Summer (wet)", "Summer (dry)"];
        Console.WriteLine($"weather seed {settings.Seed}: {settings.Years} years, {slots} slots — share (target) per state");
        for (var r = 0; r < 6; r++)
        {
            long n = 0;
            for (var k = 0; k < 6; k++) { n += counts[r, k]; }
            if (n == 0) { continue; }
            var target = Weather.RegimeTarget(r);
            var cells = new List<string>();
            for (var k = 0; k < 6; k++)
            {
                var share = counts[r, k] / (double)n;
                worst = Math.Max(worst, Math.Abs(share - target[k]));
                cells.Add(string.Create(CultureInfo.InvariantCulture, $"{(Sky)k} {share:P1} ({target[k]:P0})"));
            }

            Console.WriteLine($"  {names[r],-13} n={n,7}  {string.Join("  ", cells)}");
        }

        for (var s = 0; s < 4; s++)
        {
            var t = temps[s];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {Seasons[s],-7} beach air °C  min {t.Min(),5:F1}  mean {t.Average(),5:F1}  max {t.Max(),5:F1}"));
        }

        var pass = worst <= 0.03;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"worst share error {worst * 100:F1} points (≤ 3 required) → {(pass ? "PASS" : "FAIL")}"));
        return pass ? 0 : 1;
    }
}
