# ADR-0004 — Single-player only for v1

> **Status:** Proposed · **Date:** 2026-10-03
> **Related:** [canon §4](../01-canon.md#4-core-product--technology-decisions), [02-game-overview §12](../02-game-overview.md#12-scope-goals-and-non-goals)

## Context

The vision describes the player as "one member in a town of AIs" and never mentions multiplayer.
Core features depend on things that are hard or impossible to share between several humans:
pausing, time skips (Interludes), LLM conversations with variable latency, a per-player knowledge
model (the player sees beliefs, not truth), and Lineage after death.

## Decision

FeudalSim v1 is **single-player only**. No co-op, no PvP, no dedicated servers.

## Consequences

- The game can pause, slow down and skip time freely.
- Conversation latency only affects one person and can be masked with UX.
- No netcode, anti-cheat or server costs.
- The command/event architecture of [ADR-0002](0002-headless-deterministic-sim-core.md) keeps a
  future authoritative-server model *conceivable*, but nothing will be built or compromised for it.

## Revisit if

- Post-launch demand for co-op is strong **and** a design exists for shared time and Interludes.
