# ADR-0010 — .NET version: net8.0 now, .NET 10 SDK next, net10 runtime when Godot allows

> **Status:** Accepted · **Date:** 2026-10-03 · **Related:** [canon §4](../01-canon.md#4-core-product--technology-decisions), [ADR-0001](0001-engine-godot-dotnet.md)

## Context

- .NET 8 LTS support ends **2026-11-10**. .NET 10 is the current LTS.
- Godot **4.7.2**'s `GodotSharp` targets **net8.0** (checked on NuGet, 2026-10-03); 4.8 is in dev.
- The dev machine has SDK **8.0.401** (Roslyn 4.11). Current packages increasingly ship analyzers
  built for newer compilers. During M0 this forced: `Microsoft.CodeAnalysis.BannedApiAnalyzers`
  3.3.4 (4.x/5.x need Roslyn 4.12+), and dropping `JsonSchema.Net.Generation` (its 7.1+ analyzer
  needs Roslyn 5.x; we wrote a small schema generator instead).

## Decision

1. **Target `net8.0`** for every project now (Godot 4.7.2 compatibility).
2. **Move the SDK to .NET 10** as soon as it's installed: a .NET 10 SDK builds `net8.0` targets, removes
   the analyzer/Roslyn pins, and stays supported. Update `global.json` then.
3. **Move the runtime target to `net10.0`** when the pinned Godot release supports it (or after a spike
   proves a `net10.0` game project works with Godot 4.7.x).

## Consequences

Until step 2, avoid packages whose analyzers need Roslyn > 4.11, or pin older versions and note why in
`Directory.Packages.props`.

## Revisit if

Godot drops net8.0, or a security issue forces leaving .NET 8 before step 3.
