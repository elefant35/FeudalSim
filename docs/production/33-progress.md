# 33 — Progress Tracker

> **Living document.** Read and updated by the `/advance-plan` skill and by `/goal` sessions
> ([34-session-goals](34-session-goals.md)). Humans edit it too — especially **Owner approvals** and
> **Blockers**. Keep entries short; evidence is a commit hash, a command and its result, or a file.

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done (with evidence) · `[!]` blocked
(see Blockers) · `[-]` dropped (with reason)

---

## Current milestone

**M1 — Talking Camp** · started: 2026-10-04 · target exit: all M1 items `[x]` and the exit criteria in
[30-roadmap §5 (M1)](30-roadmap.md#m1--talking-camp) verified (including 22 §17.2's 13 criteria).

Previous: **M0 — Foundations — complete 2026-10-04** (gate passed at 7f4756e; evidence in the M0
sections below). Next after M1: **M2 — Landfall**.

---

## Owner approvals

Standing permissions the owner has granted for unattended sessions. The skill checks here before
doing anything in these categories; if a category isn't approved, it asks once and records the
answer.

| Category | Approved? | Notes |
|----------|-----------|-------|
| Install listed prerequisites with Homebrew (`git-lfs`, `ffmpeg`, `gh`; optional `sox`, `fluidsynth`) | **not yet** (nothing pending) | Owner installed git-lfs 3.8.0, ffmpeg 9.0.2 and the .NET 10 SDK (2026-10-03/04). `gh` remains optional. Commands are in 20 §20 step 1 and 32 §2 |
| Install the pinned Godot 4.x .NET editor | **done by the owner** — 4.7.2 .NET (2026-10-04) | Engine upgrades or plugin downloads (e.g. Terrain3D) still need approval; the skill proposes the exact version first |
| Push to `origin/main` after a work item passes verification | **yes — proposed default, owner to confirm or change** | Never force-push |
| LLM spend from `OPENROUTER_KEY` during development | **yes — ≈ $50 total for development (owner, 2026-10-04; soft, "plenty of wiggle room")**; spend tokens freely when it helps; keep a running tally below | The owner cares more about the **shipped game's running cost** (≤ $0.05 per typical play-hour, canon §13.5.6) than dev spend. Benches carry a `--max-usd` ceiling. **Dev spend so far: ≈ $0.06** (M0 ≈ $0.001; S2 bench $0.054) |
| Download resources needed for development (plugins such as Terrain3D, libraries, tools, CC0/permissive assets) | **yes — standing approval (owner, 2026-10-04)** | The license must be acceptable and recorded (32 §14, `ASSET_LICENSES.md`); pin versions and checksums; prefer official sources |
| Commit generated binary assets (requires Git LFS installed) | **yes** — Git LFS installed 2026-10-03 | — |
| Approve art "looks" (manifest status → `approved`) | **owner only** | Claude may set `review`, never `approved` |

---

## M1 — Talking Camp work breakdown

Generated 2026-10-04 at the M0 gate from [30 §5 (M1)](30-roadmap.md#m1--talking-camp) (in scope + exit
criteria), the M1 rows of [21 §20](../tech/21-npc-ai.md), [16](../design/16-social-systems.md),
[15](../design/15-economy-and-trade.md), [19](../design/19-player-experience.md),
[22 §17.1–17.2](../tech/22-llm-integration.md#172-m1--talking-camp-the-de-risking-milestone-for-this-document),
[32 §16](32-art-and-audio-production.md#16-production-schedule-by-milestone), the M0→M1 discovered work, and
the M1 spikes. Items are sized ≈ ½–2 days; split them (`M1-12a`…) when they grow. Risk-retiring items
(spikes, the decision-first pipeline, calibration) go first.

### Spikes (30 §4)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M1-S2 | **Dialogue latency & cost** — model bake-off; TTFT p50 < 1.0 s, first words ≤ 1.2 s (Tier A); cost per play-hour; pin fast providers | [x] | **PASS** — [docs/spikes/s2-dialogue-latency.md](../spikes/s2-dialogue-latency.md). `ai bench-dialogue`: 6 models × {default, latency} × 20 streamed decision-first turns (1,846-token 22 §7 prompt), 240 turns, $0.054. Default **qwen3-14b + `provider.sort=latency`**: TTFT p50/p95 **286/649 ms**, CHOICE line 449/709 ms, first words **628/965 ms**, 100% valid headers, 0/40 wrong prices; **$0.030 typical / $0.078 heavy per hour** ($0.032 / $0.083 normalized to 2,290 tokens). Routing now default (`LLM_PROVIDER_SORT=latency`; pings 655–1,142 ms vs M0's 3.9–9.6 s). Found: 22 §4.8's `"\n\n"` stop emptied every Qwen3 reply (fixed in 22); qwen3.6 fast but 22% wrong prices; qwen3.8-flash blocked by the account's data policy. `DialogueBenchTests` 5/5 |
| M1-S3 | **Fast-decider bake-off** — `qwen/qwen3.5-9b`, `qwen/qwen3-30b-a3b-instruct-2507`, Laya zero-shot, Jev if access; p50/p95 latency (quick-choice DPs need ≤ 500 ms; M0 measured ~650–840 ms), ECE, cost, injection susceptibility | [ ] | |
| M1-S6 | **Sim scale** — 1,500 agents across LOD tiers within 20 §19 budgets; 1 game year headless ≤ 60 s at LOD2; 21's 300–500 agents × 1 year | [ ] | |
| M1-S1 | **Crowd render** — 150 animated low-poly characters at 60 fps (recommended spec), 300 in battle mode ≥ 30 fps | [ ] | |
| M1-S4 | **Terrain streaming 8,192 m** (carried from M0): typed Terrain3D facade, region streaming while walking/running the full map with no hitch > 50 ms, VRAM in budget; **parallel chunked heightfield generation** (8 km takes 15.7 s single-threaded) and world caching under `user://worlds/<seed>/` | [ ] | |
| M1-S5 | **Local LLM early look** — 7–14B 4-bit beside the Godot client; p50 TTFT ≤ 1.5 s while holding 60 fps | [ ] | |

### Person model & NPC AI (canon §10, 21, 11)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M1-01 | Person model components: attributes, skill subset, personality facets, values, the 16 canon traits (+ ~8), psych + simplified physical needs, emotions & mood — tables, content YAML, save columns | [ ] | |
| M1-02 | Utility AI v1: action catalog (eat, drink, sleep, gather wood/food, tend fire, socialize, idle, flee), scoring + curves, noise & hijack, simple schedules (21 §7.1–7.6); 21 §19 M1 headless metrics | [ ] | |
| M1-03 | **DP propensity layer** (21 §7.8): `m_o` terms, family tempering, `includes`, hijack fold-in, `MenuPropensities` + KeyFactors; DecisionTrace / inspector | [ ] | |
| M1-04 | Initiative options (21 §14.5) and pending-DP state (21 §14.6); **persist open DPs, long-shot counters and pending AI requests in saves** (M0 discovered work) | [ ] | |

### Social core (16)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M1-05 | Opinion (modifiers #1–35, #38–39, D1–D5, D9), Trust, Familiarity, Fear; tags through Enemy/Rival | [ ] | |
| M1-06 | NPC↔NPC interactions Chat/Gossip/Joke/Praise/Comfort/Request/Argue/Insult/Apologize/Warn (policy); memory + compaction | [ ] | |
| M1-07 | Claims, beliefs and rumors with mutation; reputation axes (no Lawfulness effects yet); LLM rendering of overheard talk (policy decides) | [ ] | |
| M1-08 | Escalation ladder through rung 5 with a **stubbed brawl** hand-off; provocation-response and bystander DPs (the owner's "insult → shove → brawl" and "bystander steps in" examples) | [ ] | |
| M1-09 | Social DP owners: rapport (#58–59, +10/day cap), apology, request, being told | [ ] | |

### Trade (15)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M1-10 | Haggle prototype: §5 values and curves, §5.6 trade DP owner (menus, propensities, guards, concession steps from `Margin = C_sys·s·(0.5+0.5·K_skill)`), stub trade execution moving goods and coin at the menu price | [ ] | |

### Dialogue pipeline & AI (22)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M1-11 | Sanitize + input limits (280/500 chars, 2 s between turns) + fast-decider act classification (catalog v2, thresholds p ≥ 0.45 / margin 0.10, injection flag ≥ 0.3 → policy) | [ ] | |
| M1-12 | **Decision-first LLM reply**: bundled reply route in the gateway, header parse + pre-cleared lookup, Tier A streaming / Tier B verification, one regeneration then template (canon §13.5 #2) | [ ] | |
| M1-13 | Prompt building from sim state (persona, inclinations as verbal bands, glosses, memories) and template coverage for every line kind | [ ] | |
| M1-14 | Resilience: network cut mid-conversation → policy + templates within one turn; breaker latency trigger (p95 TTFT); sim never delayed by language calls (22 §17.2 #9) | [ ] | |
| M1-15 | Determinism: a recorded 1-hour conversation session replays with identical menu hashes, `DecisionResolved` records and state hashes (22 §17.2 #10) | [ ] | |
| M1-16 | **Calibration & red-team harness**: golden neutral scenarios (LLM-vs-policy gap ≤ 10 pts per family), refusal suite ≥ 95%, red-team suite (0 off-menu / guard bypass), guard over-rejection ≤ 5% (22 §17.2 #2–4) | [ ] | |
| M1-17 | Template-mode completability scenario in CI: the policy decides every DP (22 §17.2 #12) | [ ] | |

### Client, art & audio (19, 32)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M1-18 | Graybox camp scene: 24 settlers with the player, LOD0 bodies near, conversation entry; flat test terrain (real terrain is out of scope) | [ ] | |
| M1-19 | Dialogue UI: free text, streaming, quick intents, intent echo with 1.5 s unsay, latency choreography, decision surfacing (gestures, stance cues, proposal cards, stub trade panel, escalation ladder); People page (basic) | [ ] | |
| M1-20 | Graybox kit through the art pipeline: capsule people with role markers, primitive props, block buildings (~40 assets, manifest + checks + previews) | [ ] | |
| M1-21 | Audio: placeholder UI sounds and reaction "bark" vocalization placeholders (32 §16) | [ ] | |

### Exit-criteria work

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M1-22 | Headless social sim: 30 game days of the camp, no deadlocks; a public event's rumor reaches ≥ 80% within 3 days; ≥ 1 emergent dispute per 10 days | [ ] | |
| M1-23 | Cost and latency report at verified prices: ≤ $0.05 per typical play-hour (22 §17.2 #1, #8) | [ ] | |
| M1-24 | **Playtest** (owner-run: ≥ 5 people × ≥ 45 min; 22 §17.2 #11 rubric + 30's feel criteria) | [ ] | Needs the owner to recruit testers |
| M1-25 | ADR-0010 step 3: spike a `net10.0` game project on Godot 4.7.x (or adopt a Godot release whose GodotSharp targets net10) — .NET 8 support ends 2026-11-10 | [ ] | |

---

## M0 — Foundations work breakdown

Source of truth for the steps: [20 §20](../tech/20-architecture.md#20-m0-foundations-checklist)
(acceptance criteria live there), [32 §16](32-art-and-audio-production.md#16-production-schedule-by-milestone),
[22 §17.1](../tech/22-llm-integration.md#171-by-milestone).

### Engineering (20 §20)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M0-01 | Prerequisites: pinned Godot 4.x .NET, .NET SDK check, `gh` (optional), remote set | [x] | Owner installed Godot via `brew install --cask godot-mono`: `4.7.2.stable.mono.official.ed1daf0bf` (recorded in `game/GODOT_VERSION`); .NET SDKs 8.0.401 + 10.0.401 (the cask's dependency); remote ✓; Git LFS ✓; `gh` optional, not installed |
| M0-02 | Scaffolding: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `BannedSymbols.txt`, `FeudalSim.sln` | [x] | `dotnet build` → 0 warnings, 0 errors. SDK 8.0.401 (Roslyn 4.11) forced pins: BannedApiAnalyzers 3.3.4, JsonSchema.Net.Generation 7.0.1, and removal of the transitive Humanizer analyzer (`Directory.Build.targets`) — input for ADR-0010 |
| M0-03 | Projects + architecture test (Sim references no engine/IO/network/YAML) | [x] | 5 src + 4 test projects; `dotnet test` → 5/5 pass (xunit.v3 4.0.1). Injected `HttpClient` in Sim → architecture test fails; injected `System.Random` → build error RS0030; both reverted |
| M0-04 | Sim kernel: ids, `SimClock`, calendar, `SimRandom`, phase pipeline, `People` table, commands/events, two toy systems | [x] | `dotnet test` → 31/31 pass: calendar (32-day year, 8-day seasons, Hearthday, day-of-year 1–32), 18,000 steps = 1 game day at 30 min, all 11 day lengths give whole game-ms/step (found & fixed: 25/50-min days have fractional ratios), invalid day lengths rejected (clock + logged command), RNG key reproducibility, salt uniqueness, spawn/wander/needs. `StateHasher` (XxHash64, canonical order) included for M0-05 |
| M0-05 | Determinism harness (`StateHasher`, fixed-chunk job runner) | [x] | Integration tests (stable over 4 runs): same seed twice × 10,000 steps × 300 people → equal hashes; `JobRunner` 1 vs 4 threads → equal; different seeds → different; injected hash-code-dependent ordering → mismatch caught. Note: .NET Dictionary/HashSet enumerate in insertion order until removals, so the injected bug orders by identity hash instead |
| M0-06 | Persistence v0 (snapshot + input log, save/load equivalence) | [x] | Tests: 5,000 + atomic save/load + 5,000 steps = 10,000 straight (hash + event seq); missing column → default, unknown column dropped with warning; torn log tail (−3 B) → 9 good records recovered, file truncated, append continues; layout-fingerprint test. Benchmark (1,500 people, M-series, .NET 8.0.8): MessagePack 56 µs vs MemoryPack 57 µs serialize, 33 vs 26 µs deserialize; MessagePack+LZ4 141 µs / 59 KB vs 203 KB raw → keep MessagePack + LZ4 (ADR-0008). v0 is one LZ4 MessagePack document; the TOC/chunk file layout of 20 §9.4 is deferred to M3 (saves are dev artifacts until then) |
| M0-07 | Content v0 (YAML → schema → compiled DB; the 28 canonical skills) | [x] | `feudalsim content validate` → "OK — 5 files, 28 skills, 9 needs, 14 items; hash ea0b142a35c1cf2e". Broken fixture → `items/bad.yaml:10:17: /1/base_value_f: Value is "string" but should be "integer"`. `content schemas --check` → up to date. 8 content tests (canon skill/need lists, handle order, prefix, duplicates, hash stability, schema freshness). Replaced JsonSchema.Net.Generation with a small deterministic generator (removed the Humanizer-analyzer workaround) |
| M0-08 | Headless CLI v0 (`run` a scenario, CSV metrics, `--verify-determinism`) | [x] | `dotnet run --project src/FeudalSim.Headless -- run --scenario content/scenarios/m0_smoke.yaml --verify-determinism` → exit 0; "determinism: rerun c2c49b7f89566174, 1-thread c2c49b7f89566174 → IDENTICAL"; 54,000 steps in 0.15 s (~350k steps/s); writes `sim_runs/<id>/metrics_daily.csv`, `run.json` (final + content hash), `inputs.fslog`. NeedsDecay now reads rates from content |
| M0-09 | Hosting (sim thread, time scale, pause, snapshots, dev commands) | [x] | `feudalsim run --realtime --seconds 60` → "604 steps in 60.4 s = 10.00 steps/s (target 10.0 ± 0.2) → OK; dilation events 0". Tests: triple buffer 200k writes under contention → 0 torn, 0 backwards; pause/step/resume via dev commands; 9 dev commands (pause, resume, step, timescale, daylength, spawn, hash, time, stats); snapshots + event ring reach presentation. Fixed: runner command Seq now continues from the world's (persisted) last Seq |
| M0-10 | AI gateway v0: chat ping via OpenRouter (`OPENROUTER_KEY`), **fast-decider ping** (normalized option probabilities), template fallback, recording | [x] | `feudalsim ai ping` → "key: set · qwen/qwen3-14b via openrouter · reply: Good morning, traveler—welcome to our humble village… · 9578 ms · 56/27 tokens · $0.000013". `feudalsim ai decide` → qwen/qwen3.5-9b: A 0.001 · **B 0.834** · C 0.164 · D 0.001, sum 1.00, 655 ms, $0.000013. Tests: key never printed (scan) but used; no key → template/heuristic; no logprobs → `decider: unavailable`; 3 s fake provider vs 10-step deadline → fallback at step 12, late result rejected, headless replay reproduces identical events + hash. **Found:** OpenRouter `reasoning.enabled=false` is ignored by DeepInfra for qwen3-14b (empty replies) → gateway appends Qwen's `/no_think` |
| M0-11 | Godot project boots `SimHost` with a debug overlay | [x] | `dotnet build game/FeudalSim.Game.csproj` → 0 warnings, 0 errors (Godot.NET.Sdk 4.7.2). Headless `--import` OK (first run crashed in Godot's thread cleanup at exit after importing; re-run clean). Windowed Movie Maker run: log "SimHost: started scenario.m0_smoke (seed 42, 24 settlers)", 0 errors; frame shows ground, 24 wandering capsules with shadows, overlay "Y0 Spring 1 05:32 · step 29 · Running · 24 settlers". Lesson: `.tscn` `Transform3D` stores the basis row by row |
| M0-12 | Test terrain spike (Terrain3D vs ArrayMesh) — feeds spike S4 | [x] | Both approaches on the same seed-42 heightfield, capsule autowalk, GPU timed under Vulkan (Metal reports 0; fps capped 120 by the display): **ArrayMesh 512 m** GPU 1.64 ms, 77 MB VRAM; **Terrain3D v1.0.2 512 m** GPU 2.22 ms, 81 MB, heights 0.000 m off the sim; **Terrain3D 8,192 m** GPU 4.46 ms avg / 6.09 max, **301 MB VRAM**, import 0.56 s, worst frame 9.1 ms, on floor. Interop `Call` ≈ 0.9 µs vs C# ≈ 20 ns. Found & handled: region-grid snapping in `import_images`, `change_region_size` only after entering the tree, `free_editor_textures` drops runtime assets. Screenshots reviewed (same look both ways). Addon pinned + SHA-256 (`tools/godot/fetch_addons.sh`); headless `--terrain3d --check` → "CHECK PASS … 0.000 m"; **CI green at 49e5cc2** (ci 37166936093; godot 37166936183: addon fetched + cached, Terrain3D check passes on ubuntu). → **ADR-0009 Accepted** |
| M0-13 | Sim-driven capsule NPC with LOD0 ↔ LOD1 hysteresis; replay of a client session headless | [x] | `tools/godot/embodiment_check.sh` → Godot `--headless` autotest (`EmbodimentSpike.tscn`, 3 settlers, player walks 150 m out and back) then `feudalsim replay` of its `inputs.fslog`: "PASS: Godot session and headless replay agree at step 698 (hash 46c45ef7e2e8defe); 6 embodiments, 3 demotions, max snap 0.106 m"; no Godot errors/warnings. Demotion 5 s after crossing 100 m (steps 244–258), re-embodied at 78–92 m. `EmbodimentTests` (sim side) pass; 58 tests total. Found: navmesh from the render mesh triggers a GPU-readback warning → bake from CPU heightfield triangles (ADR-0007) |
| M0-14 | CI: `ci.yml` (ubuntu + macOS) and `godot.yml` | [x] | `ci.yml`: ubuntu + macOS build (warnings as errors), tests, content validate, schemas/licenses fresh, smoke run 1 vs 4 threads; injected unused variable → CS0219 fails the build. `godot.yml` (ubuntu, official Godot 4.7.2 .NET build, cached; version checked against `game/GODOT_VERSION`): game build (warnings as errors) → `--import` → Boot `--autotest` → `tools/godot/embodiment_check.sh`. **Both green on main** at d04d2de (ci 37165220650, godot 37165220651, every step success, ~2 min) and at 2b7c10f on the .NET 10 SDK (ci 37165483972, godot 37165483955) |
| M0-15 | ADRs 0005–0010 written (data layout, time model, embodiment, saves, terrain, .NET version); `CLAUDE.md` commands filled in | [x] | ADRs 0005–0010 all **Accepted** with M0 evidence (0009 terrain from the M0-12 spike; 0010 step 2 done). 20 bumped to **v0.2** (§12.4 terrain decision + numbers, risk row). `CLAUDE.md` Commands cover .NET, headless, AI, Godot, addons. New risk R26 (Terrain3D version coupling) in 31 |
| M0-16 | Decision-point records — `DecisionPointId`, `DecisionPointOpened`, `DecisionMade`, `DecisionResolved` / `DecisionPointCancelled`, `IDecisionPointOwner` (20 §8.5, §11; 22 §6; canon §13) | [x] | `src/FeudalSim.Sim/Decisions/` (`DecisionRulesEngine` = `world.Decisions`). Names reconciled (22 §6 note: `DecisionSubmitted` ≡ `DecisionMade`). Menu hash canonical (id order, sorted params, P/PCrit at 1e-4; gloss excluded). `DecisionPointOpened` goes through the normal submit path as a `CommandSource.Integrity` command in `SimRunner` and `ScenarioRunner`, verified (not applied) next step; DRE state is in `StateHasher`. `DecisionPointTests` 16/16; **mutations caught:** unhashed DRE state → `StateHash_CoversOpenDecisionPoints` fails; disabled verification → the tamper test fails. Headless `run --scenario content/scenarios/m0_dp_ping.yaml --verify-determinism` → "IDENTICAL ea25ca3b19f19ee9"; logs: "[Integrity] DecisionPointOpened … @step 21", "DecisionResolved { Chosen = accept_at_price, Decider = Policy, Guard = Deadline } @step 60"; `replay` → same hash ea25ca3b19f19ee9 |
| M0-17 | Deterministic **policy decider** + **DP watchdog** with a toy owner | [x] | Policy pick drawn at open (`RngStream.Ai`, chooser, `Salt.DecisionPolicy`, DP id); 4,000 draws match p (0.45/0.30/0.25 within ±3 pts) and repeat exactly. Guards (22 §6.6): off-menu, ineligible, floors 0.02/0.05, critical `PCrit ≥ 0.25`, long-shot budget 2/pair/day (player-favoring only, executed model picks only, resets next game day, spent long shots drop out of `PreCleared`). Watchdog: policy at `DeadlineStep`, a decision landing on the deadline step still applies, late → `CommandRejected`. Stale menu via a logged `SpawnPerson` → pre-drawn pick re-sampled over still-eligible options (`Salt.DecisionPolicyResample`). `Choice = null` → "policy, now". The 21 §7.8 propensity layer (personality, emotions) is M1 |
| M0-18 | Gateway resilience — per-provider **circuit breaker**, the **`AI_GATEWAY_MODE` recorder**, and gateway routing of decision points | [x] | `CircuitBreaker` (22 §3.5: 5 failures/60 s → open; probe after 30 s, one at a time; 3 successes → closed; chat and decider separate). `TranscriptHandler` (record only with `LLM_LOG_TRANSCRIPTS=true`; key = SHA-256 of method+path+body; headers never stored; replay never touches the network, needs no key; miss → fallback). `AiGateway.Open/Cancel/Decided`: shuffled pre-cleared options (pure function of DP id) → fast decider → `DecisionMade(Fast)`; template/budget/breaker/failure → "policy, now". `GatewayResilienceTests` 7 (incl. a SimRunner session with a fake decider replayed headless to the same hash with no model call). **Live:** `ai decide` recorded (818 ms, $0.000013, key occurrences in transcript: 0) then replayed (17 ms, identical probabilities 0.001/0.834/0.164/0.001); `run --scenario content/scenarios/m0_dp_ping.yaml --realtime --seconds 10` → "DecisionMade { Choice = counter_step_1, Decider = Fast, ProviderTag = openrouter-llm:qwen/qwen3.5-9b, LatencyMs = 841 } @step 29", "DecisionResolved … Guard = Passed", final hash ff6b7420c63dd0c1; `LLM_MODE=template replay` of that log → "decisions 1; integrity mismatches 0; final hash ff6b7420c63dd0c1"; template-mode run → resolved @step 21 (Guard = ModePolicy), not at the step-60 deadline. Fixed: realtime rate counted the 200 ms post-run drain (10 s runs read 9.72) |
| M0-19 | OS keychain storage for API keys | [-] | **Dropped 2026-10-04 by the owner:** "just use env for now, don't worry about publishing the game later on". Keys stay in `.env`; 22 §17.1 updated |

### Art & audio pipeline (32 §16)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M0-A1 | Git LFS installed and `git lfs install` run; `.gitattributes` patterns active | [x] | 2026-10-03: git-lfs 3.8.0 via Homebrew; repo hooks installed (pre-push, post-checkout, post-commit, post-merge); `git lfs track` lists all 15 patterns; `git check-attr` resolves `.glb/.png/.wav` to `filter: lfs` |
| M0-A2 | Palette v0 (`art/palettes/palette.png` + `palette.yaml`) | [x] | `tools/art/make_palette.py` → 64 named swatches in 8 rows (earth, foliage, stone/metal, skin/hair, cloth, culture accents, water/sky, special) as `palette.png` (256², LFS) + `palette.json` (machine-readable; replaces the planned YAML so Blender's Python can read it) |
| M0-A3 | `tools/art/export.py`, `check.py`, `preview.py` (headless Blender) | [x] | `export.py` runs a generator → `.glb`; `check.py` → `RESULT {ok: true, tris {0: 56, 1: 24}, materials [palette], height 4.23 m, problems []}` against `budgets.json` (32 §5); `preview.py` → 2×2 turntable + 40 m silhouette (reviewed: palette colours correct, silhouette reads) |
| M0-A4 | Test asset end-to-end: `art/generators/conifer` → `.glb` → Godot import → manifest entry | [x] | Generator → `pine_a.glb` (LFS) → checks pass → preview reviewed → manifest `asset.flora.pine_a` (review) → Godot import (`pine_a.glb.import`, extracted palette texture) → 14 instances render in the Boot scene with correct palette colours |
| M0-A5 | Asset manifest schema (`content/assets/*.yaml`) + `ASSET_LICENSES.md` generation | [x] | `AssetDef` kind in the content pipeline (schema generated, outputs + generator must exist, attribution needs a credit); `feudalsim content licenses [--check]` → `ASSET_LICENSES.md`; CI checks freshness; test: a missing output fails validation |
| M0-AU1 | `ffmpeg` installed | [x] | Owner fixed the install: ffmpeg 9.0.2 at /opt/homebrew/bin. Cross-check: fire crackle (2 s) ffmpeg ebur128 −24.4 LUFS = ours −24.4. For SFX < 400 ms BS.1770 is undefined (ffmpeg reports −70); ours reports a single-block estimate (documented in `loudness.py`) |
| M0-AU2 | `tools/audio/synth.py` and `check.py` (format + EBU R128 loudness) | [x] | `synth.py` (deterministic numpy recipes: knap_flake, fire_crackle, ui_click → 48 kHz 16-bit WAV at −1 dBFS); `check.py` (naming, 48 kHz, 16-bit, mono for 3D, peak, LUFS with bus targets). Numpy BS.1770 meter validated: 1 kHz full-scale sine → −3.00 LUFS (ref −3.01), amplitude 0.1 → −23.00 (ref −23.01). 4 SFX pass |
| M0-AU3 | One SFX end-to-end into Godot via `content/audio_events.yaml` | [x] | `content/audio/events.yaml` → `audio.world.campfire` resolved by `SimHost` → `AudioStreamPlayer3D` looping `sfx_world_fire_crackle_01.wav` at the camp: log "audio audio.world.campfire → … (bus SfxWorld, loop True, spatial True), playing True" |

### Spikes started in M0

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| S4 | Terrain streaming for 8,192 m (continues into M1) | [-] | **Moved to M1 as M1-S4** — **carried into M1** (decided 2026-10-04): 30 §4 timeboxes it at 1–2 weeks over M0–M1, feeding M2, and it is not an M0 exit criterion. It starts after the terrain technology is chosen (M0-12 → ADR-0009), because Terrain3D brings its own region streaming while the built-in fallback needs our own chunking/LOD. Pass: walk/run the full 8 × 8 km map with no hitch > 50 ms and VRAM within budget |

---

## M0 exit criteria (from [30 §5](30-roadmap.md#m0--foundations))

**Gate 1 (2026-10-04, 49e5cc2):** all seven criteria passed, but M0 stayed open — part of the 22 §17.1
M0 gateway scope was missing from the breakdown → M0-16…M0-19 added.
**Gate 2 (2026-10-04, 7f4756e): passed — M0 complete.** M0-16–18 done, M0-19 dropped by the owner, S4
moved to M1; every criterion re-run after those changes (evidence below).

Verified only by running the command or test and pasting the result into **Evidence**.

| Criterion | Status | Evidence |
|-----------|--------|----------|
| `dotnet build` and `dotnet test` green in CI on every push | [x] | CI (ubuntu + macOS) and `godot.yml` run on every push and PR. **Gate 2:** green at 7f4756e (ci 37168843623, godot 37168843600); every push this session went green (d04d2de, 2b7c10f, 49e5cc2, a367281, 7f4756e) |
| Headless runner: 1 game year, 24 agents, same seed → byte-identical event logs | [x] | **Gate 2:** `run --scenario content/scenarios/m0_smoke.yaml --days 32 --write-events` in two processes: 576,000 steps each (0.54 s / 0.56 s), final hash 0bb4d5da1276f3ec both (DRE state now in the hash); `events.fslog` b692b44d…a45a6f and `inputs.fslog` c8aa7c24…254c78 in both — same bytes as 2026-10-03 |
| Godot client shows a capsule moving by sim commands; pause and time scale work | [x] | **Gate 2:** `Godot --headless --max-fps 60 --path game -- --autotest`: "×1 rate: 10 · settlers moved at ×1: 23 · steps while paused: 0 · settlers moved while paused: 0 · tree paused: 1 · ×4 rate: 40 · Engine.TimeScale: 4 · AUTOTEST PASS · dilation events 0", exit 0, no warnings; also in `godot.yml` |
| A command-line call reaches OpenRouter and the response is recorded into the event log | [x] | **Gate 2:** `run --scenario content/scenarios/m0_ai_ping.yaml --realtime --seconds 25` (9.96 steps/s) → "seq 25 @step 29 [Ai] AiResultCommand { Outcome = Ok, Text = Good morrow, fellow survivor. Hope you slept well on the cold ground., ProviderTag = openrouter, LatencyMs = 888, TokensIn = 62, TokensOut = 20 }"; events log: AiResultApplied, UsedFallback = False. Decision points too: a live fast-decider `DecisionMade` (M0-18) |
| Content validation fails CI on malformed YAML | [x] | **Gate 2:** scratch copy → `content validate --root` exit 0; plus `fixtures/broken/items/bad.yaml` → "content/items/bad.yaml:3:1: /: Some items do not match the required schema … FAILED with 5 error(s)", **exit 1**; CI runs it on every push |
| All 15 steps of 20 §20 pass, including replay of a recorded client session | [x] | Steps 1–15 verified (M0-01…M0-15). **Gate 2:** `tools/godot/embodiment_check.sh` → "PASS: Godot session and headless replay agree at step 698 (hash 966311a24a11c4a1); 6 embodiments, 3 demotions, max snap 0.128 m"; Terrain3D `--check` → "CHECK PASS … 0.000 m"; plus the 22 §17.1 gateway scope (M0-16–18) |
| ADRs 0005–0010 written and accepted | [x] | **Gate 2:** 0005, 0006, 0007, 0008, 0009, 0010 — all `Status: Accepted` |

---

## Blockers (need the owner)

| Date | Item | Question / needed approval | Status |
|------|------|----------------------------|--------|
| 2026-10-03 | M0-01, M0-11–13, M0-A4, M0-AU3, godot.yml in M0-14 | ~~**Install Godot 4.7.2 (.NET edition)** — latest stable; its GodotSharp targets net8.0, matching our SDK. Self-install: `brew install --cask godot-mono` (official `Godot_v4.7.2-stable_mono_macos.universal.zip`), or approve the install in Owner approvals and the next session will do it~~ | **resolved 2026-10-04** (owner installed) |
| 2026-10-04 | M0-12 (Terrain3D half), ADR-0009 | ~~**OK to download Terrain3D?** Third-party native GDExtension (MIT) from github.com/TokisanGames/Terrain3D releases (a zip of a few tens of MB with macOS/Windows/Linux binaries) into `game/addons/terrain_3d/`. Needed to compare it against the built-in approach, which already works at 512 m but needs our own chunking/LOD for the full 8 km map~~ | **resolved 2026-10-04** (owner: yes, plus a standing download approval) |
| 2026-10-04 | M0-19 | ~~**Defer OS keychain storage to M8?** 22 §17.1 lists it under M0, but only shipped builds need it (development reads `.env`). Recommendation: defer to M8 and note it in 22 §17.1. If you'd rather keep it in M0, it's ~½ day (macOS `security` CLI / Windows Credential Manager behind `ISecretStore`)~~ | **resolved 2026-10-04** (owner: use `.env`; no keychain) |
| 2026-10-03 | M0-AU1 (and OGG encoding later) | **ffmpeg didn't install** (Homebrew source build failed in x265 on macOS 14). Options: (a) update Command Line Tools (`sudo rm -rf /Library/Developer/CommandLineTools && sudo xcode-select --install`, or Software Update) then `brew install ffmpeg` again; (b) install a prebuilt static arm64 ffmpeg binary onto your PATH; (c) MacPorts (`sudo port install ffmpeg`). Not blocking until music/ambience needs OGG | **resolved 2026-10-04** (owner installed ffmpeg 9.0.2) |

---

## Discovered work (not yet scheduled)

Items found while working that belong to a later milestone or need triage.

| Date | Item | Suggested milestone | Source |
|------|------|---------------------|--------|
| 2026-10-03 | Fine-tune Laya as the local fast decider from recorded decisions | M7 (data from M1) | canon §4.1 |
| 2026-10-03 | Apply for TypeSafe Jev API access for the S3 bake-off | M1 | 31 D28 |
| 2026-10-03 | Dialogue latency varied 3.9–9.6 s for qwen/qwen3-14b on OpenRouter (provider routing); S2 must pin fast providers or pick another model | M1 (S2) → **scheduled: M1-S2** | M0-10 |
| 2026-10-03 | Persist pending AI requests and the AI request counter in saves — **and the DRE's open decision points, long-shot counters and DP ordinal** (hashed since M0-16, not yet saved) | M1 → **scheduled: M1-04** | M0-10, M0-16 |
| 2026-10-04 | **Fast-decider latency vs the 0.5 s quick-choice deadline:** `openrouter-llm` answered in ~650 ms (M0-10), so combat/quick-choice DPs (5 steps) would mostly fall to the policy. S3's bake-off must reach p95 ≤ 500 ms (Laya local, Jev) or quick choices stay policy-only | M1 (S3) → **scheduled: M1-S3** | M0-17 |
| 2026-10-03 | Full 20 §9.4 snapshot layout (header + TOC + per-chunk hashes) | M3 | M0-06 |
| 2026-10-03 | ~~**Install the .NET 10 SDK, update `global.json`, unpin the Roslyn-4.11 workarounds**~~ — **done 2026-10-04** (SDK 10.0.401 arrived with the Godot cask; ADR-0010 step 2) | M0 | M0-02, M0-07 |
| 2026-10-04 | **Parallel, chunked world-gen heightfield** (8 km takes 15.7 s single-threaded; ≤ 60 s budget, cached per seed) and the **typed Terrain3D facade** with import/caching under `user://worlds/<seed>/` | M1 (S4) / M2 → **scheduled: M1-S4** | M0-12, ADR-0009 |
| 2026-10-04 | Ship Terrain3D's MIT notice in builds (Godot export filters drop `LICENSE.txt`) | M8 | ADR-0009 |
| 2026-10-04 | **Choice skew vs inclinations:** in S2 qwen3-14b picked `counter_step_1` ("quite possible") 35/40 times over `accept_at_price` ("likely") — measure against the policy in M1-16's golden scenarios; prompt/inclination wording may need tuning | M1 → **M1-16** | S2 |
| 2026-10-04 | **Speech must carry the option's price:** S2 saw wrong or omitted prices in 22–43% of lines from some models (none from qwen3-14b). Tier A streams trades < 48f before verification — add a cheap deterministic price-word check on the SAY text (22 §4.9) | M1 → **M1-12 / M1-13** | S2 |
| 2026-10-04 | ADR-0010 step 3: spike a `net10.0` game project on Godot 4.7.x, or wait for a Godot release whose GodotSharp targets net10 — .NET 8 support ends 2026-11-10 | M1 → **scheduled: M1-25** |  ADR-0010 |

---

## Session log

Newest first. One entry per session or work item: date, what changed, evidence, what's next.

| Date | Work | Evidence | Next |
|------|------|----------|------|
| 2026-10-04 | M1-S2 dialogue latency & cost: **PASS**; latency routing made the default; 22 §4.8 stop-sequence bug fixed | docs/spikes/s2-dialogue-latency.md; 240 turns, $0.054 | M1-S3 fast-decider bake-off |
| 2026-10-04 | **M0 gate 2 passed — M0 complete.** All 7 exit criteria re-run after M0-16–18; S4 moved to M1; M1 breakdown generated (6 spikes + 25 items) | CI + godot green at 7f4756e; gate evidence in the exit-criteria table | M1: start with M1-S2 / M1-S3 (latency, fast decider) and M1-01…03 (person model, DP propensities) |
| 2026-10-04 | M0-18: circuit breaker, AI_GATEWAY_MODE recorder, gateway DP routing (fast decider / policy, now) | 81 tests; live DP decided by the fast decider and replayed to ff6b7420c63dd0c1 | M0 gate (all items now [x] or [-]) |
| 2026-10-04 | M0-16 + M0-17: decision points in the sim (records, DRE, guards, policy decider, watchdog, integrity verification); M0-19 dropped (owner: `.env`) | 74 tests; mutation checks; dp-ping run + replay hash ea25ca3b19f19ee9 | M0-18 (breaker + recorder + gateway DP routing) |
| 2026-10-04 | M0-12 done (Terrain3D v1.0.2 vs ArrayMesh; 8 km in Terrain3D at GPU 4.46 ms / 301 MB), ADR-0009 accepted, M0-15 done (20 v0.2, R26). **M0 gate:** all 7 exit criteria re-verified, but the 22 §17.1 M0 gateway scope was missing from the breakdown → M0-16…19 added; M0 stays open | 49e5cc2; gate evidence in the exit-criteria table | M0-16 (DP records) → M0-17 (policy decider + watchdog) → M0-18 (breaker + recorder); owner: M0-19 deferral |
| 2026-10-04 | M0-14 done (`godot.yml` + Boot `--autotest`; Godot exit criterion verified); ADR-0010 step 2: .NET 10 SDK, MTP `dotnet test`, analyzers unpinned | CI + godot green at d04d2de and 2b7c10f; smoke hash unchanged 8bea5171cd1c0ad3 | Owner: Terrain3D download OK (or drop the comparison) → M0-12, ADR-0009 (M0-15), then the M0 gate |
| 2026-10-04 | M0-13 done: Godot embodiment spike (navmesh bodies for LOD0, puppets for LOD1, logged pose reports), `feudalsim replay`, `tools/godot/embodiment_check.sh` | Godot hash = replay hash 46c45ef7e2e8defe at step 698; 58 tests pass | M0-14 `godot.yml`; Godot pause/time-scale exit criterion |
| 2026-10-03 | M0 session 1: M0-02…10, M0-A2/A3/A5, M0-AU2 done; M0-14/15, M0-A4, M0-AU3 partial; 4 of 7 exit criteria verified. 57 tests; CI green; live OpenRouter round trip logged. Stopped: everything left needs Godot (or ffmpeg) | commits 86d7f0d…cbe376f | Owner: install Godot 4.7.2 .NET (`brew install --cask godot-mono`) and fix ffmpeg (Blockers); then `/advance-plan` resumes at M0-01 → M0-11 |
| 2026-10-03 | M0-A1: owner installed Git LFS; verified tracking and hooks | `git lfs track`, `git check-attr` | M0-01 (Godot .NET, ffmpeg, gh) |
| 2026-10-03 | Planning complete: 25 docs, canon v0.3 (decision points), art & audio production plan, `/advance-plan` skill, session goals | commits on `main` | Start M0-01 |
