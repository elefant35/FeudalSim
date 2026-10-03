# FeudalSim *(working title)*

A single-player, third-person 3D medieval-fantasy game about founding a society — and living inside it.

A boatload of settlers wades ashore on an empty, wild land. You are one of them: not a hero, not a
god-hand directing the colony, just one person among two dozen trying to survive the first winter.
Everyone around you is a simulated person with needs, skills, a temper, a memory and a voice. Over
years and generations the camp becomes a hamlet, a village, a lordship — and then one of several
rival realms on the same coast. People disagree, gossip, steal, fall in love, and eventually march
to war. What you become — hermit, farmer, smith, shopkeeper, knight, lord, outlaw — is up to you.

**Hard systems, soft voice:** deterministic simulation decides what happens; language models
(OpenRouter/Qwen now, local models later, plus TypeSafe's Jev decision model for fast
classification) give those systems a human voice.

## Status

**Pre-production / planning.** The design and development plan lives in [`docs/`](docs/README.md).
No code yet — milestone **M0 (Foundations)** is next; see the
[roadmap](docs/production/30-roadmap.md) and the M0 checklist in
[20-architecture](docs/tech/20-architecture.md).

## Where to start reading

1. [docs/00-vision-original.md](docs/00-vision-original.md) — the original vision brainstorm
2. [docs/01-canon.md](docs/01-canon.md) — binding decisions, numbers and names
3. [docs/02-game-overview.md](docs/02-game-overview.md) — what it feels like to play
4. [docs/README.md](docs/README.md) — index of every design, tech and production document

## Planned repository layout

```
/docs        Design, technical and production plans; ADRs in docs/adr/
/src         Pure C# (.NET 8) projects: simulation core, content, AI gateway, headless runner, tests
/game        Godot 4 (.NET) client project
/content     Data-driven game content (YAML), validated in CI
/tools       Scripts and developer tools
```

## Prerequisites (from M0 onward)

- .NET 8 SDK
- Godot 4.x **.NET** edition (exact version pinned at M0)
- An OpenRouter API key for LLM features (optional — the game has an LLM-free "template mode")

Copy `.env.example` to `.env` and fill in keys. `.env` is gitignored; never commit secrets.
