# 31 — Risks & Open Questions

> **Status:** Draft v0.1 · **Owner doc for:** the risk register and the consolidated list of open questions · **Depends on:** all docs

Two living lists. The **risk register** tracks what could sink or badly damage the project and what
we're doing about it. The **open questions** consolidate the per-document "Open questions"
sections into one place so the owner can make decisions in batches. Each question links back to
the doc that owns it; answer it there and strike it here.

---

## 1. Risk register

Likelihood (L) and Impact (I) on a 1–5 scale. **Score = L × I.** Review at every milestone end.

| # | Risk | L | I | Score | Mitigation | Early warning sign | Owner doc |
|---|------|---|---|-------|------------|--------------------|-----------|
| R1 | **Scope overwhelms the team.** The design spans survival, crafting, economy, social sim, politics and war. | 5 | 5 | **25** | Era-ordered milestones; strict out-of-scope lists; MVP vs later-depth tags in every doc; M1 proves the core before breadth; cut lines decided per milestone. | Milestone overruns > 50%; "while we're here" features. | [30-roadmap](30-roadmap.md) |
| R2 | **NPC conversations don't feel like people** (generic, sycophantic, forgetful, inconsistent). | 3 | 5 | **15** | Rich persona cards from real sim state; decision-first prompting; verification pass; golden-conversation evals; human rubric playtests in M1. | Testers describe NPCs as "chatbots"; contradictions slip past verification. | [22-llm-integration](../tech/22-llm-integration.md) |
| R3 | **LLM latency breaks immersion.** | 3 | 4 | 12 | Streaming; short replies; latency-masking UX (gestures, "thinking"); pre-generated barks; prompt caching; smaller model for low-stakes lines. | p50 TTFT > 1.5 s in M1 spike. | [22-llm-integration](../tech/22-llm-integration.md) |
| R4 | **LLM cost per play-hour too high** for players or for us. | 3 | 4 | 12 | Token budgets per turn; caching; local-first by M7; template mode; spend caps. | M1 measured cost > target. | [22-llm-integration](../tech/22-llm-integration.md) |
| R5 | **Prompt injection / exploits** — players talk NPCs into giving away the store. | 4 | 3 | 12 | Hard systems decide; clamps; untrusted-text handling; injection test suite in CI; never let a single classification trigger a high-stakes outcome. | Any injection-suite failure. | [ADR-0003](../adr/0003-hard-systems-soft-voice.md) |
| R6 | **Jev vendor risk.** Jev is new (2026), cloud-only, possibly waitlisted; its API, ids or pricing may change; details so far come from third-party write-ups. | 3 | 3 | 9 | Provider interface with drop-in fallbacks (local LLM structured output → heuristics); verify API against TypeSafe docs at M1; never hard-depend. | API access delayed; accuracy below spike threshold. | [22-llm-integration](../tech/22-llm-integration.md) |
| R7 | **Emergent behavior is boring or chaotic** — no conflict, or constant chaos. | 4 | 4 | **16** | Headless runner with drama metrics and target ranges; tuning knobs per system; "storyteller-free" principle but with pressure systems (scarcity, status, rumor). | Drama metrics out of range across seeds. | [16-social-systems](../design/16-social-systems.md), [21-npc-ai](../tech/21-npc-ai.md) |
| R8 | **Performance at scale** (1,500 simulated; ~200 rendered; battles of 150–300). | 3 | 4 | 12 | LOD tiers; data-oriented sim; spikes S1/S6 in M1; perf budgets enforced in CI. | Spike misses targets. | [20-architecture](../tech/20-architecture.md) |
| R9 | **Godot 3D limits** (terrain scale, crowds, tooling). | 2 | 4 | 8 | Engine-agnostic sim (ADR-0002) keeps a port possible; spikes S1/S4 early. | S1 or S4 fail. | [ADR-0001](../adr/0001-engine-godot-dotnet.md) |
| R10 | **Minigame fatigue** — crafts fun once, tedious by the 30th time. | 4 | 3 | 12 | Mastery shortcuts and batch crafting; delegation to apprentices; variety in materials/conditions; playtests at rep 1 and rep 20. | Testers skip minigames at the first opportunity. | [13-crafting-and-minigames](../design/13-crafting-and-minigames.md) |
| R11 | **The player feels irrelevant** in a society that "lives without you". | 3 | 4 | 12 | NPCs initiate interactions with the player; obligations/opportunities surface; words and deeds have visible effects; journal of consequences. | Playtesters can't name anything they changed. | [19-player-experience](../design/19-player-experience.md) |
| R12 | **Determinism drift** (replays and saves diverge). | 3 | 3 | 9 | Seeded per-system RNG; ordering rules; LLM outputs as recorded inputs; golden-seed regression tests in CI. | Golden-seed test failures. | [20-architecture](../tech/20-architecture.md) |
| R13 | **LOD promotion/demotion bugs** (NPCs teleport, duplicate items, impossible states). | 4 | 3 | 12 | Reconciliation rules per system; invariant checks in debug builds; soak tests that thrash LOD tiers. | Invariant violations in soak tests. | [21-npc-ai](../tech/21-npc-ai.md) |
| R14 | **Save size and load time** balloon with memories, beliefs and event logs. | 3 | 3 | 9 | Memory compaction; event-log snapshots and truncation; save-size budget tests. | Saves > budget at Y10 with 1,500 people. | [20-architecture](../tech/20-architecture.md) |
| R15 | **Chronicles hallucinate** events that didn't happen. | 3 | 3 | 9 | Facts-only input from event log; claim-to-event validation; regenerate or fall back to template. | Validator failure rate > 2%. | [22-llm-integration](../tech/22-llm-integration.md) |
| R16 | **Offensive or off-tone generated content.** | 3 | 4 | 12 | Content policy in prompts; output filtering; content settings; red-team suite; never pass raw player text as instructions. | Red-team failures; tester reports. | [22-llm-integration](../tech/22-llm-integration.md) |
| R17 | **Platform policy on AI-generated content.** Storefronts (e.g. Steam) require disclosure of live-generated AI content and the guardrails used. | 2 | 4 | 8 | Document guardrails from day one; content filters; reporting mechanism in-game; review store policies at M7. | Policy changes. | [22-llm-integration](../tech/22-llm-integration.md) |
| R18 | **Model churn** — hosted model ids are deprecated or change behavior. | 4 | 2 | 8 | Model ids in config, not code; eval suite run on any model change; keep 2 known-good models configured. | Provider deprecation notices. | [22-llm-integration](../tech/22-llm-integration.md) |
| R19 | **Local LLM hardware bar** excludes many players. | 3 | 3 | 9 | Cloud mode and template mode; small-model option; VRAM budgeting. | S5 fails on recommended spec. | [22-llm-integration](../tech/22-llm-integration.md) |
| R20 | **Bus factor / burnout** on a 1–2 person team over ~2 years. | 3 | 5 | **15** | Docs-first culture (this plan); CLAUDE.md and ADRs keep context; sustainable milestone sizing. | Repeated milestone slips; docs drifting from code. | [30-roadmap](30-roadmap.md) |

---

## 2. Decisions needed from the owner

These are the questions whose answers most change what gets built. Each has a **recommended
default**, which the plan already assumes. Confirm or override; record the answer in the owning doc
(and in canon if it's cross-cutting).

### 2.1 Foundational (confirm before M0)

| # | Question | Recommended default (assumed by the plan) | Where |
|---|----------|-------------------------------------------|-------|
| D1 | **Engine** | Godot 4 .NET (C#) client + pure C# sim core. Unity is the fallback; the engine-agnostic sim keeps a port possible. | [ADR-0001](../adr/0001-engine-godot-dotnet.md) |
| D2 | **Perspective & art** | 3D third-person, stylized low-poly | [01 §4](../01-canon.md#4-core-product--technology-decisions), [02 §9](../02-game-overview.md#9-art-direction) |
| D3 | **Single-player only** | Yes for v1 | [ADR-0004](../adr/0004-single-player-scope.md) |
| D4 | **Time scale** | 30-min days, 8-day seasons, 32-day years, Interludes for generational time | [01 §6](../01-canon.md#6-time-canon) |
| D5 | **Where rival societies come from** | Several expeditions + resupply ships until "the Silence" + schisms/exodus + outlaw camps | [01 §5.4](../01-canon.md#54-how-multiple-societies-come-to-exist-so-war-is-possible) |
| D6 | **Player mortality** | Players age and die; default **Lineage** (continue as heir); Forgiving and Ironman modes | [01 §12](../01-canon.md#12-the-player) |
| D7 | **.NET version** | `net8.0` for M0 (support ends 2026-11-10), then .NET 10 LTS as soon as Godot supports it | [20](../tech/20-architecture.md) |

### 2.2 Product & business

| # | Question | Recommended default | Where |
|---|----------|---------------------|-----------|
| D8 | **Who pays for cloud LLM inference in Early Access?** Bring-your-own OpenRouter key, a developer-run relay with quotas, or local-only? | Ship with **bring-your-own key + local + template mode**; evaluate a relay after EA telemetry | [22 Q1](../tech/22-llm-integration.md#open-questions) |
| D9 | **Working title** | Decide by M8 | [02 Q1](../02-game-overview.md#14-open-questions) |
| D10 | **Team & demo** | Budget part-time art from M2; consider a public "first winter" demo after M3 | [30 §10](30-roadmap.md#10-open-questions) |
| D11 | **Localization** | English only in v1; per-language catalogs/classifiers later | [22 Q5](../tech/22-llm-integration.md#open-questions) |
| D12 | **Voice** | Text-only v1; local TTS for barks is a post-v1 stretch | [19 Q3](../design/19-player-experience.md#open-questions), [22 Q6](../tech/22-llm-integration.md#open-questions) |

### 2.3 Content & tone

| # | Question | Recommended default | Where |
|---|----------|---------------------|-------|
| D13 | **Folklore / superstition layer** (beliefs only, never supernatural truth) | Yes, belief-only, from M5. It's cheap given the belief model. | [02 Q2](../02-game-overview.md#14-open-questions), [10 Q2](../design/10-world-and-setting.md#open-questions), [16 Q2](../design/16-social-systems.md#open-questions) |
| D14 | **Ancient ruins** | No; the only ruins are failed colonies | [10 Q1](../design/10-world-and-setting.md#open-questions), [14 Q6](../design/14-technology-and-buildings.md#open-questions) |
| D15 | **Customs for sex in succession, office and levies** | World setting: **Egalitarian (default)** / Historical | [17](../design/17-governance-and-law.md#open-questions), [18 Q3](../design/18-conflict-and-warfare.md#open-questions) |
| D16 | **Same-sex unions** | All cultures recognize them; heirs via adoption | [16 Q1](../design/16-social-systems.md#open-questions) |
| D17 | **Grim Justice** (maiming) and execution depiction | Maiming off by default; executions shown discreetly; content settings control gore | [17](../design/17-governance-and-law.md#open-questions), [18 Q6](../design/18-conflict-and-warfare.md#open-questions) |
| D18 | **Desperate acts in famine** (cannibalism) | Excluded from v1; appears only as rumor | [11 Q4](../design/11-survival.md#open-questions) |
| D19 | **Naval raids** | Out of v1 | [18 Q5](../design/18-conflict-and-warfare.md#open-questions) |

### 2.4 Systems

| # | Question | Recommended default | Where |
|---|----------|---------------------|-------|
| D20 | **Day-length normalization** — at a 60-min day, people walk twice as far per game hour | Normalize LOD1–3 travel and productivity to game time; LOD0 bodies (and the player) move physically | [10 Q4](../design/10-world-and-setting.md#open-questions), [12 Q7](../design/12-skills-and-professions.md#open-questions), [20 Q1](../tech/20-architecture.md#open-questions) |
| D21 | **Showing numbers** | Skills: tier + bar (exact values from Journeyman). Opinions: never numeric; demeanor cues only. Traits: "impressions" that can be wrong. Needs: coarse bars + diegetic cues | [02 Q4](../02-game-overview.md#14-open-questions), [12 Q5](../design/12-skills-and-professions.md#open-questions), [16 Q3](../design/16-social-systems.md#open-questions), [21 Q3](../tech/21-npc-ai.md#open-questions), [11 Q1](../design/11-survival.md#open-questions) |
| D22 | **Piece-by-piece building** for the player's homestead | No in v1 (it breaks parity); whole-blueprint placement with modules | [14 Q1](../design/14-technology-and-buildings.md#open-questions) |
| D23 | **Homeland intervention before the Silence** (a governor deposing a non-Charter ruler) | Only the Crown reeve (dues enforcement) in v1 | [17 Q1](../design/17-governance-and-law.md#open-questions), [10](../design/10-world-and-setting.md#open-questions) |
| D24 | **Combat step rate** — is 10 Hz enough for melee? | Keep 10 Hz; revisit after the M2 combat playtest | [20 Q2](../tech/20-architecture.md#open-questions) |
| D25 | **Cross-platform replay** | Not required; saves are portable, replays per platform | [20 Q6](../tech/20-architecture.md#open-questions), [21 Q2](../tech/21-npc-ai.md#open-questions) |
| D26 | **Modding** | Data packs after M8 | [20 Q12](../tech/20-architecture.md#open-questions) |

## 3. Per-document open questions

Each document's "Open questions" section is the full list. Items already settled by canon v0.2 are
marked **[Resolved — canon v0.2]** in place.

| Document | Open questions |
|----------|----------------|
| [02 Game overview](../02-game-overview.md#14-open-questions) | 5 |
| [10 World & setting](../design/10-world-and-setting.md#open-questions) | 7 |
| [11 Survival](../design/11-survival.md#open-questions) | 6 |
| [12 Skills & professions](../design/12-skills-and-professions.md#open-questions) | 7 |
| [13 Crafting & minigames](../design/13-crafting-and-minigames.md#open-questions) | 10 (5 resolved) |
| [14 Technology & buildings](../design/14-technology-and-buildings.md#open-questions) | 7 (2 resolved) |
| [15 Economy & trade](../design/15-economy-and-trade.md#open-questions) | 8 (2 resolved) |
| [16 Social systems](../design/16-social-systems.md#open-questions) | 10 |
| [17 Governance & law](../design/17-governance-and-law.md#open-questions) | 7 |
| [18 Conflict & warfare](../design/18-conflict-and-warfare.md#open-questions) | 8 (1 resolved) |
| [19 Player experience](../design/19-player-experience.md#open-questions) | 7 (1 resolved) |
| [20 Architecture](../tech/20-architecture.md#open-questions) | 12 (2 resolved) |
| [21 NPC AI](../tech/21-npc-ai.md#open-questions) | 7 (1 resolved) |
| [22 LLM integration](../tech/22-llm-integration.md#open-questions) | 10 (1 resolved) |
| [30 Roadmap](30-roadmap.md#10-open-questions) | 3 |
