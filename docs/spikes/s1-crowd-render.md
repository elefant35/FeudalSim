# Spike S1 — Crowd render (M1-S1)

**Date:** 2026-10-04 · **Machine:** Apple M3 Pro, 18 GB (proxy for canon §8.3's recommended "Apple M-series 32 GB")
· **Godot** 4.7.2 .NET, Forward+ · **Scene:** `res://scenes/dev/CrowdSpike.tscn` (`-- --count N | --battle
--seconds S [--shot out.png]`) · **Character:** `game/assets/characters/humanoid_a.glb` from
`art/generators/humanoid` — 3,648 triangles, 1 palette material, 17-bone skeleton with rigid skinning, keyframed
in-place walk (1 s) and idle (2 s) cycles; passes `tools/art/check.py` (character budget 2,500–4,000).

**Question (30 M1-S1):** 150 animated low-poly characters at 60 fps on recommended spec, 300 in battle mode ≥ 30 fps
(canon §8.2: ≤ 200 visible at 60 fps; battles 150, stretch 300).

## Results (uncapped, 20 s after a 3 s warm-up; shadowed sun, 400 m ground plane)

| Case | Mean fps | Frame ms p50 / p95 / p99 / max | Frames > 16.7 ms | Draw calls | Primitives |
|------|----------|--------------------------------|------------------|------------|------------|
| Village, 150 (⅔ walking, ⅓ idle) | 878 | 1.13 / 1.26 / 1.39 / 7.0 | 0.00% | 237 | 83,530 |
| Village, 200 (canon cap) | 716 | 1.39 / 1.52 / 1.52 / 5.2 | 0.00% | 312 | 110,530 |
| Battle, 300 packed in 60 m, all walking | 405 | 2.45 / 2.62 / 2.78 / 12.2 | 0.00% | 802 | 546,922 |

**PASS** on the reference Mac with ≈ 6× (battle) to 12× (village) frame-time headroom. Godot's import-time mesh
LODs drop distant characters' triangles (village primitives ≈ 0.15× the full count).

## Caveats

- The character is a blockout with one material; the 32 §7 modular settler (body + head + hair + 3–5 clothing layers,
  up to 3 materials, blend shapes) will cost several times more draw calls. Re-measure at M2 with real characters,
  the camp scene (M1-18) and terrain (M1-S4) together, and on the minimum spec (Apple M1 16 GB / GTX 1660).
- No foot IK, animation blending, or per-character logic beyond a circular walk; the sim's snapshot path is
  measured separately (S6).
