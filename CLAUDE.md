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

To be filled in at M0 (build, test, headless sim run, content validation, launching the Godot client).
