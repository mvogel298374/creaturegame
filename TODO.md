# Battle Sim – TODO List

> **Active tasks only.** Completed work (batches 1–17, done tech-debt, fixed bugs) lives in
> [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only if you need the history of a finished item.
> **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) · `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions)

**Current state (2026-06-17):** The Gen 1 battle engine is feature-complete — all 165 moves, XP & level-up,
the Endless Battle Chain, the Roguelite recovery/encounter layer, the Learnset System, **AI move selection**
(a gen-specific `IBattleAi` brain), and **EV / Stat-Exp gain** are all done and archived in
[`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) (read it for the history of any finished item). `ARCHITECTURE.md`, the RNG
**per-run web seed** (Tech Debt #3), and Architecture Review #7's higher-leverage structural items are also
done (only the **minor cleanups** bullet remains — see Tech Debt). A round of **Web UI polish** also landed —
STAB indicator, per-move effectiveness pill, colour-coded battle log, friendlier connection-error message (all
archived). Suite: **930 .NET + 55 Vitest + 19 Playwright E2E** (all green).

**Next:** More Web UI polish (Pokémon overview screen, sprite-shake on damage), then the Catch Mechanic /
Game-Loop layer (party, save, evolution). The recovery/replace-move **modal** E2Es are unblocked now the
per-run seed exists (pass a fixed `seed` in the `start` request for a deterministic run).

---

## Web UI — Polish

Stack: React 18 + TypeScript + SignalR + Phaser 3. (Phaser canvas & core animations ✅ done — see archive.)

> Done UI-polish items (level-up toast, STAB indicator, per-move effectiveness pill, colour-coded battle log,
> friendlier connection-error message) are archived under **Web UI Polish pass (2026-06-17)** in `TODO_ARCHIVE.md`.

- [ ] `BattleEndedOverlay` — **superseded by the Endless Battle Chain's `RunEnded` game-over screen** (a
  per-`BattleEnded` overlay no longer fits an endless chain); build it there, run-scoped, not per battle
- [x] **Pokémon overview screen** — **DONE 2026-06-18.** Tabbed INFO / STATS / MOVES overview replacing the
  old base-stats `CheckPanel`, opened by the in-battle CHECK POKEMON action. Shows actual stats + per-stat DV
  (0–15) + Stat-Exp, types/status/HP/XP/BST + front sprite (INFO), and per-move type/category/power/accuracy/
  PP/description (MOVES). Data via a new on-demand REST snapshot `GET /api/game/{gameId}/player`
  (`PlayerOverviewDto.From(Creature)` reading the live in-session creature from `GameSessionManager`) — kept
  off the per-turn event stream. Gen-1 model (single Special; physical/special by move type). Tests:
  `PlayerOverviewDtoTests` (stat + category mapping), `e2e/overview.spec.ts` (tab structure), live-verified.
  *(Between-battles/party entry stays with the deferred Game-Loop layer.)*
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
