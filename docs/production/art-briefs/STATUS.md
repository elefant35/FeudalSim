# Art status log

The art agent keeps this current; the code agent reads it to wire assets in. Newest entries at the top of each section.

## Done (status: review, waiting for the owner's look approval)

| Date | Asset(s) | Files | Notes |
|------|----------|-------|-------|
| 2026-10-04 | P0.9 Handheld tools: all 7 | `game/assets/props/{flint_knife,stone_axe,iron_axe,iron_knife,hammerstone,flint_nodule,flint_flake}.glb`, `content/assets/tools_m2.yaml`, `art/generators/handtools/` | 64–840 tris, one palette, grip origin and +Z heads. Chipped flint, shaved hafts, supported lashing, forged axe eye. All checks pass (three tools use the explicit 1,200-triangle close-up allowance); close-up turntables/silhouettes inspected. |
| 2026-10-04 | P0.4 Trees: all 23 timber variants | `game/assets/flora/trees/`, `content/assets/trees_m2.yaml`, `art/generators/broadleaf/broadleaf.py` | Scots pine/oak ×4; birch/ash/beech/alder/willow ×3. 860–1,430 tris, one palette, COLOR_0 wind masks, trunk-only convex collision. 10.9–21 m species scales. All turntables and silhouette sheets inspected; willow simplified to fit budget. |
| 2026-10-04 | P0.6 Understory: all 17 variants | `game/assets/flora/bushes/`, `plants/`, `content/assets/understory_m2.yaml`, `art/generators/understory/` | Hazel ×3, bramble/gorse ×2, reeds ×3, grass ×4, fern ×3. 48–300 tris; grass 48, one palette, no collision, exported wind anchors/tips verified. Species contact sheets and silhouettes inspected. |
| 2026-10-04 | P0.1 Settlers: two complete modular libraries | `game/assets/characters/body_male.glb`, `body_female.glb`, `content/assets/characters_m2.yaml`, `art/generators/settlers/` | Shared 40-bone rig; 4 heads/sex × 10 expression keys, 6 hair styles/sex, 3 male beards, 6 garments/sex. Largest assembled selections 3,600 / 3,524 tris, 2 palette material variants; full libraries 5,792 / 5,580. Neutral shapes, expressions, turntables, 40 m silhouette and eye-camera previews inspected. Body heights 1.72 / 1.62 m. |
| 2026-10-04 | P0.2 Animations: 5 libraries / 27 clips | `game/assets/characters/anims/`, `content/assets/animations.yaml`, `art/generators/animations/` | All required names, 30 fps, static Root and horizontal Hips. Loop endpoints match within 1e-5. Sampled stance speed 1.600 / 4.000 / 6.500 m/s. Six work/needs eye-camera checks: at least one wrist in view in all 61 samples per clip; nearest wrist 0.200 m from camera. Third-person and first-person sheets inspected. Vertical posture offset exception documented below. |
| 2026-10-04 | P0.7 Camp: 12 assets | `game/assets/buildings/`, exact props paths, `content/assets/camp_m2.yaml`, `art/generators/camp/camp.py` | Fire ring with separate Logs/Ash, two shelters, bough bed, crate/barrel/sack/chest, water skin/bucket, firewood pile/bundle. 188–642 tris, palette, cloth wind masks, appropriate post/container collision. Repaired low lean-to supports and open bucket bands after visual QA; turntables inspected. |
| 2026-10-04 | P0.3 Terrain: all 8 layers / 16 packed maps | `game/assets/terrain/`, `content/assets/terrain.yaml`, `art/generators/terrain/` | 1024² RGBA; deterministic own-work, tile/channel/1.7 m walking previews inspected. Wrap-edge mean ratios 0.935–1.093; normal length error <0.008. Full content validation passes. |
| 2026-10-04 | P0.5 Ground: 18 variants | `game/assets/nature/rocks/`, `content/assets/nature_m2.yaml`, `art/generators/nature/nature.py` | 4 boulders, 4 fieldstones, 3 flint scatters, 3 driftwood, 4 outcrops. 80–168 visible tris, one palette; grounded, appropriate collision. Turntables inspected. |


## In progress

- 2026-10-04: P0.1 delivered; source work began with settlers first: shared skeleton authored in `art/generators/settlers/skeleton.py`; modular male/female bodies, heads, hair, beards and Varrow clothing under construction. P0.2 delivered on that skeleton in parallel. P0.3 terrain procedural generator prepared independently; lower priorities follow these deliveries.
- 2026-10-04: P0.4 tree variants and P0.7 camp family exported; visual review corrections underway before logging ready. P0.6 understory final visual pass. P0.8 wreck and P0.9 handheld tools are now under construction.
- 2026-10-04: Extended `tools/art/check.py` and `preview.py` for M2 object names, collision exclusion, modular character assembly, expression keys, palette UV centres, and animation-only GLBs. Legacy graybox check passes. Tooling committed as `24517a4`.

## Requests to the code agent

- Seasonally hide `bush_bramble_<variant>_berries` for bramble without fruit; berries export as separate meshes.

- Imported `COLOR_0.R` is a **wind weight**, not albedo. Disable standard-material vertex-color multiplication and let the wind shader read R separately; glTF generic materials otherwise darken/tint these models. The preview tool reconnects the palette directly for visual QA. Blender 5 exporter now explicitly exports active vertex colors, including cloth/foliage masks.

- Contract §6.1's literal named hierarchy has **40 bones total** (including non-deform Root and two sockets), despite saying 37 deform bones. We use the literal names/hierarchy, within the 45-bone cap, rather than omitting named bones.
- Grounded sit/lie/kneel clips need a vertical Hips posture offset. Root remains static and Hips has no horizontal travel; preserve vertical Hips animation while the game drives world motion. This interprets §6.3's in-place requirement without floating seated characters.
- Art briefs override §32's old 200-triangle tree minimum (brief minimum is 40), old skeleton/blockout naming, and hand-tool close-up cap (brief allows 1,200). Shared checker follows the M2 brief.

## Questions for the owner

None. All work uses deterministic own-work generators; no external licence or AI-service approval needed.
