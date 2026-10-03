# ADR-0002 — A headless, deterministic simulation core

> **Status:** Accepted (confirmed by owner 2026-10-03) · **Date:** 2026-10-03
> **Related:** [ADR-0001](0001-engine-godot-dotnet.md), [canon §3 tenet 4, §8.2](../01-canon.md), [20-architecture](../tech/20-architecture.md)

## Context

The hardest questions in this design are systemic and long-horizon: *Does conflict actually emerge?
Does the economy stay stable over 20 years? Do 1,500 people starve, riot, or stagnate?* These
cannot be answered by playing — only by fast-forwarding society thousands of times without a
renderer. Interludes (multi-season time skips) need the same capability in the shipped game.
NPCs off-screen must keep living (pillar P1).

## Options considered

| Option | Pros | Cons |
|--------|------|------|
| **Game logic inside engine nodes/scripts** | Fastest to start; idiomatic for Godot. | Can't run headless at speed; logic tangled with rendering; hard to test; non-deterministic. |
| **Pure C# sim library, engine as a view** | Headless runs, unit tests, deterministic replays, engine independence, Interludes for free. | Requires discipline at the physics/animation boundary; some duplicated kinematics for headless mode. |
| **Separate sim process (client/server)** | Strong isolation; multiplayer-ready. | IPC overhead and complexity we don't need for single-player. |

## Decision

All game rules live in a **pure C# (.NET 8) simulation library** that:

1. has **no reference** to Godot or to any LLM provider (AI is reached through interfaces);
2. advances by **fixed ticks** on a game-minute clock, with per-LOD tick rates ([canon §8.2](../01-canon.md#82-simulation-levels-of-detail-canonical-tiers));
3. accepts **commands** (player input, client physical results, AI responses) and emits **events**;
4. draws all randomness from **seeded per-system RNG streams**;
5. records **LLM / Jev outputs as inputs** in the event log so a save + log replays identically
   on the same build + OS + CPU architecture (saves are portable across platforms; replays and
   golden tests are per platform — cross-platform bit-exactness is not required);
6. can run **headless** via a CLI runner that simulates years from a seed and emits metrics.

## Consequences

- Every system ships with headless tests and metrics; balancing becomes data-driven.
- The client must never mutate sim state directly — only via commands.
- LOD0 physical actions (navmesh movement, melee contact, projectile hits) are *intents* in the sim
  whose physical outcomes the client reports back as commands; in headless mode a simplified
  kinematic model substitutes. This boundary is the riskiest part of the architecture and is
  specified in [20-architecture](../tech/20-architecture.md).

## Revisit if

- The physics/animation boundary produces persistent desync bugs that cost more than they save.
- Multiplayer becomes a goal (then consider promoting the sim to a separate authoritative process).
