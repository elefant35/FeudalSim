# Art briefs: start here

**Audience:** a coding agent (or artist) building FeudalSim's 3D assets and animations while another agent
builds the game code. Read this file first, then the two files below, in order.

1. [01-technical-contract.md](01-technical-contract.md): the rules your files must follow so the game can load
   them without code changes: scale, axes, palette, skeleton bone names, animation clip names, file paths,
   budgets, checks and the manifest.
2. [02-first-playable-assets.md](02-first-playable-assets.md): what to build, in priority order, with sizes and
   notes for each asset.
3. [STATUS.md](STATUS.md): your progress log. Update it as you go; the code agent reads it to wire your work in.

Background, if you need it (you don't have to read all of it):

- `docs/02-game-overview.md` §9, the art direction.
- `docs/production/32-art-and-audio-production.md`, the full art pipeline. The contract here is a focused subset of it;
  if the two disagree, **this folder wins for M2 first-playable work** and you note the difference in STATUS.md.
- `docs/design/10-world-and-setting.md`, the island, its plants, rocks and biomes.
- `CLAUDE.md`, the repo's working rules (the asset and licence rules apply to you).

## The game in one paragraph

FeudalSim is a single-player medieval settlement game. You are one of 24 settlers shipwrecked on a cold, wet,
temperate island (think the coasts of Scotland, Ireland or Norway around 1100 AD). The ship, the *Wending Star*, lies
broken on a reef off the landing beach. The first season is survival: forage, cut wood, knap flint tools, build
shelters, keep the fire going, and get along with the other settlers. It is played in **first person** (a
third-person toggle exists), at walking pace, in a **stylized low-poly, painterly look**: simple strong silhouettes,
flat palette colours with subtle gradients, and mood from lighting, fog and weather. Think *Valheim's* lighting over
simpler geometry, with *Townscaper*-like readability for buildings. No magic, no fantasy creatures.

## What is wanted right now

A **first playable build**: you walk the real generated island in first person, among real trees, rocks and
plants, with the settlers' camp and the wreck on the shore, and settlers who look like people. Everything in
priority **P0** of `02-first-playable-assets.md` serves that. Then P1, then P2.

## How the work flows

1. Pick the next unfinished asset in `02-first-playable-assets.md` (P0 first, top to bottom).
2. Build it: prefer a **Blender Python generator script** in `art/generators/<family>/` for anything with
   variants (trees, rocks, plants, props); hand-built `.blend` files are fine for one-offs (characters, the wreck).
   Licensed CC0 sources are allowed (see the contract §10).
3. Export the `.glb` to the path the contract gives, run the **check** and **preview** tools, and look at the preview.
4. Add or update its **manifest entry** with `status: review`. Never set `approved`: only the owner approves a look.
5. Log it in `STATUS.md` and commit (contract §12 for git rules).

Blender runs headless:

```bash
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P <script.py> -- <args>
```

## What not to touch

The code agent owns `src/`, `tests/`, `game/scripts/` (except `game/scripts/Dev/Art*`), `content/` (except
`content/assets/`), and `docs/` (except this folder). If you need something from the code side (a new clip name, a
shader feature, a different path), write it under "Requests to the code agent" in `STATUS.md`. Don't edit their files.
