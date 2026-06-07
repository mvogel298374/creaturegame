# Battle Sim – TODO List

> **Active tasks only.** Completed work (batches 1–17, done tech-debt, fixed bugs) lives in
> [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only if you need the history of a finished item.
> **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) · `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions)

**Current state (2026-06-07):** Move-coverage pass COMPLETE — all 165 Gen 1 moves have behaviour/coverage
tests (incl. Transform + Conversion). Suite: 813 .NET + 37 Vitest. Next up: the post-coverage sequencing below.

---

## Post-coverage sequencing (the planned order)

Set 2026-06-06, with the mutation batch since done. Remaining order:
1. ~~Deferred type/identity-mutation batch (Transform + Conversion)~~ ✅ DONE
2. jump-kick / hi-jump-kick Ghost-immunity crash edge (Gen 1 also crashes on Fighting→Ghost 0×)
3. Counter answer for fixed / level-based damage (today only standard-path damage is counterable)
4. `AttackAction` lock-in abstraction (the `ILockInMechanic` refactor — see Tech Debt #6a)
5. **The full integration-test pass** — moved here (after the lock-in refactor, before the facade
   migration) because end-to-end tests are more valuable once the refactors that change call shapes land
6. `BattleState` facade migration (drop the delegating props — Tech Debt #2 optional cleanup)
7. `GameController` run-seed (Tech Debt #3 remaining)

---

## XP & Level-Up — finish the in-battle loop

**Slated: next combat-fidelity item.** Small, no new data/schema. Polishes the single-battle path that
already exists (does **not** require the Game Loop).

**What works today:** XP is awarded on enemy faint (`Battle.StartFightAsync` → `CalculateXpAwarded` →
`GainExperience`), the player levels up (chained `LeveledUp` events, one per level), stats recalc and HP
heals the delta. The frontend animates an XP-bar fill on `LeveledUp`.

**What's missing / not "proper":**
- [ ] **Live XP data to the client.** `TurnStarted` carries no XP, so the bar fills to a hardcoded
  placeholder (`playerXpToNext = 100`). Add `PlayerExperience`, `PlayerXpThisLevel`, `XpToNextLevel`
  (derived from `Creature.Experience` and `CalculateExperienceForLevel`) to `TurnStarted` (or a small
  dedicated event); `useBattleHub.ts` dispatches them into `playerXp` / `playerXpToNext`.
- [ ] **XP-gain animation.** On win, animate the bar filling by the XP earned *before* the level-up
  fill/reset (today it only fills on `LeveledUp`).
- [ ] **Verify the multi-level path end to end** — a big XP award crossing several levels emits N
  `LeveledUp` events and the bar steps through each.
- [ ] **Surface the level-up moment** clearly in the log/UI (the hook the deferred move-learning prompt
  will attach to).

**Tests:**
- [ ] Backend: `TurnStarted` (or the new event) carries correct `XpToNextLevel` / current XP for a known
  species+level.
- [ ] Backend: an XP award spanning multiple levels emits the right sequence of `LeveledUp` events.
- [ ] E2E: §7 — XP bar fills and the "grew to level N!" line appears on a win (see Browser-Based UI Testing §7).

---

## Learnset System — Level-up move learning — DEFERRED

(Initial moveset from learnsets ✅ done — see archive.) **Blocked by "XP & Level-Up" above** — level-up
move learning has no place to surface until the player can see and follow a level-up during play. Only the
**player** ever levels up; the enemy's moveset is fully settled at build time.
- [ ] `Creature.LevelUp()` checks learnset for moves at the new level
- [ ] Slot free → add automatically; emit `MoveLearned(string CreatureName, string MoveName)`
- [ ] Slots full → emit `MoveReplacementRequired(…)` — blocking event; backend waits on an
  `IBattleInput`-style TCS (Battle must drive level-ups one at a time to interleave the prompt)
- [ ] `BattleHub` + `SignalRInput` extended with `ForgetMove(int slotIndex)` / `SkipNewMove()` path
- [ ] `MoveLearned` / `MoveReplacementRequired` handled by all emitters + `useBattleHub.ts` (+ React modal)
- [ ] **XP bar:** `TurnStarted` carries `PlayerExperience` / `XpToNextLevel`; `useBattleHub.ts` dispatches
  so the bar fills live
- [ ] Tests: `Learnset_LevelUp_AddsNewMoveWhenSlotAvailable`, `Learnset_LevelUp_EmitsMoveReplacementRequired_WhenFull`

---

## AI Move Selection

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

- [ ] `BattleEndedOverlay` — covers battle screen on `BattleEnded`; winner, "Play Again" → `/select`, "Main Menu" → `/`
- [ ] Level-up notification toast on `LeveledUp` event
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

Status: **harness + core specs landed** (9 specs, run via `npm run test:e2e` or the VS Code Playwright
extension — see `ClientApp/e2e/README.md`). Remaining: a few checklist sections (§6 status, §7 XP/QUIT),
`data-testid`s, and CI.

**Remaining:**
- [ ] `data-testid` attributes — **deferred**: specs lean on stable semantic classes already present
  (`.btn-new-game`, `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`). Add testids
  only where a class proves brittle.
- [ ] CI step (or `dev.ps1`-adjacent script / `test.ps1 -StartStack`) that boots backend + frontend, runs
  the suite headless, and tears down.
- [ ] §6 Status conditions — badge on correct nameplate; log grammar (status is non-deterministic per
  battle; needs a seeded or forced-status path).
- [ ] §7 Faint & end — XP fill / level-up line / QUIT → title not yet asserted (battle play-through itself ✅).
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

## Game Loop & Progression

**Prerequisites:** Catch Mechanic, BattleState extraction (✅ done), `PlayerDbContext` / `save.db`

> **Sequencing:** this whole layer is intentionally **deferred until combat fidelity is fully ironed out** —
> the battle sim is the foundation the roguelike/lite loop builds on.

- Player starts with one Pokémon; win → new BST-scaled encounter; lose → game over with run summary
- Catch → Pokémon added to party (up to 6); choose lead between battles
- Progressive difficulty: `targetBst = party lead BST + (depth × 10)`; trainer encounters at milestones
- Evolution: player Pokémon evolve at level threshold (requires `PokemonEvolution` table in `pokemon.db`);
  enemy evolves to correct form for their level before battle
- `PlayerSave` / `SavedCreature` models in `save.db`; auto-save after each battle
- Party management UI between battles
- **Cross-encounter persistence:** carry major status across encounters and revisit the current "reset *all*
  transient state per battle" behaviour — today HP persists between battles but status doesn't (canonical
  Gen 1 keeps major status out of battle). The `Creature`/`BattleState` split is the seam; see `STATE_MODEL.md §2`.

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

> Done items (Architecture Review #1/#2/#4/#5/#6, the #6a cleanups, struct→class, DI, RNG seam, etc.) are
> in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md).

- [ ] **Flaky `RestContractTests.RestUserIsForcedToSkipTurnsWhileAsleep`** — uses `AutoSelectInput` + unseeded
  RNG; fails ~every other run, passes in isolation (same shape as the resolved flaky-OHKO debt). Rewrite to a
  seeded/deterministic path (set the relevant rolls explicitly) rather than relying on `AutoSelectInput`.

- [ ] **RNG seam — remaining (Architecture Review #3).** The core library has no direct `Random.Shared`
  (`IRandomSource` threaded through engine + setup). Still open: **`GameController` (web) uses `Random.Shared`**
  for enemy level + random move assignment — deferred; it's the composition root where a per-run seed would be
  injected, but there's no run-seed concept yet (Game Loop). Wire a run seed here when runs exist. *(Optional:
  the `AlwaysHit/AlwaysCrit` rule shims could be replaced by seeded sources — low priority.)*

- [ ] **`BattleState` facade migration (Architecture Review #2 optional cleanup).** Migrate call sites to
  `creature.Battle.X` and drop the delegating facade, so new per-battle fields can *only* be added to
  `BattleState`. Deferred — not worth the ~120-site churn yet.

- [ ] **`AttackAction.ExecuteAsync` lock-in abstraction (Architecture Review #6a, deferred).** The four
  lock-in mechanics (two-turn / rampage / rage / bide) spread logic across selection (`Battle.SelectMoveAsync`),
  the PP/continuation flags, and per-mechanic charge/store blocks. A full `ILockInMechanic`-style abstraction is
  a high-risk refactor of the most central method with no third use case driving it — **deferred** until the
  next lock-in move lands (the trigger to abstract). YAGNI for now.

- [ ] **Counter only answers standard-path damage** — fixed/level-based damage isn't recorded, so it's not
  counterable (documented simplification; item 3 in Post-coverage sequencing).

- [ ] **jump-kick / hi-jump-kick Ghost-immunity crash edge** — Gen 1 also crashes on a Fighting→Ghost 0×
  immunity; today only the accuracy-miss branch crashes (item 2 in Post-coverage sequencing).

- [ ] **Architecture / decision-log doc (`ARCHITECTURE.md`).** Capture the *why* behind the two-DB split,
  event-sourced engine + emitter pattern, the three seams and the "never branch on generation" rule, the web
  session/SignalR flow, and the import-vs-runtime boundary. Cross-link from `CLAUDE.md`'s Key Files table.

### Known Gaps
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector
  exists in the UI
- Enemy Pokémon do not evolve — wire into level-up system when Game Loop is built

### Learnset import (DB-architecture detail, part of Learnset System)
- [ ] Extend `PokeApiPokemon` DTO with `Moves` array *(✅ done in the initial-moveset work — see archive; kept
  here as the schema-level note)*
- [ ] In `PokemonImport`, parse `version_group_details`, filter to `"red-blue"` + `"level-up"`, persist
  `PokemonLearnset` rows idempotently *(✅ done — see archive)*
</content>
