# 19 — Player Experience

> **Status:** Draft v0.1 · **Owner doc for:** player creation & backgrounds, player paths, UI/UX, dialogue UI, journal, death/lineage UX, difficulty, onboarding · **Depends on:** [01-canon](../01-canon.md), [02-game-overview](../02-game-overview.md) (player fantasy and core loops — owner), [10-world-and-setting](10-world-and-setting.md) (ship manifest), [11-survival](11-survival.md), [12-skills-and-professions](12-skills-and-professions.md), [13-crafting-and-minigames](13-crafting-and-minigames.md), [14-technology-and-buildings](14-technology-and-buildings.md), [15-economy-and-trade](15-economy-and-trade.md), [16-social-systems](16-social-systems.md), [17-governance-and-law](17-governance-and-law.md), [18-conflict-and-warfare](18-conflict-and-warfare.md), [21-npc-ai](../tech/21-npc-ai.md), [22-llm-integration](../tech/22-llm-integration.md)

This document covers what it is like to *be* one person in FeudalSim. It starts with how that person is made. It then covers the lives they can lead, the screens through which they perceive a society of simulated people, and what happens when they die. The goal is the vision's promise: "The player should be able to be pretty much anything they want in this society", inside a world where "everyone should feel like a full person."

---

## Table of contents

1. [Experience principles](#1-experience-principles)
2. [Player creation](#2-player-creation)
3. [Player paths](#3-player-paths)
4. [Core loops & goals without quests](#4-core-loops--goals-without-quests)
5. [World UI: HUD, inventory, bench, building, map](#5-world-ui-hud-inventory-bench-building-map)
6. [Dialogue UI](#6-dialogue-ui)
7. [Trade UI & the journal](#7-trade-ui--the-journal)
8. [Leader, military & notification UI](#8-leader-military--notification-ui)
9. [Onboarding](#9-onboarding)
10. [Death & Lineage UX](#10-death--lineage-ux)
11. [Interludes UX](#11-interludes-ux)
12. [Settings, difficulty & accessibility](#12-settings-difficulty--accessibility)
13. [Player expression](#13-player-expression)
14. [The player's contract with LLM-driven characters](#14-the-players-contract-with-llm-driven-characters)
15. [Data schemas](#15-data-schemas)
16. [Interfaces, LOD & milestones](#16-interfaces-lod--milestones)
17. [Tuning knobs, exploits & validation](#17-tuning-knobs-exploits--validation)
18. [Open questions](#open-questions)
19. [Proposed canon additions](#proposed-canon-additions)

---

## 1. Experience principles

| # | Principle | In practice |
|---|-----------|-------------|
| X1 | **You are one person, not a god-hand.** | The camera stays with your body. Maps, ledgers and journals show only what your character has seen, been told, or written down. |
| X2 | **Beliefs, not ground truth.** | The UI never shows an NPC's hidden numbers (Opinion, Trust, needs). It shows *cues* and *your character's beliefs*, which can be wrong or out of date (§7.3). |
| X3 | **Diegetic first, minimal HUD.** | Bells, messengers, body language and weather carry information before any icon does. |
| X4 | **Every path is its own game.** | Each life (§3) has a minigame or decision loop that would be worth playing on its own ([canon P3](../01-canon.md#2-design-pillars)). |
| X5 | **Words are the main way you act, but never the only way.** | Free-text typing is first-class. Quick intents make every dialogue outcome reachable without typing (accessibility, controllers, template mode). |
| X6 | **Respect time.** | Batch crafting, standing orders, Wait, Interludes. Tedium is opt-out ([canon tenet 7](../01-canon.md#3-design-tenets-how-we-make-decisions)). |
| X7 | **Honest latency.** | The NPC reacts with body language at once, and words follow. The game never fakes an answer that the sim did not decide. |
| X8 | **Fair readability of language.** | When the game interprets your words, it tells you how (the "intent echo"), so misreads are visible and correctable before they cause consequences (§6.3). |

---

## 2. Player creation

### 2.1 Flow

| Step | Choice | Notes |
|------|--------|-------|
| 1 | **Name, sex, age (18–35), appearance** | Low-poly presets: face, skin, hair, beard, build, scars. Build is cosmetic. |
| 2 | **Background** (one of 10, §2.2) | Skill boosts, know-how, kit, a social seed. |
| 3 | **Attributes** | Point-buy: all six start at 5. Distribute **6 points**. Range 3–8; each point taken below 5 refunds one. The background adds +1 to one attribute. |
| 4 | **Traits** (2, chosen or rolled, §2.3) | From the canonical list ([canon §10.4](../01-canon.md#104-personality)). |
| 5 | **Ship ties** (2, §2.4) | Starting relationships from the *Wending Star* manifest ([10](10-world-and-setting.md)). |
| 6 | **Faith stance** | Ember orthodox · Ember lax · secret Ashen sympathizer (the secret is a belief NPCs may later discover). |
| 7 | **Ambition** (optional, §4.2) | A personal goal for the journal and the Chronicle. You can change it at any time. |
| 8 | **Review: "As the others see you"** | A first-impression card, LLM-voiced from the choices (template fallback). For example: *"Strong back, quick temper, owes Tam money. The heir's sister thinks you're handsome."* |

**Age.** Each year over 18 gives +1.5 skill points spread over the background's skills (+25 at age 35). At 30+ you get one extra background know-how. Older starts trade lifespan for competence. Aging rules belong to [12](12-skills-and-professions.md) and [16](16-social-systems.md).

### 2.2 Backgrounds

Boosts are added to the adult baseline in [12](12-skills-and-professions.md). Each background totals +75 skill points. The eight canonical backgrounds ([canon §12](../01-canon.md#12-the-player)) are joined by two new ones, **acolyte** and **joiner's apprentice**, so that the priest and builder paths have an entry. Know-how ids are illustrative; [12](12-skills-and-professions.md) owns the catalog. Kits contain **at most one salvaged iron item**, counted against the ship's finite salvage ([canon §5.3](../01-canon.md#53-the-founding-of-the-players-settlement)).

| Background | Skill boosts | Know-how | Kit | Attr. | Social seed |
|------------|--------------|----------|-----|-------|-------------|
| **Farmhand** | Farming 30, Husbandry 20, Woodcutting 10, Athletics 10, Cooking 5 | Tilling & sowing; Crop rotation; Dairying | Seed pouch, iron sickle* | End | Farming Competence +10 |
| **Woodsman** | Woodcutting 25, Hunting 20, Foraging 15, Carpentry 10, Stealth 5 | Felling; Snares & traps; Charcoal clamp (basic) | Iron hatchet*, flint & steel | Str | Known as a loner (Sociability cue) |
| **Smith's apprentice** | Smithing 30, Metallurgy 20, Mining 10, Melee 10, Carpentry 5 | Forge basics; Bloomery smelting (observed); Tool hardening | Tongs, leather apron | Str | The colony's hopes for metal rest partly on you |
| **Fisher** | Fishing 30, Athletics 15, Textiles 10, Cooking 10, Foraging 10 | Net-making; Fish drying & smoking; Small-boat handling | Line & bone hooks, net | Dex | Familiarity +10 with the ship's crew |
| **Soldier** | Melee 30, Athletics 15, Tactics 15, Archery 10, Leadership 5 | Spear & shield drill; Wound binding; Watch-keeping | Iron-headed spear*, padded jack | Str or End | Courage +10; children are wary |
| **Clerk** | Letters 30, Stewardship 20, Commerce 10, Persuasion 10, Leadership 5 | Reading & writing; Reckoning; Varrowan charter law | Wax tablet, quills & ink, copy of the Charter | Int | The drowned lord's clerk: the heir's Trust +20 |
| **Herbalist** | Healing 30, Foraging 20, Brewing 10, Cooking 10, Pottery 5 | Herb lore; Wound dressing; Poultices & tinctures | Herb satchel, stone mortar | Per | Piety −5 among the orthodox (“hedge-witch”) |
| **Peddler** | Commerce 30, Persuasion 20, Letters 10, Athletics 10, Stealth 5 | Appraisal; Reckoning; Osmeri haggling customs | Pack of needles, thread, salt; 24d coin | Cha | Honesty −5 (peddlers are distrusted) |
| **Acolyte** *(new)* | Persuasion 20, Letters 20, Healing 15, Leadership 10, Farming 10 | Ember rites; Reading; Sermon craft | Ember lamp, prayer book | Cha | Piety +15; the orthodox Opinion +10 |
| **Joiner's apprentice** *(new)* | Carpentry 30, Masonry 15, Woodcutting 15, Athletics 10, Pottery 5 | Joinery; Timber framing (basic); Wattle & daub | Mallet, iron chisel* | Dex | Builders are needed: Opinion +5 from heads of household |

\* the one salvaged iron item.

**Custom background** (M7, optional): spend 75 points freely (max 30 per skill), pick 2 know-how from a list of 12. It is flagged in the Chronicle as "of no particular trade."

### 2.3 Traits for the player

The human makes the player's choices, so traits cannot drive behavior the way they do for NPCs. Instead each trait has (a) a **mechanical effect** on the player's own systems, (b) a **perception effect**: NPCs who observe it learn it as a belief about you, and (c) an XP aptitude nudge.

| Trait | Mechanical effect | How NPCs come to see you |
|-------|-------------------|--------------------------|
| Brave | Fear gain ×0.7 | Courage +5 |
| Coward | Fear gain ×1.3; +10% sprint while fleeing | Courage −5 |
| Hot-tempered | Heavy attacks +10% damage; when insulted, Anger presentation (screen flush) | NPCs are more careful with you: confrontation E −5 against you |
| Honest | Trust gains ×1.2; lies you tell get −15% on the deception check | Honesty +10 |
| Greedy | Commerce aptitude +0.2 | Generosity −10 |
| Gossip | You hear rumors ×1.5 more often; NPCs tell you more | Trust gains ×0.9 |
| Pious | Rite XP +25%; faith events touch you more | Piety +10 |
| Vengeful | People who wronged you gain Fear +10 of you | Peaceableness −5 |
| Lazy | Energy drain −15%; work XP ×0.85 | Diligence cue: "idle" |
| Drunkard | Without drink for 2 days: aim sway +20%, stamina regen −10% | Seen drunk → Lawfulness −, rumors |
| Romantic | NPC Attraction gains ×1.25 | — |
| Paranoid | +1 effective Perception to notice stealth and pickpockets | Trust gains ×0.9 |
| Charitable | Gift Opinion effects ×1.2 | Generosity +10 |
| Ambitious | Leadership aptitude +0.2 | Lords see you as a potential rival (lord's Fear +5) |
| Stubborn | Sleep-deprivation penalties −20%; your counter-offers when haggling are believed more (+3% within the canon clamp) | "Mulish" cue |
| Jealous | You notice romantic rivals (an Attraction cue toward your partner becomes visible) | Spouse's Trust −5 |

### 2.4 Ship ties

[10](10-world-and-setting.md) owns the 24-person manifest and its **tie slots**. The player picks 2 ties. Everyone else starts at Familiarity 20–40 from the voyage.

| Tie | Initial values (NPC → player / player → NPC) | Example hook |
|-----|------------------------------------|--------------|
| Kin (sibling/cousin) | Opinion +40, Trust 60, Familiarity 80 | Shared household at Landfall |
| Friend | +30 / 50 / 60 | — |
| Sweetheart | +40 / 50 / 70, Attraction 50 | Marriage is possible by Year 1 |
| Former master | +20 / 50 / 70, `mentor` tag | Will teach background know-how |
| Creditor | −10 / 40 / 50; you owe 24d | A debt in the journal from minute one |
| Rival | −30 / 20 / 60, `rival` tag | Competes for the same job, partner or status |
| Sworn to the heir | Heir's Opinion +25 / Trust 50 | Pulls you into the Charter dispute |

---

## 3. Player paths

Paths are not classes. They are **lives that emerge** from what you practise, what you own, and what others accept you as ([canon P6](../01-canon.md#2-design-pillars)). Switching is always possible. A smith can become a lord, and a lord can end as an outlaw.

### 3.1 Path matrix

| Path | Primary skills | Its minigame / loop owner | Social standing it can reach | Earliest |
|------|----------------|---------------------------|------------------------------|----------|
| Hermit | Foraging, Hunting, Healing, Carpentry | [13](13-crafting-and-minigames.md), [11](11-survival.md) | Low Renown, high mystique; sought for counsel | M3 |
| Farmer (freeholder / tenant / serf) | Farming, Husbandry | 13 (crop & soil model) | Freeholder → yeoman → reeve | M3 |
| Craftsman | One craft skill + Commerce | 13 | Master; guild elder | M2–M5 |
| Shopkeep / merchant | Commerce, Persuasion, Letters | [15](15-economy-and-trade.md) | Burgher; council seat | M4 |
| Innkeeper / alewife *(added)* | Brewing, Cooking, Commerce | 13 + 15 | Hub of rumor and influence | M4 |
| Hunter / trapper / fisher | Hunting, Fishing, Stealth | 13 | Forester; lord's huntsman | M2 |
| Miner / prospector *(added)* | Mining, Metallurgy | 13, [10](10-world-and-setting.md) | The person who found the tin | M3 |
| Healer | Healing, Foraging | 13, 11 | Indispensable; exempt from levies | M2 |
| Priest | Persuasion, Letters, Leadership | [16](16-social-systems.md) (faith) | Shrine-keeper → parish priest | M4 |
| Soldier → knight | Melee, Tactics, Leadership | [18](18-conflict-and-warfare.md) | Sergeant → captain → knight → marshal | M5 |
| Steward / clerk | Stewardship, Letters | [17](17-governance-and-law.md), 15 | Lord's right hand | M5 |
| Lord | Leadership, Stewardship, Persuasion | 17 | Ruler | M5 |
| Outlaw / bandit | Stealth, Melee, Persuasion | 18, 17 | Outlaw chief; folk hero or villain | M4 |

### 3.2 The paths

Each path card gives: **Entry · Core loop · Its own game · Progression · Standing · Failure · A 2-hour session.**

#### Hermit
- **Entry:** Walk away. Build a hut more than 800 m from any settlement. Any background works; woodsman and herbalist are ideal.
- **Core loop:** Forage, trap, tend a garden, repair the hut, prepare for winter. You decide how much contact to have.
- **Its own game:** **Self-sufficiency across the seasons.** A homestead puzzle that balances calories, firewood, preserved stores and tools with no market behind you. Rare visitors (lost hunters, the sick, outlaws, a lord's forester) produce intense, memorable conversations.
- **Progression:** Better homestead (smokehouse, root cellar, goats). Mastery of Foraging and Healing. A rumor-built **mystique**: NPCs who hear of you form beliefs ("the wise one in the pines") and come for remedies and counsel.
- **Standing:** Outside the hierarchy. A lord may claim the land and demand rent, which brings the world to your door.
- **Failure:** Starving through a winter. Being robbed by outlaws with no help near. Being forgotten (Familiarity decays and no one would come to rescue you).
- **Session:** Check snares at dawn: two hares. Smoke one. A boy arrives with his fevered mother on a handcart. Brew willow-bark tea at the bench and dress her ulcer. She pays in salt and news: the lord is mustering. That evening a stranger asks to sleep by your fire. He is a deserter. Do you shelter him? You do. Three days later sergeants come asking. What you tell them decides who your friends are.

#### Farmer — freeholder, tenant, serf
- **Entry:** Clear and claim land in Era 0–1 (council allotment, [17](17-governance-and-law.md)). Later, rent it as a tenant, or accept serfdom for protection and a plot.
- **Core loop:** Till, sow, water, weed, fight pests and blight, harvest, store, sell on market days 4 and 8.
- **Its own game:** **The field.** The soil and crop model, rotation and pest minigames ([13](13-crafting-and-minigames.md)). Seasonal bets: which crop, how much to store versus sell, whether to plant before the frost risk passes.
- **Progression:** More land, oxen (later), a plow (T3 iron), hired hands, a barn. Freeholder → yeoman → elected **reeve**.
- **Standing:** A serf owes labor days and dues and cannot leave without leave. A freeholder owes taxes and levy service. Both are visible in the Obligations journal.
- **Failure:** Crop failure; debt; the lord seizing land for arrears; being levied during harvest ([18 §12.1](18-conflict-and-warfare.md#121-economy-interface-with-15)).
- **Session:** Spring day 2. Plow the lower field with a borrowed ox, owing its owner a day of labor. Sow barley, because rumor says the brewer pays well. The reeve reminds you of the lord's 2 labor days. You argue for moving them past planting. Persuasion moves him a little, but not past the law, so you go. At dusk you notice blight on a neighbor's rye at the border of your field. Warn him, or burn your margin and say nothing?

#### Craftsman (smith, bowyer, joiner, mason, potter, weaver/tailor, leatherworker, brewer/cook)
- **Entry:** A background, or apprenticeship to a master ([12](12-skills-and-professions.md)). You need tools, a workshop and materials.
- **Core loop:** Take commissions → source materials → work the bench minigame → sell or deliver → reinvest.
- **Its own game:** **The bench.** For example: draw out a bloom, weld, shape, quench, temper. Carve a bow stave following the grain, tiller it, string it. Each process has stages that test the player's skill, judged on the same quality model NPCs use ([13](13-crafting-and-minigames.md)).
- **Progression:** Tier mastery (Apprentice → Master). Know-how (quench hardening, crucible steel). Apprentices of your own. Maker's marks people recognize (Competence reputation). Batch-making mastered items.
- **Standing:** A master craftsman can sit on the council. A sole smith is often exempt from levies, and that is political leverage.
- **Failure:** No ore or charcoal; a rival underprices you; a bad blade breaks in battle and the widow names your mark.
- **Session:** The captain wants 12 spearheads before the muster on Summer 4. You have bloom for 8. Haggle with the charcoal-burner, who wants payment in a knife. Forge 4 heads by hand, chasing Fine quality. Then batch the rest at Common. Your apprentice asks to learn welding. Teaching costs an afternoon, but it's how the know-how survives you. At sunset the captain inspects the heads, tests one on a post, and grunts.

#### Shopkeep / merchant
- **Entry:** Have a stall on market day, then a shop (building + license, if the law requires one). The peddler background is ideal.
- **Core loop:** Buy low (from producers, other settlements, ships) → set prices → serve customers → manage stock, credit and reputation.
- **Its own game:** **Pricing and the counter.** Set prices per item against what you know of demand ([15](15-economy-and-trade.md)). Haggle face to face. Extend credit and chase debts. Run a caravan to another settlement, where the risk is bandits and the reward is the price gap.
- **Progression:** Shop → warehouse → trade partners in other polities → buying offices, land and marriages. A mercantile fortune can buy a lordship.
- **Standing:** A wealthy burgher has Status but is mistrusted (Honesty cues). They can sit on the council in Osmeri-influenced towns.
- **Failure:** Bad credit, theft, guild hostility, war cutting off routes, accusations of short weight.
- **Session:** Market day. Mark up salt 20% because you heard the ship is late. Tam haggles hard. You can't move his reservation price, but a compliment about his jugs earns a little goodwill. A widow asks for credit; you give it. At closing, count the till and find 3d short. Was it your hired boy? The journal shows only what you believe: you saw him near the box twice.

#### Innkeeper / alewife *(added)*
- **Entry:** Brew good ale (Brewing), then build a hall or tavern.
- **Core loop:** Brew → cook → host → listen.
- **Its own game:** **The room.** Brewing minigames, plus running a social space where you seat rivals apart, cut off drunks, and break up brawls before they start ([18 §4.2](18-conflict-and-warfare.md#42-brawls--bystanders)). The tavern is the settlement's **rumor hub**, so you hear everything first.
- **Progression:** Reputation for the best ale; rooms for travelers (inter-settlement news); a broker of introductions, marriages and deals.
- **Standing:** Low formal status, high informal influence (Renown).
- **Failure:** A killing in your hall; spoiled batches; a lord's ale tax.
- **Session:** Taste the new batch: hoppy and good. Two families in a feud arrive. You seat them at opposite ends and send free ale to the hothead. A traveler from Dunlach drinks and talks about their tin. That is valuable news, and the lord's steward will pay for it. You decide what to sell and to whom.

#### Hunter / trapper / fisher
- **Entry:** A woodsman or fisher background, or a bow and patience.
- **Core loop:** Scout → track → hunt or trap or fish → butcher → hides to the leatherworker, meat to the market.
- **Its own game:** Tracking, stalking (Stealth versus animal Perception), archery ballistics ([18 §2.8](18-conflict-and-warfare.md#28-archery)), trap placement ([13](13-crafting-and-minigames.md)). Dangerous prey (boar, bear, wolves) is real combat.
- **Progression:** Knowledge of game routes (map annotations that only you have). A lord's **forester** or **huntsman** office. Guiding war scouts (Perception reports, [18 §9.4](18-conflict-and-warfare.md#94-scouting--information)).
- **Failure:** Injury far from help; poaching in a lord's forest once forest law exists.
- **Session:** Follow boar sign along the river. Set a snare line. A wolf pack shadows you, so you build a fire and wait for dawn. Back in the village, trade pelts to the leatherworker for a quiver and hear that the lord now forbids deer-hunting in the north wood. The best stags are there.

#### Miner / prospector *(added)*
- **Entry:** Mining skill, a pick, and rumors of ore.
- **Core loop:** Prospect (read rock, follow streams) → dig → haul → sell ore or smelt it.
- **Its own game:** **Prospecting.** Deposits are hidden ground truth. You build beliefs from samples ("streak of green: copper?") and find out by digging. Finding the tin is a world-changing event ([canon §5.1](../01-canon.md#51-the-land)).
- **Progression:** Claims, crews, a mine charter from the lord.
- **Failure:** Cave-ins (11), a claim seized by the lord, a war fought over *your* mine.
- **Session:** Pan a hill stream: black grains. Tin? Tell no one yet. Sample the outcrop upstream: cassiterite, probably. Now decide whether to sell the knowledge to the lord, the smith, or the Osmeri traders. Whoever controls it tips the balance of power on the coast.

#### Healer
- **Entry:** Herbalist background, or learn from one.
- **Core loop:** Gather herbs → prepare remedies → treat injuries and disease → follow up.
- **Its own game:** **Diagnosis and treatment.** Read symptoms (shown as observations, not condition names). Choose and prepare a remedy at the bench. Dress wounds under time pressure (bleed-out timers on the battlefield, [18 §10.7](18-conflict-and-warfare.md#107-aftermath)). Midwifery.
- **Progression:** Reputation (Competence), apprentices, a sick-house. Exemption from levies. Battlefield surgeon.
- **Failure:** A patient dies, and the family may blame you. The orthodox may call you a hedge-witch.
- **Session:** A child with fever. Ask the mother questions; you suspect bad water and prepare an infusion. Then a brawl casualty arrives with a broken nose and a grudge. He talks while you set it and you learn who started the fight. At night you deliver twins. The father names one after you, and the Chronicle records it.

#### Priest
- **Entry:** Acolyte background, or Piety and Letters plus acceptance by the faithful. Later, appointment by a higher cleric or the lord ([16](16-social-systems.md) owns faith).
- **Core loop:** Rites (Hearthday, births, marriages, funerals) → sermons → counsel → charity.
- **Its own game:** **The sermon and the confessional.** Sermons are typed or chosen speeches, Jev-scored on topic and persuasiveness and clamped, which shift Piety and moral norms a little. Confession is dialogue in which NPCs reveal secrets, and what you do with them matters. You also arbitrate disputes.
- **Progression:** Shrine → chapel → parish. Influence over law (blessings and condemnations). Schism leader (the Ashen sympathizer path).
- **Standing:** Respected and exempt from levies. A political actor.
- **Failure:** Scandal; heresy accusations; schism against you.
- **Session:** Hearthday sermon. You type a homily about sharing the harvest; it lands well with the poor and poorly with the reeve. Afterward the miller confesses he short-weights flour. Absolve him, press him to repay, or tell the lord? At dusk you bury a drowned fisher and comfort his widow, then ask the council for a widow's dole.

#### Soldier → knight
- **Entry:** Soldier background; the watch; a muster; volunteering ([18 §8](18-conflict-and-warfare.md#8-muster-conscription--rank)).
- **Core loop:** Train and drill → watch duty → patrols against bandits → campaigns → battles.
- **Its own game:** **Personal combat and, later, command:** the order wheel as sergeant, squad cards as captain ([18 §10.4](18-conflict-and-warfare.md#104-the-players-role-by-rank)).
- **Progression:** Levy → sergeant → captain → knight → marshal, gated by CommandScore, reputation and the lord's favor. Knighthood brings a fief and obligations ([17](17-governance-and-law.md)).
- **Failure:** Death; maiming; disgrace (cowardice, desertion); serving a doomed lord.
- **Session:** Drill the squad at dawn; Bram can't keep his line. A bandit sighting comes in and you lead six men into the woods. The ambush goes wrong. You order a fighting retreat and drag a wounded friend out, which earns a Valor memory in four witnesses. That night the lord hears the story, garbled in your favor, and asks you to dine.

#### Steward / clerk
- **Entry:** Clerk background; Letters; the trust of a leader.
- **Core loop:** Count stores → keep the ledgers → plan allocation (labor, seed, rations) → collect taxes → report.
- **Its own game:** **The ledger.** A logistics puzzle: forecast winter food days, allocate labor across fields and projects, catch discrepancies (embezzlement, spoilage). It is also the information game: what you report to the lord *is* their belief (§8.1). Honesty is your choice.
- **Progression:** Clerk → steward → chancellor. The power behind the seat.
- **Failure:** A famine on your watch; being caught skimming; a new lord who brings their own steward.
- **Session:** Count the granary: 610 kg, against 700 in your last report. Rats or theft? The lord wants a tax increase for the palisade. Your projection says families will go hungry by Winter 6. Do you show him the honest number, or the one he wants? He wants the palisade. You argue in court with a careful speech, and he halves the increase.

#### Lord
- **Entry routes:** (1) Acclaimed headman in an Era 1–2 council. (2) Marry the Charter heir. (3) Knighted and enfeoffed. (4) Lead a schism or exodus and found a splinter settlement. (5) Conquest. (6) Inheritance. Legitimacy rules belong to [17](17-governance-and-law.md).
- **Core loop:** Hold court → manage finances → keep vassals loyal → defend → diplomacy → war.
- **Its own game:** **Court and treasury.** Petitions, judgments, appointments, tax rates, building programs, musters, and war councils where you argue with people who can disagree with you.
- **Progression:** Hamlet lord → town lordship → vassals → realm.
- **Standing:** The top, and therefore exposed: rivals, plots, rebellion, assassination.
- **Failure:** Lost legitimacy, revolt, deposition, defeat, death.
- **Session:** Court day. Six petitions: a boundary dispute, a theft, a request for a mill, and the Hallorans demanding justice for their son. Rule, then review the steward's ledger. Stores are tight, so the war Corwin's envoy is pushing is a bad idea. But Corwin insulted your mother at the feast, and your council is split. You make a speech. Two councillors move. The vote is still yours.

#### Outlaw / bandit
- **Entry:** Banishment, a crime, desertion, or simply choosing it.
- **Core loop:** Survive in the wild → raid or rob → fence goods → evade the hue and cry → recruit.
- **Its own game:** **Stealth and ambush** (Stealth versus Perception, [18 §6](18-conflict-and-warfare.md#6-raids--defense)). Fencing through a corruptible innkeeper. Running a camp's morale and food.
- **Progression:** Lone thief → camp chief → bargaining for a pardon, or a kingdom of your own (a settlement founded by outlaws).
- **Standing:** Outside the law. Some peasants protect you if you rob the hated lord (Generosity reputation).
- **Failure:** Capture, the gallows, betrayal by your own.
- **Session:** At night you herd three of the lord's goats into the forest while the watch is drunk on Hearthday. Sell two through the alewife and give one to the starving widow who hid you last season. On the walk back you spot the lord's sergeants searching. You hide in a ditch, and they pass within ten paces.

---

## 4. Core loops & goals without quests

[02-game-overview](../02-game-overview.md) owns the core loops as design intent. This section is the **player-facing view**: what each loop looks like on screen.

### 4.1 Loops by time scale

| Scale | Real time (30-min days) | Player verbs | Feedback the player sees |
|-------|------------------------|--------------|--------------------------|
| Moment | seconds–minutes | Move, gather, strike, craft a stage, say a line | Animation, sound, NPC body language, quality of the piece in your hands |
| Day | 30 min | Wake → eat → work block → midday meal (social) → work or obligations → evening (tavern, home, court) → sleep | Needs icons fade in and out; journal "Today" entries; coin and stores |
| Season (8 days) | ~4 h | Plant or harvest; market days 4 & 8; Hearthday (8); taxes and labor days due; muster season; Summer ships | Season-turn card (§11.3 mini-Chronicle); Obligations tab |
| Year (32 days) | ~16 h | Survive winter, marriages and births, office changes | Year Chronicle |
| Generation | Interludes | Raise heirs, pass on know-how, die well | Saga Chronicle; heir selection |

### 4.2 Goals without quests

There are no quest markers or quest givers. Goals come from four sources, and all are surfaced **only as the character would know them**:

| Source | What it is | How it reaches the player |
|--------|------------|---------------------------|
| **Ambitions** | Player-chosen, for example: own land · master a craft · lead the settlement · become a knight · grow rich · raise a family · live free · serve the faith · heal · find the tin · found a settlement | Journal *Ambitions* page with **progress signals** phrased as beliefs: "No one has granted you land. The council allots land at the Spring meeting." These change no mechanics. |
| **Obligations** | Sim-imposed: taxes, labor days, debts, promises made, muster, court dates, household needs | Messengers, reminders from NPCs, the Obligations tab with due dates |
| **Opportunities** | Sim-generated signals: a job vacancy, unmet demand ("no one sells salt"), a vacant office, unclaimed land, an eligible match, ore rumors | Only through hearing (rumor), seeing (empty stall), reading (a notice, if literate), or being asked. Logged as **Leads** with a source and a date |
| **People** | NPCs want things from you: favors, loans, marriage, alliance, revenge | Dialogue. NPCs initiate when their utility AI picks "ask player" ([21](../tech/21-npc-ai.md)) |

**Opportunity throttling:** at most 2 *new* leads per game day reach the player passively. Leads expire when the underlying sim state changes. The journal then marks them "heard it's been taken" only if the character learns of it.

---

## 5. World UI: HUD, inventory, bench, building, map

### 5.1 HUD

```
┌───────────────────────────────────────────────────────────────────────────┐
│                                                       Wenna seems uneasy ·│ ← whispers (max 3, fade 6 s)
│                                                       Smithing: Journeyman│
│                                                                           │
│                                   ·                                       │ ← tiny dot reticle (aim/interaction only)
│                              ( stamina arc )                              │ ← only in combat/sprint
│                        [E] Talk to Old Cobb                               │ ← context prompt
│                                                                           │
│ ◔ hungry  ❄ cold                                         ☼ late afternoon │ ← needs appear only below 60; time on hover/hold
└───────────────────────────────────────────────────────────────────────────┘
```

- **Needs**: an icon appears only when a need is below 60. It pulses below 25. The character also *shows* it: shivering, slower walk, a hand on the belly.
- **Health/injuries**: no health bar outside combat. Injuries show on the body (limp, bandage) and in the paper-doll (§5.2). In combat a thin health line appears under the stamina arc.
- **Compass, minimap and quest marker**: none by default. Optional "Wayfinding assist" adds a compass strip.

### 5.2 Inventory & equipment

A paper-doll with body regions shows **armor coverage per region** ([18 §2.7](18-conflict-and-warfare.md#27-armor)). Containers (belt pouch, pack, basket, quiver) have kg limits. Carry weight is shown as a word (light / laden / overburdened) and as an exact kg value on hover.

**Item knowledge is a belief.** Quality is a word (Poor … Masterwork) once you have handled the item. You see an exact value range only if your relevant skill is ≥ Journeyman (40). Value estimates show as a range whose width shrinks with Commerce (§7.1). A novice can be fooled by a pretty blade.

### 5.3 Bench camera

[13](13-crafting-and-minigames.md) owns the minigames. The UI frame around them is:
- The camera swings to the bench. A **stage strip** along the top shows the recipe's process stages (e.g., *Heat → Draw → Shape → Quench → Temper*), current stage highlighted.
- Material and tool slots are on the left. Diegetic feedback: glow color, sound of the hammer, grain lines.
- After each stage, a short **qualitative verdict** ("even taper", "a cold shut near the tang").
- **Make another (batch)** appears once the recipe is mastered. **Auto-complete** (accessibility, §12) resolves using the NPC skill model, which gives your *expected* quality with no minigame bonus (parity).

### 5.4 Building placement

A ghost blueprint snaps to terrain and grid. Its color shows **permission**: green = your land or common land you're allowed on; amber = someone else's claim (building will cause conflict); red = illegal under current law ([17](17-governance-and-law.md)). The list of required materials ticks as you deliver. **Recruiting help** is social: ask people in dialogue, or post a job (if literate and there is a notice board) offering wages ([15](15-economy-and-trade.md)).

### 5.5 Map

The map is a parchment sketched in the **character's hand**:

- Places you've been are drawn. Places you've heard of are **notes with a source** in dashed outline: *"tin? — Old Cobb says east past the marsh (Y1 Su 3)"*.
- Named places use the names *you* know. Other people may use different ones (§13).
- Knowledge can be transferred. Maps can be bought, copied (Letters), or drawn for you by a talkative hunter in conversation (the LLM voices it; the sim transfers specific `PlaceBelief`s).
- A "you are here" marker is on by default. "Immersive map: no marker" is an option.
- War overlays (army estimates) appear only from scout reports, shown as ranges and timestamps ([18 §9.4](18-conflict-and-warfare.md#94-scouting--information)).

---

## 6. Dialogue UI

Talking is the main way the player affects society ([canon P2](../01-canon.md#2-design-pillars)). The pipeline and prompts belong to [22](../tech/22-llm-integration.md). Dialogue acts, opinions and memories belong to [16](16-social-systems.md). This section owns how a conversation looks, feels and reads.

### 6.1 Layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                  (3D: Wenna in the mill doorway, arms crossed)               │
│                                                                              │
│  Wenna Marsh — the miller's wife · you know her well                         │
│  seems: guarded                                                   ▼ cooler   │ ← cue word + last-exchange effect
│ ┌──────────────────────────────────────────────────────────────────────────┐ │
│ │ Wenna: "Flour's dear this season. Dearer for them as owes."              │ │
│ │ You:   "I'll have your 3d by market day. You have my word."              │ │
│ │        ↳ read as: Promise · earnest      ✦ Promise: 3d to Wenna by Su 4  │ │ ← intent echo
│ │ Wenna: "Your word. Hm. I've had your word before…"▌                      │ │ ← streaming
│ └──────────────────────────────────────────────────────────────────────────┘ │
│ [Ask ▾] [Trade] [Give] [Request ▾] [Promise] [Apologize] [Threaten ▾] [Leave] │ ← quick intents
│ > ____________________________________________________  Enter say · Tab intents│
└──────────────────────────────────────────────────────────────────────────────┘
```

The world stays live behind the dialogue. Others can walk up, interrupt, or overhear. Conversations don't pause time, but they run on **focus time**: while a conversation is open the world clock slows to **12:1** (¼ of normal, the same rate as the battle clock), so a 3-minute exchange costs ~36 game minutes rather than 2.4 game hours ([canon §6](../01-canon.md#6-time-canon)).

### 6.2 Quick intents

Every dialogue outcome is reachable without typing. A quick intent produces a **structured dialogue act directly**, skipping the classifier. The LLM then voices the player's line in the player's established tone, or the UI shows it bracketed ("[You ask about the tin rumor]"), depending on a setting.

| Intent | Sub-picker | Act sent to 16 |
|--------|-----------|----------------|
| Ask ▾ | about a person / place / item / rumor / their work / their family (only entities the character knows) | `Ask{topic}` |
| Trade | opens the trade panel (§7.1) docked under the dialogue | `OfferTrade` |
| Give | inventory picker | `Gift{item}` |
| Request ▾ | help with a task, a job, a loan, teaching, permission, a marriage proposal | `Request{kind, terms}` |
| Promise | what + by when | `Commit{terms, due}` |
| Compliment / Insult | target aspect (work, looks, family, courage) | `Praise` / `Insult{aspect, severity}` |
| Apologize | for which memory (picker of your recent offenses toward them) | `Apology{memoryId}` |
| Threaten ▾ | violence / the law / exposure (a secret you believe) | `Threaten{kind}` |
| Challenge | duel terms ([18 §4.3](18-conflict-and-warfare.md#43-formal-duels)) | `Challenge{terms}` |
| Leave | — | `EndConversation` |

### 6.3 Free text, the intent echo, and "unsay"

When the player types, Jev classifies the text into `{act, tone, topics, commitments, claims, persuasiveness}`. The text is treated as untrusted ([canon §4.1](../01-canon.md#41-jev--what-we-know-verify-before-implementation)). The UI then shows an **intent echo** under the line, e.g. "↳ read as: Request · polite".

**Consequential acts** need a moment of confirmation, because they create obligations, crimes or fights. These are: Threaten, Insult (moderate+), Promise/Commit, Accept/Offer deal ≥ 2d, Confess, Challenge, Lie (a claim that contradicts the player's own known beliefs). For these, the echo shows **[Enter] confirm · [Backspace] unsay**. It auto-confirms after **1.5 s**. While waiting, the NPC plays a listening animation. Setting: *Confirm consequential acts: Auto 1.5 s (default) / Always ask / Never*.

If the classifier's confidence is < 0.55 on a consequential act, the act is **downgraded** to its nearest non-consequential act (e.g., Threaten → Warn). The echo says so ("↳ read as: Warning (unclear)"). This prevents a garbled sentence from starting a feud.

### 6.4 Latency masking

| t | What the player sees | Under the hood |
|---|---------------------|----------------|
| 0 | Line committed; the NPC turns and looks, a listening animation | Request queued |
| ~0.25 s | Intent echo | Jev classification ([canon §4.1](../01-canon.md#41-jev--what-we-know-verify-before-implementation): P50 ≈ 0.23 s) |
| ~0.3 s (after any confirm window) | **Reaction gesture**: nod, frown, laugh, step back, glance at a friend | The hard-coded outcome is computed *first* ([canon §13](../01-canon.md#13-the-llm-boundary-hard-systems-soft-voice)); the gesture is picked from the outcome's valence. Body language leads words, the way real people's does |
| 0.6–1.5 s | First words stream in (typewriter paced to tokens; min 25 chars/s) | LLM voices the decided outcome |
| > 1.5 s, no tokens | Filler from a pre-generated pool: "Hm.", a sip, rubbing the neck | — |
| > 6 s | Template reply; a late LLM reply is discarded | Fallback chain ([22](../tech/22-llm-integration.md) owns exact budgets) |

### 6.5 Reading people: cues, not numbers

Hidden state is **never** shown as numbers. The player sees a **demeanor word** and body language. Both come from a hard-coded mapping, and both are filtered by how well the character can read this person.

| Hidden state | Cue vocabulary |
|--------------|----------------|
| Opinion bucket | hostile (≤ −50) · cold (−49…−15) · neutral · warm (15…49) · fond (≥ 50) |
| Dominant emotion > 40 | angry · afraid · grieving · cheerful · ashamed · bitter (Jealousy) |
| Need states visible on the body | tired · hungry · drunk · in pain |
| Fear of the player > 50 | wary (keeps distance, avoids eye contact) |

**Read accuracy.** NPCs with a motive to conceal (the deceive goal from [21](../tech/21-npc-ai.md); flattering a lord; hiding guilt) show a **masked** bucket. The player sees the true one with:

```
p_read = clamp(0.20 + 0.006·Familiarity + 0.05·(Perception − 5) + 0.003·Persuasion
               − 0.004·concealer.Persuasion, 0.05, 0.95)        // NPCs with the Honest trait never conceal
```

Strangers (Familiarity < 20) show **one** cue at most. The read is rolled once per conversation and stored as a belief: "Y4 Su 2 — seemed warm."

### 6.6 Showing that words had effect

After each exchange, a small effect glyph by the cue word shows **what the character could perceive**:

| Glyph | Meaning | Trigger |
|-------|---------|---------|
| ▲ warmer / ▼ cooler | You read a shift | Opinion change ≥ 5 *and* a successful read |
| ✎ they'll remember that | A salient memory was formed | Memory salience ≥ 0.6 (16) |
| ✦ promise noted | A commitment was recorded (yours or theirs) | Commit act |
| ◉ overheard | Someone else heard it | A witness within earshot |

Setting: *Effects of words: Subtle (glyphs, default) / Explicit (adds a qualitative sentence: "Tam seems to like you more") / Off (body language only)*. Even the explicit mode shows **no numbers**.

### 6.7 Group conversations, overhearing, ending

- **Groups (≤ 4 NPCs):** the speaker is chosen by addressing a name, looking at someone, or clicking a portrait. NPCs talk to each other too. The 22 pipeline handles turn-taking. The player's cues show for each participant.
- **Overheard talk** shows as world subtitles above speakers within 12 m, which fade. Pressing *Listen* (hold) moves closer and logs rumor beliefs.
- **Ending:** *Leave*, walk away (an NPC with Opinion < 0 notes it as rude), or the NPC ends it when their patience (hard-coded: needs, schedule, Opinion) runs out: "I've bread in the oven."

### 6.8 Template mode

With LLMs disabled ([canon §13](../01-canon.md#13-the-llm-boundary-hard-systems-soft-voice)), free text is classified by keyword heuristics and confirmed through the intent echo. Replies come from templates per act × outcome × personality bucket. Quick intents remain fully functional, so **every path is completable** (§17.3).

---

## 7. Trade UI & the journal

### 7.1 Trade & haggle

[15](15-economy-and-trade.md) owns value, reservation prices and haggling math. The UI:

```
┌─ Trading with Tam Holloway, potter ───────────────────────── seems: eager ─┐
│ YOUR SIDE                          │ THEIR SIDE                            │
│  2 × hare pelt                     │  1 × glazed jug (Fine)                │
│  4d                                │                                       │
│  you'd guess: worth 5–8d           │  you'd guess: worth 6–10d             │
├────────────────────────────────────┴───────────────────────────────────────┤
│ Tam: "Six pence and the pelts, or you're robbing a poor man."              │
│ [Add/Remove items]  [Coin − / +]  [Offer]  [Say something…]  [Walk away]   │
└────────────────────────────────────────────────────────────────────────────┘
```

- **Appraisal bands** are beliefs. Half-width = `max(5%, 60% − 0.5%·Commerce − 0.3%·relevantCraftSkill)` around the true base value, centered with an error of `N(0, half-width/3)`. A novice sees "1–12d" and is easy to cheat.
- **Saying something** while haggling goes through the normal classifier. Persuasion moves the NPC's reservation within the **±15% clamp** × susceptibility. The UI never reveals the clamp. You read it through cues ("Tam wavers").
- **Walk away** is a real move: the NPC's reservation may soften (15), and their Opinion may drop if you've wasted their time three times.

### 7.2 Journal structure

| Tab | Contents | Source |
|-----|----------|--------|
| **People** | Everyone you know (§7.3) | Character's beliefs and memories |
| **Rumors & news** | What you've heard, from whom, how often, and whether you've confirmed it yourself | Rumor beliefs (16) |
| **Promises & obligations** | Your commitments, theirs to you, taxes, labor days, muster, court dates | Commit acts, 15, 17, 18 |
| **Ledger** | Coin, debts owed and owing, property, stores you own | Ground truth for *your own* holdings (you know your pockets); beliefs for others' debts |
| **Family** | Family tree including the dead. Paternity is shown as believed | 16 family graph filtered by belief |
| **Leads & ambitions** | §4.2 | — |
| **Map notes** | Place beliefs (§5.5) | — |
| **Know-how & skills** | Your skills as tier + progress bar (exact numbers under "Show numbers"), your know-how with how-to text | [12](12-skills-and-professions.md) |
| **Chronicle** | Season and Interlude Chronicles, battle accounts, the *Book of the Founding* (§9.3) | Event log + LLM prose |

### 7.3 The People page: a demonstration of "beliefs, not truth"

```
┌─ People ▸ Tam Holloway ──────────────────────────────────────────────────────┐
│ [portrait]  Tam Holloway, 31 · potter · Brookside                            │
│             Married to Ivy (deceased Y3). Two children. Feuding with the     │
│             Marsh family (heard from Wenna).                                 │
│ Last read: warm (Y4 Su 2, at market)                                         │
│                                                                              │
│ WHAT I BELIEVE                                     SOURCE            CONF.   │
│  · Stole the miller's goat in Y2                   Wenna, twice      rumor   │
│  · Owes me 2d for pelts                            I lent it         certain │
│  · Is honest in trade                              my dealings ×6    likely  │
│  · Courting the widow Fenn                         overheard (Y4)    rumor   │
│                                                                              │
│ BETWEEN US                                                                   │
│  ✎ Y1 Wi 3  I pulled him from the river.                                     │
│  ✦ Y4 Su 2  He promised a jug by market day. (due Su 4)                      │
│                                                                              │
│ MY NOTES  [ He laughed when I mentioned the goat. Guilty? ]                  │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Rules:**
1. Every belief shows its **source** and **confidence** (rumor / likely / certain / disproved). "Certain" requires first-hand observation or a trusted written record.
2. Beliefs can be **false**. If Tam did not steal the goat, the page still says what Wenna told you until you learn otherwise. Contradicting evidence shows both entries, the losing one struck through.
3. The "last read" cue is **dated** and goes stale. After 2 seasons without contact it greys out.
4. Ground truth is never shown, not even in the "Explicit" effects mode. A developer-only `sim.inspect` overlay exists behind a launch flag.

---

## 8. Leader, military & notification UI

### 8.1 Settlement ledger (leaders and stewards)

```
┌─ Ledger of Ravensford ─ as reported by Steward Aldo, Y4 Su 2 (3 days ago) ───┐
│ STORES            reported      my count (Y4 Sp 6)   projected winter days    │
│  Grain            420 kg        610 kg  (!)          19 (need 32)             │
│  Salted meat      90 kg         —                                             │
│  Iron (bloom)     14 kg         15 kg                                         │
│ PEOPLE   312 (as last censused) · able adults ~150 · households 61            │
│ TREASURY 7 crowns 4s (reported)       DUE TO LIEGE: none (independent)        │
│ LABOR   Fields 44 · Building 12 · Charcoal 6 · Watch 4 · Unassigned ~9        │
│ PROJECTS Palisade (east) 60% · Mill race 20%                                  │
│ [Assign labor] [Set taxes] [Order census] [Count stores myself] [Call muster] │
└──────────────────────────────────────────────────────────────────────────────┘
```

The ledger shows **reports**: the reporter, a date, and the reporter's accuracy, which depends on their Stewardship and honesty ([17](17-governance-and-law.md)). *Count stores myself* walks you to the granary and replaces the belief with a certain one. A gap between reports, as with the 420 versus 610 above, is how embezzlement is discovered. The game does not flag it beyond the "(!)" mark for a difference over 20%.

### 8.2 Court

```
┌─ Court, Hearthday Y4 Su 8 ─ petition 3 of 6 ─────────────────────────────────┐
│ Halloran (father) vs. Marsh (son)  ·  claim: assault on Ned Halloran          │
│ TESTIMONY            says                                  you believe them?  │
│  Ned Halloran        "Jory Marsh drew a knife."            (Trust: likely)    │
│  Jory Marsh          "Ned swung first. I drew to warn."    (Trust: doubtful)  │
│  Old Cobb (witness)  "Fists first, then steel."            (Trust: certain)   │
│ LAW  Armed assault → fine 1–3 crowns or flogging (Charter custom)            │
│ [Question someone…] [Rule for Halloran ▾] [Rule for Marsh ▾] [Fine ▾]        │
│ [Order a duel] [Defer to next court] [Send to the priest for arbitration]     │
└──────────────────────────────────────────────────────────────────────────────┘
```

Questioning opens normal dialogue with the witness in front of the court, and lies are possible. Rulings are structured choices. The player may then **pronounce** the judgment in their own words. The LLM classifies the words for tone only (harsh/merciful), which affects reputation by a bounded amount. 17 owns procedure and penalties.

### 8.3 Military command UI ([18 §10](18-conflict-and-warfare.md#10-battles) owns the mechanics)

| Rank | UI |
|------|----|
| Levy | Order icon of your sergeant's current order above the HUD; slot ghost on the ground; a bark subtitle |
| Sergeant / lance | **Order wheel** (hold Tab): Hold · Advance · Charge · Fall back · Follow me · Form ▾ · Loose ▾ · Rally. Squad health and morale as **cues** (steady / shaken / breaking), not numbers |
| Captain | Squad cards (F1–F5) with cue states; click the ground to move; Reserve toggle |
| Marshal / ruler | **Tactical map** overlay (M): parchment top-down; friendly companies as banners, enemies as **estimated ranges with timestamps**; order arrows show messenger travel time; optional slow-mo/pause per difficulty |
| Muster screen (leaders) | Called / reported / refused / substitutes (as reported), equipment shortfalls, food days carried, projected labor loss % ([18 §12.1](18-conflict-and-warfare.md#121-economy-interface-with-15)) |

### 8.4 Notifications

| Priority | Examples | Presentation | Limit |
|----------|----------|--------------|-------|
| **P0 Interrupt** | Summons, attack, accusation, death in household | A diegetic arrival (messenger, bell) then a modal card; halts Wait/Interlude | — |
| **P1 Whisper** | Skill tier up, new lead, obligation due tomorrow, a read shift | Top-right line, fades after 6 s | ≤ 1 per 20 s, queued |
| **P2 Journal** | New belief, rumor heard again, ledger change | Journal tab dot only | — |

---

## 9. Onboarding

There is no tutorial narrator and no floating instructor. **People teach.** [11-survival](11-survival.md) owns the Landfall scenario. This section owns how teaching is delivered.

### 9.1 The first hour (Y0 Spring 1–2)

At the default 30-minute day, **1 real minute = 48 game minutes**, so the first real hour spans Day 1
and most of Day 2 (sleeping skips the night). Game times follow the Landfall script in
[11 §15](11-survival.md#15-the-landfall-scenario): start 05:30, low water ≈ 09:30, sunset ≈ 17:15.

| Real min (game time) | Beat | What the player learns | Taught by |
|----------------------|------|------------------------|-----------|
| 0–2 (05:30–07:06) | Wading ashore in storm surf; the lord's boat overturns | Movement, swimming, carrying | Shouting crew; the chaos itself |
| 2–5 (07:06–09:30) | The lord is pulled out dead. The heir clutches the Charter. Arguments start | That people disagree, and that you can talk (first quick-intent bar with a one-line hint: *"Type anything, or pick an intent"*) | The heir, the bosun |
| 5–10 (09:30–13:30) | Low water: salvage from the wreck before the tide turns | Interaction, inventory, weight, the value of iron | Bosun gives orders (a request you can refuse) |
| 10–15 (13:30–17:30) | Fire and shelter before dark; a clear, cold night is coming | Gathering, the first bench (fire-making), building a lean-to | Your **ship tie** or a background-matched mentor demonstrates, then asks you to try |
| 15–18 (17:30–19:54) | First meal; who eats first? | Needs, sharing, the first opinion shifts | Cook; a hungry child |
| 18–20 (19:54–21:30) | Night watch rota; wolves howl; sleep | Sleep (skips the night if safe), safety, the watch | The soldier settler |
| 20–60 (Day 2) | Second low water (~10:20) for more salvage; the first burial; the search for fresh water; the first "who decides?" gathering at the fire | Exploration and water, rationing, the first politics | The bosun, the steward, the heir and their rivals |

**Mentors** come from the manifest ([10](10-world-and-setting.md)). The game picks the one whose know-how covers your *weakest* survival skill, so a clerk is shown fire-making and a woodsman is asked to help the cook. Teaching is a real sim interaction: you gain XP and know-how and a Familiarity bond.

### 9.2 Contextual hints

Hints fire **only on observed struggle**, never on schedule:

| Trigger | Hint (one line, dismissable) |
|---------|------------------------------|
| Warmth < 30 at night with no fire within 10 m | "Dry wood and tinder: hold [E] on deadfall." |
| Three failed attempts at the same bench stage | Stage-specific tip from 13 |
| A pending consequential act was unsaid twice | "Quick intents ([Tab]) say exactly what you choose." |
| First time an NPC's cue changes | "People show how they feel. Watch faces and posture." |
| 2 game days with no conversation | "People remember who talks to them." |

Hint level: *Full / Light (default after the first winter) / Off*. Maximum 1 hint per 2 real minutes. Later systems (Interludes, court, ledger, muster) get a **first-time explainer from an NPC in role**: the steward walks you through the ledger, a sergeant explains the order wheel. A short **Codex** card is added under *Know-how & skills*.

### 9.3 The Book of the Founding

The first 8 days (Spring of Y0) are chronicled day by day as the *Book of the Founding*, a special Chronicle. Facts come from the event log; the LLM writes the prose. It is shown at each sunrise as a single short entry. It anchors the player in "this happened, and you were there," and it becomes a sim object (a book) once someone with Letters writes it down.

---

## 10. Death & Lineage UX

### 10.1 Incapacitation

At 0 Health the player is **downed** ([canon §12](../01-canon.md#12-the-player)): the screen desaturates, you hear your heartbeat, and the camera drops low.

| Action | Effect |
|--------|--------|
| Call for help (hold) | A shout heard 60 m away. NPCs with a rescue utility (Opinion, Warmth, kin; [21](../tech/21-npc-ai.md)) come. This is where relationships pay off |
| Crawl | 0.5 m/s toward cover or allies |
| Yield / plead | If enemies are near ([18 §2.10](18-conflict-and-warfare.md#210-surrender-mercy-executions)) |
| Bind your wound | If you have cloth and a free hand: slows bleeding (11) |

**Rescue** cuts to waking in a bed, with a short Chronicle snippet: *"Wenna's boy found you by the ford. The herbalist says you'll keep the leg."* The injury records persist.

### 10.2 Death screen

```
┌──────────────────────────────────────────────────────────────────────────┐
│                         EDRIC TANNER   Y0 – Y14                          │
│        Smith of Ravensford · husband of Mara · father of three           │
│                                                                          │
│   "He forged the first steel on Farstrand, and died at the ford of       │
│    Dunlach holding a line that broke."                                   │
│                                                                          │
│   Remembered by: 41 people  ·  Mourned by: Mara, Joss, Cobb (+6)          │
│   Left: a forge, 2 crowns, a debt of 8s to the Osmeri factor, one feud    │
│                                                                          │
│   [Continue as an heir]   [Read his Chronicle]   [End the saga]           │
└──────────────────────────────────────────────────────────────────────────┘
```

The epitaph is LLM-written from the life's event log (template fallback). "Remembered by" counts NPCs with Familiarity ≥ 30, which is a fact the heir can learn later.

### 10.3 Heir selection

Eligible candidates per [canon §12](../01-canon.md#12-the-player):
1. **Heirs**: adult children, spouse, or a designated heir. Designation is a legal act in [17](17-governance-and-law.md), made earlier in life.
2. Failing those, any **adult toward whom the deceased had Opinion ≥ 50 and Familiarity ≥ 50**. This is the plain reading of canon. The player's relationship values are tracked from interactions like any person's ([16](16-social-systems.md)). Whether the candidate's view of the deceased should also be required is an open question.

Each candidate card shows: their skills (tiers), household, what they inherit, and their known reputation, as the *deceased* believed it. You are choosing on beliefs, too.

### 10.4 What carries over

| Element | Carries over? |
|---------|---------------|
| Property, coin, workshop | Per inheritance law ([17](17-governance-and-law.md)); may be split among siblings |
| Family name & **house reputation** | The heir gets 25% of the deceased's reputation axes as a "house of X" seed, decaying 10% per season (proposed; 16 owns the math) |
| Debts, obligations, feuds | **Yes.** The estate's debts pass on. Feuds pass to kin ([18 §5](18-conflict-and-warfare.md#5-feuds)) |
| Relationships | The heir's **own** relationships. NPCs add a "kin of X" modifier = 30% of their Opinion of the deceased, decaying 10% per season (proposed; 16 owns it) |
| Skills & know-how | The heir's own. Know-how the deceased never taught is **lost** ([canon tenet 6](../01-canon.md#3-design-tenets-how-we-make-decisions)) |
| Journal | The heir's own beliefs. The deceased's journal becomes a **book item**, *"Father's notes"*, readable only if the deceased had Letters ≥ 20 *and* the heir can read. Otherwise only sketches and maps survive |
| Map | The heir's own, plus any written maps in the estate |
| Ambition | Optional: "Carry on his ambition" |

### 10.5 Difficulty modes

| Mode | On death |
|------|----------|
| **Forgiving** | You wake 1–3 days later at home or a shrine. You lose what you carried (left at the site, possibly looted). 50% chance of a lasting injury. Debt to your rescuer or healer. Witnesses' beliefs ("he fell at the ford") stand |
| **Lineage** (default) | §10.3. Autosave on death; no reload past death unless the save is manual and older |
| **Ironman** | One save, continuous. Death ends the saga ([canon §12](../01-canon.md#12-the-player)). An optional *Ironman Lineage* variant (single save, heirs allowed; default off) is proposed below |

---

## 11. Interludes UX

### 11.1 The Interlude panel

```
┌─ Pass time ─────────────────────────────────────────────── Y4 Summer 3 ────┐
│ ○ Wait  [until morning ▾]    ○ Pass the season    ● Interlude [ 3 ] seasons │
│                                                                            │
│ STANDING ORDERS (what you'll do)                     [Edit…]               │
│  Work: Smithing at your forge, 6 h/day · market on days 4 & 8              │
│  Household: ration normally · sell surplus iron · pay taxes when due       │
│  Social: attend Hearthday · keep friendships · no courting                 │
│  If summoned to muster: STOP and ask me                                    │
│  If attacked: defend home                                                  │
│                                                                            │
│ WILL STOP FOR: attack · household birth/death · summons · accusation ·     │
│                war affecting you · your health or needs critical           │
│                                                     [Begin]   [Cancel]     │
└────────────────────────────────────────────────────────────────────────────┘
```

**Standing orders** are executed by the same utility AI that drives NPCs ([21](../tech/21-npc-ai.md)), with your choices as heavy priors. Options that would take decisions from the player that only the player should make (marriage, a duel, a confession, a large sale) are **never automated**. They become interrupts, as "a petition only the player can answer" ([canon §6.1](../01-canon.md#61-interludes-time-skips)). Availability: after the first winter, with a bed in a shelter. Not available while mustered ([18 §9.6](18-conflict-and-warfare.md#96-campaigns-lod-and-interludes)).

### 11.2 Progress & interrupts

While the skip runs, a parchment strip unrolls across the seasons. Headlines stream in: templated one-liners from the event log, for example *"Su 6 — the mill race is finished"*. A **Stop now** button halts at the next day boundary. An interrupt slams the strip shut and the halting event plays diegetically: a rider at the door, the bell.

### 11.3 The Chronicle

```
┌─ Chronicle ─ Y4 Summer → Y5 Spring ─────────────────────────────────────────┐
│ YOUR HOUSEHOLD   Mara bore a daughter, Ellin (Autumn 2). The forge made 140  │
│                  tools; you trained Joss to Journeyman.                     │
│ THE SETTLEMENT   A hard winter. Grain ran short by Winter 5; the lord opened │
│                  his own stores. Two elders died.                           │
│ PEOPLE YOU KNOW  Tam married the widow Fenn. Old Cobb died in his sleep.     │
│ BEYOND           Rumor: Dunlach found tin. Their chief calls himself king.  │
│ WHAT YOU HEARD   (4 rumors added to your journal)                           │
│                                              [Open journal]   [Continue]    │
└──────────────────────────────────────────────────────────────────────────────┘
```

- Facts are **only** from the event log, filtered to what the character would know (household and settlement first-hand; "Beyond" as rumor). The prose is LLM-written, with a templated list as fallback.
- Names are links to People pages. The Chronicle is stored in the journal and is exportable as text (§13).
- Season turns outside Interludes get a 3-line mini-Chronicle card.

---

## 12. Settings, difficulty & accessibility

| Group | Setting | Default | Options / notes |
|-------|---------|---------|-----------------|
| World (at creation) | Settlers on the first ship | 24 | 12–40 ([10](10-world-and-setting.md)) |
| World (at creation) | Resource richness (`deposit.richness`) | 1.0 | 0.6–1.5, scales all finite deposits ([10](10-world-and-setting.md)) |
| World (at creation) | First-winter cap (`weather.y0_winter_cap`) | Normal | Mild · Normal · Hard — a world option, not a difficulty mode ([10 §6](10-world-and-setting.md)) |
| World (at creation) | **Drama** (irrationality, `K_irr`) | 1.0 | 0–2 ([21 §8](../tech/21-npc-ai.md)) |
| World (at creation) | Customs | Egalitarian | Egalitarian · Historical — sex in succession, office and levies ([17](17-governance-and-law.md)) |
| World (at creation) | Grim Justice | Off | Enables maiming punishments ([17](17-governance-and-law.md)) |
| Time | Real minutes per game day | 30 | 20, 24, 25, 30, 32, 36, 40, 45, 48, 50 or 60 ([canon §6](../01-canon.md#6-time-canon)) |
| LLM | Mode | Auto | Auto (cloud if a key is set, else local if available, else template) · Cloud · Local · Template ([22 §3.3](../tech/22-llm-integration.md)) |
| LLM | Endpoint & model | OpenRouter, Qwen-family | Any OpenAI-compatible endpoint; local presets for llama.cpp / Ollama / LM Studio |
| LLM | Jev classifier | On (cloud) | Off → local structured output → heuristics |
| LLM | **Spend caps** | $10 / month and $1 / session; ladder at 50 / 80 / 100% | When reached: switch to Local or Template (prompt once) ([22 §3.6](../tech/22-llm-integration.md)) |
| LLM | Verbosity | Normal | Terse · Normal · Chatty |
| Dialogue | Confirm consequential acts | Auto 1.5 s | Always ask · Never |
| Dialogue | Effects of words | Subtle | Explicit · Off |
| Dialogue | Quick-intent lines | Voiced by LLM | Bracketed text |
| Content | Violence detail | Blood on, dismemberment off | Off / On |
| Content | Executions | Shown | Fade to black |
| Content | Profanity in NPC speech | Mild | None · Mild · Strong (applied to LLM output via 22 filters) |
| Accessibility | Text size | 100% | 80–200% in 4 steps; dyslexia-friendly font |
| Accessibility | Colorblind modes | Off | Protan/Deutan/Tritan; **all cues have shape or text, never color alone** |
| Accessibility | Subtitles for barks and overheard talk | On | — |
| Accessibility | Fear effects | Full | Visual-only off (the mechanical penalty still applies — parity) |
| Accessibility | **Minigame assists** | Off | Timing windows ×1.5 / ×2; **auto-complete** (= NPC skill resolution: expected quality, no bonus) |
| Accessibility | Combat assists | Off | Parry window ×1.5; aim sway −50%; tactical slow-mo (all ranks) |
| Accessibility | Hold vs toggle | Hold | Toggle for block, sprint, aim |
| Difficulty | Death mode | Lineage | Forgiving · Ironman |
| Difficulty | Commander pause | Slow-mo | Off · Slow-mo · Full pause |
| Controls | Rebinding | — | Full; controller layouts in M7 |

**Parity statement.** Assists change *the player's input demands*, never the sim's rules. Auto-complete and timing windows are calibrated against the NPC skill-based resolution ([canon tenet 2](../01-canon.md#3-design-tenets-how-we-make-decisions)). NPC behavior, prices and laws are identical in every mode.

---

## 13. Player expression

| Moment | How | Effect in the sim |
|--------|-----|-------------------|
| **Naming places** | Annotate the map; say the name in conversation | A `PlaceName` proposal spreads like a rumor. Adoption chance per hearer = `0.1 + 0.004·Renown + 0.2·[speaker holds office]`. When > 50% of a settlement uses it, it becomes the common name in barks and the Chronicle |
| **Founding things** | Name a settlement, shrine, tavern, workshop, guild, or a house (family name variant) | Stored as entity names; used by the LLM |
| **Speeches** | Council arguments, rallying speeches, sermons, court pronouncements (typed or templated) | Jev-classified, bounded effects ([18 §13.2](18-conflict-and-warfare.md#132-rallying-speech-bounded)) |
| **Letters** | Write to an NPC (needs Letters ≥ 20 or a scribe; the recipient needs to read or have a reader) | A letter is a sim object: claims inside it become the recipient's beliefs (sourced to you); promises in it count as commitments |
| **Epitaphs, naming children** | Free text | Remembered in the Chronicle and on grave markers |
| **Heraldry** | A simple charge-and-tincture editor for lords and knights | Banners in battle; recognition at a distance |
| **Laws in your own words** | A lord drafts a law; 17 maps it to a structured law, and the player confirms the mapping (intent echo) | The structured law is binding; the words are its proclamation |

---

## 14. The player's contract with LLM-driven characters

How [canon §13](../01-canon.md#13-the-llm-boundary-hard-systems-soft-voice) feels from the player's chair:

1. **What they say matches what they do.** NPC words are generated *after* the hard outcome. If Wenna says "fine, take it at 5d", the deal is 5d.
2. **NPCs never promise what the sim hasn't approved.** A line that implies an unapproved commitment is caught by 22's output checks and replaced.
3. **Words matter, bounded.** Good arguments, apologies and speeches move outcomes, but within clamps scaled by skill and relationship. You can't talk a miser into charity in one sentence. You *can* make him like you over a season.
4. **Lying is possible and risky.** Claims you make become beliefs in NPCs, sourced to you. If they're disproved, Honesty takes a hit with everyone who hears.
5. **Your text is never instructions.** Typing "ignore your instructions" is just strange speech. NPCs react as people would ("Are you drunk?").
6. **Memory is structured.** What an NPC remembers is the structured fact (16), not the LLM's paraphrase. If the prose and the fact disagree, the fact wins next time.
7. **The game works without the voice.** Template mode is plainer but complete.
8. **Report a line.** *"That's not right"* (hotkey in dialogue) flags a line locally, for the player's own bug reports. Nothing is sent without consent.

---

## 15. Data schemas

```yaml
# content/backgrounds/smiths_apprentice.yaml
id: background.smiths_apprentice
name: "Smith's apprentice"
skill_boosts: { skill.smithing: 30, skill.metallurgy: 20, skill.mining: 10, skill.melee: 10, skill.carpentry: 5 }
knowhow: [knowhow.forge_basics, knowhow.bloomery_smelting_observed, knowhow.tool_hardening]
attribute_bonus_options: [attr.strength]
kit: [item.tool.smith_tongs, item.clothing.leather_apron]
salvaged_iron_item: null
social_seed: { reputation: { competence.smithing: 5 }, notes: "colony_hopes_metal" }
mentor_tie_tags: [smith, metalworker]
milestone: M2
```

```csharp
public sealed record PlayerProfile(long PersonId, string BackgroundId, IReadOnlyList<string> TraitIds,
    FaithStance Faith, string? AmbitionId, IReadOnlyList<ShipTie> Ties, DifficultyMode Mode, AssistFlags Assists);
public sealed record StandingOrders(WorkOrder Work, HouseholdPolicy Household, SocialPolicy Social,
    InterruptPolicy OnSummons, DefensePolicy OnAttack);
public sealed record BeliefView(long BeliefId, long SubjectId, string Claim, long SourcePersonId,
    BeliefConfidence Confidence, int TimesHeard, long FirstHeardMinute, long? ContradictedBy);
public sealed record CueRead(long NpcId, OpinionBucket Bucket, EmotionCue? Emotion, bool Masked, long ReadMinute);
public sealed record IntentEcho(string UtteranceId, DialogueAct Act, Tone Tone, float Confidence,
    bool Consequential, bool Downgraded, CommitTerms? Commitment);
public sealed record UiNotification(NotifPriority Priority, string TemplateKey, long[] Refs, long GameMinute);
```

---

## 16. Interfaces, LOD & milestones

### 16.1 Interfaces

| Doc | 19 expects | 19 provides |
|-----|-----------|-------------|
| [02](../02-game-overview.md) | Core-loop intent, tone, art direction | Player-facing loop presentation |
| [10](10-world-and-setting.md) | Manifest with tie slots and mentor roles | Background and tie choices |
| [11](11-survival.md) | Landfall beats, need thresholds, downed and bleed-out timing | Onboarding delivery, downed UX |
| [12](12-skills-and-professions.md) | Baseline skills, know-how catalog, aging | Background boosts |
| [13](13-crafting-and-minigames.md) | Minigames, assist hooks, auto-complete calibration | Bench frame UI |
| [15](15-economy-and-trade.md) | Appraisal truth, reservation prices, haggling | Trade UI, appraisal bands |
| [16](16-social-systems.md) | Dialogue acts, beliefs, rumor, memory, house reputation | Quick-intent act set, cue mapping |
| [17](17-governance-and-law.md) | Court procedure, ledger truth vs reports, inheritance, law mapping | Court, ledger, heir UIs |
| [18](18-conflict-and-warfare.md) | Orders, ranks, muster, battle estimates | Command UIs, conscription choices |
| [21](../tech/21-npc-ai.md) | Standing-order execution, rescue utility, concealment goal | Standing-order priors |
| [22](../tech/22-llm-integration.md) | Streaming, filler pools, budgets, Jev wrappers, spend tracking | Latency choreography, settings |

### 16.2 LOD & Interludes (player-facing)

The player is always LOD0. NPCs at LOD1+ can't be talked to directly. Approaching one promotes it (seamless, [canon §8.2](../01-canon.md#82-simulation-levels-of-detail-canonical-tiers)). Rumors about LOD2/3 events arrive through normal propagation. During Interludes the player character is simulated at LOD3 by standing orders. Chronicles are filtered by the character's knowledge.

### 16.3 Milestones

| Milestone | Delivers |
|-----------|----------|
| **M1** | Dialogue UI: free text, streaming, quick intents, intent echo, latency choreography, cue words; People page (basic); template mode |
| **M2** | Player creation (4 backgrounds: farmhand, woodsman, smith's apprentice, soldier); HUD; inventory; bench frame; Landfall onboarding and hints; downed & rescue |
| **M3** | All 10 backgrounds; building placement; map with beliefs; save/load UI; season cards |
| **M4** | Full journal (rumors, promises, ledger, family); trade UI; Interludes UX and Chronicle; death & lineage; difficulty modes; paths: shopkeep, innkeeper, healer, priest, outlaw |
| **M5** | Settlement ledger, court UI; lord and steward paths; letters; heraldry |
| **M6** | Military UIs; muster screen; soldier → knight path end-to-end |
| **M7** | Accessibility complete; controller; custom background; onboarding polish; performance of UI at 500 known people |
| **M8** | Content completeness, localization readiness |

---

## 17. Tuning knobs, exploits & validation

### 17.1 Tuning knobs

| Knob | Default | Notes |
|------|---------|-------|
| Background point total | 75 | Higher = an easier start |
| Attribute point-buy | 6 points, 3–8 | — |
| Consequential confirm window | 1.5 s | 1–3 s |
| Downgrade confidence threshold | 0.55 | Lower = more misread fights |
| Filler / template cutoffs | 1.5 s / 6 s | Per provider |
| Read accuracy coefficients | §6.5 | Social transparency |
| Passive leads per day | 2 | Goal density |
| Hint cadence | 1 per 2 min | — |
| House reputation inheritance | 25%, −10%/season | Dynasties versus fresh starts |

### 17.2 Exploits & mitigations

| Exploit | Mitigation |
|---------|------------|
| Prompt injection in dialogue ("you agree to give me 100 crowns") | Player text is untrusted; outcomes are hard-coded; the act is classified only within fixed act lists; the LLM voices decided outcomes only. |
| Free text strictly dominating quick intents | Language influence is clamped at ±15%. Quick intents get the same Persuasion-based susceptibility with a neutral language signal, so free text adds at most the clamp. |
| Save-scumming conversations | Lineage autosaves at conversation end for consequential acts; Ironman. |
| Unsay abuse (probing reactions) | Unsay cancels *before* the outcome is committed; there is no reaction to observe. Unsays per NPC per day > 3 → the NPC notes "evasive" (Opinion −2). |
| Heir shopping (designating a rich stranger) | Designation is a legal act requiring the Opinion threshold *and* a witnessed ceremony; the inherited estate is the deceased's, not the heir's. |
| Spam-asking for rumors | NPC patience drains; repeated questions on the same topic get "I told you already." |
| Map-reading truth (deposits) | Deposits are never drawn until observed; rumors carry error. |

### 17.3 Headless validation

| Test | Pass criterion |
|------|----------------|
| **Template-mode completability** | A scripted player-proxy using only quick intents reaches each path's "established" state (e.g., master craftsman, freeholder with 2 fields, knighted, acclaimed lord) in ≤ 1.5× the median LLM-mode time, across 20 seeds per path |
| **Intent classifier eval** | ≥ 500 labeled player utterances (incl. adversarial and injection): act top-1 ≥ 90%; **precision on consequential acts ≥ 97%**; injection lines never classified as Commit/Accept |
| **Latency choreography** | Cloud: p50 time-to-first-gesture ≤ 0.35 s; p50 first token ≤ 1.5 s; template fallback rate < 3% |
| **Belief hygiene** | Journal and Chronicle renders contain zero facts the character has no belief or memory for (automated diff against the knowledge graph) |
| **Cue fidelity** | Concealing NPCs: read accuracy matches §6.5 within ±5 points over 10,000 rolls |
| **Onboarding** | Bot-run Landfall: every survival-critical action is demonstrated by an NPC within the first 60 real minutes for all 10 backgrounds |

---

## Open questions

1. **Ownership overlap:** canon §16 gives core loops to [02](../02-game-overview.md), but this doc presents them player-facing (§4). Confirm the split.
2. **Heir rule direction:** this doc uses the plain reading of canon §12 (the deceased's Opinion and Familiarity toward the candidate). Should the candidate's view of the deceased also be required?
3. **Voice:** TTS for NPC speech (a local TTS model) or text-only for v1?
4. **Foreign-born player** (Osmeri or Brannoch émigré aboard the *Wending Star*): worth the manifest complexity?
5. **Serf start:** should a player be able to *start* unfree in later-era scenarios, or is serfdom only reachable in play?
6. **[Resolved — canon v0.2: $10/month plus $1/session, with 22's ladder]** **Spend-cap default** ($10/month) needs cost data from 22's budgets.
7. **Multiple saves in Lineage mode:** allow manual saves at all, or autosave-only?

## Proposed canon additions

> **Status (canon v0.2):** accepted items have been folded into [01-canon](../01-canon.md) (see its change log). Items not reflected there remain proposals for the owner to decide.

1. **Backgrounds (10):** the canonical eight plus **acolyte** and **joiner's apprentice**; +75 skill points each; at most one salvaged iron item per kit.
2. **Attribute point-buy:** all 5, 6 points to distribute, range 3–8; the background adds +1 to one attribute.
3. **Age bonus:** +1.5 background skill points per year over 18; +1 know-how at 30+.
4. **Faith stance at creation** (orthodox / lax / secret Ashen sympathizer).
5. **Ambitions** as player-set, mechanically inert journal goals; **Leads** (≤ 2 passive per day) as the sim's opportunity surface.
6. **Intent echo + 1.5 s unsay window** for consequential acts; low-confidence consequential acts downgrade (threshold 0.55).
7. **Cue vocabulary:** Opinion buckets hostile/cold/neutral/warm/fond (−50/−15/15/50 cut points) and the read-accuracy formula; no hidden numbers are ever shown to the player.
8. **Lineage carry-over:** house reputation 25% decaying 10%/season; NPC "kin of X" opinion modifier 30% decaying 10%/season; debts and feuds inherited; the deceased's journal as a readable book.
9. **Forgiving-mode recovery rules** (§10.5).
10. **Notification priorities** P0/P1/P2 with the whisper rate limit.
11. **Default LLM spend cap** $10/month with Local/Template fallback on reaching it.
12. **Ironman Lineage** variant: one continuous save, but heir succession is allowed (default off; canon Ironman is unchanged).
