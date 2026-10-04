# 33 — Progress Tracker

> **Living document.** Read and updated by the `/advance-plan` skill and by `/goal` sessions
> ([34-session-goals](34-session-goals.md)). Humans edit it too — especially **Owner approvals** and
> **Blockers**. Keep entries short; evidence is a commit hash, a command and its result, or a file.

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done (with evidence) · `[!]` blocked
(see Blockers) · `[-]` dropped (with reason)

---

## Current milestone

**M0 — Foundations** · started: 2026-10-03 · target exit: all items below `[x]` and the exit criteria in
[30-roadmap §5 (M0)](30-roadmap.md#m0--foundations) verified.

Next milestone: **M1 — Talking Camp** (work breakdown is generated when M0 closes).

---

## Owner approvals

Standing permissions the owner has granted for unattended sessions. The skill checks here before
doing anything in these categories; if a category isn't approved, it asks once and records the
answer.

| Category | Approved? | Notes |
|----------|-----------|-------|
| Install listed prerequisites with Homebrew (`git-lfs`, `ffmpeg`, `gh`; optional `sox`, `fluidsynth`) | **not yet** | Commands are in 20 §20 step 1 and 32 §2 |
| Install the pinned Godot 4.x .NET editor | **not yet** | The skill proposes the exact version first |
| Push to `origin/main` after a work item passes verification | **yes — proposed default, owner to confirm or change** | Never force-push |
| LLM spend from `OPENROUTER_KEY` during development | **yes, ≤ $2 per session** — owner approved using the key (2026-10-03); the $2 cap is a proposed default | Prefer replay/template mode in tests |
| Commit generated binary assets (requires Git LFS installed) | **yes** — Git LFS installed 2026-10-03 | — |
| Approve art "looks" (manifest status → `approved`) | **owner only** | Claude may set `review`, never `approved` |

---

## M0 — Foundations work breakdown

Source of truth for the steps: [20 §20](../tech/20-architecture.md#20-m0-foundations-checklist)
(acceptance criteria live there), [32 §16](32-art-and-audio-production.md#16-production-schedule-by-milestone),
[22 §17.1](../tech/22-llm-integration.md#171-by-milestone).

### Engineering (20 §20)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M0-01 | Prerequisites: pinned Godot 4.x .NET, .NET SDK check, `gh` (optional), remote set | [!] | .NET SDK 8.0.401 ✓; remote set ✓; Git LFS ✓; **Godot 4.7.2 .NET not installed** (see Blockers); `gh` optional, not installed |
| M0-02 | Scaffolding: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `BannedSymbols.txt`, `FeudalSim.sln` | [x] | `dotnet build` → 0 warnings, 0 errors. SDK 8.0.401 (Roslyn 4.11) forced pins: BannedApiAnalyzers 3.3.4, JsonSchema.Net.Generation 7.0.1, and removal of the transitive Humanizer analyzer (`Directory.Build.targets`) — input for ADR-0010 |
| M0-03 | Projects + architecture test (Sim references no engine/IO/network/YAML) | [x] | 5 src + 4 test projects; `dotnet test` → 5/5 pass (xunit.v3 4.0.1). Injected `HttpClient` in Sim → architecture test fails; injected `System.Random` → build error RS0030; both reverted |
| M0-04 | Sim kernel: ids, `SimClock`, calendar, `SimRandom`, phase pipeline, `People` table, commands/events, two toy systems | [x] | `dotnet test` → 31/31 pass: calendar (32-day year, 8-day seasons, Hearthday, day-of-year 1–32), 18,000 steps = 1 game day at 30 min, all 11 day lengths give whole game-ms/step (found & fixed: 25/50-min days have fractional ratios), invalid day lengths rejected (clock + logged command), RNG key reproducibility, salt uniqueness, spawn/wander/needs. `StateHasher` (XxHash64, canonical order) included for M0-05 |
| M0-05 | Determinism harness (`StateHasher`, fixed-chunk job runner) | [x] | Integration tests (stable over 4 runs): same seed twice × 10,000 steps × 300 people → equal hashes; `JobRunner` 1 vs 4 threads → equal; different seeds → different; injected hash-code-dependent ordering → mismatch caught. Note: .NET Dictionary/HashSet enumerate in insertion order until removals, so the injected bug orders by identity hash instead |
| M0-06 | Persistence v0 (snapshot + input log, save/load equivalence) | [x] | Tests: 5,000 + atomic save/load + 5,000 steps = 10,000 straight (hash + event seq); missing column → default, unknown column dropped with warning; torn log tail (−3 B) → 9 good records recovered, file truncated, append continues; layout-fingerprint test. Benchmark (1,500 people, M-series, .NET 8.0.8): MessagePack 56 µs vs MemoryPack 57 µs serialize, 33 vs 26 µs deserialize; MessagePack+LZ4 141 µs / 59 KB vs 203 KB raw → keep MessagePack + LZ4 (ADR-0008). v0 is one LZ4 MessagePack document; the TOC/chunk file layout of 20 §9.4 is deferred to M3 (saves are dev artifacts until then) |
| M0-07 | Content v0 (YAML → schema → compiled DB; the 28 canonical skills) | [x] | `feudalsim content validate` → "OK — 5 files, 28 skills, 9 needs, 14 items; hash ea0b142a35c1cf2e". Broken fixture → `items/bad.yaml:10:17: /1/base_value_f: Value is "string" but should be "integer"`. `content schemas --check` → up to date. 8 content tests (canon skill/need lists, handle order, prefix, duplicates, hash stability, schema freshness). Replaced JsonSchema.Net.Generation with a small deterministic generator (removed the Humanizer-analyzer workaround) |
| M0-08 | Headless CLI v0 (`run` a scenario, CSV metrics, `--verify-determinism`) | [x] | `dotnet run --project src/FeudalSim.Headless -- run --scenario content/scenarios/m0_smoke.yaml --verify-determinism` → exit 0; "determinism: rerun c2c49b7f89566174, 1-thread c2c49b7f89566174 → IDENTICAL"; 54,000 steps in 0.15 s (~350k steps/s); writes `sim_runs/<id>/metrics_daily.csv`, `run.json` (final + content hash), `inputs.fslog`. NeedsDecay now reads rates from content |
| M0-09 | Hosting (sim thread, time scale, pause, snapshots, dev commands) | [x] | `feudalsim run --realtime --seconds 60` → "604 steps in 60.4 s = 10.00 steps/s (target 10.0 ± 0.2) → OK; dilation events 0". Tests: triple buffer 200k writes under contention → 0 torn, 0 backwards; pause/step/resume via dev commands; 9 dev commands (pause, resume, step, timescale, daylength, spawn, hash, time, stats); snapshots + event ring reach presentation. Fixed: runner command Seq now continues from the world's (persisted) last Seq |
| M0-10 | AI gateway v0: chat ping via OpenRouter (`OPENROUTER_KEY`), **fast-decider ping** (normalized option probabilities), template fallback, recording | [x] | `feudalsim ai ping` → "key: set · qwen/qwen3-14b via openrouter · reply: Good morning, traveler—welcome to our humble village… · 9578 ms · 56/27 tokens · $0.000013". `feudalsim ai decide` → qwen/qwen3.5-9b: A 0.001 · **B 0.834** · C 0.164 · D 0.001, sum 1.00, 655 ms, $0.000013. Tests: key never printed (scan) but used; no key → template/heuristic; no logprobs → `decider: unavailable`; 3 s fake provider vs 10-step deadline → fallback at step 12, late result rejected, headless replay reproduces identical events + hash. **Found:** OpenRouter `reasoning.enabled=false` is ignored by DeepInfra for qwen3-14b (empty replies) → gateway appends Qwen's `/no_think` |
| M0-11 | Godot project boots `SimHost` with a debug overlay | [!] | Needs Godot (Blockers) |
| M0-12 | Test terrain spike (Terrain3D vs ArrayMesh) — feeds spike S4 | [!] | Needs Godot (Blockers) |
| M0-13 | Sim-driven capsule NPC with LOD0 ↔ LOD1 hysteresis; replay of a client session headless | [!] | Needs Godot (Blockers) |
| M0-14 | CI: `ci.yml` (ubuntu + macOS) and `godot.yml` | [~] | `ci.yml` **green on main** (run 37162967313 @ eb61a11): ubuntu + macOS build (warnings as errors), tests, content validate, schemas fresh, smoke run 1 vs 4 threads. Injected unused variable → CS0219 fails the build. **`godot.yml` blocked on Godot** |
| M0-15 | ADRs 0005–0010 written (data layout, time model, embodiment, saves, terrain, .NET version); `CLAUDE.md` commands filled in | [~] | ADRs 0005–0008 and 0010 Accepted with M0 evidence; **0009 (terrain) Proposed, pending the M0-12 spike** (needs Godot). `CLAUDE.md` Commands filled in. Remaining: terrain spike results into ADR-0009 and 20 v0.2 |

### Art & audio pipeline (32 §16)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M0-A1 | Git LFS installed and `git lfs install` run; `.gitattributes` patterns active | [x] | 2026-10-03: git-lfs 3.8.0 via Homebrew; repo hooks installed (pre-push, post-checkout, post-commit, post-merge); `git lfs track` lists all 15 patterns; `git check-attr` resolves `.glb/.png/.wav` to `filter: lfs` |
| M0-A2 | Palette v0 (`art/palettes/palette.png` + `palette.yaml`) | [x] | `tools/art/make_palette.py` → 64 named swatches in 8 rows (earth, foliage, stone/metal, skin/hair, cloth, culture accents, water/sky, special) as `palette.png` (256², LFS) + `palette.json` (machine-readable; replaces the planned YAML so Blender's Python can read it) |
| M0-A3 | `tools/art/export.py`, `check.py`, `preview.py` (headless Blender) | [x] | `export.py` runs a generator → `.glb`; `check.py` → `RESULT {ok: true, tris {0: 56, 1: 24}, materials [palette], height 4.23 m, problems []}` against `budgets.json` (32 §5); `preview.py` → 2×2 turntable + 40 m silhouette (reviewed: palette colours correct, silhouette reads) |
| M0-A4 | Test asset end-to-end: `art/generators/conifer` → `.glb` → Godot import → manifest entry | [~] | Generator → `game/assets/flora/pine_a.glb` (LFS) → checks pass → preview reviewed → manifest `asset.flora.pine_a` (status review). **Godot import pending (Blockers)** |
| M0-A5 | Asset manifest schema (`content/assets/*.yaml`) + `ASSET_LICENSES.md` generation | [x] | `AssetDef` kind in the content pipeline (schema generated, outputs + generator must exist, attribution needs a credit); `feudalsim content licenses [--check]` → `ASSET_LICENSES.md`; CI checks freshness; test: a missing output fails validation |
| M0-AU1 | `ffmpeg` installed | [!] | `brew install ffmpeg` failed on macOS 14 (no bottles; source build of x265 died at `libtool … is not an object file` — Homebrew suggests updating Command Line Tools to Xcode 16.2's). See Blockers. Audio checks fall back to a numpy EBU R128 implementation meanwhile |
| M0-AU2 | `tools/audio/synth.py` and `check.py` (format + EBU R128 loudness) | [x] | `synth.py` (deterministic numpy recipes: knap_flake, fire_crackle, ui_click → 48 kHz 16-bit WAV at −1 dBFS); `check.py` (naming, 48 kHz, 16-bit, mono for 3D, peak, LUFS with bus targets). Numpy BS.1770 meter validated: 1 kHz full-scale sine → −3.00 LUFS (ref −3.01), amplitude 0.1 → −23.00 (ref −23.01). 4 SFX pass |
| M0-AU3 | One SFX end-to-end into Godot via `content/audio_events.yaml` | [~] | `audio` content kind (`content/audio/events.yaml`: bus, files, spatial, jitter, loop; files must exist) + manifest entries for the 4 SFX; validated in the pipeline. **Godot playback pending (Blockers)** |

### Spikes started in M0

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| S4 | Terrain streaming for 8,192 m (continues into M1) | [ ] | |

---

## M0 exit criteria (from [30 §5](30-roadmap.md#m0--foundations))

Verified only by running the command or test and pasting the result into **Evidence**.

| Criterion | Status | Evidence |
|-----------|--------|----------|
| `dotnet build` and `dotnet test` green in CI on every push | [x] | CI (ubuntu + macOS) green on main since eb61a11 (run 37162967313); runs on every push and PR |
| Headless runner: 1 game year, 24 agents, same seed → byte-identical event logs | [x] | `run --days 32 --write-events` twice (separate processes): 576,000 steps each; `events.fslog` SHA-256 b692b44d…a45a6f both; `inputs.fslog` c8aa7c24…254c78 both; final hash 3d3de5abbf9f5d09 both |
| Godot client shows a capsule moving by sim commands; pause and time scale work | [!] | Needs Godot (Blockers). Sim side ready: SimRunner pause/time scale tested (M0-09) |
| A command-line call reaches OpenRouter and the response is recorded into the event log | [x] | `run --scenario content/scenarios/m0_ai_ping.yaml --realtime --seconds 25` → `log inputs … --filter AiResult`: "seq 25 @step 29 [Ai] AiResultCommand { Outcome = Ok, Text = Good morrow, friend…, ProviderTag = openrouter, LatencyMs = 813 }"; events log shows AiResultApplied without fallback |
| Content validation fails CI on malformed YAML | [x] | CI runs `content validate`, which exits 1 on any error; the broken fixture fails with `items/bad.yaml:10:17` (M0-07). Schema and license freshness are also checked in CI |
| All 15 steps of 20 §20 pass, including replay of a recorded client session | [!] | 10 of 15 done; steps 1, 11, 12, 13 (and godot.yml in 14, the terrain ADR in 15) need Godot. Headless replay of a recorded runner session already passes (M0-10 test) |
| ADRs 0005–0010 written and accepted | [~] | 0005–0008, 0010 accepted; 0009 (terrain) proposed until the M0-12 spike |

---

## Blockers (need the owner)

| Date | Item | Question / needed approval | Status |
|------|------|----------------------------|--------|
| 2026-10-03 | M0-01, M0-11–13, M0-A4, M0-AU3, godot.yml in M0-14 | **Install Godot 4.7.2 (.NET edition)** — latest stable; its GodotSharp targets net8.0, matching our SDK. Self-install: `brew install --cask godot-mono` (official `Godot_v4.7.2-stable_mono_macos.universal.zip`), or approve the install in Owner approvals and the next session will do it | open |
| 2026-10-03 | M0-AU1 (and OGG encoding later) | **ffmpeg didn't install** (Homebrew source build failed in x265 on macOS 14). Options: (a) update Command Line Tools (`sudo rm -rf /Library/Developer/CommandLineTools && sudo xcode-select --install`, or Software Update) then `brew install ffmpeg` again; (b) install a prebuilt static arm64 ffmpeg binary onto your PATH; (c) MacPorts (`sudo port install ffmpeg`). Not blocking until music/ambience needs OGG | open |

---

## Discovered work (not yet scheduled)

Items found while working that belong to a later milestone or need triage.

| Date | Item | Suggested milestone | Source |
|------|------|---------------------|--------|
| 2026-10-03 | Fine-tune Laya as the local fast decider from recorded decisions | M7 (data from M1) | canon §4.1 |
| 2026-10-03 | Apply for TypeSafe Jev API access for the S3 bake-off | M1 | 31 D28 |
| 2026-10-03 | Dialogue latency varied 3.9–9.6 s for qwen/qwen3-14b on OpenRouter (provider routing); S2 must pin fast providers or pick another model | M1 (S2) | M0-10 |
| 2026-10-03 | Persist pending AI requests and the AI request counter in saves | M1 | M0-10 |
| 2026-10-03 | Full 20 §9.4 snapshot layout (header + TOC + per-chunk hashes) | M3 | M0-06 |

---

## Session log

Newest first. One entry per session or work item: date, what changed, evidence, what's next.

| Date | Work | Evidence | Next |
|------|------|----------|------|
| 2026-10-03 | M0 session 1: M0-02…10, M0-A2/A3/A5, M0-AU2 done; M0-14/15, M0-A4, M0-AU3 partial; 4 of 7 exit criteria verified. 57 tests; CI green; live OpenRouter round trip logged. Stopped: everything left needs Godot (or ffmpeg) | commits 86d7f0d…cbe376f | Owner: install Godot 4.7.2 .NET (`brew install --cask godot-mono`) and fix ffmpeg (Blockers); then `/advance-plan` resumes at M0-01 → M0-11 |
| 2026-10-03 | M0-A1: owner installed Git LFS; verified tracking and hooks | `git lfs track`, `git check-attr` | M0-01 (Godot .NET, ffmpeg, gh) |
| 2026-10-03 | Planning complete: 25 docs, canon v0.3 (decision points), art & audio production plan, `/advance-plan` skill, session goals | commits on `main` | Start M0-01 |
