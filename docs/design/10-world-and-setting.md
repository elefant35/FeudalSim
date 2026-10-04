# 10 — World & Setting

> **Status:** Draft v0.1 · revised for canon v0.3 (decision points) · **Owner doc for:** world generation, biomes, resources, climate/weather/seasons, flora & fauna (ecology), points of interest, exploration & place-naming, travel, lore, expeditions' arrival logic, resupply ships & the Silence, the ship's manifest · **Depends on:** [01-canon](../01-canon.md), [11-survival](11-survival.md), [12-skills-and-professions](12-skills-and-professions.md), [13-crafting-and-minigames](13-crafting-and-minigames.md), [14-technology-and-buildings](14-technology-and-buildings.md), [15-economy-and-trade](15-economy-and-trade.md), [16-social-systems](16-social-systems.md), [17-governance-and-law](17-governance-and-law.md), [18-conflict-and-warfare](18-conflict-and-warfare.md), [19-player-experience](19-player-experience.md), [20-architecture](../tech/20-architecture.md), [21-npc-ai](../tech/21-npc-ai.md), [22-llm-integration](../tech/22-llm-integration.md)

Farstrand has to be big enough to explore for days, uneven enough to fight over, and legible
enough that an NPC can say "the tin is up past Redwater Ford" and mean a real place. This document
specifies how the land is generated, what is on it, how it changes over a 40-year saga, how people
move across and name it, and who else comes ashore.

## Table of contents

1. [Scope & cross-cutting policies](#1-scope--cross-cutting-policies)
2. [Farstrand at a glance](#2-farstrand-at-a-glance)
3. [World generation](#3-world-generation)
4. [Biomes](#4-biomes)
5. [Resources](#5-resources)
6. [Climate, weather & seasons](#6-climate-weather--seasons)
7. [Flora](#7-flora)
8. [Fauna & ecology](#8-fauna--ecology)
9. [Points of interest & landmarks](#9-points-of-interest--landmarks)
10. [Exploration, knowledge & mapping](#10-exploration-knowledge--mapping)
11. [Place naming](#11-place-naming)
12. [Travel](#12-travel)
13. [Lore](#13-lore)
14. [Expeditions, ships & the Silence](#14-expeditions-ships--the-silence)
15. [The ship's manifest](#15-the-ships-manifest)
16. [Simulation LOD & Interludes](#16-simulation-lod--interludes)
17. [LLM / Jev touchpoints](#17-llm--jev-touchpoints)
18. [Tuning knobs](#18-tuning-knobs)
19. [Failure modes & exploits](#19-failure-modes--exploits)
20. [Validation & headless tests](#20-validation--headless-tests)
21. [Milestones](#21-milestones)
- [Open questions](#open-questions)
- [Proposed canon additions](#proposed-canon-additions)

---

## 1. Scope & cross-cutting policies

### 1.1 Owned here vs. referenced

**Owned:** the generation pipeline and its validation; terrain, hydrology, coast and tides; biomes;
the resource catalog, deposit sizes and renewal; climate, weather, snow, daylight and moon; tree
species and wood properties; wild plants; wildlife populations, predation and depletion; POIs;
per-person map knowledge; place naming; locomotion speeds, terrain costs, path formation, fords and
boats; lore; expedition and ship schedules; the ship's manifest (roster, households, backstory ties).

**Referenced (interface only):** crop/soil dynamics and all land-work resolution, including
hunting, fishing, foraging, woodcutting and mining ([13](13-crafting-and-minigames.md)); roads,
bridges, ports and buildings ([14](14-technology-and-buildings.md)); property and land tenure
([15](15-economy-and-trade.md), [17](17-governance-and-law.md)); beliefs, rumors and relationship
math ([16](16-social-systems.md)); the Charter's legal force ([17](17-governance-and-law.md)); animal
combat ([18](18-conflict-and-warfare.md) §3.6); skill and know-how seeding ([12](12-skills-and-professions.md) §8.5);
player backgrounds and tie choice ([19](19-player-experience.md)); grids, determinism and storage
([20](../tech/20-architecture.md)); the needs, injuries and the Landfall scenario ([11](11-survival.md)).

### 1.2 Time-scale policy *(proposed canon)*

Canon compresses a year to 32 days but keeps the day at 24 game hours. Every duration in this doc
and in [11](11-survival.md) obeys one rule:

| Band | Real-world duration | Rule | Examples |
|------|--------------------|------|----------|
| **Clock-scale** | up to about a day | **1 : 1** in game time | needs decay, bleeding, warmth, sleep, tides (12.4 h), weather fronts, daylight, cooking |
| **Calendar-scale** | longer than a day | **÷ K, K = 365/32 ≈ 11.4**, with a floor of 1 game day for anything a person must react to | healing, illness courses, starvation, preserved-food shelf life, pregnancy (270 d ÷ 11.4 ≈ 24 d, matching canon), animal breeding, tree growth, ore renewal |

**Corollary:** anything measured in real *years* takes the same number of game *years* (aging,
an 80-year oak, a 7-year coppice cycle, a deer herd's annual growth). Ecology and forestry use
real annual biology one-for-one per game year. Hand-tuned exceptions (fresh-food shelf life) are
listed where they occur.

### 1.3 Locomotion in game time

Embodied movement runs in real time, while needs and schedules run in game time, and day length is a
gameplay setting ([20](../tech/20-architecture.md) §5.1). All game-time travel figures below
assume the default **30-minute day** (1 game hour = 75 real s). At the default jog of 4 m/s a person
covers **300 m per game hour**, so the 8 km region is about **three days' cross-country march**. In
travel-time terms Farstrand behaves like a medieval county 100+ km across. The consequences are
intended:

- Settlements are **compact** (core radius 150–250 m). Fields beyond ~400 m cost an hour of walking
  each way.
- Distant resources (hill iron, tin, old oak) need **work camps, carts or boats**. As woods recede,
  getting firewood becomes a logistics problem, which is historically right.
- Neighbouring polities 2.5–5 km apart are **"a day or two away"**, which is right for raids,
  envoys and markets.

### 1.4 Spatial grids

Structures and storage belong to [20](../tech/20-architecture.md) §6. Their generation rules belong here.

| Grid | Resolution | Holds |
|------|-----------|-------|
| Heightfield | 2 m (4,097²) | Elevation |
| Terrain attribute grid | 8 m (1,024²) | Biome, lithology, soil type/fertility/drainage, moisture, slope, water class, trample score |
| Resource nodes | in 64 m chunks | Trees, bushes, rocks, outcrops, herb patches (regenerated from seed; deltas saved) |
| **EcoCell** | 512 m (16 × 16 = 256; ≈ 140–170 on land) | Fauna populations, wariness, disturbance, forest stand aggregates, fish stocks |
| **Knowledge tile** | 128 m (64 × 64) | Per-person "seen / heard of" bitset (512 B per person) |
| Features | vector | Rivers, lakes, coastlines, POIs, deposits and named places (each with a stable `FeatureId`) |

---

## 2. Farstrand at a glance

| Quantity | Value |
|----------|-------|
| Region | 8,192 m × 8,192 m (≈ 67 km²), centered on the origin per [20](../tech/20-architecture.md) |
| Landmass | **35–45 km²** main island + 3–10 islets; the rest is sea, reefs and shoals |
| Highest peak | 900–1,250 m; permanent snow only above ~1,100 m |
| Rivers | 2–4 major (catchment ≥ 4 km²), 8–20 streams, 2–6 lakes ≥ 2 ha, **exactly 1 primary estuary** (optionally a second, smaller one) |
| Climate | Cool temperate maritime: winters ≈ 0 °C at sea level, summers ≈ 17 °C; wet; westerly gales in Autumn and Winter |
| Daylight | 8 h (Winter 3) to 16 h (Summer 3) |
| Settlement capacity | Room for 4–8 settlements, each with its own fresh water, landing and ≥ 30 ha of good soil |
| Starting wildlife | Canonical set: deer, boar, hare, wolf, bear, fox, wild goat, waterfowl, fish. All are *naive*: they have never been hunted by people (§8.4) |

---

## 3. World generation

### 3.1 Inputs

- **Seed:** a 64-bit integer. Each stage draws from its own stream `Rng("worldgen.<stage>", seed)`
  (canon §14), so changing one stage's code does not reshuffle the others.
- **WorldSpec:** an authored YAML profile that holds every hard constraint and target band. Mods and
  scenarios can provide alternative specs.
- **Settler count** (12–40) and **world-creation knobs** (resource richness, Y0 winter cap; exposed in
  [19 §12](19-player-experience.md#12-settings-difficulty--accessibility)). They are world options, not
  difficulty modes. They shift bands but never violate the hard asserts in §3.11.

```yaml
id: worldspec.farstrand_default
region_m: 8192
land_area_km2: [35, 45]
relief_archetype_weights: { spine: 0.40, massif: 0.35, twin_ridges: 0.25 }
peak_m: [900, 1250]
rivers_major: [2, 4]
estuary: { primary: 1, secondary_chance: 0.3, mouth_width_m: [300, 900], tidal_reach_m: [1500, 3000] }
lakes: { count: [2, 6], min_area_ha: 2, at_least_one_over_ha: 10 }
biome_share:            # fraction of land area
  coast_dunes: [0.05, 0.09]
  meadow: [0.12, 0.20]
  broadleaf: [0.22, 0.32]
  pine: [0.08, 0.15]
  wetland: [0.05, 0.10]
  river_valley: [0.07, 0.12]
  hills_moor: [0.10, 0.16]
  highland: [0.05, 0.10]
deposits:
  tin_placer:  { count: [1, 2], min_path_m_from_landing: 2000 }
  hill_iron:   { count: [2, 3], min_m_from_breadbasket: 3000 }
  copper:      { count: [2, 4], at_least_one_within_path_m_of_landing: 4000 }
  bog_iron:    { count: [2, 5], at_least_one_within_path_m_of_landing: 3000 }
  galena:      { count: [1, 1] }
  sea_coal:    { count: [0, 1], chance: 0.5 }
landing:
  fresh_water_within_m: 400
  flint_within_m: 1000
  clay_within_m: 1500
  broadleaf_within_m: 600
  fertile_ha_within_1500m: 40      # fertility ≥ 0.65
  wreck_reef_offshore_m: [150, 300]
expedition_sites: { candidates_min: 3, min_path_m_from_landing: 2500, min_path_m_between: 2500 }
max_attempts: 16
```

### 3.2 Pipeline

```mermaid
flowchart LR
  A[Spec + seed] --> B[Landmass mask]
  B --> C[Relief & erosion]
  C --> D[Lithology]
  D --> E[Hydrology]
  E --> F[Coast, estuary, tides]
  F --> G[Climate fields]
  G --> H[Soils & biomes]
  H --> I[Resources & POIs]
  I --> J[Site selection]
  J --> K[Ecology spin-up 50 y]
  K --> L{Validate}
  L -- fail --> B
  L -- pass --> M[World data + manifest]
```

| # | Stage | Method (summary) | Budget (rec. spec) |
|---|-------|------------------|--------------------|
| 1 | Landmass mask | Domain-warped fBm × archetype falloff; sea level found by bisection to hit the target land area; reefs where the mask is just below sea level within 400 m of the shore | 1 s |
| 2 | Relief | Archetype ridge field (ridged multifractal) + hills fBm + lowland plain; peak normalized into `peak_m` | 2 s |
| 3 | Erosion | 60k deterministic hydraulic-erosion droplets on the 8 m grid + 2 thermal passes; detail upsampled to 2 m | 8 s |
| 4 | Lithology | Voronoi "provinces" conditioned on elevation (§3.4) | < 1 s |
| 5 | Hydrology | Priority-flood depression fill → lakes; D8 flow accumulation; stream/river extraction; channel carving into the heightfield | 3 s |
| 6 | Coast | Estuary flare, mudflats, salt marsh, dunes on the windward sandy shore, beaches, cliffs | 1 s |
| 7 | Climate fields | Static modifiers per cell: altitude band, coast distance, wind exposure, rain-shadow factor, frost-hollow flag | < 1 s |
| 8 | Soils & biomes | Rule table (§3.7) + 2 cellular-automaton smoothing passes + a minimum patch size of 0.5 ha | 1 s |
| 9 | Resources & POIs | Rule-scored candidate sampling with Poisson-disk spacing (§3.8) | 2 s |
| 10 | Sites | Landing selection, then expedition candidates (§3.9) | 1 s |
| 11 | Ecology spin-up | 50 years of the LOD3 fauna/forest model (1,600 daily steps × ~160 EcoCells) so populations start at equilibrium | 2 s |
| 12 | Validate | Hard asserts §3.11; on failure, retry with `seed' = Hash(seed, attempt)` | < 1 s |

Target: **≤ 30 s** on recommended spec and **≤ 90 s** on minimum, excluding render-asset baking.
Grids (heightfield, terrain attributes), features, deposits and EcoCell states are written into the
save, so cross-platform float drift cannot change them after creation. Resource nodes are
regenerated per chunk from the seed, with only deltas saved ([20](../tech/20-architecture.md) §6).
Node scattering must therefore use **integer-only per-chunk hashing**, reading the saved grids, so
that regeneration is platform-stable even though [20](../tech/20-architecture.md)'s general
determinism is per-platform.

*Implemented (M2-01a-i):* stages 1–2 in `Sim/WorldGen/WorldGenerator.cs` on the 8 m grid with the W1/W5 asserts and
§3.11 retries; `feudalsim worldgen --seeds N --png out.png` previews and reports (30/30 seeds valid, ≈ 0.2 s per attempt
on the reference Mac). The 35–45 km² land target is 52–67% of the 8,192 m square, so the falloff is a superellipse
rather than a circle; offshore islets come from a dedicated field in the outer band (W1's 3–10). Erosion, lithology,
hydrology and coast follow (M2-01a-ii/iii).

### 3.3 Landmass & relief

Three archetypes, randomly rotated and mirrored:

| Archetype | Shape | Typical play |
|-----------|-------|--------------|
| **Spine** | A ridge along one side; hills step down to a broad lowland and the estuary on the far coast | Classic "farmland vs. mountains" divide; iron and tin behind the ridge |
| **Massif** | An off-center granite dome with radial valleys; the estuary sits at the longest valley | Several valley pockets, each a natural settlement site; contested passes |
| **Twin ridges** | Two ridges with a central river valley draining to the estuary | One rich corridor everyone wants; flanking uplands |

Elevation bands: sea floor to −40 m; coastal plain 0–30 m; lowland and valleys 5–150 m; hills
150–450 m; highland 450 m up to the peak. Cliffs (slope > 45°) occur on 10–25% of the coastline
and on highland scarps.

*Hypsometry (M2-01b-i):* after erosion, land heights are quantile-remapped (rank → height; ties by index).
- **Anchors:** P(h < 180 m) ∈ [0.78, 0.86], P(h < 450 m) ∈ [0.93, 0.96], P(h < 800 m) ∈ [0.985, 0.992], all drawn per
  seed, with the drawn peak kept.
- **Low ground:** the lowland segment is convex (t^1.6), giving a coastal plain.
- **Ordering:** the remap is monotone, so drainage order and depressions survive and the W1/W5 pass rates are
  unchanged.
- **Side effect:** shallower lowland depressions cost two seeds a lake in W3's count (lakes failing 4 → 6 of 20;
  rivers unchanged).

### 3.4 Lithology

*Implemented (M2-01a-ii):* `Lithologies.Assign` — 420 domain-warped Voronoi provinces, each labelled from the terrain at
its site (province-scale slope over ±64 m): granite above 62% of the peak, a metamorphic margin within 900 m of granite,
one chalk band on a seeded stretch of coast, alluvium on flat lowland, sandstone/shale elsewhere. Peat needs hydrology's
wetness and is assigned in stage 5 (M2-01a-iii).

Geology drives where resources are, so placement reads as plausible ("copper near the granite
edge, like at home").

| Province | Where | Yields |
|----------|-------|--------|
| Granite massif | Highland core | Building stone; tin and copper lodes at its **contact margin**; poor acid soils |
| Metamorphic margin (slate/schist) | Ring around the granite | Slate; copper; galena; tin placers in streams draining it |
| Sandstone & shale | Hills | Ironstone (hill iron), whetstone grit, sandstone; pyrite |
| Chalk / limestone | One lowland/coastal band | **Flint** nodule beds and shingle, lime, caves; thin fertile soils |
| Alluvium & clay | Valleys, estuary | Clays, sand, the best soils |
| Peat cover | Wetland, wet moor | Peat, **bog iron** |

### 3.5 Hydrology

- **Depressions** with volume ≥ 20,000 m³ and area ≥ 2 ha are kept as lakes. Smaller ones are
  filled.
- **Channels:** a stream forms where the catchment `A ≥ 0.5 km²` and a river where `A ≥ 4 km²`. Width
  is `w = 2 + 6·√A` m and normal-stage depth `d = 0.3 + 0.25·√A` m (A = 9 km² → 20 m wide, 1.05 m
  deep). Channels are carved into the heightfield.
- **Riffles and fords:** on reaches with slope 0.3–1.5%, every 300–900 m, the channel widens 1.5×
  and depth halves. These are the ford candidates (§12.4).
- **Springs:** 6–15, at slope breaks with a high groundwater index (concave hillfoot,
  limestone/sandstone contact). They are clean year-round.
- **River stage** (dynamic, hourly): `stage = base(season) + Σ rain response`. Spring snowmelt
  adds +0.3 m on highland-fed rivers during Spring 1–4. Summer base is −0.2 m. Each Rain hour adds
  +0.01 m and each Storm hour +0.04 m, all decaying 6% per hour. Floodplain (River Valley) cells flood
  when stage exceeds bank height (+0.8 m typical): travel ×0.4, crops per [13](13-crafting-and-minigames.md).
- **Water source classes** handed to [11](11-survival.md), which owns contamination:
  `spring`, `stream`, `river`, `lake`, `marsh_pool`, `brackish` (estuary below the tidal limit,
  undrinkable), `sea`.

*Implemented (M2-01a-iii, in progress):* `Sim/WorldGen/Hydrology.cs` — priority-flood fill (lakes kept at ≥ 2 ha and
≥ 20,000 m³, the rest filled), an ε-filled surface for routing, D8 and accumulation, streams (≥ 0.5 km²) and rivers
(≥ 4 km²) carved at §3.5's width and depth, fords on 0.3–1.5% reaches ≥ 300 m apart, 6–15 springs at concave slope
breaks on permeable rock, water classes, and **peat where the topographic wetness index is high** (§3.4). Stage 2
gained the archetype's major valley axes (k from `rivers_major`; spine: down to the far coast; massif: radial; twin
ridges: the central valley plus slanting flank valleys) as broad troughs, and a tilt of the lowlands toward the estuary.
**Finding (Q16):** W3's 2–4 rivers of ≥ 4 km² are met by spines but rarely by massif and twin-ridge islands — 20 seeds:
12/20 pass every assert, all spines; without the axes ~70% of the land drained through outlets < 1 km².

### 3.6 Coast, estuary & tides

*Implemented (M2-01a-iii):* `Sim/WorldGen/Coast.cs` — the primary estuary on the largest-catchment river (a second with
the spec's 0.3 chance), flared over the drawn tidal reach to the drawn mouth width (brackish water, mudflat/marsh fringe);
shores classified (dunes facing the westerlies, sandy beach, shingle under chalk, rocky under granite/slate, cliffs past
45°); `Tides.Level` (12.42 h, 3.2 / 1.6 m spring/neap, low water ≈ 09:30 on Y0 Spring 1).

- **Primary estuary:** the river with the largest catchment. Over its last 1.5–3 km it flares to a
  300–900 m mouth with intertidal mudflats, salt marsh and a tidal limit. A smaller second estuary
  appears with chance 0.3.
- **Shores:** sandy beaches with dunes on the coast facing the prevailing westerlies, shingle
  beaches below chalk cliffs (the flint source), rocky shores and sea stacks below granite, and
  sheltered bays (anchorages: water ≥ 3 m deep within 200 m of shore, protected from the west).
- **Tides** (clock-scale): semidiurnal, period **12.42 game hours**. Range **3.2 m** at spring
  tides and **1.6 m** at neaps, following the moon (§6.1). Water level
  `η(t) = ½·R(day)·cos(2π(t − t_HW)/12.42 h)`. The phase is seeded so that **low water falls at
  ≈ 09:30 on Y0 Spring 1** (the Landfall salvage window, [11](11-survival.md)). Storm surge adds
  +0.6 to +1.2 m during onshore Storm hours.
- **What tides gate:** wading to the wreck and reefs, the estuary **tidal ford** (passable within
  ±1.5 h of low water), tidal islands, shellfish and seaweed gathering, beaching and launching boats,
  and flooding of the beach camp during surges.

### 3.7 Climate fields, soils & biomes

Static per-cell modifiers used by the weather model (§6): altitude band (`low` < 150 m, `mid`
150–450 m, `high` > 450 m), coast distance (≤ 500 m = maritime), wind exposure (0.4 in forest up to
1.3 on ridges and coast), rain-shadow factor (0.8–1.4) and a frost-hollow flag (valley floors).

**Biome assignment** goes in priority order (the first rule that matches wins). It is followed by
smoothing and the minimum patch size.

| Order | Biome | Rule |
|-------|-------|------|
| 1 | Coast & Dunes | Within 150 m of shore and below 12 m (salt marsh goes to Wetland) |
| 2 | Wetland/Marsh | Slope < 2° and (drainage index high, or lake margin ≤ 60 m, or estuary salt marsh) |
| 3 | River Valley | Within 250 m of a major river, slope < 6°, below 150 m |
| 4 | Highland/Mountain | Elevation ≥ 450 m, or slope > 30° above 300 m |
| 5 | Hills & Moor | Elevation 180–450 m, or slope 12–30°. Moor where exposed and wet; upland pasture otherwise |
| 6 | Pine Forest | Podzol or sandy soils, or north-facing slopes 100–500 m, or old dune ridges |
| 7 | Meadow/Grassland | Well-drained lowland where the tree-establishment index is below 0.35 (noise + chalk + pre-human windthrow/deer-grazing seeds) |
| 8 | Broadleaf Forest | Everything else below 300 m |

*Implemented (M2-01b-i), `Sim/WorldGen/Biomes.cs`:*
- **Grid:** stage 7–8 grids on `WorldGrid` (slope, biome, soil, fertility, exposure, rain shadow, climate flags).
- **Biomes:** the priority table in order — coast before wetland, with only salt marsh sent to Wetland. Pine's sandy
  soils are noise-gated patches over sandstone, not the whole province; north-facing means slope ≥ 8° with aspect
  within ±45° of north. Two 3×3 majority passes, then patches under 0.5 ha merge into their commonest neighbour.
- **Soils and fertility:** soils by biome and rock, with fertility inside each soil's band set by low-frequency
  noise. The breadbasket is reported.
- **Climate fields:** exposure from local relief and the coast (forest halves it), rain shadow from the
  west/south-west, frost hollows on valley floors.
- **Hypsometry:** the share bands and the 180 / 450 m thresholds together imply the island's hypsometry. Stage 2
  therefore ends with a monotone quantile remap of land heights (§3.3 note). W4 is checked and reported (Q18).
- **Preview:** `feudalsim worldgen --png out.png` also writes `out.biomes.png`.

**Soils:** each 8 m cell gets a type, a **fertility 0–1**, a drainage class and a depth.
[13](13-crafting-and-minigames.md) owns how soil changes under cultivation.

| Soil type | Typical biome | Fertility | Notes |
|-----------|---------------|-----------|-------|
| Alluvial loam | River Valley | 0.80–1.00 | Floods; the **breadbasket** |
| Brown earth | Broadleaf, Meadow | 0.60–0.85 | Must be cleared of trees or sod |
| Chalk rendzina | Meadow (downs) | 0.50–0.60 | Thin, free-draining, good for sheep |
| Peat / gley | Wetland | 0.20 (0.65 if drained) | Draining is a [14](14-technology-and-buildings.md) work |
| Podzol | Pine, Moor | 0.25–0.40 | Acid; oats and rye only |
| Sand | Dunes | 0.05–0.15 | — |
| Stony upland | Hills, Highland | 0.10–0.25 | Pasture |

The **breadbasket** is the largest contiguous area with fertility ≥ 0.7. Its centroid anchors the
iron-distance rule.

### 3.8 Resource & POI placement

Each deposit type has a candidate rule and a score. Placement takes the highest-scoring candidates
subject to spacing and to the counts in the spec.

```
for each deposit type T in priority order (tin, galena, hill_iron, copper, bog_iron, coal, fireclay, brine, flint beds, clay pits, quarries):
    C = cells matching T.rule (e.g., tin: stream cells within 1.5 km downstream of granite contact)
    score(c) = T.geoScore(c) + noise(c)·0.2 − Σ penalties(distance constraints violated)
    pick up to T.count from C by descending score with Poisson-disk spacing T.minSpacing
    size, grade ~ T.sizeRange, T.gradeRange (seeded)
then POIs (§9) by the same pattern; then node scattering (trees, bushes, rocks, herb patches) by
biome density tables, chunk-seeded so any chunk can be regenerated independently.
```

*Implemented (M2-01b-ii):*
- **Node types:** content kind **node** (`content/nodes/*.yaml`): 10 trees, 10 bushes, 4 rock kinds, 28 plant patches
  from §7.1–7.4. Densities per hectare by biome are proposals, since the tables give none; a mature broadleaf stand is
  ≈ 220 stems/ha. Types can be edge-only or rock-bound (yew on chalk); toxic plants and the §7.3 lookalike pairs are
  recorded for foraging ID.
- **Scattering:** `NodeScatter` builds 8-byte nodes per 64 m chunk with integer-only hashing over the saved byte
  grids. Each 8 m cell expects `density × 0.0064` of each type in 16.16 fixed point. Positions are in 1/1024 m and
  size classes are weighted. About 0.9 M nodes per world, scattered in ≈ 0.1 s.
- **W14 helper:** `NodeScatter.Around` counts harvestable trees (pole and up) and forage patches around a point.
- **Deposits:** `Deposits.Place` places the T0 deposits (flint beds 3–8 on chalk, clay pits 4–10 on valley banks and
  wetland edges, quarries 2–5 on steep sandstone or chalk) by rule, score + 0.2 noise and spacing.
- **Not yet:** node deltas (felled, harvested) live with the world in the save (M2-02); metal deposits come with
  mining (M3–M4); the landing's W14 check comes with the landing (M2-01c).

**Contested-resource rule:** at least one high-value item (tin, hill iron, galena or the best
old-growth oak stand) must lie in a **between zone**. That means its path distance to the player's
landing and to some expedition candidate site differ by a ratio of no more than 1.25. Scarcity
should create a rivalry, not just a chore.

### 3.9 Site selection

**Player landing (the wreck of the *Wending Star*).** It is chosen among coastal cells scored by:

| Term | Requirement / weight |
|------|---------------------|
| Hard | Sandy or shingle beach ≤ 1.5 km from the primary estuary mouth; a reef or sandbar 150–300 m offshore whose low-water depth is ≤ 1.0 m along a wadeable line; fresh water ≤ 400 m; flint ≤ 1 km; clay ≤ 1.5 km; broadleaf forest ≤ 600 m; ≥ 40 ha of fertility ≥ 0.65 within 1.5 km; no tin within 2 km path; no wolf den ≤ 1.5 km; no bear den ≤ 1.5 km |
| Score + | Good farmland nearby (×3); a nearby river terrace that is a better camp site than the beach (×1, which motivates the first move, [11](11-survival.md)); copper within 4 km (×1) |
| Score − | Hill iron < 3 km (×2, which would break the canon iron rule); a defensible natural fortress right at the landing (×1, which would make the game too easy) |

The landing is designed to be **good for food and poor for metal**. That is the canon tension.

*Implemented (M2-01c-i), `Sim/WorldGen/Landing.cs`:*
- **Candidates:** sandy, dune and shingle beach cells within 1.5 km of the primary estuary mouth.
- **Hard requirements:** water ≤ 400 m, flint ≤ 1 km, clay ≤ 1.5 km, broadleaf ≤ 600 m, ≥ 40 ha of fertility ≥ 0.65
  within 1.5 km, and open sea 150 m or more straight out.
- **Choices (reversible):**
  - Fresh water includes brooks of ≥ 0.25 km² catchment. The estuary flare turns the lower river brackish, so
    without them almost no beach near the mouth qualifies.
  - Flint counts §5.1's three sources: beds, chalk ground and shingle shores.
- **Score:** the fertile area ×3.
- **Wreck and reef:** the reef line out to the wreck (≤ 220 m) is shoaled to −0.8 m, wadeable at low water. The wreck
  is POI 1; 6–12 flotsam strands lie within 5 km downdrift (east, with the westerlies), ≥ 300 m apart.
- **Asserts:** W9 is enforced alongside W1/W5, since the game needs a landing. W14 (`WorldGenerator.CheckW14`) is
  reported.
- **Islets:** W1's upper bound became a generator rule. If the stage-1 mask raises more than 10 islets of ≥ 1 ha, the
  smallest extras are sunk before relief.
- **Measured:** 20/20 seeds pass W1/W5/W9 (in 0–6 attempts); W14 19/20.
- **Not yet:** tin, dens and metals join the hard requirements when they exist (W6–W8), and expedition sites W10/W11
  come with expeditions.

**Expedition candidate sites** (≥ 3, used in §14.2): coastal, each with an anchorage, its own
fresh water, ≥ 30 ha of fertility ≥ 0.6 within 1.2 km, ≥ 2.5 km path from the landing and from
each other, and overlapping no more than 20% of any other site's 1.5 km farmland disc. Each
candidate carries culture-affinity scores (§14.2).

### 3.10 Ecology spin-up

The fauna and forest models (§7.2, §8.2) run for 50 game years at LOD3 with no humans, so herds,
packs and stands start at their natural equilibrium. Wolves will have thinned the deer, windthrow
gaps will have regrown, and so on. The spin-up's final state is the Y0 state.

### 3.11 Validation (hard asserts) & retry

A world is accepted only if all of these hold. On failure the generator retries (`max_attempts` 16)
with a derived seed. Seeds that still fail are reported to the seed picker, which offers the next
seed. They are never silently accepted.

| ID | Assert |
|----|--------|
| W1 | Land area ∈ [35, 45] km²; 3–10 islets ≥ 1 ha |
| W2 | Exactly one primary estuary with mouth ≥ 300 m and tidal reach ≥ 1.5 km; ≤ 2 estuaries total |
| W3 | 2–4 major rivers each reaching the sea; 2–6 lakes ≥ 2 ha, at least one ≥ 10 ha; ≥ 6 springs |
| W4 | All 8 biomes present, each within its share band |
| W5 | Peak ∈ [900, 1,250] m; ≥ 0.2 km² above 800 m |
| W6 | Tin deposits ∈ {1, 2}; each ≥ 2,000 m path from the landing |
| W7 | Hill iron 2–3, each ≥ 3,000 m straight-line from the breadbasket centroid; ≥ 1 bog iron within 3 km path of the landing |
| W8 | Copper ≥ 2, ≥ 1 within 4 km path of the landing; exactly 1 galena |
| W9 | Landing hard requirements (§3.9) all satisfied |
| W10 | ≥ 98% of main-island land cells reachable on foot from the landing at normal river stage, using fords and the tidal ford; every expedition candidate reachable overland |
| W11 | ≥ 3 expedition candidates meeting §3.9 |
| W12 | Contested-resource rule (§3.8) satisfied |
| W13 | Spin-up populations within the start ranges of §8.1; no species extinct |
| W14 | Fallback node density: ≥ 200 harvestable trees and ≥ 30 forage patches within 600 m of the landing |
| W15 | Generation time within budget (perf CI, not runtime) |

### 3.12 Outputs & schema (sketch)

```csharp
public readonly record struct TerrainCell(          // 8 m attribute grid, 8 bytes packed
    byte Biome, byte Lithology, byte SoilType, byte Fertility,    // fertility ×255
    byte Moisture, byte SlopeDeg, byte WaterClass, byte Trample);  // trample: log-scaled
public sealed record Deposit(int FeatureId, string ResourceId, Vector2 Pos, float RadiusM,
    float RemainingKg, float OriginalKg, float Grade, float RenewalPerYear, int DetectDifficulty);
public sealed record EcoCellState(short Id, float[] Pop /*per species*/, float Wariness,
    float Disturbance, StandAggregate Stand, FishStock[] Stocks);
public sealed record Feature(int FeatureId, FeatureKind Kind, Geometry Shape, int? PoiId);
public sealed record ExpeditionSite(int FeatureId, Vector2 Pos, Dictionary<string,float> CultureAffinity);
public sealed record WorldGenResult(ulong Seed, int Attempt, string SpecId, /* grids, features */
    LandingSite Landing, IReadOnlyList<ExpeditionSite> Candidates, ManifestSeed Manifest);
```

---

## 4. Biomes

Speed multipliers apply to the base gaits in §12.1. Farming suitability is the starting fertility
band. Yields and crop choice belong to [13](13-crafting-and-minigames.md).

| Biome | Terrain | Flora | Fauna | Resources | Hazards | Farming | Building materials | Speed |
|-------|---------|-------|-------|-----------|---------|---------|--------------------|-------|
| **Coast & Dunes** | Beach, shingle, dunes, cliffs, rock pools | Marram, sea beet, wild cabbage, sea kale, thrift | Waterfowl, seabirds\*, fish, shellfish | Flint shingle, sand, seaweed, salt, driftwood, shellfish | Tides, surge, cliff falls, exposure (wind ×1.3) | Poor (0.05–0.15) | Driftwood, beach stone | Beach 0.85, dunes 0.6 |
| **Meadow/Grassland** | Rolling open lowland, chalk downs | Grasses, wild oats, wild flax, yarrow, plantain, wild carrot (+hemlock look-alike) | Deer (grazing), hare, fox | Fiber, herbs, fieldstone, flint (chalk) | Little cover from wind or enemies | Good (0.5–0.85) once sod is broken | Turf, fieldstone | 0.8 |
| **Broadleaf Forest** | Rich lowland woods, glades | Oak, ash, elm, beech, lime, hazel, yew (on chalk/limestone), ramsons, mushrooms, nuts | Deer, boar, bear, wolf, fox; wild bees\* | Timber, coppice, bark, nuts, mushrooms, honey | Bears, boar, getting lost (sight 40 m), falling branches in storms | Good after clearing (0.6–0.85) | Oak/ash timber, wattle, bark | 0.65 (mature high forest 0.75) |
| **Pine Forest** | Drier slopes, sandy ridges | Scots pine, birch, juniper, bilberry, heather | Deer, wolf, bear, fox | Pine timber, resin/pitch, birch bark, bilberries | Wildfire in drought, wolves | Poor (0.25–0.40) | Straight pine logs | 0.75 |
| **Wetland/Marsh** | Fen, bog, reedbed, salt marsh | Reeds, rushes, willow, alder, sphagnum, meadowsweet, valerian, water hemlock | Waterfowl (dense), eels, fish, boar edges | Reeds, peat, **bog iron**, willow, eels, salt (salt marsh) | Bogging (wetness to knee), marsh pools (bad water), ague ([11](11-survival.md)), drowning | None until drained (0.65 after) | Reed thatch, alder piles | 0.4 |
| **River Valley** | Floodplain, terraces, riparian woods | Alder, willow, elm, ash, comfrey, wild hops | Fish (salmon runs), waterfowl, deer, boar | **Best soil**, clay, sand, water, fish | Floods, fords running deep after rain | Best (0.8–1.0) | Clay, elm, willow | 0.75 (flooded 0.4) |
| **Hills & Moor** | Uplands, heath, bog-topped plateaus | Heather, bracken, gorse, birch scrub, thyme, bilberry | Deer, hare, wild goat, wolf | Hill iron, copper, galena, sandstone, peat, pyrite | Exposure, fog, peat bogs | Pasture (0.15–0.35) | Sandstone, slate, turf | 0.65 |
| **Highland/Mountain** | Crags, scree, tarns, snowline | Alpine grass, lichen, stunted pine | Wild goat, bear (dens), wolf | Granite, tin/copper lodes at margins, lookout summits | Falls, cold (lapse rate), snow, whiteouts | None | Granite | 0.5 (scree 0.35; cliffs impassable) |

\*Proposed fauna additions, see [Proposed canon additions](#proposed-canon-additions).

---

## 5. Resources

### 5.1 Catalog

"Extract" names the governing skill and know-how ([12](12-skills-and-professions.md) §8.4), plus
the tools. Work rates, yields and minigames belong to [13](13-crafting-and-minigames.md). Food values
in Satiety (Sat) come from [11](11-survival.md) (1 Sat = 25 kcal).

| Resource | Tier | Where | Abundance & distribution | Renewal | Extract |
|----------|------|-------|--------------------------|---------|---------|
| Deadfall, brush, tinder | T0 | All wooded biomes | 1–4 t/ha of deadwood | +0.5–1.5 t/ha/yr; storms add windfall | Foraging; hands |
| Standing timber | T0 | Forests (§7.1) | 150–300 m³/ha mature | Species growth (§7.2) | Woodcutting; axe (iron, or stone at a time penalty, 13) |
| Coppice poles | T0–T1 | Hazel, ash, willow, alder stools | Understory and wet woods | Cut stools regrow in 3–12 yr | Woodcutting + `coppicing` |
| Bark, bast, birch bark | T0 | Oak (tannin), lime (bast), birch | Common | With the tree | Woodcutting / Leatherworking |
| Resin, pitch, birch tar | T0 | Pine, birch | Common in Pine | Tapping 1/tree/yr | Foraging / Woodcutting |
| Reeds & rushes | T0 | Wetland, river margins | Dense | Annual (cut in Winter) | Foraging |
| Flint / chert | T0 | Chalk cliffs, nodule beds (3–8), shingle | Effectively unlimited; shingle is lower grade | — | Masonry + `knapping` |
| Pyrite ("fire-stone") | T0 | Shale hills, 2–6 outcrops | Uncommon | none | Mining 5; struck on flint, it makes sparks without steel |
| Fieldstone & building stone | T0–T3 | Sandstone and limestone quarries 2–5, granite in Highland, slate 0–2 | Unlimited for practical purposes | — | Masonry / Mining |
| Clay: earthenware / fine / **fireclay** | T1 | Valley banks and wetland edges 4–10 pits; fine clay 2–4; fireclay 1–2 | Large volume | — | Pottery / Mining. Fireclay is required for crucibles and good furnace linings |
| Sand | T1 | Coast, river bars | Unlimited | — | — |
| Peat | T1 | Wetland, wet moor | Vast | None in saga time | Mining or Farming; spade. A fuel alternative to wood |
| Salt | T0–T1 | Sea water anywhere; best on estuary salt marsh pans; brine spring 0–1 | Labor- and fuel-limited | Continuous | Cooking + `salt_curing` (Summer evaporation pans; boiling costs fuel) |
| Copper ore | T1 | Granite/slate margin, 2–4 deposits | §5.2 | none | Mining + `prospecting` |
| Tin (cassiterite) | T2 | Stream placers below the granite, 1–2 | §5.2 (scarce) | none | Mining + `prospecting` (panning) |
| Bog iron | T3 | Wetland bogs, 2–5 | §5.2 (poor) | Slow | Mining; spade and sieve |
| Hill iron ore | T3 | Sandstone/shale hills, 2–3 | §5.2 (good) | none | Mining |
| Galena (lead + silver) | T2–T3 | Metamorphic margin, exactly 1 | §5.2 | none | Mining |
| Sea coal | T3 | Coastal cliff seam, 0–1 | §5.2 | none | Mining |
| Ochre, dye earths | T1 | 2–5 small sites | — | — | Pottery / Textiles |
| Wild plants | T0 | §7.3–7.4 | Patch-based | Annual regrowth; over-digging roots cuts next year's yield 50% | Foraging (identification risk, [11](11-survival.md)) |
| Game (meat, hide, bone, antler, sinew, fat, feathers, fur) | T0 | §8 | Population-based | Breeding | Hunting |
| Fish, eels, shellfish, seaweed | T0 | §8.6 | Stock-based | Breeding / runs | Fishing / Foraging |
| Honey & wax\* | T0 | Hollow trees in Broadleaf, 1 colony per 15–30 ha | Uncommon | Colony regrows 1 yr if not destroyed | Foraging (smoke); bees sting |

### 5.2 Deposits: sizes, grades, renewal

Mining and smelting rates belong to [13](13-crafting-and-minigames.md). These sizes are set so the
**design intents** in the last column hold at the extraction rates 13 adopts. A shared headless
check enforces this (§20).

| Deposit | Count | Size (ore) | Grade | Renewal | Design intent |
|---------|-------|-----------|-------|---------|---------------|
| Copper | 2–4 | 15–40 t each | 4–8% Cu | none | Copper tools for a hamlet for decades; strained at town scale |
| Tin placer | 1–2 | 0.8–2.5 t concentrate | 50–70% Sn | none | **The bronze bottleneck.** Small, far from home, possibly nearer a rival |
| Hill iron | 2–3 | 150–400 t | 40–55% Fe | none | Saga-long iron, far from the breadbasket; who controls the hills controls the smithies |
| Bog iron | 2–5 bogs | 8–25 t each | 20–35% Fe, phosphoric | +4% of original per year (re-precipitation) | Early, near home, **poor**: 13 applies a "cold-short" material penalty |
| Galena | 1 | 20–60 t | ~70% Pb; 0.1–0.3% Ag (→ **20–180 kg silver**) | none | Lead roofing and pewter; **silver for coinage** (answers [15](15-economy-and-trade.md) Q1: local silver exists but is scarce) |
| Sea coal | 0–1 (50%) | 200–800 t | — | none | Optional T3+ forge fuel that relieves charcoal pressure |
| Brine spring | 0–1 | 400 L/day at 8% salt | — | continuous | Inland salt, a minor trade good |

Deposit depletion is visible: outcrops become pits and spoil heaps, and when `Remaining < 10%` the
sim emits `DepositExhausted`, a Chronicle-worthy event.

### 5.3 Prospecting & discovery

Every deposit, herb patch and timber stand has a **detection radius** (outcrops 25 m; placers 0 m,
because you have to pan) and a **difficulty**. When a person passes within the radius, the sim makes
one check per feature per day, using [12](12-skills-and-professions.md)'s `Resolve()`:

| Find | Skill / know-how | Difficulty | Indicator |
|------|------------------|-----------|-----------|
| Copper | Mining + `prospecting` | 35 | Green malachite staining on rock |
| Tin | Mining + `prospecting` | 55, and a panning action in the stream | Heavy black grains in the pan |
| Hill iron | Mining | 30 | Rust-red gossan cap |
| Bog iron | Mining or Metallurgy | 20 | Oily rainbow film, orange-stained water |
| Galena | Mining + `prospecting` | 45 | Heavy grey cubic crystals |
| Yew stand, straight oak | Woodcutting / Bowyery | 25 | — |
| Herbs (§7.3) | Foraging or Healing | 15–40 | — |

A success creates a true **Belief** ([16](16-social-systems.md)) "deposit X at place P" for the
finder. It spreads by talk and on maps. A critical failure by a non-specialist can create a **false
belief** (p = 0.1): pyrite taken for copper, or lichen taken for malachite. Rumors of riches that
aren't there are part of the setting.

---

## 6. Climate, weather & seasons

### 6.1 Calendar, daylight & moon

Let `d` = day-of-year, **1…32 (Spring 1 = 1)** — the canonical convention shared with [13](13-crafting-and-minigames.md). Solar noon is at 12:00.

- **Daylight:** `L(d) = 12 + 4·sin(2π(d − 3)/32)` hours. Sunrise = `12 − L/2`.
- **Moon** *(proposed)*: one cycle per season. New moon on day 1, first quarter on day 3, full moon
  on day 5, last quarter on day 7. Spring tides fall on days 1 and 5. Night visibility without light
  is 25 m at full moon, 12 m at quarters and 5 m at new moon, halved under cloud.

| Day | Daylight | Sunrise–sunset | | Day | Daylight | Sunrise–sunset |
|-----|----------|----------------|-|-----|----------|----------------|
| Spring 1 (Landfall) | 10.5 h | 06:45–17:15 | | Autumn 1 | 13.5 h | 05:15–18:45 |
| Spring 3 (equinox) | 12.0 h | 06:00–18:00 | | Autumn 3 (equinox) | 12.0 h | 06:00–18:00 |
| Spring 8 | 15.3 h | 04:20–19:40 | | Autumn 8 | 8.7 h | 07:40–16:20 |
| Summer 3 (midsummer) | 16.0 h | 04:00–20:00 | | Winter 3 (midwinter) | 8.0 h | 08:00–16:00 |
| Summer 8 | 14.2 h | 04:55–19:05 | | Winter 8 | 9.8 h | 07:05–16:55 |

### 6.2 Temperature

**Sea-level daily mean** (lagging daylight by two days): `T̄(d) = 8.5 + 8.5·sin(2π(d − 5)/32)` °C.

| Season | Day 1 | Day 4 | Day 8 | Season mean |
|--------|-------|-------|-------|-------------|
| Spring | 2.5 | 6.8 | 13.2 | 7.8 |
| Summer | 14.5 | 16.8 | 15.6 | 16.1 |
| Autumn | 14.5 | 10.2 | 3.8 | 9.3 |
| Winter | 2.5 | 0.2 | 1.4 | 0.9 |

The hourly air temperature at a cell is

```
T(cell, t) = T̄(d) + Y + a(t)                       // year-type offset, front anomaly
           + D·cos(2π(hour − 15)/24)·cloudF·coastF  // diurnal swing
           − 6.5·elev_km                            // lapse rate
           − 1.5·[frost hollow ∧ clear night]
```

The terms are:

- **D** is the diurnal amplitude: Spring 4, Summer 5, Autumn 3.5, Winter 3 °C.
- **cloudF** is 1.3 Clear, 0.7 Cloudy, 0.6 Fog, 0.5 Rain/Storm.
- **coastF** is 0.7 within 500 m of the sea.
- **a(t)** is a front anomaly, AR(1) per 6-hour slot: `a' = 0.85a + N(0, σ)`, with σ = 1.2 / 1.0
  / 1.2 / 1.5 °C (Sp/Su/Au/Wi). That gives a stationary SD of about 2–3 °C.
- **Y** is the year-type offset (§6.4).

**Sea water:** 8 / 13 / 12 / 7 °C by season (smoothly interpolated). **Rivers:** air temperature
lagged 24 h, with a minimum of 1 °C. [11](11-survival.md) uses both for immersion.

*Worked example:* Y0 Spring 1 at 04:00 on the beach (elevation 3 m), Clear, normal year,
anomaly −1 → `2.5 + 0 − 1 + 4·cos(2π·(−11)/24)·1.3·0.7 = 2.5 − 1 − 3.5 = −2.0 °C`. Landfall's
first pre-dawn is at freezing, which is why the first fire matters ([11](11-survival.md)).

### 6.3 Weather model

There is one region-wide weather state per **6-hour slot** (00, 06, 12, 18), interpolated hourly.
Short slots matter because a season is only 8 days long. Fronts last 1–6 slots, so a season sees
several weather systems rather than one.

| Season | Clear | Cloudy | Fog | Drizzle | Rain | Storm |
|--------|-------|--------|-----|---------|------|-------|
| Spring | 25% | 30% | 10% | 15% | 15% | 5% |
| Summer | 40% | 30% | 5% | 10% | 12% | 3% |
| Autumn | 20% | 28% | 15% | 15% | 15% | 7% |
| Winter | 22% | 33% | 10% | 12% | 15% | 8% |

**Transition:** with probability `p_stay` the state persists into the next slot (Clear 0.6,
Cloudy 0.6, Fog 0.4, Drizzle 0.5, Rain 0.5, Storm 0.35). Otherwise the next state is drawn from the
season's distribution, excluding the current state and reweighted by adjacency: Storm only follows
Cloudy, Drizzle or Rain; Fog only in the 00 and 06 slots, burning off by 12 with p = 0.7 unless
Autumn. The stationary shares converge to the table within ±3 points (tested, §20).

*Implementation (M2-03, `Sim/Climate/Weather.cs`):* the chain is keyed per slot index (`RngStream.Weather`), so
catching up equals stepping. "Drawn from the season's distribution" is realised as **leave weights fitted at start-up**
so that the periodic (00/06/12/18) chain, with its stay probabilities and adjacency rules, has the table's stationary
shares — drawing the leave state straight from the table under-weights the short-stay states (Fog, Storm). Fog forms only
in the 00 and 06 slots; outside Autumn it burns off at 12 with p = 0.7 and is gone by 18. Wet/Dry summers (§6.4) get
their own fitted weights from the shifted table. `feudalsim weather` runs the chain (3,000 years: worst share error 1.5
points; season means 7.8 / 16.2 / 9.2 / 0.8 °C).
**Finding (Q17):** a Dry summer's Clear +20 (60%) is unreachable with Clear's p_stay 0.6 — with every exit returning to
Clear the ceiling is ≈ 56%. *Applied (reversible, pending owner):* in a Dry summer Clear's p_stay is 0.75.

| State | Precip (mm/h) | Wind (m/s) | Humidity | Notes |
|-------|---------------|-----------|----------|-------|
| Clear | 0 | 2–5 | 65% (55% on Summer afternoons) | Cold nights |
| Cloudy | 0 | 3–7 | 80% | — |
| Fog | 0.03 | 0–2 | 100% | Sight 40–80 m; worst at coast in Spring/Summer and valleys in Autumn |
| Drizzle | 0.2 | 2–6 | 92% | — |
| Rain | 1.0 | 4–9 | 95% | Summer Rain: hail chance 5%/slot |
| Storm | 3.0 | 12–22 (gale) | 95% | Hail 10%/slot in Summer; storm surge; windthrow |

**Precipitation type:** snow if `T ≤ 0.5 °C`, sleet between 0.5 and 2 °C (counted as rain for
wetness, 50% for snow accumulation), rain above 2 °C. Highland multiplies precipitation by its
rain-shadow factor. Prevailing wind is from the west/south-west, veering with fronts. Local wind is
the regional wind × the exposure factor (§3.7).

**Forecast as belief.** NPCs (and the player, through NPC talk) form a belief about the next two
slots. The chance it is right is `0.55 + 0.004·max(Fishing, Farming)`, capped at 0.95. Old fishers
are right about rain; that is a reason to listen to them.

### 6.4 Year types & special events

Rolled on each Spring 1 from `Rng("weather.year")`.

| Roll | Options (probability) | Effect |
|------|----------------------|--------|
| **Summer type** | Normal 60%, Wet 20%, Dry 15%, Hot 5% | Wet: Rain/Drizzle +10 points each, Clear −20; Dry: precipitation ×0.4, Clear +20; Hot: Y +2.5 °C in Summer, heat spells |
| **Winter severity** | Mild 25%, Normal 55%, Hard 20% | Y = +2 / 0 / −3 °C in Winter; Hard adds one guaranteed cold snap. **Y0 Winter is capped at Normal** unless the world option `weather.y0_winter_cap` is raised (a world-creation setting, not a canon difficulty mode; [19](19-player-experience.md) decides whether to expose it) |
| **Late frost** | 30% per year | One night in Spring 5–8 at −2 to −4 °C. Blossom and seedling damage per [13](13-crafting-and-minigames.md) |
| **Mast year** | 25% per year | Acorns and beech mast ×3 in Autumn; boar births +40% the next Summer |

| Event | Window | Chance | Effect |
|-------|--------|--------|--------|
| Cold snap | Winter (and Autumn 7–8) | 1–2 per Winter (Hard +1) | Anomaly −6 to −9 °C for 4–12 slots; rivers ice at the edges; wolves bolder |
| Gale | Autumn–Winter | Every Storm slot with wind ≥ 18 m/s | Windthrow (§7.2), roof damage ([14](14-technology-and-buildings.md)), wreck breakup ([11](11-survival.md)), no small-boat launches |
| Heat spell | Summer | 0–1 per year (Hot year: 2) | +5 °C for 4–8 slots; heat stress ([11](11-survival.md)) |
| Drought | Dry Summer | — | Streams below 1 km² catchment dry up; wildfire risk in Pine and Moor (§19) |
| Flood | Wet Autumn or Spring melt | When stage exceeds bank height | Floodplain inundated 1–3 days |

### 6.5 Snow

Snow depth `S` (cm) is tracked per altitude band (low/mid/high), with drift ×1.5 in lee cells and
×0.4 under pine canopy.

- **Accumulation:** +1 cm per 0.1 mm of snow-water per hour (so +2 cm/h in Rain-intensity snowfall
  and +6 cm/h in a blizzard). Sleet counts half.
- **Melt:** −0.6 cm per degree-hour above 0 °C, and −1.5 cm per mm of rain on snow.
- **Typical outcome:** lowland snow lies 0–4 days per Winter (0–25 cm). Mid band 4–8 days. High band
  covered from Autumn 6 to Spring 4. Permanent snow only above about 1,100 m.

| Effect of depth | Rule |
|-----------------|------|
| Travel | Speed × (1 − min(0.6, S/50)) on foot; sled travel becomes efficient (§12.5) |
| Foraging | Blocked when S > 15 cm, except bark, rosehips and pre-dug roots |
| Hunting | Tracks visible: +15 detection (13 consumes) |
| Roofs | Load per [14](14-technology-and-buildings.md) |
| Falls | −2 m effective height when landing in S ≥ 30 cm ([11](11-survival.md)) |

### 6.6 Effects of weather

| Effect on | Rule | Owner of the consequence |
|-----------|------|--------------------------|
| **Outdoor work speed** | Drizzle ×0.95, Rain ×0.85, Storm ×0.5 (most outdoor tasks suspended), snow > 20 cm ×0.8, night with torch/fire ×0.6 | Applied by [13](13-crafting-and-minigames.md) |
| **Blocked work** | Open-air firing, charcoal clamps and smelting are blocked in Rain/Storm; hide drying, thatching and haymaking are blocked in any precipitation | [13](13-crafting-and-minigames.md), [14](14-technology-and-buildings.md) |
| **Fires** | An unsheltered fire cannot be lit in Rain/Storm. A burning fire uses fuel ×1.5 in Rain and has a 20%/h chance of going out in an unsheltered Storm | [11](11-survival.md) |
| **Crops** | Daily record per cell: `tMin, tMax, tMean, precipMm, frost, snowCover, wind, storm, hail, humidity, daylightH, PET` | [13](13-crafting-and-minigames.md) integrates soil moisture and growth |
| **Travel** | Mud on unpaved trails and paths ×0.8 for 12 h after ≥ 3 mm of rain; fords per §12.4; fog: sight 40–80 m and getting lost (§10) | This doc |
| **Body** | Air temperature, wind, precipitation and humidity at a person's position | [11](11-survival.md) (warmth, wetness) |
| **Mood** (inputs suggested to [21](../tech/21-npc-ai.md)) | Sunny Summer day +3; Storm −4; 3+ consecutive wet days −2 ("dreary", stacking to −6); first snow +2 for children; fog −1 | [21](../tech/21-npc-ai.md) owns mood |
| **Perception & stealth** | Rain masks sound (hearing radius ×0.6); fog cuts sight; snow and moonlight help night sight | [16](16-social-systems.md) witnesses, [18](18-conflict-and-warfare.md) |
| **Sea state** | Calm (Clear/Cloudy/Fog), Moderate (Drizzle/Rain), Rough (wind ≥ 10), Gale (Storm) | §12.5 |

---

## 7. Flora

### 7.1 Trees & wood properties

Properties are on a 1–10 scale and feed [13](13-crafting-and-minigames.md)'s material model.
"Years" are game years (§1.2).

| Tree | Biomes | Strength | Toughness/flex | Rot resistance | Workability | Fuel/charcoal | Years to use | Signature uses |
|------|--------|---------|----------------|---------------|-------------|---------------|-------------|----------------|
| **Oak** | Broadleaf, River Valley | 9 | 6 | 9 | 4 | 9 | 80–120 for great timber; 25 for poles | Frames, posts, ships, barrels, shingles, best charcoal; bark (tannin); galls (ink) |
| **Ash** | Broadleaf, valley | 7 | 9 | 3 | 7 | 8 | 40; coppice 8–12 | Tool handles, spear shafts, oars, wheel rims, currach frames |
| **Elm** | River Valley | 7 | 7 | 8 (wet) | 4 | 6 | 50 | Hubs, keels, water pipes, coffins, serviceable bows |
| **Yew** | Broadleaf on chalk/limestone; rare on hills (≤ 3 stands) | 7 | 10 | 10 | 6 | 5 | 100+ (staves come only from mature trees) | **Bow staves.** Foliage and seeds are toxic to livestock and people ([11](11-survival.md)) |
| **Scots pine** | Pine, Hills | 5 | 5 | 6 (heart) | 8 | 6 | 40–60 | Beams, planks, masts, resin, pitch, torches |
| **Birch** | Edges, Pine, Moor | 4 | 5 | 2 | 8 | 7 | 20–30 | Bark (tinder, containers, roofing under turf), tar glue, sap, brooms |
| **Willow** | Wetland, River | 2 | 8 | 2 | 9 | 3 | Coppice 2–3 | Baskets, withies, wattle, fish traps, bark (pain relief) |
| **Hazel** | Broadleaf understory | 3 | 8 | 2 | 9 | 5 | Coppice 5–7 | Wattle, hurdles, pack frames, nuts |
| **Alder** | Wetland, riverbanks | 3 | 4 | 9 (submerged) | 7 | 7 | Coppice 10–15 | Piles and foundations in water, clogs, dye |
| **Beech** | Broadleaf (well-drained) | 7 | 6 | 2 | 7 | 9 | 60 | Furniture, tool bodies, firewood; mast for pigs |
| **Lime (linden)** | Broadleaf | 3 | 4 | 2 | 10 | 4 | 40; coppice 10 | **Bast fiber** (rope), carving, shields |
| Crab apple | Woodland edges | 6 | 5 | 4 | 6 | 8 | 15 | Fruit, grafting rootstock, mallets |
| Hawthorn / blackthorn | Edges, scrub | 6 | 5 | 5 | 4 | 8 | 10 | Stock-proof hedges, cudgels, haws, sloes |

### 7.2 Forest dynamics

- **Node level** (resource chunks): each tree has a species, an age, a size class (sapling, pole,
  timber, veteran) and a state (standing, felled, stump, coppice stool). On Spring 1 each tree ages
  one year and advances its size class on the species schedule. Felled stumps of coppicing species
  become stools that produce poles after their cycle.
- **Stand level** (EcoCell aggregates used at LOD3): standing volume by species group and age class.
  Recruitment fills gaps at 2% of the gap area per year when deer browsing is below 15 deer/km²
  (heavy browsing stalls regeneration, a real dynamic that gives hunting a forestry payoff).
- **Succession** of abandoned cleared land: meadow (1 yr) → scrub of bramble, hawthorn and birch
  (3–5 yr) → young wood of birch, hazel and ash (8–15 yr) → high forest (40+ yr). Grazing holds
  meadow.
- **Windthrow:** each gale hour topples 0.05% of exposed mature trees. Windfall can be cut for
  timber without felling.
- **Depletion signal:** when the timber-class volume within 1 km of a settlement falls below 25% of
  its Y0 value, the sim emits `WoodlandDepleted`. Firewood and timber haul distances then rise
  (§1.3), which is the historical driver for coppice management and forest law
  ([17](17-governance-and-law.md)).
- **Old-growth oak is effectively non-renewable within a saga** (80–120 years). The best stands are
  a strategic resource for great halls, ships and castles.

### 7.3 Herbs, medicinal & toxic plants

Effects apply only through [11](11-survival.md)'s treatment rules and need the `herbal_remedies`
know-how ([12](12-skills-and-professions.md)). "Confusable" is the misidentification pairing used by
11's foraging-ID check.

| Plant | Biome | Season | Use (applied by 11) | Confusable with (risk) |
|-------|-------|--------|---------------------|------------------------|
| Yarrow | Meadow, Coast | Sp–Au | Wound poultice: bleeding −30%, infection −25% | — |
| Plantain | Meadow; **grows on trampled paths** | Sp–Au | Poultice: infection −20% | — |
| Comfrey ("knitbone") | River Valley, wetland edge | Sp–Su | Fracture/bruise healing ×1.2 | **Foxglove** leaves (0.15) |
| Willow bark | Wetland, River | All | Pain −30%, fever −20% | — |
| Ramsons (wild garlic) | Damp Broadleaf | Sp | Fresh food; infection −15% (dressing) | **Lily of the valley** (0.15) |
| Feverfew | Meadow, Coast | Su | Fever −25% | Chamomile (harmless) |
| Meadowsweet | Wetland, River Valley | Su | Fever; eases flux symptoms | — |
| Elder (flower, berry) | Edges | Su / Au | Winter-cough course −20% | Dwarf elder (mildly toxic, 0.1) |
| Wild thyme | Moor, Coast | Su | Cough; mild antiseptic | — |
| Sphagnum moss | Wetland, Moor | All | Absorbent dressing: bandage quality +, infection −15% | — |
| Valerian | Wetland, River | Su–Au | Sleep quality +15% | **Water hemlock** (0.20) |
| Water mint | Wetland | Su | Stomach; Comfort | — |
| Honey\* | Wild bee colonies | Su–Au | Wound dressing: infection −40% | — |

**Toxic plants & fungi** (effects in [11](11-survival.md)): hemlock (Meadow, looks like wild
carrot/parsnip), water hemlock (Wetland, the deadliest), deadly nightshade (woodland edges,
tempting berries), foxglove (medicinal only at Healing ≥ 60), lily of the valley, yew (foliage and
seeds), death cap (looks like field mushroom; Broadleaf, Autumn), fly agaric (hallucinogen), and
summer shellfish toxin (a "red tide" in Summer 3–8 with p 0.3 per year, closing the shellfish beds).

### 7.4 Wild foods, crop ancestors & phenology

Wild ancestors exist so that seed failure is survivable but miserable. Collected wild seed can start
a field at a yield multiplier set by [13](13-crafting-and-minigames.md) (suggested 0.4–0.6),
improving through selection.

| Wild ancestor | Biome | Domestic analogue |
|---------------|-------|------------------|
| Wild oats | Meadow | Oats |
| Sea beet | Coast | Beet, chard |
| Wild cabbage | Coast cliffs | Cabbages, kale |
| Wild carrot / wild parsnip | Meadow, chalk | Carrot, parsnip (hemlock risk) |
| Wild pea / vetch | Meadow | Peas, beans (poor) |
| Wild flax | Meadow | Flax / linen |
| Wild hops | River Valley edges | Hops (Brewing) |
| Wild strawberry, crab apple, bramble, sloe, bilberry, hazelnut, acorn (leached), beech mast | Various | Orchard and forage |

**Phenology calendar** (what is available and what the animals do):

| Season | Plants | Animals |
|--------|--------|---------|
| Spring | Nettle, ramsons, sorrel and birch sap (days 1–5); blossom (days 5–8, late-frost risk); herb shoots | Waterfowl and seabird eggs (days 3–8); **bears wake hungry (days 1–3)**; wolves den (territorial near dens); trout; migrant geese leave (day 4) |
| Summer | Strawberries, bilberries (day 4+); herbs at peak potency; flax; hay | **Deer calve** (days 1–3); boar piglets (sows dangerous); **herring shoals** (Summer 5 – Autumn 3); shellfish-toxin risk |
| Autumn | Blackberries, sloes, crab apples, hazelnuts, acorns and mast (mast year ×3); mushrooms after rain (death-cap risk) | **Deer rut** (days 2–5, stags aggressive); **salmon run** (days 2–6); eel run (days 4–8); geese arrive (day 3); bears fatten |
| Winter | Roots (dig before the ground freezes), bark (famine food), rosehips | Deer gather in sheltered **wintering yards**; wolves larger and bolder in Hard winters; bears hibernate (Winter 1 – Spring 2); fish: inshore cod |

---

## 8. Fauna & ecology

### 8.1 Species

Food values per carcass assume normal butchery. Combat profiles are in
[18](18-conflict-and-warfare.md) §3.6. Start ranges are post-spin-up totals for the island.

| Species | Primary habitat | Start population | Group | r (/yr) | Carcass yield | Products |
|---------|-----------------|------------------|-------|---------|---------------|----------|
| **Deer** (red deer) | Broadleaf, meadow edge, moor, valley | 450–750 | Hinds + young 5–15; stags alone or 2–4 | 0.30 | ~2,000 Sat (35 kg meat) | Hide, antler, sinew, bone, tallow |
| **Boar** | Broadleaf (mast), wetland edge | 180–320 | Sounders 6–20; boars solitary | 0.60 (+0.4 after a mast year) | ~3,000 Sat | Hide, bristle, tusk, lard |
| **Hare** | Meadow, moor, dunes | 2,000–4,000 | Solitary | 1.5 | ~80 Sat | Fur |
| **Wolf** | Forest, hills, highland | **2–3 packs**, 10–20 wolves | Pack 4–8; territory 10–15 km² | 0.30 | Not eaten | Pelt (status cloak) |
| **Bear** (brown) | Broadleaf, pine, highland dens | 5–9 | Solitary; sow + 1–3 cubs | 0.15 | ~8,000 Sat | Fur, fat (lamp oil), claws |
| **Fox** | All lowland | 40–70 | Pairs | 0.6 | — | Pelt; raids poultry |
| **Wild goat** | Highland, hill cliffs | 120–200 | Herds 5–20 | 0.35 | ~800 Sat | Hide, horn; tameable ([13](13-crafting-and-minigames.md)) |
| **Waterfowl** (ducks, geese) | Wetland, lakes, estuary | 300–600 resident; migrant geese ×3 in Autumn 3 – Spring 4 | Flocks | 1.0 | Duck ~50, goose ~150 Sat | **Feathers (fletching)**, down, eggs in Spring |
| **Fish** | Rivers, lakes, estuary, coast | Stocks (§8.6) | — | — | Trout 28, salmon 240, herring 16, cod 130, eel 58 Sat | Oil, bone |

### 8.2 Population model (all LOD tiers)

Per EcoCell `c` and species `s`, updated **once per game day**:

```
N' = N + B − M − P − H + Mig_in − Mig_out
B   = r_s · N · (1 − N / K_s,c) · pulse_s(d)            // births concentrated in the species window (deer: Summer 1–3)
M   = m_s(winterSeverity, snowDepth) · N                 // e.g., deer +10%/yr extra mortality in Hard winters, spread over Winter days
P   = predation by wolves/bears/foxes (Holling type II on prey density)
H   = kills reported by 13's hunting resolution or LOD0 kills
Mig = 3%/day diffusion toward neighbours weighted by suitability · (1 − disturbance)
K_s,c = baseDensity_s · habitatSuitability_s(biome mix of c) · area(c)
```

**Wolves** need about 8 deer-equivalents per wolf per year (60% deer, 25% boar young, 15% goats and
hares). With 15 wolves this holds deer at about 0.7·K. If prey falls below 40% of K in Winter, a
pack's **livestock-depredation** chance rises (§8.5). The colony's hunting therefore pushes wolves
toward the colony's goats, which is an emergent conflict.

**Sustainable yield** (a design check, not a rule): deer MSY ≈ 0.075·K ≈ 60/yr and boar ≈ 50/yr.
Together that is about 270k Sat/yr, or ~90 person-years of food at [11](11-survival.md)'s 3,040 Sat
per person-year. Hunting can carry the 24-person colony in Year 0 and a fraction of a 300-person
town. It cannot carry a realm.

### 8.3 Embodiment

Animals are **numbers until they are near someone**. When an EcoCell overlaps a LOD0/LOD1 bubble,
groups are spawned from its population with a cell-seeded RNG, placed by habitat and time of day
(deer at dawn and dusk in glades, boar in wallows, wolves near prey). On despawn the survivors fold
back into `N`. Kills, wounds and flight all write back. NPC hunters at LOD1–LOD2 never spawn
animals. They query the encounter function below, and [13](13-crafting-and-minigames.md) resolves
the hunt.

```
encounters per game hour of active search in cell c
  = D_groups(c)/km² · sweep_km² per hour · (1 − 0.6·W_c) · naivety
sweep = 2 · detectRadius_km · searchSpeed_km/h      // detect 0.04 km in forest, 0.15 km in open; stalking 0.15 km/h
```

### 8.4 Hunting pressure, naivety & depletion

- **Naivety:** Farstrand's animals have never met people. Each species starts with `naivety = 1.5`
  (encounters ×1.5, flight distance ×0.5). Per EcoCell, it decays by 0.02 per kill or flush event,
  down to 1.0. The first months are generous; the generosity runs out where people hunt.
- **Wariness `W_c`** (0–1): +0.08 per kill or flush, −5% per day. It increases flight distance and
  cuts encounters.
- **Disturbance:** from settlement population within 1 km, foot traffic and noise (smithies,
  felling). Animals diffuse away from disturbed cells, so even without hunting there is a **0.5–1 km
  depletion ring** around settlements. Hunters have to range farther every year.
- **Overhunting:** if a species falls below 15% of its Y0 total, the sim emits `GameScarce`, a
  rumor and Chronicle seed and an input to forest/hunting law ([17](17-governance-and-law.md)).
  Local extinction in an EcoCell is possible. Island-wide extinction is possible for bears and
  wolves (and is recorded).

### 8.5 Predation & danger

The fight itself belongs to [18](18-conflict-and-warfare.md) §3.6. This table sets **when** an
animal turns aggressive and how dangerous it is (1–5).

| Species | Default | Aggression triggers | Danger |
|---------|---------|---------------------|--------|
| Wolf | Avoids people | Pack hungry (prey < 40% K) **and** Winter/night **and** the target is alone, a child, injured, downed or livestock; den defense (Spring); cornered | 3 (pack 4) |
| Bear | Avoids | Surprised within 15 m; a sow with cubs within 40 m; Spring 1–3 hunger near food smells (stores, carcasses, hives); defending a kill; entering an occupied den | 5 |
| Boar | Flees | Wounded; cornered; a sow with piglets (Summer); crop-raiding at night | 4 |
| Deer (stag) | Flees | Rut (Autumn 2–5) within 10 m; cornered while wounded | 2 |
| Wild goat | Flees | Cornered on a ledge (risk of shoving people off: a fall, per [11](11-survival.md)) | 1 |
| Fox, hare, waterfowl | Flee | Geese defend nests (Spring) | 0–1 |

**Livestock depredation** (daily, per herd): `p = 0.02 · packHunger · (1 − guard) · proximity`,
where `guard` is 0.5 with a penned enclosure, 0.8 with a dog\* or watch, and 0.95 both. Bears raid
hives and stores in Spring and Autumn with `p = 0.03` per night near forest edges. These raise
Safety alarms and shift opinion toward whoever proposes a wolf hunt.

### 8.6 Fish & shellfish

| Stock | Where | Size | Dynamics |
|-------|-------|------|----------|
| River trout | Streams, rivers | ~100 kg per river-km | Logistic, r = 0.6 |
| **Salmon run** | Each major river, Autumn 2–6 | 200–800 fish (≈ 50–190k Sat) | Next year's run = base × min(1, escapement/0.5) (1-year lag); weirs that take > 50% shrink later runs |
| Eel run | Wetland outflows, Autumn 4–8 | 300–900 eels | Same pattern |
| **Herring shoals** | Coast, Summer 5 – Autumn 3 | Effectively limited by catch and preservation capacity | Re-seeded each year |
| Inshore cod & flatfish | Reefs, estuary | Per-cell stocks | r = 0.5; can be depleted near harbors |
| Shellfish beds (mussels, cockles, oysters) | Estuary, rocky shores | 4–10 beds | r = 0.4; low water only; Summer toxin risk |
| Seaweed | Rocky shores | Abundant | Annual; food and fertilizer |

---

## 9. Points of interest & landmarks

POIs are features with stable IDs. They anchor names (§11), knowledge (§10), events and quests that
the sim generates.

| POI | Count | Placement | Gameplay |
|-----|-------|-----------|----------|
| **Wreck of the *Wending Star*** | 1 | Landing reef | Salvage window, then timbers and scrap; breakup model in [11](11-survival.md) |
| Flotsam strands | 6–12 | Coast downdrift of the wreck (longshore current) within 5 km | Washed-up cargo from wreck sections lost in storms. Early exploration rewards |
| Waterfalls | 1–3 | Rivers crossing hill breaks | Landmarks; prime mill sites ([14](14-technology-and-buildings.md)) |
| Gorges | 0–2 | Rivers through hills | Fall hazard; natural defenses; bridge sites |
| Caves | 3–8 | Limestone, highland | Shelter, bear dens, outlaw hideouts, cold storage |
| Sea stacks & sea caves | 2–5 | Cliffs | Landmarks; seabird colonies\* |
| Tidal islands | 0–2 | Near shore | Defensible refuges reachable at low water |
| Great trees | 1–3 | Veteran oak or yew | Landmarks; folklore anchors; the "do we fell it?" dilemma |
| Lookout summits | 2–4 | High points with wide views | **Survey** action (§10) |
| Natural harbors | 1–3 | Sheltered deep bays | Expedition sites, ports |
| Salt flats | 1–2 | Estuary | Salt pans |
| Red-water bogs | = bog-iron count | Wetland | Visible indicator of bog iron |
| Deer wintering yards | 1–3 | Sheltered valley woods | Winter hunting hotspots |
| Fish-weir sites | 2–5 | River narrows | Salmon and eel harvest |
| Failed-colony ruins | 0 at start | Emergent: any settlement whose population reaches 0 | Real ruins with history, loot and graves. This is the canon-safe answer to "ruins" (Open questions) |

---

## 10. Exploration, knowledge & mapping

- **Personal map knowledge.** Each person has a 128 m knowledge-tile bitset with states `unknown`,
  `heard of` and `seen`. Tiles within sight become `seen` as the person moves (sight radius by
  terrain and fog). Feature-level knowledge (a deposit, a ford, a den) is a **Belief**
  ([16](16-social-systems.md)) and can be false or out of date.
- **Survey:** at a lookout summit or high ground, spending 1 game hour (a Perception check) marks
  every tile with line of sight within 3 km as `seen`. Biomes and major features are revealed, but
  not deposits.
- **Getting lost:** off-path in forest or fog, each game hour carries
  `p = 0.15 · fog · (1 − familiarity of the tile)`. A lost person's route deviates and they burn
  time. Landmarks and paths reduce it. NPCs get lost too (parity), and search parties are an emergent
  job ([21](../tech/21-npc-ai.md)).
- **Telling:** in conversation, people share features by talk. The sim transfers beliefs as `heard
  of` tiles plus feature beliefs ([16](16-social-systems.md) rules decide what is told). Scouts'
  reports are how a camp learns its land.
- **Drawn maps** (T1+, Letters ≥ 20 and `reading_writing`): a map item stores a snapshot of the
  maker's knowledge, with positional error `σ = 120 m · (1 − Letters/100)` and the maker's names
  (§11). Reading a map transfers its tiles and features as beliefs, errors included. Maps are
  tradeable, stealable and valuable to war ([18](18-conflict-and-warfare.md)).
- **Player map UI** ([19](19-player-experience.md)) shows only the player's beliefs. There is no
  omniscient minimap.

*Implemented (M2-01c-ii):*
- **Store:** `World/Knowledge.cs`, 2 bits per 128 m tile (1 KB a person), saved and hashed. States only rise.
- **Sight:** `KnowledgeSystem` marks every tile within sight of each person who can act — 150 m, 40 m in fog, halved
  at night. It is stateless; LOD0/1 people mark once a game minute and LOD2 people when they are due
  (2.4 µs per step at S6).
- **Not yet:** survey, getting lost, telling and maps come with the systems that use them.

---

## 11. Place naming

Places start nameless and acquire names the way real places did, from events, people and
description. The sim records names; language models only *propose* candidates.

**Data:** each Feature has `NameCandidate{text, origin, proposerId, cultureId, createdAt}` and a
**usage counter per settlement**.

**Candidates are created when:**

| Trigger | Origin type | Example |
|---------|------------|---------|
| First discovery (the finder) | Descriptive | "Redwater", "the Long Meadow", "Grey Tarn" |
| Salient event at the feature (memory salience ≥ 70: death, first bear kill, battle, wedding, miracle claim) | Event | "Drowned Lord's Reef", "Bear's Bane Hollow" |
| Honor (discoverer, a leader, a beloved dead person) | Person | "Hesketh's Ford", "Margery's Well" |
| Homeland nostalgia (settlements, by leaders) | Homeland | "New Wendmoor" |
| Player-proposed (typed or chosen) | Any | Validated like any other candidate |

**Adoption.** Whenever a person refers to the feature (orders, gossip, rumors, Chronicle facts), the
sim picks the name they use. It is their own preferred candidate with p 0.7, otherwise the
settlement's most-used candidate, with conformity raising the second option
(`+0.2·(Tradition+Sociability−100)/100`). That use increments the counter. A candidate becomes the
settlement's **common name** when it holds ≥ 60% of the last 64 references (and ≥ 20 total), or when
a recognized leader decrees it ([17](17-governance-and-law.md)) and it then gets 8 uses. A later
event with salience ≥ 85 adds a strong new candidate (initial weight 10 references), so names can
change: "Hesketh's Ford" can become "Bloody Ford" after a massacre.

**Different polities, different names.** Each settlement keeps its own counters, so the Osmeri may
call the Varrowan "Long Meadow" *Pratolungo*. Maps and envoys carry names across. Name disputes are
cosmetic, but they make the world feel inhabited and feed rivalry barks.

**Settlement names:** the camp starts as "the Landing" or "Wreck Camp". The same machinery applies.

**Generation & fallback:** the LLM gets structured facts (feature type, salient events, involved
people, the culture's naming style) and returns 3 candidates. The sim validates them (length 3–24,
allowed charset, a profanity/real-world-name filter, no duplicates on the island) and the proposer
takes the first valid one. In template mode a per-culture grammar is used:

| Culture | Grammar sketch |
|---------|---------------|
| Varrow | `[Adj|Animal|Person's] + {ford, mere, ham, stead, wick, den, holt, combe, ridge}` → "Wolfcombe" |
| Osmeri | `{Porto, Sal, Val, Isola} + [descriptor]` / `-mar, -ena` |
| Brannoch | `{Ben, Glen, Loch, Dun, Strath, Inver} + [descriptor]` |
| Ashen | Virtue + place: "Patience Vale", "Ashfall", "Mercy Spring" |

---

## 12. Travel

### 12.1 Locomotion speeds *(proposed canon)*

Speeds are embodied m/s. "Per game hour" is at the default 30-minute day (×75 s). The same speeds
apply at every LOD (parity, and seamless promotion per canon §8.2).

| Gait / mode | m/s | m per game hour | Notes |
|-------------|-----|-----------------|-------|
| Walk | 1.6 | 120 | Default when carrying or working |
| **Jog (travel pace)** | 4.0 | **300** | Default overland pace; counts as "moderate" activity for needs ([11](11-survival.md)) |
| Sprint | 6.5 | 490 | Uses stamina ([11](11-survival.md)) |
| Burdened / overloaded | 1.3 / 0.9 | 98 / 68 | Encumbrance per [11](11-survival.md) |
| Horse, travel gait | 6.0 | 450 | About 6 game h/day before horse fatigue; carries rider + 30 kg |
| Horse, gallop | 11 | 825 | Short bursts |
| Pack horse / ox cart | 1.6 / 1.2 | 120 / 90 | 100 kg / 500 kg on tracks |
| Handcart / sled on snow | 1.3 | 98 | 150 kg on a path or track / 100 kg on snow |
| Dugout or currach (paddled) | 2.0 | 150 | 150–250 kg |
| Plank rowing boat (2–4 rowers) | 2.5 | 188 | ~400 kg |
| Small sailing boat (fair wind) | 5.0 | 375 | ~600 kg; beating upwind ×0.5 |

### 12.2 Terrain cost

`speed = base · biomeMult (§4) · surfaceMult · slopeMult · snowMult · lightMult`

- `surfaceMult`: built road 1.0 in all weather; track 1.0 (0.8 in mud); path 0.95 (0.8 in mud);
  trail 0.85; otherwise the biome multiplier applies alone.
- `slopeMult` (Tobler): `exp(−3.5·|s + 0.05|) / exp(−0.175)`, where `s` is the signed grade along
  travel. Cliffs over 45° are impassable.
- `lightMult`: 0.6 off-path at night without light, 0.8 on path (moon per §6.1); a torch or lantern
  gives 1.0 within its 8 m radius.

*Example:* jogging up a 10% grade through broadleaf forest in daylight →
`4.0 · 0.65 · exp(−0.525)/exp(−0.175) = 4.0 · 0.65 · 0.705 = 1.83 m/s` ≈ 137 m per game hour.

*Implemented (M2-01c-ii):* `World/Travel.cs` has `Speed(base, biome, surface, mud, grade, slope, night, light)` with the
§4 biome multipliers, surfaces, Tobler and light; cliffs over 45° are impassable. The worked example (1.83 m/s) is a
test. Snow comes in M3; trample paths and routing come with M2-21's pathing.

### 12.3 Paths from foot traffic

Every 8 m path-cost cell keeps a **trample score** `T` (sparse storage).

- **Traversals** add +1 per person, +2 per horse, +3 per cart and +0.3 per wild-animal group. At
  LOD2/LOD3, trips are accumulated along their route polylines statistically.
- **Decay:** ×0.985 per day in Spring and Summer (vegetation regrows); ×0.995 in Autumn and Winter.
- **Thresholds** (with hysteresis, demoting at 60% of the threshold): **trail** T ≥ 25; **path**
  T ≥ 120; **track** T ≥ 400 with carts making ≥ 30% of traffic.
- **Feedback:** pathfinding cost is `length / speed`, so worn paths attract more traffic and desire
  lines emerge on their own. Plantain spawns on paths (§7.3).
- **Built roads, bridges, causeways and ferries** belong to [14](14-technology-and-buildings.md).
  This doc gives them the cost multipliers above.

### 12.4 Fords, bridges & ferries

- **Fords** are generated at riffles (§3.5) where the bank slope is ≤ 15°. Passability depends on
  depth (normal depth + stage):

| Depth | People | Horses / carts |
|-------|--------|---------------|
| ≤ 0.5 m | ×0.6 speed; feet and legs wet | ×0.8 |
| 0.5–0.9 m | ×0.4; wet to the waist; carrying > 20 kg risks a drop (Athletics check); fast current risks a knock-down (p 0.02·velocity per crossing; swept → [11](11-survival.md) swimming) | ×0.6 |
| 0.9–1.2 m | Swimming only | Horses only, ×0.4 |
| > 1.2 m | Impassable on foot | Impassable |

- **Tidal ford** across the estuary: passable within ±1.5 h of low water. People caught by the
  incoming tide face a rising-depth crossing (a classic drowning risk; NPCs check the tide belief
  before crossing).
- **Bridge and ferry sites:** narrows ≤ 25 m with firm banks are tagged as candidates for
  [14](14-technology-and-buildings.md). A rope ferry (T1) needs a candidate ≤ 60 m wide.

### 12.5 Boats & coastal travel

**Small boats only in v1.** No one builds an ocean-going ship (Open questions).

| Boat | Tier | Build | Needs | Capacity | Sea limit |
|------|------|-------|-------|----------|-----------|
| Dugout | T0 | ~4 person-days; one log ≥ 60 cm (oak, pine, elm, lime); fire + axe hollowing | Carpentry ≥ 20 | 2 people + 150 kg | Calm/Moderate; rivers, estuary |
| Hide currach | T0–T1 | ~3 person-days; ash/hazel frame + 3–4 large hides + pitch/tallow | Carpentry ≥ 15, Leatherworking ≥ 20 | 4 people + 250 kg | Moderate (skilled crew: Rough) |
| Plank rowing boat | T1–T2 | ~12 person-days; planks + treenails or iron rivets | Carpentry ≥ 35 + `plank_boats` | 6 + 400 kg | Moderate/Rough |
| Small sailing boat | T2–T3 | ~25 person-days + sail cloth | `plank_boats`, Textiles for the sail | 6 + 600 kg | Rough (not Gale) |

**Capsize risk** per hour afloat: Rough `0.02·(1 − crewFishing/100)`; Gale 0.15 (and no launches).
Capsized people swim ([11](11-survival.md)). Coastal routes are often faster than overland ones
(cliffs, marsh), and boats move cargo that porters cannot. **The first boat changes the map**: it
opens the tidal islands, the far coast and contact with other expeditions.

### 12.6 Horses & draft animals

There are no wild horses or cattle. At Landfall the colony has goats and chickens (canon §5.3).
**Sheep, cattle (oxen), pigs and horses arrive only by ship** (§14.3) or with other expeditions,
which answers [15](15-economy-and-trade.md) Q4. Horses stay **rare for a generation**: 2–8 arrive
across all Varrowan resupply ships (≥ 1 mare and ≥ 1 stallion guaranteed by Y_N), and they breed at
r ≈ 0.15/yr. A mounted knight is a status symbol, not a cavalry arm. This is consistent with
[18](18-conflict-and-warfare.md)'s infantry-first assumption. Oxen make `ard_ploughing`
([12](12-skills-and-professions.md)) possible and are a major Y1 upgrade.

---

## 13. Lore

All names are working names. Faith content belongs to [16](16-social-systems.md).

### 13.1 The homelands

The homelands lie **east across the Grey Sea**, three to four weeks' sail in the sailing season
(Spring–Autumn). No ship crosses in Winter.

- **Kingdom of Varrow:** an orderly feudal monarchy whose lords hold land of the Crown. Its people
  are Ember Faith orthodox. It is crowded, land-hungry and full of younger sons.
- **Osmeri League:** island and coastal city-states run by merchant councils, coin-minded and lax in
  faith. Its navigators first charted Farstrand.
- **Brannoch Holds:** highland clans on Varrow's northern border, recently "pacified" by Varrowan
  arms. Many lost their glens in the Clearances. They are kin-loyal, martial, and keep the old ways.
- **The Ashen Reform:** a dissenting Ember sect ("only ash remains when pride burns away") that
  rejects the orthodox hierarchy. It is persecuted in Varrow.

### 13.2 Why they came

About thirty years ago an Osmeri survey voyage charted Farstrand's coast and reported oak forests,
ore-stained cliffs, game, and **no people**. The charts were copied, sold and stolen. When bad
harvests and a costly border war left Varrow with too many landless younger sons, the Crown began
issuing **Strand Charters**: cheap grants to minor lords willing to plant colonies at their own
expense, claiming the land before the League could. The League answered with chartered merchant
companies. Displaced Brannoch clans and persecuted Ashen congregations followed for their own
reasons, with no one's blessing.

### 13.3 The Charter

A vellum Letters Patent under the King of Varrow's seal, kept in a waxed leather case. It grants the
chartered lord "and his lawful heirs" the lands of the Strand "within a day's ride of the place of
first landing". It confers the rights to hold court, take dues and grant tenancies, in return for a
**fifth of all produce, ores and furs to the Crown** (the "charter dues", §14.3) and holding the land
for Varrow. Three deliberate ambiguities are built in:

1. **"Lawful heirs."** If the heir's legitimacy is ever doubted (§15.4), so is the Charter.
2. **"A day's ride."** There are no horses on Farstrand, and §1.3 makes a day's ride a matter of
   interpretation. It is a ready-made boundary dispute with every later expedition.
3. **"For Varrow."** After the Silence, does the Charter bind anyone?

The Charter is a physical item. It can be held, hidden, stolen, forged or burned. Its legal and
political force is decided by [17](17-governance-and-law.md), not by the item's existence. The heir
carries it from the first minute (canon §5.3).

### 13.4 The Silence

The homelands are sliding into the **Long War** (Varrow against the League, with the Brannoch
rising) and then into the **Grey Death**, a plague. Each resupply ship carries news that worsens on a
fixed arc (§14.3). After the sailing season of year *N* (rolled 3–6), **no ship ever comes again**.
Homeland authority becomes a claim, not a power, and whether the King still lives becomes an open
political lever. The last ship may carry plague ([11](11-survival.md)).

---

## 14. Expeditions, ships & the Silence

### 14.1 Arrival schedule

Everything is rolled at world creation from `Rng("expeditions")` and stored. The schedule is hidden
from the player.

| Arrival | Window | Rule |
|---------|--------|------|
| **N** (last sailing year) | — | Uniform 3–6 |
| **Resupply ships** (to each living homeland colony) | Summer 1 + delay, each year Y1…Y_N | Delay is 0–3 days (a Storm slot within 2 days of arrival adds +1); the ship stays 2 days |
| **Second expedition: Osmeri** | Y1 Autumn 1 – Y2 Autumn 8, never in Winter | Weighted 15% Y1 Autumn, 35% Y2 Spring, 35% Y2 Summer, 15% Y2 Autumn |
| **Later expeditions** | Y3 Spring – min(Y5, Y_N) Autumn | Count 1 (60%) or 2 (40%). If 1: Brannoch or Ashen 50/50. If 2: both, ≥ 1 season apart |
| **The Silence** | After Y_N Autumn 8 | No arrivals of any kind. If N = 3, later expeditions are pulled into Y3 |

### 14.2 Expedition site selection

Each expedition chooses among the §3.9 candidates by **culture affinity**, after excluding sites
within 2.5 km path of any existing settlement:

| Culture | Prefers |
|---------|---------|
| Osmeri | The best anchorage (deep, sheltered), trade access, the estuary's far shore; tolerates poorer soil |
| Brannoch | Hill edge near pasture and hill iron; defensible high ground; wild goats |
| Ashen | Remote (≥ 4 km from Varrowans), defensible fertile pocket, its own spring |

Expeditions **arrive intact** (no wreck) and found their camp at LOD3 (§14.5). Brannoch and Ashen
camps may relocate inland within their first season, decided by the same scoring.

### 14.3 Resupply ships

A resupply ship anchors off the colony's landing (or harbor, [14](14-technology-and-buildings.md))
for 2 days. Cargo scales with a **homeland condition** `H` that declines from 1.0 (Y1) toward 0.3
(Y_N). Charter compliance adds ±0.15.

| Cargo | Y1 (H ≈ 1.0) | Middle years | The last ship (Y_N) |
|-------|--------------|--------------|---------------------|
| Immigrants | 8–12 (some are kin of settlers, through letters) | 4–10 | **10–20 refugees**: fewer skills, more children and elders |
| Livestock | 2 oxen, 6 sheep, 2 pigs, 0–2 horses | Sheep, cattle, 0–2 horses | None |
| Iron goods | 2 axes, 1 ard share, 4 sickles, 2 spades, 10 kg nails | About half of Y1 | 0–2 items |
| Seed, salt, cloth | 40 kg seed, 40 kg salt, 30 ells cloth | Declining | Little |
| Coin, trade goods | The Crown purser buys charter dues and sells goods ([15](15-economy-and-trade.md)) | ← | Cash-starved |
| Letters & news | "Rumors of war" | "War declared; levies"; "Sickness in the ports" | "The ports are closing. This is the last ship." |
| Demands | **Charter dues assessed** (a fifth of the season's furs, timber, fish and ore; minimum 2 crowns' value) | ← | None |

Immigrants are generated with the manifest generator (§15) in "immigrant" mode, with their own small
backstory graph that links them to existing settlers through letters.

**Charter dues** are loaded as return cargo. Meeting them raises `H` for the next voyage and is a
legitimacy input ([17](17-governance-and-law.md)). Missing them twice in a row brings a **Crown
reeve** on the next ship (if any): a new authority figure with a royal commission. That is a
deliberate political spike.

**Departures:** settlers may leave on the ship. Each adult's leave desire comes from
[21](../tech/21-npc-ai.md) (homesickness, grief, failed ambition, the `wants to go home` motive,
§15.4). If the player chooses to leave, that is the end of the saga ([19](19-player-experience.md)).

**Ship hazards:** ship rats\* enter stores ([11](11-survival.md) spoilage). The last ship carries
the Grey Death with p = 0.35, and any ship in years ≥ N−1 with p = 0.10 ([11](11-survival.md)
disease). Spotted fever arrives with immigrants (p = 0.15 per ship) and mostly endangers children
born on Farstrand, who have no childhood immunity.

### 14.4 Later expeditions & cultures

Cultures are content (`/content/cultures/*.yaml`). Values, traits and skills are seeded through
[12](12-skills-and-professions.md) §8.5 and [21](../tech/21-npc-ai.md). Priors toward other cultures
are **initial opinion modifiers** applied at first contact ([16](16-social-systems.md) decays and
overrides them).

| Culture | Size | Distinctive cargo | Value shifts | Skill/trade bias | Governance default | Priors (→ others) |
|---------|------|-------------------|-------------|------------------|-------------------|-------------------|
| Varrow | 24 (12–40) | Salvage only (wrecked) | Tradition +10, Loyalty +5 | Farming, crafts | Charter heir vs. de facto leaders | Ashen −20 (heretics) |
| Osmeri | 30–40 | **A pinnace they keep**, coin ×3, trade goods, sheep | Wealth +15, Freedom +5, Faith −10 | Commerce, Textiles, Fishing, Letters | Council of company shareholders | Varrow −5 |
| Brannoch | 25–35 | Cattle, 2–6 ponies, weapons, few tools | Loyalty +15, Honor +10, Family +10, Wealth −10 | Husbandry, Hunting, Melee | Clan chief + elders | **Varrow −25** (the Clearances) |
| Ashen | 20–40 (family-heavy) | Tools, seed, books | Faith +20, Fairness +10, Status −10 | Farming, Letters, Healing | Elders of the congregation | **Varrow −30** (persecution) |

```yaml
id: culture.brannoch
homeland: brannoch_holds
faith: faith.old_ways
naming: { grammar: names.brannoch, place_grammar: places.brannoch }
size: [25, 35]
children_share: [0.15, 0.25]
values_shift: { loyalty: 15, honor: 10, family: 10, wealth: -10 }
trait_weights: { brave: 1.5, vengeful: 1.4, hot_tempered: 1.3, pious: 0.8 }
trade_weights: { herder: 3, hunter: 2, warrior: 2, smith: 1, farmer: 1 }
knowhow_rolls: { horse_breaking: 0.8, dairying: 0.9, bloomery_smelting: 0.5 }
cargo: { cattle: [6, 10], ponies: [2, 6], iron_tools: [4, 8], provisions_days: 14 }
governance: gov.clan_chief
priors: { culture.varrow: -25, culture.osmeri: -5, culture.ashen: 0 }
```

### 14.5 Off-screen colonies

Other expeditions run at **LOD3** from arrival (canon §8.2), using the same aggregate rules as any
far settlement: survival ([11](11-survival.md) LOD3), production ([13](13-crafting-and-minigames.md)),
growth and politics. They are promoted when the player or the player's settlement makes contact (a
scout within 400 m, a boat landing, envoys). They can fail. A colony at population 0 becomes a ruin
POI (§9), with graves and whatever its stores held.

---

## 15. The ship's manifest

The manifest generator creates the 24 Landfall survivors (configurable 12–40), the drowned, their
households and **a backstory graph dense enough to seed drama from day one**. Skills and know-how
come from [12](12-skills-and-professions.md) §8.5. Personality and traits come from
[21](../tech/21-npc-ai.md). This doc owns **who is aboard and how they are tied together**.

### 15.1 Roster template (N = 24; the player fills one slot)

| Role group | Count at 24 | Scaling | Notes |
|------------|-------------|---------|-------|
| **Heir** | 1 | Always | Age 15–22; holds the Charter; Letters 30–45; Leadership 5–25 (inexperienced) |
| Heir's kin | 0–1 (often a child) | — | Younger sibling; counts toward children |
| Steward | 1 | N ≥ 14 | 40–60; Stewardship, Letters; keeps **the ledger** (debts, indentures) |
| Men-at-arms | 2 (a sergeant and a young soldier) | 1 if N < 16; 3 if N ≥ 32 | The sergeant is the default **competence rival** (Leadership 35–55) |
| Ember priest | 1 | N ≥ 16 | Funerals, oaths, and some Letters and Healing |
| Healer / midwife | 1 (+ an apprentice 50%) | Always ≥ 1 | Guarantees `herbal_remedies` |
| Smith (+ youth apprentice) | 1 (+1) | Always ≥ 1 | Guarantees `ironworking`; bloomery is rolled per 12 |
| Carpenter / wright | 1 | Always ≥ 1 | Guarantees `timber_framing` |
| Ship's crew survivors | 1–2 (the bosun, maybe a ship's boy) | 1–3 | Fishing, rowing, `plank_boats` 40%; **want to go home** |
| Farmers (households) | 6–8 adults | ≥ 30% of adults | 2–4 households with children |
| Woodsman / hunter | 1–2 | ≥ 1 | Snares, butchery, the woods |
| Cook / brewer, weaver / tailor | 1 each | N ≥ 18 | — |
| Elder | 1 | ≥ 1 if N ≥ 16 | 55–65; lore, weakness, a moral test for rationing |
| Children (0–13) | **3–5** | `round(N·U(0.12, 0.2))`, minimum 2 | Canon §5.3 |
| Youths (14–15) | 0–2 | — | Apprentices |
| **The player** | 1 | Always | Their background ([19](19-player-experience.md)) fills the nearest matching slot |

**The dead:** the lord, plus 1–3 others (mostly crew; at most one settler's spouse or parent, so that
somebody starts in **Grief**). Their bodies are part of the Landfall scene ([11](11-survival.md)).

### 15.2 Households & demographics

Households are built first: the lord's household (heir, kin, steward, men-at-arms as retainers),
2–4 farming families (couple + 0–3 children, sometimes a grandparent), and craft households (smith +
apprentice). Then singles, siblings, a widowed parent and the crew. Ages follow the role ranges, with
adults skewed 18–40. Everyone shares **Familiarity 20–40** from the three-week voyage
([19](19-player-experience.md) §2.4), and household members start at 70+.

### 15.3 Know-how coverage

Coverage is rolled through [12](12-skills-and-professions.md)'s Landfall column (U/C/F/R%/G/A). This
generator adds two constraints:

1. **Survival floor:** every T0 know-how marked U or C has ≥ 2 holders, so one death never leaves
   the colony unable to make fire or snares.
2. **Deliberate gaps:** each expedition's rare-know-how set differs by culture (a Brannoch ship
   likely has `horse_breaking` and Varrow likely does not). **Gaps between polities are trade and
   war motives**, as tenet 6 intends: the colony that has the only bloomer has something to sell or
   to lose.

### 15.4 Backstory graph

Ties are drawn from a weighted **incident deck**. Each tie creates real sim state: Opinion, Trust and
Familiarity modifiers, memories with salience ([16](16-social-systems.md)), debts in the ledger
([15](15-economy-and-trade.md)), and beliefs, including false ones.

| Tie type | Target count at N = 24 | Sim state created | Example incident |
|----------|----------------------|-------------------|------------------|
| Kin (cross-household) | 1–2 | `kin` tags | Two farm wives are sisters |
| Old friendship | 4–6 pairs | Opinion +30–50, Trust 60–80, Familiarity 70+ | Grew up in the same village |
| Debt | 2–3 | A ledger debt of 1–12 s (homeland coin) | "Lent you passage money" |
| **Indenture** | 3–6 people | 3 years' labor owed to the Callow estate in return for passage | The heir inherits the indentures; whether they still bind is a [17](17-governance-and-law.md) question |
| Grudge | 2–4 | Opinion −20 to −40 + a memory of the incident | "Your brother informed on mine"; "Your family got our tenancy" |
| Romance | 1–2 (+ 30% chance of a triangle) | Attraction, a courting tag, or a secret affair | A soldier courts a farmhand |
| Rivalry | 1–2 | `rival` tag | Smith and carpenter over who leads building |
| Mentor / apprentice | 1–3 | `mentor`/`apprentice` tags | Healer and apprentice |
| **Secret** | 1–3 | A true fact + a belief held by 0–2 people | An Ashen sympathizer among the orthodox; a fugitive under a false name; the steward skimmed the coin chest; (15%) **the heir's legitimacy is doubtful** |
| **Voyage incident** | 2–3 shared | A memory held by everyone, plus reputation seeds | "Saul hoarded ale during the calm" → Generosity −; "Brand held the mast in the gale" → Courage + |
| Stowaway | 25% chance, 1 person | Familiarity 0 with all | An outsider nobody vouches for |

**Motives** (one per adult) shape values and stance toward the Charter. They are weights into
[21](../tech/21-npc-ai.md)'s value seeding:

| Motive | Value nudges | Charter stance |
|--------|-------------|----------------|
| Land of my own | Wealth +, Freedom + | Neutral; resents tenancy |
| Followed the lord | Loyalty +, Tradition + | Supports the heir |
| Indentured for passage | Freedom + | Resents; wants the indenture void |
| Fleeing debt / the law | Freedom +, Honesty-risk trait | Prefers no authority |
| Faith (zeal or quiet dissent) | Faith ± | Depends on church alignment |
| Family (followed spouse or parent) | Family + | Follows the household head |
| Fortune (believes the ore rumors) | Wealth +, Status + | Supports whoever explores |
| Wants to go home (crew) | Family +, Tradition + | Indifferent; will leave on a ship |

### 15.5 Drama-budget validation

The manifest is rejected and re-rolled (same world, new `Rng("manifest", seed, attempt)`) unless:

- ≥ 1 secret with scandal potential, ≥ 2 grudges, ≥ 1 debt or indenture, and ≥ 1 romance;
- ≥ 1 **credible rival leader** to the heir: Leadership ≥ 35, Values.Status ≥ 60, and not of the
  lord's household;
- ≥ 1 adult with the `wants to go home` motive;
- no one has zero ties of Familiarity ≥ 40, except the optional stowaway;
- **tie slots** for the player exist: at least one eligible NPC for each of
  [19](19-player-experience.md)'s tie types (kin, friend, sweetheart, former master, creditor,
  rival, sworn to the heir). The player's 2 chosen ties are bound to the best-matching NPCs (by
  age, sex and background), and the generator adds those edges.

### 15.6 Generation order (pseudocode)

```
households = BuildHouseholds(N, cultureTemplate, rng)
for each person: trade ← role; skills/know-how ← Skills.Seed(person, trade)      // 12 §8.5
                 personality ← NpcAi.SeedPersonality(culture, motive)             // 21
EnforceKnowHowFloor(people)                                                      // §15.3
graph = DealIncidentDeck(people, targetsFor(N), rng); ApplyTies(graph)           // §15.4
BindPlayerTies(playerBackground, chosenTies)                                     // 19
if !DramaBudgetOk(graph): retry (max 32)
Narrate: LLM writes each person's 2–4 sentence backstory from their structured ties (fallback templates)
```

### 15.7 Worked example (one N = 24 manifest)

| # | Name | Age | Role | Notable ties & secrets |
|---|------|-----|------|------------------------|
| 1 | Elinor Callow | 17 | **Heir** | Holds the Charter; grieving her father; Opinion of Brand −10 ("he looks at the stores like he owns them") |
| 2 | Edwin Callow | 9 | Heir's brother | — |
| 3 | Godric Pell | 52 | Steward | **Secret:** the coin chest is 30% lighter than his ledger says; grudge with Brand over wages |
| 4 | Brand Hesketh | 38 | Sergeant | **Rival leader**; voyage hero ("held the mast"); motive: land of his own |
| 5 | Jory Tull | 22 | Soldier | Courting Cecily (#23) |
| 6 | Mother Aveline | 46 | Ember priestess | Suspects someone aboard is Ashen |
| 7 | Isobel Rye | 41 | Healer / midwife | Knows Edda's secret and keeps it |
| 8 | Edda Thorne | 19 | Healer's apprentice | **Secret: Ashen sympathizer** |
| 9 | Osric Hale | 44 | Smith ("old hand", Smithing 62) | Rival of Wat; carries `bloomery_smelting` (the colony's only holder) |
| 10 | Tobin Hale | 15 | Smith's son (youth) | Apprentice |
| 11 | Wat Fenner | 35 | Carpenter | Rival of Osric; old friend of Hugh |
| 12–15 | Hugh, Agnes, Bet (6) & Margery (61) Thatcher | — | Farm household | Margery is the elder; Agnes is Joan's sister |
| 16–18 | Robb, Joan & Pip (2) Carter | — | Farm household | Robb owes Godric 3 s; **indentured** (with Cecily and Piers) |
| 19 | Alys Weaver | 36 | Weaver | **Widowed in the wreck** (Grief 70); old friend of Saul |
| 20 | Piers Lark | 27 | Woodsman | Indentured; a loner; Opinion of the indenture −30 |
| 21 | Saul Greaves | 48 | Cook / brewer | Drunkard; voyage incident "hoarded the ale" |
| 22 | Kester Mull | 31 | Bosun | Wants to go home; holds `plank_boats` |
| 23 | Cecily Moor | 24 | Farmhand / spinner | Courted by Jory; indentured |
| 24 | *Player* | 18–35 | By background | 2 chosen ties (e.g., friend of Piers; creditor Saul, 24d) |

Children: Edwin, Bet, Pip (3). Youth: Tobin. Elder: Margery. The dead: Lord Aldous Callow; the ship's
master; Alys's husband.

---

## 16. Simulation LOD & Interludes

| System | LOD0 / LOD1 (near people) | LOD2 | LOD3 / Interlude |
|--------|---------------------------|------|------------------|
| Weather | Hourly state; local wind, fog and precipitation at the person; presentation interpolates | Hourly | Daily summary per altitude band (min, max, precip, snow, events). Gales and cold snaps still fire as events |
| Tides & river stage | Continuous | Hourly | Daily min/max; fords flagged per day |
| Fauna | Embodied groups (spawned from EcoCell N); kills write back | Encounter function | Daily population model only |
| Forests | Node-level felling and regrowth | ← | Stand aggregates; node deltas reconciled when a chunk is next loaded (felled volume removed from the nearest matching nodes) |
| Deposits | Per-action extraction | Hourly | Daily aggregate |
| Trample | Per traversal | Route polylines | Daily route tallies |
| Knowledge & naming | Per sighting and reference | Hourly | Daily belief diffusion; name counters advanced by reference counts |
| Ships & expeditions | — | — | Daily checks; arrivals are **Interlude interrupts** only if they reach the player's settlement (canon §6.1: a new ship counts as a summons when the player holds office; otherwise it is reported in the Chronicle) |

**Consistency rule:** LOD3 and LOD1 must agree within ±10% on 50-year population and stand outcomes
for the same seed (§20).

---

## 17. LLM / Jev touchpoints

All of these follow canon §13 ("language decides, systems resolve"): the sim supplies facts, models
supply words and classifications, and where a character makes a choice it picks from a menu this doc
or [16](16-social-systems.md) builds, which the sim then carries out. Every touchpoint has a template
or policy fallback.

| Touchpoint | Trigger | Sim supplies | Model output | Bound / validation | Fallback |
|------------|---------|-------------|--------------|--------------------|----------|
| Place-name proposals | Discovery or salient event (§11) | Feature type, events, people, culture style | 3 candidate names | Sim validates (length, charset, filter, uniqueness); adoption is purely counter-based | Culture grammar |
| Player-proposed name check | Player names a place | Player text (**untrusted**) | Fast decider yes/no: "in-setting, inoffensive?" | p ≥ 0.6 accepts; also filter lists | Filter lists only |
| Manifest backstories | World creation | Structured ties, motives, incidents | 2–4 sentences per person | Must mention only supplied facts; checked by entity match | Templates per incident |
| Homeland letters & news | Ship arrival | News-arc stage, the recipient's kin and facts | Letter prose | No new facts beyond the arc and supplied kin events | Templates |
| Forecast and weather barks | Conversation | Forecast belief, observed weather | A line of speech | Voices the belief, never the truth | Templates |
| Scout reports | A scout returns | Discovered features and beliefs | Spoken report | Only the transferred beliefs | Templates |
| **Asking the way, or for what someone knows** (ore, a ford, game, a guide) | Player asks in conversation | The NPC's place beliefs and the decision menu (next columns) | The NPC's **choice** (LLM, in the reply) and its line | Menu: `tell_all` · `tell_some` (the single most salient feature) · `withhold` · `sell_for` (a price from [15](15-economy-and-trade.md)) · `guide` (a day's guiding at 15's wage; eligible if free that day). `p_i` from [16](16-social-systems.md)'s disclosure rules (Trust, Opinion, how valuable or secret the knowledge is). Stakes low; medium for an undisclosed deposit. Executed by the belief transfer in §10 (*Telling*); payment and the guiding job by 15 and [21](../tech/21-npc-ai.md) | Policy samples `p_i` |
| Chronicle (world events) | Season and Interlude end | `DepositExhausted`, `GameScarce`, arrivals, the Silence | Prose | Facts from the log | Templates |

No model touches generation geometry, populations, deposits or schedules, and no model decides what a
place *is*: a character may choose whether to share what they believe, never what is true.

---

## 18. Tuning knobs

| Knob | Default | Range | Effect |
|------|---------|-------|--------|
| `world.land_area_km2` | 35–45 | 30–50 | Exploration scale |
| `deposit.tin.count` / `.size` | 1–2 / 0.8–2.5 t | 0–3 | Bronze pressure |
| `deposit.richness` (difficulty) | 1.0 | 0.6–1.5 | Scales all finite deposits |
| `fauna.naivety_start` | 1.5 | 1.0–2.0 | Early hunting generosity |
| `fauna.K_mult` | 1.0 | 0.5–1.5 | Game abundance |
| `wolf.packs` | 2–3 | 0–4 | Predator pressure |
| `weather.storm_share_mult` | 1.0 | 0.5–2.0 | Harshness; wreck breakup speed |
| `weather.y0_winter_cap` | Normal | Mild–Hard | The Era 0→1 exam's difficulty |
| `travel.jog_mps` | 4.0 | 3.0–5.0 | Effective world size |
| `trample.decay_summer` | 0.985 | 0.97–0.995 | How fast paths form and fade |
| `ship.N` | U(3, 6) | 1–10 | Length of homeland contact |
| `ship.plague_last` | 0.35 | 0–1 | Grey Death risk |
| `expedition.count_later` | 1–2 | 0–3 | Rival polities |
| `manifest.drama_budget` | §15.5 | lenient–strict | Early social heat |
| `naming.adopt_share` | 0.6 | 0.5–0.8 | How fast names stick |

---

## 19. Failure modes & exploits

| Risk | Mitigation |
|------|------------|
| A generated world is technically valid but dull (no landmarks near home) | Soft score in site selection: landmark count within 3 km; best of k attempts |
| The landing is unexpectedly perfect (iron, tin and farmland together) | Hard asserts W6/W7; Score − terms |
| Overhunting crashes the whole island in Y1 | Naivety decays fast; the disturbance ring pushes animals away before they are exterminated; `GameScarce` rumors make NPC hunters spread out ([21](../tech/21-npc-ai.md)) |
| Wolf attrition spirals (wolves eat all the livestock) | Depredation probability is capped; guard multipliers; wolf hunts are a cheap communal job |
| Player savescums the seed for a better landing | Allowed. The manifest and weather change with the seed, so there is no "best seed" |
| Player camps on the reef to hoard the wreck | The wreck is time-limited; tides and surge are dangerous; other settlers salvage in parallel |
| Exploiting naming (offensive names) | Filter + fast-decider check; names spread only if others use them |
| Fords and tides trap NPCs constantly | NPCs check tide and stage beliefs; a stuck NPC waits (Comfort cost) instead of attempting a crossing above 0.9 m |
| Wildfire wipes out the settlement | Off by default until M5. Only in Dry years, only Pine and Moor, and capped area |
| Day-length setting changes economic output | Accepted per [20](../tech/20-architecture.md); balance tests run at 30 min (Open questions) |
| LOD3 ecology diverges from LOD1 | Calibration test (§20) |

---

## 20. Validation & headless tests

| ID | Test | Pass |
|----|------|------|
| T-WG-01 | Generate 10,000 seeds | ≥ 97% pass within 4 attempts; 100% within 16 or reported |
| T-WG-02 | Biome share distribution over 1,000 seeds | Every biome's mean inside its band; < 1% of worlds at a band edge |
| T-WG-03 | Determinism | Same seed + build → identical `WorldGenResult` hash |
| T-WG-04 | Reachability (W10) | 100% of accepted worlds |
| T-WX-01 | Weather stationary shares over 100 simulated years | Within ±3 points of the §6.3 table per season |
| T-WX-02 | Temperature | Season means within ±0.5 °C of §6.2 at sea level; cold-snap count per Winter matches |
| T-WX-03 | Snow | Lowland snow days per Winter: median 1–3 |
| T-ECO-01 | Unhunted spin-up | Populations stable ±15% over 50 years after spin-up |
| T-ECO-02 | Hunting 10% of deer per year | Deer persist at 40–70% K; no extinction in 100 seeds × 40 years |
| T-ECO-03 | 150-person settlement's firewood demand | `WoodlandDepleted` within 1 km between Y8 and Y20 without coppicing; never with coppicing at 25% of woodland |
| T-ECO-04 | LOD3 vs LOD1 | 50-year outcomes within ±10% |
| T-RES-01 | Deposit lifetime at 13's extraction rates | Tin lasts 6–20 years of an active bronze industry; hill iron ≥ 30 years at Era 3 rates |
| T-NAME-01 | Naming dynamics in a 60-person hamlet | ≥ 80% of frequently referenced features have a common name within 2 years |
| T-EXP-01 | Schedule | Over 10,000 rolls, every arrival falls inside its §14.1 window; none after the Silence |
| T-MAN-01 | Manifest | 100% satisfy §15.5; children 3–5 at N = 24; ≤ 32 attempts |
| T-TRV-01 | Path formation | A daily 24-person commute route becomes a path within 4–8 days |

---

## 21. Milestones

| Feature | Milestone |
|---------|-----------|
| Seeded RNG streams; WorldSpec schema in the content pipeline | **M0** |
| Manifest generator (roster, households, backstory graph, drama budget, tie slots), on a fixed hand-made camp map | **M1** |
| Generation stages 1–10 and 12; 8 biomes; flint, wood, clay, stone, water, herbs, wild foods; tides; wreck POI; basic weather (no snow); fauna with embodiment and naivety; travel speeds; fords; knowledge tiles | **M2** |
| Full seasons, snow, year types and events; forest dynamics and succession; copper and bog iron prospecting; trample paths; place naming; drawn maps; dugout and currach; ecology spin-up; ships' **schedule** data | **M3** |
| Resupply ships and immigrants; the Osmeri expedition; other cultures at LOD3; plank boats; horses and oxen; tin, galena, hill iron in use | **M4** |
| Later expeditions (Brannoch, Ashen); the Silence; the last ship and plague; the Crown reeve; ruins from failed colonies; wildfire | **M5** |
| Maps as war assets; contested-resource tuning | **M6** |
| Balance of all knobs across 10,000 seeds | **M7** |

---

## Open questions

1. **Ancient ruins.** Canon keeps them open. This doc's answer is that the only ruins are those of
   failed colonies (§9), which are earned history, not lore dumps. Should there also be a handful of
   unexplained standing stones (natural formations the settlers *believe* are ancient)?
2. **Folklore layer.** A belief-only superstition system (wights in the marsh, the "old oak's" luck,
   omens in weather) that changes behavior but never physics. It fits [16](16-social-systems.md)'s
   belief model. Who owns it, 16 or this doc?
3. **Leaving Farstrand.** Could a late-game polity build a sea-going ship to reach the homelands after
   the Silence (an epilogue path)? Out of scope for v1, but the bosun's ambition hints at it.
4. **Day-length normalization** ([20](../tech/20-architecture.md) Q1): at 60-minute days people walk
   twice as far per game hour. Should travel and productivity be normalized?
5. **Wildfire:** include as an M5 Dry-year hazard or cut?
6. **More than one map size** (a 12 km "large world" option) for a 1,500-person realm? Perf
   implications belong to [20](../tech/20-architecture.md).
7. **Gold:** none in v1. A trace-gold placer would be a strong rumor engine ("fortune" motive) but
   distorts [15](15-economy-and-trade.md)'s coinage. Keep it out?
8. **Q17 — Dry-summer persistence** (§6.3–6.4, M2-03): keep Clear p_stay 0.75 in Dry summers (applied), or soften
   Dry to Clear +15?
9. **Q16 — W3 river count vs island archetype (M2-01a-iii):** on a 35–45 km² island with a 900–1,250 m peak, radial drainage
   gives one river of ≥ 4 km²; authored valley axes bring spines to 2–3 but massif and twin-ridge islands rarely pass,
   so retries select spines (12/20 seeds pass every assert, all spines). Options: per-archetype valley layouts (longer
   flank basins), "major" defined at a lower catchment for small islands, or `rivers_major` [1, 4]. Applied default
   (reversible): keep W3 as written and enforced by retries; `worldgen` enforces W1/W5 in CI and reports W2/W3; the
   generator returns the first world passing the finished stages if no attempt passes all (31 D38).
10. **Q18 — W4 biome shares (M2-01b-i).** After the hypsometry remap (§3.3 note), 20 seeds put highland (16/20) and
    pine (13/20) mostly in band. These remain outside:
    - **coast_dunes ≈ 11 %** (band 5–9): a 150 m strip on this much coastline (coves, islets) is geometry, not a rule.
    - **wetland ≈ 0.3 %** (band 5–10): flats under 2° are rare at 8 m on noise relief.
    - **river_valley ≈ 1 %** (band 7–12): tied to the W3 river count (Q16).
    - **meadow ≈ 8 %** (band 12–20) and therefore **broadleaf ≈ 39 %** (band 22–32): the tree-establishment index's
      scale is unspecified.

    Should W4 bind (and which lever moves first — coast geometry, wetland flats, the index scale), or are the bands
    looser in practice? W4 is reported, not enforced (31 D44).

## Proposed canon additions

> **Status (canon v0.2):** accepted items have been folded into [01-canon](../01-canon.md) (see its change log). Items not reflected there remain proposals for the owner to decide.

1. **Time-scale policy** (§1.2): clock-scale processes run 1:1; calendar-scale processes are divided by
   K = 365/32 ≈ 11.4, with a 1-game-day floor for anything a person must respond to; real years map
   to game years. This is consistent with canon pregnancy (24 days).
2. **Locomotion speeds** (§12.1): walk 1.6 m/s, jog/travel 4.0 m/s (300 m per game hour at the
   default day), sprint 6.5 m/s; the 8 km region ≈ 3 days' cross-country march. Settlements are
   compact by design.
3. **Region extent:** 8,192 m × 8,192 m (adopting [20](../tech/20-architecture.md)). The landmass
   stays at 35–45 km².
4. **Grids:** 512 m EcoCells for ecology; 128 m knowledge tiles.
5. **Calendar astronomy:** daylight `12 + 4·sin(2π(d−3)/32)` with `d` = 1…32 (midsummer = Summer 3, midwinter =
   Winter 3, equinoxes Spring 3 and Autumn 3); sea-level mean temperature
   `8.5 + 8.5·sin(2π(d−5)/32)` °C; **moon cycle = one season** (full moon on day 5); semidiurnal tides
   of 12.42 h.
6. **Deposits:** tin 1–2 placers; hill iron 2–3, ≥ 3 km from the breadbasket; copper 2–4; **bog iron
   2–5 (poor, near home)**; **exactly one galena deposit with 20–180 kg silver** (local silver exists
   but is scarce); sea coal 0–1.
7. **Livestock & horses:** none native. Landfall has goats and chickens; sheep, cattle, pigs and
   **horses arrive only by ship or with other expeditions**; 2–8 Varrowan horses before the Silence.
8. **Fauna additions** (canon lists nine species): **wild bees** (honey and wax), **seabirds** (eggs,
   at cliffs), **seals** (optional coastal haul-outs), **ship rats and mice** (pests, plague vector),
   and **ship's cats and dogs** (domestic, arriving by ship). Ambient birds and insects are
   non-simulated.
9. **The Charter's text:** "lawful heirs", "a day's ride of first landing", and "a fifth to the Crown"
   (charter dues), as deliberate ambiguities.
10. **Schedule:** Osmeri arrive Y1 Autumn – Y2 Autumn (never in Winter); later expeditions Y3 to
    min(Y5, Y_N); no arrivals after the Silence; resupply ships arrive Summer 1 + 0–3 days and stay
    2 days.
11. **The Crown reeve:** if charter dues are missed twice, a royal official arrives on the next ship.
12. **Manifest roles:** the steward's **ledger** and **indentured settlers** (3–6, three years' service
    to the Charter-holder) as canonical Landfall seeds. The surviving crew includes the **bosun**
    (matching [19](19-player-experience.md) §9.1).
13. **Conflict noted (20):** resource nodes are "regenerated from the seed, deltas saved", but
    determinism is per-platform. Node scattering must be integer-hash based (§3.2), or saves must
    store nodes.
14. **Conflict noted:** [15](15-economy-and-trade.md) §10.2's salvage quantities (seed grain 60 kg,
    4 goats, 4 knives) are adopted by [11](11-survival.md). Canon §5.3 should list them so 11, 15
    and 19 stay in sync.
15. **Place-knowledge disclosure DP** (canon v0.3): asking someone what they know of a place opens a
    decision point `tell_all · tell_some · withhold · sell_for · guide` (§17); the choice is the
    character's, the transferred beliefs are exactly the ones they hold.
