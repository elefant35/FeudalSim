# Spike S6 — Sim scale

> **Result: PASS** (2026-10-04) on the dev machine (Apple M3 Pro, one core, .NET 8, Release), after making the
> simulation tiered: LOD2 people update once per game hour, LOD3 people once per game day (or per macro step),
> and the nightly social work is spread over the day. A **1,500-person year takes 21.5 s at LOD2 (≤ 60 s) and
> 1.7 s at LOD3 (≤ 5 s)**; a realistic 1,500-person tier mix steps in **0.05 / 0.18 / 20.7 ms** avg / p99 / max
> (≤ 8 / 16 / 40 ms); **500 people at LOD1 run a year in 86 s** (21 §18.1: ≤ 5 min).
> Caveat: the 20 §19 step budget is stated for *minimum* spec; these are dev-machine numbers (see Risks).
>
> **Question** ([30 §4](../production/30-roadmap.md#4-risk-retiring-spikes)): can the sim tick 1,500 agents across
> LOD tiers within budget, and fast-forward a year headless quickly? **Pass:** per-agent costs and step budgets in
> [20 §19](../tech/20-architecture.md#19-performance-budgets) on a synthetic population; 1 game year of 1,500 people
> headless ≤ 60 s at LOD2 / ≤ 5 s at LOD3.

## Starting point

Before the spike every system updated every person every 100 ms step. The 24-person camp ran at ~1.1 µs per
person-step, but cost grew with the square of the population: **one game day of a 1,500-person camp took 237 s**
(13 ms per step) — a year would take two hours. The quadratic parts were the nightly Renown pass (all pairs, a
sorted-dictionary lookup each), the pre-first-night Renown fallback inside every gossip `Tell` (O(people) per call),
co-work pairs and partner scans over everyone at a crowded site, and the all-pairs shipmate seeding.

## What changed

| Change | Where | Effect on the 24-person camp |
|--------|-------|------------------------------|
| **`TierSchedule`**: per step, the due rows and each row's own `dt = now − LastUpdateGameMs` (20 §5.2). LOD0/1 every step; LOD2 when its per-person phase offset `hash(id) mod 1 h` passes; LOD3 at the day boundary or every macro step | `Sim/Simulation/TierSchedule.cs`, `SimWorld.Step` | none (metrics identical over 8 days; only the hash changed, because `LastUpdateGameMs` is now kept) |
| Needs, psychology and activity iterate the due rows with per-row `dt`; **LOD2** spends the past hour on its current action (arrival immediate inside the camp), then the same scorer picks the next hour's (21 §15.5) | `NeedsDecaySystem`, `PsychologySystem`, `ActivitySystem` | none |
| **LOD2 interaction rolls**: ≤ 2 per agent-hour, chance I/16 each (the LOD1 quarter-hour rate over an hour), partner among ≤ 8 sampled co-located LOD2 people, same resolution as LOD1 | `InteractionSystem.Lod2` | none |
| **`Lod3System`**: daily aggregate — production by stock pressure, food from the stores, daily need equilibria, psych needs by mass balance, exact emotion decay, mood, crewmate chats (21 §15.6) — **uncalibrated** | `Sim/Systems/Lod3System.cs` | none (no LOD3 people) |
| **Macro steps** (`SimWorld.StepMacro`, scenario `macro_step: hour\|day`, 20 §5.4) | `SimWorld`, `ScenarioRunner`, `bench` | — |
| `SetLodTier` command (scenario `tiers: {lod2, lod3}`) to pin people to the abstract tiers until settlements and the relevance set exist | `StateCommand`, `Scenario` | — |
| Shipmates seeded per ship of ≤ 60, else crews of 24 (sparse graph) | `RelationshipStore.SeedShipmates` | none |
| Renown: a pass at Landfall seeding (Renown is always state; the O(people) fallback stays for worlds without one); the nightly pass is sparse (only pairs with familiarity or a held belief) with `den = Σw − w_b` | `SocialSystem`, `ReputationStore.Recompute` | day-1 J_eff from seeded familiarity; tiny rounding change |
| Relationship daily update **time-sliced**: each holder's edges update in hour `hash(holder) mod 24` (16 §4.8–4.12 says "once per game day", not at midnight) | `RelationshipStore.DailyUpdate(slice)` | edge upkeep at a holder-specific hour |
| Crowds: ≤ 32 LOD0/1 partner candidates per initiation (keyed subset; camps never reach it) and co-work counted within teams of 12 | `InteractionSystem`, `SocialSystem.Hour` | none for candidates; co-work only if > 12 on one task |
| Exact speedups: trait handles and creeds cached in `RelationshipStore` (was a string scan per opinion), no closure per edge | `RelationshipStore` | none (hash identical) |
| **`feudalsim bench`**: per-step avg/p99/max, per-system totals, the slowest step's breakdown, per-agent cost by tier, `--max-step-ms` / `--max-seconds` gates; `ISystemObserver` hook (timing stays in Hosting) | `Headless/Commands/BenchCommand.cs` | — |

The camp's 21 §19 sweep (100 seeds × 30 days) is still in band after all of it: idle 0.177 (94 % of seeds), need
health 0, mood +10.2, divergence 0.253, task failure 0, friends 0.315 / person (96 % in band, a friend in every seed);
the public-event rumor check still reaches ≥ 80 % in 3 days in 99 % of seeds.

## Measurements

All on Apple M3 Pro, one thread, Release, `feudalsim bench` (warm-up days excluded where noted). Scenarios:
[s6_lod1](../../content/scenarios/s6_lod1.yaml), [s6_lod2](../../content/scenarios/s6_lod2.yaml),
[s6_lod3](../../content/scenarios/s6_lod3.yaml), [s6_mixed](../../content/scenarios/s6_mixed.yaml).

| Budget (source) | Target | Measured | Pass |
|-----------------|--------|----------|------|
| Headless year, 1,500 people at **LOD2** (20 §19) | ≤ 60 s | **21.5 s** (576,000 fine steps; 18.6 µs per agent-hour) | ✅ |
| Headless year, 1,500 people at **LOD3** (20 §19) | ≤ 5 s | **1.69 s** (32 macro days; 35 µs per agent-day) | ✅ |
| Sim step, 1,500 people, avg / p99 / max (20 §19) — 200 LOD1 + 1,000 LOD2 + 300 LOD3, after 1 warm-up day | 8 / 16 / 40 ms | **0.051 / 0.179 / 20.7 ms** (worst: midnight social upkeep) | ✅ (dev machine) |
| 500 people at **LOD1** for a year on one core (21 §18.1) | ≤ 5 min | **86 s** (0.30 µs per person-step) | ✅ |
| Per-agent: LOD1 update / LOD2 hour / LOD3 day (20 §19) | 30 / 400 / 150 µs | **0.3 / 18.6 / 35 µs** | ✅ |
| LOD2 agent-hour (21 §15.5) | ≤ 50 µs | **18.6 µs** | ✅ |
| Sim step, M1 camp (24 at LOD0/1) (20 §19) | ≤ 2 ms avg | 0.008 ms (144,000 steps in 1.1 s) | ✅ |

Per-system shares in the LOD2 year: interactions 49 %, social upkeep 26 %, activity 13 %, needs 5 %, LOD 5 %. In the
500-person LOD1 stress camp (everyone at one fire) steady-state max step is 16.6 ms; the one-time Landfall setup step
(500 spawns, ~11,400 shipmate edges, first Renown pass) takes 82 ms.

**LOD0** has no sim-only cost of its own yet (perception, combat and embodiment sync arrive in M2); its sim path is the
LOD1 path plus `EmbodimentReport` commands, so it is covered by the LOD1 figure until then.

## Findings

1. **Tiering is the design, not an optimization.** Without hourly/daily cadences the LOD2 year would take ~2 h.
   With them the per-agent costs sit 10–20× under 20 §19's ceilings, which leaves room for M2–M5 systems.
2. **20 §19's per-agent ceilings and the year targets disagree:** 1,500 × 768 agent-hours × 400 µs = 461 s, not
   ≤ 60 s; 1,500 × 32 × 150 µs = 7.2 s, not ≤ 5 s. The year targets are the binding ones (they imply ≤ 52 µs per
   LOD2 agent-hour, matching 21 §15.5's 50 µs). Recorded as a calibration finding (20 §19 note; 31 Q).
3. **LOD1 still runs every step** (20 §5.2 says 1 Hz). At 0.3 µs per person-step it is 100× under budget, and keeping
   it avoids changing the tuned camp; the 1 Hz bucketing waits until a profile needs it.
4. **Midnight work must be sliced.** The relationship upkeep over ~70k edges was an 80 ms step; spread over 24 hourly
   slices the worst step is ~20 ms, and the remainder is the Renown pass, memory compaction and belief forgetting —
   the next candidates if minimum spec needs it.
5. **Dense crowds need caps.** Weighting every co-located person as a partner, or pairing everyone on a task as
   co-workers, is O(k²) at a 500-person fire; candidate subsets (32) and work teams (12) keep crowds linear without
   touching the camp.
6. **LOD3 is a first cut.** It is cheap (35 µs per agent-day) but uncalibrated; 21 §15.6's calibration tables and the
   LOD1-vs-LOD3 fidelity check (production ±10 %, mood ±8 …) belong to M4 (Interludes), where skips become real.

## Risks and follow-ups

- **Minimum spec:** all numbers are from an M3 Pro. If minimum spec is ~2.5× slower, the worst step (20.7 ms) is
  ~52 ms — over 40 ms. Slicing the Renown pass and compaction (finding 4) is the fix; re-measure on minimum-spec
  hardware in M5 (when 1,500 people become real) and M7.
- **Tier assignment** is pinned by scenario commands; the real membership rules (near region, relevance set, far
  settlements) need settlements (M2–M3) and the player's position.
- **Cross-tier social contact** (LOD1 with LOD2) is not modelled yet; LOD2 people only meet LOD2 people.
- The `tiers:`/`macro_step:` scenario fields and `SetLodTier` are dev tools; skips (`BeginSkip`) replace them in M4.

## Reproduce

```bash
dotnet run -c Release --project src/FeudalSim.Headless -- bench --scenario content/scenarios/s6_lod2.yaml --max-seconds 60
dotnet run -c Release --project src/FeudalSim.Headless -- bench --scenario content/scenarios/s6_lod3.yaml --max-seconds 5
dotnet run -c Release --project src/FeudalSim.Headless -- bench --scenario content/scenarios/s6_mixed.yaml --warmup-days 1 --days 4 --max-step-ms 8
dotnet run -c Release --project src/FeudalSim.Headless -- bench --scenario content/scenarios/s6_lod1.yaml --max-seconds 300
```
