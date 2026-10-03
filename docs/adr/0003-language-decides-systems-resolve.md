# ADR-0003 — Language decides, systems resolve: the LLM boundary

> **Status:** Accepted (revised per owner direction, 2026-10-03) · **Date:** 2026-10-03
> **Related:** [canon §4.1, §13](../01-canon.md#13-the-llm-boundary-language-decides-systems-resolve), [22-llm-integration](../tech/22-llm-integration.md), [21-npc-ai](../tech/21-npc-ai.md)

## Context

The vision wants "robust systems managing things behind the scenes but [to] give the illusion of
infinite choice and interaction using LLMs", with the LLM layer as "a facade for the real game
mechanics while still having real world impacts where it makes sense", and with an NPC's
"willingness to be swayed" being "more hard coded".

The first version of this ADR ("hard systems, soft voice") let LLMs only voice outcomes and turn
player language into bounded signals; hard-coded functions decided everything. On review, the owner
asked for more agency: **LLMs should be able to make decisions** — build up relationships, start or
prevent fights, barter, and similar — **while the systems those decisions run on stay
deterministic**: "a player can start a fight with words, but settle them with a combat system. They
can convince someone to buy something, but the trade is enacted by a trading system."

Constraints that still hold: LLMs are slow (hundreds of ms to seconds), non-deterministic, costly at
scale, prone to agreeing with whoever is talking to them (sycophancy), and manipulable by player text.
Jev (TypeSafe's decision model) is fast and cheap but is documented as **not robust to injected
instructions**, and as of 2026-10-03 isn't reachable through OpenRouter as a decision model.

## Options considered

| Option | Pros | Cons |
|--------|------|------|
| **LLM decides and acts freely** (agentic NPCs with open tool use) | Maximum apparent freedom | Prompt-injectable; inconsistent; untestable; can break the economy and physics; sim stalls on model latency |
| **LLM voices only** (v1 of this ADR) | Safe, deterministic, testable | Characters have no agency of their own; the owner wants more |
| **Decision points: systems offer the menu, a model picks, systems resolve** | Real choices with real consequences; every option, number and resolution is deterministic; guards stop exploits; deterministic fallback for every choice | More plumbing: menus, propensities, guards, calibration against the policy |

## Decision

Adopt **decision points** (canon §13):

1. A deterministic system opens a **decision point** when a character must choose in a
   language-driven moment, and builds the **menu**: options with fixed parameters, eligibility, a base
   propensity (what the deterministic policy would do), and a stakes level. The menu's width encodes
   the hard-coded willingness to be swayed; the speaker's skill widens it.
2. A **decider** chooses: the LLM (decision-first, bundled with its reply), the fast decider (a small
   model reading log-probabilities over fixed option labels now; Jev later), or the deterministic
   **policy** (seeded sampling from the propensities).
3. The DRE **guards** the choice for feasibility and bounds — on the menu, eligible, above a low
   anti-exploit floor, within a daily long-shot budget — and critical options also need a
   deterministic propensity that never saw the player's text. Guards stop exploits; they don't
   overrule character.
4. The **owning system executes and resolves** the choice deterministically (combat, trade,
   relationships, obligations, justice).
5. Every choice is **recorded as an input event**; the sim never waits on a model.
6. LLM decisions happen only where an LLM is already in the loop (conversations and attended
   scenes). Off-screen life, Interludes, headless runs and real-time loops use the policy; NPC↔NPC
   exchanges the player overhears are policy-decided and only rendered by the LLM.

## Consequences

- Characters get real agency: an NPC can decide to swing at you, walk away from a deal, warm to you
  over a long talk, or step in to stop a fight — and the game's systems carry it out.
- We build: menu builders in each owning system, base-propensity functions (most already exist as the
  old outcome functions), the guard layer, decision-first prompting, and calibration tooling that
  compares LLM choices with the policy (sycophancy becomes a measured risk with an M1 exit criterion).
- Designers still tune outcomes in code and data: they control which options exist, their parameters
  and their propensities; prompts shape voice and judgment within that space.
- Template mode stays fully playable: the policy can make every decision.

## Revisit if

- Calibration shows LLM choices drifting from the policy beyond the agreed band and prompt fixes
  don't close the gap (tighten menus or floors).
- Playtests show characters feel railroaded (widen menus, lower floors) or exploitable (raise
  floors, cut the long-shot budget).
- Jev or local models make the fast decider good enough to take over more LLM-in-reply decisions.

## Revision history

- **2026-10-03 v1 — "Hard systems, soft voice."** LLMs voiced outcomes and produced bounded signals
  (±15% clamp); hard-coded functions decided all outcomes.
- **2026-10-03 v2 — "Language decides, systems resolve."** Owner direction: LLMs may decide among
  system-offered options; systems stay deterministic. The ±15% clamp became the menu's width.
