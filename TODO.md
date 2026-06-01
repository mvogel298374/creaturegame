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

## Generation Abstraction — Stat Selection ✅ DONE

- [x] `IBattleRules.GetOffensiveStat(Creature, AttackType)` and `GetDefensiveStat(Creature, AttackType)` added
- [x] `Gen1BattleRules`: Physical → Attack/Defense; Special → Special (combined Gen 1 stat)
- [x] `DamageCalculator`: duplicated crit/non-crit stat selection block collapsed; stat reads delegated to rules
- [x] `AlwaysHitRules` and `AlwaysCritRules` test helpers updated to implement new methods
- [x] 2 new tests — `DamageCalculator_UsesOffensiveStatFromRules`, `DamageCalculator_UsesDefensiveStatFromRules` (124 total passing)

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

### Phaser Canvas ✅ DONE
- [x] `phaser` + `mitt` npm dependencies added to `ClientApp`
- [x] `BattleCanvas.tsx` — mounts Phaser `Game` lazily (dynamic import, separate chunk); destroys on unmount
- [x] `BattleScene.ts` — loads front/back sprites, diagonal layout (enemy top-right, player bottom-left), entry slide-in animation with Web Audio cries
- [x] `PhaserBridge.ts` — typed mitt emitter; React dispatches `playMoveAnimation` / `playFaintAnimation`; Phaser emits `animationComplete` back
- [x] `AudioEngine.ts` — Web Audio API synth: `playCry`, `playFaintCry`, `playHit`, `playTick`
- [x] CSS sprite `<img>` placeholders replaced by the Phaser canvas; React retains HP/status/nameplate overlay layer (z-index 2)

### Animations ✅ DONE
- [x] Entry: sprites slide in from edges with species cries; idle bob tween starts after entry
- [x] `MoveUsed` → attacker lunges toward opponent (~150ms in, ~200ms back); target white-flash + `playHit()`
- [x] `DamageDealt` → `UPDATE_HP` fires immediately (CSS `transition: width 0.6s ease-out`); log message appears after 650ms
- [x] `CreatureFainted` → sprite slides down + fades (~500ms) with `playFaintCry()`; log appears after
- [x] `LeveledUp` → XP bar fills to 100% (CSS `transition: width 0.9s linear`) then resets; log after
- [x] All events enqueued — log text always appears **after** the relevant animation (Gen 1 feel)
- [x] Move menu re-enabled only after animation queue drains (`animationComplete` bridge event)
- [x] `useBattleHub` state gains `animating: boolean`; FIGHT + move buttons check `phase === 'choosing' && !animating`

### Polish
- [ ] `BattleEndedOverlay` — covers battle screen on `BattleEnded`; shows winner, "Play Again" → `/select`, "Main Menu" → `/`
- [ ] Level-up notification toast on `LeveledUp` event
- [ ] Move menu STAB indicator — subtle highlight on moves matching player's type
- [ ] Color-coded effectiveness in battle log (super-effective green, not very effective grey, no effect red)
- [ ] Sprite shake tween on damage received
- [ ] `ConsoleInput : IBattleInput` — numbered move menu for terminal play (low priority)

---

## Browser-Based UI Testing (Playwright)

Promote the manual Puppeteer checklist (`ui_checklist.md`) into a committed, CI-runnable E2E suite. Playwright drives the **React DOM** (≈70% of the checklist); the **Phaser canvas** is tested through the existing `mitt` bridge, not by inspecting pixels.

**Key constraint:** Playwright/Puppeteer query the DOM only. Phaser renders to one opaque `<canvas>` — sprite slide-in, idle bob, lunge, faint fade, and audio (cries/hit/status) are **not** directly assertable. Don't attempt pixel/sprite selectors, and never assert wall-clock animation durations (the checklist's "~1.8 s silence", "~350 ms lunge", "~600 ms HP drain") in E2E — they are the #1 source of flake. Assert **event ordering** via the bridge instead; unit-test durations separately if needed.

**Testability seams (prerequisite plumbing):**
- [ ] Add `data-testid` attributes to React overlays — nameplates, HP/XP bars, status badge, battle log, FIGHT/move grid, PP counts, level slider, CONFIRM
- [ ] Expose the `PhaserBridge` `mitt` emitter on `window` behind a dev/test flag so specs can await `entryComplete` / `animationComplete` and observe `playMoveAnimation` / `playHitSound` / `playFaintAnimation` / `playStatusSound`
- [ ] Add an "instant animations" test flag — set Phaser tween time scale high (or zero delays) and collapse CSS transition durations so flows run deterministically and fast

**Scaffold:**
- [ ] `npm create playwright` in `ClientApp/`; config points at the Vite dev server (`:5173`), single Chromium project to start
- [ ] CI step (or `dev.ps1`-adjacent script) that boots backend + frontend, runs the suite headless, and tears down

**Specs (mirror `ui_checklist.md` sections):**
- [ ] §1–2 Title + Starter selection — text, NEW GAME, 151-card grid, type badges, BST, level slider range/default, CONFIRM navigation (pure DOM)
- [ ] §3 Battle entry — nameplates + HP/XP/status overlays render; "X VS Y" log line; FIGHT/CHECK enabled; await `entryComplete` rather than sleeping
- [ ] §4 Move menu — 2×2 grid, PP counts, 0-PP greyed/unclickable, BACK, CHECK POKEMON
- [ ] §5 Attack sequencing — assert **order** of bridge events (`playMoveAnimation` → `playHitSound` → HP-bar update → log line) and the animating-lock disable/re-enable, not durations
- [ ] §6 Status conditions — badge on correct nameplate; log grammar ("fell asleep!", "is fully paralyzed!", "thawed out!", etc.)
- [ ] §7 Faint & end — `playFaintAnimation` fires; menu stays locked until `animationComplete`; XP fill + "grew to level N!"; winner line; QUIT → title
- [ ] §8 (optional) Visual regression snapshots of the canvas **at settled states only** (post-entry, post-faint) — expect maintenance cost; many teams skip canvas snapshots

**Notes:**
- Keep Puppeteer-MCP for agent-driven, ad-hoc verification during a session; Playwright is the durable regression layer. The two are complementary.
- Audio is verified by asserting the bridge *fired* the sound event, never by capturing sound.

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

### Architecture Review (2026-06-01) — prioritised

Findings from a full read of the core engine + web layer. The conceptual architecture (generation seams, headless event-sourced engine, `IBattleInput`) is sound and stays; these are concentrated in the web/runtime layer plus one consistency gap. Ordered by severity.

#### 1. Web battle lifecycle — disconnect leak + broken reconnect + swallowed errors `[runtime bug]`
`SignalRInput.ChooseMoveAsync` (`SignalRInput.cs:14`) awaits a `TaskCompletionSource<int>` with **no cancellation path**, and `BattleHub` has no `OnDisconnectedAsync`. If the player closes the tab mid-turn, the fire-and-forget battle loop (`GameSessionManager.cs:51`, `_ = Task.Run(...)`) awaits that TCS forever — the `SignalRInput`, the two `Creature`s, and the loop task are never collected. **Every abandoned battle is a permanent leak.**

Fix (minimal, no core-engine signature change — cancellation surfaces as the awaited input throwing):
- [x] `SignalRInput`: add a `_cancelled` flag + `Cancel()` that sets it and calls `_tcs?.TrySetCanceled()`. `ChooseMoveAsync` checks the flag on entry and throws `OperationCanceledException` (covers disconnect during enemy turn/animation when `_tcs` is null and the *next* player turn would otherwise hang).
- [x] `BattleHub.OnDisconnectedAsync` → `manager.AbandonBattle(connectionId)` → looks up the input and calls `Cancel()`.
- [x] `GameSessionManager`: wrap the `Task.Run` body in try/catch — swallow/log `OperationCanceledException` at debug, log other exceptions at error (currently a throw in the loop is silent and the client just hangs).
- [ ] **Reconnect (follow-up within this item):** inputs + emitter are keyed by `connectionId`, but the client uses `.withAutomaticReconnect()`. On reconnect the connectionId changes, `_pending` is already consumed, and events keep targeting the dead connection while input can't reach the live battle. Re-key the active battle by `gameId` (an `ActiveBattle { SignalRInput, CancellationTokenSource, string CurrentConnectionId }` map), have `SignalRBattleEventEmitter` resolve the *current* connectionId dynamically, and rebind on the second `OnConnectedAsync`.

#### 2. Pull `BattleState` extraction forward (was: "when save system is built") `[latent bug source]`
`Creature` conflates persistent identity (Name, DVs, Exp, base stats), transient battle state, and behaviour. `ResetBattleState()` (`Creature.cs:112`) is a hand-maintained reset list that must be updated for every new transient field — miss one and state silently leaks between battles (the `StatStages` struct→class bug was exactly this class of fault). This is *already* a bug source, not just a future-save concern.
- [ ] Extract transient fields (`Status`, `SleepTurns`, `ConfusedTurns`, `ToxicCounter`, `Stages`, `IsRecharging`, `IsFlinched`, `HasLeechSeed`, `BindingTurnsRemaining`, `IsTwoTurnCharging`, `ChargingMove`) into a `BattleState` class held as `Creature.Battle`
- [ ] Replace `ResetBattleState()` with `Creature.Battle = new BattleState()` (a fresh object instead of a field-by-field reset — structurally impossible to "forget a field")
- [ ] Update `StatusResolver`, `AttackAction`, `Battle` references; keep a save-friendly permanent/transient split ready for `save.db`

#### 3. RNG is the one fidelity-critical concern not behind a seam `[consistency]`
Crit, accuracy, speed tie-break, Metronome, and move assignment call `Random.Shared` directly inside the engine. Tests route around it with `AlwaysHitRules`/`AlwaysCritRules`, but for a true Gen 1 clone heading toward roguelike runs, **seeded/replayable RNG** will matter — and it's the natural thing to inject through the same seam pattern used everywhere else.
- [ ] Add `IRandomSource` (e.g. `Next(int maxExclusive)`, `Next(int min, int max)`) with a `SystemRandomSource` default and a `SeededRandomSource` for tests/replays
- [ ] Thread it through `Battle`, `AttackAction`, `DamageCalculator`, `Gen1BattleRules`, `EncounterSelector` (constructor-injected, default to shared instance)
- [ ] Makes the whole suite deterministic without the `AlwaysHit/AlwaysCrit` shims; enables seeded run replays later

#### 4. Speed tie-break uses RNG as a sort key `[footgun]`
`Battle.cs:88` — `.ThenBy(_ => Random.Shared.Next())` calls RNG inside the `OrderBy` comparator, which is an ill-defined key (LINQ may invoke the selector multiple times per element). Harmless at 2 actions; bites the moment the turn queue grows (doubles, multi-battles).
- [ ] Resolve the tie with a single coin flip computed once (fold into #3 — use the injected `IRandomSource`)

#### 5. DbContext via `new()` instead of DI `[maintainability]`
`GameController` does `new PokemonDbContext()` / `new MovesDbContext()` (`GameController.cs:20,25`); `PokemonService`/`AttackService` aren't registered. Works only because `OnConfiguring` hardcodes the path. Note the background battle loop touches **no** DB (data is materialised up front and passed in), so the scoped-context-in-`Task.Run` hazard doesn't apply — the real costs are lost connection pooling and tests needing real SQLite files.
- [ ] Register `AddDbContextFactory<PokemonDbContext>()` / `<MovesDbContext>()` in `Program.cs`; inject the factory into the controller/services
- [ ] Register `PokemonService` / `AttackService` in DI and use them instead of inline `new()` + raw `ToListAsync()`

#### 6. Frontend battle-log queue is structurally racy `[design]`
The imperative `enqueue` / `waitForBridge` / hand-tuned `delay()` choreography in `useBattleHub` coordinating Phaser over the `mitt` bus is where today's two bugs lived (permanent freeze + listener leak). The recent try/catch + timeout hardening is defensive patching over an inherently timing-fragile design.
- [ ] Model the log as a reducer over the event stream with explicit per-event states; treat `animationComplete` as an event, not an awaited side effect
- [ ] Dovetails with the Playwright-testing item (bridge events as the test seam) — do them together

#### 7. Architecture / decision-log doc `[docs — after the above]`
The doc set is strong, but the *why* behind the two-DB split, event sourcing, and the seam invariants lives only implicitly. For a project explicitly built to extend generation-by-generation, capture these as an `ARCHITECTURE.md` (or lightweight ADR log) so the invariants survive future drift.
- [ ] Document: two-DB rationale, event-sourced engine + emitter pattern, the three seams (`ITypeChart`/`IBattleRules`/`IStatCalculator`) and the "never branch on generation" rule, the web session/SignalR flow, and the import-vs-runtime data boundary
- [ ] Cross-link from `CLAUDE.md` Key Files table

### Known Gaps
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector exists in the UI
- Enemy Pokémon do not evolve — wire into level-up system when Game Loop is built
- `GameController.BuildCreature` uses random moves — fixed by Learnset System

### Fixed ✅
- Battle log froze on faint (stuck on last damage line, no "fainted!"/winner): `BattleScene`'s `destroy()` was dead code (Phaser never calls it), so `bridge.on` listeners leaked across canvas remounts (HMR/StrictMode) and a stale scene's `playFaintAnimation` threw on a destroyed sprite — now removed via `SHUTDOWN`/`DESTROY` scene events (`teardown`). Hardened the queue too: `drainQueue` try/catch-continues per task with a `finally` reset, and `waitForBridge` times out after 3 s so a lost `animationComplete` can't hang the log.
- Battle-log text polish: move names display formatted (`fury-attack` → `FURY ATTACK`) via `utils/format.ts#formatMoveName`, applied to the log (`MoveUsed`/`MoveMissed`/`BindingStarted`) and the move-menu grid; Gen 1 per-move two-turn charge lines (`chargingMsg`: Dig "dug a hole!", Fly "flew up high!", Solar Beam "took in sunlight!", etc.) replace the generic "is charging up X!"; immunity now reads "It doesn't affect X..." with no damage number/crit and no hit sound (was "took 0 damage! It had no effect.")
- Metronome (`MoveEffect.Metronome`): picks a random eligible Gen 1 move and executes it in full; move pool threaded from `GameController` → `GameSessionManager` → `Battle` → `AttackAction`; DB updated via re-run of importer
