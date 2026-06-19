# Battle Sim – TODO List

> **Active tasks only.** Completed work (batches 1–17, done tech-debt, fixed bugs) lives in
> [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only if you need the history of a finished item.
> **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) · `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions)

**Current state (2026-06-19):** The Gen 1 battle engine is feature-complete — all 165 moves, XP & level-up,
the Endless Battle Chain, the Roguelite recovery/encounter layer, the Learnset System, **AI move selection**
(a gen-specific `IBattleAi` brain), **EV / Stat-Exp gain**, and the full **Evolution System** (level-up
evolution end-to-end incl. the Phaser sprite-morph + a Gen 1 B-cancel prompt) are all done and archived in
[`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) (read it for the history of any finished item). `ARCHITECTURE.md`, the RNG
**per-run web seed** (Tech Debt #3), and Architecture Review #7's higher-leverage structural items are also
done (only the **minor cleanups** bullet remains — see Tech Debt). A round of **Web UI polish** landed too —
STAB indicator, per-move effectiveness pill, colour-coded battle log, friendlier connection-error message, and
the tabbed **Pokémon overview screen** (CHECK POKEMON) (all archived). Suite: **1021 .NET + 59 Vitest + 20
Playwright E2E** (all green). The **Gen 1 item-data import** (battle-usable items → `items.db`) is also
done (2026-06-19) — see its section.

**Next:** **Encounter Logic** (see its section) — the design of *what* the player faces and *how* they can
acquire it, which must land **before** any catch/acquisition mechanic. The Catch Mechanic is intentionally
**pushed back behind it** (see the note in its section): in a roguelite, letting the player catch *truly
random* Pokémon balloons the party's power curve and breaks balance fast, so encounter/acquisition rules are
the real prerequisite, and "catch" is likely a misnomer for what will be a broader **acquisition** layer.
Web UI polish is essentially done (move-specific attack animations + the low-priority `ConsoleInput` terminal
menu remain). The recovery/replace-move **modal** E2Es are unblocked now the per-run seed exists (pass a fixed
`seed` in the `start` request for a deterministic run).

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
- [ ] **Between-encounter modal E2Es** — deterministic via a fixed `seed` in the `start` request: Poké Center
  recovery Heal/Skip, move-replacement forget/decline, and **evolution Allow/Cancel** (the Gen 1 B-cancel
  prompt). All three share the same blocking-modal shape and are unblocked by the per-run seed; none are
  written yet. Each is unit/integration-covered (runner + timeline arms); this closes the DOM-level gap.
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

## Evolution System ✅ DONE — archived

Full level-up evolution end-to-end (data+seam → core+loop → Phaser morph) + a Gen 1 B-cancel prompt, all
shipped and committed. See **Evolution System** in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) for the full record.
**Only open piece:** stone evolutions, deferred with the **Catch Mechanic** (the `Stone` trigger +
`IEvolutionRules.StoneUsed` are built and dormant, waiting on a bag).

---

## Encounter Logic  ⟵ do this BEFORE the Catch / acquisition mechanic

> **Why this comes first.** This is **not** a normal Pokémon game — it's a roguelite. If the player can
> acquire *truly random* Pokémon, the party's power curve balloons and balance breaks fast (a lucky
> early high-BST catch trivialises the run; an unlucky one strands it). So the rules governing **what the
> player faces** and **how/whether they can take it** have to be designed *before* any acquisition mechanic
> is wired in — otherwise we'd be balancing the catch formula against an undefined encounter distribution.

The seam already exists (`EncounterSelector.PickByBst`, `GameController.BuildCreature`, the
`targetBst = lead BST + depth × 10` curve in **Game Loop & Progression**) — this is about turning that into a
deliberate, balance-aware encounter *design*, not an ad-hoc pick.

- [ ] **`/plan` pass first** — define the encounter model for a roguelite run, e.g.:
  - encounter pool / distribution per depth (BST band + variance, not a flat random draw from all 151)
  - what is even *eligible* to be acquired (cap the BST ceiling relative to the lead? rarity tiers? curated
    "offer" set per encounter rather than "whatever you fought")
  - how acquisition interacts with the difficulty curve so a single lucky pickup can't break the run
- [ ] Gate the eventual acquisition mechanic on these rules (the catch formula's odds are meaningless until
  the encounter distribution is fixed).

---

## Catch / Acquisition Mechanic  ⟵ pushed back behind Encounter Logic

**Deferred — now gated on Encounter Logic above** (random acquisition is a balance hazard in a roguelite; the
encounter/eligibility rules must exist first). Also still wants Phaser animation work (throw/shake/catch — or
whatever the acquisition flow turns out to be).

> **"Catch" is likely a misnomer.** Because this isn't a normal Pokémon game, the player may receive Pokémon
> in **several different ways** — classic in-battle capture, but also (e.g.) post-battle rewards, gifts/offers,
> picking from a curated set, etc. Treat this as a broader **acquisition** layer when it's designed; the
> in-battle "catch" below is just *one* possible channel, not the whole feature.

**When ready (in-battle capture channel — Gen 1 reference):**
- [ ] Bag action in move menu; `Battle` extended with a "catching" state
- [ ] Gen 1 capture formula: `floor((MaxHP × 3 − HP × 2) × CatchRate / (MaxHP × 3))` vs. 0–255 roll
- [ ] `PokemonSpecies.CatchRate` already imported ✓
- [ ] `CaptureAttempted(string TargetName, bool Caught)` battle event
- [ ] `BattleEnded` variant: `reason: "Caught"`
- [ ] Unlocks the dormant **stone evolutions** (`Stone` trigger + `IEvolutionRules.StoneUsed` are built and
  waiting on a bag).

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
- Evolution: ✅ **DONE** (level-up evolution end-to-end + Gen 1 B-cancel; trade lines → level 37) — see
  **Evolution System** in `TODO_ARCHIVE.md`. Only **stone** evolutions remain, gated on the bag (Catch Mechanic).
- `PlayerSave` / `SavedCreature` models in `save.db`; auto-save after each battle
- Party management UI between battles
- **Cross-encounter persistence:** ✅ major status now carries across encounters in the Endless Battle Chain
  (2026-06-10) — `BattleRunner` snapshots the player's status after each win and re-applies it into the next
  `Battle` (via `playerEntryStatus`), with `IBattleRules.CarryStatusOutOfBattle` deciding the out-of-battle
  transform (Gen 1: Toxic→Poison). Volatiles (confusion, stages) still reset per battle — canonical. HP/PP
  already persisted. (Sleep carries its counter; Freeze persists.) Remaining: only matters again when
  switching/party exists. See `STATE_MODEL.md §2`.

---

## Item System — Data Import (Gen 1)  ⟵ unblocked, data-layer only · `/plan` DONE 2026-06-19

Bring Gen 1 items into the data layer, mirroring the existing two-DB / EF-import pattern (`PokeApiConnector`
→ SQLite → EF Core context → service). **Import only** for now — no in-game bag, use, or effects yet; this is
the foundation the later **acquisition / bag** layer (and held-item / consumable mechanics) will sit on, but
it has **no blockers** and can land standalone.

**Locked design decisions (`/plan`, 2026-06-19):**
- **DB home:** new `items.db` + `ItemsDbContext`, parallel to `moves.db`/`pokemon.db` (own
  `DB/Migrations/Items` folder). Keeps two-DB symmetry; isolates item schema churn.
- **Scope = "anything usable *in battle*"**: Poké Balls (standard + special), healing
  (Potion→Full Restore), status cures (Antidote/Burn Heal/Ice Heal/Awakening/Paralyze Heal/Full Heal),
  Revive/Max Revive, PP restore (Ether/Max Ether/Elixir/Max Elixir), and X-items (X Attack/Defense/Speed/
  Special/Accuracy, Dire Hit, Guard Spec). **Excluded:** evolution stones, vitamins (HP Up/Protein/…/PP Up),
  Rare Candy, key items, TMs, berries — all menu-only or out of a battle roguelite's scope.
- **Gen 1 filter:** ⚠️ the planned `game_indices = generation-i` filter **does not work** — PokeAPI items
  have no `/generation/1` list AND their `game_indices`/`flavor_text_entries` only reach back to **Gen 3**
  (Poké Ball has no Gen 1 entry in either). There is **no data-driven Gen 1 item signal**. So, as
  `GameAvailabilitySeeder` does for species (DATA_IMPORT.md §4.3/§5.4), the Gen 1 roster is a **hand-curated
  allowlist** (`ItemMapper.Gen1BattleItemNames`) that also drives the fetch (fetch each `/item/{slug}`).

**Implementation — ✅ DONE (2026-06-19):**
- [x] `Item` model (`creaturegame/Items/Item.cs`) + `ItemCategory` enum; layer-2 Gen 1 gameplay numbers
  (heal amount, cured status, revive %, PP restore, X-item stat boost). Ball catch-rate multiplier
  deliberately NOT modelled (capture is a battle formula → deferred Catch mechanic).
- [x] `ItemsDbContext` (`items.db`) + EF migration `DB/Migrations/Items`; `DbPathHelper` path.
- [x] `PokeApiItem` DTO + `ItemImport` (network+DB) + `ItemMapper` (pure mapping + roster), mirroring the
  `EvolutionImport`/`EvolutionMapper` split. Idempotent upsert; `Program.cs` step + `-- items` single-stage.
- [x] `ItemService` read API (by id / name / all / by category), parallel to `AttackService`.
- [x] `AddDbContextFactory<ItemsDbContext>` registered in `creaturegame.Web/Program.cs`.
- [x] Tests: `ItemImportTests` (mapping + roster) + `ItemsDbServiceTests` (migration + service round-trip) —
  drive real code. **Import run verified** against PokeAPI: 29 items, categories + gameplay numbers correct.
- [x] `DATA_IMPORT.md` updated (new §4.5 + the no-Gen-1-signal/curated-roster wrinkle).
- [ ] **Deferred to UI time (flagged, not built):** item sprite download into `wwwroot/sprites/items/` via the
  idempotent `SpriteDownloader` pattern; the `cost` field is PokeAPI's *current* price (a few Gen 1 prices
  differ — uncorrected, not battle-relevant; see DATA_IMPORT.md §4.5).

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
