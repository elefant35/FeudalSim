# CLAUDE.md — working agreement for AI-assisted development

This repo is **FeudalSim**, a single-player 3D medieval society simulation (Godot 4 .NET client +
pure C# simulation core + LLM/Jev "voice" layer). It is currently in **pre-production**; the plan
in `docs/` is the basis for all development.

## Read before working

1. `docs/01-canon.md` — binding decisions, numbers, names, scales, milestone IDs and the
   **document ownership map** (§16). Canon wins over every other doc.
2. The owning design/tech doc for whatever you are touching (see canon §16 or `docs/README.md`).
3. `docs/production/30-roadmap.md` — which milestone we are in and its exit criteria.

## Non-negotiable rules

- **Sim core purity:** `src/FeudalSim.Sim` must never reference Godot, any LLM provider, or any IO,
  network or YAML library. AI is reached through interfaces; the client talks to the sim only through
  commands and events. The sim never waits on a model.
- **Language decides, systems resolve (ADR-0003, canon §13):** an LLM or the fast decider may
  *choose* for a character, but only an option from a **decision-point menu** that a deterministic
  system built (fixed parameters, eligibility, base propensity, stakes). The DRE guards the choice;
  the owning system executes and resolves it (combat, trade, relationships, justice). Models never
  set numbers or resolve consequences, never run in real-time loops, and off-screen/headless
  decisions use the deterministic policy. Player text is untrusted data. Every decision has a policy
  fallback and every line a template (template mode must stay playable).
- **Determinism (ADR-0002):** all randomness via seeded, keyed RNG streams; no `DateTime.Now`,
  `Random.Shared`, or unordered iteration in sim logic; LLM/Jev responses are recorded as inputs.
  Scope: bit-identical on the same build + OS + CPU architecture.
- **Parity:** player and NPCs use the same rules (skills, needs, laws, quality formulas).
- **Content is data:** items, recipes, skills, traits, crops, buildings live in `/content` YAML with
  schema validation, not hard-coded in C#.
- **Secrets:** keys live in `.env` (gitignored); development uses `OPENROUTER_KEY`. Never commit keys,
  never print them, never write them to logs or saves.
- **Hot paths are allocation-free** (sim ticks and per-frame client code).

- **Assets:** every model, texture and sound has a manifest entry with its source and license
  (`docs/production/32-art-and-audio-production.md` §14); procedural assets are rebuilt from
  generator scripts in `art/generators/`; budgets in 32 §5 are enforced by checks. Blender runs
  headless at `/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P <script> -- <args>`.
  Only the owner approves an asset's look. Don't commit binaries until Git LFS is installed.

## Working on the plan

- **`/advance-plan`** (`.claude/skills/advance-plan/`) does one verified step of the current
  milestone: `/advance-plan`, `/advance-plan M0-04`, `/advance-plan gate`.
- **`/goal`** conditions for long sessions are in `docs/production/34-session-goals.md`.
- Progress, owner approvals and blockers live in `docs/production/33-progress.md`; keep it current.

## Keeping the plan alive

- If an implementation decision changes a design, update the owning doc in the same change.
- Changing a canon number/name: edit `docs/01-canon.md` and add a change-log line.
- Architectural decisions get an ADR in `docs/adr/` (copy `0000-template.md`).
- New open questions go in the owning doc's "Open questions" section and in
  `docs/production/31-risks-and-open-questions.md`.

## Commands

Run from the repo root. `global.json` pins the .NET **10** SDK (10.0.401) and opts `dotnet test` into
Microsoft.Testing.Platform; every project **targets net10.0** (ADR-0010 step 3: Godot 4.7.2 runs it), so only the
.NET 10 SDK is needed. Godot: `/Applications/Godot_mono.app/Contents/MacOS/Godot` (4.7.2 .NET).

```bash
dotnet build                                   # 0 warnings required (TreatWarningsAsErrors)
dotnet test                                    # all suites; Integration includes determinism + replay
dotnet test --project tests/FeudalSim.Sim.Tests --filter-class '*ClockTests'   # one project / class (xunit.v3 filters)
dotnet run --project src/FeudalSim.Headless -- content validate          # YAML → schema → compile
dotnet run --project src/FeudalSim.Headless -- content schemas [--check] # regenerate / verify JSON Schemas
dotnet run --project src/FeudalSim.Headless -- run --scenario content/scenarios/m0_smoke.yaml --verify-determinism [--threads 4]
dotnet run --project src/FeudalSim.Headless -- run --realtime --seconds 60   # SimRunner at 10 steps/s
dotnet run --project src/FeudalSim.Headless -- social --seeds 10 --days 30            # M1-22: deadlocks, emergent disputes, brawl rungs
dotnet run --project src/FeudalSim.Headless -- complete --seeds 10 [--verbose]   # 22 §13.3 golden goals in template mode (CI)
dotnet run --project src/FeudalSim.Headless -- ai ping                   # one chat completion (key from .env, never printed)
dotnet run --project src/FeudalSim.Headless -- ai decide                 # one fast-decider question (option probabilities)
dotnet run --project src/FeudalSim.Headless -- ai calibrate [--suite neutral,refusal,...] [--max-usd 1]   # 22 §15 calibration/refusal/red-team suites (live, ≈ $0.25)
dotnet run --project src/FeudalSim.Headless -- session --turns 40      # M1-23: live multi-settler session — latency per beat, cost per play-hour (≈ $0.013)
dotnet run --project src/FeudalSim.Headless -- run --scenario content/scenarios/m0_ai_ping.yaml --realtime --seconds 25   # live AI round trip, logged
dotnet run --project src/FeudalSim.Headless -- run --scenario content/scenarios/m0_dp_ping.yaml [--realtime --seconds 10]   # a decision point: policy at deadline / live fast decider
AI_GATEWAY_MODE=record LLM_LOG_TRANSCRIPTS=true dotnet run --project src/FeudalSim.Headless -- ai decide   # then AI_GATEWAY_MODE=replay: no network, no key
dotnet run --project src/FeudalSim.Headless -- log inputs|events <file.fslog> [--filter X]   # inspect logs
dotnet run --project src/FeudalSim.Headless -- replay --scenario <yaml> --log <inputs.fslog> --until-step N   # final hash of a recorded session
dotnet run --project tests/FeudalSim.Benchmarks -c Release -- --filter '*'
dotnet run -c Release --project src/FeudalSim.Headless -- bench --scenario content/scenarios/s6_mixed.yaml --warmup-days 1 --days 4   # 20 §19 step budgets (S6): per-system + slowest step
dotnet run --project src/FeudalSim.Headless -- weather [--seed N --years 3000]   # 10 §6.3 chain: shares vs the table (±3 points), beach temperatures
dotnet build game/FeudalSim.Game.csproj                                  # the Godot client (also builds Hosting/Sim)
$GODOT --path game                                                       # the M1 camp (m1_view), play mode: WASD walk · Shift jog · Ctrl sprint · [E] talk · [Esc] leave · Space pause · 1/2/4/8 speed; `-- --view` overhead
$GODOT --path game -- --scenario m1_overheard                            # any content/scenarios/<name>.yaml; `-- --shot out.png 30` saves a screenshot and quits
$GODOT --headless --path game -- --autotest                              # Boot smoke: movement, pause, time scale (exit 0/1)
$GODOT --headless --path game -- --autotest-camp                         # M1-18 play mode: bodies, walk to a settler, [E] talk, [Esc] leave (exit 0/1)
$GODOT --headless --path game -- --autotest-dialogue                     # M1-19 dialogue UI on top: echo, unsay, quick intent, People page (CI; add --live for the model)
python3 tools/audio/build_m1.py                                          # M1-21 placeholder audio: barks + dialogue UI sounds → check → manifest + mappings
python3 tools/art/build_kit.py [--previews]                              # M1-20 graybox kit: 40 assets → export → check → content/assets/graybox.yaml
tools/godot/embodiment_check.sh                                          # LOD0 bodies + headless replay of the client session
tools/godot/fetch_addons.sh                                              # pinned third-party addons (Terrain3D) → game/addons/ (SHA-256 checked)
$GODOT --path game res://scenes/dev/TerrainSpike.tscn -- --terrain3d [--samples 4097] [--autowalk] [--cache] [--traverse 150]   # terrain (ADR-0009; S4: cache + full-map traverse, 0 hitches > 50 ms)
```

Headless runs write `sim_runs/<id>/` (gitignored). CI (`.github/workflows/ci.yml`) runs build, tests,
content checks and the smoke run with `LLM_MODE=template`; `godot.yml` builds the game with the pinned
Godot (from `game/GODOT_VERSION`) and runs the two headless Godot checks above.
