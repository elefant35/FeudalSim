# Technical contract for art (M2 first playable)

Follow this exactly and the game loads your files with no code changes. Anything you can't follow: say so in
`STATUS.md` under "Requests to the code agent" instead of improvising.

## 1. Units, axes, origin

| Rule | Value |
|------|-------|
| Units | 1 Blender unit = **1 metre** |
| Up | Blender **Z-up**; the glTF exporter converts to Godot's Y-up (use the repo exporter, §7) |
| Forward | Characters, animals and directional props face **Blender −Y** (the game turns them; verified in Godot 4.7.2 on the M2 settlers, 2026-10-04) |
| Origin | Base centre, on the ground plane (z = 0). Handheld items: origin at the **grip**, blade/head pointing Blender +Z |
| Human scale | Adults 1.60–1.85 m (use 1.72 m for the base male, 1.62 m for the base female); doors 2.0 m; hand tools 0.2–0.9 m |
| Apply | Apply all transforms (scale 1, rotation 0) before export |

## 2. Look and the palette

- **Stylized low-poly, flat shaded.** Hard edges on everything except organic silhouettes (faces, animals,
  tree canopies may smooth).
- **One shared palette texture:** `art/palettes/palette.png` (256 × 256, an 8 × 8 grid of swatches;
  names and UV centres in `art/palettes/palette.json`). Colour models by **collapsing each face's UVs onto a
  swatch cell**, not with per-object colours. Rows: `earth`, `foliage`, `stone_metal`, `skin_hair`, `cloth`,
  `culture`, `water_sky`, `special`.
- **≤ 2 materials per prop** (the palette material + one optional special). The palette material is named
  `palette`. Characters may use 3 (palette, a ≤ 512² pattern atlas for cloth, optional eyes).
- **Need a colour the palette lacks?** Don't add stray textures. Ask in `STATUS.md`. The owner of the palette is
  `tools/art/make_palette.py`, and you may propose new swatches there; the 8 × 8 grid is full, so a change means a
  bigger grid, which the code agent coordinates.
- **Seasons:** foliage uses only the `foliage` row, so a shader can shift it per season. Don't bake autumn colours
  into a separate mesh.
- **Wind:** on foliage, cloth and grass, paint **vertex colour R = wind weight** (0 at the trunk or anchor, 1 at
  the leaf tips or hem). The code agent's shader reads it.
- **Silhouette test:** characters and animals must read at 40 m as a black silhouette (the preview tool renders
  it).

## 3. Budgets (LOD0 triangles; checked by `tools/art/check.py`)

| Class (`budget_class`) | LOD0 tris | Materials |
|------------------------|-----------|-----------|
| `character` (body + clothes + hair, assembled) | 2,500–4,000 | ≤ 3 |
| `animal_small` (hare, fowl) | 300–800 | 1 |
| `animal_medium` (deer, boar, wolf, goat) | 1,200–2,500 | ≤ 2 |
| `tree` | 40–1,500 | ≤ 2 |
| `shrub` (bushes, herbs, plants) | 30–300 | 1 |
| `rock` | 50–400 | 1 |
| `prop_handheld` | 50–600 | ≤ 2 |
| `workstation` (campfire, rack, knapping stone) | 300–2,000 | ≤ 2 |
| `building_small` (lean-to, shelter, hut) | 300–1,500 | ≤ 3 |
| `building_medium` (longhouse, wreck sections) | 1,500–4,000 | ≤ 3 |

Only LOD0 is required. Godot generates mesh LODs on import; don't hand-author LODs unless a preview shows the
automatic ones breaking the silhouette. **First-person hand items** (§6.4) may use up to 1,200 tris because they fill
the screen.

## 4. Collision

Use Godot's import-name suffixes on extra mesh objects inside the same `.glb`:

- `-colonly`: an invisible collision mesh (buildings, the wreck, large rocks).
- `-convcolonly`: an invisible convex hull (trunks, boulders, crates). Prefer this; it is cheapest.
- Trees: one convex hull around the **trunk only** (people walk under canopies). Bushes, herbs and grass: **no
  collision**.

## 5. Naming and paths

Objects inside a file: `<category>_<name>_<variant>` (for example `tree_oak_a`), collision objects with the suffix.
Files land under `game/assets/`:

| What | Path | Example |
|------|------|---------|
| Trees | `game/assets/flora/trees/<node>_<variant>.glb` | `game/assets/flora/trees/oak_a.glb` |
| Bushes | `game/assets/flora/bushes/<node>_<variant>.glb` | `game/assets/flora/bushes/hazel_a.glb` |
| Plants, herbs, fungi | `game/assets/flora/plants/<node>_<variant>.glb` | `game/assets/flora/plants/death_cap_a.glb` |
| Rocks and ground nodes | `game/assets/nature/rocks/<node>_<variant>.glb` | `game/assets/nature/rocks/boulder_b.glb` |
| Terrain textures | `game/assets/terrain/<layer>_albedo_height.png`, `<layer>_normal_rough.png` | `sand_albedo_height.png` |
| Camp and buildings | `game/assets/buildings/<name>.glb` | `game/assets/buildings/sailcloth_shelter.glb` |
| Props and items | `game/assets/props/<name>.glb` | `game/assets/props/flint_knife.glb` |
| The wreck and flotsam | `game/assets/wreck/<name>.glb` | `game/assets/wreck/hull_stern.glb` |
| Characters | `game/assets/characters/<name>.glb` | `game/assets/characters/body_male.glb` |
| Animations | `game/assets/characters/anims/<set>.glb` (actions only, on the shared skeleton) | `locomotion.glb` |
| Animals | `game/assets/animals/<species>.glb` (mesh, skeleton and clips together) | `red_deer.glb` |

`<node>` is the id from `content/nodes/*.yaml` without the `node.` prefix (`node.scots_pine` → `scots_pine`).
Variants are lower-case letters `a`, `b`, `c` … The game picks a variant per node instance by a hash, so **every
variant of a node must be interchangeable** (same scale class, same origin convention).

**Tree sizes:** the sim has four size classes per tree (sapling, pole, timber, veteran). Model the **timber** size
(a mature tree: oak ≈ 16–20 m, scots pine ≈ 18–22 m, birch ≈ 14 m, willow and alder ≈ 10–12 m); the game scales it
×0.3 / ×0.6 / ×1.0 / ×1.25. Keep trunk taper plausible at those scales.

## 6. Characters and animation

### 6.1 One skeleton for every human

Bone names follow **Godot's `SkeletonProfileHumanoid`** so animations from any source retarget in Godot. Use
exactly these deform bones (37; the 32 §5 cap is 45):

```
Root (at the floor, between the feet; no deform; it never moves in clips: all clips are in place)
Hips
  Spine > Chest > UpperChest > Neck > Head > Jaw, LeftEye, RightEye
  UpperChest > LeftShoulder > LeftUpperArm > LeftLowerArm > LeftHand
      LeftHand > LeftThumbMetacarpal > LeftThumbProximal
      LeftHand > LeftIndexProximal > LeftIndexIntermediate
      LeftHand > LeftMiddleProximal > LeftMiddleIntermediate   (drives the middle, ring and little fingers together: a "grip" chain)
  (the same for the Right side)
  Hips > LeftUpperLeg > LeftLowerLeg > LeftFoot > LeftToes
  Hips > RightUpperLeg > RightLowerLeg > RightFoot > RightToes
```

Plus two **non-deform socket bones**: `RightHandProp` (child of RightHand) and `LeftHandProp` (child of LeftHand),
placed in the palm with +Y along the held tool's shaft. The game parents held items to these.

- Rest pose: **A-pose** (arms ≈ 45° down), facing −Y, feet on z = 0.
- Rigify is fine for authoring, but **export only the deform bones above** (rename DEF bones, drop the control rig).
- The same skeleton serves both base bodies and is **scaled** for youths (×0.92), elders (×0.97) and children
  (×0.6–0.8). Don't make separate skeletons.

### 6.2 Character meshes: modular, separate objects

Each character file holds the skeleton plus **separate mesh objects**, so the game can mix parts and hide the head in
first person. Object name prefixes are part of the contract:

| Prefix | Parts |
|--------|-------|
| `Body_` | `Body_Male`, `Body_Female` (full body, hands included; the regions under clothing may be cut away) |
| `Head_` | heads (face, ears, neck), at least 4 per sex, varied in nose, jaw and brow |
| `Hair_`, `Beard_` | hair and beard meshes |
| `Cloth_` | clothing layers named after items: `Cloth_linen_shirt`, `Cloth_wool_tunic`, `Cloth_wool_hose`, `Cloth_turnshoes`, `Cloth_wool_cloak`, `Cloth_wool_hood`, (P1) `Cloth_hide_jerkin`, `Cloth_fur_cloak`, `Cloth_oiled_cloak`, `Cloth_sailcloth_poncho` |
| `Headwear_` | hoods and hats, if separate from cloth |

In first person the game hides every `Head_`, `Hair_`, `Beard_` and `Headwear_` object for the player only; so keep
the neck seam inside `Head_`, and make sure the body looks right from the eyes downwards (collar, shoulders, arms).

**Faces:** blend shapes (shape keys) on each `Head_` named `blink`, `joy`, `anger`, `fear`, `grief`, `shame`,
`jealousy`, `mouth_open`, `mouth_wide`, `mouth_round`. Subtle is fine; they read at 2–5 m.

**Skin and hair** use the `skin_hair` palette row (skin_1 … skin_5; hair dark, brown, fair). Clothing uses the
`cloth` row; Varrowan settlers (the expedition) get `varrow_blue` / `varrow_gold` accents on 1–2 garments at most.

### 6.3 Animation clips: names are the contract

All clips are **in place** (Root and Hips don't travel: the game moves the body). 30 fps. Loops loop cleanly.
Export clips as Blender actions named exactly as below (lower case), grouped into these files:

| File | Clips (P0 bold) |
|------|-----------------|
| `anims/locomotion.glb` | **`idle`**, **`idle_alt`**, **`walk`** (1.6 m/s), **`jog`** (4.0 m/s), **`sprint`** (6.5 m/s), `crouch_walk`, `carry_walk`, **`turn_left`**, **`turn_right`**, `jump`, `fall`, `land` |
| `anims/rest.glb` | **`sit_ground_down`**, **`sit_ground_loop`**, **`sit_ground_up`**, **`lie_down`**, **`sleep_loop`**, **`lie_up`**, **`warm_hands_loop`** (crouched at a fire) |
| `anims/social.glb` | **`talk_a`**, **`talk_b`**, **`talk_c`**, **`nod`**, **`shake_head`**, `shrug`, **`point`**, **`wave`**, `laugh`, `argue_a`, `argue_b`, `cry` |
| `anims/needs.glb` | **`eat_loop`**, **`drink_kneel_loop`** (cupped hands at a stream), `drink_skin` |
| `anims/work.glb` | **`chop_loop`** (axe, overhead to a trunk), **`pick_up`**, **`forage_pick_loop`** (kneel and pick), **`knap_loop`** (seated, hammerstone on a core), `carry_log_walk`, `dig_loop`, `stoke_fire` |
| `anims/combat_basic.glb` (P2) | `unarmed_ready`, `punch_a`, `punch_b`, `block`, `shove`, `hit_react_front`, `stagger`, `downed_loop`, `death_a` |

The game cross-fades by speed (`walk`, `jog`, `sprint`) and plays the rest by name. A clip it can't find falls back to
`idle`, so partial sets still work. **Walk speeds matter:** feet should not slide at 1.6 / 4.0 / 6.5 m/s.

### 6.4 First-person view

The player is a normal character whose camera sits at the eyes, with the head parts hidden. So:

- Arms and hands are what the player sees most. Hands need a believable grip pose on `RightHandProp`.
- **First-person check:** render each work clip (`chop_loop`, `knap_loop`, `forage_pick_loop`, `eat_loop`,
  `drink_kneel_loop`, `pick_up`) from a camera at the `Head` bone, looking forward, and look at it. Arms must stay
  in view and must not clip through the camera.
- Handheld items that are seen up close (`flint_knife`, `stone_axe`, `iron_axe`, `hammerstone`) may use up to 1,200
  tris.

### 6.5 Animals (P2)

Same rules with their own skeleton (≤ 35 bones, `Root` bone at the floor, in-place clips): `idle`, `walk`, `run`,
`graze_loop`, `alert`, `flee` (= `run` is fine), `attack` (wolf, boar), `hit`, `death`, `sleep_loop`.

## 7. Tools (already in the repo)

```bash
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup -P tools/art/export.py  -- <generator.py> <out.glb> '<json-params>'   # run a generator, export .glb
$B -b --factory-startup -P tools/art/check.py   -- <asset.glb> <budget_class>                  # budgets, scale, origin, normals, palette UVs
$B -b --factory-startup -P tools/art/preview.py -- <asset.glb> <out-prefix>                    # 4-angle turntable + 40 m silhouette
python3 tools/art/build_kit.py --previews                                                       # example: how the graybox kit is built end to end
```

Generators print a machine-readable `RESULT …` line. Look at the previews yourself before you log an asset as ready.
Existing examples: `art/generators/conifer/` (a pine), `art/generators/humanoid/` (a 17-bone blockout,
**superseded by §6.1**), `art/generators/graybox/kit.py`.

If a tool doesn't handle something you need (skinned meshes with many objects, animation-only files), extend it under
`tools/art/` and say so in `STATUS.md`. Keep the tools headless and scriptable.

## 8. Godot import

You don't need Godot to deliver. If you want to look in-engine: Godot 4.7.2 .NET at
`/Applications/Godot_mono.app/Contents/MacOS/Godot`. `game/scenes/dev/KitGallery.tscn` shows the graybox kit; you may
add your own gallery as `game/scenes/dev/ArtGallery.tscn` with a script in `game/scripts/Dev/ArtGallery.cs`
(those two paths are yours). Commit the `.import` files Godot creates next to your assets.

## 9. The manifest (no entry, no merge)

Every shipped file has an entry in `content/assets/<category>.yaml` (create `flora_m2.yaml`, `nature.yaml`,
`buildings.yaml`, `props.yaml`, `wreck.yaml`, `characters_m2.yaml`, `animations.yaml`, `animals.yaml`, `terrain.yaml`
as needed). Format (validated against `content/schemas/asset.schema.json`; run
`dotnet run --project src/FeudalSim.Headless -- content validate` after editing):

```yaml
- id: asset.flora.oak_a
  kind: model                     # model | animation | texture
  status: review                  # you set review; only the owner sets approved
  milestone: M2
  source:
    type: generator               # generator | hand | external
    generator: art/generators/broadleaf/broadleaf.py
    params: { species: oak, seed: 1 }
    tool: Blender 5.1.0 (headless)
  outputs: [game/assets/flora/trees/oak_a.glb]
  budget_class: tree
  measured: { tris_lod0: 812, materials: 1 }    # from check.py
  license: { name: Proprietary-own-work, author: FeudalSim, attribution_required: false }
  notes: "Timber-size oak, 18 m; trunk-only convex collision"
```

## 10. External sources and licences

- **Allowed without asking:** CC0 sources (Quaternius, Kenney, Poly Haven, ambientCG, and similar). Record
  `source.type: external`, the URL, the pack name and `license: { name: CC0-1.0, … }`. Re-colour to the palette.
- **Ask first (in STATUS.md, then wait for the owner):** anything that costs money; CC-BY or other attribution
  licences; Mixamo or other services with terms of use; any AI generation service (32 §8: allowed only with
  commercial-use terms, logged as `external` with the tool's name).
- **Never:** ripped or unlicensed assets, or assets imitating a real brand.

## 11. Definition of done for one asset

1. File at the contract path; the object names follow §5 and §6.
2. `check.py` passes for its budget class (or the gap is explained in STATUS.md).
3. You looked at the preview and the silhouette; for character work, also the first-person check (§6.4).
4. Manifest entry with `status: review` and `measured` filled in; `content validate` passes.
5. **Regenerate the licence file in the same commit:** `dotnet run --project src/FeudalSim.Headless -- content licenses`
   (CI's `ContentPipelineTests` fails when `ASSET_LICENSES.md` doesn't match the committed manifests).
6. A line in `STATUS.md`.

## 12. Git

- Work on `main`, touching only your paths: `art/`, `game/assets/`, `content/assets/`, `tools/art/`,
  `game/scenes/dev/Art*`, `game/scripts/Dev/Art*`, `docs/production/art-briefs/`.
- Binaries go through **Git LFS** (installed; patterns are in `.gitattributes`). Check `git lfs status` before
  committing; a `.glb` or `.png` must show as LFS.
- Before every push: `git pull --rebase origin main`. **Never force-push.** The code agent pushes to the same branch.
- Conventional messages: `art(flora): oak a–c`, `art(char): base bodies and Varrow kit`, `anim(char): locomotion set`.
- Keep commits to one asset family each, so a rejected look is easy to revert.
