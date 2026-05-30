# Battle Sim – TODO List

> **See also:** `CLAUDE.md` (session setup, architecture, commands) · `AI_CONTEXT.md` (agent profiles) · `DESIGN_GUIDES.md` (mechanics rules) · `DEV_STANDARDS.md` (coding conventions)

---

## Completed ✅

<details>
<summary>Type Chart, PP, Status, Crits, Move Effects, Damage Categories, Bad Poison, XP/Levelling, Enemy Encounters</summary>

**Type Chart** — `ITypeChart` + `Gen1TypeChart` (17-type Gen 1 matrix, Ghost/Psychic bug, Poison→Bug quirk). Wired into `DamageCalculator` and `AttackAction`.

**PP Tracking** — `PokemonAttack` wrapper; decrements on use; Struggle when all PP = 0.

**Move Priority** — `AttackAction` reads `move.Priority` (was hardcoded 0).

**Status Conditions** — Applied after damage; `EffectChance` roll; sleep turn counter; status blocked if target already statused.

**Status Effects in Battle Loop** — Sleep/Freeze/Paralysis pre-turn; Burn/Poison end-of-turn 1/16; Confusion; Paralysis quarters Speed in sort order.

**Critical Hits & Stat Stages** — Gen 1 Speed-based crit formula; high-crit moves; stat stage multipliers on `IBattleRules`; crits ignore stages and Burn.

**Move Effects** — `MoveEffect` enum; stat-stage moves (Swords Dance, Growl); Haze; Flinch; Recharge; LeechSeed; Binding; TwoTurn.

**Damage Categories** — Fixed (Dragon Rage), LevelBased (Seismic Toss), OHKO, SelfDestruct (halves target Defense), SuperFang, Drain.

**Bad Poison (Toxic)** — `StatusCondition.BadPoison`; `ToxicCounter` escalates damage each turn; `IBattleRules.BadPoisonDamageFraction`.

**Experience, Levelling & Level Picker** — Gen 1 wild XP formula; `LeveledUp` event; level slider in UI (5–100); `GainExperience → LevelUp` path.

**Enemy Encounter System** — BST-matched random selection (±15%, widens to ±50%/all); enemy level = player level ±3; player's own species excluded. `EncounterSelector` in core library.

</details>

---

## ← NEXT: Generation Abstraction — Stat Selection

The last hardcoded Gen 1 assumption in the combat core. `DamageCalculator` currently picks `Attributes.Special` symmetrically for both offence and defence on special moves — correct for Gen 1, wrong from Gen 2 onwards (SpAtk ≠ SpDef). Closing this now keeps the engine clean before Learnset and AI work begins.

- [ ] Add two methods to `IBattleRules`:
  ```csharp
  int GetOffensiveStat(Creature attacker, AttackType moveType);
  int GetDefensiveStat(Creature defender, AttackType moveType);
  ```
- [ ] `Gen1BattleRules`: both return `Attributes.Special` for `Special` moves; `Attributes.Attack` / `Attributes.Defense` for `Physical` (same as today, just explicit)
- [ ] `DamageCalculator`: replace the four inline `Attributes.Special` references with calls to the new methods; remove the duplicated crit / non-crit stat selection block
- [ ] Tests: `DamageCalculator_UsesOffensiveStatFromRules` and `DamageCalculator_UsesDefensiveStatFromRules` — verify injected rules control which stat is read

---

## Learnset System

Currently creatures receive 4 random moves from the full move pool. Learnsets ensure Pokémon only know moves they can actually learn.

**Prerequisite:** Experience, Levelling & Level Picker ✅

**Data:**
- [ ] `PokemonLearnset` model: `SpeciesId` (FK), `MoveId` (FK), `LearnLevel` (int); EF migration on `PokemonDbContext`
- [ ] Import from PokeAPI: filter `/pokemon/{id}` moves array to `version_group.name == "red-blue"` and `move_learn_method.name == "level-up"`; fold into `PokemonImport` (no extra API calls)
- [ ] `Creature.InitializeFromSpecies(species, learnset, allMoves, atLevel)` — up to 4 moves at or below `atLevel` (highest-level ones); replaces random assignment in `GameController.BuildCreature`

**Level-up move learning:**
- [ ] `Creature.LevelUp()` checks learnset for moves at the new level
- [ ] Slot free → add automatically; emit `MoveLearned(string CreatureName, string MoveName)`
- [ ] Slots full → emit `MoveReplacementRequired(string CreatureName, string NewMoveName, IReadOnlyList<MoveInfo> CurrentMoves)` — blocking event; backend waits on `IBattleInput`-style TCS
- [ ] `BattleHub` + `SignalRInput` extended with `ForgetMove(int slotIndex)` / `SkipNewMove()` path
- [ ] `MoveLearned` and `MoveReplacementRequired` handled by all emitters and `useBattleHub.ts`

**XP bar:**
- [ ] `TurnStarted` carries `PlayerExperience` and `XpToNextLevel`; `useBattleHub.ts` dispatches into state so the XP bar fills live

**Tests:**
- [ ] `Learnset_InitializeFromSpecies_GivesCorrectMovesAtLevel`
- [ ] `Learnset_LevelUp_AddsNewMoveWhenSlotAvailable`
- [ ] `Learnset_LevelUp_EmitsMoveReplacementRequired_WhenFull`

---

## AI Move Selection

**Prerequisite:** Learnset System (so AI evaluates moves the Pokémon can actually learn)

`IBattleInput` is the seam. AI scores available moves via `IMoveEvaluator` and picks using a selection strategy.

**Evaluator dimensions:**
- Expected damage — base power × type effectiveness × STAB × stat ratio
- Type effectiveness bonus — super-effective moves strongly preferred
- Stat-stage move value — Swords Dance high-value at full HP; Growl low-value when outmatched
- Priority move value — prefer Quick Attack when own HP low or opponent near KO
- Status move value — Thunder Wave high-value early; worthless if target already statused
- PP conservation — small penalty for moves with ≤ 5 PP remaining

**Selection strategies:**
- `RandomMoveInput` — ignores evaluators; pure random (wild Pokémon / lowest AI tier)
- `WeightedAIInput(IMoveEvaluator)` — probabilistic, weighted by score (average trainer)
- `GreedyAIInput(IMoveEvaluator)` — always picks highest score (Elite Four / boss tier)
- `CompositeEvaluator` — weighted sum of multiple evaluators; trainer "personality" via different weights

**Tasks:**
- [ ] `DamageEvaluator : IMoveEvaluator`
- [ ] `TypeEffectivenessEvaluator : IMoveEvaluator`
- [ ] `StatStageMoveEvaluator : IMoveEvaluator`
- [ ] `StatusMoveEvaluator : IMoveEvaluator`
- [ ] `CompositeEvaluator : IMoveEvaluator`
- [ ] `RandomMoveInput : IBattleInput`
- [ ] `GreedyAIInput : IBattleInput`
- [ ] `WeightedAIInput : IBattleInput`
- [ ] Wire `RandomMoveInput` as default enemy input in `GameSessionManager` (replaces `AutoSelectInput`)

---

## EV Gain (Effort Values)

No prerequisites. All `ExpHP/Attack/Defense/Special/Speed` fields exist on `Creature` but are never written.

- [ ] After awarding XP in `Battle.StartFightAsync`, add fainted enemy's base stats to player's corresponding `Exp*` fields; cap each at 65535 (Gen 1 has no per-stat cap); call `CalculateStats()` immediately
- [ ] No new battle event required (Gen 1 is silent about EVs)

---

## Web UI

Stack: React 18 + TypeScript + SignalR. Phaser 3 for sprite/animation canvas.

### Phaser Canvas
- [ ] Add `phaser` npm dependency to `ClientApp`
- [ ] `BattleCanvas.tsx` — mounts a Phaser `Game` instance in a `useEffect`; destroys on unmount
- [ ] `BattleScene.ts` — loads front sprite (`/sprites/front/{id}.png`) for enemy, back sprite for player; diagonal layout (enemy top-right, player bottom-left)
- [ ] React↔Phaser bridge — shared `PhaserBridge` event emitter (`mitt`); React dispatches `playMoveAnimation` / `playFaintAnimation`; Phaser emits `animationComplete` back
- [ ] Current CSS sprite placeholders replaced by the Phaser canvas; React retains HP/status overlay layer

### Animations
- [ ] `MoveUsed` → attacker sprite bobs toward opponent (lunge + return, ~300ms)
- [ ] `DamageDealt` → target white-flash (2 frames); HP bar drains animated over ~600ms
- [ ] `CreatureFainted` → target slides down and fades (~500ms)
- [ ] Move menu re-enabled only after `animationComplete` for the full turn sequence
- [ ] `useBattleHub` state gains `animating: boolean`; move buttons check `phase === 'choosing' && !animating`

### Polish
- [ ] `BattleEndedOverlay` — covers battle screen on `BattleEnded`; shows winner, "Play Again" → `/select`, "Main Menu" → `/`
- [ ] Level-up notification toast on `LeveledUp` event
- [ ] Move menu STAB indicator — subtle highlight on moves matching player's type
- [ ] Color-coded effectiveness in battle log (super-effective green, not very effective grey, no effect red)
- [ ] Sprite shake tween on damage received
- [ ] `ConsoleInput : IBattleInput` — numbered move menu for terminal play (low priority)

---

## Catch Mechanic

Deferred until Phaser animations exist — the mechanic needs a throw/shake/catch animation sequence to be meaningful.

**When ready:**
- [ ] Bag action in move menu; `Battle` extended with a "catching" state
- [ ] Gen 1 capture formula: `floor((MaxHP × 3 − HP × 2) × CatchRate / (MaxHP × 3))` vs. 0–255 roll
- [ ] `PokemonSpecies.CatchRate` already imported ✓
- [ ] `CaptureAttempted(string TargetName, bool Caught)` battle event
- [ ] `BattleEnded` variant: `reason: "Caught"`

---

## Game Loop & Progression

**Prerequisites:** Catch Mechanic, BattleState extraction (Tech Debt), `PlayerDbContext` / `save.db`

- Player starts with one Pokémon; win → new BST-scaled encounter; lose → game over with run summary
- Catch → Pokémon added to party (up to 6); choose lead between battles
- Progressive difficulty: `targetBst = party lead BST + (depth × 10)`; trainer encounters at milestones
- Evolution: player Pokémon evolve at level threshold (requires `PokemonEvolution` table in `pokemon.db`); enemy evolves to correct form for their level before battle
- `PlayerSave` / `SavedCreature` models in `save.db`; auto-save after each battle
- Party management UI between battles

---

## Multi-Generation: Data Model & Schema

The stat-selection abstraction (← NEXT section) is the only change to do now. Everything below is deferred to the Gen 2 sprint.

**`Attributes` stat split:**
- [ ] `Attributes.Special` → `Attributes.SpAtk` + `Attributes.SpDef`; keep `Special` as a computed alias for Gen 1 (`SpAtk`, since they're equal) so existing tests migrate cleanly
- [ ] `Creature.BaseSpecial`, `DvSpecial`, `ExpSpecial` split in parallel

**`PokemonSpecies` per-generation schema:**
- [ ] Separate timeless identity (`Id`, `Name`, `CatchRate`, `BaseExperience`, `PokedexEntry`, `GrowthRate`) from generation-specific data
- [ ] New `PokemonSpeciesGenData` table: `SpeciesId`, `Generation` (int), `Type1`, `Type2`, `BaseHP`, `BaseAttack`, `BaseDefense`, `BaseSpAtk`, `BaseSpDef`, `BaseSpeed`; Gen 3+ adds `Ability1/2/Hidden`
- [ ] Importer stores one row per species per generation; engine queries by active generation
- [ ] **Note:** PokeAPI has no `past_stats` equivalent — Gen 1 stat corrections (e.g. Clefable, Beedrill, Pikachu line were buffed in Gen 6) will need a corrections table or separate data source

**Generation filtering:**
- [ ] `Attack.GenerationIntroduced` (int) + `PokemonSpecies.GenerationIntroduced` (int) — set on import
- [ ] `EncounterSelector.PickByBst` and `GameController.BuildCreature` filter by `GenerationIntroduced <= activeGeneration`
- [ ] `PokemonService.GetSpeciesForGenerationAsync(int)` + `AttackService.GetMovesForGenerationAsync(int)` replace unfiltered `ToListAsync()` calls

---

## User Documentation

Target: after AI Move Selection lands — at that point battles are fully playable and docs won't describe a moving target.

- [ ] `/help` route or modal — starter selection, battle controls, status icons, level picker
- [ ] Expand `README.md` — architecture decisions (two-DB model, `IBattleRules` pattern, how to add a move effect, how to add a generation)
- [ ] `GEN_DIFFERENCES.md` (already written) — adapt for player-facing "what makes Gen 1 different" explainer

---

## Database Architecture (reference)

**Current two-database model:**
- `pokemon.db` / `PokemonDbContext` — species, base stats, types, growth rates, catch rates, learnsets, game availability
- `moves.db` / `MovesDbContext` — moves, damage type, accuracy, PP, stat effects, status effects

**Where new tables go:**
- Pokémon-world data (learnsets, evolution chains, egg groups) → `pokemon.db`
- Move-world data (Z-move mappings, move combos) → `moves.db`
- Player save state (party, caught Pokémon, items) → `save.db` / `PlayerDbContext` (defer until Catch Mechanic)

**Learnset import (part of Learnset System section above):**
- [ ] Extend `PokeApiPokemon` DTO with `Moves` array
- [ ] In `PokemonImport`, parse `version_group_details`, filter to `"red-blue"` + `"level-up"`, persist `PokemonLearnset` rows idempotently

---

## Tech Debt / Cleanup

### Done ✅
- Remove dead scaffolding (`Body`, `Brain`, `BodyPart`, `CreatureType`, etc.)
- `.gitignore`, `.gitattributes`, `.editorconfig`, `global.json` (SDK pin)
- EF Core migrations; `EnsureDatabaseCreated()` calls `Database.Migrate()`
- `StatStages` struct→class (silent mutation fix)
- `AsNoTracking()` on all read-only DB service methods
- Pending-session TTL in `GameSessionManager` (2-min eviction)
- `AlwaysHitRules` test helper (eliminates 1/256-miss flakiness)

### Future: BattleState extraction
**Trigger:** when save system is built. Extract transient battle fields (`Status`, `ToxicCounter`, `IsRecharging`, `IsFlinched`, `HasLeechSeed`, `BindingTurnsRemaining`, `IsTwoTurnCharging`, `ChargingMove`, `StatStages`) from `Creature` into a `BattleState` class held as `Creature.Battle`. Serialize only the permanent half for `save.db`.

### Known Gaps
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector exists in the UI
- Enemy Pokémon do not evolve — wire into level-up system when Game Loop is built
- `GameController.BuildCreature` uses random moves — fixed by Learnset System
- `PokemonService` / `AttackService` not registered in DI — using direct `new()`; revisit when scoped lifetime or multi-context scenarios arise
