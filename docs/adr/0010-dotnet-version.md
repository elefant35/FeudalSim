# ADR-0010 — .NET version: net8.0 now, .NET 10 SDK next, net10 runtime when Godot allows

> **Status:** Accepted · step 2 done 2026-10-04 · **step 3 done 2026-10-04** · **Date:** 2026-10-03 · **Related:** [canon §4](../01-canon.md#4-core-product--technology-decisions), [ADR-0001](0001-engine-godot-dotnet.md)

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

- ~~Until step 2, avoid packages whose analyzers need Roslyn > 4.11.~~ Step 2 is done (below), so current
  analyzers are fine again.
- Machines and CI need **two** installs: the .NET 10 SDK (builds) and the .NET 8 runtime (runs tests,
  the CLI and the Godot game, which all target net8.0). CI's `setup-dotnet` installs both.

## Step 2 — done 2026-10-04

- `global.json` → SDK **10.0.401**. `BannedApiAnalyzers` unpinned to **5.6.0** (an injected
  `System.Random` in the Sim still fails the build with RS0030).
- .NET 10's `dotnet test` no longer runs Microsoft.Testing.Platform apps (xunit.v3) through VSTest, so
  `global.json` opts in with `"test": { "runner": "Microsoft.Testing.Platform" }`; the VSTest-only
  packages (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) were removed. New syntax:
  `dotnet test --solution …`, `--project …`, xunit filters (`--filter-class`, `--filter-method`),
  `--report-xunit-trx --results-directory TestResults`.
- Evidence: Release build 0 warnings; 58/58 tests; content validate/schemas/licenses OK; smoke run
  final hash **8bea5171cd1c0ad3**, identical to the SDK 8 build; the Godot game builds and both
  headless Godot checks pass; CI green on ubuntu + macOS and `godot.yml` at 2b7c10f (runs 37165483972,
  37165483955). `JsonSchema.Net.Generation` stays dropped (our generator is smaller and
  deterministic).

## Step 3 — done 2026-10-04 (M1-25)

- Spike in a scratch worktree: `Directory.Build.props` and `game/FeudalSim.Game.csproj` → `net10.0`, Godot 4.7.2
  (`Godot.NET.Sdk/4.7.2`, GodotSharp built for net8.0 runs forward on .NET 10). The game's runtimeconfig names
  `Microsoft.NETCore.App 10.0.0`; Godot hosts it without changes.
- Evidence: Release build 0 warnings; 246/246 tests; smoke run hash **22e904d95294ad74** and `m1_camp` hash
  **ad3e70f63d9ab6ca** — identical to the net8.0 build (determinism scope holds across the runtime move); Godot boot
  autotest, `--autotest-dialogue` and `embodiment_check.sh` all PASS with no errors or warnings.
- Applied on main the same day: every project targets `net10.0`; CI installs only the .NET 10 SDK.
- Not yet verified: exported builds (export templates, self-contained .NET 10 runtime) — check at the first export (M2).

## Revisit if

Godot drops net8.0, or a security issue forces leaving .NET 8 before step 3.
