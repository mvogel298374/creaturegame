# Battle Sim – TODO List

> **Active tasks only.** Completed work lives in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only for the
> history of a finished item. **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) ·
> `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions).

## Current state (2026-07-02)

The Gen 1 battle engine is **feature-complete**: all 165 moves, XP & level-up, the Endless Battle Chain, the
Roguelite recovery/encounter layer, the Learnset System, AI move selection, EV / Stat-Exp gain, the full
Evolution System, and the complete **Item System** (data import + use-in-battle, playable end-to-end) are all
done and archived. The **Encounter Logic** run layer (biome graph + node kinds) and the **Run Economy** (gold,
battle/Treasure/Mystery rewards, earned transient bag, gold HUD + reward modal) are done and archived.
`ARCHITECTURE.md` and the per-run web seed are done.

**Next up, in order:**
0. **Run Economy — gold, item rewards, transient bag & Treasure/Mystery nodes** — *Phases A + B + C DONE
   (2026-07-02; A/B audited PASS-WITH-ADVISORIES).* Backend currency + battle/Treasure/Mystery reward rolls +
   earned transient bag + the reward wire, **plus the Phase C frontend** (gold HUD, reward modal, `acknowledgeReward`
   wiring). The web-path node-plan gate is **removed** — Treasure/Mystery run at full distribution and the client
   answers their reward ack. Playable end-to-end. **Remaining follow-up: the Shop node** (spend-gold purchase
   modal — see the *Item Acquisition* cluster / its own item below).
1. **Encounter Logic** — *design DONE* (`ENCOUNTER_DESIGN.md`, 2026-06-27). Phased build: **Phase 1 (biome
   model)** ✅ + **Phase 2 (enemy tiers + depth bands)** ✅ + **Phase 3a (event model / `RunDirector`)** ✅ +
   **Phase 3b (biome graph + map screen)** ✅ + **Phase 3c (node-kind bones + tuned curve)** ✅ done — biome
   runs route through varied nodes (wild/elite/boss/shop/treasure/mystery) on a battle-heavy tuned distribution,
   each biome capped by a Boss apex, foes scaled by biome-position depth, **plus per-run biome-map randomisation**
   (each run draws a seeded connected subset of Kanto's biomes). **Phase 3 (Encounter Logic) is complete. Next:
   Phase 4 — Acquisition channels** (boss catch + themed draft, fought-only). **Run model
   (confirmed with the user):** region (Kanto) → player chooses a biome → a **randomised 4–6 themed events**
   capped by a Poké Center → choose the next biome (its neighbours) → repeat until death.
2. **Item Acquisition · Bag Persistence · Catch** — the deferred cluster, unblocked by (1)'s acquisition phase.
3. **Game Loop & Progression** — party, switching, save layer (`save.db`).

Lower priority / opportunistic: Web UI polish (move-specific animations), Multi-Generation groundwork, Tech
Debt cleanup, User Documentation.

---

## Encounter Logic — Phase 4 (the only open piece)

Phases 1–3 (biome model + type-filtered pool, `IEnemyArchetype` tiers + depth bands, `RunDirector` event model
+ live biome-graph map + tuned Boss-capped node curve) are **done and archived**, along with the four follow-on
refinements — per-run biome-map randomisation, randomised 4–6 route length, Roar/Whirlwind→`ForceFlee`, and the
opening-route favourable-matchup guarantee. Full per-phase record (design, pins, seam reviews) in
[`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) → *Encounter Logic*.

- [ ] **Phase 4 — Acquisition channels** (boss catch + themed draft, fought-only) — the remaining
  `ENCOUNTER_DESIGN.md §4` piece, and the bridge into the *Item Acquisition · Bag Persistence · Catch* cluster
  below. Now unblocked (the §1–§3 layer is done): a biome's **Boss** node is the catch hook, the **fought-only
  themed pool** is the draft source. `/plan` first; *n%* rates tuned here.

---

## Reward Visibility & XP Pacing  ✅ DONE (2026-07-05, pending gates + commit)

Compelling-rewards pass — boost reward *amount* and *visibility*:

- **XP boost (soft level-aware curve).** New **`RunRules`** — a roguelite "game-balance dials" bag kept
  **separate from the Gen-1 `IBattleRules` seam** (which stays untouched) — carries a level-aware XP curve,
  threaded `GameSessionManager → RunDirector → BattleRunEvent → Battle` and applied to the pure Gen-1 award
  (`floor(baseExp × level / 7)`) at faint time. `RunRules.Default` is a 1.0 no-op (all existing callers/tests =
  pure Gen 1); the web run passes a linear ramp `XpMultiplierEarly = 1.5` (L1) → `XpMultiplierLate = 4.5`
  (L100). Low levels (already fast) get a light nudge — no sharp multi-level jumps — while the high-level grind
  gets the bigger lift, ~3× (2.98×) around the default L50. Design target (from the brief): a biome (~4–6
  encounters) ≈ **0.8–1.5 levels** across the picker's 5–100 range — a playtest-validated tuning goal, not a
  tested invariant (`RunRulesTests` pins the curve *shape*, not the pacing outcome). The two anchors are the
  tuning dials (slider-ready); provisional. Elite/Boss (trainer-analog tiers) get the **Gen-1 trainer ×1.5** XP
  bonus (user-approved) — applied in the seam (`CalculateXpAwarded(…, trainerOwned)`), separate from the curve,
  wired by tier in `BattleRunEvent`. That ×1.5 *stacks* on the curve for the (higher-XP) Elite/Boss nodes, so a
  typical biome trends to the upper end of / slightly above the 0.8–1.5 band — intended (beefier bosses), retune
  the anchors down if playtest wants it back in-band. See `GENERATION_SEAMS.md`.
- **Drop hover.** Battle-win drops (the `Battle`-source `RewardGranted`) now raise a transient floating loot
  toast (gold + items) over the field for ~2.8 s (`DROP_TOAST_MS`) — inline & non-blocking, `pointer-events:
  none`, auto-dismissed by the view — in addition to the existing gold-HUD bump + battle-chat line. Reuses the
  existing `RewardGranted` fields (no new wire projection). Treasure/Mystery keep their blocking modal.

---

## Run Economy — Gold, Item Rewards, Transient Bag & Treasure/Mystery Nodes  ✅ DONE (2026-07-02)

Phases **A** (core, generation-agnostic) + **B** (web-layer reward policy) + **C** (frontend gold HUD + reward
modal) are done — currency, battle-win + Treasure/Mystery reward rolls, an earned transient bag, playable
end-to-end. Commits `ea41531` (A/B, audited **PASS-WITH-ADVISORIES**) + `7d9afc5` (C). 1267 tests green.
**Full record → [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) → *Run Economy*.**

**Remaining follow-up — the Shop node** (the one deferred piece): a between-encounter shop that **spends** the
transient `Wallet` (`Wallet.TrySpend` is built and waiting). Needs a shop inventory + pricing + a purchase modal;
`GameSessionManager` currently routes Shop nodes to the no-op `InteractionStubEvent` (banner + advance). `/plan`
the inventory/pricing/purchase UX before building.

---

## Item Acquisition · Bag Persistence · Catch  ⟵ deferred cluster, gated on Encounter Logic

**One interlocked cluster, deliberately deferred together** — each depends on the previous and on the
Encounter Logic gate:
- **Acquisition** can't be designed until the encounter / eligibility model exists (drop rates are meaningless
  against an undefined distribution).
- **Bag persistence** is meaningless until acquisition defines *what's* in the bag and *when* it's earned.
- **Catch** is just one acquisition channel, and a random high-BST catch is the canonical balance hazard.

> **"Catch" is likely a misnomer.** The player may receive Pokémon several ways — in-battle capture,
> post-battle rewards, gifts/offers, picking from a curated set. Treat this as a broader **acquisition** layer
> when designed; in-battle "catch" is one channel, not the whole feature.

### Current state — built vs. stubbed (code anchors)
- **Bag is transient** — `Items/Bag.cs` is in-memory `id → qty`, reseeded every run, never saved. Per-run:
  consumed items stay gone; the Poké Center refills HP/PP/status, not the bag.
- **Item acquisition (the item side) is now DONE** — the **Run Economy** replaced the old ×20 test loadout:
  `EncounterFactory.BuildStartingBag` seeds a curated modest start and battle-win + Treasure/Mystery drops grow
  it (web-layer `RewardCalculator` policy). So *item* acquisition is solved; **bag persistence** and **catch**
  (below) are the remaining, still-deferred pieces of this cluster.
- **Poké Balls are imported data only** — mapped to `ItemCategory.Ball`, but `ItemEffects.For(Ball)` returns
  null ⇒ `ItemUseFailed`. The frontend hides Ball & Revive via `bag.ts isUsableInBattle`. `CatchRate` is
  already imported on `PokemonSpecies` ✓.

### 1 — Item acquisition (the design gate) · ✅ DONE via Run Economy
- [x] The item-acquisition model is the **Run Economy** (see archive): battle-win drops + Treasure/Mystery
  rewards, gated by the web-layer `RewardCalculator` (skewed rates so a lucky early haul can't trivialise a run),
  replacing the fixed loadout. *(A between-encounter **Shop** — spending gold — is the remaining follow-up.)*

### 2 — Bag persistence · once acquisition defines what a bag holds
- [ ] Persist the `Bag` to `save.db` / `PlayerDbContext` (rides on the broader save layer — see **Game Loop**).
- [ ] Decide bag scope: **per-run** (lost on death) vs. **meta-progression** (carries across runs). The
  acquisition design drives this.

### 3 — Catch / Poké Ball effect (one acquisition channel) · Gen 1 reference
- [ ] `BallItemEffect : IItemEffect` for `ItemCategory.Ball`, registered in `ItemEffects.All`; extend `Battle`
  with a "catching" state/outcome.
- [ ] Gen 1 formula: `floor((MaxHP × 3 − HP × 2) × CatchRate / (MaxHP × 3))` vs a 0–255 roll (per-ball modifier
  lives in the formula, not the `Item` row).
- [ ] `CaptureAttempted(string TargetName, bool Caught)` event; `BattleEnded` variant `reason: "Caught"`.
- [ ] Caught creature → party (needs party / switching — see **Game Loop**); closes the acquisition loop.
- [ ] Unlocks the dormant **stone evolutions** (`Stone` trigger + `IEvolutionRules.StoneUsed` are built and
  waiting on a bag).
- [ ] Phaser throw / shake / catch animation.

> **Revive / Max Revive** (the only remaining in-scope item effect) is also blocked here — it needs a
> fainted-but-revivable party member, which the single-creature chain doesn't have. `ItemEffects.For(Revive)`
> stays null until Game Loop adds a party.

---

## Game Loop & Progression

**Prerequisites:** Catch Mechanic, `PlayerDbContext` / `save.db`. Intentionally deferred until combat fidelity
is fully ironed out (the battle sim is the foundation). The **Endless Battle Chain** (done) is the first minimal
slice; the items below are what it deliberately leaves out.

- [ ] Catch → Pokémon added to party (up to 6); choose lead between battles.
- [ ] Progressive difficulty beyond the current `targetBst = lead BST + depth × 10`; trainer encounters at
  milestones.
- [ ] `PlayerSave` / `SavedCreature` models in `save.db`; auto-save after each battle; party-management UI.
- [ ] **Stone evolutions** — the only remaining evolution piece, gated on the bag (Catch). The `Stone` trigger
  + `IEvolutionRules.StoneUsed` are built and dormant.
- [x] **Cross-encounter status persistence** — DONE (2026-06-10); major status carries across chain encounters,
  volatiles reset per battle. See `STATE_MODEL.md §2` and `TODO_ARCHIVE.md`.

---

## Web UI — Polish

Stack: React 18 + TypeScript + SignalR + Phaser 3. (Canvas & core animations done — see archive.)

- [ ] **Move-specific attack animations (grouped, not per-move).** Today every move plays the one generic lunge
  + type-neutral white tint + `playDamageShake`. Map each move to one of ≈5–7 **animation families** keyed off
  data we already have (`DamageType`, `AttackType`) + a few special cases — believable variety without 165
  bespoke clips.
  - **Families:** *physical contact* (current lunge, keep) · *projectile/ranged special* (sprite travels
    attacker→target, no lunge) · *status/self-buff* (glow/pulse on user, no lunge) · *two-turn/charge* (charge
    glow turn 1, release burst turn 2) · *multi-hit/flurry* (repeat a jab in step with `MultiHitCompleted`).
    Cheap layered win: tint the flash/shake by the move's **type colour** (reuse the `TypeBadge` palette).
  - **Plumbing (the real work, mind the seam):** `MoveUsed` carries only `(AttackerName, MoveName)` — the client
    can't see the *enemy's* move type/category. Project `DamageType` + `AttackType` onto `MoveUsed` + its
    `SignalRBattleEventEmitter` mapping with the field-level guard (the recurring **web event field-projection
    gap** — see the memory + `WebEventContractTests`). Then a pure `moveAnimationFamily(type, category, slug)`
    map (unit-testable like `timeline.ts`), new per-family `BridgeCommand`s + `BattleScene` handlers, each still
    emitting `animationComplete` so the timeline's `awaitAnim` contract holds.
- [ ] `ConsoleInput : IBattleInput` — numbered move menu for terminal play (low priority).

---

## Browser-Based UI Testing (Playwright)

Suite lives in `ClientApp/e2e/` (`npm run test:e2e`). Playwright drives the React DOM; the Phaser canvas is
tested through the `mitt` bridge (assert **event ordering**, never wall-clock durations — the #1 flake source).

**Done (2026-07-05):**
- [x] **Seed plumbing** — `StarterSelection` forwards an optional `?seed=<int>` URL param into the `/start`
  request (backend already accepted `Seed`), so an E2E can pin a fully deterministic run. `?e2e=1` still sets
  test mode. react-router drops the query on nav from the title, so seeded specs land directly on `/select?seed=`.
- [x] **Run Economy reward-modal E2E** (`reward-modal.spec.ts`) — seed 31 / CHARIZARD @ L50 lays the first
  biome node as a **Treasure**, so the modal fires right after the opening route pick (no battle to win). Asserts
  the modal + title, a gold line (`+N₽`) + item line, the **gold HUD credit** (was `₽0`), and OK →
  `acknowledgeReward` → modal closes + run continues into the next node. **Closes the known live-verification
  gap** — the reward modal + gold credit are now observed in a browser, not just unit/integration.
- [x] **E2E harness recovered from spec-rot** — the suite was fully red: **biome mode (Phase 3b-2)** added an
  opening route-choice modal that blocked before every battle (the `startBattle` helper didn't answer it), and
  the **Run Economy** starting bag stopped seeding `BattleStatBoost` items. Fixed `startBattle` to pick the
  opening biome (`chooseBiomeIfPresent`); fixed `battle.spec` (the first log line is now the biome banner, not
  the VS line); removed the two `item-use` specs (X ATTACK / GUARD SPEC aren't battle-1 obtainable anymore — the
  item-effect logic stays covered by `ItemEffectTests`, bag grouping by `bag.test.ts`).

**Remaining (in priority order):**
- [ ] **⏭ NEXT — Stabilise inter-test E2E flakiness (a seed-determinism pass).** `stat-stage`, `level-up`
  (column-spacing), and `battle-ui-cues` pass **run alone** but flake **in-suite** — the specs share one
  stateful backend (serial, "one in-flight battle per connection") and biome mode's extra async step widened the
  timing windows. Not a product bug; a test-determinism gap. Approach (leverages the new seed plumbing):
  1. Add optional `seed` (and species/level) params to the `startBattle` helper; when a seed is given it lands
     directly on `/select?e2e=1&seed=…` (the `reward-modal.spec` pattern) so the whole run — enemy, biome offer,
     battle rolls — is deterministic.
  2. Per flaky spec, discover a stable seed that yields the matchup it needs (player moves first / a survivable
     turn 1 / the right enemy type), the way `reward-modal.spec` pins seed 31.
  3. Replace the coin-flip `reachLog` / retry loops in those specs with the seeded setup.
  **Done when:** `npm run test:e2e` is green across 3 consecutive runs with `retries: 0`.
- [ ] **Other between-encounter modal E2Es** — same seeded/blocking-modal shape as the reward modal, now
  unblocked by the seed plumbing: Poké Center recovery Heal/Skip, move-replacement forget/decline, evolution
  Allow/Cancel (Gen 1 B-cancel).
- [ ] **CI step** (or `test.ps1 -StartStack`-adjacent) that boots backend + frontend, runs headless, tears down.
  **This is the root cause of the rot going unnoticed** — E2E isn't gated in CI and `test.ps1` skips it when the
  stack is down, so a red suite stayed invisible. Wiring E2E into the gate is what prevents a repeat.
- [ ] `data-testid` attributes — **deferred**: specs lean on stable semantic classes (`.btn-new-game`,
  `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`). Add testids only where a class
  proves brittle.
- [ ] §8 visual-regression canvas snapshots — skipped (maintenance cost).

## Frontend Unit Coverage (Vitest)

Test-harness audit (2026-07-05) — the .NET engine + event-wire seam are near-exhaustively covered; the gap was
the frontend. Closed the pure-logic gaps and pinned the suite split.

**Done (2026-07-05):** extracted the pure `battleReducer` out of `useBattleHub` (`hooks/battleReducer.ts`,
type-only imports → zero runtime deps) and added `battleReducer.test.ts` — the edge transitions a live
playthrough can't deterministically force (name-mismatch HP/status no-ops, `XP_GAIN` clamp, the level-up→
move-replacement supersede, the `BATTLE_STARTED` enemy-nameplate reset, biome-choice which has no E2E spec).
Plus `format`/`fetchError` unit tests (the backend-unreachable path is invisible to E2E). 84 → 107 Vitest tests.

**The suite-split rule (so future tests land in the right place):** Vitest owns **pure decision logic**
(input → exact output, especially branches E2E can't force or that an assembled-state test hits trivially).
Playwright owns anything needing the **full stack or the DOM** (rendering, flows, modal gating, event/animation
ordering). *Do not* add a second DOM harness (`jsdom`/RTL) to re-assert what E2E already renders — the one real
component-gating gap (the Run Economy reward modal) is closed by a **seeded Playwright spec** (see Browser-Based
UI Testing above), not RTL.

**Open (opt-in, low urgency):**
- [ ] **`GameSessionManager` connection lifecycle** — reconnect rebind, abandon grace, pending-session eviction
  TTL, and the run-loop `Task.Run` are covered by *neither* suite (they're entangled with `IHubContext` +
  `Task.Run` + wall-clock timers). Regression-insurance only: the reconnect behaviour is a settled/validated
  edge, not a suspected bug. Would need an injectable clock to unit-test the timing without real delays.

---

## Multi-Generation: Data Model & Schema

Deferred to the Gen 2 sprint. (The stat-selection abstraction — the only piece to do now — is done.)

- [ ] **`Attributes` Special split:** `Special` → `SpAtk` + `SpDef` (keep `Special` as a Gen 1 computed alias);
  `Creature.BaseSpecial`/`DvSpecial`/`ExpSpecial` split in parallel.
- [ ] **`PokemonSpecies` per-generation schema:** separate timeless identity (`Id`, `Name`, `CatchRate`,
  `BaseExperience`, `PokedexEntry`, `GrowthRate`) from a new `PokemonSpeciesGenData` table (`SpeciesId`,
  `Generation`, types, base stats; Gen 3+ adds abilities). Importer stores one row per species per generation;
  engine queries by active generation. *(PokeAPI has no `past_stats` — Gen 1 stat corrections need a
  corrections table or separate source.)*
- [ ] **Move per-generation data:** a generalisation, not a rewrite — resolve a field for gen *G* as the
  earliest `past_values` entry whose version-group generation is **> G**, else the current value ("earliest =
  Gen 1" is the *G=1* case). Store one `Attack` row per `(moveId, generation)` (mirror the learnset model) or
  resolve on demand; make the layer-2 override table per-generation too. Keep mechanic/formula differences on
  the **seams**, never in per-gen move data.
- [ ] **Generation filtering:** `Attack.GenerationIntroduced` + `PokemonSpecies.GenerationIntroduced` (set on
  import); `EncounterSelector.PickByBst` / `BuildCreature` filter by `<= activeGeneration`;
  `GetSpeciesForGenerationAsync(int)` / `GetMovesForGenerationAsync(int)` replace the unfiltered `ToListAsync()`.

---

## User Documentation

Battles are fully playable now — docs won't describe a moving target.

- [ ] `/help` route or modal — starter selection, battle controls, status icons, level picker.
- [ ] Expand `README.md` — architecture decisions (two-DB model, `IBattleRules` pattern, how to add a move
  effect / a generation).
- [ ] `GEN_DIFFERENCES.md` (written) — adapt into a player-facing "what makes Gen 1 different" explainer.

---

## Tech Debt / Cleanup

**Done & archived** (2026-06-20 → 22 code-review + Architecture Review #7 pass — full write-ups in
[`TODO_ARCHIVE.md`](TODO_ARCHIVE.md)): (A) `MoveSet` cross-thread mutation → lock-free copy-on-write;
(B) `AttackAction.ExecuteAsync` split into `ResolveDamage` + `ResolvePreDamageGates`; (C) repo-wide
comment-density pass; (D) minor comment/dead-field batch; the **RNG seam** (CLOSED — do not re-file the
`AlwaysHit`/`AlwaysCrit` shim idea, the unseeded-web-composition-root, or "Roll\* ignores the battle seed");
and Architecture Review #7 (`SecondaryHits` seam dedup, `MoveImport.MapToAttack` split + `MoveMappingTests`).

**Still open:**

*(none — the `bag.ts` effect-registry drift seam below is now closed.)*

**Done & archived:**
- [x] **`bag.ts` re-encodes the engine's effect registry** — CLOSED (2026-07-04). The frontend
  `USABLE_CATEGORIES` set (which hardcoded which `ItemCategory`s are usable in battle) is gone; the backend now
  projects a server-computed `UsableInBattle` boolean onto `BagItemView` (from `ItemEffects.For(category)`), and
  the client filters the bag menu on that flag. Single source of truth — when Ball/Revive get effects, only the
  registry changes and the menu follows. Mirrors the `RestoresPpAllMoves` field-projection precedent.

### Known Gaps
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector exists.
- Enemy Pokémon do not evolve — wire into level-up when Game Loop is built.
- **Endless-chain double-faint** — tested (2026-06-12): a mutual end-of-turn DoT double-faint counts as a loss,
  pinned by `BattleRunnerTests.Runner_DoubleFaintFromEndOfTurnPoison_CountsAsLoss_NotAWin`.

---

## Database Architecture (reference)

**Two-database model:**
- `pokemon.db` / `PokemonDbContext` — species, base stats, types, growth/catch rates, learnsets, game
  availability, evolution chains.
- `moves.db` / `MovesDbContext` — moves, damage type, accuracy, PP, stat/status effects.
- `items.db` / `ItemsDbContext` — battle-usable items (Gen 1 roster + gameplay numbers).

**Where new tables go:** Pokémon-world data (egg groups, …) → `pokemon.db`; move-world data → `moves.db`; item
data → `items.db`; player save state (party, caught Pokémon, bag) → `save.db` / `PlayerDbContext` (deferred
until Catch).
