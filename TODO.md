# Battle Sim – TODO List

> **Active tasks only.** Completed work lives in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only for the
> history of a finished item. **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) ·
> `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions).

## Current state (2026-06-20)

The Gen 1 battle engine is **feature-complete**: all 165 moves, XP & level-up, the Endless Battle Chain, the
Roguelite recovery/encounter layer, the Learnset System, AI move selection, EV / Stat-Exp gain, the full
Evolution System, and the complete **Item System** (data import + use-in-battle, playable end-to-end) are all
done and archived. `ARCHITECTURE.md` and the per-run web seed are done.

**Next up, in order:**
1. **Encounter Logic** — *design DONE* (`ENCOUNTER_DESIGN.md`, 2026-06-27). Next is its **phased build**
   (biome model → enemy tiers → biome graph/`RunDirector` → acquisition channels — see the section below).
2. **Item Acquisition · Bag Persistence · Catch** — the deferred cluster, unblocked by (1)'s acquisition phase.
3. **Game Loop & Progression** — party, switching, save layer (`save.db`).

Lower priority / opportunistic: Web UI polish (move-specific animations), Multi-Generation groundwork, Tech
Debt cleanup, User Documentation.

---

## Encounter Logic  ⟵ the design gate; do BEFORE any acquisition/catch mechanic

> **Why first.** This is a roguelite, not a normal Pokémon game. If the player can acquire *truly random*
> Pokémon the power curve balloons and balance breaks fast. The rules for **what the player faces** and
> **how/whether they can take it** must be designed *before* any acquisition mechanic — otherwise we'd be
> balancing a catch formula against an undefined encounter distribution.

- [x] **`/plan` design pass — DONE (2026-06-27).** Designed in **`ENCOUNTER_DESIGN.md`**. Run = a **graph of
  themed biomes** under a regional origin (Kanto first); biome **type theme** drives the encounter pool which
  *is* the **fought-only** acquisition guardrail. Enemy strength is an **`IEnemyArchetype`** seam
  (Weak/Medium/Strong/Boss) composing existing levers (moveset quality, DV/Stat-Exp, BST band, level), with
  biome **depth** setting the baseline band (restores the never-coded `depth × 10` curve) and tier modulating
  it. Two **gated** acquisition channels: **boss catch** (after a boss, *n%*) + **themed draft** (every 3rd
  encounter, *n%*). Node **bones** for all kinds (battle/elite/shop/mystery/treasure/rest) as `IRunEvent`s,
  sequenced by `chooseNextEvent` (`BattleRunner` → `RunDirector`, per `GAME_LOOP.md §3`).

**Phased build (from `ENCOUNTER_DESIGN.md §7` — implementation, gated on the design above):**
- [ ] **Phase 1 — Biome model + type-filtered pool.** *Design + roster specced* in `ENCOUNTER_DESIGN.md §2`
  (`BiomeDefinition`/`Region` static-registry model, the three membership rules — either-type match,
  Wild-available only, empty biomes don't generate — and the verified **18-biome Kanto roster**). **Remaining =
  implementation:** the static biome registry + a biome-type + Wild predicate on `EncounterSelector.PickByBst`
  (in-theme BST widening), wired through `EncounterFactory.CreateEnemyAsync`.
- [ ] **Phase 2 — `IEnemyArchetype` tiers + depth-scaled bands.** Weak/Medium/Strong/Boss strategies; replace
  the flat `playerBst` draw with a depth-scaled BST + level band that the tier modulates.
- [ ] **Phase 3 — Biome graph + `chooseNextEvent` / `RunDirector`.** Map traversal; node kinds land as
  `IRunEvent` stubs (bones).
- [ ] **Phase 4 — Acquisition channels** (boss catch + themed draft, fought-only). Gates the cluster below;
  *n%* rates tuned here.

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
- **The run bag is a fixed test loadout, not earned** — `EncounterFactory.BuildRunSetupAsync` seeds every
  imported item ×20 (`TestBagQuantityEach`). A stub standing in for a real acquisition source.
- **Poké Balls are imported data only** — mapped to `ItemCategory.Ball`, but `ItemEffects.For(Ball)` returns
  null ⇒ `ItemUseFailed`. The frontend hides Ball & Revive via `bag.ts isUsableInBattle`. `CatchRate` is
  already imported on `PokemonSpecies` ✓.

### 1 — Item acquisition (the design gate) · `/plan`, after Encounter Logic
- [ ] `/plan` the acquisition model: **how** items enter the bag (battle drops? a between-encounter shop?
  curated offers?), at **what rate**, and how it meshes with the difficulty curve. Replaces the fixed
  `TestBagQuantityEach` loadout. Gate amount/rarity so a lucky early haul can't trivialise a run.

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
- [x] **Cross-encounter status persistence** — major status carries across chain encounters (2026-06-10);
  `BattleRunner` snapshots/re-applies via `playerEntryStatus`, `IBattleRules.CarryStatusOutOfBattle` does the
  out-of-battle transform (Gen 1: Toxic→Poison). Volatiles reset per battle (canonical). See `STATE_MODEL.md §2`.

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

Suite landed (16 specs / 8 files, `npm run test:e2e`). Playwright drives the React DOM; the Phaser canvas is
tested through the `mitt` bridge (assert **event ordering**, never wall-clock durations — the #1 flake source).

**Remaining:**
- [ ] **Between-encounter modal E2Es** — deterministic via a fixed `seed` in the `start` request: Poké Center
  recovery Heal/Skip, move-replacement forget/decline, evolution Allow/Cancel (Gen 1 B-cancel). All share the
  blocking-modal shape and are unblocked by the per-run seed; each is unit/integration-covered, this closes the
  DOM-level gap.
- [ ] **CI step** (or `test.ps1 -StartStack`-adjacent) that boots backend + frontend, runs headless, tears down.
- [ ] `data-testid` attributes — **deferred**: specs lean on stable semantic classes (`.btn-new-game`,
  `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`). Add testids only where a class
  proves brittle.
- [ ] §8 visual-regression canvas snapshots — skipped (maintenance cost).

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

> **Code-review pass (2026-06-21)** — a docs-blind, engine-first read. The four items below came out of it.
> None block current work; **(A) is the only correctness item** and is cheap.

- [x] **(A) `MoveSet` cross-thread mutation can throw on the CHECK POKEMON read** — **DONE (2026-06-21).**
  Fixed lock-free copy-on-write: every structural `MoveSet` mutation now swings the reference to a new list
  instead of editing in place — `AddAttack` (`[.. MoveSet, …]`), `ReplaceMove` (copy→set slot→assign),
  `RestoreOriginalIdentity` (`[.. snap.MoveSet]`), and Transform via a new `internal Creature.SetMoveSet`
  (the setter is private and Transform lives in `MoveEffects`). A concurrent CHECK POKEMON reader enumerates
  the prior list safely (one-tick staleness, same model already accepted for `Bag`/scalars). Corrected the
  wrong "scalar fields only" comment in `GetPlayerCreature`. Behaviour-preserving (1081/1081 tests, seam
  review CLEAN). Original analysis kept below for reference.
- [ ] ~~**(A) original analysis**~~ *(code review, 2026-06-21).*
  The battle runs on a background `Task.Run` (`GameSessionManager.AttachConnection`) while the web request thread
  reads the live player via `GetPlayerCreature` → `PlayerOverviewDto.From`, which enumerates `c.MoveSet`
  (`PlayerOverviewDto.cs:50`). `MoveSet` is a plain `List<PokemonAttack>` that the battle thread **structurally
  mutates** — `MoveSet.Clear()`+`AddRange` in `Creature.RestoreOriginalIdentity` (`Creature.cs:259`, also fires
  mid-battle via Haze→`ResetBattleState`), `Clear()`+`Add` in the Transform effect (`MoveEffects.cs:384`),
  `AddAttack` (`Creature.cs:28`), `ReplaceMove` (`Creature.cs:57`). A `Clear`/`AddRange` racing the enumeration
  throws `InvalidOperationException: Collection was modified`. Note the asymmetry: `Bag` already defends this exact
  hazard (it's a `ConcurrentDictionary`, see `Bag.cs:16` + its class comment) — only `MoveSet` was left exposed,
  and the `GetPlayerCreature` comment that claims "the battle thread only mutates scalar stat fields"
  (`GameSessionManager.cs:151`) is factually wrong.
  **Fix (lock-free copy-on-write, mirrors the staleness model already accepted for `Bag`/scalars):** make every
  `MoveSet` mutation swing the reference to a new list instead of mutating in place — `AddAttack` →
  `MoveSet = [.. MoveSet, new(attack)]`, `ReplaceMove` → rebuild+assign, `RestoreOriginalIdentity` →
  `MoveSet = [.. snap.MoveSet]`, Transform → build+assign. Reference assignment is atomic and the property already
  has a private setter, so a concurrent reader enumerates the old list safely (worst case: CHECK shows the
  pre-mutation moveset for one tick — identical to the staleness `Bag` already accepts). Then delete/correct the
  wrong "scalar fields only" comment. ~5 line-level edits; no locks.

- [x] **(B) `AttackAction.ExecuteAsync` is a ~400-line orchestrator** — **DONE (2026-06-21).** Two
  behaviour-preserving extractions roughly halved the method: **(1)** `ResolveDamage(move, category,
  usingStruggle, screenMult)` — the whole `switch (category)` damage block (multi-hit/drain/self-destruct/
  Super Fang/Psywave), returns the accumulated damage; **(2)** `ResolvePreDamageGates(move, category,
  usingStruggle, lockIn, lockCtx)` — the OHKO→accuracy→thaw→type-immunity→crash→Dream-Eater gate sequence,
  returning a `PreDamageGateResult(bool Proceed, bool JustThawed)` (the gate fires its own miss/crash/faint
  side-effects internally). `ExecuteAsync` now reads recharge → lock-in → redirect → gates → resolve-damage →
  post-damage. Two intentional deviations from the suggested signature: dropped the `out bool isCrit` (the
  caller never read it — it stays local to `ResolveDamage`) and added the `usingStruggle` param (the gates and
  multi-hit/Struggle branches need it). Metronome/Mirror-Move + lock-in orchestration left untouched, as the
  note required. Seam review CLEAN, 1081/1081 tests.

- [x] **(C) Comment-density pass — "why, not what"** — **DONE (2026-06-21).** Two parts: first the flagged
  redundancy classes (restatements of an effect's own class `<summary>`/the next statement; the Substitute-shield
  rationale consolidated onto the canonical `_targetShieldedAtImpact` field; "describes other code" phrasings),
  then a full compression pass tightening the padded multi-line blocks down to their essential *why* — same
  discipline applied repo-wide, not just the two named files. Touched `AttackAction`, `MoveEffects`, the one
  bloated `IBattleRules` doc (`PureStatusMoveChecksTypeImmunity`), `LockInMechanics` (Binding), and the
  `MoveImport` `past_values` block. **Net ≈ −60 comment lines, zero information loss** — every Gen 1 quirk,
  formula, cross-gen note and seam-justification preserved (seam-reviewer verified fact-by-fact). The seam
  contract's per-member Gen 1/Gen 2+ tables (`IBattleRules`/`Gen1BattleRules`), the `BattleState`/`BattleEvents`
  field-semantics docs, and the `MoveImport` `// Gen 1: X (modern: Y)` provenance comments were deliberately
  left intact — they *are* the institutional knowledge the item says to keep, so compressing them would be
  net-negative. Comments-only; build + 1081/1081 unchanged.

- [x] **(D) Minor batch** — **DONE (2026-06-22).** (i) Fixed `respondEvolution`'s copy-pasted comment in
  `useBattleHub.ts` (it described the Poké Center recovery offer — the *next* handler — instead of the evolution
  Allow/Cancel prompt). (ii) Dropped the dead `PendingSession.Seed` field: the run threads `Rng` and the seed is
  logged + returned straight from `GameController`'s local, so the field was never read — removed it plus the
  `RegisterSession(int seed)` param and its call-site arg, and corrected the prose `session.Seed` reference.
  (iii) *(context, not a task)* The gitignored `*.db` concern stays a non-issue — the data-contract tests
  (`SecondaryChanceDataContractTests`) hardcode independent Gen 1 values and validate the imported rows, so the
  fidelity truth lives in committed source + tests, not the derived `.db`. No action. Build + 1081/1081 unchanged.

- [ ] **`bag.ts` re-encodes the engine's effect registry** *(new, 2026-06-20 architecture pass).* The frontend
  `USABLE_CATEGORIES` set in `bag.ts:20` hardcodes which `ItemCategory`s are usable in battle — knowledge the
  backend already owns (`ItemEffects.For(category) != null`). When Ball/Revive get effects, **two places must
  change in lockstep** or the menu silently hides a now-usable item. Fix mirrors the `RestoresPpAllMoves`
  precedent: project a server-computed `usableInBattle` boolean onto `BagItemView` (from the registry) and have
  the client filter on that flag. Single source of truth; same field-projection discipline as the rest of the
  wire. Low risk today (documented), but a drift seam to close before the acquisition cluster lands.

- [x] **RNG seam — CLOSED, nothing left to do (2026-06-21).** The per-run web seed, rules-RNG seeding, and the
  engine `IRandomSource` thread are all done/archived. The lone remaining "optional" idea — replace the
  `AlwaysHit`/`AlwaysCrit` rule shims with seeded `IRandomSource`s — was evaluated and **deliberately declined**:
  it would be a regression, not a win. Those shims are test doubles that override the *seam members themselves*
  (`GetHitThreshold => 256`, `GetCritChance => 1.0`) — the correct, fidelity-clean way to force an outcome. A
  seed can't replace them cleanly because the draws aren't isolated: the accuracy roll shares `AttackAction._rng`
  with the secondary-effect roll / Metronome pick, and crit (`NextDouble()` on that stream) vs. max variance
  (`Next(217,256)` on `Gen1BattleRules`' *separate* inner `_rng`) can't both be forced from one seed. Replacing a
  one-line rule override with per-draw rigged sources coupled to call order is strictly more fragile for zero
  fidelity gain. **Do not re-file** this, "web composition root builds runs unseeded", or "Roll* draws ignore the
  battle seed" — all closed.

- **Architecture Review #7 — "Minor cleanups" — essentially DONE (2026-06-20).** None were correctness bugs;
  the goal was keeping complexity-concentrating files change-safe as Gen 2 lands.
  - [x] Deduped the repeated `_rng.Next(1, 101)` secondary-roll idiom behind a new
    `IBattleRules.SecondaryHits(chance, rng)` seam member (the 1–100 roll is a Gen-1 modelling choice, so it
    belongs on the seam). All four call sites (`AttackAction` status/stat, `MoveEffects` flinch/confuse) route
    through it; the rng is passed in to preserve each site's exact stream (behaviour-preserving).
  - [x] Split `MoveImport.MapToAttack` into focused methods (`BuildGen1Attack` / `ApplyDamageCategory` /
    `ApplyStatStageEffect` / `ApplySpecialEffects` / `ApplyGen1Corrections`) and replaced the magic move IDs
    (120/153/69/101/162/149/49/82/129) with named constants. The audit surfaced that `MapToAttack` had **no
    direct test** (only live-`moves.db` contract tests, which need a re-import to catch a mapping regression),
    so made it `public` (a pure DTO→model fn, like `EvolutionMapper`/`ItemMapper`) and added `MoveMappingTests`
    (15 cases, one per concern) — closes the gap and verifies the refactor without a network re-import.
  - [~] The legacy `out`-less `DamageCalculator.CalculateDamage` overload was **kept, not dropped** — a
    deliberate call. It's a legitimate test-only convenience used by 15 damage-only asserts; deleting it adds
    `Gen1BattleRules.Instance, out _` noise to all 15 for zero production benefit (it's a 15-line static helper,
    not a complexity-concentrating file). The actual smell — a misleading "backward-compatible" comment implying
    external consumers — was fixed instead. Re-open only if we want all damage calls on one signature.

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
