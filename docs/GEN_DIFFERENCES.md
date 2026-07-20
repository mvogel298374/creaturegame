# Pokémon Generational Differences

A reference doc covering every major system across all main-series generations — what was added, changed, overhauled, or removed. Intended to eventually feed into player-facing documentation for the battle sim.

---

## Contents

1. [Type Chart History](#type-chart-history)
2. [Battle Gimmick Timeline](#battle-gimmick-timeline)
3. [Stat System Evolution](#stat-system-evolution)
4. [Generation I — Red / Blue / Yellow (1996)](#generation-i--red--blue--yellow-1996)
5. [Generation II — Gold / Silver / Crystal (1999)](#generation-ii--gold--silver--crystal-1999)
6. [Generation III — Ruby / Sapphire / Emerald (2002)](#generation-iii--ruby--sapphire--emerald-2002)
7. [Generation IV — Diamond / Pearl / Platinum (2006)](#generation-iv--diamond--pearl--platinum-2006)
8. [Generation V — Black / White (2010)](#generation-v--black--white-2010)
9. [Generation VI — X / Y (2013)](#generation-vi--x--y-2013)
10. [Generation VII — Sun / Moon (2016)](#generation-vii--sun--moon-2016)
11. [Generation VIII — Sword / Shield (2019)](#generation-viii--sword--shield-2019)
12. [Legends: Arceus (2022) — Spin-off / Gen 8 era](#legends-arceus-2022--spin-off--gen-8-era)
13. [Generation IX — Scarlet / Violet (2022)](#generation-ix--scarlet--violet-2022)
14. [Cross-Generation System Timelines](#cross-generation-system-timelines)

---

## Type Chart History

| Generation | Types | Changes |
|:-----------|:-----:|:--------|
| I | **15** | No Steel, Dark, or Fairy |
| II | **17** | **+ Steel** (immune to Poison; resists 11 types), **+ Dark** (immune to Psychic; resists Ghost/Dark) |
| II | 17 | Ghost → Psychic fixed from 0× to **2×**; Poison→Bug and Bug→Poison both changed from 2× to ½× |
| VI | **18** | **+ Fairy** (immune to Dragon; resists Fighting/Bug/Dark; weak to Steel/Poison). Steel loses Ghost and Dark resistances (both become 1×) |
| IX | 18 | No new types. Hail weather condition renamed/reworked into **Snow** |

---

## Battle Gimmick Timeline

| Generation | Gimmick | One Per Battle? | Notes |
|:-----------|:--------|:---------------:|:------|
| I–V | None | — | No per-battle power gimmick |
| VI | **Mega Evolution** | ✓ (per trainer) | Requires Mega Stone + Key Stone; boosts stats, sometimes changes type/ability |
| VII | **Z-Moves** | ✓ (per trainer) | Type crystal held by Pokémon; converts move into massive Z-Move; coexists with Mega |
| VIII | **Dynamax / Gigantamax** | ✓ (3 turns) | Replaces Mega and Z-Moves in SwSh; Max Moves have secondary weather/terrain effects |
| IX | **Terastallization** | ✓ (recharge at Pokémon Center) | Changes Pokémon's type to its Tera Type; STAB rules reworked around it |

---

## Stat System Evolution

| Feature | Gen I–II | Gen III+ |
|:--------|:---------|:---------|
| Individual Values | DVs: **0–15** per stat | IVs: **0–31** per stat |
| Base stat bonus | **Stat EXP**: uncapped, gained from every KO, complex square-root formula | **EVs**: 252 per stat cap, 510 total cap, 4 EVs = 1 stat point |
| Gender tied to IVs? | Yes (Attack DV determined gender in Gen 1–2) | No (Gen 3+: gender separate from IVs) |
| Natures | None | 25 natures; +10% to one stat, −10% to another (5 neutral) |
| Abilities | None | One ability per Pokémon; species has up to 2 normal options (Gen 3); Hidden Ability added (Gen 5) |
| Special stat | Single **Special** stat | Split into **Sp. Atk** and **Sp. Def** (Gen 2) |

---

## Generation I — Red / Blue / Yellow (1996)

**Pokémon:** 151 (IDs 1–151) | **Moves:** 165 | **Types:** 15

### Battle Mechanics

#### Stats
- Special Attack and Special Defense are a single combined **Special** stat
- Critical hit chance based on base Speed: `floor(BaseSpeed / 2) / 256`
- High-crit moves: `min(floor(BaseSpeed / 2) × 8, 255) / 256`
- Critical hits ignore all stat stages (both attacker's and defender's)
- Critical hits ignore Burn's Attack penalty

#### Accuracy
- Internal scale: **0–255** (not 0–100)
- **1/256 miss bug**: any move — including 100% accuracy — misses if the RNG rolls 255, because the check is `roll < threshold` and 255 can never be less than 255
- Accuracy/Evasion stage multipliers use a different formula than Gen 2+

#### Move Category (Physical vs Special)
- Determined by **type**, not by individual move:
  - **Physical types:** Normal, Fighting, Flying, Ground, Rock, Bug, Ghost, Poison
  - **Special types:** Fire, Water, Grass, Electric, Psychic, Ice, Dragon
- Both attacking and defending use the same Special stat (no Sp. Atk vs Sp. Def distinction)

#### Damage Formula
```
Damage = floor(floor(floor(2×Level/5 + 2) × A × Power / D) / 50 + 2)
         × STAB × Type × Critical × (217–255)/255
```
Where A = Attack (or Special), D = Defense (or Special), with no Sp. Atk/Sp. Def split.

#### Type Chart Quirks (Gen 1 only)
- **Ghost → Psychic = 0×** (programming bug; intended 2×)
- **Poison → Bug = 2×** (later reversed)
- **Bug → Poison = 2×** (later reversed)
- **Ice → Fire = 1×** (neutral in Gen 1; stays this way through future gens on the Ice side)
- No Steel, Dark, or Fairy types

#### Binding / Trapping Moves
Wrap, Bind, Fire Spin, Clamp behave very differently in Gen 1:
- **Fully immobilise the target** — target cannot attack at all while trapped
- User is **locked into the move** for 2–5 turns
- Deals 1/16 max HP chip damage per turn
- Considered overpowered; used competitively to completely shut down opponents

#### Status Quirks
- **Hyper Beam**: does NOT require a recharge turn if it KOs the target
- **Focus Energy / Dire Hit**: bug causes it to **quarter** the crit rate instead of quadrupling it
- **Sleep**: Pokémon wakes up before acting — but **cannot act** on the turn it wakes
- **Burn**: halves the Attack stat; has no effect on Special (no Sp. Atk yet)
- **Poison**: causes damage outside battle; Pokémon **can faint** from overworld Poison in Gen 1
- **Toxic / Bad Poison**: escalating damage but switching in/out does not reset the counter
- **Badge boosts**: your own Gym Badges passively boost corresponding stats in battle
- PP is **not enforced for AI trainers** — opponents can use moves infinitely
- **Haze**: resets both battlers' stat stages, Confusion, Disable, Mist, Focus Energy, Leech Seed,
  Reflect, and Light Screen — but cures the non-volatile status (Sleep/Freeze/Burn/Paralysis/Poison)
  only on the **target**, never the user's own; a self-inflicted Toxic still downgrades to a regular
  Poison either way. An active Transform (or a Mimic'd move) is **not** reverted by Haze — only a
  battle-boundary reset undoes those. If Haze cures a target's Sleep/Freeze, that target still
  **forfeits its already-chosen action for that same turn** rather than getting to act immediately —
  it's simply free again from the next turn on.

#### No Systems (not yet introduced)
- No held items
- No abilities
- No natures
- No breeding or eggs
- No gender
- No shinies
- No weather conditions
- No day/night cycle
- No Double Battles

---

## Generation II — Gold / Silver / Crystal (1999)

**New Pokémon:** 100 (152–251) | **New Types:** Steel, Dark | **New Moves:** 86

### Battle System Changes

#### Stat Overhaul
- **Special stat split** into Special Attack and Special Defense — now 6 stats
- DVs remain 0–15 but are now per-stat (Attack/Defense/Speed/Sp. Def each 0–15; HP derived; Sp. Atk tied to Special DV)
- Shinies in Gen 2 are determined by specific DV combinations (Attack DV = 2/3/6/7/10/11/14/15 + others); approx. 1/8192 chance

#### Type Chart Fixes
- Ghost → Psychic corrected to **2×** (fixing the Gen 1 bug)
- Poison → Bug: 2× → **½×**
- Bug → Poison: 2× → **½×**
- Steel and Dark types added with full interaction tables

#### Move and Mechanic Fixes
- **Hyper Beam**: now requires recharge even after KOing a target
- **Binding moves** (Wrap, etc.): target can now **choose and use moves** while trapped (no longer fully immobilised); still cannot switch out
- **Poison outside battle**: now stops at 1 HP instead of causing faints
- **Sleep**: Pokémon can now act on the turn it wakes up (partial change — varies by version)
- **1/256 miss bug**: fixed
- Focus Energy crit bug: fixed

#### New Battle Systems
- **Held Items** in battle (Leftovers, berries, type-boosters, etc.)
- **Weather moves**: Rain Dance, Sunny Day, Sandstorm introduced; last 5 turns each
  - Sunny Day: boosts Fire moves ×1.5, weakens Water ×0.5
  - Rain Dance: boosts Water ×1.5, weakens Fire ×0.5
  - Sandstorm: end-of-turn damage to non-Rock/Ground/Steel
  - Steel types immune to Sandstorm chip damage

### New Game Systems
- **Gender** — Pokémon now have male/female (or genderless); affects some evolutions and breeding
- **Breeding** — Day Care; two compatible Pokémon of opposite gender produce an Egg
- **Shinies** — rare alternate-coloration Pokémon (~1/8192 base rate)
- **Friendship / Happiness** — unlocks evolutions (Espeon, Umbreon, Togetic, etc.)
- **Day/Night cycle** — real-time clock; affects encounters, evolution, and events
- **Pokérus** — rare buff that doubles EV gain; spreads to party Pokémon
- **Berries** — first appearance as static overworld items (not the Gen 3 growing system)
- **New evolution methods**: happiness-based, time-of-day-based, held-item-trading

---

## Generation III — Ruby / Sapphire / Emerald (2002)

**New Pokémon:** 135 (252–386) | **New Weather:** Hail

### Battle System Overhaul

#### Abilities (New System)
- Every Pokémon now has an **Ability** — a passive trait affecting battle (and sometimes overworld)
- Most species have two ability options (randomly assigned on encounter)
- Examples: Intimidate (lowers foe's Attack on entry), Static (30% paralysis on contact), Levitate (immune to Ground), Wonder Guard (only super-effective moves land)
- Completely new strategic layer; persists to all future generations

#### Natures (New System)
- 25 natures; each modifies one stat by +10% and another by −10%
- 5 neutral natures (no effect)
- Visible on Pokémon summary screen; permanent once set at capture/birth

#### EV / IV System Overhaul
- **IVs** expanded to **0–31** (from 0–15 DVs)
- **EVs** completely reworked: each defeated Pokémon yields 1–3 EVs in specific stats; 252 cap per stat; 510 total cap; every 4 EVs = 1 stat point at level 100
- Gender no longer derived from Attack IV — females can now have max Attack IVs

#### Double Battles (New Format)
- First appearance in the main series
- Both trainers send out 2 Pokémon simultaneously
- Moves can target specific slots or spread to both targets

#### Weather: Hail Added
- **Hail**: end-of-turn Ice-chip damage to all non-Ice-types (introduced for the first time)
- Weather from ability is now **permanent** until overwritten (Drizzle, Drought, Sand Stream, Snow Warning)
- Weather moves from Gen 2 (Rain Dance, Sunny Day, Sandstorm) still last 5 turns; ability-induced weather lasts indefinitely

#### Contests (New Non-Battle System)
- Pokémon have 5 contest stats: Cool, Beauty, Cute, Smart, Tough
- Raised by feeding Pokéblocks (made from berries)
- Contests have Appeal and Jam rounds judged on contest stats and move selection

### Removed / Changed
- **No day/night in Ruby/Sapphire** (time-based mechanics absent; restored in Emerald partially)
- **Save incompatibility**: Gen 3 is completely incompatible with Gen 1–2 (new data format)
- Physical/Special split still **type-based** (not per-move yet)

---

## Generation IV — Diamond / Pearl / Platinum (2006)

**New Pokémon:** 107 (387–493) | **New Entry Hazard:** Stealth Rock

### Physical/Special Split Per Move (Major Overhaul)

Before Gen 4, whether a move was Physical or Special depended on its type. Gen 4 gave **every move its own category**:

| Category | Description |
|:---------|:------------|
| **Physical** | Uses Attack vs Defense |
| **Special** | Uses Sp. Atk vs Sp. Def |
| **Status** | No direct damage |

- Fire Punch → Physical; Flamethrower → Special (both Fire, now separated)
- New moves added to give types both options (Zen Headbutt, Psycho Cut for Physical Psychic; Focus Blast for Special Fighting)
- Previously lopsided types (Ice, Electric, Grass) gained a full toolkit overnight
- Widely considered the most impactful single mechanical change in the series

### Other Battle Changes
- **Turn structure fix**: when a Pokémon faints mid-turn, the trainer is now prompted to send in a replacement **after the turn ends**, not immediately — prevents sending in a Pokémon to absorb a second attack on the same turn
- **Stealth Rock**: first entry hazard; deals Rock-type damage on switch-in proportional to Rock weakness
- **Power Items**: new held items that add +4 EVs to a specific stat per KO (speed up EV training)
- **New held items**: Choice Scarf (1.5× Speed, locked to one move), Choice Specs (1.5× Sp. Atk), Life Orb (1.3× damage, 10% recoil)

### New Game Systems
- **Global Trade Station (GTS)**: first online trading via Wi-Fi
- **Underground**: 2-player cooperative digging minigame for spheres and fossils
- **Super Contests**: enhanced contest format
- **Pokétch**: wrist device with utility apps
- **Amity Square**: walk with select Pokémon
- **Pal Park**: import Pokémon from Gen 3
- **Day/Night cycle fully restored** with proper time events

---

## Generation V — Black / White (2010)

**New Pokémon:** 156 (494–649) | **Platform:** Nintendo DS

### Battle System Changes

#### New Battle Formats
- **Triple Battles**: both sides send 3 Pokémon in a row; positional mechanics apply
  - Moves can only target adjacent positions; leftmost can't hit rightmost directly
  - Wide-range moves hit all adjacent Pokémon on both sides
- **Rotation Battles**: both sides send 3 Pokémon arranged in a ring
  - Only the front Pokémon attacks; trainer can rotate for free each turn
  - Strategy involves predicting opponent rotations

#### Hidden Abilities (New System)
- Every species gets a third ability option: the **Hidden Ability** (also called Dream World ability)
- Not normally obtainable through wild encounters; originally through the online Dream World service
- Opens entirely new ability-based strategies per species (e.g., Drizzle on Politoed, Drought on Ninetales — later restricted competitively)

#### Quality-of-Life Improvements
- **TMs now have infinite uses** (previously single-use in all prior generations)
- Poison damage **outside battle removed** — no more overworld HP drain
- EXP displayed per Pokémon after battle, even for switchers
- Battle animations: cleaner, faster

#### Seasons System
- 4 seasons cycle **monthly** in real time: Spring → Summer → Autumn → Winter
- Each season changes encounter rates, overworld appearance, and some area accessibility
- Exclusive to Gen 5 — never repeated

### Removed / Changed
- **Triple/Rotation Battles** introduced here but removed in Gen 7 Sun/Moon (partially restored in some titles)
- **Game Corner removed** (regulatory changes in Europe; never brought back)
- **Pokémon no longer follow the player** (was a feature in HG/SS; gone until later games)
- Permanent weather abilities (Drizzle, Drought, etc.) still grant indefinite weather — competitive scene bans them; formally changed in Gen 6

---

## Generation VI — X / Y (2013)

**New Pokémon:** 72 (650–721) | **New Type:** Fairy | **Platform:** Nintendo 3DS (first 3D main series)

### Battle System Changes

#### Fairy Type Added
- 18th and (as of Gen 9) final type
- **Immune** to Dragon
- **Resists** Fighting, Bug, Dark
- **Weak to** Steel, Poison
- **Super effective** against Dragon, Dark, Fighting
- Designed primarily to counter Dragon (which had very few weaknesses since Gen 1)

#### Steel Type Nerfed
- Loses resistances to **Ghost** and **Dark** (both now deal 1× to Steel)
- Steel goes from ~11 resistances/immunities to ~9

#### Mega Evolution (New Battle Gimmick)
- Once per battle, a Pokémon holding its species-specific **Mega Stone** can Mega Evolve when the trainer uses the **Key Stone**
- Increases base stats (total stat increase varies)
- Often changes **Ability** and sometimes changes **typing**
- Only one Mega Evolution per trainer per battle
- Available in X/Y, ORAS, and as a feature in Gen 7 (USUM)

#### EXP Share Reworked
- Changed from a held item (single Pokémon gets bonus EXP) to a **Key Item** toggled on/off
- When on: non-participants receive 50% of full EXP; participant still gets 100%
- Effectively doubles party EXP gain; controversial for making the game very easy

#### Pokémon-Amie (New System)
- Touch-screen minigame for petting, feeding, and playing with Pokémon
- Raises **Affection** stat (separate from Friendship)
- High affection grants in-battle bonuses: survive a KO hit at 1 HP (rare), avoid status, crit more, shake off paralysis early, etc.

#### Super Training (EV Training)
- Touch-screen minigame for directly raising EVs via stat bag items and bag training
- First time EVs are explicitly visible and directly manipulable by the player

#### Weather Abilities Nerfed
- **Drizzle, Drought, Sand Stream, Snow Warning**: in Gen 3–5 these caused permanent weather; in Gen 6 they now trigger **5-turn weather** (same duration as Rain Dance/Sunny Day)

#### Other Battle Changes
- **Sky Battles**: only Flying-type or Levitate Pokémon can participate; terrain-exclusive encounters
- **Inverse Battles**: type effectiveness completely inverted (super effective ↔ not very effective; immunities become super effective)
- **Horde Encounters**: 5 wild Pokémon simultaneously; useful for EV training

### Removed / Changed
- Double Battles, Triple Battles still available; Triple Battles de-emphasised

---

## Generation VII — Sun / Moon (2016)

**New Pokémon:** 88 (722–809) | **Platform:** Nintendo 3DS

### Battle System Changes

#### Z-Moves (New Battle Gimmick)
- Once per battle, a Pokémon holding a **Z-Crystal** (type-specific or species-specific) can convert a compatible move into a **Z-Move**
- **Offensive Z-Moves**: base power of 100–200+; bypass some immunities
- **Status Z-Moves**: unique secondary effects (heal HP, boost stats sharply, etc.)
- Species-specific Z-Moves exist for select Pokémon (Pikachu, Snorlax, etc.)
- Can be used alongside Mega Evolution in the same battle (one each)

#### Alolan Forms (New System)
- Existing Kanto Pokémon get **regional variant forms** with different types, stats, abilities, and appearances
- Examples: Alolan Ninetales (Ice/Fairy), Alolan Marowak (Fire/Ghost), Alolan Raichu (Electric/Psychic)
- Establish the template for all future regional forms

#### SOS Battles (New Wild Battle Type)
- Wild Pokémon can **call for help** mid-battle, adding an ally to the fight
- Allies can be species not normally encountered in the area
- Allies sometimes carry their **Hidden Ability**
- Chaining SOS calls was the primary method for finding rare Pokémon and IVs

#### Hyper Training (New System)
- Use **Bottle Caps** at a specific NPC to max out a Pokémon's **Battle Stats** at level 100
- Does not change underlying IV values — affects calculations only; Pokémon-HOME and breeding still see original IVs
- Allows non-perfect-IV Pokémon to reach competitive viability without breeding

#### Totem Pokémon (Boss Encounters)
- End-of-trial encounters with **buffed wild Pokémon** carrying a stat-boosting aura
- Can call in specific ally Pokémon each turn
- Replaces Gym Leader battles as the Island Trial format

#### Battle Royal (New Format)
- 4-player free-for-all; each trainer uses 1 Pokémon
- Game ends when any trainer's party is fully KO'd; winner is determined by points (KOs)

### Removed / Changed
- **HMs completely removed** — replaced by **Poké Ride** (call rideable Pokémon for field traversal: Tauros, Mudsdale, Lapras, Charizard, etc.)
- **Triple Battles and Rotation Battles removed** in Sun/Moon (were in Gen 5)
- **No traditional Gym Badges** — replaced by Island Trials and Grand Trial Kahuna battles
- **Festival Plaza** replaces PSS (Player Search System) for online features
- **Pokémon Refresh** replaces Pokémon-Amie (same Affection mechanics, new interface)

---

## Generation VIII — Sword / Shield (2019)

**New Pokémon:** 96 (810–905 base) | **Platform:** Nintendo Switch

### Battle System Changes

#### Dynamax / Gigantamax (New Battle Gimmick)
- Once per battle, a Pokémon in a **Power Spot** (any stadium or Wild Area raid den) can **Dynamax** for 3 turns
- Dynamaxed Pokémon grow to massive size; all moves convert to **Max Moves**
- **Max Moves** deal boosted damage and have secondary effects (Max Flare → sets Harsh Sunlight, Max Lightning → sets Electric Terrain, etc.)
- **Gigantamax**: special Dynamax for specific species/forms; changes appearance and grants a species-unique **G-Max Move** with a special effect
- Replaces Mega Evolution and Z-Moves entirely in the base SwSh games

#### Max Raid Battles (New Co-op Format)
- Up to 4 players cooperate to battle a single **Dynamax wild Pokémon**
- Wild Pokémon can act multiple times per round at higher difficulty tiers
- Only one player can Dynamax; wild Pokémon remains Dynamaxed the entire battle
- First console-era co-op battle format; precursor to Gen 9's Tera Raids

#### Galarian Forms
- Regional variants following Alolan Forms template (new types, stats, abilities)
- Examples: Galarian Ponyta (Psychic), Galarian Darumaka (Ice), Galarian Zigzagoon (Dark/Normal)

### New Game Systems
- **Wild Area**: large open-zone area with visible overworld Pokémon spawns influenced by weather
- **Camping**: set up camp; cook curry; raises friendship
- **Curry Cooking**: 150+ curry recipes; affect friendship and in-battle bonuses during camp
- **EXP Candy**: direct EXP items dropped from Max Raids; replaces grinding
- **Ranked Battles**: official online competitive ladder with seasonal rankings
- **Pokémon HOME**: cross-generation cloud storage (replaces Bank); routes Pokémon between games
- **Surprise Trade**: mass Wonder Trade equivalent

### Removed / Changed
- **Mega Evolution**: not available in SwSh or BDSP (available in Legends: Arceus via Mega Bracelet, sort of, and brought back in Gen 9 DLC)
- **Z-Moves**: not available in SwSh
- **Random grass encounters**: mostly replaced by visible overworld Pokémon (Wild Area) or ambush encounters
- **National Dex controversy**: SwSh do not include all 890 Pokémon; only those in the Galar Dex are available

---

## Legends: Arceus (2022) — Spin-off / Gen 8 era

**New Pokémon:** 17 new + many Hisuian forms | **Setting:** Ancient Hisui (historical Sinnoh)

This title departs from the traditional structure significantly enough to warrant its own section.

### Action-RPG Catching
- Throw Poké Balls **directly at wild Pokémon** in the overworld — no mandatory battle required
- Pokémon can be caught by sneaking up on them; battle is optional for tougher Pokémon
- Pokémon can attack the player character in the overworld

### Battle System Overhaul: Agile and Strong Styles

Every mastered move can be used in one of two styles:

| Style | Power | Speed (next action) |
|:------|:-----:|:-------------------:|
| **Agile Style** | Lower | Acts sooner |
| **Strong Style** | Higher | Acts later |

- Turn order is determined by an **Action Point system** — multiple actions can happen before one side acts
- Strategic depth: using Agile Style might let your Pokémon act twice before the opponent
- Moves must be used repeatedly to **master** them; mastered moves unlock both styles

### Hisuian Forms
- New regional variants unique to the ancient Hisui region
- Examples: Hisuian Zorua/Zoroark (Normal/Ghost), Hisuian Decidueye (Grass/Fighting), Hisuian Typhlosion (Fire/Ghost)

### New Evolution Methods
- Task-based evolutions tied to in-game challenges
  - Use a specific move in Strong Style a set number of times
  - Accumulate a certain amount of recoil damage
  - Level up during a full moon
  - Use new items (Black Augurite, Peat Block)

### Removed / Changed (relative to main series)
- No traditional trainer-vs-trainer battle format for most of the game
- No held items equipped during battle (Poké Balls used from bag)
- No Dynamax, Z-Moves, or Mega Evolution

---

## Generation IX — Scarlet / Violet (2022)

**New Pokémon:** 120 (906–1025) | **Platform:** Nintendo Switch | **First true open world**

### Battle System Changes

#### Terastallization (New Battle Gimmick)
- Once per battle (recharged at any Pokémon Center or Tera Orb spot), a Pokémon can **Terastallize**
- Pokémon takes on a crystalline appearance and its **type changes to its Tera Type**
- STAB rules:
  - If Tera Type matches one of the Pokémon's base types → STAB still applies to both; Tera Type gets 2× instead of 1.5×
  - If Tera Type is different from base types → only Tera Type attacks get STAB; base-type moves lose STAB
- Every Pokémon has a fixed Tera Type (often matches a base type); can be changed by clearing Tera Raids

#### Hail → Snow (Weather Change)
- **Hail** is replaced by **Snow** (same visual but different mechanics)
  - Hail: dealt 1/16 chip damage to non-Ice-types per turn
  - Snow: **no chip damage**; instead raises **Ice-type Defense by 50%** (and Sp. Def via Aurora Veil compatibility)

#### Tera Raid Battles
- 4-player co-op vs. a Terastallized wild Pokémon with a **countdown timer**
- At 6-star difficulty, the wild Pokémon can act **multiple times per player turn**
- Evolutionary chain of Max Raids (Gen 8) — mechanically similar but with Terastallization

#### Auto Battle (Let's Go! Mode)
- Send your lead Pokémon out to **automatically fight weak wild Pokémon** while walking
- Gains EXP and items; skips battle UI entirely for grinding
- Inspired by Let's Go Pikachu/Eevee (2018 spin-off)

### New Game Systems
- **Fully open world**: no routes, no loading zones between areas; three independent story paths
- **Picnics**: replace Camping; sandwich-making grants temporary buffs (encounter rate boosts, egg probability, etc.)
- **Paldean Forms**: regional variants for Paldea
- **Paradox Pokémon**: ancient (Scarlet) or futuristic (Violet) counterparts to existing species; no evolutions, just alternate forms tied to the lore
- **Union Circle**: 4-player co-op open-world exploration in the same game session

### Removed / Changed
- **Random encounters completely removed** — all wild Pokémon visible in the overworld
- **Mega Evolution**: not in base game (available in Indigo Disk DLC via exception cases)
- **Z-Moves**: not available
- **Dynamax / Gigantamax**: not available

---

## Cross-Generation System Timelines

### Weather Conditions

| Condition | Introduced | Notes |
|:----------|:----------:|:------|
| Rain | Gen 2 | Boosts Water ×1.5; weakens Fire ×0.5 |
| Sun | Gen 2 | Boosts Fire ×1.5; weakens Water ×0.5; enables Solar Beam 1-turn |
| Sandstorm | Gen 2 | End-of-turn damage to non-Rock/Ground/Steel; boosts Rock Sp. Def ×1.5 (Gen 4+) |
| Hail | Gen 3 | End-of-turn damage to non-Ice-types |
| Snow | Gen 9 | **Replaces Hail**; no chip damage; boosts Ice Defense ×1.5 |
| Permanent weather | Gen 3 | Via ability (Drizzle/Drought/Sand Stream/Snow Warning); made 5-turn in Gen 6 |
| Primordial weather | Gen 6 (ORAS) | Kyogre/Groudon abilities override all weather; cannot be overridden |

### HMs and Field Moves

| Generation | System |
|:-----------|:-------|
| I–IV | HMs (Surf, Fly, Cut, etc.): permanent moves, cannot be deleted without HM Deleter NPC |
| V–VI | HMs still exist; some flexibility; fewer mandatory HMs |
| VII | **HMs removed**; replaced by Poké Ride (call wild Pokémon for field traversal) |
| VIII (SwSh) | No HMs; Rotom Bike, map-based fast travel |
| VIII (LA) | No HMs; rideable alpha Pokémon in Hisui |
| IX | No HMs; Koraidon/Miraidon (Legendary mount) handles all traversal |

### Breeding

| Generation | System |
|:-----------|:-------|
| I | No breeding |
| II+ | Day Care + compatible Pokémon produce Eggs |
| III | EV/IV system makes breeding competitively critical |
| VI | Destiny Knot: passes 5 of 6 IVs from parents; Everstone: 100% nature pass |
| VIII+ | Ability Patch introduced (can change Hidden Ability); no new breeding mechanics |

### Shinies

| Generation | Rate | Notes |
|:-----------|:----:|:------|
| I | None | No shiny Pokémon |
| II | 1/8192 | Determined by specific DV combination |
| III+ | 1/8192 | Fully random; decoupled from IVs |
| VI+ | 1/4096 | Halved base rate; Shiny Charm item for further reduction; Masuda Method (1/683 approx with Charm) |

### Battle Formats

| Format | Introduced | Status |
|:-------|:----------:|:------:|
| Single | Gen I | All generations |
| Double | Gen III | All generations |
| Multi | Gen III | Sporadic |
| Triple | Gen V | Removed in Gen VII |
| Rotation | Gen V | Removed in Gen VII |
| Battle Royal | Gen VII | Gen VII only (main series) |
| Max Raid | Gen VIII | Gen VIII (SwSh) |
| Tera Raid | Gen IX | Gen IX |

### Random Encounters

| Generation | Method |
|:-----------|:-------|
| I–VII | Random encounters in tall grass, caves, and water |
| VIII (SwSh) | Mostly overworld encounters in Wild Area; some random in routes |
| VIII (BDSP) | Both random (grass) and overworld |
| IX | **Random encounters fully removed**; all wild Pokémon visible in overworld |

### Regional Forms

| Name | Generation | Source Region | Note |
|:-----|:----------:|:-------------:|:-----|
| Alolan Forms | Gen VII | Alola | Kanto Pokémon only |
| Galarian Forms | Gen VIII | Galar | Mix of older Pokémon |
| Hisuian Forms | Gen VIII (LA) | Ancient Hisui | Some new evolutions exclusive to LA |
| Paldean Forms | Gen IX | Paldea | Limited set |
