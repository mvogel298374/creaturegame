# Battle Sim – TODO List

> **See also:** `CLAUDE.md` (session setup, architecture, commands) · `AI_CONTEXT.md` (agent profiles) · `DESIGN_GUIDES.md` (mechanics rules) · `DEV_STANDARDS.md` (coding conventions)

---

## Type Chart ✅ DONE
- [x] Create `ITypeChart` interface
- [x] Implement `Gen1TypeChart` with full 17-type Gen 1 matrix (Ghost/Psychic bug, Poison super vs Bug, no Steel/Dark/Fairy)
- [x] Wire into `DamageCalculator` via interface (swappable per-generation)
- [x] `AttackAction` and `Battle` accept `ITypeChart` — one injection point to swap generation
- [x] 7 type chart tests added and passing

## PP Tracking ✅ DONE
- [x] Switch `Creature.MoveSet` from `List<Attack>` to `List<PokemonAttack>`
- [x] `AttackAction` decrements `PowerPointsCurrent`, checks > 0 before executing
- [x] Handle Struggle when all PP = 0

## Move Priority Fix ✅ DONE
- [x] `AttackAction` constructor: read `move.Priority` instead of hardcoding 0

## Status Condition Application ✅ DONE
- [x] Add `StatusEffect` (`StatusCondition`) property to `Attack`; add EF migration
- [x] Add `meta.ailment` mapping to `PokeApiMove`; import `ailment.name` → `StatusEffect`, `ailment_chance` → `EffectChance` in `MoveImport`
- [x] `AttackAction.ExecuteAsync()`: after damage, roll `EffectChance` and set `Target.Status` if target has no status and move has a `StatusEffect`
- [x] Set `SleepTurns` (1–7, random) when applying Sleep
- [x] Tests: status applied on hit; not applied when target already statused; secondary effect chance respected

## Status Effects in Battle Loop ✅ DONE
- [x] Pre-turn: Sleep skips action and decrements `SleepTurns`; wakes when counter hits 0
- [x] Pre-turn: Freeze skips action; thaws on any Fire-type move that can burn hitting the frozen target
- [x] Pre-turn: Paralysis — 25% chance to skip action
- [x] Stat modifiers: Burn halves physical Attack in `DamageCalculator`; Paralysis quarters Speed in turn ordering
- [x] End-of-turn: Burn deals 1/16 max HP; Poison deals 1/16 max HP
- [x] Pseudo-status — Confusion: `Creature.ConfusedTurns` counter; 50% chance to hurt itself each turn (40 base power, typeless); clears when counter expires (2–5 turns, Gen 1)

## Critical Hits & Stat Stages ✅ DONE

**Stat stages:**
- [x] `StatStages` class on `Creature` — Attack, Defense, Special, Speed, Accuracy, Evasion each clamped to [-6, +6]; `Clear()` + `Raise*()` helpers; class (not struct) so mutations are visible through property
- [x] `IBattleRules.GetStatMultiplier(int stage)` — Gen 1: 2/(2+|n|) for n≤0, (2+n)/2 for n>0 (0.25× at -6, 4× at +6)
- [x] `IBattleRules.GetAccuracyStageMultiplier(int stage)` — Gen 1: 3/(3+|n|) for n≤0, (3+n)/3 for n>0
- [x] `IBattleRules.GetHitThreshold` + `AccuracyRollBound` — Gen 1 0–255 scale; roll 255 always misses (1/256 bug)
- [x] `DamageCalculator` applies Attack/Defense/Special stage multipliers via `IBattleRules`
- [x] `StatusResolver.EffectiveSpeed` folds in Speed stage multiplier (stacks with Paralysis quartering)
- [x] `Creature.ResetBattleState()` — clears Status, SleepTurns, ConfusedTurns, Stages; called at start of each battle

**Critical hits:**
- [x] `Attack.IsHighCrit` bool — EF migration added; PokeApiConnector imports `meta.crit_rate > 0`
- [x] `IBattleRules.GetCritChance` — Gen 1 normal: floor(BaseSpeed/2)/256; high-crit: min(floor(BaseSpeed/2)×8, 255)/256
- [x] `IBattleRules.CritMultiplier` → 2.0; `CritIgnoresStatStages` → true in Gen 1
- [x] `DamageCalculator` rolls crit; Gen 1 crit path uses raw Attributes stats — no stages, no Burn penalty
- [x] `DamageDealt` event carries `IsCrit` flag

---

## Move Effects (Stat-Stage Moves) ✅ DONE
- [x] `StageStat` / `StageTarget` / `MoveEffect` enums + `StatEffect` record (`creaturegame/Attacks/MoveEffect.cs`)
- [x] `Attack`: four nullable stat-effect columns (`StatEffectStat`, `StatEffectDelta`, `StatEffectTarget`, `StatEffectChance`) + computed `[NotMapped] StatEffect?` property + `MoveEffect Effect` column; EF migration `AddStatEffectAndMoveEffect`
- [x] `AttackAction`: `TryApplyStatEffect` rolls chance, calls `Stages.Raise*(delta)` on correct target, emits `StatStageChanged`; `TryApplyHaze` calls `ResetBattleState()` on both creatures, emits `HazeClearedStages`
- [x] `StatStageChanged` + `HazeClearedStages` events handled in `ConsoleBattleEventEmitter`, `SignalRBattleEventEmitter`, and `useBattleHub.ts`
- [x] `MoveImport`: maps `stat_changes[]` → stat-effect columns; `flinch_chance > 0` → `MoveEffect.Flinch`; name `"haze"` → `MoveEffect.Haze`; `PokeApiMove` extended with `Target`, `StatChanges`, `MoveMeta.FlinchChance`
- [x] 7 new tests — `SwordsDance_RaisesAttackStageByTwo`, `Growl_LowersEnemyAttackStage`, `StatStage_ClampedAtPlusSix/MinusSix`, `Haze_ClearsAllStagesOnBothCreatures`, `StatEffect_ZeroChance_NeverApplies`, `StatEffect_HundredChance_AlwaysApplies`; migration round-trip test extended

---

## Move Execution Completeness ✅ DONE

**DamageCategory on Attack (new enum + columns):**
- [x] `DamageCategory` enum: `Standard`, `Fixed`, `LevelBased`, `Drain`, `OHKO`, `SelfDestruct`, `SuperFang`
- [x] `Attack.DamageCategory` (default `Standard`); `Attack.FixedDamageValue` (nullable int); `Attack.DrainPercent` (int, default 50); `Attack.NeverMisses` (bool); EF migration `AddDamageCategoryAndMoveFlags`

**MoveEffect additions:**
- [x] `MoveEffect.Recharge` — sets `Source.IsRecharging` after damage; `CanAct` blocks and clears next turn
- [x] `MoveEffect.LeechSeed` — sets `Target.HasLeechSeed`; `Battle` drains 1/16 max HP end-of-turn and heals opponent
- [x] `MoveEffect.Binding` — sets `Target.BindingTurnsRemaining` (2–5 turns); `CanAct` blocks while > 0; end-of-turn drains 1/16 and decrements
- [x] `MoveEffect.TwoTurn` — charge turn: emit `ChargingUp`, set state; `Battle` loop auto-fires `ChargingMove` on release turn; PP only on charge turn
- [x] `MoveEffect.Flinch` — sets `Target.IsFlinched`; `CanAct` blocks and self-clears

**Persistent creature state (all cleared by ResetBattleState):**
- [x] `IsRecharging`, `IsFlinched`, `HasLeechSeed`, `BindingTurnsRemaining`, `IsTwoTurnCharging`, `ChargingMove`

**Engine — AttackAction:**
- [x] `NeverMisses`: skip accuracy check entirely (Swift)
- [x] `OHKO`: fail before accuracy roll if `Source.Level < Target.Level`; on hit, set target HP → 0
- [x] `Fixed`: deal exactly `FixedDamageValue` HP; bypass formula (Dragon Rage, Sonic Boom)
- [x] `LevelBased`: deal exactly `Source.Level` HP (Seismic Toss, Night Shade)
- [x] `SuperFang`: deal `floor(Target.HP / 2)`, min 1
- [x] `Drain`: standard damage then heal Source by `floor(damage × DrainPercent / 100)`; emit `DrainHealed`
- [x] `SelfDestruct`: standard formula with target Defense/Special halved (Gen 1 quirk); Source HP → 0 after damage; user faints even on miss

**New battle events (all wired to SignalR emitter + useBattleHub.ts):**
- [x] `DrainHealed`, `LeechSeedApplied`, `LeechSeedDamage`, `LeechSeedHealed`, `Recharging`, `BindingStarted`, `BindingBlocked`, `BindingDamage`, `FlinchBlocked`, `ChargingUp`

**Import (PokeApiConnector):**
- [x] `meta.category` and `meta.drain` added to `MoveMeta` DTO
- [x] `meta.category.name` → `DamageCategory` mapping; ID-based overrides for Fixed/LevelBased/SuperFang/SelfDestruct/NeverMisses
- [x] MoveEffect mapping: Hyper Beam → Recharge; Leech Seed → LeechSeed; Wrap/Bind/Clamp/Fire Spin → Binding; Fly/Dig/Solar Beam/Razor Wind/Sky Attack → TwoTurn

**Tests (16 new, 106 total passing):**
- [x] `DrainMove_HealsSourceByHalfDamageDealt`
- [x] `FixedDamage_DealsDamageIgnoringStats`
- [x] `LevelBasedDamage_DealsAttackerLevelDamage`
- [x] `OHKOMove_FailsIfSourceLevelLowerThanTarget`
- [x] `OHKOMove_FaintsTargetIfLevelSufficient`
- [x] `SelfDestruct_FaintsUser`
- [x] `SelfDestruct_FaintsUserEvenOnMiss`
- [x] `SuperFang_HalvesTargetCurrentHp`
- [x] `Recharge_SourceCannotActNextTurn`
- [x] `LeechSeed_SetsHasLeechSeedOnTarget`
- [x] `LeechSeedDrain_DrainsTargetAndHealsSource`
- [x] `Binding_SetsBindingTurnsOnTarget`
- [x] `Binding_BlocksTargetViaCanAct`
- [x] `TwoTurnMove_ChargesFirstThenDeliversDamage`
- [x] `NeverMisses_AlwaysHitsRegardlessOfAccuracy`
- [x] `Flinch_BlocksTargetViaCanAct_AndSelfClears`

---

## Bad Poison (Toxic)

Regular Poison does flat 1/16 max HP damage per turn. Toxic inflicts Bad Poison which does escalating damage: 1/16, 2/16, 3/16... per turn. Without this, the most iconic Gen 1 stall move maps to `None` on import and is silently ignored.

**Model:**
- [ ] Add `StatusCondition.BadPoison` to the enum
- [ ] `Creature.ToxicCounter` (int) — starts at 1 when Bad Poison is applied, incremented each end-of-turn before damage; reset in `ResetBattleState()`
- [ ] PokeAPI importer: map `"bad-poison"` → `StatusCondition.BadPoison`

**Engine:**
- [ ] `IBattleRules.BadPoisonDamageFraction(int toxicCounter)` — Gen 1: returns `toxicCounter / 16.0`; counter does not cap in Gen 1 (theoretically escalates forever, but ~15 turns ends any battle)
- [ ] `StatusResolver.ApplyEndOfTurnDamage`: if `Status == BadPoison`, deal `Max(1, floor(MaxHP × rules.BadPoisonDamageFraction(ToxicCounter)))`, then `ToxicCounter++`
- [ ] `StatusDamage` event already exists and covers this — use `Source = StatusCondition.BadPoison`
- [ ] `StatusResolver.CanAct`: no pre-turn effect (Bad Poison only affects end-of-turn, unlike Sleep/Freeze/Paralysis)
- [ ] `useBattleHub.ts`: add `'BADPSN'` status badge alongside existing PSN/BRN/PAR/SLP/FRZ badges

**Tests:**
- [ ] `BadPoison_FirstTurn_Deals1_16MaxHP`
- [ ] `BadPoison_SecondTurn_Deals2_16MaxHP` — counter increments correctly
- [ ] `BadPoison_DoesNotBlockAction` — creature can still act each turn
- [ ] `BadPoison_ResetOnNewBattle` — ToxicCounter cleared by ResetBattleState

---

## Experience, Levelling & Level Picker

XP is awarded when a creature faints. The `Creature.GainExperience()` method and level-up formula exist but nothing calls them yet. This section also adds the level picker so the player can start a battle with any level (5–100) instead of a hardcoded 50. The chosen level feeds directly into the Learnset section, where it becomes the `atLevel` argument for move initialisation.

**Level picker (backend):**
- [ ] `StartGameRequest` gains an optional `Level` property (`int`, default 50, clamped server-side to [5, 100])
- [ ] `GameController.BuildCreature(species, level)` sets `Creature.Level = level`, sets `Creature.Experience` to the threshold for that level (`CalculateExperienceForLevel(level)` already exists), then calls `CalculateStats()`
- [ ] Moveset at the chosen level is still random until the Learnset section replaces random assignment with level-filtered moves — noted in Known Gaps

**Level picker (UI):**
- [ ] `StarterSelection` screen gains a level slider (range 5–100, default 50, step 1) below the species grid
- [ ] `POST /api/game/start` payload includes `level`; `useBattleHub.ts` stores chosen level in local state for the HUD

**Engine:**
- [ ] `Battle.StartFightAsync`: on `CreatureFainted`, award XP to the winning side's creature — Gen 1 wild formula: `floor(EnemySpecies.BaseExperience × EnemyLevel / 7)`. Trainer modifier (×1.5) added once the Enemy Encounter System exists
- [ ] `PokemonSpecies.BaseExperience` is already imported and stored in DB ✓
- [ ] `Creature` needs a `SpeciesBaseExperience` property (set by `InitializeFromSpecies`) so `Battle` can compute the award without a DB call
- [ ] `LeveledUp(string CreatureName, int NewLevel)` battle event — replaces `Console.WriteLine` in `Creature.LevelUp()`
- [ ] `ConsoleBattleEventEmitter`, `SignalRBattleEventEmitter`, and `useBattleHub.ts` handle `LeveledUp`
- [ ] Moves gained on level-up are handled by the Learnset section — not in scope here

**Tests:**
- [ ] `XP_AwardedToWinnerOnEnemyFaint` — correct Gen 1 formula result
- [ ] `XP_LevelUpTriggered_WhenThresholdReached` — GainExperience → LevelUp path
- [ ] `LeveledUp_EventFires` — RecordingEmitter captures the event
- [ ] `XP_NotAwardedToLoser`
- [ ] `BuildCreature_AtLevel30_SetsCorrectStatsAndXPThreshold` — level picker integration

---

## Enemy Encounter System

`GameController` hardcodes Charmander as the enemy. Before the AI section this needs to be a real system — both so battles feel varied and so the AI has opponents to fight against.

**Design:**
- `EncounterTable` — maps a difficulty tier (or player-species base-stat total) to a list of eligible opponent species IDs and a level range
- Simplest first pass: pick a random species from the full 151 pool whose `BaseStatTotal` is within ±15% of the player's chosen species; set opponent level = player level ± 3
- `GameController.Start` computes the opponent; no new API endpoints needed

**Tasks:**
- [ ] `EncounterTable` static class (or config-driven) — defines tier buckets by BST range; returns a random eligible `(speciesId, level)` pair given a player species
- [ ] `GameController` uses `EncounterTable` instead of hardcoded ID 4
- [ ] `GameController` sets both creatures to the encounter-determined level (player level comes from the Level Picker — default 50)
- [ ] `SpeciesController` or helper: compute `BaseStatTotal` from `PokemonSpecies` for bucketing
- [ ] Tests: encounter table returns species within expected BST range; never returns the player's own species

---

## Learnset System

Currently creatures receive 4 random moves from the full move pool. Learnsets ensure Pokémon only know moves they can actually learn, making battles feel authentic and making the AI evaluator meaningful.

**Prerequisite:** Experience, Levelling & Level Picker must be done first — the chosen level becomes the `atLevel` argument for move initialisation here. Until then, `atLevel` defaults to 50.

**Data:**
- [ ] `PokemonLearnset` model: `SpeciesId` (FK), `MoveId` (FK), `LearnLevel` (int); EF migration on `PokemonDbContext` (learnsets are Pokémon-world data — see Database Architecture section)
- [ ] Import from PokeAPI: the moves array is already present in the existing `/pokemon/{id}` response (`PokeApiPokemon.Moves`); fold learnset parsing into `PokemonImport` rather than adding a separate importer class — filter `version_group_details` to `version_group.name == "red-blue"` and `move_learn_method.name == "level-up"`; persist each `(speciesId, moveId, learnLevel)` entry
- [ ] `Creature.InitializeFromSpecies(species, learnset, allMoves, atLevel)` — populates `MoveSet` with up to 4 moves learned at or below `atLevel` (take the 4 highest-level ones, as Gen 1 does); this replaces the current random move assignment in `GameController.BuildCreature`

**Level-up move learning — tie-in to the Level Picker:**
The Level Picker sets the starting level; `LevelUp()` in battle is the permanent ongoing tie-in. Every level gained during a battle checks the learnset for newly available moves at that exact level.
- [ ] `Creature.LevelUp()` checks the learnset for moves at the new level
- [ ] If `MoveSet.Count < 4`: add automatically; emit `MoveLearned(string CreatureName, string MoveName)` battle event
- [ ] If `MoveSet.Count == 4`: emit `MoveReplacementRequired(string CreatureName, string NewMoveName, IReadOnlyList<MoveInfo> CurrentMoves)` — this is a blocking event that requires a player decision before the battle can resume. The UI must present a "learn/forget" choice; the backend waits on a new `IBattleInput`-style TCS
- [ ] `BattleHub` + `SignalRInput` extended with `ForgetMove(int slotIndex)` / `SkipNewMove()` path
- [ ] `MoveLearned` and `MoveReplacementRequired` handled by all emitters and `useBattleHub.ts`

**Tests:**
- [ ] `Learnset_InitializeFromSpecies_GivesCorrectMovesAtLevel`
- [ ] `Learnset_LevelUp_AddsNewMoveWhenSlotAvailable`
- [ ] `Learnset_LevelUp_EmitsMoveReplacementRequired_WhenFull`

---

## AI Move Selection

Design: `IBattleInput` is the seam. AI implementations score available moves via `IMoveEvaluator` and pick using a selection strategy. `IMoveEvaluator` interface already defined.

**Evaluator dimensions (score each available move):**
- Expected damage — base power × type effectiveness × STAB × stat ratio
- Type effectiveness bonus — super-effective moves strongly preferred
- Stat-stage move value — Swords Dance is high-value at full HP with no threats; Growl is low-value when outmatched
- Priority move value — prefer Quick Attack / high-priority when own HP is low or opponent near KO
- Status move value — Thunder Wave is high-value early; worthless if target already has a status
- PP conservation — small penalty for moves with ≤ 5 PP remaining
- Opponent HP threshold — any move finishes a near-KO target; don't waste a precision pick

**Selection strategies (how scores become a choice):**
- `RandomMoveInput` — ignores evaluators; pure random (wild Pokémon / lowest AI tier)
- `WeightedAIInput(IMoveEvaluator)` — probabilistic, weighted by score (average trainer)
- `GreedyAIInput(IMoveEvaluator)` — always picks highest score (Elite Four / boss tier)

**Composition:**
- `CompositeEvaluator(IEnumerable<(IMoveEvaluator evaluator, double weight)>)` — weighted sum; trainer "personality" = different weights (aggressive vs. defensive vs. status-heavy)

**Implementation tasks:**
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

## Catch Mechanic

Deferred until the Phaser animation layer and item/bag UI exist. The catch mechanic requires:
a bag action in the move menu, an item inventory, a capture-roll formula, and an animation sequence (throw → shake × 3 → catch or escape). Implementing it before those exist produces a dead-end backend with no playable surface.

**When ready:**
- [ ] `StatusCondition` and `Battle` extended with a "fleeing" state (catching replaces an attack action)
- [ ] Gen 1 capture formula: `floor((MaxHP × 3 - HP × 2) × CatchRate / (MaxHP × 3))` vs. a 0–255 roll
- [ ] `PokemonSpecies.CatchRate` already imported ✓
- [ ] `CaptureAttempted(string TargetName, bool Caught)` battle event
- [ ] If caught: battle ends with `BattleEnded(winner: "Player", reason: "Caught")` variant

---

## Web UI

Stack: React 18 + TypeScript + SignalR. Phaser 3 for sprite/animation canvas.

### Completed ✅
- ASP.NET Core host: SignalR, CORS, static files, BattleHub, SpeciesController, GameController
- React skeleton: Vite, React Router, Title/Select/Battle routes
- Battle engine output abstraction: `BattleEvent` hierarchy, `IBattleEventEmitter`, `ConsoleBattleEventEmitter`
- Title screen
- Starter selection: species grid, type badges, sprites, POST /api/game/start
- SignalR battle emitter: `IBattleClient`, `SignalRBattleEventEmitter`, `GameSessionManager`, `SignalRInput`
- Battle screen shell: HP bars, move menu (2×2, PP, type badges), battle log, status badges, `useBattleHub` reducer

### Phaser Canvas
- [ ] Add `phaser` npm dependency to `ClientApp`
- [ ] `BattleCanvas.tsx` — mounts a Phaser `Game` instance in a `useEffect`; destroys on unmount
- [ ] `BattleScene.ts` — Phaser Scene that loads front sprite (`/sprites/front/{id}.png`) for enemy and back sprite (`/sprites/back/{id}.png`) for player; positions them at the correct diagonal layout (enemy top-right, player bottom-left)
- [ ] React↔Phaser bridge: a shared `PhaserBridge` event emitter (tiny `EventEmitter` or `mitt`) — React dispatches animation commands (`playMoveAnimation`, `playFaintAnimation`); Phaser emits `animationComplete` back to React
- [ ] Current CSS sprite placeholders replaced by the Phaser canvas; React retains the HP/status overlay layer

### Animations
- [ ] `MoveUsed` → attacker sprite bobs toward the opponent (lunge + return tween, ~300ms)
- [ ] `DamageDealt` → target sprite white-flash (swap to white texture for 2 frames); HP bar drains animated over ~600ms rather than snapping
- [ ] `CreatureFainted` → target sprite slides down and fades out (~500ms)
- [ ] Move menu re-enabled only after `PhaserBridge` emits `animationComplete` for the full turn sequence
- [ ] `useBattleHub` state gains `animating: boolean`; move buttons check both `phase === 'choosing'` and `!animating`

### Polish
- [ ] `BattleEndedOverlay` — covers the battle screen on `BattleEnded` event; shows winner name, "Play Again" button navigates to `/select`; "Main Menu" navigates to `/`
- [ ] Level-up notification toast (when `LeveledUp` event fires during battle)
- [ ] Move menu STAB indicator — subtle highlight on moves whose type matches the player creature's type
- [ ] Color-coded effectiveness in battle log (super-effective in green, not very effective in grey, no effect in red)
- [ ] Sprite shake tween on damage received (small horizontal oscillation before HP drain)
- [ ] `ConsoleInput : IBattleInput` — numbered move menu for terminal play; wire into `Program.cs` console runner as the player-side input (low priority for web path but completes the debug runner)

---

## Database Architecture

**Current two-database model (keep as-is):**
- `pokemon.db` / `PokemonDbContext` — Pokémon-world static data: species, base stats, types, growth rates, catch rates, learnsets
- `moves.db` / `MovesDbContext` — mechanics data: moves, damage type, accuracy, PP, stat effects, status effects

**Where new tables go:**
- `PokemonLearnset` → **`pokemon.db`** (`PokemonDbContext`). It is per-species data; it belongs alongside `PokemonSpecies`.
- Any future species-linked data (egg groups, evolution chains, held items) → `pokemon.db`
- Any future move-linked data (move combos, Z-move mappings) → `moves.db`

**When a third database is warranted:**
Player save state — caught Pokémon, active party, items, trainer name, save slots — is the first genuinely orthogonal dataset: it's runtime-mutable data that neither `pokemon.db` nor `moves.db` should own. Defer `PlayerDbContext` / `save.db` until the save system (after the Catch Mechanic).

**PokeApiConnector scaffolding for learnset import:**
The moves array is already returned in the existing `/pokemon/{id}` response. Fold learnset parsing into `PokemonImport` rather than adding a `LearnsetImport` class — no extra API calls needed. Steps:
- [ ] Extend `PokeApiPokemon` DTO with a `Moves` array if not already present
- [ ] In `PokemonImport`, after persisting `PokemonSpecies`, parse `move.version_group_details` and `INSERT OR IGNORE INTO PokemonLearnset` for each valid entry
- [ ] `PokeApiConnector` re-runnable (idempotent) — already the case via `UpsertAttack` / EF `SetValues`; apply the same pattern to learnset entries

---

## Tech Debt / Cleanup

### Done
- [x] Remove dead scaffolding: `Body`, `Brain`, `BodyPart`, `Special`, `Dragon`, `CreatureType`, `Attributes.SetAttributesByCreatureType`
- [x] Fix `.gitignore` and `.gitattributes`; add `.editorconfig`; add `global.json` (SDK pin 9.0.200)
- [x] Adopt EF Core migrations — `EnsureDatabaseCreated()` calls `Database.Migrate()`
- [x] Remove `Creature.Attack()` direct-damage method
- [x] Resolve `Creature` class/namespace collision — namespace now `creaturegame.Creatures`
- [x] Add `README.md`; add `dev.ps1` launcher
- [x] `StatStages` struct→class — silent mutation fix; `Creature.ResetBattleState()` called at battle start
- [x] `AsNoTracking()` on all read-only DB service methods
- [x] Pending-session TTL in `GameSessionManager` (2-minute eviction)
- [x] `AlwaysHitRules` test helper — eliminates Gen 1 1/256-miss flakiness in status unit tests

### Known Gaps (not bugs — design decisions for future sections)
- `GrowthRate` enum missing `Erratic` and `Fluctuating` — importer falls back to `MediumFast`; fix in Experience & Levelling
- `StatusCondition` missing `BadPoison` — Toxic maps to `None` on import until Bad Poison (Toxic)
- `GameController.BuildCreature` hardcodes level 50 and picks random moves — level picker fixed by Experience & Levelling; correct learnset moves fixed by Learnset System
- `Creature.LevelUp()` calls `Console.WriteLine` directly — fixed by Experience & Levelling (`LeveledUp` event)
- `PokemonService` / `AttackService` not registered in DI — `GameController` uses direct `new()`; intentional for now, revisit when scoped lifetime or multiple-context scenarios arise
