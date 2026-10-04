# ADR-0012 — Local LLM: early look and the M1 position

> **Status:** Accepted · **Date:** 2026-10-04

## Context

22 §14 plans a local-first option (llama.cpp sidecar by default, LM Studio / Ollama supported) shipping at M7;
22 §17.2 #13 asks M1 to record a local spike's findings in an ADR: TTFT (target ≤ 1.5 s p50 on turns 2+ with a warm
cache), header latency, Laya CPU latency and frame-time impact. Spike S5 measured `qwen/qwen3-8b` MLX 4-bit in LM
Studio on an M3 Pro beside the M1 camp view ([s5-local-llm](../spikes/s5-local-llm.md)).

## Options considered

| Option | Pros | Cons |
|--------|------|------|
| A. Cloud default, local as an opt-in profile from M7 (22 §14 as written) | Meets cloud cost/latency now; local work waits for the prompt/caching design to settle | Local stays unmeasured in play until M7 |
| B. Make local the default for M1–M3 development | $0 dialogue; no network | TTFT p50 1.8 s, first words 3.4 s, cold turns 5 s; one-slot cache eviction; decisions uncalibrated |
| C. Drop local | Simpler | Contradicts canon's offline/no-subscription promise |

## Decision

A. Cloud remains the default. Local is a supported profile through the same OpenAI-compatible client
(`LLM_BASE_URL`, `LLM_API_KEY`, `LLM_TIMEOUT_TTFT_MS`), measured with `feudalsim session`. Measured on the reference
Mac: TTFT p50/p95 **1.77 / 2.41 s** beside the client (target 1.5 / 3.0), warm-prefix TTFT 0.59 s, cold 5.0 s, the
client **59.6 fps** (0.53% frames slower than 55 fps). Header latency with grammar-constrained `CHOICE` and Laya CPU
latency were not measured (llama.cpp and Laya are not installed); they stay open for the M7 local plan.

## Consequences

- Local profiles need a TTFT cut of ≈ 5–8 s (now honored from config) and per-conversation prompt-cache pinning
  so overheard talk and regenerations don't evict the dialogue prefix.
- The frame-time result is for a graybox scene; it must be re-measured with M2 art and crowds.

## Revisit if

- A warm-cache local profile reaches TTFT p50 ≤ 1.5 s and first words ≤ 1.8 s in `feudalsim session`.
- The client drops below 60 fps beside the model on recommended spec once real art lands.
- Cloud prices or availability change enough to break 22 §12.3's targets.
