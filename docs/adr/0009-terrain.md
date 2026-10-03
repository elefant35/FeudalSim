# ADR-0009 — Terrain: Terrain3D, with a custom chunked mesh as the fallback

> **Status:** Proposed — **pending the M0-12 terrain spike** (blocked until Godot 4.7.2 .NET is installed) · **Date:** 2026-10-03 · **Related:** [20 §12.4](../tech/20-architecture.md#124-terrain--world-streaming), spike S4

## Context

The world is 8,192 m square (canon §5.1). Godot has no built-in large-terrain system. Terrain3D
(GDExtension, MIT) offers clipmap LOD, region maps, texture layers and collision near the camera.

## Proposed decision

Use **Terrain3D** behind a typed C# facade, at 2 m vertex spacing, with the sim generating the
heightfield. Keep an **ArrayMesh + `HeightMapShape3D`** chunked terrain as the fallback.

## To decide in the spike (M0-12)

C# interop cost through `Call`/`Get`, macOS arm64 binaries for Godot 4.7.2, runtime import of
procedurally generated maps, VRAM at 2 m spacing, and ≥ 60 fps walking on the dev Mac with both
approaches. Results get written here and the status flips to Accepted or Rejected.
