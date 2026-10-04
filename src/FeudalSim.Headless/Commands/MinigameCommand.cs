using System.ComponentModel;
using System.Globalization;
using System.Text;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Crafting.Minigames;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class MinigameSettings : CommandSettings
{
    [CommandOption("--check")]
    [Description("Fail if content/minigames/knapping.yaml is not what a fresh fit produces.")]
    public bool Check { get; init; }

    [CommandOption("--samples <N>")]
    [Description("Plays per band for the target checks (the fit itself always uses 4,000).")]
    public int Samples { get; init; } = 4000;
}

/// <summary>13 §13.3: fit the knapping calibration curves from headless bots and check the targets.</summary>
public sealed class MinigameCommand : Command<MinigameSettings>
{
    private static readonly string[] Stages = ["choose", "rough", "thin", "pressure"];

    public const int FitSamples = 4000;

    public override int Execute(CommandContext context, MinigameSettings settings, CancellationToken cancellationToken)
    {
        var root = RepoPaths.FindContentRoot(Directory.GetCurrentDirectory());
        var path = Path.Combine(root, "minigames", "knapping.yaml");
        var def = Calibration.FitKnapping(FitSamples);   // fixed: the shipped curve is always this fit (--samples only sizes the checks)
        var yaml = Yaml(def);
        if (settings.Check)
        {
            // Values, not text: libm differs across OS/arch (ADR-0002's scope), so compare within the 4-decimal rounding.
            var current = File.Exists(path) ? Numbers(File.ReadAllText(path)) : [];
            var fresh = Numbers(yaml);
            var worst = current.Length != fresh.Length ? float.PositiveInfinity : current.Zip(fresh).Select(p => MathF.Abs(p.First - p.Second)).DefaultIfEmpty(0f).Max();
            if (worst > 2e-3f) { Console.WriteLine($"minigame: {path} is stale (largest difference {worst:G3}) — run `feudalsim minigame`"); return 1; }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"minigame: knapping curves up to date (largest difference {worst:G3})"));
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, yaml);
            Console.WriteLine($"minigame: wrote {path}");
        }

        // 12 §6.5 (the hard requirement, gates the exit code): under the shipped curve an attentive player's m has median
        // ≈ 0 and quartiles ≈ ±0.35 in every grip band. Practiced and novice are proposals (13 Q11): reported, not gated.
        var shipped = settings.Check ? Load(root) ?? def : def;
        var ok = true;
        Console.WriteLine("12 §6.5 attentive per band (median ∈ ±0.05, quartiles within 0.08 of ∓0.35) · 13 §13.3 proxies (practiced median 0.25–0.45, m ≥ 0.9 < 5 %; novice ≈ −0.3):");
        for (var stage = 0; stage < Stages.Length; stage++)
        {
            var bands = new List<string>();
            var stageOk = true;
            for (var band = 0; band < Calibration.Bands; band++)
            {
                var ms = Calibration.Sample(BotPlayer.Attentive, stage, band, settings.Samples / 2, salt: 9)
                    .Select(x => Calibration.M(shipped.Stages[stage], x.Band, x.Raw, x.Catastrophic)).OrderBy(x => x).ToArray();
                var (q25, q50, q75) = (Calibration.Quantile(ms, 0.25), Calibration.Quantile(ms, 0.5), Calibration.Quantile(ms, 0.75));
                var pass = MathF.Abs(q50) <= 0.05f && MathF.Abs(q25 + 0.35f) <= 0.08f && MathF.Abs(q75 - 0.35f) <= 0.08f;
                stageOk &= pass;
                bands.Add(string.Create(CultureInfo.InvariantCulture, $"{q25:F2}/{q50:F2}/{q75:F2}{(pass ? "" : "!")}"));
            }

            ok &= stageOk;
            var pra = Calibration.Ms(shipped, BotPlayer.Practiced, stage, settings.Samples / 4);
            var nov = Calibration.Ms(shipped, BotPlayer.Novice, stage, settings.Samples / 4);
            var top = pra.Count(m => m >= 0.9f) / (double)pra.Length;
            var proxies = Calibration.Quantile(pra, 0.5) is >= 0.25f and <= 0.45f && top < 0.05 && Calibration.Quantile(nov, 0.5) is >= -0.45f and <= -0.15f;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {Stages[stage],-8} attentive q25/q50/q75 by band [{string.Join(" ", bands)}] {(stageOk ? "PASS" : "FAIL")} · practiced median {Calibration.Quantile(pra, 0.5):F2} (m ≥ 0.9: {top:P1}) · novice median {Calibration.Quantile(nov, 0.5):F2} → proxies {(proxies ? "PASS" : "MISS (13 Q11)")}"));
        }

        Console.WriteLine($"minigame: 12 §6.5 attentive calibration {(ok ? "PASS" : "FAIL")}");
        return ok ? 0 : 2;
    }

    private static float[] Numbers(string yaml)
        => [.. System.Text.RegularExpressions.Regex.Matches(yaml, @"-?\d+\.\d+").Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture))];

    private static MinigameDef? Load(string contentRoot) => ContentCompiler.Compile(contentRoot).Database?.Minigame("minigame.knapping");

    public static string Yaml(MinigameDef def)
    {
        var sb = new StringBuilder();
        sb.Append("# yaml-language-server: $schema=../schemas/minigame.schema.json\n");
        sb.Append("# GENERATED by `feudalsim minigame calibrate` (13 §13.3): per stage and grip band (g in fifths), the attentive bot's\n");
        sb.Append("# raw-score quantiles at 2.5/10/25/50/75/90/97.5 %. CI runs `--check`; refresh from playtest telemetry later (13 §20).\n");
        sb.Append(CultureInfo.InvariantCulture, $"- id: {def.Id}\n  recipe: {def.Recipe}\n  stages:\n");
        foreach (var s in def.Stages)
        {
            sb.Append(CultureInfo.InvariantCulture, $"    - stage: {s.Stage}\n      bands:\n");
            foreach (var b in s.Bands) { sb.Append("        - [").Append(string.Join(", ", b.Select(x => x.ToString("0.0###", CultureInfo.InvariantCulture)))).Append("]\n"); }
        }

        return sb.ToString();
    }
}
