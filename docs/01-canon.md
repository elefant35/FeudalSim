# 01 — Canon: Core Decisions, Numbers & Names

> **Status:** Draft v0.3 · **Applies to:** every other document in `docs/`
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
13. [The LLM boundary ("language decides, systems resolve")](#13-the-llm-boundary-language-decides-systems-resolve)
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

1. **Language decides, systems resolve.** Language models may make real choices for characters —
   warm to someone, pick or defuse a fight, strike or refuse a bargain — but only from options a
   deterministic system offers, and that system enacts and resolves the result. A fight started with
   words is settled by the combat system; a sale talked into is enacted by the trade system. See
   [§13](#13-the-llm-boundary-language-decides-systems-resolve).
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
   works. Every LLM decision has a deterministic policy fallback, and every LLM line has a template.

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
| Simulation core | **Pure C# class library with zero engine dependencies** — it references no engine, IO, network or YAML library — deterministic, tickable, runnable headless. The Godot client is a view + input layer. | [ADR-0002](adr/0002-headless-deterministic-sim-core.md) |
| .NET version | **`net10.0` (.NET 10 LTS)** since M1 — a spike proved Godot 4.7.2 runs a `net10.0` game assembly ([ADR-0010](adr/0010-dotnet-version.md) step 3); `net8.0` was the M0 target (.NET 8 support ends 2026-11-10). | [20-architecture](tech/20-architecture.md) |
| Sim step & clocks | Fixed **100 ms step** (10 Hz) on its own thread; a monotonic step counter plus a game clock with game-ms precision. Stored timestamps stay in whole game-minutes (§14). | [20-architecture](tech/20-architecture.md) |
| Data layout | **Custom id-ordered domain tables** (structure-of-arrays), not a third-party ECS library. | [20-architecture](tech/20-architecture.md) |
| Determinism scope | Bit-identical on the **same build + OS + CPU architecture**. Saves are portable across platforms; replays and golden tests are per platform. | [ADR-0002](adr/0002-headless-deterministic-sim-core.md) |
| Embodiment boundary | The sim is authoritative for everything **except LOD0 body pose**: Godot moves embodied bodies and reports their results back as logged commands. Headless runs, LOD1 puppets and battle crowds use one sim-side kinematic mover. | [20-architecture §3](tech/20-architecture.md) |
| Terrain | **Terrain3D** (GDExtension), pending spike S4; custom chunk terrain as the fallback. | [20-architecture](tech/20-architecture.md) |
| Generative LLM | Any **OpenAI-compatible chat-completions endpoint**. Start: **OpenRouter** (key `OPENROUTER_KEY` in `.env`) with a Qwen-family instruct model, thinking mode disabled — `qwen/qwen3-14b` is the default, with newer Qwen 3.5–3.8 models in the M1 bake-off (all listed on OpenRouter as of 2026-10-03). Target: **local** (llama.cpp / Ollama / LM Studio): **8B 4-bit is the local default** on 12 GB GPUs; 14B 4-bit needs ≥ 16 GB VRAM or ≥ 32 GB Apple unified memory. One resident local model serves all roles. | [22-llm-integration](tech/22-llm-integration.md) |
| Fast decider | A sub-second model that classifies player language and makes quick choices among **fixed options**, returning probabilities. **Now:** a small Qwen model on OpenRouter reading log-probabilities over single-letter option labels (`qwen/qwen3.5-9b`, verified working 2026-10-03). **Later:** Jev (TypeSafe) once direct API access exists, and/or **Laya** (open source, runs locally; must be fine-tuned on our data). Fallbacks: the resident local model with the same technique → heuristics. | [ADR-0003](adr/0003-language-decides-systems-resolve.md), §4.1 |
| Content data | Authored content (items, recipes, crops, skills, traits, buildings…) in **YAML** files under `/content`, validated against JSON Schema in CI. | [20-architecture](tech/20-architecture.md) |
| Saves | Snapshot of sim state + append-only event log. | [20-architecture](tech/20-architecture.md) |

### 4.1 Jev — what we know, and the fast decider until we have it

Jev is a "decision model" from TypeSafe AI. It does **not** generate text; it reads a *state*
(text / JSON / text arrays) and answers *questions* with calibrated probabilities. Facts below come
from third-party write-ups (Sept 2026) and **must be verified against TypeSafe's own docs before
implementation**:

- Question types: **choice** (up to 255 options → selected option + per-option probabilities +
  confidence), **score** (2–10 ordered levels → probability-weighted mean + per-level probabilities),
  **noul** (yes/no → probability of yes). Multiple questions evaluate in parallel in one call; they
  cannot depend on each other.
- Reported latency 70–500 ms; ~32K context; ~$0.042 per million input tokens, output free.
- Documented weaknesses: **weak arithmetic, counting and date ordering**; degrades with irrelevant
  context; **does not treat input as hostile — injected instructions can move answers**; confidence
  measures concentration, not correctness.

**Availability (checked 2026-10-03):** OpenRouter's catalog lists only **`typesafe/jev-router`** — a
router that picks *other* models for each request — **not** the Jev decision model (`typesafe/jev-1.13`
is not listed). Direct Jev access means TypeSafe's own API (waitlisted) or Braintrust. We have only an
OpenRouter key for now.

**The fast-decider role (canon):** one interface (`IDecider`) with interchangeable providers:

| Provider | Status | How it answers a choice question |
|----------|--------|----------------------------------|
| **OpenRouter small LLM** (default now) | Working: `qwen/qwen3.5-9b` returned a sensible distribution over four labeled options (≈ 117 input tokens, ≈ $0.00001 per decision) | Options labeled `A`, `B`, `C`…; `max_tokens = 1`; read `top_logprobs` of the first token and normalize; request with `provider.require_parameters = true` so the serving provider actually returns log-probabilities |
| **Jev (TypeSafe API)** | Pending access | Native choice / score / yes-no questions |
| **Laya** (open source, local) | Candidate local fast decider. `pip install laya` (Apache-2.0, Convai Innovations); English checkpoint 421M parameters (512-token context), multilingual 322M (per the Hugging Face model card; one third-party article swaps these); ~33–40 ms per question on a T4 GPU, ~0.2–0.5 s on CPU; `laya-serve` exposes `POST /v1/systemone`, **the same API shape as Jev**; ONNX export allows in-process use from C#. **Base checkpoints are near chance zero-shot on typed decisions (0.36)** and ship over-confident, so it needs fine-tuning on our own decision data plus temperature calibration. Not on OpenRouter | Native choice / score / yes-no questions |
| **Local model** | M7 | Same log-probability technique on the resident local model |
| **Heuristics** | Always available | Lexicons, regex and a small linear classifier with calibrated pseudo-probabilities |

**Design consequences (canon):** `IDecider`'s contract is the **"System One" shape** — a state plus
typed questions (choice / score / yes-no) in, typed answers with probabilities out — so Jev, Laya and
the OpenRouter-LLM adapter are interchangeable. Decisions recorded in play and in M1 test suites
become the training set for fine-tuning a local Laya checkpoint. Whichever provider fills it, the
fast decider classifies player language and makes quick choices **among fixed options only**. It never computes numbers, its inputs
containing player text are untrusted, and every choice passes the same guards as an LLM choice (§13).

---

## 5. Setting canon

### 5.1 The land

- **Farstrand** *(working name)* — a large, temperate, previously **uninhabited** landmass across
  the sea from the settlers' homelands. No native peoples. (Ancient ruins are an open question,
  not canon.)
- Playable world: an **8 km region, implemented as 8,192 m × 8,192 m centred on the origin**; the
  landmass occupies roughly **35–45 km²** of it, the rest is sea, islets and reefs. Generated from a
  seed using hand-authored constraints (see [10-world-and-setting](design/10-world-and-setting.md)).
- Crossing the land on foot takes about **three game days** of travel (see movement speeds, §14).
- Biomes (canonical set): **Coast & Dunes, Temperate Meadow/Grassland, Broadleaf Forest, Pine Forest,
  Wetland/Marsh, River Valley, Hills & Moor, Highland/Mountain** (with a snowline). Rivers, lakes
  and at least one large estuary exist on every map.
- Resources are **unevenly distributed by design**. Uneven distribution drives trade, expansion and
  war. Canonical deposit counts per map:

  | Deposit | Count | Notes |
  |---------|-------|-------|
  | Tin (placer) | 1–2 | The bronze bottleneck |
  | Hill iron ore | 2–3 | ≥ 3 km from the best farmland |
  | Bog iron | 2–5 | Poor quality, but near the lowlands |
  | Copper | 2–4 | — |
  | Galena (lead with silver) | **exactly 1** | 20–180 kg recoverable silver: local silver is real but scarce |
  | Sea coal | 0–1 | — |
  | Gold | 0 | None in v1 |
- Wildlife (canonical starting set): deer, boar, hare, wolf, bear, fox, wild goats, waterfowl, fish,
  plus wild bees, seabirds, seals (optional coastal haul-outs), and ship-borne rats and mice.
  No fantasy monsters in v1 (the "fantasy" is in the setting, not creatures). *Open question: light
  folklore / superstition layer.*
- **Domestic animals are never native.** Landfall brings goats and hens (and ship's cats and dogs);
  sheep, cattle/oxen, pigs and **horses arrive only by ship or with other expeditions** (2–8
  Varrowan horses before the Silence). Riding exists; **mounted combat is not in v1**.

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
- Landfall begins at **05:30** in a storm; the lord drowns within the first game hour.
- Salvage from the ship (canonical quantities; details in [11 §15](design/11-survival.md#15-the-landfall-scenario)
  and [15 §10](design/15-economy-and-trade.md)): a **limited, irreplaceable stock of iron tools**
  (3 felling axes, 4 knives, 1 saw, 1 hammer), **60 kg of seed grain**, **4 goats and 8 hens**,
  20 ells of cloth, rope, and **≈ 240 rations** of provisions (≈ 11 colony-days at full ration —
  the canonical "~10 days"). Eating the seed grain buys ~4 more days: a deliberate dilemma. The hull
  can be broken up for 40–120 kg of scrap iron.
- The **Charter Chest** holds the Charter, the lord's seal ring and **3 crowns (2,880f)**; settlers'
  purses average **40f per adult**. Legally the salvage is the late lord's estate (so the heir's);
  by custom it is the **Common Store**. This is the first property conflict.
- The **Charter is a physical item** (it can be stolen, forged or burned). Its text is deliberately
  ambiguous: "lawful heirs", "a day's ride of first landing", "a fifth to the Crown".
- The manifest includes the **bosun**, the **steward and his ledger**, and **3–6 indentured
  settlers** owing three years' service to the Charter-holder; 1–3 settlers hold secrets (e.g. an
  Ashen sympathizer among the orthodox). See [10 §15](design/10-world-and-setting.md).

### 5.4 How multiple societies come to exist (so war is possible)

Rival polities emerge through **four channels**, all simulated:

1. **Other expeditions** land elsewhere on Farstrand: the Osmeri between **Y1 Autumn and Y2 Autumn**
   (never in Winter), one or two more between **Y3 and min(Y5, Y*N*)**. None arrive after the
   Silence. Each starts as its own settlement with its own culture.
2. **Resupply / immigrant ships** arrive at **Summer 1 (+0–3 days)** for Years 1 to *N* (*N* rolled
   3–6), stay 2 days, add settlers and livestock to existing colonies, and trade (they sell at 1.5×
   homeland prices and buy exports at 0.6× base). If Charter dues are missed twice, a **Crown reeve**
   arrives on the next ship.
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
| Real time per in-game day (1×) | **30 minutes** (player setting, restricted to 20, 24, 25, 30, 32, 36, 40, 45, 48, 50 or 60 so every 100 ms step is a whole number of game-ms; balance and golden tests run at 30) |
| Time ratio at default | **48 : 1** (1 game hour = 75 real seconds) |
| Daylight | Varies by season: ~16 h (midsummer) to ~8 h (midwinter) |
| Days per season | **8** |
| Seasons | **Spring, Summer, Autumn, Winter** |
| Days per year | **32** (≈ 16 real hours at 1×, ≈ 12 h if nights are slept through) |
| Date format | `Y{year} {Season} {day}` — e.g. `Y0 Spring 1` is Landfall |
| Day-of-year index | **`d` = 1…32** (Spring 1 = 1, Summer 1 = 9, Autumn 1 = 17, Winter 1 = 25) |
| Rest day | Day 8 of each season ("Hearthday") — customary, not enforced by the sim |
| Market days | Days 4 and 8 of each season, once a market exists; the day-8 **Hearth Market** is held in the morning after worship |
| Quarter day / court day | **Day 1 of each season**: rents and dues fall due, and the manor court (hallmote) sits; great courts on Spring 1 and Autumn 1 |
| Council day | Day 8 evening, after the Hearth Market |
| Grain harvest window | **Summer 5 – Autumn 4** (`d` 13–20) |
| Campaign season | Summer 1–4 and Autumn 5–8 (outside the harvest); Winter campaigning is penalized |
| Moon | One lunar cycle per season (full moon on day 5) |
| Daylight | `L(d) = 12 + 4·sin(2π(d − 3)/32)` hours (midsummer = Summer 3, midwinter = Winter 3); owned by [10 §6.1](design/10-world-and-setting.md#61-calendar-daylight--moon) |
| "A year and a day" | **33 days** (serf freedom in a chartered town; Brannoch handfasting) |
| Apprenticeship (default) | **64 days** (2 game years) |
| Autosave | Daily at 06:00 game time, plus before/after Interludes and battles and on quit |
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

### 6.3 Focus time

While a **conversation, a court session, or battle mode** is open, the world clock runs at **12:1**
(¼ of normal). The world stays live: others can walk up, interrupt or overhear. A 3-minute
conversation therefore costs ~36 game minutes, not 2.4 game hours.

### 6.4 Time-scale policy

The calendar is compressed (32-day years) but days are not. Rules every system follows:

- **Clock-scale processes run 1:1** with game time: hunger, thirst, sleep, walking, bleeding,
  cooking, a fire burning down, daily milk and eggs, foraging and fishing per hour.
- **Calendar-scale processes are divided by K = 365/32 ≈ 11.4**, with a 1-game-day floor for
  anything a person must respond to: illness courses beyond a day, animal gestation and growth
  (**1 real month ≈ 2.7 game days**), tanning and seasoning times, pregnancy (9 months → 24 days).
- **Once-a-year harvests** use **harvest compression κ = 4**: per-area seed rates and yields are
  ¼ of historical, so that land stays scarce (tenet 5) while people eat realistic daily rations.
- Real years map to game years (aging, terms of service, apprenticeships).

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
| **LOD2** | Abstract | The **near region (≤ 1.5 km)** plus each agent's relevance set (kin, counterparties) | Every game hour | Schedule-level; tasks resolved statistically; social interactions rolled |
| **LOD3** | Statistical | Everyone during Interludes; settlements beyond 1.5 km in normal play | Every game day (partial-day steps during sleep skips) | Aggregate production/consumption, event rolls, relationship drift |
| **LOD0-B** | Battle | All combatants while battle mode is active: **≤ 150 (stretch 300)** | 10 Hz | The 48 nearest the player get full LOD0 fidelity; the rest use sim-side kinematics at 5 Hz decisions with identical damage math. The world clock runs on focus time (§6.3) |

**Hysteresis:** promote to LOD0 at ≤ 80 m (if a slot is free); demote at ≥ 100 m after 5 s.
LOD1 ↔ LOD2 at 400 / 450 m (10 s). The cap of 48 counts physics-embodied NPCs only; the player is
always embodied and not counted. Priority for slots: conversation partner > hostile/in combat > in
view > nearest.

**Promotion/demotion must be seamless:** an NPC demoted from LOD0 to LOD2 and back must be in a
plausible state (position on their schedule, inventory consistent).

**Sparsity:** each person keeps at most **150 relationship edges** (LOD3 uses the top 24 contacts). Rendering budget: **≤ 200
visible characters** at 60 fps on recommended spec; battles use a dedicated mode targeting
**150 combatants** (stretch 300).

### 8.3 Hardware targets

| Tier | Spec | LLM mode |
|------|------|----------|
| Minimum | 6-core CPU, 16 GB RAM, GTX 1660 / RX 5600 / Apple M1 (16 GB) | Cloud LLM |
| Recommended | 8-core CPU, 32 GB RAM, RTX 3060 12 GB / Apple M-series 32 GB | Local LLM |

The game itself must stay within **≤ 4 GB VRAM** so a local model fits alongside it: **8B 4-bit
(~5 GB) on 12 GB GPUs**; 14B 4-bit (~9 GB) only with ≥ 16 GB VRAM or ≥ 32 GB Apple unified memory.
Minimum spec defaults to cloud (or template mode).

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

- **The tree** (identical for player and NPCs): a **specialization** at 40, **perks** at 60/70/90, a
  **capstone** at 80; apprentice slots 1/2/3 at Journeyman/Expert/Master.
- **XP:** learn by doing — ~10 XP per nominal labor-hour at matched difficulty; easy work teaches
  almost nothing. Curve `XPreq(L) = 11 × 1.055^L` ([12 §5](design/12-skills-and-professions.md)).
- **Rust, not decay:** skill levels never fall; after 16 idle days a temporary penalty grows (+1 per
  8 days, capped at 15% of the level) and practice clears it.
- **Children's** skills are capped at 25 until age 14.
- Swimming, climbing and breath-holding resolve through **Athletics**.

### 10.3 Know-how

Discrete techniques ("Bloomery smelting", "Quench hardening", "Crop rotation", "Reading") held by
individuals with a proficiency: **Aware · Familiar · Practiced · Mastered**. Every homeland-born
adult is *Aware* of all homeland know-how through T4; reading a manual reaches *Familiar* at most.
Recipes and processes require know-how; know-how spreads by apprenticeship, teaching, and (once
Letters exists) written manuals.

### 10.4 Personality

- **Five facets** (0–100, mean 50, SD 15): **Curiosity, Diligence, Sociability, Warmth, Volatility**
  (Big-Five analogues: Openness, Conscientiousness, Extraversion, Agreeableness, Neuroticism).
- **Nine values** (importance 0–100): **Family, Wealth, Status, Honor, Tradition, Faith, Fairness,
  Freedom, Loyalty.** Each homeland culture has its own value means ([21 §4](tech/21-npc-ai.md)).
- **Traits:** 2–4 discrete traits per person (35% / 45% / 20%) from a **catalog of 45** owned by
  [21-npc-ai](tech/21-npc-ai.md) (including *Hot-tempered, Greedy, Honest, Gossip, Coward, Brave,
  Pious, Vengeful, Lazy, Drunkard, Romantic, Paranoid, Charitable, Ambitious, Stubborn, Jealous*).
  Traits are partly heritable and revealed in childhood.
- **Orientation:** each person has an attraction profile. Attraction exists **only between two
  adults (16+)** who are orientation-compatible — a hard gate no culture, setting or LLM output can
  override.
- **Irrationality** is tuned by one master knob, **K_irr** (0–2, default 1.0), shown to players as
  the **Drama** setting.

### 10.5 Needs, mood, emotion

- **Physical needs** (player and NPCs; 0–100 where 100 = fully satisfied): **Satiety, Hydration,
  Energy, Warmth.** Health is a separate injury/condition system.
- **Psychological needs** (NPCs drive behavior from these; the player has none — the human
  supplies them): **Social, Comfort, Safety, Purpose, Status.** Children have **Play** instead of
  Purpose, and no Status need.
- **Mood:** −100 … +100 composite. Bands: Elated / Content / Neutral / Low / Breaking at ±20 and
  ±60; below −60 an NPC can hit a **breaking point**.
- **Emotions** (0–100, decaying): **Anger, Fear, Grief, Joy, Shame, Jealousy.** Half-lives (game
  time): Anger 4 h, Fear 1 h, Grief 3 days, Joy 6 h, Shame 1 day, Jealousy 1 day.
- **Hidden states:** **Drunkenness** (0–100) and, for addicts, **Craving**.

### 10.6 Relationships & reputation

- Directed, per pair: **Opinion** (−100 … +100, sum of decaying modifiers; serious harms leave a
  permanent floor), **Trust** (0–100), **Familiarity** (0–100), **Fear** (0–100), optional
  **Attraction** (0–100), plus tags (kin, spouse, friend, rival, enemy, mentor, apprentice, lord,
  vassal…). Starting Familiarity: shipmates 15–35, household 70, other kin 50.
- Per community: **Renown** (0–100, how widely known) and reputation axes (−100 … +100):
  **Honesty, Generosity, Courage, Peaceableness, Lawfulness, Piety**, plus per-profession
  **Competence**. Reputation is held **per observer**, derived from their beliefs; a community's
  view is a weighted aggregate, **never a global number**. Communities may assign **epithets**.
- **Memories** (episodic, with salience that decays), **Beliefs** (claims about the world that may be
  false), and **Rumors** (beliefs in transit). Ground truth always exists in the sim; beliefs can
  diverge from it. Belief thresholds: forget < 0.1 · hold ≥ 0.5 · act ≥ 0.6 · certain ≥ 0.85.
- Speech is audible at **10 m**; raised voices at **25 m**.

### 10.7 Social structure

- **Status bands** (ordinal standing, every era): **0 Unfree/landless · 1 Commoner · 2 Master/merchant
  · 3 Notable · 4 Ruler** (outlaws sit outside, with no legal protection). Defined in
  [17 §9.1](design/17-governance-and-law.md).
- **Kin-groups** (family name, house, clan) are first-class; they carry succession and feuds ([16](design/16-social-systems.md)).
- **Political factions** are owned by [17](design/17-governance-and-law.md); informal cliques and
  culture/faith groups by [16](design/16-social-systems.md).
- **Legitimacy** (0–100) comes from five sources: **Tradition/Charter, Competence, Popularity, Force,
  Faith**. Authority — whether a given order is obeyed — is a per-person, per-order probability.
- **Governance forms:** Co-op, Household Council, Headman, Chieftain, Charter-lordship, Feudal
  lordship, Kingship; variants Elders' Synod (Ashen), Merchant Council (Osmeri), Outlaw Band.
- **CommunityTension** (0–100) per community drives scapegoating and unrest.
- **Faith working names:** Ember clergy are **Keepers**; holy days **Kindling** (Spring 1),
  **Midsummer** (Summer 4), **Harvest Home** (Autumn 8), **Cairn Night** (Autumn 8, Brannoch),
  **Long Night** (Winter 8).

### 10.8 Work, skill checks & quality

- **One shared skill check, `Resolve()`**, owned by [12 §6](design/12-skills-and-professions.md):
  `R = E − D + Logistic(0, 8)` (noise clamped ±31); performance score `PS = 50 + 1.25R`; ±3 effective
  skill per attribute point away from 5. NPCs roll the noise; **the player's minigame replaces it
  within ±24**, so skill still dominates. Multi-stage processes use a per-process RNG stream
  (reloading cannot reroll a craft).
- **Item quality `Q`** is an integer 0–100 with **50 = common**. Grades: **Crude 0–19 · Poor 20–39 ·
  Common 40–59 · Fine 60–74 · Superior 75–89 · Masterwork 90–100**; **Ruined** is an outcome, not a
  grade. Quality caps at 89 unless a masterwork is deliberately attempted. Maker's marks from
  Journeyman. Owned by [13](design/13-crafting-and-minigames.md).
- **Every process stage costs at least its nominal game time**; batch work, delegation and
  automation are never better than a skilled hand.

### 10.9 Body, health & food

- **Health** is derived (0–100) from injuries on **six body regions** (Head, Torso, ArmL, ArmR, LegL,
  LegR) with severity tiers at 8 / 20 / 35 / 55, plus **Blood** (0–100; **downed below 35**) and a
  bruise pool for light hits. Owned by [11](design/11-survival.md); damage types **cut, pierce, blunt**
  (+ stun for brawling) by [18](design/18-conflict-and-warfare.md).
- **Combat Stamina** is a fast pool, separate from the Energy need.
- **Food unit:** 1 Satiety point = 25 kcal; **1 ration = 100 Satiety ≈ 1 kg of bread** (one adult's
  moderate day); annual need ≈ **3,040 Satiety** per person. Death timelines: about 2 days without
  water, about 6 days without food.
- **Movement:** walk 1.6 m/s, jog/travel 4.0 m/s (≈ 300 m per game hour at the default day),
  sprint 6.5 m/s.

---

## 11. Economy units

- Barter first; homeland coin exists from Day 1 but has little use until there is something to buy.
- Denominations: **farthing (f)**, **penny (d) = 4f**, **shilling (s) = 12d**, **crown = 20s = 240d**.
- **Internal value unit = 1 farthing.** All prices, wages, taxes and item base values are integers
  in farthings.
- **Anchor:** one day of unskilled labor ≈ **2d (8f)**. All other base values are calibrated
  against this anchor in [15-economy-and-trade](design/15-economy-and-trade.md).
- Local minting requires an authority, a mint, and copper/silver supply (Era 3+). Debasement is
  possible and is detected over time.
- **Monetization index μ** (per settlement): 16-day average share of trade value settled in coin.

| Canonical economic constant | Value | Owner |
|-----------------------------|-------|-------|
| Work-day | 8 work-hours | [15](design/15-economy-and-trade.md) |
| Labor-rate ladder (f per work-hour) | Novice 1.0 · Apprentice 1.25 · Journeyman 1.5 · Expert 2.0 · Master 3.0 | 15 |
| Quality multiplier on value | `clamp(2^((Q − 50)/25), 0.25, 4)` | 15 |
| Grain | 2f per kg | 15 |
| Ration-day | ≈ 3f | 15 |
| Field productivity target | ≈ 5 kg grain per Apprentice field-day; arable 0.35–0.5 ha per adult | [13](design/13-crafting-and-minigames.md) |
| Taxes (defaults) | Tithe 1/10 in kind (⅔ faith / ⅓ lay); hearth tax 8f/household/yr; poll tax 4f/adult/yr; sales toll 1/24; mill toll 1/16 | 15, [17](design/17-governance-and-law.md) |
| Corvée | 2 labor-days/season per serf household + 1 Autumn boon day | 15, 17 |
| Knight's fee | ≈ 8–12 households, ≈ 1,000f/yr gross; 8 field days + 4 castle-guard days per year | 17 |
| **Scutage** (knight's service commuted) | 240f per knight's fee per year | 15, 17 |
| **Levy commutation** (a conscript's buy-out) | 48f per seasonal service | [18](design/18-conflict-and-warfare.md) |
| Manumission | 480f | 17 |
| Fine bands | 2–8f · 8–48f · 48–240f · 240–960f · capital; fines capped at 50% of liquid wealth | 17 |
| Wergild / ransom | Table from serf 480f to lord 9,600f | 18 |
| Trade stakes thresholds | Transfers ≥ 1 shilling (48f) are **high** stakes (floor 0.05); ≥ 1 crown (960f) are **critical** (deterministic `p_i ≥ 0.25`) — §13.1 | [22](tech/22-llm-integration.md) |

---

## 12. The player

- **Creation:** name, appearance, sex, age (18–35), one of **10 backgrounds** (farmhand, woodsman,
  smith's apprentice, fisher, soldier, clerk, herbalist, peddler, acolyte, joiner's apprentice; each
  +75 skill points plus know-how and a kit with at most one salvaged iron item), **attribute
  point-buy** (all six start at 5; distribute 6 points; range 3–8), **2 traits** (chosen or rolled),
  2 **ship ties**, and a **faith stance**. No class. Owned by
  [19-player-experience](design/19-player-experience.md).
- **Same rules as NPCs:** skills, needs, injuries, law, relationships, aging. **One deliberate
  exception:** when the player character runs on **standing orders** (Interludes), emotional
  hijacks, breaking points and vices are switched off and decision noise is pinned low — the
  player's character never does something irrational the player didn't choose.
- **Downed before dead:** reaching 0 health in combat leaves the player **incapacitated**; death
  follows only if enemies finish them, wounds go untreated, or the fall/drowning/exposure is fatal.
- **Mortality & Lineage (default mode):** when the player dies they may continue as an **heir**
  (adult child, spouse, or designated heir; inherits property and family name) or, failing that, as
  an adult toward whom the deceased had **Opinion ≥ 50 and Familiarity ≥ 50**. If none qualify, the
  saga ends with a Chronicle. Carry-over (house reputation, inherited debts and feuds, "kin of X"
  opinions) is owned by [19 §10](design/19-player-experience.md).
- **Difficulty modes:** **Forgiving** (death → recovery with consequences), **Lineage** (default),
  **Ironman** (one save; death ends the saga). An optional **Ironman Lineage** variant (one save, but
  heirs allowed) is off by default.
- **Ambitions** are optional, player-set journal goals with no mechanical reward; **Leads** are the
  sim's opportunities surfaced to the player (≤ 2 passive per day).

---

## 13. The LLM boundary ("language decides, systems resolve")

The vision calls the LLM layer "a facade for the real game mechanics while still having real world
impacts where it makes sense." The owner clarified the rule on 2026-10-03: **language models may make
real decisions for characters** — build up or sour relationships, start or prevent fights, strike or
refuse bargains, and more — **but the systems those decisions run on are deterministic.** *A player
can start a fight with words, but it is settled by the combat system. They can convince someone to
buy something, but the trade is enacted by the trading system.*

### 13.1 Decision points

Whenever a character must choose during a language-driven moment, the owning system opens a
**decision point (DP)**:

1. **Open.** Examples: an NPC answering the player; an NPC lord hearing a petition the player
   attends; a councillor voting in a session the player attends; an envoy negotiating; a duelist
   answering a challenge; a bystander watching a quarrel.
2. **Menu.** The owning system builds the **option menu** deterministically. Each option has:
   - an id with a fixed meaning (`retort`, `shove`, `attack`, `walk_away`, `accept_at_price`,
     `counter_step_2`, `refuse`, `warm_to_speaker`, `call_guards`, `accept_request`…);
   - **fixed parameters** computed by the system (price, quantity, target, opinion delta, escalation
     rung) — **the decider never invents a number**;
   - **eligibility** (feasibility, inventory, law, physics, the escalation state, who is present);
   - a **base propensity** `p_i`: the probability the deterministic policy would choose it, from
     personality, needs, emotions, relationship, beliefs and the speaker's skill;
   - a **stakes** level: low, medium, high or critical.

   The menu's shape *is* the hard-coded willingness to be swayed: a stubborn, greedy merchant's menu
   has few concession steps; a warm, susceptible friend's has more.
3. **Decide.** One **decider** picks one option:
   - **LLM** — when the LLM is already writing the character's reply, it emits the choice first
     (decision-first structured output) and then speech conditioned on it.
   - **Fast decider** (§4.1) — sub-second choices from loose text: a reaction to a shouted insult, a
     passer-by stepping in, yield or mercy mid-fight.
   - **Policy** — the deterministic fallback: sample from `p_i` with the seeded RNG.
4. **Guard.** The Dialogue Rules Engine (DRE) checks **feasibility and bounds, not taste**. Guards
   exist to stop exploits, not to overrule the character:
   - the option must be on the menu and eligible;
   - **anti-exploit floor:** `p_i ≥ 0.02` (low/medium stakes) or `≥ 0.05` (high stakes). Unlikely but
     in-character choices are allowed;
   - **long-shot budget:** at most **2 choices per NPC–player pair per game day** with `p_i < 0.20` that
     favor the player (closes "rephrase until yes");
   - **critical options** — armed or lethal violence (a drawn weapon), war or feud declarations, executions, maiming and
     banishment, oaths of fealty, transfers worth ≥ 1 crown (960f) — also need a deterministic
     `p_i ≥ 0.25`. That check never sees the untrusted player text; a second model call reading the
     same text is **not** an independent check;
   - on failure, the policy chooses instead and the line is regenerated to match (or a template is used).
5. **Execute.** The owning system enacts the option and resolves every consequence: the trade
   system moves goods and coin at the option's price; the escalation ladder advances and the combat
   system resolves the fight; the relationship system applies a capped modifier; the obligation
   system records the promise; the justice system applies the verdict.
6. **Record.** The DP, a hash of its menu, the chosen option and the decider are logged as input
   events, so saves and replays stay deterministic.

**Canonical illustrations** (the owner's examples):

| Moment | The decision (decider) | Menu built by | Resolved by |
|--------|------------------------|---------------|-------------|
| The player insults a hot-tempered fishwife | laugh it off · retort · threaten · shove · attack · walk away (LLM, in her reply) | Escalation ladder ([16 §9](design/16-social-systems.md)) | A brawl or fight in the combat system ([18](design/18-conflict-and-warfare.md)) |
| A bystander sees the quarrel | step in · call others · ignore (fast decider) | 16 §9 | Escalation steps down, or the fight goes ahead |
| The player talks a farmer into buying a plough | accept at the menu price · counter at step k · refuse (LLM) | Trade system ([15 §5](design/15-economy-and-trade.md)) | Trade system transfers goods and coin |
| A long, friendly conversation | warm to the speaker · stay neutral · cool (LLM) | Relationship system ([16 §4](design/16-social-systems.md)) | A capped opinion modifier and Familiarity gain |
| The player asks the smith to take them as an apprentice | accept · accept with conditions · refuse (LLM) | Apprenticeship ([12 §9](design/12-skills-and-professions.md)) | Apprenticeship system |

### 13.2 Where each decider is used

| Situation | Decider |
|-----------|---------|
| Conversations with the player; court sessions, councils, negotiations and war councils the player attends | LLM (in the reply); fast decider for quick sub-choices |
| Combat, evasion, real-time work (farming, crafting, travel) | Hard-coded AI. In combat at most a fast-decider yield/mercy choice (≤ 500 ms); never LLM generation |
| Off-screen life, NPC↔NPC interactions, Interludes, headless runs | Policy only |
| NPC↔NPC exchanges the player overhears | Policy decides; the LLM only renders, so being watched never changes outcomes |

**Parity invariant:** the policy is the calibrated expectation; the LLM is the conversational sample.
On neutral scenarios the LLM's choice frequencies must stay close to the policy's (gap ≤ 10
percentage points per option family), so the part of the world the player talks to is neither kinder
nor harsher than the rest.

### 13.3 What language models may and may not do

**MAY:**
- **Choose** from decision menus, including *initiating*: proposing deals, asking favors, picking or
  defusing fights, warming to or cooling on someone, making or accepting promises, sharing or
  withholding information, calling the guards.
- **Voice** everything: dialogue, barks, overheard talk, petitions, speeches, Chronicles, rumors,
  letters and books — always from sim state.
- **Classify** player language: dialogue acts, tone, topics and entities, commitments, claims, and
  injection attempts.
- **Summarize** conversations into memories (stored alongside facts the sim extracted).

**MUST NOT:**
- Pick anything that isn't on the menu, or set a parameter (a price, an amount, a damage value).
- Resolve consequences: combat results, trade settlement, craft quality, a verdict's effects.
- Create items, coin or facts; reveal what the character couldn't know.
- Run inside real-time loops.
- Be the only check on a critical option.
- Player text is **untrusted data** everywhere it flows.

### 13.4 Words, skill and the willingness to be swayed

- **The old ±15% clamp is now the width of the menu**, not a cap on the decider. How far concession
  and acceptance options reach is
  `Margin = C_sys · s · (0.5 + 0.5·K_skill)`, where `s ∈ [0.05, 1.0]` is the listener's hard-coded
  susceptibility (personality, relationship, mood), `K_skill ∈ [0, 1]` is the speaker's relevant skill
  (Persuasion, blended by the owning doc with Commerce for trade [15] and Leadership for politics [17]), and `C_sys` is **0.15** by default (prices,
  acceptance, political support) and **0.05** for taxes and fines. Verdict menus are built from
  evidence only (words don't widen them), but the judge still chooses among the eligible verdicts.
- **Skill sets the reach; words decide how much of it is granted.** The decider picks a discrete step
  (none · ⅓ · ⅔ · full). In policy fallback the step comes from the classified words signal,
  `L = 0.5·L_words + 0.5·L_skill`, so template mode keeps the same 50/50 weighting.
- **Relationships:** a conversation's rapport option moves Opinion by a small, Familiarity-scaled step
  (±1 to ±4); words can add at most **+10 Opinion per speaker–listener pair per game day**. Acts (an
  insult, a gift, a lie found out) still apply their own deterministic modifiers ([16](design/16-social-systems.md)).
- **Repetition:** asking the same person for the same thing again within a game day multiplies the
  acceptance propensity by `0.5^(n−1)` and raises Anger (deterministic).

### 13.5 Operational rules

Owned in detail by [22-llm-integration](tech/22-llm-integration.md); these are binding everywhere:

1. **The DRE is a sim system:** it builds menus, guards choices and dispatches execution. **The sim
   never waits on a model:** every DP has a deadline (default 4 s real time in conversation, 0.5 s
   for the fast decider in combat), after which the policy decides.
2. **Decision-first output:** the reply's first tokens are the choice, validated before any speech
   is shown. Low/medium stakes (Tier A) stream as soon as the decision passes its guards; high and
   critical stakes (Tier B) hold the line until the speech is verified consistent with the decision
   (one regeneration, then a template). A non-verbal reaction shows within ~0.4 s.
3. **The player's own consequential acts** (insults, threats, accusations, promises, orders given
   with authority) are classified and shown as an **intent echo** with a 1.5 s "unsay" window; orders
   given with authority are always confirmed.
4. **Fast decider:** probabilities over fixed labels only, never numbers. A classification counts
   when `p ≥ 0.45` with a margin ≥ 0.10; if the injection-attempt probability is ≥ 0.3, that turn's
   DPs are decided by the policy.
5. **Input limits:** 280 characters per line by default (max 500); at least 2 s between turns.
6. **Budgets:** ≤ **$0.05 per typical play-hour** and ≤ $0.10 heavy (cloud); default caps **$1 per
   session** and **$10 per month**, with a degradation ladder at 50 / 80 / 100%.
7. **Calibration:** LLM-vs-policy choice-rate gap ≤ 10 points per option family on neutral golden
   scenarios; a refusal suite (NPCs who should say no) passes ≥ 95%. Play telemetry watches for drift.
8. **Chronicles cite their sources:** every sentence maps to event ids and is validated; unsupported
   sentences are dropped.
9. **Content defaults:** violence standard; romance fades to black; profanity medieval-mild; slurs
   never generated; **no romantic or sexual content involving anyone under 16**, ever.
10. **Barks are never generated live in combat**; they come from pre-generated pools.

**Fallback chain** (every touchpoint): cloud LLM → fast decider → local model → policy and
templates. The game must be completable with LLMs disabled ("template mode").

---

## 14. Scales, units & conventions

| Convention | Value |
|------------|-------|
| Distance | meters (Godot units = 1 m); axes **X east, −Z north, +Y up**; single-precision floats |
| Mass | kilograms |
| Game time | integer **game-minutes** since `Y0 Spring 1 00:00` (`long`) for all stored data; the sim clock keeps game-ms internally |
| Probabilities | 0.0–1.0 in code; percentages in docs |
| 0–100 scales | needs, skills, trust, familiarity, fear, attraction, emotions, renown |
| −100…+100 scales | opinion, mood, reputation axes |
| Content IDs | `<kind>.<snake_case>` (`item.iron_axe`, `skill.smithing`, `knowhow.bloomery`, `building.longhouse`); the kind prefix must match the content folder; renames go through `aliases:` |
| Runtime IDs | 64-bit ints; the top 8 bits encode the entity kind; **ids are never reused** (the dead are archived, not deleted) |
| Spatial grids | **EcoCell 512 m** (ecology), **knowledge tile 128 m** (mapping), **field cell 10 × 10 m** (farming) |
| Randomness | All randomness from seeded, keyed RNG streams (per system, or per process for crafts) — never `Random.Shared` or wall-clock time |
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
| World generation, biomes, resources, climate & calendar astronomy (daylight, moon, tides), flora/fauna, place naming, travel, expeditions' arrival logic, the ship's manifest | [10-world-and-setting](design/10-world-and-setting.md) |
| Physical needs, health, injuries, disease, exposure, food spoilage, shelter, Landfall scenario | [11-survival](design/11-survival.md) |
| Skills, XP, aptitude, attributes, the shared `Resolve()` skill check, specializations & perks, know-how, teaching/apprenticeship, professions & job choice | [12-skills-and-professions](design/12-skills-and-professions.md) |
| Crafting model, quality, materials, per-profession minigames for crafts **and land work** (farming incl. crop/soil model, husbandry & livestock, hunting, fishing, foraging, woodcutting, mining), NPC work resolution *via 12's `Resolve()`* | [13-crafting-and-minigames](design/13-crafting-and-minigames.md) |
| Tech tiers detail, buildings, construction, infrastructure, settlement layout | [14-technology-and-buildings](design/14-technology-and-buildings.md) |
| Value, prices, barter, haggling, shops, markets, currency, wages, property, taxes (mechanics) | [15-economy-and-trade](design/15-economy-and-trade.md) |
| Relationships, reputation, memory/belief/rumor propagation, escalation ladder, family, kin-groups, romance, life cycle & mortality, crime detection & witnesses, informal groups, faith, community events (and any folklore/superstition layer, if adopted) | [16-social-systems](design/16-social-systems.md) |
| Governance forms, leadership emergence, legitimacy, political factions, status bands, feudal obligations, laws & justice, court, succession, schism, diplomacy between polities | [17-governance-and-law](design/17-governance-and-law.md) |
| Personal combat, fights & duels, feuds, raids, war causes, muster/conscription, battles, sieges, war's effects | [18-conflict-and-warfare](design/18-conflict-and-warfare.md) |
| Player creation & backgrounds, player paths, UI/UX, dialogue UI, journal, death/lineage UX, difficulty, onboarding | [19-player-experience](design/19-player-experience.md) |
| Engine/sim architecture, ticks, ECS/data layout, save/load, determinism, content pipeline, testing, perf | [20-architecture](tech/20-architecture.md) |
| NPC agent architecture: personality model & trait catalog, needs-driven utility AI, psychological needs, schedules, job execution, emotion & mood, irrationality & the Drama knob, standing orders, sim LOD behavior | [21-npc-ai](tech/21-npc-ai.md) |
| LLM & fast-decider integration: decision points (DRE, menus, guards), dialogue pipeline, prompts, context building, guardrails, providers, budgets, calibration, fallbacks | [22-llm-integration](tech/22-llm-integration.md) |
| Milestones, sequencing, exit criteria | [30-roadmap](production/30-roadmap.md) |
| Producing 3D models, animation, VFX, UI art and audio: pipelines (Blender, audio), budgets, asset catalog, manifest & licensing, art/audio schedule | [32-art-and-audio-production](production/32-art-and-audio-production.md) |
| Live progress: current milestone, work items, owner approvals, blockers, session log | [33-progress](production/33-progress.md) |
| Ready-to-paste `/goal` conditions for long sessions | [34-session-goals](production/34-session-goals.md) |
| Risks & consolidated open questions | [31-risks-and-open-questions](production/31-risks-and-open-questions.md) |
| Terminology | [99-glossary](99-glossary.md) |

---

## 17. Change log

| Date | Change |
|------|--------|
| 2026-10-03 | v0.1 — initial canon derived from the original vision document. |
| 2026-10-03 | v0.2 — folded in accepted proposals from every subsystem doc: .NET 10 migration path, 100 ms sim step, data layout, determinism scope, embodiment boundary, terrain choice; 8,192 m region, deposits, ship-borne livestock and horses, salvage quantities, Charter details, arrival windows; day-of-year index, calendar days (quarter/court/council/market), harvest window, campaign season, focus time (12:1), time-scale policy; LOD hysteresis, LOD0-B battle tier, near region, edge cap; local model sizing (8B default); skill tree, Rust, know-how levels, trait catalog size, orientation gate, Drama knob, mood bands, emotion half-lives; status bands, legitimacy, governance forms, faith names; `Resolve()`, quality grades; health model, food unit, movement speeds; economic constants; 10 backgrounds, point-buy, standing-orders parity exception; LLM operational rules; id and spatial conventions; ownership-map clarifications. |
| 2026-10-03 | v0.3 — owner direction: **language decides, systems resolve.** LLMs may make real choices for characters through decision points (menus built by deterministic systems, guards, deterministic execution). Rewrote tenet 1 and §13; the ±15% clamp became menu width; fast-decider role defined with a working OpenRouter provider (Jev pending direct access); dev key is `OPENROUTER_KEY` in `.env`. |
| 2026-10-04 | v0.3.1 — .NET version: projects target `net10.0` (ADR-0010 step 3, M1-25 spike: Godot 4.7.2 runs it; tests, determinism hashes and the Godot checks unchanged). |
