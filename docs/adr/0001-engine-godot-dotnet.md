# ADR-0001 — Engine: Godot 4 (.NET / C#) for presentation

> **Status:** Accepted (confirmed by owner 2026-10-03) · **Date:** 2026-10-03
> **Related:** [ADR-0002](0002-headless-deterministic-sim-core.md), [canon §4](../01-canon.md#4-core-product--technology-decisions), [20-architecture](../tech/20-architecture.md)

## Context

FeudalSim is a single-player, third-person 3D game on PC (Windows, macOS, Linux) that must:

- render an 8 × 8 km region with stylized low-poly art, up to ~200 visible characters (150–300 in
  battle mode);
- host a large, deterministic social/economic simulation of up to 1,500 people that must also run
  **headless** (no renderer) for testing, balancing and Interludes;
- talk asynchronously to LLM and classifier services;
- be built by a very small team that leans heavily on AI-assisted development (Claude Code), where
  **text-based, diffable project files** are a real productivity advantage;
- leave ≤ 4 GB of VRAM headroom for a local LLM on recommended hardware.

The developer's machine is macOS with the .NET 8 SDK installed.

## Options considered

| Option | Pros | Cons |
|--------|------|------|
| **Godot 4.x .NET (C#)** | Free, MIT-licensed, no royalties. Scenes/resources are human-readable text (`.tscn`/`.tres`) — excellent for AI-assisted editing and code review. Fast iteration, small editor, first-class macOS. C# lets the sim core be a plain .NET library. Mature terrain plugin ecosystem (e.g. Terrain3D). | 3D tooling and large-world support less mature than Unity/Unreal; crowds need custom work (MultiMesh, animation LOD); smaller asset marketplace; C# export to web unsupported (irrelevant for PC). |
| **Unity 6 (C#)** | Proven for this genre (Valheim, Rust were built on Unity). Large asset store, DOTS/ECS for crowds, strong 3D tooling. Same C# sim core would work. | Licensing history (2023 runtime-fee episode) erodes trust; scenes/prefabs are GUID-heavy YAML that is hard to hand- or AI-edit; heavier editor; DOTS has a steep learning curve. |
| **Unreal Engine 5 (C++/Blueprint)** | Best-in-class visuals, World Partition, Mass Entity for crowds. | Heavy for a small team; C++ iteration speed; binary `.uasset` files are hostile to AI-assisted workflows and diffs; weaker macOS dev experience; photoreal strengths unused by our art style. |
| **Bevy (Rust ECS)** | ECS-native — ideal for simulation; fast; code-first. | Pre-1.0 with breaking changes each release; no mature editor; 3D tooling (terrain, animation, UI) immature; much more to build ourselves. |
| **Custom engine** | Total control. | Years of non-game work. Rejected. |

## Decision

Use **Godot 4.x with .NET (C#)** as the presentation layer (rendering, input, audio, UI, physics and
navmesh for embodied characters). Keep **all game rules in a pure C# .NET 8 simulation library with
zero Godot dependencies** ([ADR-0002](0002-headless-deterministic-sim-core.md)).

Pin a specific Godot 4.x .NET release at M0 and upgrade deliberately (not automatically).

## Consequences

- The engine is **replaceable**: the simulation, content and AI layers are engine-agnostic C#. A
  port to Unity would rewrite the client, not the game.
- We must build: crowd rendering (MultiMesh / animation LOD / impostors), world streaming for the
  terrain region, and a battle-mode renderer for 150–300 combatants. These are M1/M2 spikes.
- Godot's C# integration runs on .NET with a GC: hot paths in the client must be allocation-free;
  the sim core has the same rule.
- Single-precision floats are fine at our scale (8 × 8 km centred on the origin keeps every point
  within ~5.7 km of it); no large-world-coordinates build needed.

## Revisit if

- The M1 scale spike cannot render 150 animated characters at 60 fps on recommended hardware after
  reasonable optimization.
- Terrain/streaming for 8 × 8 km proves unworkable in Godot by end of M2.
- A console release becomes a goal (Godot console ports require third-party porting services).
