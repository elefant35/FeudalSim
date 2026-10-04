# 34 — Session Goals (`/goal`)

> **Status:** v0.1 · **Owner doc for:** ready-to-paste `/goal` conditions for long-running Claude Code
> sessions · **Depends on:** [33-progress](33-progress.md), the `/advance-plan` skill
> (`.claude/skills/advance-plan/`), [30-roadmap](30-roadmap.md)

## How the two tools fit together

- **`/advance-plan`** (a project skill) does **one verified step**: picks the next work item from
  [33-progress](33-progress.md), implements it, verifies it, updates the docs and tracker, and
  commits. Use it on its own for short sessions: `/advance-plan`, `/advance-plan M0-04`,
  `/advance-plan gate`.
- **`/goal`** (built into Claude Code) keeps a **long session** running until a condition is met:
  after every turn a separate evaluator model checks the condition against the transcript; Claude
  keeps working while it's "not yet met", and the goal clears when it's met or judged impossible.
  The goals below tell Claude to loop `/advance-plan` and define "done" in terms the evaluator can
  see.

**Usage:** paste one goal (≤ 4,000 characters) at the start of a session. `/goal` with no argument
shows status; `/goal clear` stops it. Non-interactive: `claude -p "/goal …"`. One goal per session.
If a session is resumed (`--continue` / `--resume`), the condition comes back but its turn count and
timer reset — which is why every goal asks for evidence in the transcript and why the tracker, not
the conversation, holds progress between sessions. While background work runs, check-ins happen
every 30 minutes by default (`CLAUDE_CODE_GOAL_CHECKIN_MINUTES`).

**Before a long session:** fill in **Owner approvals** in [33-progress](33-progress.md) (installs,
pushing, spend) so Claude doesn't stop to ask for things you'd say yes to anyway.

---

## Goal A — Finish the current milestone (the main one)

**Expect a milestone to span several sessions** (M0 alone has ~24 work items). The 80-turn stop is a
per-session safety limit, not a failure: when a session stops, the tracker holds the state, and you
start the next session by pasting the same goal again — it picks up from the tracker.

```text
/goal Finish the current FeudalSim milestone, working through it with the /advance-plan skill one verified work item at a time. The current milestone, its work items, owner approvals and blockers are in docs/production/33-progress.md. The goal is met only when ALL of these are shown in this session's transcript: (1) every work item for the current milestone in 33-progress.md is marked [x] with evidence; (2) every exit criterion for that milestone in docs/production/30-roadmap.md section 5 was checked in this session, with its passing command output or measurement pasted in the transcript and recorded in the tracker; (3) as the final step, a fresh run of dotnet build, dotnet test, content validation and the headless determinism smoke run all pass, with their output shown; (4) 33-progress.md marks the milestone complete, names the next milestone and lists its work breakdown; (5) git status -sb shows a clean working tree, in sync with origin/main if pushing is approved in the tracker (otherwise everything committed locally). Stop early, record the reason under Blockers in 33-progress.md, and report if: an owner decision or approval is needed that the tracker does not grant; the same failure persists after three different fix attempts; LLM spend this session would exceed the cap approved in the tracker; or 80 turns have passed.
```

## Goal B — A bounded work session

```text
/goal Using the /advance-plan skill, complete and verify up to 5 work items from the current milestone in docs/production/33-progress.md (if the milestone runs out of items first, run /advance-plan gate instead). The goal is met when, for every item finished this session, the transcript shows its verification output and its commit, 33-progress.md marks it [x] with evidence and has a session-log entry, a final dotnet build and dotnet test pass with output shown, and git status -sb is clean. Stop early and report if an owner decision or approval is needed, the same problem survives three different fix attempts, LLM spend would exceed the tracker's cap, or 40 turns have passed.
```

## Goal C — One specific work item

Replace `<ID>` (e.g. `M0-10`).

```text
/goal Complete work item <ID> from docs/production/33-progress.md using the /advance-plan skill. The goal is met when each acceptance criterion for <ID> from its owning doc is demonstrated by command output shown in the transcript, 33-progress.md marks <ID> [x] with that evidence, dotnet build and dotnet test pass (output shown), and the change is committed. Stop and report if it needs an owner decision or approval the tracker does not grant, or after 25 turns.
```

## Goal D — An art or audio batch

Replace `<FAMILY>` (e.g. `M2 flora: the 8 tree families`, `M2 footsteps`).

```text
/goal Produce <FAMILY> as listed for the current milestone in docs/production/32-art-and-audio-production.md section 16 and docs/production/33-progress.md, using the /advance-plan skill and the pipeline in 32 (headless Blender at /Applications/Blender.app/Contents/MacOS/Blender for 3D; tools/audio for sound). The goal is met when every asset in the batch has a generator script or recorded source, passes tools/art/check.py or tools/audio/check.py with the output shown in the transcript, has a preview render you have looked at (3D) or a loudness report (audio), has a manifest entry with its license and status review, imports into Godot without warnings, and is committed with Git LFS. Stop and report if Git LFS is not installed and installs are not approved in the tracker, an asset needs an external source whose license is unclear, or 40 turns have passed.
```

## Goal E — A risk spike

Replace `<SPIKE>` (e.g. `S1 Crowd render`, `S3 Fast decider`, `S6 Sim scale`).

```text
/goal Run spike <SPIKE> from docs/production/30-roadmap.md section 4 to a clear answer. The goal is met when the transcript shows the measurements the spike's pass condition requires, a short write-up exists at docs/spikes/<spike-id>.md stating pass or fail with the numbers and the exact setup, 33-progress.md records the result, any ADR whose "Revisit if" clause the result triggers has been updated, and everything is committed. Stop and report if the spike needs hardware, accounts or spend that the tracker does not approve, or after 40 turns.
```

## Goal F — Overnight: as many milestones as possible

For unattended runs (the owner asleep). It keeps going across milestones instead of stopping at owner-only work: playtests,
asset approvals and undelegated choices are marked *awaiting owner* (with the doc's reversible default applied and
logged), and the session ends with a morning summary in the tracker. The full game won't fit in one night. Expect
a session to finish several M1 items, or a milestone at most, before a stop condition (turns, spend, blockers).
Paste the same goal again to continue; the tracker holds the state.

```text
/goal Develop FeudalSim milestone by milestone, M1 through M8, overnight and unattended, using the /advance-plan skill one verified work item at a time. Scope, order, approvals and blockers come from docs/production/33-progress.md; milestones and exit criteria from docs/production/30-roadmap.md; canon in docs/01-canon.md wins. Rules: (a) Every item is implemented, verified with command output shown in the transcript, recorded [x] with evidence in 33-progress.md, committed and pushed to origin/main (never force-push). (b) Never weaken a test, band, exit criterion or guard to make it pass; if a target looks wrong, record a calibration finding and an open question in the owning doc and 31-risks-and-open-questions.md. (c) Owner-only work (playtests with people, approving an asset's look, choices the tracker doesn't delegate): don't block on it. Mark the item 'awaiting owner' with what is needed. For a design choice, apply the doc's recommended default if it is reversible, record it under Owner decisions pending, and move on. (d) When a milestone's remaining items are all awaiting owner, run /advance-plan gate for everything else, mark the milestone 'complete except owner items' in the tracker, list the next milestone's breakdown, and continue with next-milestone items whose dependencies (30 section 3) are met. (e) Before each milestone transition, a fresh dotnet build, dotnet test, content validate, schemas check, the determinism smoke with --threads 4, the camp sweep and the Godot autotest all pass, with output shown. (f) Keep CLAUDE.md rules: sim purity, determinism, content as data, allocation-free hot paths, no keys printed or committed, assets with license entries, no binaries without Git LFS. (g) Use the advisor before each new milestone and before declaring a milestone done. The goal is met when 33-progress.md shows M8 complete (owner items aside) with the final checks passing in this session, or it stops early. Stop early, write a morning summary in 33-progress.md (done, awaiting owner, decisions taken on your behalf, spend, next step), commit, push, and report if: LLM spend this session would exceed $15 (keep the running tally in 33); the same failure survives three different fix attempts and nothing else can proceed; every remaining item is blocked on the owner; a needed install, account or purchase isn't approved in the tracker; git or CI is broken in a way you can't fix safely; or 600 turns have passed.
```

---

## Writing your own goals

- State **one measurable end state** and how it's checked ("`dotnet test` exits 0, output shown").
- Ask for **evidence in the transcript** — the evaluator only sees the conversation.
- Point at the **tracker** for scope so the goal text stays reusable across milestones.
- Always include **stop conditions**: owner decisions, repeated failures, spend, and a turn limit.
