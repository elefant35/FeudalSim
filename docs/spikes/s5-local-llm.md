# Spike S5 — Local LLM early look (M1-S5)

**Date:** 2026-10-04 · **Machine:** Apple M3 Pro, 18 GB unified memory (the developer Mac) · **Runtime:** LM Studio
server (OpenAI-compatible, MLX backend) on `127.0.0.1:1234` · **Model:** `qwen/qwen3-8b` MLX 4-bit (4.62 GB on disk,
4.31 GiB loaded, context 8,192) · **Pipeline:** `feudalsim session` with dialogue routed locally
(`LLM_BASE_URL=http://127.0.0.1:1234/v1`, dummy `LLM_API_KEY`), fast decider still `qwen/qwen3.5-9b` on OpenRouter,
utility model disabled locally (overheard talk falls to templates) · **Client:** the M1 camp view
(`$GODOT --max-fps 60 --path game -- --fps 70`, graybox capsules, 24 settlers).

**Question (30 M1-S5):** can a 7–14B 4-bit model beside the Godot client reach p50 TTFT ≤ 1.5 s while the game holds
60 fps?

## Results

| Measurement | Value | Target (22 §12.4 local) |
|-------------|-------|-------------------------|
| Raw TTFT, ≈ 1,100-token prompt, cold | 5.00 s | — |
| Raw TTFT, same prefix, warm (prompt cache) | **0.59 s** | < 1.5 s |
| Pipeline, 3 s TTFT cut (the old hard-coded value) | every turn cut → speak-only regeneration → template; breaker opened after 5 turns | — |
| Pipeline, `LLM_TIMEOUT_TTFT_MS=8000`, no client (10 turns) | TTFT **1.79 / 4.01 s** · gesture 2.60 / 5.33 · first words 3.59 / 6.71 | < 1.5 / < 3.0 · ≤ 1.6 / ≤ 3.2 · ≤ 1.8 / ≤ 3.8 |
| Pipeline beside the Godot client (12 turns) | TTFT **1.77 / 2.41 s** · gesture 2.59 / 3.24 · first words 3.39 / 4.61 · all 12 lines from the model | as above |
| Godot frame time with the model generating (70 s, cap 60) | **59.6 fps mean**, p50/p95/p99 16.67 ms, max 137.5 ms, **0.53%** of frames slower than 55 fps | 60 fps |
| Godot frame time, no model (uncapped) | 1,150 fps, p99 1.15 ms | — |

## Findings

1. **The game holds 60 fps beside an 8B model** on this machine (0.5% slow frames, one 137 ms hitch) — but the M1
   scene is graybox; re-measure with M2's art and crowd (M1-S1) on the GPU.
2. **TTFT p50 1.77 s misses 1.5 s; p95 2.41 s meets 3.0 s.** The cause is prefill: a 2,300-token decision-first
   prompt takes ≈ 5 s cold; only the prefix cache makes local viable. Each speak-only regeneration or overheard
   render with a different prompt evicts the cache (LM Studio keeps one), so the next turn is cold again.
3. The router ignored `LLM_TIMEOUT_TTFT_MS` (a hard-coded 3 s) — fixed; local profiles need ≈ 5–8 s for the first
   turn with a new persona.
4. Decision quality looked in character (12/12 turns voiced by the model, no rule-check failures), but the
   calibration suites have not been run against it.

## Next (22 §14, M7 local plan)

Slot pinning per conversation (llama.cpp `--slot-save` / `cache_prompt`) so overheard talk and regenerations don't
evict the dialogue prefix; pre-warming the persona prefix when a conversation starts; a shorter local prompt profile;
header latency with grammar-constrained `CHOICE`; Laya CPU latency beside it (not measured here). ADR-0012 records the
decision.
