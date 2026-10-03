using System.Text;
using FeudalSim.Sim.Content;

namespace FeudalSim.Content;

/// <summary>Renders ASSET_LICENSES.md from the asset manifest (32 §14). Deterministic output.</summary>
public static class AssetLicenses
{
    public static string Render(IReadOnlyList<AssetDef> assets)
    {
        var sb = new StringBuilder()
            .AppendLine("# Asset licenses")
            .AppendLine()
            .AppendLine("> Generated from `content/assets/*.yaml` by `feudalsim content licenses` — do not edit by hand.")
            .AppendLine("> Policy: docs/production/32-art-and-audio-production.md §14. The in-game credits are generated from the same data.")
            .AppendLine()
            .AppendLine("| Asset | Kind | Status | Source | License | Author | Credit |")
            .AppendLine("|-------|------|--------|--------|---------|--------|--------|");
        foreach (var a in assets.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var source = a.Source.Type switch
            {
                AssetSourceType.Generator => $"generator `{a.Source.Generator}`",
                AssetSourceType.External => $"external {a.Source.Url}",
                _ => a.Source.Type.ToString().ToLowerInvariant(),
            };
            sb.AppendLine($"| `{a.Id}` | {a.Kind.ToString().ToLowerInvariant()} | {a.Status.ToString().ToLowerInvariant()} | {source} | {a.License.Name} | {a.License.Author} | {(a.License.AttributionRequired ? a.License.Credit : "—")} |");
        }

        return sb.ToString();
    }
}
