---
name: advance-plan
description: Advance FeudalSim's development plan by one verified step — pick the next work item for the current milestone from docs/production/33-progress.md, implement it (code, content, 3D/audio assets, docs), verify it, update the tracker and owning docs, and commit. Use whenever asked to continue, advance, or work on the game, the plan, the current milestone, or a specific work item id.
argument-hint: "[work-item id | next | gate | free-text focus]"
---

# Advance the FeudalSim plan

You are continuing development of **FeudalSim** from its written plan. Each invocation completes
**one coherent, verified step** (one work item, or a milestone gate) and leaves the repo, the docs and
the tracker consistent. Arguments: `$ARGUMENTS`
(empty or `next` = pick the next item · a work-item id like `M0-04` = do that item · `gate` = run the
current milestone's exit-criteria check · anything else = a focus hint for choosing the item).

Detailed checklists, commands and conventions are in [reference.md](reference.md).

## 1. Orient (always, briefly)

1. Read `CLAUDE.md` (non-negotiable rules) and `docs/production/33-progress.md` (current milestone,
   in-progress items, **Owner approvals**, **Blockers**).
2. `git status -sb` and `git log --oneline -5`. If the tree is dirty with someone else's
   unfinished work, stop and ask before touching it.
3. Note which canon sections and owning docs the item touches (`docs/01-canon.md` §16 ownership
   map). Read only the sections you need — the docs are long.

## 2. Choose the work item

- An explicit id in the arguments wins (if it is blocked, say why and stop).
- Otherwise: resume the item marked `[~]`; else take the first `[ ]` item of the current milestone
  whose prerequisites are done, preferring items that **retire risk** (spikes, the LLM/decision-point
  pipeline, scale) over polish.
- If every item is `[x]`, run the **milestone gate** (§6) instead.
- **Never start work for a later milestone** while the current one has open items or unverified
  exit criteria — record such ideas under "Discovered work".
- If the item needs something the owner hasn't approved (see Owner approvals: installs, art
  approval, spend), or an open decision in `docs/production/31-risks-and-open-questions.md` §2,
  add a row to **Blockers**, mark the item `[!]`, tell the owner exactly what you need, and pick
  another unblocked item if there is one.

Mark the chosen item `[~]` in the tracker before you start.

## 3. Plan the step

- Pull the acceptance criteria from the owning doc (for M0: `docs/tech/20-architecture.md` §20; for
  art/audio: `docs/production/32-art-and-audio-production.md`; for features: the doc's milestone
  section and its numbers). Canon numbers are binding — don't invent alternatives.
- State the plan in a few lines: files to create/change, tests to write, how you'll verify, which
  docs need updating. If the item is bigger than one session, split it in the tracker
  (`M0-04a`, `M0-04b` …) and do the first part.

## 4. Implement

Follow `CLAUDE.md`. The rules that most often matter:

- **Sim purity:** `src/FeudalSim.Sim` references no Godot, IO, network, YAML or LLM code; the client
  talks to the sim only through commands and events; the sim never waits on a model.
- **Determinism:** seeded keyed RNG only; no wall-clock time, `Random.Shared`, `GetHashCode`, or
  unordered iteration in sim logic; record LLM/decider results as input events.
- **Language decides, systems resolve:** LLM or fast-decider choices only pick from
  decision-point menus built by deterministic systems; the owning system executes; every decision
  has a policy fallback and every line a template.
- **Parity, content as data, allocation-free hot paths, secrets** — as in `CLAUDE.md`.
- **Tests first** where practical (xUnit); every system contributes headless metrics.
- **LLM calls during development:** use `OPENROUTER_KEY` from `.env` — never print, log or commit
  it. Prefer `AI_GATEWAY_MODE=replay` or `LLM_MODE=template` in tests; stay within the session
  spend approval in the tracker.
- **3D assets:** follow 32 §6 using headless Blender
  (`/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P <script> -- <args>`):
  generator → palette → LODs/collision → export `.glb` → `tools/art/check.py` → preview render →
  **look at the preview yourself** (Read the PNG) → manifest entry with `status: review`. Only the
  owner sets `approved`. Don't commit binaries unless Git LFS is installed (`git lfs version`).
- **Audio:** follow 32 §12 (48 kHz; mono WAV for 3D SFX; OGG for music/ambience; loudness targets;
  `content/audio_events.yaml` mapping; license recorded in the manifest).
- **External assets** (CC0 packs, libraries, AI services) only with an acceptable license recorded in
  the manifest (32 §14). When in doubt, don't.

## 5. Verify, document, commit

1. **Verify** with real commands and show their output: build, tests, content validation, headless
   smoke run, and the item's own acceptance check (see reference.md §1). Never mark an item done on
   "should work". If something fails three different ways, stop, record a blocker with what you
   tried, and report.
2. **Docs stay true:** if the implementation changed a design decision, update the owning doc in the
   same commit; canon value changes also get a line in the canon change log; architecture changes
   get an ADR (`docs/adr/0000-template.md`). New open questions go in the owning doc and 31.
3. **Tracker:** mark the item `[x]` with evidence (commit hash, command + result), add any
   discovered work, and add a one-line session-log entry with "Next".
4. **Binary guard:** before *any* commit, check whether the staged files include binaries (`.png`,
   `.glb`, `.blend`, `.wav`, `.ogg`, … — the patterns in `.gitattributes`). If they do, run
   `git lfs version`; if Git LFS isn't installed, unstage the binaries, record a blocker
   (`brew install git-lfs && git lfs install`), and commit only the text files.
5. **Commit** with a conventional message (`feat(sim): …`, `test(content): …`, `art(flora): …`,
   `docs(plan): …`) and the session's attribution trailer. Push to `origin/main` only if the tracker's
   Owner approvals say pushing is approved; never force-push.

## 6. Milestone gate (`gate`, or when every item is `[x]`)

1. For each exit criterion in `docs/production/30-roadmap.md` §5 for the current milestone, run the
   check and paste the evidence into the tracker's exit-criteria table.
2. If all pass: mark the milestone complete in the tracker, set **Current milestone** to the next
   one, and generate its work breakdown from 30 §5 (in scope + exit criteria), each owning doc's
   milestone section, and 32 §16 (art/audio). Keep items small (≈ ½–2 days each) with ids
   (`M1-01` …).
3. If any fail: add work items for the gaps and continue with those.

## 7. Report

End with a short report: the item, what changed (files), verification evidence, docs updated,
the commit, the next item, and any blockers or owner decisions needed.
