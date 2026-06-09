# Battle Sim – TODO List

> **Active tasks only.** Completed work (batches 1–17, done tech-debt, fixed bugs) lives in
> [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only if you need the history of a finished item.
> **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) · `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions)

**Current state (2026-06-09):** Move-coverage pass COMPLETE (all 165 Gen 1 moves), integration-test pass
COMPLETE, and the `BattleState` facade migration COMPLETE — the whole **post-coverage sequencing** is done
(archived in `TODO_ARCHIVE.md`). The only deferred item from it is the `GameController` run-seed (needs the
Game Loop; tracked under Tech Debt #3). Suite: 840 .NET + 37 Vitest. **Next up — XP & progression, in two
pieces:** (1) **XP & Level-Up** fidelity, then (2) the **Endless Battle Chain** (a minimal run loop so XP/HP
have stakes). Both below.

---

## XP & Level-Up — finish the in-battle loop

**PIECE 1 — DONE (2026-06-09)** except the E2E §7 spec (below). Engine emits `ExperienceGained` + an
enriched `LeveledUp`/`TurnStarted`; the frontend bar fills honestly and a stat-growth line surfaces on
level-up. Suite 844 .NET + 37 Vitest, `/audit` PASS-WITH-ADVISORIES (all resolved). No new data/schema; did
not require the Game Loop.

**Scope (decided 2026-06-09):** live XP display + honest gain/multi-level animation + **stat-growth
surfacing** and proper level-up output. **Out of scope:** EV gain (its own section below) and level-up move
learning (still deferred). Enemies are **wild** for now, so the XP `a`-multiplier = 1; the trainer 1.5×
lands with trainer encounters later.

**What works today:** XP is awarded on enemy faint (`Battle.StartFightAsync` → `CalculateXpAwarded` →
`GainExperience`), the player levels up (one `LeveledUp` per level), and `CalculateStats` heals the MaxHP
delta. The frontend animates a *placeholder* bar — `playerXpToNext = 100`, fills to full on `LeveledUp`
regardless of the real amount, and nothing animates on a win that doesn't level.

**Event model (engine emits facts; `timeline.ts` owns cadence):**
- [x] New `ExperienceGained(string CreatureName, int Amount)` — emitted on win before any `LeveledUp`.
- [x] Extend `LeveledUp` → level-relative XP pair (`XpThisLevel`, `XpToNextLevel`) + post-level `StatBlock`.
- [x] Enrich `TurnStarted` with `PlayerXpThisLevel` / `PlayerXpToNextLevel` (hardcoded `100` gone).
- [x] Battle drives level-ups **one at a time** (`AddExperience` + `while (TryLevelUp())`) — the seam the
  deferred move-learning will reuse.
- [x] `Creature` exposes `XpThisLevel` / `XpToNextLevel` (full-bar at the level cap) + `StatSnapshot()`.

**Frontend:**
- [x] Honest fill: `XP_GAIN` fills toward the boundary (capped at the level max); each `LeveledUp` resets +
  refills to the leftover via `XP_SET`. `XP_FILL`-to-full slam removed.
- [x] Level-up stat-growth surfaced as a log line (HP/ATK/DEF/SPC/SPD totals). *A richer toast/panel is the
  deferred "Level-up notification toast" Web-UI Polish item.*
- [x] `useBattleHub.ts` dispatches the new XP fields into `playerXp` / `playerXpToNext`.

**Tests:**
- [x] Backend: `TurnStarted` carries correct level-relative XP for a known species+level.
- [x] Backend: a multi-level award emits `ExperienceGained` then the right `LeveledUp` sequence, each with
  correct thresholds + stats; intermediate levels overshoot their span (client caps), final is a partial fill.
- [x] Backend: the `LeveledUp` stat block matches `CalculateStats` at the new level; max-level helpers full-bar.
- [ ] E2E §7 — bar fills and "grew to level N!" + the stat line appear on a win. *(Remaining — Playwright spec.)*

---

## Endless Battle Chain (minimal run loop)

**Slated: PIECE 2, after XP & Level-Up.** A deliberate **minimal slice** of the deferred *Game Loop &
Progression* (below) — pulled forward so XP and HP have stakes: **battle after battle with no end** until
the player's single Pokémon faints. **Not** the full loop — no catch, no party, no save, no evolution, no
version filtering (those stay in *Game Loop & Progression*). One persistent player `Creature` vs a fresh
wild enemy each encounter.

**Persistence (mostly free — the permanent/transient split already does it):** reusing one player `Creature`
across consecutive `Battle` instances carries HP, PP (`PowerPointsCurrent`), Experience and Level forward
(permanent half); status / stat-stages / confusion reset per battle (transient `BattleState`) — canonical
Gen 1. No between-battle heal, so HP and PP matter. See `STATE_MODEL.md §2`.
- [ ] **Verify + lock** with a test that runs two consecutive battles on one `Creature` and asserts
  HP/PP/XP/Level persist while transient state resets. Add `Creature.ResetForNewEncounter()` only if a gap
  surfaces.

**Encounter factory (web):**
- [ ] Extract enemy construction from `GameController.Start` into an injectable `EncounterFactory`
  (DB-backed: species + learnsets + moves). `GameController` uses it for encounter 1; the run loop uses it
  for every encounter after.
- [ ] Level-scaling rule: enemy level tracks the player's **current** level (a leveled-up player keeps
  meeting same-tier foes); BST tracks the current lead. (Depth ramp / difficulty curve = future Game Loop.)

**Run loop (web orchestrator):**
- [ ] Replace the single `StartFightAsync` in `GameSessionManager` (or a new `BattleRunner`) with a loop:
  run battle → player alive? build next enemy via the factory + start the next battle → repeat; player
  fainted → end the run. Per-encounter `BattleEnded(winner)` stays unchanged.
- [ ] New `RunEnded(int BattlesWon, int FinalLevel, string FinalCreatureName)` event (terminal).
- [ ] RNG/seed: this is the composition root for the deferred per-run seed (Tech Debt #3) — wire a seed here
  when seeding lands; not required now.

**Frontend:**
- [ ] `BattleEnded` with the player as winner → brief **intermission** ("next foe approaches"), keep the
  HP/XP bars, then the next `BattleStarted` resumes play (no terminal screen).
- [ ] `RunEnded` → game-over screen with a run summary (battles won, final level) → back to title. (Folds in
  the deferred `BattleEndedOverlay` Web-UI item, now run-scoped.)

**Tests:**
- [ ] Backend: the chain runs N encounters on one `Creature`; a player faint emits `RunEnded` with the
  correct summary.
- [ ] Web contract: `RunEnded` maps to a named client event (extend `WebEventContractTests`).
- [ ] E2E: win an encounter → next encounter starts with HP/XP carried; faint → game-over summary.

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

- [ ] `BattleEndedOverlay` — **superseded by the Endless Battle Chain's `RunEnded` game-over screen** (a
  per-`BattleEnded` overlay no longer fits an endless chain); build it there, run-scoped, not per battle
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

> Done items (Architecture Review #1/#2/#4/#5/#6, the #6a lock-in abstraction, the `BattleState` facade
> migration, flaky-test sweep, struct→class, DI, RNG seam, etc.) are in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md).

- [ ] **RNG seam — remaining (Architecture Review #3).** The core library has no direct `Random.Shared`
  (`IRandomSource` threaded through engine + setup). Still open: **`GameController` (web) uses `Random.Shared`**
  for enemy level + random move assignment — deferred; it's the composition root where a per-run seed would be
  injected, but there's no run-seed concept yet (Game Loop). Wire a run seed here when runs exist. *(Optional:
  the `AlwaysHit/AlwaysCrit` rule shims could be replaced by seeded sources — low priority.)*
  - **Also (found 2026-06-09):** `IBattleRules.Roll*Turns` roll from the *rules object's own* RNG
    (`Random.Shared`-backed for the `Gen1BattleRules`/`DelegatingBattleRules` default), **not** the battle's
    injected `IRandomSource`. So a `BattleScenario.Seed(...)` only makes deterministic the rolls
    `ScriptableRules` explicitly pins — any unpinned `Roll*Turns` stays globally nondeterministic and
    test-order-flaky (this surfaced as a Disable flake; worked around by pinning `DisableTurns`). Proper fix:
    thread the battle's `IRandomSource` into the rules' rolls (or give the rules a seedable source).

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
