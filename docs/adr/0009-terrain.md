# ADR-0009 — Terrain: Terrain3D, with a custom chunked mesh as the fallback

> **Status:** Accepted (M0-12 spike, 2026-10-04) · **Date:** 2026-10-03 · **Related:** [20 §12.4](../tech/20-architecture.md#124-terrain--world-streaming), spike S4, [ADR-0007](0007-embodiment-boundary.md)

## Context

The world is 8,192 m square (canon §5.1). Godot has no built-in large-terrain system. Terrain3D
(GDExtension, MIT) offers clipmap LOD, region maps, texture layers and collision near the camera. The
M0 spike (20 §20 step 12) rendered one sim-generated heightfield both ways and walked a capsule on it.

## Decision

1. Use **Terrain3D v1.0.2** at **2 m vertex spacing** with **1,024-vertex regions (2,048 m)**, so the
   8,192 m world centred on the origin is 4 × 4 regions. The **sim generates the heightfield**; the
   client imports it with `Terrain3DData.import_images` (height + colour map) at new-game.
2. Reach Terrain3D only through a **typed C# facade** (it has no C# bindings — `ClassDB.Instantiate`,
   `Get`/`Set`/`Call` by name), used at setup and for edits, never per frame. **Gameplay height queries
   stay in C# on the sim heightfield**, which the sim owns anyway.
3. **Pin and fetch** the addon: `tools/godot/addons.json` (version, URL, SHA-256, licence) and
   `tools/godot/fetch_addons.sh`; native binaries are not committed (Git LFS bandwidth). CI fetches it
   with a cache and runs a headless check.
4. Keep the **ArrayMesh + `HeightMapShape3D`** path (`TerrainBuilder`) as the fallback and for dev scenes.

## Spike results (2026-10-04, M3 Pro MacBook, Godot 4.7.2 .NET)

Same seed-42 heightfield (`Sim.World.Heightfield`), same capsule autowalk (6.5 m/s circle, 16 s
measured after 3 s warm-up), vsync off. Frame rate is capped at **120 fps by the display** in every
run, so GPU frame time is the comparison; it was measured with `--rendering-driver vulkan` (MoltenVK)
because Godot's Metal backend reports 0 for viewport GPU timestamps. Terrain3D has one near-white
detail texture tinted by the palette colour map — the same look as the ArrayMesh vertex colours
(screenshots compared).

| | ArrayMesh + HeightMapShape3D, 512 m | Terrain3D, 512 m | Terrain3D, **8,192 m** |
|---|---|---|---|
| Setup | heights 63 ms · mesh 29 ms · collision 6 ms | heights 62 · images 8 · node 67 · import 4 ms | heights **15.7 s** (sim, 1 thread) · images 1.1 s · node 0.1 s · import 0.56 s |
| GPU frame avg / max | **1.64 / 1.78 ms** | 2.22 / 2.50 ms | 4.46 / 6.09 ms |
| Worst frame (after warm-up) | 8.6 ms | 8.3 ms | 9.1 ms |
| VRAM total (textures) | 77 MB (47) | 81 MB (54) | **301 MB (268)** |
| Draw calls · primitives | 11 · 673 k | 50 · 314 k | 49 · 314 k |
| Heights vs sim heightfield | exact (same data) | **0.000 m** max error (64 points) | **0.000 m** |
| Capsule on floor | yes | yes (dynamic collision) | yes |

- **C# interop:** `get_height` through `Call` costs **0.89–0.98 µs** per call vs **13–44 ns** for the
  C# array lookup (~30–70×). Fine for setup and edits, wrong for hot paths → decision 2.
- **Compatibility:** loads and runs on Godot **4.7.2** (Metal and Vulkan), although upstream only
  lists 4.4–4.6. One deprecation warning, `instance_reset_physics_interpolation() is deprecated`,
  when a Terrain3D node is created — a version-coupling risk for the next Godot minor.
- **8 km fits the budget:** 301 MB total VRAM for the whole scene vs the **450 MB terrain line** of
  the 20 §19 VRAM plan (maps at 2 m ≈ 3 × 64 MB plus mips); texture arrays for real materials will add
  tens of MB at 1,024² BC7. GPU 4.5 ms leaves most of a 16.7 ms (60 fps) frame.
- The fallback is cheaper at 512 m but is a single mesh; reaching 8 km needs chunking, LOD, seams and
  per-chunk collision that Terrain3D already provides.

### Integration gotchas found (all handled in `TerrainSpike.cs`, checked in CI)

1. **`import_images` snaps to the region grid.** Its position is in metres, but each slice goes into
   the region *containing* it (`terrain_3d_data.cpp`), so a map corner that isn't on the region grid
   shifts the whole map (we saw 21 m height errors). Keep the map corner on a region boundary
   (8 km: −4,096 m is a multiple of 2,048 m ✓); import `(n−1)²` samples so the map is whole regions.
2. **`region_size` only applies after the node is in the tree**, via `change_region_size`; setting the
   property before `AddChild` is silently ignored.
3. **`free_editor_textures` must be false for runtime-built assets.** In-game, Terrain3D reloads its
   assets from their file path on entering the tree; generated assets have no path, so they were
   dropped (`load("")` error) and the terrain fell back to the debug checkerboard.
4. With **no texture asset**, the shader shows the checkerboard and ignores the colour map.

## Consequences

- World generation must produce the heightfield faster: **15.7 s single-threaded for 8 km** in the
  sim's fBm. 20 §12.4 allows ≤ 60 s once per world (cached), but generation should be chunked and
  parallel (fixed 64-row chunks keep it deterministic) — M1/M2 world-gen work.
- The facade owns the gotchas above, the camera/collision target, and import/caching under
  `user://worlds/<seed>/`.
- Shipping builds must carry Terrain3D's MIT notice (Godot export filters drop `LICENSE.txt` by
  default) — M8 release checklist.
- S4 (streaming the full map with no hitch > 50 ms) continues in M1 on Terrain3D: this spike
  loaded 8 km in one go and walked near the origin; walking/running across regions is still to measure.

## S4 — streaming the full map (M1-S4, 2026-10-04)

- **Typed facade** `game/scripts/World/Terrain3DFacade.cs` owns every name-based call and the four gotchas above;
  `TerrainSpike` uses it (the `--check` CI step goes through it).
- **Parallel, chunked heightfield:** `Heightfield.Generate(..., jobs)` in fixed 64-row chunks on the Hosting job runner;
  bit-identical to serial (`HeightfieldTests`). 8 km: **15.7 s → 2.7–2.9 s** on 10 threads (M3 Pro).
- **World cache** `user://worlds/<seed>/terrain` (Terrain3D region files, 60 MB for 8 km): first session imports
  (479 ms) and saves (342 ms); later sessions load in **307–359 ms** with heights still matching the sim (0.000 m).
- **Traverse** (`--traverse 150`): a serpentine of 38.4 km through all **16/16 regions** at 150 m/s (a stress pace,
  ~23× sprint), 256 s, 124,875 frames: p50 2.04 ms, p99 2.11, p99.9 2.38, **max 13.0 ms — 0 hitches > 50 ms** (0 > 33 ms);
  VRAM **320 MB** (textures 260) inside the 450 MB terrain line. **S4 PASS** on the reference Mac; re-measure with real
  terrain materials (M2) and on minimum spec.
- Still open: the sim heights are regenerated each session (2.8 s) even when Terrain3D loads from cache — cache the
- *Closed (M2-02):* the sim world is cached too — `WorldCache` writes `user://worlds/<seed>/sim.world` (versioned codec,
  keyed by generator version and content hash); a cached session loads it in under 3 s instead of regenerating (≈ 17 s).
  sim heightfield beside it when world generation lands (M2, 10).

## Revisit if

Terrain3D stops loading on the pinned Godot (the deprecation above becomes a removal), S4 shows
hitches > 50 ms crossing regions, VRAM on minimum spec exceeds the 450 MB terrain line, or the
project goes unmaintained. The fallback (64 m chunk meshes with LOD + per-chunk `HeightMapShape3D`)
stays viable because the sim, not the plugin, owns the heights.
