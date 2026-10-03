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
| M0-05 | Determinism harness (`StateHasher`, fixed-chunk job runner) | [ ] | |
| M0-06 | Persistence v0 (snapshot + input log, save/load equivalence) | [ ] | |
| M0-07 | Content v0 (YAML → schema → compiled DB; the 28 canonical skills) | [ ] | |
| M0-08 | Headless CLI v0 (`run` a scenario, CSV metrics, `--verify-determinism`) | [ ] | |
| M0-09 | Hosting (sim thread, time scale, pause, snapshots, dev commands) | [ ] | |
| M0-10 | AI gateway v0: chat ping via OpenRouter (`OPENROUTER_KEY`), **fast-decider ping** (normalized option probabilities), template fallback, recording | [ ] | |
| M0-11 | Godot project boots `SimHost` with a debug overlay | [!] | Needs Godot (Blockers) |
| M0-12 | Test terrain spike (Terrain3D vs ArrayMesh) — feeds spike S4 | [!] | Needs Godot (Blockers) |
| M0-13 | Sim-driven capsule NPC with LOD0 ↔ LOD1 hysteresis; replay of a client session headless | [!] | Needs Godot (Blockers) |
| M0-14 | CI: `ci.yml` (ubuntu + macOS) and `godot.yml` | [ ] | |
| M0-15 | ADRs 0005–0010 written (data layout, time model, embodiment, saves, terrain, .NET version); `CLAUDE.md` commands filled in | [ ] | |

### Art & audio pipeline (32 §16)

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| M0-A1 | Git LFS installed and `git lfs install` run; `.gitattributes` patterns active | [x] | 2026-10-03: git-lfs 3.8.0 via Homebrew; repo hooks installed (pre-push, post-checkout, post-commit, post-merge); `git lfs track` lists all 15 patterns; `git check-attr` resolves `.glb/.png/.wav` to `filter: lfs` |
| M0-A2 | Palette v0 (`art/palettes/palette.png` + `palette.yaml`) | [ ] | |
| M0-A3 | `tools/art/export.py`, `check.py`, `preview.py` (headless Blender) | [ ] | Prototype scripts proven 2026-10-03 (pine → .glb → EEVEE preview) |
| M0-A4 | Test asset end-to-end: `art/generators/conifer` → `.glb` → Godot import → manifest entry | [ ] | |
| M0-A5 | Asset manifest schema (`content/assets/*.yaml`) + `ASSET_LICENSES.md` generation | [ ] | |
| M0-AU1 | `ffmpeg` installed | [ ] | |
| M0-AU2 | `tools/audio/synth.py` and `check.py` (format + EBU R128 loudness) | [ ] | |
| M0-AU3 | One SFX end-to-end into Godot via `content/audio_events.yaml` | [ ] | |

### Spikes started in M0

| ID | Item | Status | Evidence |
|----|------|--------|----------|
| S4 | Terrain streaming for 8,192 m (continues into M1) | [ ] | |

---

## M0 exit criteria (from [30 §5](30-roadmap.md#m0--foundations))

Verified only by running the command or test and pasting the result into **Evidence**.

| Criterion | Status | Evidence |
|-----------|--------|----------|
| `dotnet build` and `dotnet test` green in CI on every push | [ ] | |
| Headless runner: 1 game year, 24 agents, same seed → byte-identical event logs | [ ] | |
| Godot client shows a capsule moving by sim commands; pause and time scale work | [ ] | |
| A command-line call reaches OpenRouter and the response is recorded into the event log | [ ] | |
| Content validation fails CI on malformed YAML | [ ] | |
| All 15 steps of 20 §20 pass, including replay of a recorded client session | [ ] | |
| ADRs 0005–0010 written and accepted | [ ] | |

---

## Blockers (need the owner)

| Date | Item | Question / needed approval | Status |
|------|------|----------------------------|--------|
| 2026-10-03 | M0-01, M0-11–13, M0-A4, M0-AU3, godot.yml in M0-14 | **Install Godot 4.7.2 (.NET edition)** — latest stable; its GodotSharp targets net8.0, matching our SDK. Self-install: `brew install --cask godot-mono` (official `Godot_v4.7.2-stable_mono_macos.universal.zip`), or approve the install in Owner approvals and the next session will do it | open |

---

## Discovered work (not yet scheduled)

Items found while working that belong to a later milestone or need triage.

| Date | Item | Suggested milestone | Source |
|------|------|---------------------|--------|
| 2026-10-03 | Fine-tune Laya as the local fast decider from recorded decisions | M7 (data from M1) | canon §4.1 |
| 2026-10-03 | Apply for TypeSafe Jev API access for the S3 bake-off | M1 | 31 D28 |

---

## Session log

Newest first. One entry per session or work item: date, what changed, evidence, what's next.

| Date | Work | Evidence | Next |
|------|------|----------|------|
| 2026-10-03 | M0-A1: owner installed Git LFS; verified tracking and hooks | `git lfs track`, `git check-attr` | M0-01 (Godot .NET, ffmpeg, gh) |
| 2026-10-03 | Planning complete: 25 docs, canon v0.3 (decision points), art & audio production plan, `/advance-plan` skill, session goals | commits on `main` | Start M0-01 |
