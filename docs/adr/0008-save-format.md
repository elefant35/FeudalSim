# ADR-0008 — Saves: column-tolerant MessagePack + LZ4 snapshots with an input log

> **Status:** Accepted · **Date:** 2026-10-03 · **Related:** [20 §9](../tech/20-architecture.md#9-commands-events--persistence)

## Decision

A save is a **snapshot** (header + column-tolerant tables, MessagePack, LZ4) plus an append-only
**input log** of every applied command (`[u32 length][u32 crc32][msgpack]` records). Columns are
mapped by name: missing columns take registered defaults, unknown ones are dropped with a warning,
and a layout-fingerprint test forces a `LayoutVersion` bump when a persisted struct changes.
Snapshots are written atomically (temp file → flush → rename). Autosave daily at 06:00 game time.

**v0 (M0)** writes the snapshot as one LZ4 MessagePack document; the chunked layout with a TOC and
per-chunk hashes (20 §9.4) lands in M3, before saves need to survive between milestones.

## Evidence (M0-06, 1,500 people, Apple Silicon, .NET 8.0.8)

| | Serialize | Deserialize | Size |
|---|---|---|---|
| MessagePack | 56 µs | 33 µs | 203 KB |
| MemoryPack | 57 µs | 26 µs | 216 KB |
| MessagePack + LZ4 | 141 µs | 71 µs | **59 KB** |

The two serializers are equivalent on our column blobs, so MessagePack wins on versioned keys and
Python readability. LZ4 cuts size 3.5× for ~85 µs. Save/load mid-run reproduces a straight run's
state hash exactly; a torn log tail is recovered.

## Revisit if

Save capture at full scale exceeds 100 ms on the sim thread (20 §19).
