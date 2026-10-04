# M1 playtest kit — Talking Camp

For the owner (M1-24). Criteria: 22 §17.2 #4 and #11, 22 §15.4 rubric, 30 §5 M1 feel. **≥ 5 testers × ≥ 45 min.**

## Setup (per tester)

1. `git pull` (Git LFS on), then `dotnet build game/FeudalSim.Game.csproj`.
2. Live AI needs `OPENROUTER_KEY` in `.env` (≈ $0.03 per tester-hour at measured prices); without it the camp runs in
   template mode — use it for at most one tester, as a control.
3. Start: `/Applications/Godot_mono.app/Contents/MacOS/Godot --path game` (the camp at Landfall, evening).
4. Optional record of the session for later analysis: the AI gateway and decisions are logged under `sim_runs/`.

## Controls

WASD walk · Shift jog · Ctrl sprint (uses stamina) · wheel zoom · **[E]** talk to the settler in front of you · type and **Enter** to speak ·
quick-intent buttons (Ask, Request ▾, Trade, Compliment, Apologize, Insult, Threaten, Leave) · **Esc** leave ·
**[P]** people you've met · Space pause · 1/2/4/8 time speed.
Consequential lines (insults, threats, promises, deals) wait 1.5 s: **Backspace** unsays them.

## Suggested 45 minutes (don't read the tester the design; let them play)

1. 10 min: meet at least four settlers; small talk; ask about their work and the camp.
2. 10 min: ask favors (firewood, foraging, the fire); try persuading someone who says no — with a real reason, then
   with only pressure.
3. 10 min: provoke someone (rude remark or insult); see what happens; try to make it right later.
4. 10 min: tell someone something about another settler; come back later and see what they remember.
5. 5 min: free play.

## After each conversation (quick, 1–5)

| # | Felt like a person | Responded to what I actually said | Remembered our history | Consistent with the world | Distinct voice | Reaction proportionate | Could be persuaded, not pushed around | Made their own choices | Immersion break? (category) | Felt railroaded? (y/n) |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | | | | | | | | | | |
| 2 | | | | | | | | | | |
| 3 | | | | | | | | | | |

## After the session (interview, unprompted first)

1. Describe the people you met. *(Count personalities described unprompted — target: 3+ for ≥ 70% of testers.)*
2. Was there a moment when what you said changed what happened? Describe it. *(Target: every tester has one.)*
3. Did anyone react to something you'd done earlier? *(Target: ≥ 70% recall one.)*
4. Did any character feel scripted or railroaded? When?

## Results (owner fills in)

| Tester | Minutes | Mean "felt like a person" | Mean "responded to what I said" | Mean "made their own choices" | Personalities described | Words changed an outcome (y/n) | Recalled an earlier reaction (y/n) | Railroaded (y/n) | Notes |
|---|---|---|---|---|---|---|---|---|---|
| | | | | | | | | | |

**Pass (22 §17.2 #11, 30 §5):** mean ≥ 3.5 felt like a person, ≥ 3.8 responded to what I said, ≥ 3.5 made their own
choices; ≥ 70% recall an earlier reaction; ≥ 70% describe 3+ personalities; every tester reports words changing an
outcome; < 10% answer "felt railroaded" (#4).

Known M1 limits to tell testers up front: graybox art and placeholder sounds, no crafting/farming/building, fights are
placeholders, and **settlers quarrel and come to blows among themselves far more often than intended** (≈ one brawl a
camp-day; calibration is an M2 item — discount it when rating "reaction proportionate").
