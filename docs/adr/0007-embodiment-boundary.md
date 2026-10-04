# ADR-0007 — The embodiment boundary: the sim owns intent, Godot moves LOD0 bodies

> **Status:** Accepted (implemented and verified in M0-13, 2026-10-04) · **Date:** 2026-10-03 · **Related:** [20 §3](../tech/20-architecture.md#3-the-embodiment-boundary-lod0--headless-sim), [canon §4](../01-canon.md#4-core-product--technology-decisions)

## Decision

The sim is authoritative for everything **except the pose of LOD0 bodies**. For up to 48 embodied
NPCs, the sim publishes intents (where to go, which action, when), Godot's physics and navmesh move the
bodies, and the client reports results back as **logged `EmbodimentReport` commands**. Headless runs,
LOD1 puppets and battle crowds use one sim-side kinematic mover instead.

## Consequences

- Replays reproduce LOD0 sessions headless because body reports are in the input log (M0-13's test).
- The client must mirror the sim's pause and time scale (`Engine.TimeScale`), or bodies drift.
- Promotion/demotion between tiers needs reconciliation rules (owned by 21-npc-ai).

## Evidence (M0-13)

- **Sim side:** `LodSystem` promotes within 80 m and demotes beyond 100 m after 5 s (50 steps), capped at 48
  LOD0; `PlayerMoved` and `EmbodimentReport` are logged commands; the first report after promotion emits
  `Embodied` with the snap distance. `EmbodimentTests`: snap 0.4 m, demotion ≥ 250 steps, re-embodied
  snap < 1 m, headless replay identical.
- **Godot side:** `game/scenes/dev/EmbodimentSpike.tscn` — LOD0 NPCs become `CharacterBody3D` +
  `NavigationAgent3D` bodies snapped to the navmesh; others are MultiMesh puppets at the sim pose; the
  client submits the player pose and each body's pose once per sim step. The navmesh is baked from
  CPU-side heightfield triangles (`TerrainBuilder.NavigationFaces`), not by parsing the render mesh,
  which Godot warns stalls the GPU at runtime.
- **Check:** `tools/godot/embodiment_check.sh` (Godot `--headless` autotest: player walks 150 m out and
  back) → "PASS: Godot session and headless replay agree at step 698 (hash 46c45ef7e2e8defe);
  6 embodiments, 3 demotions, max snap 0.106 m". 3 settlers, 2,120 logged commands.

## Revisit if

Desync bugs at the boundary cost more than navmesh-in-sim would (20 §3.9 lists the alternative).
