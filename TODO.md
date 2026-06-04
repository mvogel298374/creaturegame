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

**Experience, Levelling & Level Picker** — Gen 1 wild XP formula; `LeveledUp` event; level slider in UI (5–100); `GainExperience → LevelUp` path. *(Core mechanic only — XP is awarded and the player levels up at the moment of victory, recalculating stats. The on-screen XP bar is still cosmetic and there's no level-up move learning; see "XP & Level-Up — finish the in-battle loop" below.)*

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

Creatures used to receive 4 random moves from the full move pool (a Bulbasaur could roll
Hydro Pump). Learnsets ensure Pokémon only know moves they can actually learn at their level.

**Prerequisite:** Experience, Levelling & Level Picker ✅

### Initial moveset from learnsets ✅ DONE (2026-06-02)

Generation separation: learnsets are **data**, not a battle rule, so no new seam. The Gen 1
decision (filter to `red-blue` level-up moves) is isolated in the importer & commented (like
`Gen1TypeSlots`); rows are tagged with a `Generation` column; runtime filters by a single
`GameController.ActiveGeneration` constant — no generation branching in logic.

- [x] `PokemonLearnset` model (`Id`, `SpeciesId` FK→`PokemonSpecies`, `MoveId` logical
  cross-DB ref to `moves.db`, `LearnLevel`, `Generation`) + index `(SpeciesId, Generation,
  LearnLevel)`; `AddPokemonLearnset` migration on `PokemonDbContext`. Lives in `pokemon.db`.
- [x] Import: `LearnsetMapper.ExtractGen1Learnset` (pure, testable) filters the already-fetched
  `/pokemon/{id}` moves array to `red-blue` + `level-up`, parses MoveId from the URL, keeps
  `MoveId <= 165`, lowest level on repeats; `PokemonImport.ImportLearnset` persists idempotently
  (clear-then-insert). Re-imported → **989 rows across all 151 species** (verified via MCP).
- [x] `LearnsetMoveSelector.Select(strategy, …)` (core, gen-agnostic, `IRandomSource`-seamed):
  - **`CanonicalLatest`** (player) — deterministic, the 4 highest-level moves ≤ level.
  - **`WeightedSmart`** (enemy) — semi-random, semi-intelligent: weight = power (or flat 60 for
    Fixed/OHKO/etc., 35 for status) × 1.5 STAB × recency nudge; **always force-picks the top
    damaging move** (never all-status), fills the rest by weighted draw without replacement so
    same-species/level enemies vary. Deliberate precursor to the planned `IMoveEvaluator`.
- [x] Wired into `GameController.BuildCreature` (player = Canonical, enemy = WeightedSmart),
  replacing the random-4 block; graceful fallback to random if a species has no learnset rows.
- [x] Tests (18 new, 156 total): `LearnsetImportTests` (filter/range/dedup/order, ×5),
  `LearnsetMoveSelectorTests` (canonical, level-gating, ≤4 returns-all, always-damaging, seeded
  determinism, statistical STAB/power bias, ×7), `MigrationTests` learnset schema + round-trip (×2),
  `LearnsetIntegrationTests` (DB round-trip → EF query → selection: canonical legality, low-level
  gating, **generation filter isolates gens**, WeightedSmart legal + always-attack, ×4).
- [x] E2E: committed Playwright spec `e2e/learnset.spec.ts` — Bulbasaur@50 move menu equals the
  canonical 4 (RAZOR LEAF/GROWTH/SLEEP POWDER/SOLAR BEAM); also verified live via Puppeteer
  (enemy Paras used SCRATCH — legal, attacking — battle resolves).

### Level-up move learning — DEFERRED (blocked by "XP & Level-Up — finish the in-battle loop")

Only the **player** ever levels up; the enemy never gains XP, so its moveset is fully settled
at build time (above). This interactive path is **blocked by the XP & Level-Up section below** —
level-up move learning has no place to surface until the player can actually see and follow a
level-up during play. Build that first, then this.
- [ ] `Creature.LevelUp()` checks learnset for moves at the new level
- [ ] Slot free → add automatically; emit `MoveLearned(string CreatureName, string MoveName)`
- [ ] Slots full → emit `MoveReplacementRequired(…)` — blocking event; backend waits on an
  `IBattleInput`-style TCS (Battle must drive level-ups one at a time to interleave the prompt)
- [ ] `BattleHub` + `SignalRInput` extended with `ForgetMove(int slotIndex)` / `SkipNewMove()` path
- [ ] `MoveLearned` / `MoveReplacementRequired` handled by all emitters + `useBattleHub.ts` (+ React modal)
- [ ] **XP bar:** `TurnStarted` carries `PlayerExperience` / `XpToNextLevel`; `useBattleHub.ts`
  dispatches so the bar fills live
- [ ] Tests: `Learnset_LevelUp_AddsNewMoveWhenSlotAvailable`,
  `Learnset_LevelUp_EmitsMoveReplacementRequired_WhenFull`

---

## XP & Level-Up — finish the in-battle loop

**Slated: next combat-fidelity item after the Learnset System** (small, no new data/schema).
This is the concrete "set up XP/level-up properly" work that the Learnset level-up section is
blocked on. It does **not** require the Game Loop — it polishes the single-battle path that
already exists.

**What works today:** XP is awarded on enemy faint (`Battle.StartFightAsync` → `CalculateXpAwarded`
→ `GainExperience`), the player levels up (chained `LeveledUp` events, one per level), stats
recalc and HP heals the delta. The frontend animates an XP-bar fill on `LeveledUp`.

**What's missing / not "proper":**
- [ ] **Live XP data to the client.** `TurnStarted` carries no XP, so the bar fills to a hardcoded
  placeholder (`playerXpToNext = 100`) instead of real progress. Add `PlayerExperience`,
  `PlayerXpThisLevel`, `XpToNextLevel` (derived from `Creature.Experience` and
  `CalculateExperienceForLevel`) to `TurnStarted` (or a small dedicated event); `useBattleHub.ts`
  dispatches them into `playerXp` / `playerXpToNext` so the bar reflects actual XP.
- [ ] **XP-gain animation.** On win, animate the bar filling by the XP earned *before* the
  level-up fill/reset, so the gain reads correctly (today it only fills on `LeveledUp`).
- [ ] **Verify the multi-level path end to end** — a big XP award crossing several levels emits
  N `LeveledUp` events and the bar steps through each (the backend loop already emits per level;
  confirm the timeline plays them in order).
- [ ] **Surface the level-up moment** clearly in the log/UI (the hook the deferred move-learning
  prompt will attach to).

**Tests:**
- [ ] Backend: `TurnStarted` (or the new event) carries correct `XpToNextLevel` / current XP for a
  known species+level (unit/integration).
- [ ] Backend: an XP award spanning multiple levels emits the right sequence of `LeveledUp` events.
- [ ] E2E: §7 — XP bar fills and the "grew to level N!" line appears on a win (currently unasserted;
  see Browser-Based UI Testing §7).

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
- [x] `RandomMoveInput : IBattleInput` — uniform pick among PP-available moves, `IRandomSource`-seamed (`Combat/RandomMoveInput.cs`)
- [ ] `GreedyAIInput : IBattleInput`
- [ ] `WeightedAIInput : IBattleInput`
- [x] Wire `RandomMoveInput` as default enemy input in `GameSessionManager` (replaced `AutoSelectInput`) — fixed the "enemy only ever uses its slot-0 status move" bug (see Fixed below). The evaluator-driven tiers above are still pending.

---

## Gen 1 Attack Behavior Coverage (batched)

Prove **every Gen 1 attack does what it sets out to do** when given to a Pokémon and used in
battle, in **batches of 10 moves**, via parametrized "effect contract" tests (`[Theory]` +
`[InlineData]` — a shared effect like "deals damage" is written once and run over every move
that has it). Real move rows come from the live `moves.db` (`MovesFixture`); the
`MoveScenario` harness gives the move to a creature and runs one `AttackAction`.

### Batch 1 (moves 1–10) ✅ DONE (2026-06-03)
pound, karate-chop, double-slap, comet-punch, mega-punch, pay-day, fire-punch, ice-punch,
thunder-punch, scratch. **+49 test cases (228 total).**
- Harness built once for all batches: `TestSupport/MovesFixture` (live DB loader),
  `MoveScenario`/`TestCreatures`, shared `RecordingEmitter` (deduped the 3 copies), and the
  deterministic rules doubles (`NeverHitRules`, `ForceSecondaryRules`, `NoVarianceNoCritHitRules`,
  `FixedMultiHitRules`) on `DelegatingBattleRules`.
- Contracts: damage, PP decrement, accuracy/miss, secondary status (burn/freeze/paralysis incl.
  miss + already-statused), Gen-1 special-by-type (the punches are Special), high-crit rate,
  STAB ~1.5×, type-effectiveness scaling, multi-hit count, Pay Day coins.
- **Two engine features implemented** (both behind the gen seam per `GENERATION_SEAMS.md §5.0`):
  - **Multi-hit (2–5)** — `MoveEffect.MultiHit`, `IBattleRules.RollMultiHitCount` (Gen 1 weighted
    2/3 = 3/8, 4/5 = 1/8), per-hit crit/variance, stop-on-faint, `MultiHitCompleted` event +
    "Hit N times!" line. Maps double-slap/comet-punch/fury-attack/pin-missile/barrage/
    fury-swipes/spike-cannon. Verified live (Clefairy Double Slap → "Hit 2 times!").
  - **Pay Day** — `MoveEffect.PayDay`, `IBattleRules.PayDayCoinMultiplier` (Gen 1 = 2× level),
    `CoinsScattered` event ("Coins scattered everywhere!"). No economy yet — the mechanic is the event.

### Test layout: capability classes, not batch files
Tests are organised by **what the move does**, not the batch it arrived in:
`tests/.../Integration/Gen1Attacks/` — `DamageContractTests`, `StabAndTypeEffectivenessContractTests`,
`CriticalHitContractTests`, `MultiHitContractTests`, `SecondaryStatusContractTests`,
`PhysicalSpecialSplitContractTests`, `OneHitKoContractTests`, `TwoTurnMoveContractTests`,
`StatStageMoveContractTests`, `BindingContractTests`, `UniqueMoveEffectContractTests`, over a shared
`Gen1MoveContract` base. **Covering a new batch means adding `InlineData` rows to the matching
class** and creating a new class only when a move introduces a genuinely new mechanic. (Batch 1 +
batch 2 were merged into this structure when batch 2 landed — there are no `Batch1/Batch2` classes.)

### Batch 2 (moves 11–20) ✅ DONE (2026-06-03)
vice-grip, guillotine, razor-wind, swords-dance, cut, gust, wing-attack, whirlwind, fly, bind.
**248 total.** All mechanics below were already implemented in the engine — this batch is
**coverage only, no new engine code** — and each test drives the real `AttackAction` path (the only
substitutions are RNG-gated rolls through the `IBattleRules` seam doubles).
- Reused contracts (rows added to existing classes): damage, PP decrement, accuracy/miss, STAB
  (added a Flying mover), type-effectiveness scaling (Flying super-effective vs Grass/Fighting),
  physical/special-by-type (generalised to a category theory over Normal/Fighting/Flying/Fire/Ice/Electric).
- New capability classes for first-seen mechanics:
  - **One-hit KO** (guillotine) — deals full-HP damage & fells; **fails** (not misses) when user
    level < target level (Gen 1 rule); misses on accuracy fail.
  - **Two-turn charge** (razor-wind, fly) — turn 1 emits `ChargingUp` with no damage / no
    `MoveUsed`; turn 2 lands & deals damage; PP spent once; misses on the release turn. Razor Wind's
    high-crit verified on the release turn vs Fly. **Plus a full-`Battle` test** proving the release
    turn is auto-driven from `ChargingMove` without re-asking input (`CountingInput.CallCount == 1`).
  - **Self-targeting stat-stage** (swords-dance) — +2 user Attack, no damage, `StatStageChanged`
    targets the user.
  - **Binding** (bind) — damages + traps 2–5 turns (`BindingStarted`).
  - **No-op status move** (whirlwind) — announced but no combat effect yet (switch/flee has no
    home until the Game Loop); Gen 1 −6 priority pinned, so the gap is documented not silent.
- Harness: added `MoveScenario.UseRepeated(move, turns)` — runs consecutive real `AttackAction`s on
  one reused `PokemonAttack` wrapper (exactly what `Battle` feeds on a two-turn release), so PP +
  two-turn state carry across turns like a real battle.

### Batch 3 (moves 21–30) ✅ DONE (2026-06-03)
slam, vine-whip, stomp, double-kick, mega-kick, jump-kick, rolling-kick, sand-attack, headbutt,
horn-attack. **293 .NET + 18 Vitest.** Two genuine engine features this batch (both behind the gen
seam per `GENERATION_SEAMS.md §5.0`); everything else coverage-only over real `AttackAction` paths.
- Reused contracts (rows added): damage/PP/miss; STAB (first **Special-type** mover, vine-whip;
  + Fighting jump-kick); type-effectiveness (Grass→Water, Fighting→Normal); physical/special split
  (vine-whip→Special, the Fighting kicks + Normal movers→Physical, sand-attack→Undefined).
- New capability classes (engine already supported these — coverage only):
  - **Flinch** (`FlinchContractTests`: stomp, rolling-kick, headbutt) — sets the flag on hit, never
    on miss, **plus a full-`Battle` test** where a faster flincher locks the target out
    (`FlinchBlocked`, target never emits `MoveUsed`).
  - **Foe stat-drop** (sand-attack) — −1 foe Accuracy, folded into `StatStageMoveContractTests`
    alongside swords-dance's self-buff.
- **Two new engine features:**
  - **Fixed-count multi-hit** — `int? Attack.MultiHitCount` column (+`AddMoveMultiHitCount`
    migration); `AttackAction` uses `MultiHitCount ?? RollMultiHitCount()`. The fixed count is move
    data; the variable 2–5 distribution stays the gen rule. double-kick mapped (Effect=MultiHit,
    count 2). Twineedle/bonemerang reuse the mechanism in their batches.
  - **Jump Kick crash damage** — `MoveEffect.Crash` + `IBattleRules.CalculateCrashDamage`
    (Gen 1 = flat 1 HP) + `CrashDamage` event (console + SignalR emitters + `timeline.ts`
    "kept going and crashed!"). Applied on the accuracy-miss branch. jump-kick mapped. *Deferred
    edge:* Gen 1 also crashes on a Ghost immunity (Fighting→Ghost 0×) — documented, not handled.
- Data: full `PokeApiConnector` re-run (authoritative path) applied the migration + new mappings;
  verified double-kick MultiHitCount=2 / jump-kick Effect=Crash via MCP.

### Batch 4 (moves 31–40) ✅ DONE (2026-06-03)
fury-attack, horn-drill, tackle, body-slam, wrap, take-down, thrash, double-edge, tail-whip,
poison-sting. **342 .NET + 18 Vitest.** Two genuine engine features (both behind the gen seam);
everything else coverage-only over real `AttackAction` paths.
- Reused contracts (rows added): damage/PP/miss; OHKO parametrized (guillotine **+ horn-drill**);
  binding parametrized (bind **+ wrap**); secondary status (body-slam Paralysis, poison-sting Poison);
  variable multi-hit (fury-attack); foe stat-drop (tail-whip −1 Defense, with sand-attack);
  physical/special split (Normal movers + poison-sting→Poison Physical, tail-whip→Undefined);
  STAB/effectiveness (first **Poison** mover poison-sting; Poison→Grass 2×).
- **Two new engine features:**
  - **Recoil** (take-down, double-edge) — `MoveEffect.Recoil` + `IBattleRules.CalculateRecoilDamage`
    (Gen 1 = ¼ damage dealt, min 1); `AttackAction` reuses the existing `RecoilDamage` event (already
    wired through console/SignalR/`timeline.ts`). Recoil applies even on a KO. → `RecoilContractTests`.
  - **Rampage** (thrash) — multi-turn lock + self-confusion, mirroring the two-turn pattern:
    `BattleState.RampageTurnsRemaining`/`RampageMove` (+`Creature` props), `MoveEffect.Rampage`,
    `IBattleRules.RollRampageTurns` (Gen 1 = 2–3). `Battle` force-selects the locked move (no input
    consulted); when the lock expires the user confuses itself (reuses `ConfusedTurns` +
    `ConfusionStarted`). Lock decrements even on a miss. → `RampageContractTests` incl. a full-`Battle`
    test (turn 2 not consulted; player ends up confused). petal-dance reuses this in its batch.
- Data: full `PokeApiConnector` re-run; verified take-down/double-edge→Recoil, thrash→Rampage,
  wrap→Binding, horn-drill→OHKO, body-slam→Paralysis, poison-sting→Poison via MCP. No schema change
  (recoil/rampage are runtime + the existing `Effect` column).

### Batch 5 (moves 41–50) ✅ DONE (2026-06-04)
twineedle, pin-missile, leer, bite, growl, roar, sing, supersonic, sonic-boom, disable.
**375 .NET + 25 Vitest.** One major new engine feature (Disable) and a cross-cutting Gen 1
**move-type correction pass**; everything else coverage-only over real `AttackAction`/`Battle` paths.
- Reused contracts (rows added): damage/PP/miss (pin-missile, bite, twineedle); variable multi-hit
  (pin-missile) + fixed-2 multi-hit (twineedle); foe stat-drop (leer −1 Defense, growl −1 Attack);
  flinch (bite); secondary status (twineedle 20% Poison); no-op switch move (roar, folded with
  whirlwind, −6 priority pinned); physical/special split (bite now Normal/Physical, +comment fixes).
- New capability classes: **`StatusMoveContractTests`** (sing → Sleep, supersonic → Confuse; pure
  status moves that afflict without damage, nothing on a miss) and **`FixedDamageContractTests`**
  (sonic-boom deals exactly 20 regardless of stats/type, incl. immunities; can miss).
- **Two genuine engine features this batch:**
  - **Disable** (`MoveEffect.Disable`) — full mechanic: `BattleState.DisabledMove` +
    `DisableTurnsRemaining` (+ `Creature` delegating props + `CanSelectAnyMove`),
    `IBattleRules.RollDisableTurns` (Gen 1 = 1–7), `AttackAction` picks a random PP-bearing foe
    move and locks it (fails if one's already disabled). Enforced at **move-selection time**:
    `TurnContext.DisabledMove`, `RandomMoveInput`/`AutoSelectInput`/`SignalRInput` skip it, and
    `Battle` Struggles when it's the only move; the counter ticks down in `StatusResolver` and
    re-enables. New `MoveDisabled`/`MoveReEnabled` events wired through console + SignalR +
    `timeline.ts` (+ Vitest). UI greys the locked move (`MoveInfo.Disabled` → move button). Covered
    by `DisableContractTests` incl. a **full-`Battle`** lock→Struggle→re-enable test.
  - **Twineedle** — mapped to the existing fixed-2 multi-hit mechanism (Effect=MultiHit,
    MultiHitCount=2) + its 20% poison secondary; no new code (deferred item now closed).
- **Gen 1 move-type correction pass** — PokeAPI returns each move's *modern* type, but four Gen 1
  moves were retyped later: **karate-chop** (→Fighting), **gust** (→Flying), **sand-attack**
  (→Ground), **bite** (→Dark). The importer now restores their RBY type (all Normal) right after the
  type parse, so STAB, the type chart, and the type-derived physical/special split are Gen-1-correct
  (bite flips Special→Physical). Affected STAB/type-effectiveness rows updated (Flying-effectiveness
  coverage stays via wing-attack). Re-imported + verified all rows via MCP.
- **Frontend Vitest gap closed**: `timeline.test.ts` now also covers `ConfusionStarted`,
  `ConfusionMessage`/`Damage`/`Cleared`, `CoinsScattered`, and the new `MoveDisabled`/`MoveReEnabled`.

### Batch 6 (moves 51–60) ✅ DONE (2026-06-04)
acid, ember, flamethrower, mist, water-gun, hydro-pump, surf, ice-beam, blizzard, psybeam.
**424 .NET + 27 Vitest.** First special-attack-heavy batch; introduced a **data-driven Gen 1
move-data resolver**, the **Mist** mechanic, and a gen-seam cleanup. Everything else coverage-only
over real `AttackAction` paths (first Water & Psychic STAB movers).
- Reused contracts (rows added): damage/PP/miss (all nine damaging moves; hydro-pump 80 & blizzard 90
  can miss); secondary status burn (ember, flamethrower) + freeze (ice-beam, blizzard); STAB + type-
  effectiveness for **Water** (water-gun → Fire 2×) & **Psychic** (psybeam → Poison 2×) + Ice → Dragon;
  physical/special split (Water/Ice/Psychic → Special, acid → Poison Physical).
- New capability classes: **`SecondaryEffectContractTests`** (damaging moves whose secondary is a
  stat drop (acid → −1 foe Defense) or confusion (psybeam) — distinct from the status secondaries)
  and **`MistContractTests`**.
- **`past_values` resolver (the big one)** — PokeAPI returns each move's *modern* stats; Gen 1 often
  differed (special moves stronger, Blizzard 90% acc, several moves a different type). The importer
  now reads PokeAPI's `past_values` array and applies the **earliest** recorded power/accuracy/pp/
  effect_chance/**type** as the Gen 1 value — one data-driven source, no per-move hardcoding. This
  **supersedes batch 5's hardcoded type switch** (karate-chop/gust/sand-attack/bite are now corrected
  via `past_values`, hardcodes removed) and fixed special-move powers (Flamethrower/Surf/Ice Beam 95,
  Hydro Pump/Blizzard 120), Blizzard accuracy 90, and even **double-edge → 100** (Gen 1) from batch 4.
  One documented exception: **acid** (Gen 1 lowers **Defense** at 33%, not Sp.Def/10%) is a manual
  override since PokeAPI's `past_values` is empty for it. Re-imported + verified all rows via MCP.
- **Mist** (`MoveEffect.Mist`) — `BattleState.HasMist` (+ `Creature` prop); `AttackAction` sets it +
  emits `MistApplied`; `TryApplyStatEffect` blocks foe-induced stat drops on the holder (emits
  `StatDropBlocked`), leaving self-buffs/raises untouched. New events wired through console + SignalR
  + `timeline.ts` (+ Vitest). → `MistContractTests`.
- **Gen-seam cleanup (§5.0):** acid is the first chance-based stat drop on a *damaging* move, so the
  stat-effect chance now routes through `IBattleRules.GetSecondaryEffectChance` (new
  `SecondaryEffectKind.StatStage`) instead of reading the `StatEffectChance` column directly —
  matching the status/flinch/confuse secondaries. Behavior-preserving for all existing moves.

### Batch 7 (moves 61–70) ✅ DONE (2026-06-04)
bubble-beam, aurora-beam, hyper-beam, peck, drill-peck, submission, low-kick, counter, seismic-toss,
strength. **467 .NET + 27 Vitest.** One new mechanic (Counter), two new coverage contracts (Recharge,
LevelBased), a **full Gen 1 secondary-chance override sweep**, and submission→Recoil. No frontend
changes (Counter reuses `DamageDealt`/`MoveMissed`).
- Reused contracts (rows added): damage/PP/miss (all eight damaging movers; hyper-beam 90 / submission
  80 / low-kick 90 can miss); secondary stat-drop (bubble-beam −1 Speed, aurora-beam −1 Attack, with
  acid); recoil (submission, with take-down/double-edge); flinch (low-kick); physical/special split
  (hyper-beam→Normal Physical — the classic split case — peck→Flying, submission→Fighting).
- New capability classes: **`RechargeContractTests`** (hyper-beam forces a skip turn after a hit;
  miss → no recharge; full-`Battle`), **`LevelBasedDamageContractTests`** (seismic-toss = user level,
  ignores bulk/type), **`CounterContractTests`** (2× last Normal/Fighting damage; fails otherwise;
  full-`Battle`).
- **Counter** (`MoveEffect.Counter`) — `BattleState.LastDamageTaken` + `LastDamageType` (+ `Creature`
  props), recorded on the standard damage path; `AttackAction` returns 2× when the last hit was
  Normal/Fighting (else `MoveMissed`); −5 priority (move data) resolves it after the opponent's hit.
  Importer maps counter→Counter. Fixed/level-based/self damage isn't recorded ⇒ not counterable (a
  documented simplification).
- **Full Gen 1 secondary-chance override sweep** (layer 2, per `DATA_IMPORT.md` §5.5) — PokeAPI reports
  modern secondary chances and rarely backfills `past_values` for them, so these verified Gen 1 values
  are set in one commented block in the importer: **acid** 33% Def, **aurora-beam** 33% Atk,
  **bubble-beam** 33% Spe, **bite** 10% flinch (was 30), **low-kick** 30% flinch (PokeAPI: none),
  **poison-sting** 20% poison (was 30). Audited the rest (punches/beams 10%, body-slam 30%, twineedle
  20%, stomp/rolling-kick/headbutt 30%, psybeam 10%) — confirmed unchanged. Re-imported + verified via MCP.

### Remaining batches (cadence)
- [ ] Batches 8–17 (moves 71–165): query the next 10 → add `InlineData` rows to the matching
  capability class → add a new capability class only for genuinely new mechanics. **Next: batch 8 =
  moves 71–80.**
- [ ] **Fixed-2 multi-hit mover still pending**: bonemerang — the fixed-count mechanism exists
  (double-kick, twineedle); just needs mapping + coverage in its batch.
- [ ] **Rampage reuse pending**: petal-dance just needs the importer tag (mechanism exists) + coverage.
- [ ] **Deferred Gen 1 fidelity edges** (documented simplifications, revisit if they matter):
  body-slam's Gen 1 *Normal-types-immune-to-its-paralysis* quirk (a type→status-immunity rule — belongs
  on a seam); Fighting/Normal → Ghost immunity for level-based moves (seismic-toss) and Counter; Counter
  only answers damage recorded on the standard damage path (not fixed/level-based).
- [x] **Gen 1 move-data fidelity** is data-driven via the `past_values` resolver (power/accuracy/pp/
  effect_chance/type); **secondary chances/targets** that `past_values` can't express are a short,
  verified override block in the importer (see batch 7). Add to it as later batches surface more.

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

**Move per-generation data (intention — see `DATA_IMPORT.md` §4.1/§5.5):**
- Today the importer resolves each move's **Gen 1** values from PokeAPI `past_values` by taking the
  *earliest* recorded entry. The mechanism already carries the full history, so going multi-gen is a
  **generalisation, not a rewrite**: resolve a field for target generation *G* as the value of the
  earliest `past_values` entry whose `version_group` generation is **> G** (the change happened after
  G, so the old value still applied at G), else the current value. "Earliest = Gen 1" is just the
  *G = 1* case.
- [ ] When moves go per-generation, either store one `Attack` row per `(moveId, generation)` (mirror
  the **learnset model** — a `Generation` column + an `ActiveGeneration` filter, already the template
  in `PokemonLearnset`) **or** resolve on demand for `ActiveGeneration`. Prefer the stored-per-gen row
  for query simplicity and parity with `PokemonSpeciesGenData`.
- [ ] Make the **layer-2 override table per-generation** too (e.g. Acid's stat target/chance differs
  Gen 1 vs Gen 4+). The override key becomes `(moveName, generation)`, not just `moveName`.
- [ ] Keep mechanic/formula differences on the **seams** (`IBattleRules` et al.), never in the
  per-gen move data — the data layer answers "what are this move's numbers in gen G," the seam answers
  "how does the engine apply them in gen G."

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
- ~~`GameController.BuildCreature` uses random moves~~ — **fixed** by the Learnset System (initial moveset now learnset-driven; see Learnset System section)

### Fixed ✅
- Post-feature gen-seam + smell cleanup (2026-06-02): closed three seam leaks surfaced by the Learnset/confusion work — confusion self-hit chance (`ConfusionSelfHitPercent`), STAB (`StabMultiplier`), and the EffectChance read (`GetSecondaryEffectChance` + `SecondaryEffectKind`, a Gen 1 pass-through stub showing the generic shape) are now all on `IBattleRules`; `CalculateConfusionDamage` reads stats via `GetOffensiveStat`/`GetDefensiveStat`. Killed the 5× duplicated `IBattleRules` test doubles with a `TestSupport/DelegatingBattleRules` base (new members are now a one-line change). Centralised move-selection policy in `LearnsetMoveSelector.SelectWithFallback` (dropped the split `BuildCreature` signature + fallback). **Strengthened the docs so this stops recurring:** new generation-agnostic checklist + definition-of-done in `GENERATION_SEAMS.md §5.0`, cross-linked from `DEV_STANDARDS.md` and the `/dev` profile in `AI_CONTEXT.md`. 179 tests green.
- Enemy "only ever uses one status move" (e.g. a wild Charizard that just spammed Leer): the enemy ran on `AutoSelectInput`, which always returns move **slot 0**. `WeightedSmart`/`CanonicalLatest` order a moveset ascending by learn level, so a level-1 status move (Leer, Growl) lands in slot 0 and got used every turn. *(Not a learnset/pre-evolution data gap — Charizard's own learnset has Scratch/Ember/Rage/Slash/Fire Spin ≤ L50.)* Fixed by adding `RandomMoveInput` (uniform pick among PP-available moves, `IRandomSource`-seamed) and wiring it as the enemy input in `GameSessionManager`. Covered by `ConfusionAndInputTests` (only-available-PP, variety-over-many-turns, seeded determinism) and verified live (enemy Vaporeon used Bite/varied moves). The evaluator-driven AI tiers remain future work (AI Move Selection). *Pre-evolution movesets are a separate, lower-priority fidelity item: we use a species' own learnset only, which is correct for most mons but means a fully-evolved species whose own learnset is sparse below its level could get a thin pool — revisit when evolution-chain data is imported (Game Loop / Evolution).*
- Confusion-inflicting moves did nothing (Supersonic appeared to "miss quietly" / have no effect): confusion is a per-battle counter (`ConfusedTurns`), **not** a `StatusCondition`, and nothing ever set it — the importer dropped the `"confusion"` ailment and no move effect applied it. Fixed end-to-end: added `MoveEffect.Confuse` + `IBattleRules.RollConfusionTurns` (Gen 1: 2–5 counter ≈ 1–4 turns), an `AttackAction` `Confuse` case (independent of major status, no stacking, `EffectChance`-gated for secondary confusion), a `ConfusionStarted` event wired through the console + SignalR emitters and the frontend `timeline.ts` ("X became confused!"), and the importer now maps ailment `"confusion"` → `MoveEffect.Confuse`. Re-imported: the 5 Gen 1 confusion moves (Supersonic, Psybeam, Confusion, Confuse Ray, Dizzy Punch) now carry the effect with correct chances; Thrash/Petal Dance correctly excluded. Covered by `ConfusionAndInputTests` and verified live ("VAPOREON became confused!" → "hurt itself in confusion!").
- Attack cadence (Gen 1 feel): the lunge + target flash played **before** the "X used MOVE!" line, and the HP bar snapped to its end-of-turn value the instant a move was chosen (the next turn's `TurnStarted` was applied immediately). Fixed by announcing the move first then animating (`MoveUsed` expansion in `timeline.ts`) and routing `TurnStarted` **through the timeline** so HP/status sync only after the turn's damage animates — bars drain in step now. Verified live (Puppeteer) + locked by the `MoveUsed`/`TurnStarted` Vitest cases and `cadence.spec.ts`.
- Gen 1 physical/special split miscategorised 18 of 110 damaging moves: the importer copied PokeAPI's per-move `damage_class` (the Gen 4+ split), but Gen 1 decides physical/special by the move's **type**. So Hyper Beam/Gust/Acid/Sludge/etc. used Special and Fire/Ice/Thunder Punch, Waterfall, Crabhammer, Vine Whip, Razor Leaf used Attack — computing damage off the wrong stat. Fixed in `MoveImport.MapToAttack` (now derives `AttackType` from `DamageType` via `Gen1DamageCategory`) and the existing `moves.db` rows were corrected in place by the same rule (verified 0 mismatches). See `DATA_IMPORT.md` §4.1/§6.
- Battle log froze on faint (stuck on last damage line, no "fainted!"/winner): `BattleScene`'s `destroy()` was dead code (Phaser never calls it), so `bridge.on` listeners leaked across canvas remounts (HMR/StrictMode) and a stale scene's `playFaintAnimation` threw on a destroyed sprite — now removed via `SHUTDOWN`/`DESTROY` scene events (`teardown`). Hardened the queue too: `drainQueue` try/catch-continues per task with a `finally` reset, and `waitForBridge` times out after 3 s so a lost `animationComplete` can't hang the log.
- Battle-log text polish: move names display formatted (`fury-attack` → `FURY ATTACK`) via `utils/format.ts#formatMoveName`, applied to the log (`MoveUsed`/`MoveMissed`/`BindingStarted`) and the move-menu grid; Gen 1 per-move two-turn charge lines (`chargingMsg`: Dig "dug a hole!", Fly "flew up high!", Solar Beam "took in sunlight!", etc.) replace the generic "is charging up X!"; immunity now reads "It doesn't affect X..." with no damage number/crit and no hit sound (was "took 0 damage! It had no effect.")
- Metronome (`MoveEffect.Metronome`): picks a random eligible Gen 1 move and executes it in full; move pool threaded from `GameController` → `GameSessionManager` → `Battle` → `AttackAction`; DB updated via re-run of importer
