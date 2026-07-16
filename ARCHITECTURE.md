# ARCHITECTURE.md

> **Status: DRAFT (2026-06-12).** Section 1 + the §2 decision entries are first-pass — we're filling /
> double-checking each together. The `Why` lines still being confirmed are marked `<!-- confirm -->`.

This is the **decision log** for the battle engine: the *why* behind the structure, and the index that
routes to the specialized reference docs for the *what*. It deliberately does **not** restate them — when a
section bottoms out in mechanics, state, import mapping, or the seam contract, it links out (see §4).

---

## 1. System map

```
  IMPORT (one-shot)
  -----------------
    pokeapi.co --> PokeApiConnector --> moves.db / pokemon.db / items.db
                   (Gen-1 corrections applied here)

  RUNTIME
  -------
    moves.db  --(EF Core)--+
    pokemon.db --(EF Core)-+--> EncounterFactory --> builds Creatures
    items.db  --(EF Core)--+    (items.db: ItemService → the player's Bag)

    +------------------- creaturegame . core engine -------------------+
    |  BattleRunner --> Battle --> AttackAction                        |
    |       |                                                          |
    |       +-- delegates gen-variable rules --> Seams:                |
    |       |        IBattleRules . ITypeChart . IStatCalculator       |
    |       v                                                          |
    |  emits BattleEvent   (engine never does IO)                      |
    +--------------------------------+---------------------------------+
                                     | IBattleEventEmitter
                                     v
                        SignalRBattleEventEmitter   (event -> JSON)
                                     |
                                     v   SignalR  /hubs/battle
    +--------------- creaturegame.Web . React frontend ----------------+
    |  useBattleHub --> expandEvent   (pure: event -> actions + steps) |
    |                        |                                         |
    |            +-----------+-----------+                             |
    |            v                       v                             |
    |   reducer -> React DOM     timeline driver -> Phaser canvas      |
    +-----------------------------------------------------------------+
```

*(Input seams — `SignalRInput` for the player, `RandomMoveInput` for the enemy — and the test-only
`ConsoleBattleEventEmitter` are omitted from the map for readability; see §2.4 and §2.2.)*

**Four projects** (see `CLAUDE.md` → Architecture for the one-liners): `creaturegame` (core engine, no entry
point), `creaturegame.Web` (ASP.NET host + React/Phaser frontend), `PokeApiConnector` (one-shot importer),
`tests/creaturegame.Tests` (xUnit).

---

## 2. Decision log

Each entry: **Decision · Why · Where it lives.**

### 2.1 The three seams + "never branch on generation"
- **Decision:** every generation-variable rule is delegated to a strategy interface —
  `IBattleRules` (stat-stage tables, crit/accuracy formulas, status rates, durations…),
  `ITypeChart` (effectiveness matrix), `IStatCalculator` (stat formulas). Engine code **never** contains a
  `if (generation == 1)` branch; it asks the seam.
- **Why:** Gen 2+ becomes a new implementation, not a rewrite; the litmus test ("would Gen 2 change this
  number?") decides whether a value goes on the seam or stays inline.
- **Where:** `Combat/IBattleRules.cs` (+ `Gen1BattleRules`), `Combat/ITypeChart.cs` (+ `Gen1TypeChart`),
  `Creatures/IStatCalculator.cs` (+ `Gen1StatCalculator`). **Full contract + §5.0 checklist → `GENERATION_SEAMS.md`.**

### 2.2 Event-sourced engine + emitter pattern
- **Decision:** the engine computes outcomes and **emits `BattleEvent` records**; it never writes to a
  console, socket, or UI. Output is the job of an `IBattleEventEmitter` implementation.
- **Why:** keeps the engine pure and testable (assert on the event stream, no IO), and lets the same battle
  drive multiple sinks — a SignalR payload for the web, text for a console, animation steps for the frontend.
- **The 3-way fan-out:** every event is hand-mapped in `SignalRBattleEventEmitter.MapEvent` (→ payload),
  `ConsoleBattleEventEmitter` (→ text, a `CG_BATTLE_LOG`-gated debug narrator), and frontend
  `timeline.ts expandEvent` (→ text + steps). All three legs are now guarded by `WebEventContractTests`: the
  SignalR leg by `EveryBattleEventMapsToItsOwnNamedClientEvent` (nothing maps to `"Unknown"`), and the TS leg
  by `EveryBattleEventHasATimelineArm` (every event has a `case` in `expandEvent`, so none falls through
  `default: {}`).
- **Where:** `Combat/BattleEvents.cs`, `Combat/IBattleEventEmitter.cs`, `creaturegame.Web/Battle/SignalRBattleEventEmitter.cs`,
  `ClientApp/src/battle/timeline.ts`.

### 2.3 Run layer vs battle layer (`BattleRunner` orchestration)
- **Decision:** a single `Battle` is one fight; the roguelite **run** is orchestrated above it by
  `BattleRunner` — the endless chain (persistent player, a fresh enemy built per encounter via an injected
  supplier), the Poké Center recovery node, cross-encounter status carry-over, and the single `RunEnded`
  summary. `Battle` itself knows nothing of "runs."
- **Why:** run-layer choices — full heal, wild level band — are generation-invariant, so they live here and
  are **not** behind an `IBattleRules` seam; the enemy supplier is a delegate so the DB/encounter-building
  concern stays in the web layer and core stays data-agnostic. <!-- confirm -->
- **Where:** `Combat/BattleRunner.cs`; enemies supplied by `creaturegame.Web/Battle/EncounterFactory.cs`.
  **Run loop / event model → `GAME_LOOP.md`.**

### 2.4 Turn-action & input seams (`IBattleAction` + `IBattleInput`)
- **Decision:** a turn is an `IBattleAction` (`Priority` + `ExecuteAsync`) — `AttackAction` (a move/Struggle)
  and `ItemAction` (use a bag item), with switch / catch the future plug-ins. *What a combatant decides* is an
  `IBattleInput`: `ChooseMoveAsync` picks a move, and the additive `ChooseTurnActionAsync` returns a
  `TurnChoice` (FIGHT `MoveTurnChoice` / ITEM `ItemTurnChoice`). Its default just delegates to
  `ChooseMoveAsync`, so `AutoSelectInput` / `RandomMoveInput` / the AI never need to know about items — only
  the web's `SignalRInput` offers the bag.
- **Why:** decouples "what the combatant decides" from "how the turn executes," so new action types and new
  deciders drop in without touching `Battle`'s turn loop. The additive default-interface-method keeps every
  non-interactive input untouched when a player-only choice (item, recovery, evolution) is added.
- **Item turn specifics (Gen 1):** an item **resolves before any move** — `ItemAction.Priority` sits above
  the move range, so it sorts to the front of the same priority queue. Lock-in/Struggle take precedence over
  the bag (a locked-in creature can't open the menu), and the per-action `CanAct`/dead-target guards apply
  only to `AttackAction`, so an item can be used while asleep/paralyzed (using it *is* the turn). The bag
  itself is a **transient `Bag`** (item-id → qty) — not persisted yet (save layer deferred with Catch).
- **Where:** `Combat/IBattleAction.cs` + `Combat/AttackAction.cs` + `Combat/ItemAction.cs`,
  `Combat/IBattleInput.cs` (the `TurnChoice` seam, + `AutoSelectInput` / `RandomMoveInput`), `Items/Bag.cs`,
  `creaturegame.Web/Battle/SignalRInput.cs`. **Item effects → §2.11.**

### 2.5 Database-per-domain split (`pokemon.db` + `moves.db` + `items.db`)
- **Decision:** data is partitioned by **overarching domain object** — Pokémon-world data in `pokemon.db`,
  move-world data in `moves.db`, item-world data in `items.db` — each with its own `DbContext`
  (`PokemonDbContext` / `MovesDbContext` / `ItemsDbContext`), service (`PokemonService` / `AttackService` /
  `ItemService`), and migration folder.
- **Why:** the split follows the natural object boundaries of the domain and is **extensible** — items proved
  the pattern: a new unique object domain gets its own database + context rather than being bolted onto an
  existing one, so each domain versions and imports independently. (Player save state will likewise be its own
  `save.db` / `PlayerDbContext` when it lands.)
- **Where:** `DB/MovesDbContext.cs`, `DB/PokemonDbContext.cs`, `DB/ItemsDbContext.cs` (one file per context),
  `DB/Migrations/{Moves,Pokemon,Items}`.

### 2.6 Import-vs-runtime boundary (Gen-1 corrections happen at import)
- **Decision:** PokeAPI returns *modern* move data; all Gen-1 corrections (power/accuracy/type from
  `past_values`, the type-derived physical/special split, the hand-curated layer-2 secondary-effect fixes) are
  applied **once, at import**, in `MapToAttack`. The runtime engine trusts the DB.
- **Why:** the data layer answers "what are this move's Gen-1 numbers"; the seam answers "how does the engine
  apply them." Keeping corrections out of the engine preserves the gen-agnostic rule (2.1).
- **Where:** `PokeApiConnector/PokeAPI/MoveImport.cs`. **Full mapping → `DATA_IMPORT.md` §4.1/§5.5.**

### 2.7 Web session lifecycle (SignalR + reconnect grace + blocking input)
- **Decision:** a run is a server-side `BattleRunner` task per `gameId`, owned by `GameSessionManager`. Player
  choices arrive as SignalR invocations that unblock a `SignalRInput` (TCS-based); the emitter resolves the
  *current* connection per-event so output follows a reconnect; a dropped connection starts a grace timer
  before the run is abandoned.
- **Why:** turn-based play is naturally "engine blocks awaiting input"; a per-event connection lookup +
  reconnect grace survives a transient network drop without losing the run.
- **Where:** `creaturegame.Web/Battle/GameSessionManager.cs`, `SignalRInput.cs`, `Hubs/BattleHub.cs`.
  **Run loop / event model → `GAME_LOOP.md`.**

### 2.8 Frontend animation timeline (pure expand + driver, Phaser/React isolation)
- **Decision:** backend events become UI in two stages — a **pure** `expandEvent` maps each event to immediate
  `now` actions + an ordered list of `steps`, and a small queue-draining driver (`useBattleTimeline`) plays the
  steps, owning *all* timers and the Phaser bridge. View state is a `useReducer`; canvas rendering is isolated
  behind a `mitt` event bridge (tests assert the bridge *fired*, never pixels).
- **Why:** keeping sequencing / timing / text in a pure function makes the battle-log cadence unit-testable
  with no browser or wall clock, and confining timers + bridge to the driver removes the async-closure tangle.
  Events that would race the animation (`TurnStarted`, a chained `BattleStarted`) are *queued*, not applied at
  once, so the HP bars drain in step with the animation. <!-- confirm -->
- **Where:** `ClientApp/src/battle/timeline.ts` (expandEvent + driver), `battle/PhaserBridge.ts` (mitt),
  `hooks/useBattleHub.ts` (reducer). **Frontend intent → `FRONTEND_PLAN.md`.**

### 2.9 `BattleState` with no facade (forgotten-reset made impossible)
- **Decision:** all transient per-battle state lives on `Creature.Battle` (`BattleState`), reset by assigning
  a fresh instance. There is **deliberately no delegating property** on `Creature` for these fields.
- **Why:** a new per-battle field *must* be added to `BattleState`, so it is automatically covered by the
  wholesale reset — a forgotten reset becomes structurally impossible, not just discipline.
- **Where:** `Creatures/BattleState.cs`, `Creature.ResetBattleState()`. **Full split → `STATE_MODEL.md`.**

### 2.10 Determinism / the RNG seam
- **Decision:** all randomness goes through `IRandomSource`; the engine and battle setup are seedable, and
  `BattleScenario.Seed(...)` makes every roll deterministic in tests.
- **Web run seed (done 2026-06-17):** the composition root now seeds each run end-to-end. `GameController.Start`
  picks one seed per run (client may supply `StartGameRequest.Seed`; else a random int, logged + returned as
  `{ gameId, seed }`) and threads a single `SeededRandomSource` through the whole run — player + every enemy's
  construction (`EncounterFactory` seeds `Gen1StatCalculator` for DVs and passes the source to move selection /
  `PickByBst` / `ScaleWildLevel`), the battle (`BattleRunner`), and the AI (`Gen1TrainerAi`). One shared stream
  is safe because a run is single-threaded. This unblocks the deferred recovery/replace-move E2E specs (pass a
  fixed `seed`). Guarded by `RunSeedReproducibilityTests`.
- **Where:** `Combat/IRandomSource.cs`, `Web/Controllers/GameController.cs`, `Web/Battle/EncounterFactory.cs`,
  `tests/.../TestSupport/BattleScenario.cs`.

### 2.11 Effect strategies (lock-ins + post-damage move effects + item effects)
- **Decision:** three parallel registries follow the same shape — each resolved by a `For(key)` lookup, none
  branched inline. Multi-turn "lock-in" moves (two-turn, rampage, rage, bide, binding) are each an
  `ILockInMechanic` via `LockInMechanics.For(effect)`. The ~20 post-damage move effects (Haze, Counter,
  Reflect, Transform, Rest, Substitute…) are each an `IMoveEffect` via `MoveEffects.For(effect)`. The in-battle
  **item effects** (Heal, StatusCure, PpRestore, X-item) are each an `IItemEffect` via
  `ItemEffects.For(category)`, keyed by `ItemCategory`.
- **Why:** lock-ins differ sharply turn-to-turn (charge / store / strike); post-damage effects were a
  ~320-line switch that concentrated change-risk. Owning their own hooks keeps the main pipeline linear and
  each effect independently testable. Damage-dealing move effects (Counter) reach `AttackAction`'s centralized
  `DealDamageToTarget` through a context delegate, so the Substitute-soak / Bide / Counter-recording stays in
  one place. **Item effects** mirror the same shape: `CanApply` gates the announce + bag-consume (the Gen 1
  "won't have any effect" rule), `Apply` mutates state and emits events; their *amounts* are **data** read off
  the `Item` row (never inlined), and deferred categories (Revive needs a party, Ball needs Catch) are simply
  absent from the registry so `For` returns null.
- **Where:** `Combat/LockInMechanics.cs`, `Combat/MoveEffects.cs`, `Combat/ItemEffects.cs`.

---

## 3. How to extend

One-liners for orientation — the full procedure for each lives in the linked doc.

- **Add a damaging/secondary move:** usually pure data — confirm `MapToAttack` resolves its Gen-1 values (add
  a layer-2 correction only if PokeAPI can't express it). → `DATA_IMPORT.md`.
- **Add a move with new behavior:** add a `MoveEffect`, implement it as an `IMoveEffect` (a class in
  `MoveEffects.All`), emit a `BattleEvent`, map it in all three emitter legs, add a contract test.
- **Add an item effect:** implement an `IItemEffect` (a class in `ItemEffects.All`) keyed by `ItemCategory`;
  read amounts off the `Item` row (never inline); gate use with `CanApply`; emit a `BattleEvent` and map it in
  all three emitter legs (the `WebEventContractTests` guard enforces this even before the bag UI exists).
- **Add a generation:** implement new `IBattleRules` / `ITypeChart` / `IStatCalculator`; never branch on
  generation in engine code; run the §5.0 gen-agnostic checklist. → `GENERATION_SEAMS.md`.
- **Add an AI / alternative input:** implement `IBattleInput` (score moves via `IMoveEvaluator`) and wire it
  as the enemy or player input — no engine change. → TODO "AI Move Selection".

---

## 4. Testing architecture

How the suites are shaped (the *why* behind the test layout, not a how-to-run — that's `CLAUDE.md`).

- **Contract tests, one file per capability.** `Integration/Gen1Attacks/*` (`DamageContractTests`,
  `CounterContractTests`, `SubstituteContractTests`…) and `Integration/Interactions/*` assert Gen-1 behavior
  through the **real** engine. Tests are grouped by *capability*, not by the batch that introduced them.
- **Tests drive real engine code; never mock the unit under test.** `BattleScenario` / `MoveScenario` build
  real `Creature`s + moves and run a real `Battle`; assertions read the emitted event stream
  (`RecordingEmitter`), not internals.
- **Deterministic rules.** `DelegatingBattleRules` / `ScriptableRules` wrap a *seedable* `Gen1BattleRules` so
  a test can force a crit / hit / specific roll, and `BattleScenario.Seed(...)` makes a whole battle
  reproducible (see §2.10).
- **Frontend.** Vitest unit-tests the pure timeline (`timeline.test.ts`); Playwright `e2e/*` drives the React
  DOM and asserts Phaser via the bridge (never pixels, never wall-clock durations).
- **Unit suite** is capability-grouped under `Unit/` (`StatCalculationTests`, `DamageCalculationTests`,
  `StatusConditionTests`, `MoveExecutionTests`, `EffectRegistryTests`, …) — matching the Integration layout
  (the old ~3100-line `CoreMechanicsTests` monolith was split per Architecture Review #7).

---

## 5. Documentation index

Every doc in the repo and what it answers, in a line. This file (`ARCHITECTURE.md`) is the entry point for
*why the system is shaped this way*; the rest go deeper on a single concern.

| Doc | What it is |
|:----|:-----------|
| `README.md` | Project overview / getting started (repo root). |
| `CLAUDE.md` | Always-on primer: setup, commands, architecture overview, the read-on-demand trigger table (repo root). |
| `ARCHITECTURE.md` | **This file** — the decision log (the *why*) + system map (repo root). |
| `docs/GENERATION_SEAMS.md` | The seam contract + the §5.0 gen-agnostic definition-of-done checklist (the real gate). |
| `docs/STATE_MODEL.md` | The `Creature` permanent/transient split (`BattleState`). |
| `docs/GAME_LOOP.md` | The run/roguelite loop ↔ event model (battle & heal as events). |
| `docs/ENCOUNTER_DESIGN.md` | The biome-graph run model, enemy strength tiers, themed pool, gated acquisition channels. |
| `docs/DATA_IMPORT.md` | The PokeAPI import pipeline + the import-vs-runtime boundary and field mapping. |
| `docs/DESIGN_GUIDES.md` | Design reference: Gen-1 mechanics, type-balancing, move-import mapping. |
| `docs/DEFINITION_OF_READY.md` | DoR — the exit criteria for a `/plan` pass (a plan isn't done until every item is covered). |
| `docs/DEFINITION_OF_DONE.md` | DoD (technical) — the rubric the `pr-review` subagent checks at feature-finish. |
| `docs/DEV_STANDARDS.md` | .NET / EF coding conventions and architecture rules. |
| `docs/GEN_DIFFERENCES.md` | Gen-1 mechanic requirements — lookup reference, consulted loosely (the seam check gates). |
| `docs/GAME_AVAILABILITY.md` | Game-version availability requirements — lookup reference, consulted loosely. |
| `docs/FRONTEND_PLAN.md` | Frontend plan: React + SignalR + Phaser structure and intent. |
| `docs/TODO.md` | The authoritative active task list (finished work → `docs/TODO_ARCHIVE.md`). |
| `docs/TODO_ARCHIVE.md` | History of completed tasks, batches, and resolved tech debt. |
| `.claude/AI_CONTEXT.md` | Agent profiles (`/plan` `/dev` …) + the tooling/automation reference. |
