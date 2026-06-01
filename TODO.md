# Battle Sim – TODO List

> **See also:** `CLAUDE.md` (session setup, architecture, commands) · `AI_CONTEXT.md` (agent profiles) · `DESIGN_GUIDES.md` (mechanics rules) · `DEV_STANDARDS.md` (coding conventions)

---

## Completed ✅

<details>
<summary>Type Chart, PP, Status, Crits, Move Effects, Damage Categories, Bad Poison, XP/Levelling, Enemy Encounters</summary>

**Type Chart** — `ITypeChart` + `Gen1TypeChart` (15-type Gen 1 matrix, Ghost/Psychic bug, Poison→Bug quirk). Wired into `DamageCalculator` and `AttackAction`.

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

Status: **harness + core specs landed** (9 specs, run via `npm run test:e2e` or the VS Code Playwright extension — see `ClientApp/e2e/README.md`). Remaining: a few checklist sections (§6 status, §7 XP/QUIT), `data-testid`s, and CI.

**Testability seams (prerequisite plumbing):**
- [ ] `data-testid` attributes — **deferred**: specs lean on stable semantic classes already present (`.btn-new-game`, `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`). Add testids only where a class proves brittle.
- [x] Expose the `PhaserBridge` `mitt` emitter on `window` behind a flag (`src/testEnv.ts` → `window.__CG_E2E__`) and **record every bridge event** on `window.__cgEvents` for ordering assertions (`PhaserBridge.ts`)
- [x] "Instant animations" flag — under E2E, `timeline` collapses step delays + shortens the animation-complete wait, and `BattleScene` runs tweens/timers at 8× (`this.tweens/time.timeScale`)

**Scaffold:**
- [x] `@playwright/test` + Chromium installed; `playwright.config.ts` → Vite `:5173`, single Chromium, serial (battles are stateful), `webServer` reuses a running Vite; `test:e2e`/`test:e2e:ui` scripts; Vitest scoped to `src/` so the runners don't collide. The E2E flag rides on a `?e2e=1` URL param (`src/testEnv.ts`) so specs import straight from `@playwright/test` (reliable Rider/WebStorm gutter detection); `e2e/helpers.ts` (page objects) + `e2e/README.md`; shared Rider run config `.run/E2E_Playwright.run.xml`
- [x] Repo-root `test.ps1` runs all three suites (.NET / Vitest / Playwright) with a per-suite pass/total + failing-test summary and a CI exit code (`-Dotnet`/`-Web`/`-E2E`/`-StartStack`); documented in `CLAUDE.md`
- [ ] CI step (or `dev.ps1`-adjacent script / `test.ps1 -StartStack`) that boots backend + frontend, runs the suite headless, and tears down

**Specs (mirror `ui_checklist.md` sections):**
- [x] §1–2 Title + Starter selection — title loads (`smoke`), 151-card grid, Gen 1 type badges + BST, level slider range/default, CONFIRM → battle (`starter-select.spec.ts`)
- [x] §3 Battle entry — player/enemy nameplates, "X VS Y" log, FIGHT/CHECK enabled (`battle.spec.ts`)
- [x] §4 Move menu — 2×2 grid + PP, BACK returns (`battle.spec.ts`). *(0-PP greyed/unclickable not yet asserted.)*
- [x] §5 Attack sequencing — bridge ordering (`playMoveAnimation` before `playHitSound`) and the move announced before resolution; **plus the cadence regression guard** (enemy HP doesn't snap to end-of-turn HP at choose-time) in `cadence.spec.ts`
- [ ] §6 Status conditions — badge on correct nameplate; log grammar (not yet — status is non-deterministic per battle; needs a seeded or forced-status path)
- [x] §7 Faint & end (partial) — plays through to faint → winner, asserting order (`battle.spec.ts`). *(XP fill / level-up line / QUIT → title not yet asserted.)*
- [ ] §8 (optional) Visual regression snapshots of the canvas at settled states — still skipped (maintenance cost).

**Notes:**
- Keep Puppeteer-MCP for agent-driven, ad-hoc verification during a session; Playwright is the durable regression layer.
- Audio is verified by asserting the bridge *fired* the sound event, never by capturing sound.
- Deterministic §6 (status) and richer §7 (XP/level/QUIT) coverage would benefit from a **seeded battle** entry point (the `IRandomSource` seam exists in core; wiring a per-game seed through `GameController` would make these specs deterministic).

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

**Prerequisites:** Catch Mechanic, BattleState extraction (Tech Debt ✅ done), `PlayerDbContext` / `save.db`

> **Sequencing:** this whole layer is intentionally **deferred until combat fidelity is fully ironed out** — the battle sim is the foundation the roguelike/lite loop builds on.

- Player starts with one Pokémon; win → new BST-scaled encounter; lose → game over with run summary
- Catch → Pokémon added to party (up to 6); choose lead between battles
- Progressive difficulty: `targetBst = party lead BST + (depth × 10)`; trainer encounters at milestones
- Evolution: player Pokémon evolve at level threshold (requires `PokemonEvolution` table in `pokemon.db`); enemy evolves to correct form for their level before battle
- `PlayerSave` / `SavedCreature` models in `save.db`; auto-save after each battle
- Party management UI between battles
- **Cross-encounter persistence:** carry major status across encounters and revisit the current "reset *all* transient state per battle" behaviour — today HP persists between battles but status doesn't (canonical Gen 1 keeps major status out of battle). The `Creature`/`BattleState` split is the seam for this; see `STATE_MODEL.md §2`.

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
- [x] **Reconnect** — active battles are now keyed by `gameId` (`_active` + `_connToGame`), `SignalRBattleEventEmitter` resolves the current connection per-emit (`Func<string?>`), and a later `OnConnectedAsync` with the same `gameId` rebinds the battle to the new connection (`AttachConnection`). Disconnect no longer cancels immediately: `DetachConnection` arms a 40 s grace timer (covering the JS auto-reconnect policy) that abandons only if no reconnect arrives; a reconnect cancels it. Verified end-to-end via a SignalR client (start on conn1 → drop → reconnect as conn2 → `ChooseMove` on conn2 → resolution events arrive on conn2).

#### 2. Pull `BattleState` extraction forward (was: "when save system is built") `[latent bug source]` ✅ DONE
`Creature` conflated persistent identity (Name, DVs, Exp, base stats), transient battle state, and behaviour. `ResetBattleState()` was a hand-maintained reset list that had to be updated for every new transient field — miss one and state silently leaks between battles (the `StatStages` struct→class bug was exactly this fault).
- [x] Extracted transient fields (`Status`, `SleepTurns`, `ConfusedTurns`, `ToxicCounter`, `Stages`, `IsRecharging`, `IsFlinched`, `HasLeechSeed`, `BindingTurnsRemaining`, `IsTwoTurnCharging`, `ChargingMove`) into `BattleState` (`Creature/BattleState.cs`), held as `Creature.Battle`
- [x] `ResetBattleState()` is now `=> Battle = new BattleState()` — whole-object swap, so a forgotten field is structurally impossible. Locked in by `ResetBattleState_ReplacesWholeBattleState_ClearingEveryTransientField`
- [x] Used **delegating properties** on `Creature` (`Status => Battle.Status`, …) so the ~120 engine/test call sites stay unchanged and behavior is provably identical (136 tests pass). The save split is ready: persist Creature minus `Battle`.
- [ ] Optional future cleanup (cosmetic, no behavior change): migrate call sites to `creature.Battle.X` and drop the delegating facade, so new per-battle fields can *only* be added to `BattleState`. Deferred — not worth the ~120-site churn now.

#### 3. RNG is the one fidelity-critical concern not behind a seam `[consistency]`
Crit, accuracy, speed tie-break, Metronome, and move assignment call `Random.Shared` directly inside the engine. Tests route around it with `AlwaysHitRules`/`AlwaysCritRules`, but for a true Gen 1 clone heading toward roguelike runs, **seeded/replayable RNG** will matter — and it's the natural thing to inject through the same seam pattern used everywhere else.
- [x] Add `IRandomSource` (`Next(int maxExclusive)`, `Next(int min, int max)`, `NextDouble()`) with a `SystemRandomSource` default and a `SeededRandomSource(seed)` for tests/replays (`Combat/IRandomSource.cs`)
- [x] Thread it through the **battle engine** — `Battle`, `AttackAction`, `DamageCalculator`, `StatusResolver`, `Gen1BattleRules` (optional trailing ctor/method params defaulting to `SystemRandomSource.Instance`, so no existing call site broke; interface signatures unchanged so the test doubles compile as-is)
- [x] Seeded determinism proven: `Battle_SameSeed_ProducesIdenticalEventSequence` (135 tests pass)
- [x] **Setup-time RNG in the core library** routed through `IRandomSource` (optional params, default `SystemRandomSource`): `Gen1StatCalculator.RandomiseDvs` (injected source), `EncounterSelector.PickByBst`, `AttackService.GetRandomAttackAsync`/`GiveRandomMoveAsync`. The `creaturegame` library now has **no direct `Random.Shared`**. Seeded reproducibility proven by `Gen1StatCalculator_SeededRandomiseDvs_IsReproducible` and `EncounterSelector_PickByBst_SameSeed_PicksSameSpecies` (138 tests).
- [ ] **`GameController` (web) still uses `Random.Shared`** for enemy level + random move assignment — deliberately deferred: it's the composition root where a per-run seed would be injected, but there's no run-seed concept yet (Game Loop), and the random-move-pick line is slated for replacement by the Learnset System. Wire a run seed here when runs exist.
- [ ] Optional cleanup: the `AlwaysHit/AlwaysCrit` rule shims could be replaced by seeded sources now, but they still read clearly — low priority.

#### 4. Speed tie-break uses RNG as a sort key `[footgun]` ✅ DONE
`Battle.cs` — `.ThenBy(_ => Random.Shared.Next())` called RNG inside the `OrderBy` comparator (ill-defined key; LINQ may invoke the selector multiple times per element).
- [x] Now draws the tie-break once (`int tieBreak = _rng.Next(2)`) and uses it as a stable sort key via the injected `IRandomSource`

#### 5. DbContext via `new()` instead of DI `[maintainability]` ✅ DONE
`GameController` / `SpeciesController` did `new PokemonDbContext()` / `new MovesDbContext()`. Worked only because `OnConfiguring` hardcodes the path. The real costs were lost connection pooling and tests needing real SQLite files. (The background battle loop touches no DB — data is materialised up front and passed in — so the scoped-context-in-`Task.Run` hazard never applied.)
- [x] Registered `AddDbContextFactory<PokemonDbContext>()` / `<MovesDbContext>()` in `Program.cs` (SQLite via `DbPathHelper`); both controllers now inject `IDbContextFactory<T>` and use `CreateDbContextAsync()`
- [x] Verified at runtime: `GET /api/Species` → 151 species, `POST /api/Game/start` → gameId
- n/a `PokemonService` / `AttackService` are not used by the web host (controllers query contexts directly), so there was nothing to register there. If they're adopted later, register them then.

#### 6. Frontend battle-log queue is structurally racy `[design]` ✅ DONE
The imperative `enqueue` / `waitForBridge` / hand-tuned `delay()` choreography in `useBattleHub` coordinating Phaser over the `mitt` bus is where two bugs lived (permanent freeze + listener leak).
- [x] Split into a **pure** `expandEvent(eventType, payload, ctx) → { now, steps }` (`battle/timeline.ts`) that maps each backend event to immediate actions + an ordered list of primitive steps (`dispatch` | `emit` | `wait` | `awaitAnim`), and a small **driver** (`useBattleTimeline`) that plays steps one at a time — the only place with timers/bridge access, retaining the per-step try/catch + `awaitAnim` timeout hardening. `useBattleHub` slimmed to connection + reducer + feeding events to the timeline.
- [x] Sequencing/timing/text is now unit-tested without a browser: `timeline.test.ts` (15 Vitest cases) pins move-name formatting, the immunity line, crit/effectiveness suffixes, two-turn charge text, stat-stage wording, the control-plane-vs-timeline split, and `MoveUsed`/`TurnStarted` ordering (the cadence guards).
- [x] Playwright E2E landed — 9 specs via the `?e2e=1` seam (smoke / starter-select / battle / cadence); see the **Browser-Based UI Testing** section above for the remaining checklist gaps (CI, §6 status, §7 XP/QUIT, data-testids).
- [x] Full-flow parity verified live this session — Puppeteer `ui_checklist` run + the Playwright faint→winner play-through; cadence confirmed.

#### 7. Architecture / decision-log doc `[docs — after the above]`
The doc set is strong, but the *why* behind the two-DB split, event sourcing, and the seam invariants lives only implicitly. For a project explicitly built to extend generation-by-generation, capture these as an `ARCHITECTURE.md` (or lightweight ADR log) so the invariants survive future drift.
- [ ] Document: two-DB rationale, event-sourced engine + emitter pattern, the three seams (`ITypeChart`/`IBattleRules`/`IStatCalculator`) and the "never branch on generation" rule, the web session/SignalR flow, and the import-vs-runtime data boundary
- [ ] Cross-link from `CLAUDE.md` Key Files table

### Known Gaps
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector exists in the UI
- Enemy Pokémon do not evolve — wire into level-up system when Game Loop is built
- `GameController.BuildCreature` uses random moves — fixed by Learnset System

### Fixed ✅
- Attack cadence (Gen 1 feel): the lunge + target flash played **before** the "X used MOVE!" line, and the HP bar snapped to its end-of-turn value the instant a move was chosen (the next turn's `TurnStarted` was applied immediately). Fixed by announcing the move first then animating (`MoveUsed` expansion in `timeline.ts`) and routing `TurnStarted` **through the timeline** so HP/status sync only after the turn's damage animates — bars drain in step now. Verified live (Puppeteer) + locked by the `MoveUsed`/`TurnStarted` Vitest cases and `cadence.spec.ts`.
- Gen 1 physical/special split miscategorised 18 of 110 damaging moves: the importer copied PokeAPI's per-move `damage_class` (the Gen 4+ split), but Gen 1 decides physical/special by the move's **type**. So Hyper Beam/Gust/Acid/Sludge/etc. used Special and Fire/Ice/Thunder Punch, Waterfall, Crabhammer, Vine Whip, Razor Leaf used Attack — computing damage off the wrong stat. Fixed in `MoveImport.MapToAttack` (now derives `AttackType` from `DamageType` via `Gen1DamageCategory`) and the existing `moves.db` rows were corrected in place by the same rule (verified 0 mismatches). See `DATA_IMPORT.md` §4.1/§6.
- Battle log froze on faint (stuck on last damage line, no "fainted!"/winner): `BattleScene`'s `destroy()` was dead code (Phaser never calls it), so `bridge.on` listeners leaked across canvas remounts (HMR/StrictMode) and a stale scene's `playFaintAnimation` threw on a destroyed sprite — now removed via `SHUTDOWN`/`DESTROY` scene events (`teardown`). Hardened the queue too: `drainQueue` try/catch-continues per task with a `finally` reset, and `waitForBridge` times out after 3 s so a lost `animationComplete` can't hang the log.
- Battle-log text polish: move names display formatted (`fury-attack` → `FURY ATTACK`) via `utils/format.ts#formatMoveName`, applied to the log (`MoveUsed`/`MoveMissed`/`BindingStarted`) and the move-menu grid; Gen 1 per-move two-turn charge lines (`chargingMsg`: Dig "dug a hole!", Fly "flew up high!", Solar Beam "took in sunlight!", etc.) replace the generic "is charging up X!"; immunity now reads "It doesn't affect X..." with no damage number/crit and no hit sound (was "took 0 damage! It had no effect.")
- Metronome (`MoveEffect.Metronome`): picks a random eligible Gen 1 move and executes it in full; move pool threaded from `GameController` → `GameSessionManager` → `Battle` → `AttackAction`; DB updated via re-run of importer
