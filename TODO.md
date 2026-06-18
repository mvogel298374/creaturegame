# Battle Sim – TODO List

> **Active tasks only.** Completed work (batches 1–17, done tech-debt, fixed bugs) lives in
> [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only if you need the history of a finished item.
> **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) · `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions)

**Current state (2026-06-18):** The Gen 1 battle engine is feature-complete — all 165 moves, XP & level-up,
the Endless Battle Chain, the Roguelite recovery/encounter layer, the Learnset System, **AI move selection**
(a gen-specific `IBattleAi` brain), and **EV / Stat-Exp gain** are all done and archived in
[`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) (read it for the history of any finished item). `ARCHITECTURE.md`, the RNG
**per-run web seed** (Tech Debt #3), and Architecture Review #7's higher-leverage structural items are also
done (only the **minor cleanups** bullet remains — see Tech Debt). A round of **Web UI polish** landed too —
STAB indicator, per-move effectiveness pill, colour-coded battle log, friendlier connection-error message, and
the tabbed **Pokémon overview screen** (CHECK POKEMON) (all archived). Suite: **932 .NET + 55 Vitest + 20
Playwright E2E** (all green).

**Next:** the **Evolution System** is ✅ DONE — all three stages (data+seam, core+loop+event, Phaser
sprite-morph). Trade lines → level 37; stones remain deferred with the Catch/bag work. Stage 3's commit is
pending approval; an in-run E2E is deferred (see its note). After this, the next layer is the **Catch
Mechanic** (bag — which also unlocks stone evolutions) → the rest of the Game-Loop layer (party, save).
Web UI polish is essentially done (only the low-priority `ConsoleInput` terminal menu remains). The
recovery/replace-move **modal** E2Es are unblocked now the per-run seed exists (pass a fixed `seed` in the
`start` request for a deterministic run).

---

## Web UI — Polish

Stack: React 18 + TypeScript + SignalR + Phaser 3. (Phaser canvas & core animations ✅ done — see archive.)

> Done UI-polish items are archived in `TODO_ARCHIVE.md`: level-up toast, STAB indicator, per-move
> effectiveness pill, colour-coded battle log, friendlier connection-error message under **Web UI Polish pass
> (2026-06-17)**; the run-over screen (`BattleEndedOverlay`), Pokémon overview screen (CHECK POKEMON), and
> sprite-shake-on-damage under **Web UI Polish — Run-Over Screen, Overview, Sprite-Shake (2026-06-18)**.

- [ ] **Move-specific attack animations (grouped, not per-move)** — today every move plays the one generic
  lunge (`BattleScene.playMoveAnimation`) + the type-neutral white tint + the new `playDamageShake`. Give moves
  distinct animations by mapping each to one of a small set of **animation families** (≈5–7), keyed off data we
  already have — `DamageType` (Gen 1: 15 types) and `AttackType` (Physical / Special) — plus a few special-cased
  effects. Goal is a believable variety **without** 165 bespoke clips.
  - **Proposed families** (refine in `/plan`):
    - *Physical contact* — the current lunge (Tackle, Body Slam, most Normal/Fighting/Ground physical). Keep as-is.
    - *Projectile / ranged special* — a sprite/particle travels attacker→target, no lunge (Water/Fire/Electric/
      Psychic/Ice/Grass specials: Ember, Water Gun, Thunderbolt, Psybeam, Ice Beam…).
    - *Status / self-buff (no contact)* — a glow/pulse on the **user**, no lunge or target shake (stat-stage moves,
      screens, Mist/Focus Energy, Sleep/Poison/Para powders target-side instead).
    - *Two-turn / charge* — pair with the existing charge text + a charge-glow on turn 1, release burst on turn 2
      (Fly, Dig, Solar Beam, Sky Attack, Razor Wind, Skull Bash).
    - *Multi-hit / flurry* — repeat a quick jab N times in step with `MultiHitCompleted` (Fury Attack, Double Slap…).
    - *(Cheap layered win, any family)* tint the contact flash + shake/particle colour by the move's **type colour**
      (reuse the `TypeBadge` palette) instead of flat white.
  - **Plumbing (the real work, mind the seam):** the animation is driven by `MoveUsed`, which today carries only
    `(AttackerName, MoveName)` — the client can't see the *enemy's* move type/category (the player's is in the
    turn's `MoveInfo`, the foe's is not). So project `DamageType` + `AttackType` onto the `MoveUsed` event and its
    `SignalRBattleEventEmitter` mapping, with the matching field-level guard (this is exactly the recurring
    **web event field-projection gap** — engine tests don't catch the missing wire field; see the memory +
    `WebEventContractTests`). Then add a pure `moveAnimationFamily(type, category, slug)` map in the client
    (unit-testable like `timeline.ts`), new per-family `BridgeCommand`s + `BattleScene` handlers, each still
    emitting `animationComplete` so the timeline's `awaitAnim` contract holds.
  - **Builds on** the existing `playMoveAnimation` / `playDamageShake` seam and the `timeline.ts` step model;
    keep durations unit-tested away from the wall clock and assert ordering via the bridge in E2E (per
    `e2e/README.md`). Polish-tier — after the current run-over/shake items, before/with the Catch animation work.
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

## Evolution System

**Decided (2026-06-18, `/plan` with the user):**
- **Trade evolutions → level-up.** No trading in a single-player roguelite, so the 4 Gen 1 trade lines
  (Kadabra→Alakazam, Machoke→Machamp, Graveler→Golem, Haunter→Gengar) all evolve at **level 37** (flat, for
  consistency). This roguelite conversion lives **on the seam**, not in the data.
- **Stone evolutions → deferred** with the Catch/bag work (they need an item/bag system). The `Stone` trigger
  is imported + modelled now but kept a **documented dormant stub** — Eevee, Pikachu→Raichu, Clefairy→Clefable,
  etc. simply don't evolve player-side until the bag exists. (Enemies can still *spawn* as the evolved form —
  that's emergent from BST encounter selection, not the evolution trigger.)
- **Scope = full level-up system end-to-end**, built in reviewable **stages** (below).

**Architecture (matches the existing seam pattern — see `GENERATION_SEAMS.md`):**
- *Data is faithful, seam does the adaptation.* `PokemonEvolution` stores the **real** Gen 1 trigger
  (`Trade` stays `Trade`); `Gen1EvolutionRules` interprets a `Trade` edge as level-37. Keeps the
  data honest and the roguelite/gen rule swappable in one place.
- New generation seam **`IEvolutionRules`** + `Gen1EvolutionRules.Instance` default, per-gen XML docs on every
  member. A future `Gen2EvolutionRules` adds friendship/time/held-item triggers by *implementing the
  interface* — zero engine changes.

### Stage 1 — Data + seam ✅ DONE (2026-06-18; audit PASS-WITH-ADVISORIES, both resolved)
- [x] **`PokemonEvolution` table** in `pokemon.db` (`FromSpeciesId`, `ToSpeciesId`, `Trigger`,
  `LevelThreshold?`, `StoneItemId?`, `Generation`) + migration `AddPokemonEvolution`. Unique index on
  (From, To, Generation); lookup index on (From, Generation).
- [x] **Importer** — `EvolutionMapper` (pure chain→Gen-1-edge filter) + `EvolutionImport` (idempotent,
  dedup-by-chain), new DTOs (`PokeApiEvolutionChain` + `evolution_chain` on the species DTO), wired into
  `Program.cs` (+ an `-- evolutions` arg to re-run just this stage). **Live import verified: 72 Gen 1 edges
  (52 Level / 16 Stone / 4 Trade)** — matches canon; triggers stored faithfully (Trade stays Trade).
- [x] **`IEvolutionRules` seam** + `Gen1EvolutionRules.Instance` in `creaturegame/Evolution/`.
  `EvolutionContext` = closed record hierarchy (`LeveledTo` / `StoneUsed` / `Traded`). Gen 1: Level fires at
  `Level >= threshold`; Trade → `TradeEvolutionLevel` (37) named constant on the seam; Stone fully
  implemented but dormant (no caller emits `StoneUsed` until the bag exists). Mutual-exclusivity assumption
  documented on the interface.
- [x] **Tests (20):** `EvolutionImportTests` (mapper, incl. multi-detail precedence), `Gen1EvolutionRulesTests`
  (seam — asserts the level-37 quirk + stone dormant-on-levelup/ready-on-stoneuse), and
  `PokemonEvolutionDataContractTests` (live-db pin: 72/52/16/4, the 4 trade lines, Eevee's 3 branches,
  dex-bounds). Suite: **951 .NET** green.

### Stage 2 — Core application + game loop ✅ DONE (2026-06-18; audit PASS-WITH-ADVISORIES, both resolved)
- [x] **`Creature.EvolveTo(PokemonSpecies newForm)`** — adopts new base stats/types/growth-rate/base-exp/id
  and recomputes via the existing `InitializeFromSpecies`→`CalculateStats` (no new stat math). Individual half
  (DVs/Stat Exp/Level/Experience/PP/moveset) carries over; current HP rises by exactly the max-HP delta.
- [x] **Learnset on evolution** — extracted `MoveLearning.LearnMovesForLevelAsync` from `Battle` (shared,
  behaviour-preserving) and reused it: after evolving, the run loop seats the evolved learnset and runs the
  same auto-learn / replace-move flow for moves learned at the current level.
- [x] **Loop wiring** — `BattleRunner` takes an optional injected `checkEvolution` resolver (mirrors
  `enemySupplier`, keeping core data/gen-agnostic; null = plain chain). After each win it applies `EvolveTo`,
  emits `CreatureEvolved`, then drives move-learning. Web resolver = `EncounterFactory.ResolvePlayerEvolutionAsync`
  (edges → `Gen1EvolutionRules` → evolved species + learnset), wired in `GameSessionManager`.
- [x] **`CreatureEvolved` event** + SignalR projection + `timeline.ts` log arm + field-level contract guard
  (the recurring web event field-projection gap) + console-emitter line. *(Phaser sprite-morph animation is
  Stage 3.)*
- [x] **Enemy form** — confirmed BST encounter selection already yields level-appropriate evolved enemies (the
  full 1–151 pool is in scope and evolved forms outrank pre-evos by BST as depth scales); no guard needed.
- [x] **Tests (+9):** `EvolveToTests` (stat recompute + HP-delta + individual-half preservation),
  `BattleRunnerEvolutionTests` (wiring: fires after win, emits event then learns; null-resolver = plain chain),
  `EncounterEvolutionTests` (live DB: level fires at threshold, trade→37, stone dormant), `CreatureEvolved`
  field guard + Vitest timeline arm. Suite: **962 .NET + 57 Vitest** green.

> **Known limitation (accepted):** a multi-threshold level jump in a single battle evolves only one stage that
> win (the next stage fires on the next win). Fine for the roguelite; not a bug.

### Stage 3 — Web event + animation ✅ DONE (2026-06-18)
- [x] **`CreatureEvolved` event** — done in Stage 2 (event + SignalR projection + field-level guard +
  timeline arm + console line). See Stage 2.
- [x] **Client morph** — `playEvolutionAnimation` `BridgeCommand` + `PhaserBridge` event + `BattleScene`
  handler: the classic Gen 1 **white-silhouette flicker** (`setTintFill(0xffffff)` alternating old/new shapes,
  then settle on the evolved back sprite in colour), loaded on demand. Emits `animationComplete` so
  `timeline.ts`'s `awaitAnim` holds; the "evolved into" line lands on the new sprite. The `CreatureEvolved`
  timeline arm now emits the morph + awaits it. **Correctness fix:** evolution updates `playerTrueSpeciesId`
  (+ a new `initialPlayerSpeciesId`) so the post-win `resetPlayerSprite` reverts to the *evolved* form, not
  the pre-evolution sprite.
- [x] **Timeline unit test** — Vitest pins the arm's order (announce → emit `playEvolutionAnimation` →
  `awaitAnim` → confirm line). Suite: **962 .NET + 57 Vitest** green; `tsc --noEmit` clean.
- [ ] **E2E (deferred, with reason)** — a Playwright spec driving a *real* in-run evolution is hard to force
  deterministically (the player must win battles and cross a level/trade-37 threshold; one seeded run can't
  reliably reach it without a test-only entry hook). The bridge **ordering contract** is already covered
  deterministically at the timeline layer (Vitest). Revisit if a seeded "evolve now" hook is added.

- [x] **Cry-mismatch fix (2026-06-18)** — the OGG cry keys were bound once in `preload()` to the *initial*
  species (`cry-player`/`cry-enemy`), so with cries present (production) **every chained enemy** played the
  first enemy's cry and an evolved/transformed player played its pre-form cry. Re-keyed cries by species id
  (`cry-{id}`), loaded on demand wherever the sprite changes (`spawnEnemy`, evolution morph, `transformSprite`)
  and at `preload`; `playCry` resolves the live id's cry, synth-fallback unchanged. `tsc` clean, 57 Vitest green.

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
> the battle sim is the foundation the roguelike/lite loop builds on. The **Endless Battle Chain** (above) is
> the first minimal slice of this layer (persistent single creature, endless wild encounters); the items
> here are everything it deliberately leaves out (catch, party, save, evolution, difficulty curve).

- Player starts with one Pokémon; win → new BST-scaled encounter; lose → game over with run summary
- Catch → Pokémon added to party (up to 6); choose lead between battles
- Progressive difficulty: `targetBst = party lead BST + (depth × 10)`; trainer encounters at milestones
- Evolution: **now its own section** — see **Evolution System** above (designed 2026-06-18, staged). Player
  level-up evolution end-to-end; trade lines → level 37; stones deferred with the bag.
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

- [ ] **RNG seam — only an optional test shim remains.** The per-run web seed (Architecture Review #3,
  2026-06-17), the rules-RNG seeding (2026-06-12), and the engine `IRandomSource` thread are all closed and
  archived (see **Web UI Polish + per-run seed pass** in `TODO_ARCHIVE.md`). *Optional, low priority:* replace
  the `AlwaysHit`/`AlwaysCrit` rule shims with seeded `IRandomSource`s. **Do not re-file** "web composition
  root builds runs unseeded" or "Roll*/Roll*Turns draws ignore the battle seed" — both closed.

- [x] **Architecture / decision-log doc (`ARCHITECTURE.md`) — DONE.** Documents the two-DB split, the
  event-sourced engine + emitter pattern, the three seams + "never branch on generation" rule, the web
  session/SignalR + reconnect-grace flow, and the import-vs-runtime boundary; cross-linked from `CLAUDE.md`'s
  Key Files table (kept in sync this session — §2.10 RNG per-run seed).

- [ ] **Architecture Review #7 — only "Minor cleanups" remains.** The higher-leverage structural items are
  all done (2026-06-13/14) and archived in `TODO_ARCHIVE.md`: `AttackAction` god-object → `IMoveEffect`
  registry, the `timeline.ts` event-coverage guard, the `ConsoleBattleEventEmitter` debug-narrator re-scope,
  the `CoreMechanicsTests` split-by-capability (+`EffectRegistryTests`), the filename≠type renames, and the
  importer's shared `HttpClient`. None were correctness bugs — the goal was keeping the few
  complexity-concentrating files change-safe as Gen 2 lands. Remaining:
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
