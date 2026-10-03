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

## 2. Consolidated open questions

*To be consolidated from every document's "Open questions" section during the consistency pass.*
