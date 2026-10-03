# FeudalSim — Documentation Index

This folder is the plan FeudalSim is built from. Start with the vision and canon, then read
whichever design or technical documents cover the system you're working on.

## How the docs fit together

- **[01-canon](01-canon.md) is binding.** It holds the decisions, numbers, names, scales and milestone
  IDs every other document uses, plus the **ownership map** (§16): each concept is defined in exactly
  one document and referenced everywhere else.
- **Design docs** (`design/`) say what the game does and how each system works mechanically.
- **Technical docs** (`tech/`) say how it's built.
- **Production docs** (`production/`) say in what order, and what could go wrong.
- **ADRs** (`adr/`) record architectural decisions and the conditions for revisiting them.
- Every design/tech doc ends with **Open questions** and **Proposed canon additions**. Accepted
  proposals are folded into canon; the rest are tracked in
  [31-risks-and-open-questions](production/31-risks-and-open-questions.md).

## Reading order

| If you are… | Read |
|-------------|------|
| New to the project | 00 → 01 → 02 → 30 |
| Implementing a system | 01 (canon) → that system's owning doc → [20-architecture](tech/20-architecture.md) |
| Working on NPCs or dialogue | 01 → [21-npc-ai](tech/21-npc-ai.md) → [22-llm-integration](tech/22-llm-integration.md) → [16-social-systems](design/16-social-systems.md) |
| Planning work | [30-roadmap](production/30-roadmap.md) → [31-risks-and-open-questions](production/31-risks-and-open-questions.md) → [33-progress](production/33-progress.md) |
| Making art or audio | [02 §9–10](02-game-overview.md#9-art-direction) → [32-art-and-audio-production](production/32-art-and-audio-production.md) |
| Running a development session | `/advance-plan` (skill in `.claude/skills/`) or a goal from [34-session-goals](production/34-session-goals.md) |

## Foundation

| Doc | What it covers |
|-----|----------------|
| [00 — Original vision](00-vision-original.md) | The owner's brainstorm, kept verbatim. Every idea in it is honored somewhere in the plan. |
| [01 — Canon](01-canon.md) | Pillars, tenets, core decisions, setting, time, eras, scale & LOD, tech tiers, the person model, economy units, the player, the LLM boundary, conventions, milestones, ownership map. |
| [02 — Game overview](02-game-overview.md) | Player fantasy, core loops, the arc of a playthrough, target emergent stories, tone, art & audio direction, comparables, non-goals, success criteria. |

## Design

| Doc | What it covers |
|-----|----------------|
| [10 — World & setting](design/10-world-and-setting.md) | World generation, biomes, resources & deposits, climate/weather/daylight, flora & fauna ecology, landmarks, exploration & mapping, place naming, travel, lore, expeditions & resupply ships, the Silence, the ship's manifest. |
| [11 — Survival](design/11-survival.md) | Physical needs, health & injuries, disease & contagion, exposure, food & spoilage, water, sleep, encumbrance, incapacitation & death, the Landfall scenario, the first winter. |
| [12 — Skills & professions](design/12-skills-and-professions.md) | Attributes, the 28 skills, XP & aptitude, the shared `Resolve()` skill check, specializations & perks, know-how, teaching & apprenticeship, 35 professions, job choice & labor allocation. |
| [13 — Crafting, land work & minigames](design/13-crafting-and-minigames.md) | The process model, quality & materials, a minigame spec for every craft and land-work profession, the crop & soil model, livestock, tedium control, NPC parity. |
| [14 — Technology & buildings](design/14-technology-and-buildings.md) | The capability graph T0–T4, the buildings catalog, construction, decay & fire, settlement layout, infrastructure, institutions, settlement metrics, era criteria. |
| [15 — Economy & trade](design/15-economy-and-trade.md) | The value model, a starter price table, price formation, barter & haggling, shopkeeping, markets & ships, currency & credit, wages, property, taxes & the lord's finances, war economics. |
| [16 — Social systems](design/16-social-systems.md) | Relationships & opinion modifiers, social interactions, memory, beliefs & rumors, reputation, provocation & escalation, crime detection, households, romance & marriage, life cycle & grief, groups, faith, community events. |
| [17 — Governance & law](design/17-governance-and-law.md) | Governance forms, legitimacy & authority, political factions, the player as leader, paths to power, councils, the feudal structure, laws as data, crimes & punishments, justice, holding court, succession, rebellion & exodus, diplomacy. |
| [18 — Conflict & warfare](design/18-conflict-and-warfare.md) | Personal combat, NPC combat AI & animals, brawls & duels, feuds, raids, war causes, muster & conscription, campaigns, battles, sieges, war's effects & morale. |
| [19 — Player experience](design/19-player-experience.md) | Character creation & backgrounds, player paths, goals without quests, all major UI (HUD, bench, map, dialogue, trade, journal, ledger, court, command), onboarding, death & Lineage, Interludes, settings & accessibility. |

## Technical

| Doc | What it covers |
|-----|----------------|
| [20 — Architecture](tech/20-architecture.md) | The sim/client split, the embodiment boundary, repo layout, clocks & ticks, data layout, scheduling, determinism, commands/events & saves, content pipeline, AI gateway interfaces, Godot client, headless runner, testing, tooling, CI, conventions, performance budgets, **the M0 checklist**. |
| [21 — NPC AI](tech/21-npc-ai.md) | Agent architecture, personality & the trait catalog, needs, emotions & mood, utility AI, irrationality, schedules, job execution, ambitions, groups, perception, LOD behavior, the player's standing orders, debugging, performance. |
| [22 — LLM integration](tech/22-llm-integration.md) | Providers & configuration (LLM and fast decider: small OpenRouter model now, Jev or Laya later), the dialogue turn pipeline with **decision points**, the fast-decider question catalog, the Dialogue Rules Engine (menus, guards, execution), prompts & context, worked examples, other generation tasks, guardrails, replay, cost & latency, template mode, local-first, evals & calibration, **the M1 de-risking plan**. |

## Production

| Doc | What it covers |
|-----|----------------|
| [30 — Roadmap](production/30-roadmap.md) | Milestones M0–M8, dependencies, risk-retiring spikes, exit criteria, cross-cutting tracks, playtests, definition of done, not-in-v1. |
| [31 — Risks & open questions](production/31-risks-and-open-questions.md) | The risk register and the consolidated list of open questions awaiting the owner's decisions. |
| [32 — Art & audio production](production/32-art-and-audio-production.md) | How every 3D model, animation, VFX, UI graphic and sound gets made: tools (headless Blender, verified), repo layout and the asset manifest, budgets, the 3D and audio pipelines, what Claude can generate vs. what needs people, asset and audio catalogs, licensing, validation, schedule, staffing. |
| [33 — Progress tracker](production/33-progress.md) | Live state: current milestone, work items with evidence, owner approvals, blockers, discovered work, session log. Read and updated by `/advance-plan`. |
| [34 — Session goals](production/34-session-goals.md) | Ready-to-paste `/goal` conditions for long-running Claude Code sessions, and how they pair with the `/advance-plan` skill. |

## Architecture decision records

| ADR | Decision |
|-----|----------|
| [0001](adr/0001-engine-godot-dotnet.md) | Godot 4 (.NET / C#) for presentation |
| [0002](adr/0002-headless-deterministic-sim-core.md) | A headless, deterministic simulation core |
| [0003](adr/0003-language-decides-systems-resolve.md) | Language decides, systems resolve: the LLM boundary |
| [0004](adr/0004-single-player-scope.md) | Single-player only for v1 |
| [0005](adr/0005-custom-domain-tables.md) | Custom id-ordered domain tables, not an ECS library |
| [0006](adr/0006-time-model.md) | Time model: 100 ms step, two clocks, eleven day lengths |
| [0007](adr/0007-embodiment-boundary.md) | The embodiment boundary: the sim owns intent, Godot moves LOD0 bodies |
| [0008](adr/0008-save-format.md) | Saves: column-tolerant MessagePack + LZ4 snapshots with an input log |
| [0009](adr/0009-terrain.md) | Terrain3D with a chunked-mesh fallback (proposed; pending spike) |
| [0010](adr/0010-dotnet-version.md) | .NET: net8.0 now, .NET 10 SDK next, net10 runtime when Godot allows |
| [template](adr/0000-template.md) | Copy this for new ADRs. |

## Vision traceability

Every idea in the [original vision](00-vision-original.md) and where the plan implements it.

| Vision idea | Where it lives |
|-------------|----------------|
| Medieval fantasy; the founding of a society and its eventual conflicts | [01 §1–2, §7](01-canon.md), [02 §4](02-game-overview.md) |
| Settlers arrive on an uninhabited land that is large, with varied terrains and resources | [01 §5](01-canon.md), [10 §3–5](design/10-world-and-setting.md) |
| A survival first stage (tools, resources, shelter, food), like Minecraft/Rust/Valheim | [11](design/11-survival.md), [13](design/13-crafting-and-minigames.md), [14 §2–4](design/14-technology-and-buildings.md) |
| You are one member of a town of AIs, all working to survive | [01 §2 P1](01-canon.md), [21](tech/21-npc-ai.md), [12 §11](design/12-skills-and-professions.md) |
| Mostly hard-coded AI; LLM conversations (Qwen on OpenRouter first, small local later); Jev (or an alternative such as Laya) only where it makes sense | [01 §4, §13](01-canon.md), [ADR-0003](adr/0003-language-decides-systems-resolve.md), [22 §3, §16](tech/22-llm-integration.md) |
| Real-time actions (combat, evasion, farming) are hard code, not LLM | [21 §2, §7](tech/21-npc-ai.md), [18 §2–3](design/18-conflict-and-warfare.md) |
| An identical skill tree for players and NPCs; people gravitate toward jobs | [01 §10.2](01-canon.md), [12 §4–7, §11](design/12-skills-and-professions.md) |
| Technology tops out around the steel age | [01 §5.5, §9](01-canon.md), [14 §2](design/14-technology-and-buildings.md) |
| From a small co-op to towns of hundreds who disagree and conflict | [01 §7–8](01-canon.md), [16](design/16-social-systems.md), [17](design/17-governance-and-law.md) |
| People don't always act rationally | [01 §3 tenet 3](01-canon.md), [21 §8](tech/21-npc-ai.md) |
| Everyone feels like a full person; relationships; people react as you'd expect | [16 §4–8](design/16-social-systems.md), [22 §4–8](tech/22-llm-integration.md) |
| A would-be leader must get people on board with their vision | [17 §6–7](design/17-governance-and-law.md) |
| Insult someone → be ready for a fight | [16 §9](design/16-social-systems.md), [18 §4](design/18-conflict-and-warfare.md), [22 §8.2](tech/22-llm-integration.md) |
| Do something absurd → rumors spread and people react | [16 §7–8](design/16-social-systems.md) |
| Steal → people grow wary, distrust you, authorities may come for you | [16 §10](design/16-social-systems.md), [17 §12](design/17-governance-and-law.md) |
| Be anything: hermit, feudal farmer owing taxes, lord (benevolent or not), knight, shopkeep, craftsman | [19 §3](design/19-player-experience.md), [02 §6](02-game-overview.md) |
| Complex crafting as minigames (carve, treat and string a bow; swords; tools) | [13](design/13-crafting-and-minigames.md) |
| Farming: tilling, planting, pests, diseased plants, watering. Cooking means making the food | [13 (crop & soil model, cooking)](design/13-crafting-and-minigames.md) |
| Lords and kings hold court and manage finances; shopkeeps set prices | [17 §13–14](design/17-governance-and-law.md), [15 §6, §11](design/15-economy-and-trade.md) |
| Each profession feels like its own game | [13](design/13-crafting-and-minigames.md), [19 §3](design/19-player-experience.md) |
| The LLM is a facade over real mechanics, yet has real-world impact | [01 §13](01-canon.md), [22 §6](tech/22-llm-integration.md) |
| *(Owner direction, 2026-10-03)* LLMs can make decisions — build relationships, start or prevent fights, barter — but the systems those decisions run on are deterministic | [01 §13](01-canon.md#13-the-llm-boundary-language-decides-systems-resolve), [ADR-0003](adr/0003-language-decides-systems-resolve.md), [22 §4, §6, §8](tech/22-llm-integration.md), [16 §9](design/16-social-systems.md), [15 §5](design/15-economy-and-trade.md) |
| Barter has hard-coded values; talk can sway the price, but willingness to be swayed is hard-coded | [15 §2, §5](design/15-economy-and-trade.md), [22 §6.3](tech/22-llm-integration.md) |
| Wars start for petty or necessary reasons | [18 §7](design/18-conflict-and-warfare.md) |
| Leaders must raise fighters from their own people, which hurts the economy and morale at home | [18 §8, §12](design/18-conflict-and-warfare.md), [15 §12](design/15-economy-and-trade.md) |
| The player may be a conscript, a military leader or a small-unit commander, depending on standing | [18 §8.3–8.6, §10](design/18-conflict-and-warfare.md) |

## Glossary

[99 — Glossary](99-glossary.md): terms used across the plan.
