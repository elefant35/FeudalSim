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
- **Hard systems, soft voice (ADR-0003):** LLM/Jev output is never an outcome. It is a bounded,
  clamped input to hard-coded rules, or text that voices an already-decided outcome. Player text is
  untrusted data. Every LLM touchpoint needs a non-LLM fallback (template mode must stay playable).
- **Determinism (ADR-0002):** all randomness via seeded, keyed RNG streams; no `DateTime.Now`,
  `Random.Shared`, or unordered iteration in sim logic; LLM/Jev responses are recorded as inputs.
  Scope: bit-identical on the same build + OS + CPU architecture.
- **Parity:** player and NPCs use the same rules (skills, needs, laws, quality formulas).
- **Content is data:** items, recipes, skills, traits, crops, buildings live in `/content` YAML with
  schema validation, not hard-coded in C#.
- **Secrets:** keys live in `.env` (gitignored). Never commit keys, never write them to logs or saves.
- **Hot paths are allocation-free** (sim ticks and per-frame client code).

## Keeping the plan alive

- If an implementation decision changes a design, update the owning doc in the same change.
- Changing a canon number/name: edit `docs/01-canon.md` and add a change-log line.
- Architectural decisions get an ADR in `docs/adr/` (copy `0000-template.md`).
- New open questions go in the owning doc's "Open questions" section and in
  `docs/production/31-risks-and-open-questions.md`.

## Commands

To be filled in at M0 (build, test, headless sim run, content validation, launching the Godot client).
