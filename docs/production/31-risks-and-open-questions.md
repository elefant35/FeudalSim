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
| R3 | **LLM latency breaks immersion.** | 3 | 4 | 12 | Streaming; short replies; latency-masking UX (gestures, "thinking"); pre-generated barks; prompt caching; smaller model for low-stakes lines. **S2 (2026-10-04): PASS** — qwen3-14b latency-routed TTFT p50 286 ms, first words p50 628 ms / p95 965 ms ([spike](../spikes/s2-dialogue-latency.md)); likelihood lowered. | p50 TTFT > 1.5 s in M1 spike. | [22-llm-integration](../tech/22-llm-integration.md) |
| R4 | **LLM cost per play-hour too high** for players or for us. | 3 | 4 | 12 | Token budgets per turn; caching; local-first by M7; template mode; spend caps. | M1 measured cost > target. | [22-llm-integration](../tech/22-llm-integration.md) |
| R5 | **Prompt injection / exploits** — players talk NPCs into giving away the store. | 4 | 3 | 12 | Decision menus bound every choice (fixed parameters, no invented numbers); guards: anti-exploit floors, the daily long-shot budget, and a deterministic propensity check (that never sees player text) for critical options; untrusted-text handling; red-team suite in CI. | Any red-team case producing an off-menu action or a guard bypass. | [ADR-0003](../adr/0003-language-decides-systems-resolve.md), [22](../tech/22-llm-integration.md) |
| R6 | **Fast-decider vendor risk.** Jev isn't reachable through OpenRouter as a decision model (only `typesafe/jev-router`, a model router); direct access is waitlisted; its details come from third-party write-ups. | 2 | 3 | 6 | The `IDecider` contract uses the System One shape so providers swap freely. A small OpenRouter model reading log-probabilities works today (`qwen/qwen3.5-9b`); **Laya** (open source, Apache-2.0, Jev-compatible API) is the local fallback once fine-tuned; heuristics always work. | The logprob technique stops being served by providers; Laya fine-tuning underperforms. | [22](../tech/22-llm-integration.md), [01 §4.1](../01-canon.md#41-jev--what-we-know-and-the-fast-decider-until-we-have-it) |
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
| R21 | **Sycophancy / agreeable-NPC drift.** LLMs tend to agree with whoever is talking to them, so NPCs that decide through an LLM may say yes to the player far more often than their personalities justify. | 4 | 4 | **16** | Menus and propensities shown to the model as inclinations; long-shot budget; calibration gap ≤ 10 points vs the policy on neutral scenarios; refusal suite ≥ 95%; play telemetry on acceptance rates by NPC trait. | Calibration gap or refusal-suite failures in M1; testers say NPCs are pushovers. | [22](../tech/22-llm-integration.md), [01 §13](../01-canon.md#13-the-llm-boundary-language-decides-systems-resolve) |
| R22 | **Railroading.** Guards that reject too many LLM choices make characters feel scripted again. | 2 | 3 | 6 | Floors kept low (2–5%) and purposed only as anti-exploit; over-rejection rate tracked; critical checks limited to the critical list. | Guard over-rejection > 5% of LLM decisions; testers say NPCs feel canned. | [22](../tech/22-llm-integration.md) |
| R23 | **Local decider quality.** Laya's base checkpoints are near chance zero-shot on typed decisions (0.36) and over-confident until calibrated. | 3 | 2 | 6 | Fine-tune on our recorded decisions (M1 suites + play), temperature-scale, and keep the cloud decider and heuristics as fallbacks. | Fine-tuned accuracy below the cloud decider on the golden set. | [22](../tech/22-llm-integration.md) |
| R24 | **Art & animation capacity.** ~1,000 meshes, ~300 animation clips and ~1,200 sounds for v1; human motion, characters and music need people. | 4 | 4 | **16** | Generators for regular families (trees, rocks, crops, building kits, props); one palette; placeholders allowed until each milestone's art gate; CC0/licensed packs for prototypes; contract character artist/animator from M2 and a composer from M3 ([32 §17](32-art-and-audio-production.md#17-staffing)). | Milestone art gates slip; style drift in reviews. | [32](32-art-and-audio-production.md) |
| R25 | **License contamination** of assets (unclear sources, AI outputs without commercial terms). | 2 | 4 | 8 | Manifest entry with license required for every file; CI fails on missing licenses; acceptable-license list ([32 §14](32-art-and-audio-production.md#14-provenance--licensing)); the ledger doubles as the storefront AI disclosure. | Any asset without provenance. | [32](32-art-and-audio-production.md) |
| R26 | **Terrain3D version coupling.** A third-party GDExtension (v1.0.2, upstream-tested to Godot 4.6) already logs a deprecation (`instance_reset_physics_interpolation`) on 4.7.2; a Godot upgrade could break it. | 2 | 3 | 6 | Pin addon + engine; SHA-256 fetch; `godot.yml` headless Terrain3D check on every push; the sim owns heights, so the ArrayMesh/chunk fallback stays viable ([ADR-0009](../adr/0009-terrain.md)). | The CI Terrain3D check fails after a Godot or addon upgrade. | [20 §12.4](../tech/20-architecture.md#124-terrain--world-streaming) |
| R27 | **AI requests in flight are not saved.** Pending AI requests and the request counter are outside the save image (an M0 gap). With a player present, overheard renders (M1-07c) are almost always in flight, so a mid-session save/load re-issues requests the original run skipped and the presentation diverges (state does not). | 3 | 2 | 6 | **Mitigated (owner's approach, 2026-10-04):** `SimRunner.SaveWhenSettled()` sends a logged `HoldAiRequests`: new requests play their template at once, the save captures when none is in flight (≤ 3 s for overheard talk; while paused the runner steps just past the deadlines), then the hold lifts. `OverheardTests` covers it. **M1-04a:** pending requests, the request counter, open DPs, long-shot counters and the DP ordinal are now in the save image (`ai`, `decisions` tables) and the state hash, so even a save taken mid-flight restores identically (`PersistenceTests`); the gateway still has to re-issue restored requests in a new session, so the hold stays the normal path. | A save/load replay test shows different `AiResultApplied` events than the straight run. | [20 §11](../tech/20-architecture.md) |

| R28 | **Sim performance on minimum spec is unmeasured.** S6 passed on an Apple M3 Pro (1,500 people: worst step 20.7 ms vs the 40 ms minimum-spec budget); a ~2.5× slower CPU would exceed it at midnight (Renown pass, memory compaction). | 3 | 3 | 9 | `feudalsim bench` with `--max-step-ms`; slice the remaining nightly passes like the relationship upkeep; re-measure on minimum-spec hardware when 1,500 people become real (M5) and at M7. | `bench` on minimum spec: max step > 40 ms. | [s6](../spikes/s6-sim-scale.md), [20 §19](../tech/20-architecture.md#19-performance-budgets) |

---

## 2. Decisions needed from the owner

These are the questions whose answers most change what gets built. Each has a **recommended
default**, which the plan already assumes. Confirm or override; record the answer in the owning doc
(and in canon if it's cross-cutting).

### 2.1 Foundational — **confirmed by the owner, 2026-10-03**

| # | Question | Decision | Where |
|---|----------|-------------------------------------------|-------|
| D1 | **Engine** | ✅ Godot 4 .NET (C#) client + pure C# sim core. Unity is the fallback; the engine-agnostic sim keeps a port possible. | [ADR-0001](../adr/0001-engine-godot-dotnet.md) |
| D2 | **Perspective & art** | ✅ 3D third-person, stylized low-poly | [01 §4](../01-canon.md#4-core-product--technology-decisions), [02 §9](../02-game-overview.md#9-art-direction) |
| D3 | **Single-player only** | ✅ Yes for v1 | [ADR-0004](../adr/0004-single-player-scope.md) |
| D4 | **Time scale** | ✅ 30-min days, 8-day seasons, 32-day years, Interludes for generational time | [01 §6](../01-canon.md#6-time-canon) |
| D5 | **Where rival societies come from** | ✅ Several expeditions + resupply ships until "the Silence" + schisms/exodus + outlaw camps | [01 §5.4](../01-canon.md#54-how-multiple-societies-come-to-exist-so-war-is-possible) |
| D6 | **Player mortality** | ✅ Players age and die; default **Lineage** (continue as heir); Forgiving and Ironman modes | [01 §12](../01-canon.md#12-the-player) |
| D7 | **.NET version** | ✅ `net8.0` for M0 (support ends 2026-11-10), then .NET 10 LTS as soon as Godot supports it | [20](../tech/20-architecture.md) |

### 2.2 Product & business

| # | Question | Recommended default | Where |
|---|----------|---------------------|-----------|
| D8 | **Who pays for cloud LLM inference?** | **Development: the owner's OpenRouter key in `.env` as `OPENROUTER_KEY` (decided 2026-10-03).** Early Access still open — recommended: bring-your-own key + local + template mode; evaluate a relay after EA telemetry | [22 Q1](../tech/22-llm-integration.md#open-questions) |
| D9 | **Working title** | Decide by M8 | [02 Q1](../02-game-overview.md#14-open-questions) |
| D10 | **Team & demo** | Budget part-time art from M2; consider a public "first winter" demo after M3 | [30 §10](30-roadmap.md#10-open-questions) |
| D11 | **Localization** | English only in v1; per-language catalogs/classifiers later | [22 Q5](../tech/22-llm-integration.md#open-questions) |
| D12 | **Voice** | Text-only v1; local TTS for barks is a post-v1 stretch | [19 Q3](../design/19-player-experience.md#open-questions), [22 Q6](../tech/22-llm-integration.md#open-questions) |

### 2.2a LLM decisions (new with canon v0.3)

| # | Question | Recommended default | Where |
|---|----------|---------------------|-------|
| D27 | **Overheard NPC↔NPC exchanges** — should the LLM decide them when the player is close enough to listen? | No: the policy decides and the LLM only renders, so being watched never changes outcomes. Revisit after M1 calibration | [01 §13.2](../01-canon.md#132-where-each-decider-is-used) |
| D28 | **Fast-decider path** | OpenRouter small model (`qwen/qwen3.5-9b`, log-probability technique) now; request TypeSafe Jev access for the M1 bake-off; fine-tune **Laya** as the local decider by M7 | [01 §4.1](../01-canon.md#41-jev--what-we-know-and-the-fast-decider-until-we-have-it) |
| D29 | **Guard strictness** | Floors 2% (low/medium stakes) and 5% (high); long-shot budget 2 per NPC–player pair per day; critical options need deterministic p ≥ 0.25. Tune after M1 playtests | [01 §13.1](../01-canon.md#131-decision-points) |

### 2.2b Art & audio (new with doc 32)

| # | Question | Recommended default | Where |
|---|----------|---------------------|-------|
| D30 | **Art/animation/music budget** — how much contract work, from which milestone? | Character & animal artist + animator from M2; composer from M3; 2–4 foley/voice sessions | [32 §17, Q1](32-art-and-audio-production.md#19-open-questions) |
| D31 | **Generative-AI assets** in shipped content? | Placeholders only by default; shipped only with clear commercial terms, human review and a ledger entry | [32 §8, Q2](32-art-and-audio-production.md#19-open-questions) |
| D32 | **CC0 packs** (e.g. Quaternius, Kenney) in shipped content? | Allowed if re-paletted and recorded; otherwise prototypes only | [32 Q4](32-art-and-audio-production.md#19-open-questions) |

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
| D33 | **Per-agent vs year sim budgets** (S6 calibration finding): 20 §19's per-agent ceilings (LOD2 hour 400 µs, LOD3 day 150 µs) multiply out to 461 s / 7.2 s for a 1,500-person year, against 60 s / 5 s | **Applied (reversible, pending owner):** the year targets bind; the per-agent rows are worst single-update ceilings, averages ≤ 50 µs (LOD2 hour) and ≤ 100 µs (LOD3 day). Measured 18.6 / 35 µs | [20 Q17](../tech/20-architecture.md#open-questions), [s6](../spikes/s6-sim-scale.md) |
| D34 | **Leaning in the dialogue prompt** (M1-16 calibration): without it the model is mode-seeking (gap 14.6 pts, lift +7.3) | **Applied (reversible, pending owner):** rules v2.1 shows the policy's pre-drawn pick as a LEANING (gap ≈ 10, lift ≈ +6; still iterating at the M1 gate) | [22 Q16](../tech/22-llm-integration.md#open-questions) |
| D35 | **Conditional yes when hostile** (M1-16): 16 §5.4's `accept_with_condition` keeps ≈ ⅓ of the mass even at Opinion −60 | **Applied (reversible, pending owner):** keep the formula; refusal suite counts only the unconditional yes | [16 Q21](../design/16-social-systems.md#open-questions), [22 Q17](../tech/22-llm-integration.md#open-questions) |
| D36 | **NPC↔NPC brawl rate** (M1-22): with arguments on the §9 ladder (16 §5.2) the camp has 0.46 brawls per day for 24 people, ≈ 4–15× 18 §16's target; friends per person 0.47 → 0.22 | **Applied (reversible, pending owner):** keep 16's formulas (no authority/status/mediation in M1); calibrate in **M2** (item + owner decision) with `feudalsim social`. Tried 2026-10-04: 16 §9.4's hard end ("E < θ_current − 10 ends the exchange", not yet in code) cut brawls only 137 → 118 per 300 camp-days and moved friends to 85% of seeds in band (CI needs 90%) — reverted; land it with the M2 calibration. **M2-26 (2026-10-04):** the hard end + §9.4 mediation are in, and idle quarrels expire again (a bug): brawls 306 → 195 per 600 camp-days (0.33/day, still ≈ 3–11×), friends 91% of 100 seeds (was 83%). Remaining choice is the owner's: accept until 17's authority/status terms land, or apply a later lever now | [16 Q22](../design/16-social-systems.md#open-questions) |
| D37 | **Cloud latency targets** (22 §17.2 #1, M1-26): classification ≤ 0.3 s is below one logprob call (≈ 0.39 s p50), so gesture 1.6 s / first words 2.1 s miss 1.1 / 1.2 s | **Applied (reversible, pending owner):** keep 22's targets; the 0.3 s take keeps the first reaction < 0.4 s (canon §13.5); revisit with the single-call variant or Laya (M4). **Owner: may M1 close with #1 failing, or should targets be revised / M2 wait?** | [22 Q18](../tech/22-llm-integration.md#open-questions) |
| D38 | **W3 rivers on small islands** (M2-01a-iii): 2–4 rivers ≥ 4 km² are met by spine islands but rarely by massif/twin ridges, so retries bias the archetype mix toward spines (12/20 seeds pass everything, all spines) | **Applied (reversible, pending owner):** keep W3; CI enforces W1/W5 and reports W2/W3; next lever: per-archetype valley layouts | [10 Q16](../design/10-world-and-setting.md#open-questions) |
| D39 | **Dry-summer Clear share** (M2-03): 10 §6.4's Clear +20 (60%) is unreachable with Clear p_stay 0.6 (ceiling ≈ 56%) | **Applied (reversible, pending owner):** Clear p_stay 0.75 in Dry summers; all regimes then fit the table within 1.5 points | [10 Q17](../design/10-world-and-setting.md#open-questions) |
| D40 | **Soaked-kit night anchor** (M2-05a): 11 §2.3 says a soaked, inert sleeper is Freezing by ≈ 02:00 with Hypothermia ≈ 50 at dawn. The §9 rules give ≈ 22:00 and ≈ 74 | **Applied (reversible, pending owner):** keep the §9 rules; the test asserts the rules' outcome (Freezing 21:00–02:00, Hypothermia 50–90) and the conclusion (lethal if inert); the prose or the rules need one owner choice | [11 Q7](../design/11-survival.md#open-questions) |
| D41 | **Arterial-bleed anchor** (M2-06a): 11 §2.3 says Downed ≈ 1.6 h (Blood < 35), but a Critical arterial cut reaches Health ≤ 0 first (≈ 1.4 h) | **Applied (reversible, pending owner):** keep §4.1's Health formula; the test asserts Downed between 1.35 and 1.62 h and death at 2.5 h | [11 Q9](../design/11-survival.md#open-questions) |
| D42 | **Wound-cleaning anchor** (M2-06b): 11 §5.3's example (boiled water q 0.7 + honey → c 0.12, ≈ 11%) disagrees with its formula ×(1 − 0.7q) → c 0.204 (≈ 18.6%) | **Applied (reversible, pending owner):** the formula; the test asserts 0.204 and the untreated 57% anchor | [11 Q10](../design/11-survival.md#open-questions) |
| D43 | **Minigame calibration populations** (M2-11a): 13 §13.3 targets need practiced/novice populations the doc never defines; with proposed bots, pressure passes; strike stages miss only the practiced tail (6.1–6.2 % ≥ 0.9 vs < 5 %); choose misses practiced/novice medians (≈ 0.5 Q effect) | **Applied (reversible, pending owner):** gate CI on 12 §6.5's attentive requirement only (passes in every band and stage); report the proxies; refit from playtest telemetry | [13 Q11](../design/13-crafting-and-minigames.md#open-questions) |
| D44 | **W4 biome shares** (M2-01b-i): with the hypsometry remap, highland and pine sit mostly in band, but coast_dunes ≈ 11 % (5–9), wetland ≈ 0.3 % (5–10), river_valley ≈ 1 % (7–12, tied to D38), meadow ≈ 8 % (12–20) and broadleaf ≈ 39 % (22–32) miss on 20 seeds | **Applied (reversible, pending owner):** W4 reported, not enforced (like W2/W3); the rules follow 10 §3.7 as written; levers listed in 10 Q18 | [10 Q18](../design/10-world-and-setting.md#open-questions) |

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
| [16 Social systems](../design/16-social-systems.md#open-questions) | 22 |
| [17 Governance & law](../design/17-governance-and-law.md#open-questions) | 7 |
| [18 Conflict & warfare](../design/18-conflict-and-warfare.md#open-questions) | 8 (1 resolved) |
| [19 Player experience](../design/19-player-experience.md#open-questions) | 7 (1 resolved) |
| [20 Architecture](../tech/20-architecture.md#open-questions) | 17 (2 resolved) |
| [21 NPC AI](../tech/21-npc-ai.md#open-questions) | 13 (1 resolved) |
| [22 LLM integration](../tech/22-llm-integration.md#open-questions) | 18 (1 resolved) |
| [30 Roadmap](30-roadmap.md#10-open-questions) | 3 |
| [32 Art & audio production](32-art-and-audio-production.md#19-open-questions) | 5 |
