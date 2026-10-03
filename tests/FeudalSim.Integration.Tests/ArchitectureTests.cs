using System.Reflection;

namespace FeudalSim.Integration.Tests;

/// <summary>
/// Enforces 20 §4.3: the simulation core references no engine, IO, network, YAML or AI assembly.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] AllowedSimReferencePrefixes =
    [
        "System.Runtime", "System.Collections", "System.Memory", "System.Linq", "System.Runtime.Intrinsics",
        "System.Numerics", "System.Threading", "System.IO.Hashing", "System.Text.Encoding.Extensions",
        "System.ComponentModel", "System.Buffers",
        "MessagePack", "Microsoft.Extensions.Logging.Abstractions", "netstandard", "mscorlib",
    ];

    private static readonly string[] ForbiddenSimReferencePrefixes =
    [
        "Godot", "GodotSharp", "System.Net", "YamlDotNet", "FeudalSim.AI", "FeudalSim.Content",
        "FeudalSim.Hosting", "System.Console", "System.IO.FileSystem",
    ];

    [Fact]
    public void Sim_references_only_allowed_assemblies()
    {
        var refs = typeof(FeudalSim.Sim.AssemblyMarker).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "").ToArray();

        var forbidden = refs.Where(r => ForbiddenSimReferencePrefixes.Any(f => r.StartsWith(f, StringComparison.Ordinal))).ToArray();
        forbidden.ShouldBeEmpty($"Sim must not reference: {string.Join(", ", forbidden)}");

        var unknown = refs.Where(r => !AllowedSimReferencePrefixes.Any(a => r.StartsWith(a, StringComparison.Ordinal))).ToArray();
        unknown.ShouldBeEmpty($"Sim references assemblies not on the allow-list (add an ADR first): {string.Join(", ", unknown)}");
    }

    [Fact]
    public void Content_and_AI_do_not_reference_each_other()
    {
        Name(typeof(FeudalSim.Content.AssemblyMarker).Assembly).ShouldNotContain("FeudalSim.AI");
        Name(typeof(FeudalSim.AI.AssemblyMarker).Assembly).ShouldNotContain("FeudalSim.Content");
    }

    private static string[] Name(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name ?? "").ToArray();
}
