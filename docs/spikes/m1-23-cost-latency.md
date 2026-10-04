# M1-23 — Cost and latency report (22 §17.2 #1, #8)

**Date:** 2026-10-04 · **Command:** `feudalsim session --turns 40` (live; key from `.env`) · **Models:** dialogue
`qwen/qwen3-14b`, fast decider `qwen/qwen3.5-9b` (latency routing), prices of 22 §12.1 · **Scenario:** `m1_talk`
(the camp's evening gathering, Y0 Spring 1 19:30) · **Spend:** $0.026 for two runs.

## Method

One scripted player walks between settlers (5 turns each) and says 20 typical lines in rotation (greetings,
questions, requests, thanks, a tell, an apology, an insult, a promise) through the whole M1 pipeline in real time:
`SimRunner` at 10 steps/s, the AI gateway (overheard talk), `DialogueHost` (sanitize → 12-question classification →
DPs → decision-first reply → Tier A/B → regeneration → template). Each turn waits for its line, then 2.1 s (the turn
limit). Timing is measured from the moment the player's line is submitted; costs come from the providers' reported
usage. Background spend (overheard renders) is measured per wall-clock hour of the running camp.

## Results (two runs of 40 turns)

| Metric | Run 1 | Run 2 | Target (22 §12.4 / §17.2) | |
|--------|-------|-------|---------------------------|---|
| Classification p50 / p95 | 0.73 / 1.89 s | 0.91 / 1.63 s | ≤ 0.3 / ≤ 0.7 s | **FAIL** |
| Decision gesture p50 / p95 | 1.48 / 2.56 s | 1.59 / 2.32 s | ≤ 1.1 / ≤ 2.5 s | **FAIL** (p50) |
| First words p50 / p95 | 1.70 / 3.04 s | 1.90 / 3.03 s | Tier A ≤ 1.2 / ≤ 3.0 s | **FAIL** |
| Whole line p50 / p95 | 1.83 / 3.04 s | 2.03 / 3.27 s | — | |
| DP deadline expiries | 2.5% | 3.4% | < 3% | borderline |
| Lines | 26 voiced + 14 lost to the harness* | llm 35 · regenerated 3 · template 2 | template fallback < 4% (cloud healthy) | 5% |
| **Cost per turn** | $0.00031 | $0.00031 | model ≈ $0.00066 | |
| Classification share | $0.00018 | $0.00018 | | |
| Background | $0.0116 / h | $0.0122 / h | | |
| **Light hour (15 turns)** | $0.016 | $0.017 | ≤ $0.05 | **PASS** |
| **Typical hour (40 turns)** | $0.024 | $0.025 | ≤ $0.05 | **PASS** (2× margin) |
| **Heavy hour (120 turns)** | $0.048 | $0.050 | ≤ $0.10 | **PASS** (2× margin) |

\* Run 1's harness stopped waiting when a conversation closed; run 2 waits for the closing line (every turn voiced).

## Findings

1. **Cost is well inside the targets** — a turn costs half of 22 §12.2's model (shorter prompts than the 2,290-token
   estimate; verification on fewer turns). Classification is 58% of a turn's cost.
2. **Latency misses the M1 targets, and classification is the cause.** The 12 parallel logprob questions take the
   slowest call's time (p50 0.7–0.9 s; S3 measured 391 ms per call and 465 ms for 7 in parallel); the decision header
   then adds ≈ 0.7 s and first words ≈ 0.2–0.3 s. Levers, in 22 §12.3's order: fewer questions on the critical path
   (act + injection first, scores after the gesture), the single-call multi-answer variant (§17.2), Laya local (M4),
   pinning faster providers, and overlapping classification with the dialogue call's prompt prefill.
3. Late in the evening settlers close conversations to go to bed (`ended: npc` every turn after 21:00) — correct
   behaviour, and the reason a harness must wait for the closing line.
4. Two lines failed `unknown_names` because settlers are still called "Settler N" (discovered work: real names).

## Disposition

M1-23's cost criterion (22 §17.2 #8) **passes**. The latency criterion (#1) **fails** and goes to the M1 gate as an
iteration item (22 §17.2: "if criteria 1 … fail, M1 iterates"); re-measure with this command after each lever.
