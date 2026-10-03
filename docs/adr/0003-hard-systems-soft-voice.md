# ADR-0003 — Hard systems, soft voice: the LLM boundary

> **Status:** Proposed · **Date:** 2026-10-03
> **Related:** [canon §4.1, §13](../01-canon.md#13-the-llm-boundary-hard-systems-soft-voice), [22-llm-integration](../tech/22-llm-integration.md)

## Context

The vision wants "robust systems managing things behind the scenes but [to] give the illusion of
infinite choice and interaction using LLMs", with the LLM layer as "a facade for the real game
mechanics while still having real world impacts where it makes sense", and with an NPC's
"willingness to be swayed" being "more hard coded".

LLMs are slow (hundreds of ms to seconds), non-deterministic, costly at scale, and manipulable by
player text. Jev (TypeSafe's decision model) is fast and cheap but documented as weak at arithmetic
and dates and **not robust to injected instructions**.

## Options considered

| Option | Pros | Cons |
|--------|------|------|
| **LLM decides outcomes** (agentic NPCs) | Maximum apparent freedom | Exploitable by prompt injection; inconsistent; untestable; slow; breaks the economy |
| **LLM is cosmetic only** | Safe, deterministic | Words don't matter; violates "real world impacts" |
| **Hybrid: language → bounded signals → hard rules → LLM voices the result** | Words matter within limits; testable; robust to injection; graceful fallback | More plumbing: classification schema, rules engine, verification step |

## Decision

Adopt the hybrid ("hard systems, soft voice"):

- Player language is classified (Jev, or fallbacks) into **structured, bounded signals**.
- **Hard-coded rule functions** decide every outcome, using those signals as one input among many,
  clamped (default ±15% on prices/acceptance) and scaled by the listener's hard-coded susceptibility.
- The LLM **voices** the decided outcome in character, and a verification pass checks that the
  generated text doesn't contradict it.
- Player text is **untrusted data** everywhere it flows.
- Every LLM/Jev touchpoint has a fallback: local LLM structured output → heuristics/templates. The
  game remains completable in "template mode" with no LLM at all.

## Consequences

- We build a Dialogue Rules Engine, a signal schema, clamps, and a post-generation verifier.
- Designers tune outcomes in code/data, not prompts; prompts only shape voice.
- LLM outputs are recorded in the event log to preserve determinism on replay.

## Revisit if

- Playtests show words feel inconsequential (raise clamps or broaden signal types).
- Local models become fast and robust enough to safely take on more open-ended decisions in
  low-stakes domains.
