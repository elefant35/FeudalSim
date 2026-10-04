# What to build: M2 first playable, in priority order

Work top to bottom. **P0** is the first playable build: the player walks the island in first person, among real
trees, rocks and plants, with the camp and the wreck on the shore and settlers who look like people. **P1** fills
the island out. **P2** is the next gameplay wave (animals, combat). Counts are minimums; more variants are welcome
if they stay in budget.

Setting reminders: a cold, wet, windswept North-Atlantic island around 1100 AD. Settlers are from the **Kingdom of
Varrow** (think Anglo-Norman or Norse-Irish commoners: wool, linen, leather; muted naturals with a little woad blue
and weld gold). The ship's salvage is the only manufactured stuff on the island. Nothing should look polished, new
or fantastical.

---

## P0: the first playable

### P0.1 Settlers (the most visible thing in the game)

| Asset | Spec |
|-------|------|
| Skeleton | Contract §6.1. Deliver it first, in `game/assets/characters/body_male.glb`, so the animation work can start in parallel |
| `body_male.glb`, `body_female.glb` | Skeleton + `Body_*` + all `Head_*`, `Hair_*`, `Beard_*`, `Cloth_*` parts for that sex in one file (the game shows one of each slot). 1.72 m / 1.62 m. Worn, weather-beaten working people: broad hands, sensible proportions, not heroic |
| Heads | ≥ 4 per sex, varied in nose, jaw, brow and age (one visibly older per sex). Face shape keys per contract §6.2 |
| Hair | ≥ 6 (short cropped, shoulder-length, braided, tied back, balding with fringe, long loose); beards ≥ 3 (short, full, moustache-and-chin) |
| The Varrow homeland kit (everyone lands in it) | `Cloth_linen_shirt` (undyed linen, visible at collar and cuffs), `Cloth_wool_tunic` (knee-length men, ankle-length women's dress; wool_brown, russet, woad or weld), `Cloth_wool_hose` (leg wraps fine), `Cloth_turnshoes` (soft leather), `Cloth_wool_cloak` (pinned at the right shoulder; wind weights in vertex colour R), `Cloth_wool_hood` (with a short shoulder cape). Assembled character in budget (2,500–4,000 tris) |
| First-person look | Contract §6.4: from the eyes the player sees sleeves, hands and the cloak edge. Check it |

### P0.2 Animations (on the shared skeleton; contract §6.3)

All **bold** clips in the contract table: locomotion (`idle`, `idle_alt`, `walk`, `jog`, `sprint`, `turn_left`,
`turn_right`), rest (`sit_ground_*`, `lie_down`, `sleep_loop`, `lie_up`, `warm_hands_loop`), social (`talk_a/b/c`,
`nod`, `shake_head`, `point`, `wave`), needs (`eat_loop`, `drink_kneel_loop`), work (`chop_loop`, `pick_up`,
`forage_pick_loop`, `knap_loop`).

Hand-keyed is acceptable for P0 if it reads clearly; CC0 animation packs retargeted to the skeleton (Quaternius'
universal animation library is CC0) are a good start. Mixamo needs the owner's OK first (contract §10). The
settlers spend most of their day at these: `walk`, `idle`, `sit_ground_loop`, `warm_hands_loop`, `talk_*`,
`chop_loop`, `forage_pick_loop`, `eat_loop`, `sleep_loop`. Make those the best.

### P0.3 Terrain textures (Terrain3D layers)

Tileable, **1024²**, two PNGs per layer (Terrain3D's packing): `<layer>_albedo_height.png` (RGB albedo, A height)
and `<layer>_normal_rough.png` (RGB normal in OpenGL convention, A roughness). Painterly and low-contrast, matching
the palette's `earth`, `foliage` and `stone_metal` rows; they must tile over 2 m without an obvious repeat at walking
height. Procedural generation (Python or Blender) is ideal.

| Layer | Where it goes (biomes in 10 §3.7) |
|-------|------------------------------------|
| `sand` | beaches, dunes |
| `shingle` | storm beaches of pebbles and flint (the landing beach) |
| `grass` | meadow, the default |
| `grass_rough` | coastal grassland, dune tops (tussocky, paler) |
| `forest_floor` | broadleaf and pine woods (leaf litter, needles, moss) |
| `heather_moor` | hills and moor (heather, bilberry, peaty) |
| `mud` | wetland, stream banks, trampled camp ground |
| `rock` | cliffs, outcrops, steep slopes (granite-grey) |

### P0.4 Trees (the 7 commonest; timber size, contract §5)

| Node | Variants | Look |
|------|----------|------|
| `scots_pine` | 4 | Tall straight orange-brown upper trunk, flat-topped dark canopy high up; the island's pine woods are dense |
| `oak` | 4 | Broad, spreading, gnarled, rounded crown; 16–20 m |
| `birch` | 3 | Slender white bark with dark marks, light airy crown; often leaning |
| `ash` | 3 | Tall, open, upward-sweeping branches, grey bark |
| `beech` | 3 | Smooth grey trunk, dense dome crown |
| `alder` | 3 | Wet ground; dark fissured bark, conical crown, often multi-stemmed |
| `willow` | 3 | Wet ground; multi-stemmed, shaggy, leaning |

Windswept is good: a few variants per species with crowns pushed one way (the prevailing wind is westerly). Trunk-only
convex collision. A generator per family (conifer, broadleaf) with species parameters is the expected approach.

### P0.5 Rocks and ground

| Node or asset | Variants | Notes |
|---------------|----------|-------|
| `boulder` | 4 | 1–2.5 m glacial erratics, rounded granite, lichen spots (a palette accent) |
| `fieldstone` | 4 | 0.3–0.6 m stones that can be picked up |
| `flint_scatter` | 3 | A patch (≈ 1.5 m) of grey-white flint nodules in the grass or shingle |
| `driftwood` | 3 | Bleached grey logs and branches on the beach, 1–4 m |
| `outcrop` (no node; the game places it on steep ground) | 4 | 3–8 m rock faces and slabs that sit into slopes; `-colonly` collision |

### P0.6 Bushes and the plants the player sees most

| Node | Variants | Notes |
|------|----------|-------|
| `hazel` | 3 | Multi-stemmed shrub 3–5 m; the camp's pole wood |
| `bramble` | 2 | Low tangled thicket, blackberries in summer (a berry-dot mesh part that can be hidden by season) |
| `gorse` | 2 | Spiky dark-green dome, yellow flowers (spring), coast and moor |
| `reeds` | 3 | Dense 1.5–2.5 m stands on wet ground; wind weights |
| `grass_tuft` (no node; scattered by the game) | 4 | Clumps 0.2–0.6 m for the meadow and dunes; wind weights; very cheap (≤ 60 tris) |
| `fern` (no node; woodland ground cover) | 3 | Bracken, 0.6–1.2 m |

### P0.7 The camp (Landfall, Day 1–8)

| Asset | Path | Spec |
|-------|------|------|
| Campfire | `buildings/campfire.glb` | A ring of fieldstones (≈ 1.2 m), a few burning logs; objects `Logs` and `Ash` separate (the game hides logs when the fire dies). Flames and smoke are VFX on the code side, so leave space |
| Sailcloth shelter | `buildings/sailcloth_shelter.glb` | 3 × 3 m, sleeps 3 (14 §T0): salvaged sail over poles and an oar ridge, guyed with ship's rope, weighted with stones; open front. Sail uses `linen` / `wool_grey`, sea-stained |
| Lean-to | `buildings/lean_to.glb` | 2 × 3 m: poles and thatch or brush against a ridge pole, sleeps 2 |
| Bough bed | `props/bough_bed.glb` | 2 × 0.8 m mattress of pine or heather boughs |
| Salvage stores | `props/crate.glb`, `props/barrel.glb`, `props/sack.glb`, `props/chest.glb` | Ship's stores, sea-stained; 2 variants each if cheap. The camp's food stores are a pile of these |
| Water place | `props/water_skin.glb`, `props/wooden_bucket.glb` | At the stream |
| Firewood | `props/firewood_pile.glb` | A stacked pile ≈ 1 m (the camp's firewood stock); a smaller `firewood_bundle.glb` to carry |

### P0.8 The wreck of the *Wending Star*

A two-masted, clinker-built trading ship (a cog or knarr type, ≈ 22 m), broken on a reef 100–300 m off the landing
beach. Files under `game/assets/wreck/`:

| Asset | Spec |
|-------|------|
| `hull_bow.glb`, `hull_stern.glb` | The two halves, broken amidships and heeled over ≈ 25°, sitting in water to about the waterline (z = 0 is the sea surface here, not the seabed); `-colonly` collision on deck and hull |
| `mast_broken.glb`, `rigging_tangle.glb` | A snapped mast with the spar and torn sail trailing |
| `reef_rocks.glb` | 3–4 dark rock masses the hull rests on, for the code agent to place around it |
| Flotsam | `flotsam_planks.glb`, `flotsam_barrel.glb`, `flotsam_crate.glb`, `rope_coil.glb`, `sail_heap.glb`: washed up along the beach |

The sea itself, foam and weather are the code agent's (shaders and VFX).

### P0.9 What the player holds (seen up close in first person)

`props/flint_knife.glb`, `props/stone_axe.glb` (hafted with lashing), `props/iron_axe.glb` (a ship's wood axe),
`props/iron_knife.glb`, `props/hammerstone.glb`, `props/flint_nodule.glb`, `props/flint_flake.glb`. Origin at the
grip (contract §1), up to 1,200 tris for the four tools, a **crude** look (the game later adds quality variants).

---

## P1: the island filled out

- **Trees:** `elm`, `yew` (dark, dense, ancient), `lime` (3 each); a **stump** and **felled log** per family
  (`<node>_stump.glb`, `<node>_log.glb`: the game shows these after a tree is cut).
- **Bushes:** `hawthorn`, `blackthorn`, `elder`, `crab_apple`, `juniper`, `dog_rose`, `bilberry` (2 each).
- **Forage plants** (`kind: patch` in `content/nodes/plants.yaml`; one cluster ≈ 0.5–1.5 m; 2 variants each):
  every plant node except `reeds` (P0). **The look-alike pairs are gameplay:** they must look alike at a glance and differ on a close
  look, per the real plants' tells:
  - `wild_carrot` vs `hemlock` vs `water_hemlock` (umbellifers; hemlock has purple-blotched stems)
  - `field_mushroom` vs `death_cap` (pink-brown gills vs white gills and a cup at the base)
  - `ramsons` vs `lily_of_the_valley` (broad leaves; ramsons' white star flowers vs nodding bells)
  - `comfrey` vs `foxglove` (hairy leaves; foxglove's tall purple spire when flowering)
  - `bilberry` vs `deadly_nightshade` (dark berries; nightshade's berries sit in a star-shaped calyx)
- **Camp, later days:** `drying_rack.glb`, `knapping_stone.glb` (a seat stone and a flat anvil stone with flakes
  around), `storage_pit.glb` (a lined pit with a cover), `latrine.glb`, `hut.glb` (4 × 5 m wattle-and-daub with a thatched
  roof; T0 to T1 in 14), and a construction stage for each shelter (`<name>_stage1.glb` frame, `_stage2` half covered).
- **Items on the ground and in hand:** `rough_log`, `pole`, `reed_thatch` bundle, `clay` lump, `antler_billet`,
  `pressure_flaker`, the 21 forage items as small hand-held pickups (`props/item_<name>.glb`, ≤ 150 tris each).
- **Characters:** `Cloth_hide_jerkin`, `Cloth_fur_cloak`, `Cloth_oiled_cloak`, `Cloth_sailcloth_poncho`; the
  child body (the same skeleton scaled, its own proportions: bigger head, shorter limbs); 2 more heads per sex.
- **Animations:** the non-bold clips in the contract table.

## P2: the next gameplay wave

- **Animals** (contract §6.5): `red_deer` (stag and hind), `wild_boar`, `hare`, `grey_wolf`; later `brown_bear`,
  `fox`, `wild_goat`. Medium animals 1,200–2,500 tris.
- **Combat basics:** the `combat_basic.glb` set; a `wooden_spear`, a `sling`, a `self_bow` with arrows, a `round_shield`.
- **Carcass and hunting props:** a deer carcass, a hide on a frame, a snare.

---

## Out of scope for you (the code agent does these)

Water, sky, fog and weather shaders; fire, smoke and spark VFX; the terrain itself (heights come from the sim's
world generator); placing assets in the world; the first-person camera and interaction; UI and icons; audio.
