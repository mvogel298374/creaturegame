# Battle Sim – TODO List

> **See also:** `CLAUDE.md` (session setup, architecture, commands) · `AI_CONTEXT.md` (agent profiles) · `DESIGN_GUIDES.md` (mechanics rules) · `DEV_STANDARDS.md` (coding conventions)

## Priority 1 – Type Chart ✅ DONE
- [x] Create `ITypeChart` interface
- [x] Implement `Gen1TypeChart` with full 17-type Gen 1 matrix (Ghost/Psychic bug, Poison super vs Bug, no Steel/Dark/Fairy)
- [x] Wire into `DamageCalculator` via interface (swappable per-generation)
- [x] `AttackAction` and `Battle` accept `ITypeChart` — one injection point to swap generation
- [x] 7 type chart tests added and passing

## Priority 2 – PP Tracking ✅ DONE
- [x] Switch `Creature.MoveSet` from `List<Attack>` to `List<PokemonAttack>`
- [x] `AttackAction` decrements `PowerPointsCurrent`, checks > 0 before executing
- [x] Handle Struggle when all PP = 0

## Priority 3 – Move Priority Fix ✅ DONE
- [x] `AttackAction` constructor: read `move.Priority` instead of hardcoding 0

## Priority 4 – Status Condition Application ✅ DONE
- [x] Add `StatusEffect` (`StatusCondition`) property to `Attack`; add EF migration
- [x] Add `meta.ailment` mapping to `PokeApiMove`; import `ailment.name` → `StatusEffect`, `ailment_chance` → `EffectChance` in `MoveImport`
- [x] `AttackAction.ExecuteAsync()`: after damage, roll `EffectChance` and set `Target.Status` if target has no status and move has a `StatusEffect`
- [x] Set `SleepTurns` (1–7, random) when applying Sleep
- [x] Tests: status applied on hit; not applied when target already statused; secondary effect chance respected

## Priority 5 – Status Effects in Battle Loop ✅ DONE
- [x] Pre-turn: Sleep skips action and decrements `SleepTurns`; wakes when counter hits 0
- [x] Pre-turn: Freeze skips action; thaws on any Fire-type move hitting the frozen target
- [x] Pre-turn: Paralysis — 25% chance to skip action
- [x] Stat modifiers: Burn halves physical Attack in `DamageCalculator`; Paralysis quarters Speed in turn ordering
- [x] End-of-turn: Burn deals 1/16 max HP; Poison deals 1/16 max HP
- [x] Pseudo-status — Confusion: `Creature.ConfusedTurns` counter; 50% chance to hurt itself each turn (40 base power, typeless); clears when counter expires (2–5 turns, Gen 1)

## Priority 6 – Critical Hits & Stat Stages ✅ DONE

**Stat stages:**
- [x] `StatStages` struct on `Creature` — Attack, Defense, Special, Speed, Accuracy, Evasion each clamped to [-6, +6]; `Clear()` + `Raise*()` helpers
- [x] `IBattleRules.GetStatMultiplier(int stage)` — Gen 1: 2/(2+|n|) for n≤0, (2+n)/2 for n>0 (0.25× at -6, 4× at +6)
- [x] `IBattleRules.GetAccuracyStageMultiplier(int stage)` — Gen 1: 3/(3+|n|) for n≤0, (3+n)/3 for n>0
- [x] `IBattleRules.GetHitThreshold` + `AccuracyRollBound` — replaces old `< 100` check in `AttackAction`; Gen 1 0–255 scale
- [x] `DamageCalculator` applies Attack/Defense/Special stage multipliers via `IBattleRules`
- [x] `StatusResolver.EffectiveSpeed` folds in Speed stage multiplier (stacks with Paralysis quartering)
- [x] Gen 1 accuracy: all moves 0–255 scale; roll 255 always misses (1/256 bug encoded in `Gen1BattleRules`)

**Critical hits:**
- [x] `Attack.IsHighCrit` bool — EF migration added; PokeApiConnector imports `meta.crit_rate > 0`
- [x] `IBattleRules.GetCritChance` — Gen 1 normal: floor(BaseSpeed/2)/256; high-crit: min(floor(BaseSpeed/2)×8, 255)/256
- [x] `IBattleRules.CritMultiplier` → 2.0; `CritIgnoresStatStages` → true in Gen 1
- [x] `DamageCalculator` rolls crit; Gen 1 crit path uses raw Attributes stats — no stages, no Burn penalty
- [x] `DamageDealt` event carries `IsCrit` flag; console prints "A critical hit!"; SignalR payload includes it

**Tests (all passing — 78 total):**
- [x] `GetStatMultiplier_Plus6_Returns4` / `Minus6_Returns0Point25` / `Zero_Returns1`
- [x] `StatStage_Plus6_AttackDamageHigherThanBase` / `Minus6_AttackDamageLowerThanBase`
- [x] `SpeedStage_Plus6_IncreasesEffectiveSpeed` / `StacksWithParalysisQuartering`
- [x] `HitThreshold_100AccuracyNeutralStages_Is255` (1/256 bug verified)
- [x] `HitThreshold_AccuracyMinus6Stage_ReducesThreshold` / `EvasionPlus6Stage_ReducesThreshold`
- [x] `CritChance_HighCritMove_IsHigherThanNormal` / `CritMultiplier_Gen1_IsTwo`
- [x] `Crit_IgnoresAttackersNegativeAttackStage` / `Crit_IgnoresDefendersPositiveDefenseStage`
- [x] `Crit_Gen1_DropsBurnAttackPenalty`

## Move Effects Layer (generation-agnostic)
Move effects that are properties of the move itself rather than generation rules.
Haze is the first concrete case; others follow the same pattern.
- [ ] `MoveEffect` enum on `Attack`; `Attack.Effect` property; EF migration
- [ ] `AttackAction` switches on `Effect` after damage/status: `ClearAllStatus` (Haze) clears `Status`, `SleepTurns`, `ConfusedTurns`, and `StatStages` on both Pokémon
- [ ] Future effects to add as moves require: `Flinch`, stat-stage changes (`SwordsDance`, `Growl`, etc.), `Recharge` (Hyper Beam), `Leech Seed`, `Substitute`

## Priority 7 – Move Selection (Player Input)
- [ ] Implement `ConsoleInput : IBattleInput` — numbered move menu, shows PP and type
- [ ] Wire `ConsoleInput` into `Program.cs` for the player side; enemy keeps `AutoSelectInput`

## Priority 8 – Experience & Catch System
- [ ] `Battle.cs` awards XP to winner on faint (Gen 1 formula)
- [ ] Basic catch mechanic using `PokemonSpecies.CatchRate`

## Priority 9 – Learnset System
- [ ] `PokemonLearnset` DB table: species ID → move ID → level learned
- [ ] Import learnsets from PokeAPI (`/pokemon/{id}/moves`)
- [ ] `Creature.InitializeFromSpecies()` populates starting moveset by level

## Priority 10 – AI Move Selection
Design: `IBattleInput` is already the seam. AI implementations score available moves via
`IMoveEvaluator` and pick using a selection strategy.

**Evaluator dimensions (score each available move):**
- Expected damage — base power × type effectiveness × STAB × stat ratio
- Type effectiveness bonus — super-effective moves strongly preferred
- Priority move value — prefer Quick Attack / high-priority when own HP is low or opponent near KO
- Status move value — Thunder Wave is high-value early; worthless if target already has a status
- PP conservation — small penalty for moves with ≤ 5 PP remaining
- Opponent HP threshold — any move finishes a near-KO target; don't waste a precision pick

**Selection strategies (how scores become a choice):**
- `RandomMoveInput` — ignores evaluators; pure random (wild Pokémon / lowest AI tier)
- `WeightedAIInput(IMoveEvaluator)` — probabilistic, weighted by score (average trainer)
- `GreedyAIInput(IMoveEvaluator)` — always picks highest score (Elite Four / boss tier)

**Composition:**
- `CompositeEvaluator(IEnumerable<(IMoveEvaluator evaluator, double weight)>)` — weighted sum;
  trainer "personality" = different weights (aggressive vs. defensive vs. status-heavy)

**Implementation tasks:**
- [ ] `DamageEvaluator : IMoveEvaluator`
- [ ] `TypeEffectivenessEvaluator : IMoveEvaluator`
- [ ] `StatusMoveEvaluator : IMoveEvaluator`
- [ ] `CompositeEvaluator : IMoveEvaluator`
- [ ] `RandomMoveInput : IBattleInput`
- [ ] `GreedyAIInput : IBattleInput`
- [ ] `WeightedAIInput : IBattleInput`

## Priority 11 – Web UI
Full plan in `FRONTEND_PLAN.md`. Stack: React 18 + Phaser 3 + SignalR, hosted by a new
`creaturegame.Web` ASP.NET Core project. Flow: Title → Starter Selection → Battle.

**Phase 1 – .NET plumbing**
- [ ] Add `creaturegame.Web` ASP.NET Core project; configure SignalR + CORS
- [ ] Stub `BattleHub`, `SpeciesController` (GET /api/species), `GameController` (POST /api/game/start)
- [ ] `GameSessionManager` maps SignalR connectionId → `GameSession` (Battle + SignalRInput)

**Phase 2 – React skeleton**
- [ ] Vite + React + TypeScript in `creaturegame.Web/ClientApp`
- [ ] React Router: `/` Title · `/select` StarterSelection · `/battle` Battle
- [ ] SignalR client connects; log received events to console

**Phase 3 – Battle engine output abstraction** ✅ DONE
- [x] Define `BattleEvent` record hierarchy (`MoveUsed`, `DamageDealt`, `StatusApplied`, etc.)
- [x] `IBattleEventEmitter` interface; inject into `Battle`, `AttackAction`, `StatusResolver`
- [x] Replace all `Console.WriteLine` with `_emitter.Emit(new TypedEvent(...))`
- [x] `ConsoleBattleEventEmitter` preserves console runner
- [x] All 63 tests still pass

**Phase 1 – .NET plumbing** ✅ DONE
- [x] Add `creaturegame.Web` ASP.NET Core project; configure SignalR + CORS
- [x] Stub `BattleHub`, `SpeciesController` (GET /api/species), `GameController` (POST /api/game/start)

**Phase 2 – React skeleton** ✅ DONE
- [x] Vite + React + TypeScript in `creaturegame.Web/ClientApp`
- [x] React Router: `/` Title · `/select` StarterSelection · `/battle` Battle
- [x] Title screen styled and visible; placeholder screens for select and battle

**Phase 4 – Title screen** ✅ DONE (part of Phase 2 above)

**Phase 5 – Starter selection** ✅ DONE
- [x] Species grid fetched from `/api/species`; type badges; front sprites from `/sprites/front/{id}.png`
- [x] Confirm pick calls `POST /api/game/start`; navigates to `/battle`
- [x] 151 front + back sprites downloaded by PokeApiConnector and served as static files

**Phase 5.5 – SignalR battle emitter** ✅ DONE
- [x] `IBattleClient` typed hub interface — `OnBattleEvent(string eventType, object payload)`
- [x] `SignalRBattleEventEmitter : IBattleEventEmitter` — wraps `IHubContext<BattleHub, IBattleClient>`; maps all BattleEvent subtypes to camelCase JSON payloads; fire-and-forget push
- [x] `GameSessionManager` singleton — `RegisterSession(player, enemy) → gameId`; `StartBattleAsync(gameId, connectionId)` wires emitter + starts battle on thread-pool
- [x] `BattleHub` upgraded to `Hub<IBattleClient>`; `OnConnectedAsync` reads `?gameId` query param and starts battle
- [x] `GameController.Start` builds both creatures from DB (player species + Charmander enemy, 4 random moves each), registers session, returns `{ gameId }`
- [x] `StarterSelection` forwards `gameId` in navigate state to `/battle`
- [x] `ConsoleBattleEventEmitter` kept unchanged — used by unit tests and local debug runner

**Phase 6 – Battle screen shell (React, no Phaser yet)** ✅ DONE
- [x] Battle field: sky/ground background, diagonal sprite layout (enemy front top-right, player back bottom-left), nameplates with HP bars; player nameplate adds XP bar
- [x] Action panel: FIGHT (stub) + CHECK POKEMON (shows base stats + type badges); species passed via navigate state from StarterSelection
- [x] `useBattleHub` hook + `useBattleState` reducer (HP, status, moves) — wire SignalR; replace mock state
- [x] Move menu (shown when FIGHT pressed; disabled until TurnStarted event; 2×2 grid with PP + type badge)
- [x] Text log for battle events (scrollable left panel; live HP, status, crit, effectiveness messages)
- [x] `SignalRInput : IBattleInput` — blocks on TCS until player sends ChooseMove; wired into `GameSessionManager` for player side; `BattleHub.ChooseMove` forwards to it
- [x] Status badges on nameplates (PSN/BRN/PAR/SLP/FRZ)
- [x] Integration tests: `TurnControlledInput` + `RecordingEmitter` helpers; 5 new tests covering player-input path, event ordering, full multi-turn simulation, and poison end-of-turn kill
- [x] Bug fix: fire move hitting frozen target no longer applies its own burn effect after thaw (`justThawed` guard in `AttackAction`)
- [x] Test hardening: `AlwaysHitRules` helper eliminates Gen 1 1/256-miss flakiness in status/freeze unit tests; `MigrationTests` verifies `IsHighCrit` column

**Phase 7 – Phaser canvas**
- [ ] `BattleCanvas.tsx` mounts Phaser; `BattleScene.ts` loads + positions sprites

**Phase 8 – Animations**
- [ ] MoveUsed → attacker bob tween; DamageDealt → white flash + HP drain; CreatureFainted → slide+fade
- [ ] React re-enables move menu only after Phaser emits `turnAnimationComplete`

**Phase 9 – Polish**
- [ ] Result overlay (win/lose + Play Again); effectiveness labels; status badges
- [ ] `Program.cs` console runner retained as dev/debug tool

---

## Tech Debt / Cleanup

### Done
- [x] Remove dead scaffolding: `Body`, `Brain`, `BodyPart`, `Special`, `Dragon`, `CreatureType`, `Attributes.SetAttributesByCreatureType`
- [x] Remove unused `using System.Net.NetworkInformation` from `Attributes.cs`
- [x] Fix `.gitignore` — add local tool config exclusions (`.claude/settings.local.json`, `.ai/`), untrack committed build artifacts and IDE files
- [x] Add `global.json` to pin .NET SDK to 9.0.200
- [x] Adopt EF Core migrations — `InitialCreate` scaffolded for both `MovesDbContext` and `PokemonDbContext`; `EnsureDatabaseCreated()` now calls `Database.Migrate()` instead of raw `ALTER TABLE` hacks
- [x] Remove hardcoded placeholder `Traits` from `Creature` constructor; `Trait.cs` / `TraitType.cs` scaffolding retained for future Abilities layer
- [x] Add `.editorconfig` for consistent indentation (4 spaces), charset (UTF-8), and editor-side line ending preference
- [x] Add `.gitattributes` to normalise line endings in the repo (LF in git, auto CRLF on Windows checkout)
- [x] Add `README.md` at repo root
- [x] Remove `Creature.Attack()` direct-damage method — bypasses `DamageCalculator`/type chart and has a naming collision with the `Attack` class; `AttackAction` is the correct path
- [x] Remove redundant `Attributes.GetCurrentHealth()` call from `IsAlive()` — now reads `Attributes.HP` directly; `IsAlive()` itself retained as a meaningful predicate

- [x] Add `dev.ps1` launcher — starts `dotnet watch run` (port 5100) + `npm run dev` (port 5173) in separate windows from a single command

### Pending
- [x] Resolve `Creature` class/namespace name collision — renamed namespace to `creaturegame.Creatures`; all 16 files updated, fully-qualified `Creature.Creature` references eliminated
- [x] Remove redundant `Attributes.GetSpeed()` wrapper — no callers; deleted
- [x] Decide on `.idea/` strategy — keep fully excluded; solo project, no run configs to share, dataSources.local.xml contains local paths
- [x] Consolidate or clarify relationship between `AI_CONTEXT.md` / `DESIGN_GUIDES.md` / `DEV_STANDARDS.md` and `CLAUDE.md` — all files now carry See Also cross-reference tables; CLAUDE.md has a Key Files section and delegates TODO state to TODO.md
- [x] Decide on `Traits` scaffolding — removed `Trait.cs` / `TraitType.cs`; Gen 1 has no Abilities; reintroduce as a proper Abilities layer when that priority is reached
- [x] Retrospective maintenance pass — `StatStages` struct→class (silent mutation fix for stat-stage moves); `Creature.ResetBattleState()` called at top of `Battle.StartFightAsync`; `AsNoTracking()` added to all read-only methods in `PokemonService`/`AttackService`; pending-session TTL added to `GameSessionManager`; stale Priority 6 comments updated; dead dev-note removed from `Creature.GainExperience`
