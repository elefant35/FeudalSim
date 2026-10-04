# M2 first playable: play guide and feedback sheet

**Build:** `main` from 2026-10-04 (M2-FP1…FP5). **Time:** 20–40 minutes. **What it is:** the first build where you walk
the generated island in first person among real art. It's a slice, not the game: the systems underneath (needs,
weather, injuries, illness, crafting, social life) run, but much of what you'd *do* about them isn't wired to the
player yet. Feedback on the feel, the look and what you reach for first is what's most useful now.

## Launch

```bash
tools/play.sh
```

- The first launch generates the island (about 100 s with a frozen window; then it's cached). Later launches take about 30 s.
- `tools/play.sh --template` plays without any AI calls: overheard talk uses written templates. Without it, the
  settlers' overheard talk and your conversations use the live model through `.env`. That's cheap (a few cents an hour),
  but it isn't free.
- `tools/play.sh --third-person` starts over the shoulder. `tools/play.sh --scenario m1_view` is the old flat M1 camp.

## Controls

| Key | Does |
|-----|------|
| Mouse | Look (captured while you walk) |
| W A S D | Walk where you look |
| Shift / Ctrl | Jog / sprint (sprint uses stamina) |
| E | Interact with what you look at: talk to a settler, fell a tree, gather a plant; by water (looking at nothing), drink |
| V | First person ↔ third person (wheel sets the distance in third person) |
| Tab | Free the mouse (a click takes it back) |
| K | Knap a flint knife (the bench minigame) |
| P | People: who you know and what you think of them |
| I | What you carry (as you believe it is); ↑↓ choose, Enter eats one, D puts one down |
| T | Take something from the settler you look at (theft: they may notice) |
| Esc | Leave a conversation or the bench |
| Space · 1 2 4 8 | Pause · time speed |

## What you'll find

You start at the Landfall camp on the dunes above the landing beach, late on the first afternoon of Spring, looking
out to sea. The *Wending Star* is broken on the reef about 250 m out. Around the fire: 24 settlers going about the
camp's day (wood, fire, food, water at the brook about 180 m west, talk), eight sailcloth shelters, the salvaged stores.
Inland: dune grass, then meadow, woods and the island's hills. Every tree, bush, rock and plant is a real sim node.
What you fell or pick stays felled or picked.

## Things to try (tick what you did; note anything odd)

1. [ ] Look around from the start and walk down to the beach. Does it read as a shipwreck landing?
2. [ ] Walk inland to the woods and the hills. How does the scale feel: distances, tree heights, walking and jogging
       speed?
3. [ ] Look at a settler and press E. Talk to them: ask for help, ask about others, be rude, apologize.
4. [ ] Look at a tree and press E to fell it (you have a ship's axe). It takes about 40 s at ×1 (use 4 or 8 to hurry it).
       You should see the chop, then a stump.
5. [ ] Gather plants (E on a plant). Some edible plants have **poisonous look-alikes**, and what you *think* you picked
       may not be what it is.
6. [ ] Press K by the fire and knap a flint knife.
7. [ ] Switch to third person (V) and watch the camp for a while at ×4. Do the settlers' days look believable?
8. [ ] Stay up into the night (×8). Watch the light, the fire and where people sleep.
9. [ ] Anything you tried to do that the game didn't let you.

## Known gaps (so you don't have to report them)

- You can eat what you gather ([I]), drink at water ([E]), swim (watch your breath), and carry only so much (felled logs
  stay in a pile by the stump; [E] picks one up). You can't yet sleep or build as the player (the settlers can).
- Felling has no minigame yet: the stages resolve on their own. Gathering is instant.
- Settlers fetch wood and food from abstract camp places (the "woods" and "forage ground"), not from the trees and
  plants you see. That's M2-21's work.
- No seasons on foliage yet, no rain or snow particles, and no footstep or ambience sounds beyond the fire.
- Name labels over settlers within 14 m, and the plain HUD text at the top, are development aids.
- All the art is the art agent's first pass with `status: review`. **It's yours to approve or reject**, piece by piece
  if you like (`content/assets/*.yaml`; previews under `art/previews/`).

## Feedback

Write freely here (or just tell me in chat). These questions are a prompt, not a form.

- **First impression in the first two minutes:**
- **The look** (terrain, trees, settlers, camp, wreck, sea, light). What works, what jars?
- **First person:** comfortable? Field of view, height, mouse speed, head bob (there's none), seeing your own body?
- **Movement and scale:**
- **Interaction by looking (E):** clear what you can do? What did you expect to be able to do?
- **The settlers:** alive? Their talk, their day, how they react to you?
- **What you most wanted to do next:**
- **Art decisions** (approve / change / reject, by family: trees, ground, rocks, plants, settlers, clothing,
  animations, camp, wreck):
- **Anything else:**
