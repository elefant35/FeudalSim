# ADR-0005 — Data layout: custom id-ordered domain tables, not an ECS library

> **Status:** Accepted · **Date:** 2026-10-03 · **Related:** [20 §6](../tech/20-architecture.md#6-data-layout-ecs), [ADR-0002](0002-headless-deterministic-sim-core.md)

## Context

The sim must be deterministic, hashable, saveable column by column, parallel over fixed chunks, and
readable by humans and AI assistants. Candidate ECS libraries (Arch, Friflo.Engine.ECS, DefaultEcs)
optimize archetype iteration but add their own ordering, storage and serialization rules.

## Decision

Each domain owns a **table**: dense, blittable struct columns in **ascending `EntityId` order**
(rows are appended with increasing ids; ids are never reused). Variable-size data lives in sparse
stores keyed by id. Iteration, hashing and saving all walk tables in that canonical order.

## Consequences

- Determinism and hashing are trivial (`StateHasher` hashes columns as bytes; M0-05).
- Saves are column-tolerant by construction (`SaveCodec` maps columns by name; M0-06).
- Parallel phases split rows into fixed 64-row chunks (`IJobScheduler`), independent of thread count.
- We write a little more plumbing per table than an ECS would need. Accepted.

## Revisit if

Profiling at 1,500 people (spike S6) shows table iteration, not system logic, dominating step time.
