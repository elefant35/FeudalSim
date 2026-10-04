using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.World;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Hosting;

/// <summary>
/// M2-02 (ADR-0009 §S4's open item): the generated region is cached on disk so a session doesn't regenerate it (≈ 17 s) —
/// `<dir>/<seed>/sim.world`, keyed by the content hash and the generator version (any change regenerates). The client
/// passes `user://worlds`; headless runs use `sim_runs/worlds`. A save carries its own copy of the grids (10 §3.2), so the
/// cache only ever saves time.
/// </summary>
public static class WorldCache
{
    public const string GeneratorVersion = "m2-01c";

    public static string DefaultDir => Environment.GetEnvironmentVariable("FEUDALSIM_WORLD_CACHE") is { Length: > 0 } d ? d : Path.Combine("sim_runs", "worlds");

    public static WorldMap GetOrGenerate(ContentDatabase content, string specId, ulong seed, string? dir = null, IJobScheduler? jobs = null)
    {
        var folder = Path.Combine(dir ?? DefaultDir, seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var file = Path.Combine(folder, "sim.world");
        var key = Path.Combine(folder, "sim.key");
        var expected = $"{GeneratorVersion} {content.Hash:x16} {specId}";
        if (File.Exists(file) && File.Exists(key) && File.ReadAllText(key) == expected)
        {
            try { return WorldMap.Decode(File.ReadAllBytes(file)); }
            catch (InvalidDataException) { /* stale or damaged: regenerate */ }
        }

        var spec = content.WorldSpec(specId) ?? throw new InvalidOperationException($"no world spec {specId}");
        var map = WorldMap.From(WorldGenerator.Generate(spec, seed, jobs));
        Directory.CreateDirectory(folder);
        var tmp = file + ".tmp";
        File.WriteAllBytes(tmp, map.Encode());
        File.Move(tmp, file, overwrite: true);
        File.WriteAllText(key, expected);
        return map;
    }
}
