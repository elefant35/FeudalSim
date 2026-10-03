# ADR-0007 — The embodiment boundary: the sim owns intent, Godot moves LOD0 bodies

> **Status:** Accepted (implementation in M0-13, pending Godot) · **Date:** 2026-10-03 · **Related:** [20 §3](../tech/20-architecture.md#3-the-embodiment-boundary-lod0--headless-sim), [canon §4](../01-canon.md#4-core-product--technology-decisions)

## Decision

The sim is authoritative for everything **except the pose of LOD0 bodies**. For up to 48 embodied
NPCs, the sim publishes intents (where to go, which action, when), Godot's physics and navmesh move the
bodies, and the client reports results back as **logged `EmbodimentReport` commands**. Headless runs,
LOD1 puppets and battle crowds use one sim-side kinematic mover instead.

## Consequences

- Replays reproduce LOD0 sessions headless because body reports are in the input log (M0-13's test).
- The client must mirror the sim's pause and time scale (`Engine.TimeScale`), or bodies drift.
- Promotion/demotion between tiers needs reconciliation rules (owned by 21-npc-ai).

## Revisit if

Desync bugs at the boundary cost more than navmesh-in-sim would (20 §3.9 lists the alternative).
