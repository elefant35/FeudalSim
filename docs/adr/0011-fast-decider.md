# ADR-0011 — Fast decider for M1–M3: `qwen/qwen3.5-9b` log-probabilities on OpenRouter, latency-routed

> **Status:** Accepted · **Date:** 2026-10-04 · **Related:** [spike S3](../spikes/s3-fast-decider.md),
> [22 §3.2, §5, §17.2 #6](../tech/22-llm-integration.md), canon §4.1, [ADR-0003](0003-language-decides-systems-resolve.md)

## Context

The fast decider classifies the player's words (22 §5.2: ~12 questions per utterance) and makes quick
choices among a DP's pre-cleared options (22 §5.3, 0.5 s deadline). 22 §17.2 #6 requires a bake-off whose
results — accuracy, measured p50/p95 latency, ECE and cost — name the default for M1–M3. S3 measured the
candidates on a 103-line golden set (2026-10-04).

## Options considered

| Option | Pros | Cons |
|--------|------|------|
| **`openrouter-llm` · `qwen/qwen3.5-9b` · `provider.sort = latency`** | 88.2% `act`; ECE 0.068; injection 90% recall / 2.2% FPR; p50 391 ms, p95 613 ms; Core pack 465 ms in parallel; $0.000023 / question; works today | Cloud dependency; borderline accuracy on a small set; p95 above the 0.5 s quick-choice deadline; a call per question |
| Same model, default routing | 89.2% | p50 620 ms — fails the 500 ms condition |
| Laya zero-shot (local, GPU or CPU) | 61 ms/question on GPU; one call per pack; $0; Jev-shaped API | 67.7%; over-confident (ECE 0.225); 21.5% false injection flags; CPU pack 1.4 s — needs fine-tuning |
| `qwen/qwen3-30b-a3b-instruct-2507` | Strong instruct model | No log-probability endpoint for this account (HTTP 404) |
| Jev (TypeSafe API) | Native multi-question calls | No access yet |

## Decision

1. **Default for M1–M3:** `DECIDER_PROVIDER=openrouter-llm`, `DECIDER_MODEL=qwen/qwen3.5-9b`,
   **`DECIDER_PROVIDER_SORT=latency`** (now the code and `.env.example` default).
2. Quick-choice DPs keep the 0.5 s deadline; answers that miss it fall to the policy (by design). The gateway
   blends the decider's distribution with the policy prior and samples (22 §5.3), so no decider output alone
   can push a low-propensity option through.
3. **Laya is the local path:** collect M1+ decisions as training data; fine-tune and temperature-calibrate,
   then re-run S3's bench. Jev joins the bake-off if access arrives.

## Consequences

- The `heuristic` fallback, the breaker and "policy, now" (M0-18) cover outages; template mode never needs it.
- Accuracy must be re-measured on M1-16's larger golden suite; the first prompt fixes are the
  `accept_offer`/`trade_offer` and `command`/`request` glosses.
- Quick choices can be swayed by injected text (+0.33 mean on 4 red-team pairs); the injection gate (≥ 0.3 →
  policy decides the turn's DPs) is a required layer, and M1-16 red-teams the whole turn, not just the decider.
- Cost per typical play-hour stays within 22 §12's estimate (≈ $0.000023 per question measured).

## Revisit if

M1-16's golden suite puts `act` accuracy below 88%; quick-choice deadline expiries exceed 3% in play; a
fine-tuned Laya reaches ≥ 88% with ECE ≤ 0.08 (then it becomes the default for quick choices, locally);
Jev access is granted; or OpenRouter pricing/availability for `qwen3.5-9b` changes.
