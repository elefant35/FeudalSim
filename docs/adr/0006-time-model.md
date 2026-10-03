# ADR-0006 — Time model: a 100 ms step, two clocks, eleven day lengths

> **Status:** Accepted · **Date:** 2026-10-03 · **Related:** [canon §6](../01-canon.md#6-time-canon), [20 §5](../tech/20-architecture.md#5-time-clocks--ticks)

## Decision

- The sim advances in **fixed 100 ms steps of embodied time** (10 Hz). `Step` (monotonic count) orders
  everything; `GameMs` is game time since `Y0 Spring 1 00:00`. Every stored timestamp is a whole
  **game-minute**.
- Game-ms per step = **144,000 / day length**, so the day-length setting is limited to
  **{20, 24, 25, 30, 32, 36, 40, 45, 48, 50, 60}** real minutes (all give whole game-ms per step). The
  game-minutes-per-real-minute *ratio* is fractional for 25 and 50 (57.6, 28.8), so it is display-only.
- Changing day length is a logged command; time scale (pause, hurry) is not a sim input.
- **Focus time** (12:1 during conversations, court and battle) is a logged clock-ratio change that keeps
  10 Hz stepping, so decision-point deadlines stay fixed step counts.

## Evidence (M0)

18,000 steps = 1 game day at 30 minutes; all eleven day lengths verified; the real-time runner held
**10.00 steps/s for 60 s** with no dilation (M0-04, M0-09).

## Revisit if

Melee needs a finer step than 100 ms (decide after the M2 combat playtest).
