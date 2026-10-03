# 01 — Canon: Core Decisions, Numbers & Names

> **Status:** Draft v0.1 · **Applies to:** every other document in `docs/`
> **Source:** [00-vision-original.md](00-vision-original.md) (the original brainstorm, kept verbatim)

This is the **single source of truth** for FeudalSim's foundational decisions. Every subsystem
document builds on it. If a subsystem doc and this doc disagree, **this doc wins** until it is
deliberately changed. Changing a canon value means: update this file, add a line to the change log
at the bottom, and (for anything architectural) write or amend an ADR in [`adr/`](adr/).

Names marked *(working name)* are placeholders. Numbers are starting values to be tuned through
playtesting and headless simulation runs, but documents must use these values until the canon changes.

---

## Table of contents

1. [Elevator pitch](#1-elevator-pitch)
2. [Design pillars](#2-design-pillars)
3. [Design tenets (how we make decisions)](#3-design-tenets-how-we-make-decisions)
4. [Core product & technology decisions](#4-core-product--technology-decisions)
5. [Setting canon](#5-setting-canon)
6. [Time canon](#6-time-canon)
7. [The era ladder](#7-the-era-ladder)
8. [Scale, population & simulation LOD](#8-scale-population--simulation-lod)
9. [Technology tiers](#9-technology-tiers)
10. [The person model (shared by player and NPCs)](#10-the-person-model-shared-by-player-and-npcs)
11. [Economy units](#11-economy-units)
12. [The player](#12-the-player)
13. [The LLM boundary ("hard systems, soft voice")](#13-the-llm-boundary-hard-systems-soft-voice)
14. [Scales, units & conventions](#14-scales-units--conventions)
15. [Milestone IDs](#15-milestone-ids)
16. [Document ownership map](#16-document-ownership-map)
17. [Change log](#17-change-log)

---

## 1. Elevator pitch

A boatload of settlers wades ashore on an empty, wild land. You are one of them — not a chosen
hero, not a god-hand directing the colony, just one person among two dozen trying not to die before
winter. Everyone around you is a simulated person with needs, skills, a temper, a memory and a voice.
Over years (and generations) the camp becomes a hamlet, a village, a town, a lordship — and then one
of several rival realms on the same coast. People disagree, hold grudges, gossip, steal, fall in
love, and eventually march to war. What you become in that society — hermit, farmer, smith,
shopkeeper, knight, lord, outlaw — is up to you and to how the people around you come to see you.

Under the hood, deterministic simulation systems decide what happens. Language models give those
systems a human voice.

---

## 2. Design pillars

Every feature must serve at least one pillar. Features that serve none are cut.

| # | Pillar | What it means in practice |
|---|--------|---------------------------|
| P1 | **A society that lives without you** | The settlement is fully simulated whether or not the player is watching. NPCs gather, build, trade, marry, feud and die on their own. The player is *one member*, not the colony's controller. |
| P2 | **Everyone is a person** | Every NPC has persistent needs, personality, memories, relationships and opinions — and speaks in natural language about them. Talking to people is the primary way the player affects society. |
| P3 | **Work is a craft** | Each profession is a game of its own: carving a bow, tending a field, smelting bloom, haggling over a counter, holding court. No "click to make sword". |
| P4 | **Consequences travel** | Actions create memories, memories create rumors, rumors change reputations, reputations change how a whole community treats you. Insults lead to fights; theft leads to suspicion and the law. |
| P5 | **From campfire to crown to war** | The arc runs from a survival co-op of ~24 people to rival realms of hundreds at war, with real economic and emotional costs. |
| P6 | **Be anyone** | No classes. One shared skill system for player and NPCs. Your role emerges from what you practise, what you own, and what others will accept you as. |

---

## 3. Design tenets (how we make decisions)

1. **Hard systems, soft voice.** Deterministic, hard-coded systems own every *outcome* (prices,
   acceptance of requests, combat, crafting quality, law, opinions). LLMs *voice* those outcomes and
   *interpret* player language into structured signals. See [§13](#13-the-llm-boundary-hard-systems-soft-voice).
2. **Parity.** The player and NPCs obey the same rules: same skills, same needs, same crafting
   quality formulas, same laws, same relationship math. NPCs don't play minigames (they use the
   skill-based resolution the minigame is calibrated against), but their results come from the same
   model.
3. **Imperfect people.** NPCs are not optimizers. Personality, emotion, misinformation, grudges and
   noise bend their choices. Irrationality is a feature, tuned deliberately, never a bug to remove.
4. **Simulate first, render second.** Everything that matters happens in a headless simulation
   core that can run without graphics, faster than real time. The game client is a view onto it.
5. **Scarcity creates story.** Resources, land, status, partners and knowledge are finite and
   unevenly distributed. That pressure is where conflict comes from.
6. **Knowledge lives in people.** Settlers already know how to smelt iron — they lack the ore,
   the furnace, the charcoal and the hands. Know-how is carried by individuals and spreads by
   teaching; if the only steelsmith dies untaught, the steel dies with them.
7. **Respect the player's time.** Tedium is opt-out: mastered crafts can be batch-made, routines
   can be delegated, and time can be skipped (Interludes) once the player has earned it.
8. **Graceful degradation.** If the LLM or classifier is slow, offline or wrong, the game still
   works. Every LLM touchpoint has a non-LLM fallback.

---

## 4. Core product & technology decisions

| Topic | Decision | Notes / ADR |
|-------|----------|-------------|
| Genre | Third-person survival → social/settlement simulation → feudal politics & war RPG | — |
| Dimension & camera | **3D, third-person over-the-shoulder.** Close-up "bench camera" for crafting minigames; optional first-person toggle. | — |
| Art style | **Stylized low-poly**, painterly lighting and fog (Valheim-adjacent). Chosen for production feasibility and to render 150–300 characters at once. | [02-game-overview §Art](02-game-overview.md) |
| Players | **Single-player only.** Multiplayer is an explicit non-goal for v1. | [ADR-0004](adr/0004-single-player-scope.md) |
| Platforms | **PC: Windows, macOS (Apple Silicon first-class), Linux.** Keyboard & mouse first; controller support later. | — |
| Engine | **Godot 4.x (.NET / C#)** for presentation. | [ADR-0001](adr/0001-engine-godot-dotnet.md) |
| Simulation core | **Pure C# (.NET 8) class library with zero engine dependencies**, deterministic, tickable, runnable headless. The Godot client is a view + input layer. | [ADR-0002](adr/0002-headless-deterministic-sim-core.md) |
| Generative LLM | Any **OpenAI-compatible chat-completions endpoint**. Start: **OpenRouter** with a Qwen-family instruct model. Target: **local** (llama.cpp / Ollama / LM Studio) small model (~7–14B, 4-bit). | [22-llm-integration](tech/22-llm-integration.md) |
| Decision model | **Jev (TypeSafe AI, "System One" model)** for fast structured judgments (choice / score / yes-no probability). Cloud-only, so every use has a local fallback (local LLM structured output → heuristics). | [ADR-0003](adr/0003-hard-systems-soft-voice.md) |
| Content data | Authored content (items, recipes, crops, skills, traits, buildings…) in **YAML** files under `/content`, validated against JSON Schema in CI. | [20-architecture](tech/20-architecture.md) |
| Saves | Snapshot of sim state + append-only event log. | [20-architecture](tech/20-architecture.md) |

### 4.1 Jev — what we know (verify before implementation)

Jev is a "decision model" from TypeSafe AI. It does **not** generate text; it reads a *state*
(text / JSON / text arrays) and answers *questions* with calibrated probabilities. Facts below come
from third-party write-ups (Sept 2026) and **must be verified against TypeSafe's own docs before
implementation**:

- Question types: **choice** (up to 255 options → selected option + per-option probabilities +
  confidence), **score** (2–10 ordered levels → probability-weighted mean + per-level probabilities),
  **noul** (yes/no → probability of yes). Multiple questions evaluate in parallel in one call; they
  cannot depend on each other.
- Reported latency 70–500 ms (P50 ≈ 0.23 s via OpenRouter); ~32K context; ~$0.042 per million input
  tokens, output free.
- Model ids reported: `jev-latest` (TypeSafe API), `typesafe/jev-1.13` (OpenRouter),
  `typesafe-ai/jev` (Vercel AI Gateway).
- Documented weaknesses: **weak arithmetic, counting and date ordering**; degrades with irrelevant
  context; **does not treat input as hostile — injected instructions can move answers**; confidence
  measures concentration, not correctness.

**Design consequences (canon):** Jev only *classifies* (intent, tone, topic, persuasiveness,
consistency checks). It never computes numbers, never decides outcomes on its own, and its inputs
containing player text are treated as untrusted. All thresholds that turn a Jev probability into a
consequence live in hard code and are clamped.

---

## 5. Setting canon

### 5.1 The land

- **Farstrand** *(working name)* — a large, temperate, previously **uninhabited** landmass across
  the sea from the settlers' homelands. No native peoples. (Ancient ruins are an open question,
  not canon.)
- Playable world: **8 km × 8 km** region; the landmass occupies roughly **35–45 km²** of it, the
  rest is sea, islets and reefs. Generated from a seed using hand-authored constraints (see
  [10-world-and-setting](design/10-world-and-setting.md)).
- Biomes (canonical set): **Coast & Dunes, Temperate Meadow/Grassland, Broadleaf Forest, Pine Forest,
  Wetland/Marsh, River Valley, Hills & Moor, Highland/Mountain** (with a snowline). Rivers, lakes
  and at least one large estuary exist on every map.
- Resources are **unevenly distributed by design** (e.g., tin exists in one or two places; good iron
  ore is in the hills, far from the best farmland). Uneven distribution drives trade, expansion and war.
- Wildlife (canonical starting set): deer, boar, hare, wolf, bear, fox, wild goats, waterfowl, fish.
  No fantasy monsters in v1 (the "fantasy" is in the setting, not creatures). *Open question: light
  folklore / superstition layer.*

### 5.2 The homelands *(working names)*

| Homeland | Character | Faith | Role |
|----------|-----------|-------|------|
| **Kingdom of Varrow** | Feudal monarchy, orderly, hierarchical | Ember Faith (orthodox) | The player's expedition |
| **Osmeri League** | Mercantile city-states, coin-minded | Ember Faith (lax) | Second expedition |
| **Brannoch Holds** | Highland clans, kin-loyal, martial | Old ways / ancestor rites | Later expedition |
| **The Ashen Reform** | Religious dissenters fleeing persecution | Ashen Reform (heterodox Ember sect) | Later expedition |

Cultural and religious differences between expeditions are an intended source of friction.

### 5.3 The founding of the player's settlement

- The player arrives on the ship ***Wending Star*** *(working name)* in **Year 0, Spring, Day 1**,
  with **24 settlers** (configurable 12–40), including **3–5 children**.
- The expedition was chartered by a minor Varrowan lord, who **drowns during the landing**. His
  surviving heir (a teenager or young adult) holds the **Charter** — a document granting rights to
  the land. Authority is therefore **contested from Day 1**: legitimacy-by-charter vs.
  legitimacy-by-competence. This is the first political seed.
- Salvage from the ship: a **limited, irreplaceable stock of iron tools** (a few axes, knives,
  a saw, one hammer), seed grain, a few goats and chickens, cloth, rope, some homeland coin,
  and minimal provisions (~10 days). Enough to start; not enough to last.

### 5.4 How multiple societies come to exist (so war is possible)

Rival polities emerge through **four channels**, all simulated:

1. **Other expeditions** land elsewhere on Farstrand: the second around **late Year 1 – Year 2**,
   one or two more by **Year 3–5**. Each starts as its own settlement with its own culture.
2. **Resupply / immigrant ships** arrive at the start of each Summer for Years 1 to *N*
   (*N* rolled 3–6), adding settlers to existing colonies.
3. **The Silence.** After year *N* the ships stop coming (the homelands are consumed by their own
   war/plague). Farstrand becomes self-reliant; homeland authority becomes a claim, not a power.
4. **Schism & Exodus.** When a faction's grievance and cohesion pass thresholds and it has a
   viable leader, it can leave and found a **splinter settlement**. Banished people and outlaws may
   form **outlaw camps**.

### 5.5 Technology ceiling

Upper bound ≈ **high-medieval, pre-gunpowder**: steel, water mills, windmills, crossbows,
trebuchets, mail and coat-of-plates armor, stone castles. **No** gunpowder, printing press,
or blast-furnace cast iron. See [§9](#9-technology-tiers).

---

## 6. Time canon

| Quantity | Value |
|----------|-------|
| Real time per in-game day (1×) | **30 minutes** (player setting: 20–60) |
| Time ratio at default | **48 : 1** (1 game hour = 75 real seconds) |
| Daylight | Varies by season: ~16 h (midsummer) to ~8 h (midwinter) |
| Days per season | **8** |
| Seasons | **Spring, Summer, Autumn, Winter** |
| Days per year | **32** (≈ 16 real hours at 1×, ≈ 12 h if nights are slept through) |
| Date format | `Y{year} {Season} {day}` — e.g. `Y0 Spring 1` is Landfall |
| Rest day | Day 8 of each season ("Hearthday") — customary, not enforced by the sim |
| Market days | Days 4 and 8 of each season, once a market exists |
| Sleeping | Skips to morning if the player's safety allows; NPCs continue (at LOD3) |
| Pregnancy | **3 seasons** (24 days) |
| Aging | **1 year of age per in-game year** |
| Life stages | Child 0–13 · Youth 14–15 (apprentice-capable) · Adult 16–54 · Elder 55+ |
| Natural lifespan | Typically 55–80; violence, disease, childbirth and accidents dominate early deaths |

### 6.1 Interludes (time skips)

Generational change is impossible at 16 real hours per year without skipping time. **Interludes**
are a core mechanic, not a convenience:

- Available once the player has a bed in a shelter, after **the first Winter is survived**.
- **Wait:** skip hours, to morning, or to a specific time (any time, if safe).
- **Pass the season:** skip to the next season start.
- **Interlude:** skip **1–8 seasons** (max 2 years) per invocation; chainable.
- During a skip, the sim runs at **LOD3** (statistical). The player character follows **standing
  orders** (their routine) executed by the same AI that drives NPCs.
- **Interrupts** halt a skip: an attack on the player's settlement, death or birth in the player's
  household, a summons/petition only the player can answer, a crime accusation against the player,
  a declared war affecting the player, or the player's needs/health going critical.
- After a skip the player receives a **Chronicle** — a narrative summary of what happened, written by
  the LLM from the sim's event log (facts come from the log; prose from the LLM).

### 6.2 Playthrough length targets

| Milestone in a playthrough | Target in-game time | Target real time (typical) |
|----------------------------|--------------------|---------------------------|
| Survive first winter (Era 0 → 1) | Y0 Winter | 8–14 h |
| Village forms (Era 2) | Y2–Y4 | 20–35 h |
| Lordship / town (Era 3) | Y5–Y10 | 35–60 h |
| First war is possible (Era 4) | Y6–Y12 | 40–80 h |
| A full "saga" (two generations) | Y25–Y40 | 100+ h |

---

## 7. The era ladder

Eras are **descriptive milestones measured per settlement**, not timers or hard gates. Systems unlock
through *institutions* (a market, a mint, a court, a keep), not because an era label changed. The
roadmap builds features in era order.

| Era | Name | Settlement pop. | Typical time | Governance (typical) | Tech tier | Defining activities |
|-----|------|-----------------|--------------|----------------------|-----------|--------------------|
| 0 | **Landfall** | 12–40 (default 24) | Y0 Spring–Autumn | Contested: Charter heir vs. de-facto leaders; informal co-op | T0 | Shelter, fire, water, food, salvage, first tools, scouting, surviving |
| 1 | **Hamlet** | 20–60 | Y0 Winter – Y2 | Council of heads of household or acclaimed headman | T0–T1 | Permanent homes, first fields, storage, early specialization, barter |
| 2 | **Village** | 60–150 | Y2–Y5 | Headman / chieftain / charter-lord with council | T1–T2 | Markets, first coin use, workshops, property, law & crime, contact with other settlements |
| 3 | **Town & Lordship** | 150–500 | Y5–Y10 | **Feudal lordship**: lord, vassals, freemen, serfs; courts and taxes | T2–T3 | Guild-like professions, taxation, courts, walls, mills, iron, satellite hamlets |
| 4 | **Realms** | Several settlements; 400–1,500 across the land | Y6+ | Competing lords and kings; alliances, vassal webs | T3–T4 | Diplomacy, feuds, raids, wars, sieges, steel, castles |

---

## 8. Scale, population & simulation LOD

### 8.1 Population targets

| Quantity | Target |
|----------|--------|
| First-ship settlers | 24 (12–40) |
| Single settlement max (perf target) | 500 |
| Whole-world simulated persons (perf target) | **1,500** |
| Settlements in a mature world | 4–8 (incl. hamlets, splinter villages, outlaw camps) |

### 8.2 Simulation levels of detail (canonical tiers)

LOD here is **simulation fidelity**, independent of rendering LOD.

| Tier | Name | Who | Tick | Fidelity |
|------|------|-----|------|----------|
| **LOD0** | Embodied | NPCs within **~80 m** of the player or in active interaction/combat; cap **48** | 10 Hz behavior, per-frame animation | Full navmesh movement, physical actions, perception, combat, conversations |
| **LOD1** | Local | Same settlement / within **~400 m**, not embodied; rendered with simplified animation when visible | 1 Hz | Moves along path graph, actions resolved at task granularity |
| **LOD2** | Abstract | Elsewhere in the world | Every game hour | Schedule-level; tasks resolved statistically; social interactions rolled |
| **LOD3** | Statistical | Everyone during Interludes; far settlements | Every game day | Aggregate production/consumption, event rolls, relationship drift |

**Promotion/demotion must be seamless:** an NPC demoted from LOD0 to LOD2 and back must be in a
plausible state (position on their schedule, inventory consistent). Rendering budget: **≤ 200
visible characters** at 60 fps on recommended spec; battles use a dedicated mode targeting
**150 combatants** (stretch 300).

### 8.3 Hardware targets

| Tier | Spec | LLM mode |
|------|------|----------|
| Minimum | 6-core CPU, 16 GB RAM, GTX 1660 / RX 5600 / Apple M1 (16 GB) | Cloud LLM |
| Recommended | 8-core CPU, 32 GB RAM, RTX 3060 12 GB / Apple M-series 32 GB | Local LLM |

The game itself must stay within **≤ 4 GB VRAM** so a local 7–14B 4-bit model fits alongside it.

---

## 9. Technology tiers

Progression is about **capacity** (resources, infrastructure, skilled people, know-how), not research
points. Settlers *know of* iron and steel from home; reaching them requires finding ore, building
furnaces, producing charcoal at scale, and having people skilled enough to work them.

| Tier | Name | Key materials | Signature infrastructure | Signature outputs |
|------|------|---------------|--------------------------|-------------------|
| T0 | **Salvage & Stone** | Flint/chert, wood, bone, fiber, hide; salvaged iron (finite) | Campfire, lean-tos, drying racks | Knapped tools, spears, snares, baskets, bark/hide shelters |
| T1 | **Clay & Copper** | Clay, copper ore, flax/wool | Kiln, clay oven, loom, copper smelter | Pottery, bricks, copper tools, woven cloth |
| T2 | **Bronze** | Copper + **tin** (scarce) | Crucible furnace, casting molds | Bronze tools, weapons, fittings |
| T3 | **Iron** | Iron ore, charcoal at scale | Bloomery, forge, charcoal clamps, water-powered bellows (late) | Iron tools, weapons, mail, nails, plows |
| T4 | **Steel** | Steel via carburizing + quench/temper; crucible steel (rare) | Advanced forge, water hammer, castle works | Steel blades, crossbows, coat-of-plates, trebuchets |

Tiers overlap; a settlement can skip bronze entirely if it reaches workable iron first.

---

## 10. The person model (shared by player and NPCs)

Detailed rules are owned by the documents in [§16](#16-document-ownership-map); the **names and
scales** below are canonical.

### 10.1 Attributes (1–10, average 5)

**Strength, Endurance, Dexterity, Perception, Intellect, Charisma.** Change slowly with use, injury
and age.

### 10.2 Skills (0–100) — the canonical list (28)

| Domain | Skills |
|--------|--------|
| **Land** (7) | Farming, Husbandry, Foraging, Hunting, Fishing, Woodcutting, Mining |
| **Craft** (11) | Carpentry, Masonry, Metallurgy, Smithing, Bowyery, Leatherworking, Textiles, Pottery, Cooking, Brewing, Healing |
| **Social** (5) | Persuasion, Commerce, Leadership, Stewardship, Letters |
| **Martial & Body** (5) | Melee, Archery, Athletics, Stealth, Tactics |

Skill tiers: **Novice 0–19 · Apprentice 20–39 · Journeyman 40–59 · Expert 60–79 · Master 80–100**.
Each person also has a hidden **aptitude** per skill (XP multiplier **0.5–1.5**, mean 1.0).

### 10.3 Know-how

Discrete techniques ("Bloomery smelting", "Quench hardening", "Crop rotation", "Reading") held by
individuals with a proficiency. Recipes and processes require know-how; know-how spreads by
apprenticeship, teaching, and (once Letters exists) written manuals.

### 10.4 Personality

- **Five facets** (0–100, mean 50, SD 15): **Curiosity, Diligence, Sociability, Warmth, Volatility**
  (Big-Five analogues: Openness, Conscientiousness, Extraversion, Agreeableness, Neuroticism).
- **Nine values** (importance 0–100): **Family, Wealth, Status, Honor, Tradition, Faith, Fairness,
  Freedom, Loyalty.**
- **Traits:** 2–4 discrete traits per person from a curated list (e.g. *Hot-tempered, Greedy,
  Honest, Gossip, Coward, Brave, Pious, Vengeful, Lazy, Drunkard, Romantic, Paranoid, Charitable,
  Ambitious, Stubborn, Jealous*). Owned by [21-npc-ai](tech/21-npc-ai.md).

### 10.5 Needs, mood, emotion

- **Physical needs** (player and NPCs; 0–100 where 100 = fully satisfied): **Satiety, Hydration,
  Energy, Warmth.** Health is a separate injury/condition system.
- **Psychological needs** (NPCs drive behavior from these; the player has none — the human
  supplies them): **Social, Comfort, Safety, Purpose, Status.**
- **Mood:** −100 … +100 composite.
- **Emotions** (0–100, decaying): **Anger, Fear, Grief, Joy, Shame, Jealousy.**

### 10.6 Relationships & reputation

- Directed, per pair: **Opinion** (−100 … +100, sum of decaying modifiers), **Trust** (0–100),
  **Familiarity** (0–100), **Fear** (0–100), optional **Attraction** (0–100), plus tags (kin, spouse,
  friend, rival, enemy, mentor, apprentice, lord, vassal…).
- Per community: **Renown** (0–100, how widely known) and reputation axes (−100 … +100):
  **Honesty, Generosity, Courage, Peaceableness, Lawfulness, Piety**, plus per-profession
  **Competence**.
- **Memories** (episodic, with salience that decays), **Beliefs** (claims about the world that may be
  false), and **Rumors** (beliefs in transit). Ground truth always exists in the sim; beliefs can
  diverge from it.

---

## 11. Economy units

- Barter first; homeland coin exists from Day 1 but has little use until there is something to buy.
- Denominations: **farthing (f)**, **penny (d) = 4f**, **shilling (s) = 12d**, **crown = 20s = 240d**.
- **Internal value unit = 1 farthing.** All prices, wages, taxes and item base values are integers
  in farthings.
- **Anchor:** one day of unskilled labor ≈ **2d (8f)**. All other base values are calibrated
  against this anchor in [15-economy-and-trade](design/15-economy-and-trade.md).
- Local minting requires an authority, a mint, and copper/silver supply (Era 3+).

---

## 12. The player

- **Creation:** name, appearance, sex, age (18–35), one **background** (starting skill boosts +
  know-how; e.g. farmhand, woodsman, smith's apprentice, fisher, soldier, clerk, herbalist, peddler)
  and **2 traits** (chosen or rolled). No class. Backgrounds are owned by
  [19-player-experience](design/19-player-experience.md).
- **Same rules as NPCs:** skills, needs, injuries, law, relationships, aging.
- **Downed before dead:** reaching 0 health in combat leaves the player **incapacitated**; death
  follows only if enemies finish them, wounds go untreated, or the fall/drowning/exposure is fatal.
- **Mortality & Lineage (default mode):** when the player dies they may continue as an **heir**
  (adult child, spouse, or designated heir; inherits property and family name) or, failing that, as
  an adult with whom they had **Opinion ≥ 50 and Familiarity ≥ 50**. If none qualify, the saga ends
  with a Chronicle.
- **Difficulty modes:** **Forgiving** (death → recovery with consequences), **Lineage** (default),
  **Ironman** (one save; death ends the saga).

---

## 13. The LLM boundary ("hard systems, soft voice")

The vision calls the LLM layer "a facade for the real game mechanics while still having real world
impacts where it makes sense." Canon operationalizes that:

**LLMs and Jev MAY:**
- Generate NPC dialogue, barks, overheard conversations, court petitions, Chronicles, rumor wording,
  letters and books — always *from* sim state.
- Classify player language into structured signals: dialogue act, tone, topic/entity references,
  commitments (promises), claims (information/lies), persuasiveness score.
- Choose among options the sim has **pre-approved as equally valid** (e.g., which anecdote to share).
- Summarize conversations into memories (stored alongside structured facts the sim extracted).

**LLMs and Jev MUST NOT:**
- Decide whether a request is accepted, what a price is, whether a fight starts, a crime's verdict,
  a crafting result, or any other outcome. They provide **inputs** to hard-coded functions.
- Invent facts the NPC could not know, create items/money, or change state directly.
- Receive player text in a way that lets it override instructions (player text is **untrusted data**).

**Bounded influence:** a language-derived signal (e.g., persuasiveness) may move a hard-coded outcome
by at most a **per-system clamp** (default: **±15%** on prices/acceptance probability), scaled by the
listener's hard-coded *susceptibility* (personality, relationship, skill). The player's **Persuasion**
skill matters as much as their words.

**Fallback chain** (every touchpoint): cloud LLM / Jev → local LLM (structured output) →
heuristics/templates. The game must be completable with LLMs disabled ("template mode").

---

## 14. Scales, units & conventions

| Convention | Value |
|------------|-------|
| Distance | meters (Godot units = 1 m) |
| Mass | kilograms |
| Game time | integer **game-minutes** since `Y0 Spring 1 00:00` (`long`) |
| Probabilities | 0.0–1.0 in code; percentages in docs |
| 0–100 scales | needs, skills, trust, familiarity, fear, attraction, emotions, renown |
| −100…+100 scales | opinion, mood, reputation axes |
| IDs | Stable string ids for content (`item.iron_axe`, `skill.smithing`); 64-bit ints for runtime entities |
| Randomness | All randomness from seeded, per-system RNG streams (determinism) |
| Content files | `snake_case` YAML ids, one definition per id |

---

## 15. Milestone IDs

Subsystem docs tag features with the milestone that first needs them. Details in
[30-roadmap](production/30-roadmap.md).

| ID | Name | Focus |
|----|------|-------|
| **M0** | Foundations | Repo, solution, sim core skeleton, content pipeline, LLM gateway, CI |
| **M1** | Talking Camp | De-risk: LLM dialogue + social sim with ~24 NPCs; scale spike to hundreds headless |
| **M2** | Landfall | Era 0 vertical slice: terrain region, survival, gathering, first crafts, basic combat |
| **M3** | Hamlet | Era 1: farming, construction, skills/jobs, seasons & winter, barter, save/load |
| **M4** | Village | Era 2: markets/shops, coin, more professions, crime & law, families/aging, Interludes |
| **M5** | Town & Lordship | Era 3: feudal governance, court, taxes, LOD at scale, schism/exodus |
| **M6** | Realms at War | Era 4: diplomacy, raids, muster, battles, sieges, war economy & morale |
| **M7** | Local-first & Polish | Local LLM packaging, performance, onboarding, balance |
| **M8** | Early Access | Content completeness, stability, release ops |

---

## 16. Document ownership map

Each concept is **defined** in exactly one document; others reference it.

| Concept | Owning document |
|---------|-----------------|
| Pillars, tenets, canonical numbers & names | **01-canon** (this doc) |
| Player fantasy, core loops, tone, art & audio direction | [02-game-overview](02-game-overview.md) |
| World generation, biomes, resources, climate, flora/fauna, expeditions' arrival logic | [10-world-and-setting](design/10-world-and-setting.md) |
| Physical needs, health, injuries, disease, exposure, food spoilage, shelter, Landfall scenario | [11-survival](design/11-survival.md) |
| Skills, XP, aptitude, attributes, know-how, teaching/apprenticeship, professions & job choice | [12-skills-and-professions](design/12-skills-and-professions.md) |
| Crafting model, quality, materials, per-profession minigames for crafts **and land work** (farming incl. crop/soil model, husbandry, hunting, fishing, foraging, woodcutting, mining), NPC work resolution | [13-crafting-and-minigames](design/13-crafting-and-minigames.md) |
| Tech tiers detail, buildings, construction, infrastructure, settlement layout | [14-technology-and-buildings](design/14-technology-and-buildings.md) |
| Value, prices, barter, haggling, shops, markets, currency, wages, property, taxes (mechanics) | [15-economy-and-trade](design/15-economy-and-trade.md) |
| Relationships, reputation, memory/belief/rumor propagation, family, romance, life cycle, crime detection & witnesses, faith | [16-social-systems](design/16-social-systems.md) |
| Governance forms, leadership emergence, legitimacy, feudal obligations, laws & justice, court, succession, schism, diplomacy between polities | [17-governance-and-law](design/17-governance-and-law.md) |
| Personal combat, fights & duels, feuds, raids, war causes, muster/conscription, battles, sieges, war's effects | [18-conflict-and-warfare](design/18-conflict-and-warfare.md) |
| Player creation & backgrounds, player paths, UI/UX, dialogue UI, journal, death/lineage UX, difficulty, onboarding | [19-player-experience](design/19-player-experience.md) |
| Engine/sim architecture, ticks, ECS/data layout, save/load, determinism, content pipeline, testing, perf | [20-architecture](tech/20-architecture.md) |
| NPC agent architecture: personality model & trait list, needs-driven utility AI, schedules, jobs, emotion, irrationality, sim LOD behavior | [21-npc-ai](tech/21-npc-ai.md) |
| LLM & Jev integration: dialogue pipeline, prompts, context building, guardrails, providers, budgets, fallbacks | [22-llm-integration](tech/22-llm-integration.md) |
| Milestones, sequencing, exit criteria | [30-roadmap](production/30-roadmap.md) |
| Risks & consolidated open questions | [31-risks-and-open-questions](production/31-risks-and-open-questions.md) |
| Terminology | [99-glossary](99-glossary.md) |

---

## 17. Change log

| Date | Change |
|------|--------|
| 2026-10-03 | v0.1 — initial canon derived from the original vision document. |
