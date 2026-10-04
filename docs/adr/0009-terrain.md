# ADR-0009 — Terrain: Terrain3D, with a custom chunked mesh as the fallback

> **Status:** Proposed — **pending the M0-12 terrain spike** (blocked until Godot 4.7.2 .NET is installed) · **Date:** 2026-10-03 · **Related:** [20 §12.4](../tech/20-architecture.md#124-terrain--world-streaming), spike S4

## Context

The world is 8,192 m square (canon §5.1). Godot has no built-in large-terrain system. Terrain3D
(GDExtension, MIT) offers clipmap LOD, region maps, texture layers and collision near the camera.

## Proposed decision

Use **Terrain3D** behind a typed C# facade, at 2 m vertex spacing, with the sim generating the
heightfield. Keep an **ArrayMesh + `HeightMapShape3D`** chunked terrain as the fallback.

## Spike results so far (M0-12, 2026-10-04, dev Mac, Godot 4.7.2 .NET)

**Approach B — ArrayMesh + `HeightMapShape3D` (built-in, no plugin): works.** A sim-generated
(`Sim.World.Heightfield`, keyed value-noise fBm) 257 × 257 heightfield at 2 m spacing = 512 m square,
131,072 triangles: heights 61 ms, mesh 27 ms, collision 6 ms. A `CharacterBody3D` capsule walked it
for 16 s with vsync off at **min 120 / avg 120 fps** (likely the display cap), staying on the floor.
`HeightMapShape3D` scaled by the spacing lines up with the render mesh.

**What it doesn't answer:** the full 8,192 m region at 2 m is 4,097² ≈ 16.8 M samples, so a single
mesh is out; the fallback would need chunking plus distance LOD (clipmaps or per-chunk decimation),
which Terrain3D provides out of the box.

**Approach A — Terrain3D: not yet evaluated.** It is a third-party native GDExtension (MIT) that has
to be downloaded from its GitHub releases; waiting for the owner's OK to download it.

## To decide in the spike (M0-12)

C# interop cost through `Call`/`Get`, macOS arm64 binaries for Godot 4.7.2, runtime import of
procedurally generated maps, VRAM at 2 m spacing, and ≥ 60 fps walking on the dev Mac with both
approaches. Results get written here and the status flips to Accepted or Rejected.
