# 99 — Glossary

> **Status:** Draft v0.3 · **Owner doc for:** terminology · **Depends on:** all docs

Short definitions of terms used across the plan. The **owning document** (in brackets) holds the
full definition; when this glossary and an owning doc disagree, the owning doc wins.

---

## A–C

- **Ambition** — A long-term goal. For NPCs it drives plans (become master smith, marry X, take
  revenge). For the player it is an optional, mechanically inert journal goal. [21, 19]
- **Aptitude** — A hidden per-person, per-skill XP multiplier (0.5–1.5, mean 1.0). [01 §10.2, 12]
- **Aware / Familiar / Practiced / Mastered** — Know-how proficiency levels. Every homeland-born
  adult is *Aware* of homeland know-how through T4; manuals alone reach *Familiar*. [12]
- **Bark** — A short ambient NPC line (greeting, complaint, battle cry), drawn from pre-generated,
  cached pools. Never generated live in combat. [22]
- **Battle mode / LOD0-B** — The dedicated simulation tier for battles: up to 150 combatants (stretch
  300). The 48 nearest the player get full LOD0 fidelity; the rest use sim-side kinematics with the
  same damage math. The world clock runs on focus time while it's active. [18, 20]
- **Base propensity (`p_i`)** — The probability the deterministic policy would choose a given
  menu option, computed from personality, needs, emotions, relationship, beliefs and the speaker's
  skill. Guards use it; LLMs see it only as qualitative "inclinations". [01 §13, 21]
- **Belief** — A claim a person holds with a confidence (0–1) and a source chain; may be false.
  Thresholds: forget < 0.1, hold ≥ 0.5, act ≥ 0.6, certain ≥ 0.85. [16]
- **Bench camera** — The close-up camera used for crafting minigames. [01 §4, 19]
- **Breaking point** — A crisis state an NPC can enter when mood stays below −60 (e.g. flight,
  binge, outburst). Disabled for the player's standing orders. [21]
- **Canon** — [01-canon](01-canon.md): binding decisions and numbers. Wins over every other doc.
- **Capability** — A settlement-level technology (e.g. bloomery smelting) with a lifecycle: KnownOf ·
  Attempted · Established · Sustained · Lapsed · Lost. Tech tier is derived from Established
  capabilities. [14]
- **Charter / Charter Chest** — The document granting the drowned lord's expedition rights to the
  land, kept with 3 crowns and a seal ring. The Charter is a physical item that can be stolen,
  forged or burned, and its wording is deliberately ambiguous. [01 §5.3, 10, 15, 17]
- **Chronicle** — A narrative summary of what happened during an Interlude (or at a saga's end),
  written by the LLM from event-log facts. Every sentence must cite events; unsupported sentences
  are dropped. [01 §6.1, 22]
- **Claim** — The structured content of a belief or rumor (who did what, where, when). The sim
  mutates claims by fixed rules; the LLM only rewords them. [16]
- **Common Store** — The communal stockpile of salvage and early production. Legally it is the late
  lord's estate (so the Heir's); by custom it belongs to everyone. A deliberate Day-1 property
  conflict. [15]
- **CommunityTension** — A 0–100 per-community metric that drives scapegoating, false accusations and
  unrest. [16, 17]
- **Commutation** — A levy buy-out: 48f per seasonal service paid instead of reporting for muster.
  Not the same as scutage. [18]
- **Competence** — A per-profession reputation axis. True competence is `2 × (skill − 35)`, clamped
  to −100…100; others perceive it through beliefs. [12, 16]
- **Corvée** — Labor owed to a lord: by default 2 labor-days per season plus 1 Autumn boon day per
  serf household. [15, 17]
- **Crown reeve** — A royal official who arrives on the next ship if Charter dues are missed twice
  (before the Silence). [10]

## D–I

- **Day-of-year (`d`)** — 1…32, where Spring 1 = 1. Used by calendar formulas (daylight, crop
  windows). [01 §6, 10, 13]
- **Decider** — Whatever picks an option at a decision point: the **LLM** (inside its reply), the
  **fast decider**, or the deterministic **policy**. [01 §13]
- **Decision-first output** — The LLM emits its chosen option in the first tokens, which are
  validated before any speech is shown; the speech is conditioned on the choice. [01 §13.5, 22]
- **Decision point (DP)** — A moment when a character must choose in a language-driven situation.
  The owning system builds a **menu**, a decider picks, guards check, and the owning system executes
  and resolves the choice. "Language decides, systems resolve." [01 §13]
- **Demeanor cues** — Visible signs of an NPC's attitude (posture, expression, wording) shown instead
  of hidden numbers. [19]
- **Dialogue Rules Engine (DRE)** — The sim system that opens decision points, builds their menus
  through the owning systems, guards the chosen option, and dispatches its execution. It never
  waits on a model. [22]
- **Drama setting (K_irr)** — The master irrationality knob (0–2, default 1.0) scaling every
  irrationality mechanism. [21]
- **EcoCell** — A 512 m grid cell used for ecology (fauna populations, forest stands, fish stocks). [10]
- **Embodiment boundary** — The rule that the sim owns intent, state and timing, while Godot moves
  LOD0 bodies and reports the physical results back as logged commands. [20]
- **Epithet** — A name element a community assigns ("the Bold", "Goat-thief"). [16]
- **Era** — A descriptive per-settlement milestone (0 Landfall, 1 Hamlet, 2 Village, 3 Town &
  Lordship, 4 Realms). Systems unlock through institutions, not era labels. [01 §7, 14]
- **Escalation ladder** — Calm · Slight · Argument · Threat · Shove · Brawl · Armed fight · Lethal. A
  feud is a kin-group state that may follow. [16, 18]
- **Event log** — The record of what happened. Saves hold a snapshot plus an **input log** (every
  command, including recorded LLM/Jev results) and a **history log** (events used by memory,
  Chronicles and tools). [20]
- **Exodus** — A faction leaving to found a splinter settlement once grievance, cohesion and
  leadership thresholds are met (minimum 6 adults and 16 ration-days of food each). [01 §5.4, 17]
- **Fast decider** — A sub-second model that classifies player language and picks among fixed
  options, returning probabilities. Now a small OpenRouter model read through log-probabilities;
  later Jev or a fine-tuned local Laya. [01 §4.1]
- **Farthing (f)** — The internal unit of value. 4f = 1 penny (d); 12d = 1 shilling (s); 20s = 1
  crown. One unskilled labor-day ≈ 8f. [01 §11, 15]
- **Focus time** — The world clock runs at 12:1 (¼ of normal) while a conversation, court session or
  battle is open. [01 §6]
- **Hallmote** — The manor court held on court day (day 1 of each season, the quarter day). [17]
- **Hearthday** — Day 8 of each season: the customary rest day, with the morning **Hearth Market**
  and the evening council. [01 §6, 15, 17]
- **Institution** — A building plus human and legal conditions that unlock a system (market, mint,
  court, keep…). [14]
- **Intent echo / unsay** — Before a consequential act, the dialogue UI shows how the game read the
  player's words and allows 1.5 s to take them back. [19]
- **Interlude** — A time skip of 1–8 seasons during which the sim runs at LOD3, the player follows
  standing orders, and **interrupts** can halt the skip. [01 §6.1]

## J–P

- **Jev** — TypeSafe AI's "System One" decision model: calibrated probabilities over fixed options,
  no text generation. One possible fast-decider provider, pending direct API access (OpenRouter lists
  only the *Jev Router*, which routes to other models). Never produces numbers. [01 §4.1, 22]
- **Kin-group** — A first-class grouping (family name, house or clan) used by succession and feuds. [16]
- **Keepers** — The working name for Ember Faith clergy. [16]
- **Know-how** — A discrete technique held by individuals ("Bloomery smelting", "Quench hardening");
  spreads by teaching, apprenticeship and manuals; lost if every holder dies untaught. [01 §10.3, 12]
- **Landfall** — Era 0 and the opening scenario: the *Wending Star*'s landing on `Y0 Spring 1`. [01, 11]
- **Laya** — An open-source (Apache-2.0) Jev-style decision model from Convai Innovations
  (ModernBERT-based, 322–421M parameters) that runs locally and serves the same API shape as Jev. The
  candidate local fast decider once fine-tuned on our data. [01 §4.1, 22]
- **Leads** — The sim's opportunity surface for the player: vacancies, unmet demand, unclaimed land,
  learned through rumor, sight, reading or being asked. Logged in the journal. [19]
- **Legitimacy** — A ruler's standing, built from five sources: Tradition/Charter, Competence,
  Popularity, Force and Faith. It governs **authority** — whether a given order is obeyed. [17]
- **Lineage** — The default death rule: continue as an heir or close companion. [01 §12, 19]
- **LOD0–LOD3** — Simulation levels of detail: Embodied (≤ 80 m, cap 48), Local (≤ 400 m), Abstract
  (hourly), Statistical (daily; Interludes and far settlements). [01 §8.2, 21]
- **Manifest (ship's manifest)** — The generated roster of the first 24 settlers, with professions,
  families, pre-existing relationships, debts, grudges and secrets. [10]
- **Long-shot budget** — At most 2 player-favoring choices per NPC–player pair per game day for
  options with base propensity < 0.20; closes "rephrase until yes". [01 §13.1]
- **Masterwork attempt** — A deliberate attempt at Q ≥ 90. It raises difficulty; without one, quality
  caps at 89. [13]
- **Menu (option menu)** — The options a decision point offers, each with fixed parameters
  (prices, amounts, targets), eligibility, base propensity and stakes. Its width is the hard-coded
  willingness to be swayed: `Margin = C_sys · s · (0.5 + 0.5·K_skill)`. [01 §13]
- **Monetization index (μ)** — The 16-day average share of a settlement's trade settled in coin. [15]
- **Mood** — A −100…+100 composite of needs and emotions. Bands: Elated, Content, Neutral, Low,
  Breaking. [01 §10.5, 21]
- **Morale (three scales)** — Combat Morale (per fight), Campaign Morale (per soldier) and Home-front
  Morale (per settlement), each 0–100. [18]
- **Obligation** — A tracked commitment (a promise, debt, rent or service) with a deadline; breaking
  it hurts trust. Promises made in conversation become obligations. [16, 22]
- **Opinion modifier** — A decaying term in one person's Opinion of another (e.g. "insulted me"). One
  slot per type per pair; serious harms leave a permanent floor. [16]
- **Parity** — The tenet that the player and NPCs obey the same rules. [01 §3]
- **Performance score (PS)** — The 0–100 result of a `Resolve()` check (`50 + 1.25R`), mapped to
  quality or yield by the owning system. [12, 13]
- **Persona card** — The compact, sim-derived description of an NPC that goes into every prompt. [22]
- **Policy** — The deterministic decider: seeded sampling from base propensities. It decides
  everything off-screen, in Interludes and headless runs, in template mode, and whenever a model
  times out or a guard rejects its choice. [01 §13, 21]
- **Priority classes P0–P4** — Tiers of NPC action selection, from survival emergencies (P0) to idle
  (P4). [21]
- **Process** — A multi-stage piece of work (tanning, seasoning, firing, fermenting) that can span
  game days and be interrupted and resumed. Every stage costs at least its nominal game time. [13]
- **Puppet** — An LOD1 entity rendered with simplified animation and moved by sim-side kinematics. [20]

## Q–Z

- **Quality (Q) & grades** — Item quality is an integer 0–100 (50 = common). Grades: Crude 0–19 ·
  Poor 20–39 · Common 40–59 · Fine 60–74 · Superior 75–89 · Masterwork 90–100. **Ruined** is an
  outcome, not a grade. [13]
- **Quick Work / Batch** — Mastery shortcuts that skip a minigame once a recipe is mastered, always
  at or below a skilled hand's result. [13]
- **Renown** — How widely a person is known in a community (0–100). [01 §10.6, 16]
- **Reputation axes** — Honesty, Generosity, Courage, Peaceableness, Lawfulness, Piety and
  per-profession Competence. Each is held per observer, derived from beliefs; communities have no
  global number. [01 §10.6, 16]
- **`Resolve()`** — The shared skill-check function every system uses:
  `R = E − D + Logistic(0, 8)`. NPCs roll the noise; the player's minigame replaces it within ±24.
  [12]
- **Resupply ship** — An immigrant and trade ship arriving at Summer 1 (+0–3 days) in Years 1…N,
  until the Silence. [01 §5.4, 10, 15]
- **Risk tiers (A/B)** — Tier A (low/medium stakes) streams once the decision header passes its
  guards; Tier B (high/critical) holds the line until the speech is verified consistent with the
  decision, regenerates once, then falls back to a template. [01 §13.5, 22]
- **Rumor** — A belief in transit between people; spreads, distorts and decays by hard-coded rules. [16]
- **Rust** — A temporary penalty on unused skills (after 16 idle days, up to 15% of the level), cleared
  by practice. Skill levels themselves never fall. [12]
- **Salience** — How strongly a memory persists; decays over time unless reinforced. [16]
- **Scutage** — Money paid by a knight instead of field service: 240f per knight's fee per year. [15, 17]
- **Silence, the** — The point (after year N, rolled 3–6) when homeland ships stop coming. [01 §5.4]
- **Specialization / perk / capstone** — The skill-tree layer, the same for everyone: a
  specialization at 40, perks at 60/70/90, a capstone at 80. [12]
- **Standing orders** — The routine the player character follows during Interludes, executed by the
  NPC AI with irrationality switched off. [01 §6.1, 21]
- **Status band** — Ordinal social standing 0–4 (Unfree/landless, Commoner, Master/merchant,
  Notable, Ruler), defined in every era. [17]
- **Susceptibility (s)** — A listener's hard-coded openness to persuasion (0.05–1.0), from
  personality, relationship and state. Sets how wide their decision menus reach. [01 §13.4, 22]
- **Task board** — The Era-0 communal job list that settlers take work from. [21, 12]
- **Template mode** — Play with no LLM: template/grammar dialogue, heuristic classification, and the
  policy making every decision. The game must stay completable in it. [01 §13, 22]
- **Tech tier (T0–T4)** — Salvage & Stone · Clay & Copper · Bronze · Iron · Steel. [01 §9, 14]
- **Wending Star** — The player's expedition ship *(working name)*. [01 §5.3]
- **Wergild** — A blood-money table in farthings by status, also used for ransom. [18]
- **War Score / War Exhaustion** — Per-side war progress (−100…+100) and weariness (0–100). [18]
- **"A year and a day"** — 33 game days (serf freedom in a chartered town; Brannoch handfasting). [17, 16]
