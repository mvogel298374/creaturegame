# Battle Sim – TODO List

> **Active tasks only.** Completed work (batches 1–17, done tech-debt, fixed bugs) lives in
> [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only if you need the history of a finished item.
> **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) · `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions)

**Current state (2026-06-12):** Move-coverage pass, integration-test pass, the `BattleState` facade
migration, the whole "XP & progression" milestone, **and the Learnset System (Level-up move learning)**
are all COMPLETE and archived in `TODO_ARCHIVE.md` — XP & Level-Up fidelity, the Endless Battle Chain, and
now level-up move learning (auto-learn on a free slot; blocking replace-move prompt + confirm modal when the
four slots are full; learned moves persist across the chain). **Also done (2026-06-11): the Roguelite Run
Layer — an interactive Poké Center recovery (offer/HEAL/SKIP modal) every 3rd win + a 50–80%-of-player wild
level band** (see the section below). Suite: 867 .NET + 48 Vitest + 17 Playwright E2E. **Logged 2026-06-12: a whole-repo
architecture/code-smell review → `ARCHITECTURE.md` (immediate next task) + Architecture Review #7 (see Tech
Debt). Next feature after that: AI Move Selection** (now unblocked — Learnset System was its prerequisite). **Resolved 2026-06-12:** the RNG seam is
now seedable end-to-end — `BattleScenario.Seed(...)` makes every roll deterministic (`SeededRulesTests`) — and
the deferred **double-faint-as-loss** test is landed (`BattleRunnerTests`). Remaining from the chain: only the
*production* per-run seed at the web composition root (Tech Debt #3); the replace-move/recovery **modal** E2Es
stay deferred until that web seed exists (the .NET coverage is already complete).

---

## AI Move Selection — NEXT UP

**Prerequisite:** Learnset System (so AI evaluates moves the Pokémon can actually learn).

`IBattleInput` is the seam. AI scores available moves via `IMoveEvaluator` and picks using a selection
strategy. (`RandomMoveInput` ✅ already wired as the default enemy input — see archive. The evaluator-driven
tiers below are pending.)

**Evaluator dimensions:** expected damage (power × type eff × STAB × stat ratio); type-effectiveness bonus;
stat-stage move value; priority move value; status move value; PP conservation.

**Selection strategies:** `RandomMoveInput` (wild/lowest tier ✅); `WeightedAIInput` (probabilistic);
`GreedyAIInput` (always best — boss tier); `CompositeEvaluator` (weighted sum; trainer "personality").

**Tasks:**
- [ ] `DamageEvaluator : IMoveEvaluator`
- [ ] `TypeEffectivenessEvaluator : IMoveEvaluator`
- [ ] `StatStageMoveEvaluator : IMoveEvaluator`
- [ ] `StatusMoveEvaluator : IMoveEvaluator`
- [ ] `CompositeEvaluator : IMoveEvaluator`
- [ ] `GreedyAIInput : IBattleInput`
- [ ] `WeightedAIInput : IBattleInput`

---

## EV Gain (Effort Values)

No prerequisites. All `ExpHP/Attack/Defense/Special/Speed` fields exist on `Creature` but are never written.

- [ ] After awarding XP in `Battle.StartFightAsync`, add fainted enemy's base stats to player's corresponding
  `Exp*` fields; cap each at 65535 (Gen 1 has no per-stat cap); call `CalculateStats()` immediately
- [ ] No new battle event required (Gen 1 is silent about EVs)

---

## Web UI — Polish

Stack: React 18 + TypeScript + SignalR + Phaser 3. (Phaser canvas & core animations ✅ done — see archive.)

- [ ] `BattleEndedOverlay` — **superseded by the Endless Battle Chain's `RunEnded` game-over screen** (a
  per-`BattleEnded` overlay no longer fits an endless chain); build it there, run-scoped, not per battle
- [x] Level-up notification toast — Gen 1 stat-gain panel (HP/ATTACK/DEFENSE/SPECIAL/SPEED with +gains and
  new totals) on `LeveledUp`. Engine sends per-stat `StatGains` (before/after `TryLevelUp` delta). **Polish
  (2026-06-10):** plays the level-up fanfare (`playLevelUpSound` bridge → `Audio.playLevelUp`), and the
  panel no longer auto-hides — it sits bottom-right above the battle menu and stays until the player's next
  input (`useBattleHub.dismissLevelUp`).
- [ ] Move menu STAB indicator — subtle highlight on moves matching player's type
- [ ] Color-coded effectiveness in battle log (super-effective green, not very effective grey, no effect red)
- [ ] Sprite shake tween on damage received
- [ ] `ConsoleInput : IBattleInput` — numbered move menu for terminal play (low priority)

---

## Browser-Based UI Testing (Playwright)

Promote the manual Puppeteer checklist (`ui_checklist.md`) into a committed, CI-runnable E2E suite.
Playwright drives the **React DOM** (≈70% of the checklist); the **Phaser canvas** is tested through the
existing `mitt` bridge, not by inspecting pixels.

**Key constraint:** Playwright/Puppeteer query the DOM only. Phaser renders to one opaque `<canvas>` — sprite
slide-in, idle bob, lunge, faint fade, and audio are **not** directly assertable. Don't attempt pixel/sprite
selectors, and never assert wall-clock animation durations (the #1 source of flake). Assert **event ordering**
via the bridge instead; unit-test durations separately if needed.

Status: **suite landed** (16 specs across 8 files, run via `npm run test:e2e` or the VS Code Playwright
extension — see `ClientApp/e2e/README.md`). §6 status, §7 XP/level-up/QUIT, and the endless chain are now
covered. Remaining: `data-testid`s and CI.

**Remaining:**
- [ ] `data-testid` attributes — **deferred**: specs lean on stable semantic classes already present
  (`.btn-new-game`, `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`). Add testids
  only where a class proves brittle.
- [ ] CI step (or `dev.ps1`-adjacent script / `test.ps1 -StartStack`) that boots backend + frontend, runs
  the suite headless, and tears down.
- [x] §6 Status conditions — `status.spec.ts`: player-inflicted Sleep Powder → sleep badge on the enemy
  nameplate + "fell asleep!" (player move + retry-until-lands; enemy-inflicted / immunity edges stay at the
  integration layer). (2026-06-10.)
- [x] §7 Faint & end — XP fill + level-up panel (`level-up.spec.ts`); run-over / game-over + QUIT → title
  (`endless-chain.spec.ts`). (2026-06-10.)
- [ ] §8 (optional) Visual regression snapshots of the canvas at settled states — skipped (maintenance cost).

**Notes:** keep Puppeteer-MCP for agent-driven ad-hoc verification; Playwright is the durable regression
layer. Audio is verified by asserting the bridge *fired* the sound event. Deterministic §6/§7 coverage would
benefit from a **seeded battle** entry point (the `IRandomSource` seam exists in core; wiring a per-game seed
through `GameController` would make these specs deterministic).

---

## Catch Mechanic

Deferred until Phaser animations exist — the mechanic needs a throw/shake/catch animation sequence.

**When ready:**
- [ ] Bag action in move menu; `Battle` extended with a "catching" state
- [ ] Gen 1 capture formula: `floor((MaxHP × 3 − HP × 2) × CatchRate / (MaxHP × 3))` vs. 0–255 roll
- [ ] `PokemonSpecies.CatchRate` already imported ✓
- [ ] `CaptureAttempted(string TargetName, bool Caught)` battle event
- [ ] `BattleEnded` variant: `reason: "Caught"`

---

## Roguelite Run Layer — Recovery & Encounter Scaling ✅ (2026-06-11)

Two run-layer features on top of the Endless Battle Chain. Both are **run/game-loop concerns, not battle
mechanics**, so they stay in the run orchestrator (`BattleRunner`) / web encounter builder (`EncounterFactory`)
and are *not* behind an `IBattleRules` seam — `/audit` §5.0 clears them (no new engine magic numbers, no gen
checks, full heal + level band are generation-invariant choices).

- [x] **Poké Center recovery every 3rd win — an interactive game-loop step.** After every 3rd chained win the
  player is *offered* a full restore before the next encounter; it's its own blocking node in the loop, not a
  silent auto-heal. `Creature.FullHeal()` does the restore (HP→max, all PP→max, major status cleared, Toxic
  counter reset) — matches the Gen 1 Poké Center exactly (HP + PP + status, unconditional/free), identical in
  every generation, so it's ordinary engine logic, not a seam. Interval is `BattleRunner.healEveryNBattles`
  (default 3, 0 disables).
  - **Blocking choice** reuses the move-replacement plumbing: `IBattleInput.ConfirmRecoveryAsync` (default
    accepts, so AI/headless never block) ↔ hub `RespondRecovery` ↔ `SignalRInput` TCS. `BattleRunner` emits
    `RecoveryOffered(name, speciesId, battlesWon)` then awaits the choice; on accept → `FullHeal` +
    `PlayerRecovered`, on skip → `RecoveryDeclined` (status still carries). All three events mapped in both
    emitters + `timeline.ts`.
  - **UI:** in-page `RecoveryModal` (BattleScreen) shows the player's creature sprite with a CSS heal-glow and a
    single **HEAL / SKIP** press that both decides and advances the chain. Verified live (Puppeteer): offer →
    modal blocks → HEAL → "was fully healed!" → next battle; and the SKIP path → "decided to keep going!".
  - Tests: `BattleRunnerTests` (heals once after win 3 restoring HP/PP/status; **declining** leaves the player
    wounded/poisoned), `CoreMechanicsTests.FullHeal_*`, auto-covering `WebEventContractTests`, `timeline.test.ts`
    (offer/heal/decline). **Deferred:** a recovery-modal **E2E** spec (needs the seeded-battle entry point to
    reach 3 wins deterministically — same reason the replace-move modal E2E is deferred).
- [x] **Wild level band 50–80% of player level.** `EncounterFactory.ScaleWildLevel` replaces the old
  `playerLevel ± 3` with a uniform pick in `[floor(0.5·L), floor(0.8·L)]`, floored at 2 — wild foes sit a step
  below the player so the chain stays winnable while still scaling. Tests: `EncounterLevelBandTests` (band
  bounds across levels, both ends reachable, never < 2).

---

## Game Loop & Progression

**Prerequisites:** Catch Mechanic, BattleState extraction (✅ done), `PlayerDbContext` / `save.db`

> **Sequencing:** this whole layer is intentionally **deferred until combat fidelity is fully ironed out** —
> the battle sim is the foundation the roguelike/lite loop builds on. The **Endless Battle Chain** (above) is
> the first minimal slice of this layer (persistent single creature, endless wild encounters); the items
> here are everything it deliberately leaves out (catch, party, save, evolution, difficulty curve).

- Player starts with one Pokémon; win → new BST-scaled encounter; lose → game over with run summary
- Catch → Pokémon added to party (up to 6); choose lead between battles
- Progressive difficulty: `targetBst = party lead BST + (depth × 10)`; trainer encounters at milestones
- Evolution: player Pokémon evolve at level threshold (requires `PokemonEvolution` table in `pokemon.db`);
  enemy evolves to correct form for their level before battle
- `PlayerSave` / `SavedCreature` models in `save.db`; auto-save after each battle
- Party management UI between battles
- **Cross-encounter persistence:** ✅ major status now carries across encounters in the Endless Battle Chain
  (2026-06-10) — `BattleRunner` snapshots the player's status after each win and re-applies it into the next
  `Battle` (via `playerEntryStatus`), with `IBattleRules.CarryStatusOutOfBattle` deciding the out-of-battle
  transform (Gen 1: Toxic→Poison). Volatiles (confusion, stages) still reset per battle — canonical. HP/PP
  already persisted. (Sleep carries its counter; Freeze persists.) Remaining: only matters again when
  switching/party exists. See `STATE_MODEL.md §2`.

---

## Multi-Generation: Data Model & Schema

Deferred to the Gen 2 sprint. (The stat-selection abstraction — the only piece to do now — is ✅ done.)

**`Attributes` stat split:**
- [ ] `Attributes.Special` → `Attributes.SpAtk` + `Attributes.SpDef`; keep `Special` as a computed alias for
  Gen 1 (`SpAtk`, since they're equal) so existing tests migrate cleanly
- [ ] `Creature.BaseSpecial`, `DvSpecial`, `ExpSpecial` split in parallel

**`PokemonSpecies` per-generation schema:**
- [ ] Separate timeless identity (`Id`, `Name`, `CatchRate`, `BaseExperience`, `PokedexEntry`, `GrowthRate`)
  from generation-specific data
- [ ] New `PokemonSpeciesGenData` table: `SpeciesId`, `Generation` (int), `Type1`, `Type2`, `BaseHP`,
  `BaseAttack`, `BaseDefense`, `BaseSpAtk`, `BaseSpDef`, `BaseSpeed`; Gen 3+ adds `Ability1/2/Hidden`
- [ ] Importer stores one row per species per generation; engine queries by active generation
- [ ] **Note:** PokeAPI has no `past_stats` equivalent — Gen 1 stat corrections (e.g. Clefable, Beedrill,
  Pikachu line buffed in Gen 6) will need a corrections table or separate data source

**Move per-generation data (intention — see `DATA_IMPORT.md` §4.1/§5.5):**
- Today the importer resolves each move's **Gen 1** values from PokeAPI `past_values` by taking the *earliest*
  recorded entry. Going multi-gen is a **generalisation, not a rewrite**: resolve a field for target generation
  *G* as the value of the earliest `past_values` entry whose `version_group` generation is **> G**, else the
  current value. "Earliest = Gen 1" is just the *G = 1* case.
- [ ] When moves go per-generation, either store one `Attack` row per `(moveId, generation)` (mirror the
  **learnset model** — a `Generation` column + an `ActiveGeneration` filter) **or** resolve on demand. Prefer
  the stored-per-gen row for query simplicity and parity with `PokemonSpeciesGenData`.
- [ ] Make the **layer-2 override table per-generation** too (e.g. Acid's stat target/chance differs Gen 1 vs
  Gen 4+). The override key becomes `(moveName, generation)`.
- [ ] Keep mechanic/formula differences on the **seams** (`IBattleRules` et al.), never in the per-gen move
  data — the data layer answers "what are this move's numbers in gen G," the seam answers "how does the engine
  apply them in gen G."

**Generation filtering:**
- [ ] `Attack.GenerationIntroduced` (int) + `PokemonSpecies.GenerationIntroduced` (int) — set on import
- [ ] `EncounterSelector.PickByBst` and `GameController.BuildCreature` filter by `GenerationIntroduced <= activeGeneration`
- [ ] `PokemonService.GetSpeciesForGenerationAsync(int)` + `AttackService.GetMovesForGenerationAsync(int)`
  replace unfiltered `ToListAsync()` calls

---

## User Documentation

Target: after AI Move Selection lands — at that point battles are fully playable and docs won't describe a
moving target.

- [ ] `/help` route or modal — starter selection, battle controls, status icons, level picker
- [ ] Expand `README.md` — architecture decisions (two-DB model, `IBattleRules` pattern, how to add a move
  effect, how to add a generation)
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

---

## Tech Debt / Cleanup (open items)

> Done items (Architecture Review #1/#2/#4/#5/#6, the #6a lock-in abstraction, the `BattleState` facade
> migration, flaky-test sweep, struct→class, DI, RNG seam, etc.) are in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md).

- [ ] **RNG seam — only the web run-seed remains (Architecture Review #3).** The core library has no direct
  `Random.Shared` (`IRandomSource` threaded through engine + setup). Still open: the **web composition root**
  builds runs unseeded — `GameSessionManager` constructs `BattleRunner` with no `rng`/seed, and
  `EncounterFactory` (enemy level + move assignment) plus `Gen1StatCalculator` (random DVs) use `Random.Shared`.
  That's where a *per-run* seed would be injected so a whole run replays; wire it when a run needs to be
  reproducible (the recovery/replace **modal** E2Es are the first concrete consumer). Note: reproducing a run
  means seeding creature **construction** (DVs) too, not just the battle. *(Optional: the `AlwaysHit/AlwaysCrit`
  rule shims could be replaced by seeded sources — low priority.)*
  - [x] **Rules-RNG seedable (fixed 2026-06-12).** `DelegatingBattleRules`/`ScriptableRules` now delegate to a
    *seedable* inner `Gen1BattleRules`, and `BattleScenario.Seed(...)` makes EVERY roll deterministic —
    including the rules' previously-global `Roll*` draws (the old Disable/double-faint test-order flakiness).
    Proven by `SeededRulesTests`. **Closed — do not re-file "Roll*/Roll*Turns draws ignore the battle seed."**

- [ ] **Architecture / decision-log doc (`ARCHITECTURE.md`) — NEXT UP (start here).** Capture the *why*
  behind the two-DB split, the event-sourced engine + emitter pattern, the three seams and the "never branch
  on generation" rule, the web session/SignalR + reconnect-grace flow, and the import-vs-runtime boundary.
  Cross-link from `CLAUDE.md`'s Key Files table. It should *point to* — not restate — the existing
  requirements/lookup docs: `GENERATION_SEAMS.md` (the seam contract + §5.0 checklist), `GEN_DIFFERENCES.md`
  and `GAME_AVAILABILITY.md` (Gen-1 mechanic / game-version requirements references, consulted loosely when
  filling a seam, since the seam check is the real gate). Several existing docs shrink once this exists to
  link to. See **Architecture Review #7** below for the structural debt this doc will reference.

- [ ] **Architecture Review #7 — whole-repo code-smell pass (2026-06-12).** Ordered by leverage. None are
  correctness bugs — the engine, the three seams, and the no-facade `BattleState` reset trick are all sound;
  this is about the two or three files that concentrate all the complexity not becoming change-risky as Gen 2
  and AI move-selection land. `ARCHITECTURE.md` (above) is the first task and should cross-link these.
  - [x] **`AttackAction` god-object → `IMoveEffect` registry (highest leverage). DONE 2026-06-13** — moved
    to `TODO_ARCHIVE.md` (Tech-Debt cleanups). The ~320-line effect switch now lives behind an `IMoveEffect`
    registry in `Combat/MoveEffects.cs`, mirroring `ILockInMechanic`; file renamed to `AttackAction.cs`.
  - [x] **`timeline.ts` event-coverage guard. DONE 2026-06-13.** The TS leg of the 3-way event map was the
    one unguarded by a contract test — a new `BattleEvent` would silently fall through `expandEvent`'s
    `default: {}` and never render. Added `WebEventContractTests.EveryBattleEventHasATimelineArm`: it reflects
    over every concrete `BattleEvent` (the same drift-proof source as the existing SignalR-leg test) and
    asserts each has a `case '<Name>'` arm in `timeline.ts` (located via `[CallerFilePath]`, read as text —
    no codegen, single source of truth = backend reflection). Verified it fails-and-names the event when an
    arm is removed. Suite 867 → **868 .NET**.
  - [ ] **Delete or re-scope `ConsoleBattleEventEmitter` (254 lines, production-dead).** Never instantiated
    in app code (`new ConsoleBattleEventEmitter` = 0 hits); used only in `CoreMechanicsTests` as a "run the
    formatter" emitter that spams stdout no one reads. It is a 3rd exhaustive event-map with no live
    consumer. Either delete it (point those tests at `RecordingEmitter` / a null emitter) or document the
    test-only role and bring it under the `timeline.ts`-style coverage guard so it can't silently rot.
  - [ ] **Split `CoreMechanicsTests.cs` by capability.** ~3100 lines, one class, ~130 tests across 14
    unrelated `// ──` regions (stat stages, crit, XP, Metronome, EncounterSelector, seeded stat-calc…).
    Violates our own "tests grouped by capability, not batch" rule — which the **Integration** suite already
    follows (`Gen1Attacks/*`, `Interactions/*`). Split the unit suite the same way.
  - [ ] **Filename ≠ contained type (renames).** ~~`IBattleAction.cs` → `AttackAction.cs`~~ ✅ (done with the
    `IMoveEffect` extraction above — interface split into its own `IBattleAction.cs`). Still open:
    `GameDbContext.cs` holds `MovesDbContext` + `PokemonDbContext` and **no** `GameDbContext` type — rename /
    split to match what's inside. Pure navigation friction, no behavior change.
  - [ ] **Importer `new HttpClient()` per request.** `MoveImport.FetchMoveDataByUrl` and the
    `PokeApiConnector` downloaders create a client per call (socket-exhaustion antipattern). One-shot tool so
    low blast radius — swap to a single shared/static `HttpClient`.
  - [ ] **Minor cleanups.** Drop the legacy `out`-less `DamageCalculator.CalculateDamage` overload if only
    tests use it; dedupe the repeated `_rng.Next(1, 101)` secondary-roll idiom (written as both `> chance`
    and `<= chance` in the same file) behind a `rules.SecondaryHits(...)` helper; name the magic move IDs in
    `MoveImport.MapToAttack` (120/153/69/101/162…) and split its three concerns — `past_values` resolution,
    name→effect map, layer-2 corrections — into private methods.

### Known Gaps
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector
  exists in the UI
- Enemy Pokémon do not evolve — wire into level-up system when Game Loop is built
- **Endless-chain double-faint:** ✅ tested (2026-06-12). A mutual end-of-turn DoT double-faint counts as a
  loss (`break` before the win-count); pinned deterministically by
  `BattleRunnerTests.Runner_DoubleFaintFromEndOfTurnPoison_CountsAsLoss_NotAWin`.

### Learnset import (DB-architecture detail, part of Learnset System)
- [ ] Extend `PokeApiPokemon` DTO with `Moves` array *(✅ done in the initial-moveset work — see archive; kept
  here as the schema-level note)*
- [ ] In `PokemonImport`, parse `version_group_details`, filter to `"red-blue"` + `"level-up"`, persist
  `PokemonLearnset` rows idempotently *(✅ done — see archive)*
</content>
