# advance-plan — reference

## 1. Verification commands

Some of these only exist once M0 has built them; until then, verify what exists and say what's not
yet available.

| Check | Command (from the repo root) | Pass condition |
|-------|------------------------------|----------------|
| Build | `dotnet build` | 0 errors, 0 warnings |
| Unit tests | `dotnet test` | 0 failures |
| Content | `dotnet run --project src/FeudalSim.Headless -- content validate` | exit 0 |
| Headless smoke + determinism | `dotnet run --project src/FeudalSim.Headless -- run --scenario content/scenarios/m0_smoke.yaml --verify-determinism` | exit 0; identical hashes |
| AI gateway (chat) | `dotnet run --project src/FeudalSim.Headless -- ai ping` | Prints a completion + latency, or `fallback: template` without a key |
| Fast decider | `dotnet run --project src/FeudalSim.Headless -- ai decide` | Prints normalized option probabilities summing to 1 |
| Godot build | `dotnet build game/FeudalSim.Game.csproj` | 0 errors |
| Art checks | `/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/art/check.py -- <asset>` | `RESULT ok` within budget class |
| Art preview | `… -P tools/art/preview.py -- <asset.glb> art/previews/<name>.png` | PNG written; Read it and judge it |
| Audio checks | `python3 tools/audio/check.py <files>` | Format, sample rate, loudness within 32 §12 |
| Links in docs | a Markdown link/anchor check over `docs/` | 0 broken links |
| Git LFS present | `git lfs version` | Prints a version (needed before committing binaries) |

The canonical command list moves into `CLAUDE.md` § Commands at M0-15; prefer that list once it
exists.

## 2. Definition of done (from 30 §8)

A work item is done when:

1. It matches its owning doc (or the doc was updated in the same change).
2. It has tests and contributes metrics to the headless runner (where it's a system).
3. It behaves correctly at every relevant LOD tier and across save/load.
4. It respects parity (player and NPCs use the same rules).
5. Any LLM/decider touchpoint has a policy/template fallback and is covered by the eval suite.
6. It meets the performance budgets in 20 §19.
7. Assets have manifest entries, licenses, and pass their checks; looks await owner approval.

## 3. Where things are

| Need | Look in |
|------|---------|
| Binding numbers and names | `docs/01-canon.md` (ownership map in §16) |
| What a milestone must deliver | `docs/production/30-roadmap.md` §5 |
| M0 step-by-step acceptance criteria | `docs/tech/20-architecture.md` §20 |
| LLM, decision points, fast decider | `docs/tech/22-llm-integration.md`, canon §4.1 and §13 |
| NPC behavior and the policy | `docs/tech/21-npc-ai.md` |
| Art & audio pipeline, budgets, catalog | `docs/production/32-art-and-audio-production.md` |
| Owner decisions pending | `docs/production/31-risks-and-open-questions.md` §2 |
| Progress, approvals, blockers | `docs/production/33-progress.md` |
| Long-running session goals | `docs/production/34-session-goals.md` |

## 4. Commit conventions

- Types: `feat`, `fix`, `test`, `refactor`, `perf`, `docs`, `build`, `ci`, `art`, `audio`, `content`.
- Scopes: `sim`, `content`, `ai`, `headless`, `game`, `tools`, `plan`, plus asset categories.
- One work item per commit where possible; the message names the item id, e.g.
  `feat(sim): game clock and calendar (M0-04)`.
- Footer `BREAKING-SAVE:` when the save schema breaks (20 §16).

## 5. Asking the owner

Ask only when you need a decision or approval the tracker doesn't already give. When you ask, give
the exact command or choice and your recommendation, record it under Blockers, and carry on with
other unblocked work.
