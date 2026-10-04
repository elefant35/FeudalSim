using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.WorldGen;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class WorldGenSettings : CommandSettings
{
    [CommandOption("--spec <ID>")]
    public string Spec { get; init; } = "worldspec.farstrand_default";

    [CommandOption("--seed <N>")]
    public ulong Seed { get; init; } = 42;

    [CommandOption("--seeds <N>")]
    [Description("Generate seeds seed…seed+N−1 and summarise the bands.")]
    public int Seeds { get; init; } = 1;

    [CommandOption("--threads <N>")]
    public int Threads { get; init; } = Environment.ProcessorCount;

    [CommandOption("--strict")]
    [Description("Fail on any 10 §3.11 assert (default: the completed stages' W1/W5; W2/W3 are reported).")]
    public bool Strict { get; init; }

    [CommandOption("--png <PATH>")]
    [Description("Write a preview of the first world.")]
    public string? Png { get; init; }
}

/// <summary>M2-01: generate worlds (10 §3) and report the §3.11 asserts, retries and stage timing; optionally a PNG preview.</summary>
public sealed class WorldGenCommand : Command<WorldGenSettings>
{
    public override int Execute(CommandContext context, WorldGenSettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var spec = content.WorldSpec(settings.Spec) ?? throw new InvalidOperationException($"no world spec {settings.Spec}");
        using var jobs = new JobRunner(settings.Threads);
        var ok = 0;
        var full = 0;
        for (var k = 0; k < settings.Seeds; k++)
        {
            var seed = settings.Seed + (ulong)k;
            var sw = Stopwatch.StartNew();
            var w = WorldGenerator.Generate(spec, seed, jobs);
            var ms = sw.Elapsed.TotalMilliseconds;
            if (settings.Strict ? w.Valid : w.CoreValid) { ok++; }
            if (w.Valid) { full++; }
            var lith = Enumerable.Range(0, 7).Select(l => w.Grid.Lithology.Count(b => b == l)).ToArray();
            var landCells = Math.Max(1, lith.Skip(1).Sum());
            if (w.Water is { } hy)
            {
                var outlets = hy.Rivers.Select(r => r.CatchmentKm2).OrderDescending().ToList();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"   outlets ≥1 km² {hy.Outlets.Count(o => o >= 1)} · ≥2 {hy.Outlets.Count(o => o >= 2)} · ≥4 {hy.Outlets.Count(o => o >= 4)} · top {string.Join(", ", hy.Outlets.Take(6).Select(o => o.ToString("F1", CultureInfo.InvariantCulture)))} · drained by <1 km² outlets {hy.Outlets.Where(o => o < 1).Sum() / Math.Max(0.001f, hy.Outlets.Sum()):P0}"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"   water: lakes {hy.Lakes.Count} ({string.Join(", ", hy.Lakes.Select(l => $"{l.AreaHa:F1} ha"))}) · river outlets {outlets.Count} (km²: {string.Join(", ", outlets.Take(8).Select(a => a.ToString("F1", CultureInfo.InvariantCulture)))}) · fords {hy.Fords.Count} · springs {hy.Springs.Count}"));
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"seed {seed}: {(w.Valid ? "VALID" : "INVALID")} attempt {w.Attempt} · {w.Archetype} · land {w.LandAreaKm2:F1} km² · islets {w.Islets} · peak {w.PeakM:F0} m · above 800 m {w.AreaAbove800Km2:F2} km² · {ms:F0} ms total · rock {string.Join(" ", Enum.GetNames<Lithology>().Skip(1).Select((name, j) => $"{name[..3].ToLowerInvariant()} {100.0 * lith[j + 1] / landCells:F0}%"))}{(w.Biomes is { } bio ? " · biomes " + string.Join(" ", bio.Shares.Select(kv => $"{Biomes.Keys[(int)kv.Key]} {kv.Value:P0}")) + $" · breadbasket {bio.BreadbasketHa:F0} ha" : "")}{(w.Valid ? "" : " · " + string.Join("; ", w.Failures))}"));
            if (k == 0 && settings.Png is { } png)
            {
                File.WriteAllBytes(png, WorldPreview.Png(w.Grid));
                File.WriteAllBytes(Path.ChangeExtension(png, ".lithology.png"), WorldPreview.LithologyPng(w.Grid));
                File.WriteAllBytes(Path.ChangeExtension(png, ".biomes.png"), WorldPreview.BiomePng(w.Grid));
                Console.WriteLine($"worldgen: wrote {png} (+ .lithology.png, .biomes.png)");
            }
        }

        Console.WriteLine($"worldgen: {ok}/{settings.Seeds} pass {(settings.Strict ? "all asserts" : "W1/W5 (stages 1–4)")} · {full}/{settings.Seeds} pass every assert incl. W2/W3/W4");
        return ok == settings.Seeds ? 0 : 1;
    }
}
