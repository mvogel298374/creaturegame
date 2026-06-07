# Battle Sim – Done / Archive

Historical record of completed work, split out of `TODO.md` to keep the live task list (and the
per-session read) small. Nothing here is pending. The per-batch logs are kept verbatim because they
double as a fidelity record and the `seam-reviewer` references these patterns.

> **Live tasks:** `TODO.md` · **See also:** `CLAUDE.md`, `AI_CONTEXT.md`, `DESIGN_GUIDES.md`, `DEV_STANDARDS.md`

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

**Experience, Levelling & Level Picker** — Gen 1 wild XP formula; `LeveledUp` event; level slider in UI (5–100); `GainExperience → LevelUp` path. *(Core mechanic only — XP is awarded and the player levels up at the moment of victory, recalculating stats. The on-screen XP bar is still cosmetic and there's no level-up move learning; see "XP & Level-Up — finish the in-battle loop" in `TODO.md`.)*

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

## Learnset System — Initial moveset from learnsets ✅ DONE (2026-06-02)

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

---

## Gen 1 Attack Behavior Coverage — Batches 1–17 ✅ COMPLETE (2026-06-07)

Proved **every Gen 1 attack does what it sets out to do** when given to a Pokémon and used in
battle, in **batches of 10 moves**, via parametrized "effect contract" tests (`[Theory]` +
`[InlineData]`). Real move rows come from the live `moves.db` (`MovesFixture`); the
`MoveScenario` harness gives the move to a creature and runs one `AttackAction`. Moves 1–165 are
all covered (including the deferred Transform/Conversion mutation batch). Final suite: **813 .NET
+ 37 Vitest**.

### Test layout: capability classes, not batch files
Tests are organised by **what the move does**, not the batch it arrived in:
`tests/.../Integration/Gen1Attacks/` — `DamageContractTests`, `StabAndTypeEffectivenessContractTests`,
`CriticalHitContractTests`, `MultiHitContractTests`, `SecondaryStatusContractTests`,
`PhysicalSpecialSplitContractTests`, `OneHitKoContractTests`, `TwoTurnMoveContractTests`,
`StatStageMoveContractTests`, `BindingContractTests`, `UniqueMoveEffectContractTests`, over a shared
`Gen1MoveContract` base. **Covering a new batch means adding `InlineData` rows to the matching
class** and creating a new class only when a move introduces a genuinely new mechanic.

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
  wrap→Binding, horn-drill→OHKO, body-slam→Paralysis, poison-sting→Poison via MCP. No schema change.

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
    `timeline.ts` (+ Vitest). UI greys the locked move. Covered by `DisableContractTests` incl. a
    **full-`Battle`** lock→Struggle→re-enable test.
  - **Twineedle** — mapped to the existing fixed-2 multi-hit mechanism + its 20% poison secondary.
- **Gen 1 move-type correction pass** — PokeAPI returns each move's *modern* type, but four Gen 1
  moves were retyped later: **karate-chop** (→Fighting), **gust** (→Flying), **sand-attack**
  (→Ground), **bite** (→Dark). The importer now restores their RBY type (all Normal) right after the
  type parse. *(Superseded in batch 6 by the `past_values` resolver — the hardcodes were removed.)*

### Batch 6 (moves 51–60) ✅ DONE (2026-06-04)
acid, ember, flamethrower, mist, water-gun, hydro-pump, surf, ice-beam, blizzard, psybeam.
**424 .NET + 27 Vitest.** First special-attack-heavy batch; introduced a **data-driven Gen 1
move-data resolver**, the **Mist** mechanic, and a gen-seam cleanup.
- New capability classes: **`SecondaryEffectContractTests`** (damaging moves whose secondary is a
  stat drop (acid → −1 foe Defense) or confusion (psybeam)) and **`MistContractTests`**.
- **`past_values` resolver (the big one)** — PokeAPI returns each move's *modern* stats; Gen 1 often
  differed. The importer now reads PokeAPI's `past_values` array and applies the **earliest**
  recorded power/accuracy/pp/effect_chance/**type** as the Gen 1 value — one data-driven source, no
  per-move hardcoding. **Supersedes batch 5's hardcoded type switch** and fixed special-move powers
  (Flamethrower/Surf/Ice Beam 95, Hydro Pump/Blizzard 120), Blizzard acc 90, double-edge → 100. One
  documented exception: **acid** (Gen 1 lowers Defense at 33%) is a manual override (empty `past_values`).
- **Mist** (`MoveEffect.Mist`) — `BattleState.HasMist`; `AttackAction` sets it + emits `MistApplied`;
  `TryApplyStatEffect` blocks foe-induced stat drops on the holder (emits `StatDropBlocked`).
- **Gen-seam cleanup (§5.0):** acid's chance-based stat drop on a damaging move now routes through
  `IBattleRules.GetSecondaryEffectChance` (new `SecondaryEffectKind.StatStage`).

### Batch 7 (moves 61–70) ✅ DONE (2026-06-04)
bubble-beam, aurora-beam, hyper-beam, peck, drill-peck, submission, low-kick, counter, seismic-toss,
strength. **467 .NET + 27 Vitest.** One new mechanic (Counter), two new coverage contracts (Recharge,
LevelBased), a **full Gen 1 secondary-chance override sweep**, and submission→Recoil.
- New capability classes: **`RechargeContractTests`** (hyper-beam), **`LevelBasedDamageContractTests`**
  (seismic-toss = user level), **`CounterContractTests`** (2× last Normal/Fighting damage; full-`Battle`).
- **Counter** (`MoveEffect.Counter`) — `BattleState.LastDamageTaken` + `LastDamageType`; `AttackAction`
  returns 2× when the last hit was Normal/Fighting; −5 priority resolves it after the opponent's hit.
  Fixed/level-based/self damage isn't recorded ⇒ not counterable (documented simplification).
- **Full Gen 1 secondary-chance override sweep** (layer 2, per `DATA_IMPORT.md` §5.5) — verified Gen 1
  values set in one commented importer block: **acid** 33% Def, **aurora-beam** 33% Atk, **bubble-beam**
  33% Spe, **bite** 10% flinch, **low-kick** 30% flinch, **poison-sting** 20% poison. Rest audited unchanged.

### Batch 8 (moves 71–80) ✅ DONE (2026-06-04)
absorb, mega-drain, leech-seed, growth, razor-leaf, solar-beam, poison-powder, stun-spore,
sleep-powder, petal-dance. **490 .NET + 28 Vitest.** Almost entirely a coverage batch, plus the
**Gen 1 type-immunity seam** and one new event.
- New capability classes: **`DrainContractTests`**, **`LeechSeedContractTests`**, **`ImmunityContractTests`**.
- **Type-immunity seam** — new `IBattleRules.CanReceiveStatus` (Poison-type can't be poisoned, Fire-type
  can't be burned, Normal-move can't paralyze a Normal-type = the Body Slam quirk) and `CanBeLeechSeeded`
  (Grass immune). Moves that bypass the damage calc (fixed/level-based/OHKO/Super Fang) and Counter now
  respect 0× type immunity via `ITypeChart`. New `MoveHadNoEffect` event. Closes the deferred body-slam +
  Ghost edges.
- **Data fix:** Gen 1 **growth** raises Special (not Attack) — importer layer-2 override.
- Latent fidelity bug fixed: `SonicBoomIgnoresTheTypeMatchup(Ghost)` corrected to "ignores effectiveness
  *scaling*" + a Ghost-immunity test.

### Batch 9 (moves 81–90) ✅ DONE (2026-06-04)
string-shot, dragon-rage, fire-spin, thunder-shock, thunderbolt, thunder-wave, thunder, rock-throw,
earthquake, fissure. **535 .NET + 28 Vitest.** Pure coverage batch + one small engine extension and
two data fixes — no new mechanics.
- **Engine extension:** the batch-8 type-immunity guard now also covers **pure-status moves** — a status
  move whose type is 0× against the target has no effect (Thunder Wave is Electric ⇒ Ground immune).
- **Data fixes (layer-2):** string-shot Speed −2 → −1; thunder paralysis 30% → 10%.
- **Self-audit fixes:** removed a dead Counter Ghost-immunity branch; added `SecondaryChanceDataContractTests`
  to pin the importer's layer-2 secondary-chance overrides (previously a re-import could silently regress).

### Batch 10 (moves 91–100) ✅ DONE (2026-06-05)
dig, toxic, confusion, psychic, hypnosis, meditate, agility, quick-attack, rage, teleport.
**574 .NET + 28 Vitest.** One genuine new mechanic (Rage) + one Gen 1 data fix (Toxic → BadPoison).
- New capability classes: **`RageContractTests`** and **`PriorityMoveContractTests`** (quick-attack).
- **Rage (new mechanic, behind the gen seam):** `MoveEffect.Rage`; lock-in mirrors rampage/two-turn. On
  hit, a raging creature gains Attack by `IBattleRules.RageAttackStagesPerHit` (Gen 1 = 1) once per
  connecting attack. Full-`Battle` test asserts the **quirk** (Attack rises once per *hit received*).
- **Data fix:** Gen 1 **toxic** → importer layer-2 `BadPoison` override; pinned. Verified via MCP.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers); all 3 advisories fixed before commit.

### Infra cleanup (2026-06-05, between batches 10 and 11)
Extracted `Battle.SelectMoveAsync` from the duplicated 4-level player/enemy move-selection ternary
(two-turn → rampage → rage → struggle/input) so lock-precedence lives in one place; removed the
unreachable `"bad-poison"` importer arm. Behavior-preserving.

### Batch 11 (moves 101–110) ✅ DONE (2026-06-05)
night-shade, mimic, screech, double-team, recover, harden, minimize, smokescreen, confuse-ray,
withdraw. **605 .NET + 30 Vitest.** Two new mechanics (Recover, Mimic) + a correctness fix to the
type-immunity guard. Two new events (`Healed`, `MimicLearned`).
- New capability classes: **`HealContractTests`** (Recover ½ max HP) and **`MimicContractTests`**.
- **Recover (`MoveEffect.Heal`):** heals `MaxHP × IBattleRules.RecoverHealFraction` (Gen 1 = ½); emits
  `Healed` with the *actual* amount.
- **Mimic (`MoveEffect.Mimic`):** copies a random foe move by swapping `PokemonAttack.Base`; revert lives
  in **`Creature.ResetBattleState`** (so Haze's mid-battle reset can't orphan it) — the transient swap
  never leaks into the permanent `MoveSet`.
- **Correctness fix (the immunity seam):** the batch-9 pure-status type-immunity guard now only fires for
  **foe-directed** moves, so a Normal-type self-buff/Recover is no longer wrongly blocked against a Ghost.
  Counter (BaseDamage 0 but foe-directed) stays inside the guard — a failing test caught that.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers, 4 advisories); all fixed, incl. a **real bug**
  (Haze+Mimic permanent-MoveSet leak).

### Batch 12 (moves 111–120) ✅ DONE (2026-06-05)
defense-curl, barrier, light-screen, haze, reflect, focus-energy, bide, metronome, mirror-move,
self-destruct. **638 .NET + 33 Vitest.** Mechanic-heavy: **five** new mechanics (Reflect, Light Screen,
Focus Energy, Bide, Mirror Move). Three new events (`ScreenApplied`, `FocusEnergyApplied`, `BideStoring`).
- New capability classes: **`ScreenContractTests`**, **`FocusEnergyContractTests`**, **`BideContractTests`**,
  **`MirrorMoveContractTests`**.
- **Reflect / Light Screen:** double the holder's Defense / Special vs the matching damage via a new
  `DamageCalculator` `screenDefenseMultiplier` param (crits bypass screens, Gen 1). Factor on
  `IBattleRules.ScreenDefenseMultiplier` (Gen 1 = 2).
- **Focus Energy:** the Gen 1 *bug* (quarters crit instead of ×4) lives in `Gen1BattleRules.GetCritChance`;
  test pins the ÷4 quirk.
- **Bide:** lock-in; release deals `accumulated × IBattleRules.BideDamageMultiplier` (Gen 1 = 2),
  typeless/never-miss. **Accumulation runs in every damage-category branch** (a seam-review BLOCK caught
  the original Standard-only gap).
- **Mirror Move:** re-executes the foe's last move via an inner action; fails if the foe hasn't moved.
- Seam-review gate: BLOCK → 2 doc blockers (per-gen XML docs for Bide seam members) + 4 advisories, all
  fixed; the Bide all-category accumulation gap was the substantive one.

### Batch 13 (moves 121–130) ✅ DONE (2026-06-05)
egg-bomb, lick, smog, sludge, bone-club, fire-blast, waterfall, clamp, swift, skull-bash.
**690 .NET + 33 Vitest.** Pure **coverage + data-fidelity** batch — no new engine code, events,
schema, or seam. Only production change: three Gen 1 importer data fixes.
- New capability class: **`NeverMissContractTests`** (swift).
- **lick is Ghost-type** — 0× vs Normal *and* (the Gen 1 bug) 0× vs Psychic; folds the immunity into the
  calc (emits `DamageDealt` at 0, not `MoveHadNoEffect`).
- **Three importer data fixes (layer-2 + name-match), pinned:** **skull-bash → TwoTurn** (Gen 1 plain
  charge); **fire-blast** burn 30%; **waterfall** no secondary (the 20% flinch was Gen 4).
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers). Surfaced the pre-existing flaky OHKO test (fixed batch 16).

### Batch 14 (moves 131–140) ✅ DONE (2026-06-05)
spike-cannon, constrict, amnesia, kinesis, soft-boiled, high-jump-kick, glare, dream-eater, poison-gas,
barrage. **730 .NET + 33 Vitest.** One new mechanic (Dream Eater) + two importer mappings. No layer-2
override needed.
- New capability class: **`DreamEaterContractTests`**.
- **Dream Eater (`MoveEffect.DreamEater`):** fails on a non-sleeping target (reuses `MoveMissed`, the
  state-precondition path). The sleep requirement is **gen-invariant**, so inline, not on the seam. The
  50% drain heal rides on `DamageCategory.Drain`.
- **Two importer mappings:** high-jump-kick → Crash; dream-eater → DreamEater.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers).

### Batch 15 (moves 141–150) ✅ DONE (2026-06-06)
leech-life, lovely-kiss, sky-attack, bubble, dizzy-punch, spore, flash, psywave, splash (**9 of 10** —
Transform deferred). **758 .NET + 34 Vitest.** Two new engine bits, rest coverage-only.
- **Psywave (`DamageCategory.Psywave`):** variable damage = random 1..floor(1.5 × user level), ignoring
  Attack/Defense, type, STAB, crits. Magnitude on the seam (`IBattleRules.RollPsywaveDamage`).
  **`PsywaveContractTests`** exercises the *quirk*, not just the import mapping.
- **Splash (`MoveEffect.Splash`):** Gen 1 no-op — new `ButNothingHappened` event. Inline (gen-invariant).
- **Layer-2 importer data overrides, pinned:** **bubble & constrict → 33% Speed drop** (also corrects
  batch-14 constrict); **dizzy-punch → no secondary**; **sky-attack → flinch chance cleared**.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers).

### Batch 16 (moves 151–160) ✅ DONE (2026-06-06)
acid-armor, crabhammer, explosion, fury-swipes, bonemerang, rest, rock-slide, hyper-fang, sharpen
(**9 of 10** — Conversion deferred). **779 .NET.** One new mechanic (Rest) + bonemerang mapping; rest
coverage + one data fix.
- **Rest (`MoveEffect.Rest`):** self-targeting heal+sleep. Fully restores HP, overwrites status with
  `Sleep`, forces sleep for a fixed `IBattleRules.RestSleepTurns` (Gen 1 = 2; on the seam). Fails at full
  HP via `MoveMissed`. **`RestContractTests`** + a full-`Battle` forced-skip test (asserts the foe is never slept).
- **Bonemerang:** importer → `MoveEffect.MultiHit` + `MultiHitCount=2` (reuses double-kick/twineedle).
- **Layer-2 data fix, pinned:** **rock-slide → flinch cleared** (Gen 1 had no flinch; Gen 2 added 30%).
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers). Reviewer flagged a potential self-vs-foe status
  leak on Rest; verified the row's `StatusEffect` is None, then guarded + pinned it.

### Batch 17 (moves 161–165) ✅ DONE (2026-06-07) — FINAL COVERAGE BATCH
tri-attack, super-fang, slash, substitute, struggle. **802 .NET + 35 Vitest.** One big mechanic
(Substitute) + a data fix; rest reuse.
- **Substitute (`MoveEffect.Substitute`):** costs floor(maxHP/4) HP, raises a decoy with floor(maxHP/4)+1
  HP; fails if one's up or HP ≤ cost. **Cross-cutting:** added one shared `DealDamageToTarget` helper that
  absorbs into the decoy and routed **every** damage path through it (Standard/Drain, Fixed, LevelBased,
  OHKO, SelfDestruct, SuperFang, Psywave, Counter, **and Bide unleash**) — closing the "hook on only the
  Standard path" leak class. While up, the decoy shields status/stat-drop/confusion — snapshotted at impact
  so the shield still blocks on the **breaking** hit. 3 new events. `SubstituteContractTests` covers
  create/cost, absorb, break+overflow, fail cases, shields, breaking-hit shield, full-`Battle` persistence.
- Reused/coverage: super-fang (`SuperFangContractTests` + data pin); slash (single-turn high-crit); struggle.
- **Layer-2 data fix, pinned:** tri-attack → no secondary in Gen 1.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers). Advisory fixed: secondary-shield snapshotted at impact.

### Type/identity-mutation batch — Transform (144) + Conversion (160) ✅ DONE (2026-06-07)
**813 .NET + 37 Vitest.** The two deferred identity/type-mutation moves — covered together so the
snapshot/restore machinery (wider than Mimic's) is built once. No schema change, no new seam, no
layer-2 override (only the `Effect` name-mapping was added).
- **Shared identity-snapshot machinery:** new `BattleState.OriginalIdentity` (an `IdentitySnapshot` of
  pre-mutation types, the four non-HP battle stats, SpeciesId, original moveset wrappers) +
  `Creature.SnapshotIdentityForMutation()` (captures **once**) and `RestoreOriginalIdentity()`.
  `ResetBattleState()` restores before the `Battle = new()` swap, and `Battle`'s end cleanup calls it
  alongside `RestoreMimickedMove()` — same leak-proofing as Mimic. Added `StatStages.Copy()`.
- **Transform (`MoveEffect.Transform`):** copies the target's types, Atk/Def/Spec/Speed, stat stages,
  SpeciesId, full moveset (each move at `min(5, max)` PP); HP/MaxHP/level stay the user's. Self-affecting.
  New `TransformedInto` event.
- **Conversion (`MoveEffect.Conversion`):** copies the foe's Type1/Type2 onto the user (the Gen 1 mechanic
  — Gen 2+ matches one of the user's own moves instead, kept inline + documented). New `ConvertedType` event.
- Tests: `TransformContractTests` + `ConversionContractTests` (incl. the shared-machinery proof:
  Conversion-after-Transform still restores the true pre-Transform original). Both pinned.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers, 2 advisories fixed): pinned `StatusEffect == None`
  on both moves + asserted the foe's status stays None; named both in the `targetsFoe` immunity-guard comment.

### Resolved coverage-era tech debt ✅
- **Flaky OHKO tests** (fixed batch 16): both `OHKOMove_*` tests relied on level implying speed, but
  randomised DVs flipped order. Rewrote to set Speed explicitly + renamed to the speed framing
  (`OHKOMove_FailsIfTargetFasterThanSource` / `OHKOMove_FaintsTargetIfSourceAtLeastAsFast`) — Gen 1 OHKO
  is a Speed compare (`IBattleRules.OneHitKoSucceeds`), not the level check Gen 2 added.
- **Fixed-2 multi-hit mover**: bonemerang — done in batch 16.
- **Rampage reuse**: petal-dance — done in batch 8.
- **Gen 1 type immunities** (batch 8): Poison→poison, Fire→burn, Body Slam→Normal-paralysis, Grass→Leech
  Seed, Ghost (0×) for fixed/level-based/OHKO/Super Fang/Counter — all on the seam. Remaining edge: Counter
  still only answers standard-path damage (documented simplification — see `TODO.md`).
- **Seam audit (2026-06-04):** fixed two move-specific damage quirks that leaked out of the seams: (1)
  **OHKO success** was using the Gen 2+ level rule → now `IBattleRules.OneHitKoSucceeds` (Gen 1 Speed
  compare); (2) **Self-Destruct/Explosion Defense-halving** was an inline `/2` mutating `Target.Attributes`
  → now `IBattleRules.SelfDestructDefenseDivisor` passed into `DamageCalculator`.
- **Gen 1 move-data fidelity** is data-driven via the `past_values` resolver; **secondary chances/targets**
  that `past_values` can't express are a short, verified override block in the importer (see batch 7).

---

## Web UI — Phaser Canvas & Animations ✅ DONE

### Phaser Canvas ✅ DONE
- [x] `phaser` + `mitt` npm dependencies added to `ClientApp`
- [x] `BattleCanvas.tsx` — mounts Phaser `Game` lazily (dynamic import, separate chunk); destroys on unmount
- [x] `BattleScene.ts` — loads front/back sprites, diagonal layout, entry slide-in animation with Web Audio cries
- [x] `PhaserBridge.ts` — typed mitt emitter; React dispatches `playMoveAnimation` / `playFaintAnimation`; Phaser emits `animationComplete` back
- [x] `AudioEngine.ts` — Web Audio API synth: `playCry`, `playFaintCry`, `playHit`, `playTick`
- [x] CSS sprite `<img>` placeholders replaced by the Phaser canvas; React retains HP/status/nameplate overlay layer

### Animations ✅ DONE
- [x] Entry: sprites slide in from edges with species cries; idle bob tween starts after entry
- [x] `MoveUsed` → attacker lunges; target white-flash + `playHit()`
- [x] `DamageDealt` → `UPDATE_HP` fires immediately (CSS transition); log message after 650ms
- [x] `CreatureFainted` → sprite slides down + fades with `playFaintCry()`; log after
- [x] `LeveledUp` → XP bar fills to 100% then resets; log after
- [x] All events enqueued — log text always appears **after** the relevant animation (Gen 1 feel)
- [x] Move menu re-enabled only after animation queue drains (`animationComplete` bridge event)
- [x] `useBattleHub` state gains `animating: boolean`; FIGHT + move buttons check `phase === 'choosing' && !animating`

---

## Tech Debt / Cleanup — Done ✅

- Remove dead scaffolding (`Body`, `Brain`, `BodyPart`, `CreatureType`, etc.)
- `.gitignore`, `.gitattributes`, `.editorconfig`, `global.json` (SDK pin)
- EF Core migrations; `EnsureDatabaseCreated()` calls `Database.Migrate()`
- `StatStages` struct→class (silent mutation fix)
- `AsNoTracking()` on all read-only DB service methods
- Pending-session TTL in `GameSessionManager` (2-min eviction)
- `AlwaysHitRules` test helper (eliminates 1/256-miss flakiness)

### Architecture Review (2026-06-01) — resolved items

#### 1. Web battle lifecycle — disconnect leak + broken reconnect + swallowed errors ✅ DONE
`SignalRInput.ChooseMoveAsync` awaited a TCS with no cancellation path and `BattleHub` had no
`OnDisconnectedAsync`, so every abandoned battle leaked the input + both `Creature`s + the loop task.
- [x] `SignalRInput`: `_cancelled` flag + `Cancel()` that calls `_tcs?.TrySetCanceled()`; `ChooseMoveAsync`
  throws `OperationCanceledException` on entry if cancelled.
- [x] `BattleHub.OnDisconnectedAsync` → `manager.AbandonBattle(connectionId)` → `Cancel()`.
- [x] `GameSessionManager`: wrap the `Task.Run` body in try/catch — swallow/log `OperationCanceledException`
  at debug, other exceptions at error.
- [x] **Reconnect** — active battles keyed by `gameId`; `SignalRBattleEventEmitter` resolves the current
  connection per-emit; `OnConnectedAsync` with the same `gameId` rebinds (`AttachConnection`). Disconnect
  arms a 40 s grace timer (`DetachConnection`) that abandons only if no reconnect arrives. Verified e2e.

#### 2. Pull `BattleState` extraction forward ✅ DONE
`Creature` conflated persistent identity, transient battle state, and behaviour; `ResetBattleState()` was a
hand-maintained reset list (the `StatStages` struct→class bug was exactly this fault).
- [x] Extracted transient fields into `BattleState` (`Creature/BattleState.cs`), held as `Creature.Battle`.
- [x] `ResetBattleState()` is now `=> Battle = new BattleState()` — whole-object swap. Locked in by
  `ResetBattleState_ReplacesWholeBattleState_ClearingEveryTransientField`.
- [x] **Delegating properties** on `Creature` so the ~120 call sites stay unchanged. Save split is ready:
  persist Creature minus `Battle`. *(Optional future cleanup — migrate call sites to `creature.Battle.X`
  and drop the facade — deferred; see `TODO.md` tech debt.)*

#### 4. Speed tie-break uses RNG as a sort key ✅ DONE
`Battle.cs` called `.ThenBy(_ => Random.Shared.Next())` inside the `OrderBy` comparator (ill-defined key).
- [x] Now draws the tie-break once (`int tieBreak = _rng.Next(2)`) via the injected `IRandomSource`.

#### 5. DbContext via `new()` instead of DI ✅ DONE
`GameController` / `SpeciesController` did `new PokemonDbContext()` / `new MovesDbContext()` (lost pooling).
- [x] Registered `AddDbContextFactory<…>()` in `Program.cs`; both controllers inject `IDbContextFactory<T>`
  and use `CreateDbContextAsync()`. Verified at runtime.

#### 6. Frontend battle-log queue was structurally racy ✅ DONE
The imperative enqueue/waitForBridge/delay choreography in `useBattleHub` (two bugs: permanent freeze +
listener leak).
- [x] Split into a **pure** `expandEvent(...) → { now, steps }` (`battle/timeline.ts`) + a small **driver**
  (`useBattleTimeline`) that plays steps one at a time; `useBattleHub` slimmed to connection + reducer.
- [x] Sequencing/timing/text unit-tested without a browser (`timeline.test.ts`, 15 Vitest cases).
- [x] Playwright E2E landed (9 specs via the `?e2e=1` seam).
- [x] Full-flow parity verified live (Puppeteer + Playwright faint→winner play-through).

#### 6a. Code-review cleanups (batches 11–13, 2026-06-05) ✅ (one item deferred — see `TODO.md`)
- [x] **Importer name-dispatch consolidated** — the ~20-arm `else if (Name == …)` chain replaced by a
  `static readonly Dictionary<string, MoveEffect> Gen1MoveEffects`.
- [x] **`AttackAction.ExecuteInner(Attack)` helper** — Metronome and Mirror Move share one helper.
- [x] **Bide "typeless" contradiction resolved** — release no longer records `LastDamageTaken`, so Bide is
  non-counterable like the other non-standard categories. Pinned by `BideDamageIsTypelessAndNotCounterable`.
- [x] **Mirror Move filter/comment made consistent** — dropped the dead `last.Effect != MirrorMove` check.
- [x] **`Creature.cs` delegating-prop alignment** normalised.
- [x] **PP-skip predicate named** — `isLockedInContinuation` local.

---

## Known Gaps — resolved ✅
- ~~`GameController.BuildCreature` uses random moves~~ — **fixed** by the Learnset System (initial moveset
  now learnset-driven).

---

## Fixed ✅ (battle/UI bugs)
- Post-feature gen-seam + smell cleanup (2026-06-02): closed three seam leaks surfaced by the
  Learnset/confusion work — confusion self-hit chance (`ConfusionSelfHitPercent`), STAB (`StabMultiplier`),
  and the EffectChance read (`GetSecondaryEffectChance` + `SecondaryEffectKind`) are now all on
  `IBattleRules`; `CalculateConfusionDamage` reads stats via `GetOffensiveStat`/`GetDefensiveStat`. Killed
  the 5× duplicated `IBattleRules` test doubles with a `TestSupport/DelegatingBattleRules` base. Centralised
  move-selection policy in `LearnsetMoveSelector.SelectWithFallback`. Added the generation-agnostic checklist
  + definition-of-done in `GENERATION_SEAMS.md §5.0`. 179 tests green.
- Enemy "only ever uses one status move": the enemy ran on `AutoSelectInput`, which always returns slot 0;
  `WeightedSmart`/`CanonicalLatest` order ascending by learn level, so a level-1 status move landed in slot 0.
  Fixed by adding `RandomMoveInput` (uniform pick among PP-available moves, `IRandomSource`-seamed) and
  wiring it as the enemy input. Covered by `ConfusionAndInputTests` + verified live.
- Confusion-inflicting moves did nothing: confusion is a per-battle counter (`ConfusedTurns`), not a
  `StatusCondition`, and nothing set it. Fixed end-to-end: `MoveEffect.Confuse` + `IBattleRules.RollConfusionTurns`
  (Gen 1: 2–5 counter), an `AttackAction` `Confuse` case, a `ConfusionStarted` event, and the importer maps
  ailment `"confusion"` → `Confuse`. Covered by `ConfusionAndInputTests` + verified live.
- Attack cadence (Gen 1 feel): the lunge + flash played **before** the "X used MOVE!" line, and the HP bar
  snapped to its end-of-turn value when a move was chosen. Fixed by announcing the move first then animating,
  and routing `TurnStarted` **through the timeline**. Locked by Vitest + `cadence.spec.ts`.
- Gen 1 physical/special split miscategorised 18 of 110 damaging moves: the importer copied PokeAPI's
  `damage_class` (the Gen 4+ split), but Gen 1 decides physical/special by the move's **type**. Fixed in
  `MoveImport.MapToAttack` (derives `AttackType` from `DamageType` via `Gen1DamageCategory`); existing rows
  corrected in place (0 mismatches). See `DATA_IMPORT.md` §4.1/§6.
- Battle log froze on faint: `BattleScene.destroy()` was dead code, so `bridge.on` listeners leaked across
  canvas remounts and a stale scene's `playFaintAnimation` threw — now removed via `SHUTDOWN`/`DESTROY` scene
  events. Hardened the queue (`drainQueue` try/catch-continues; `waitForBridge` 3 s timeout).
- Battle-log text polish: move names display formatted (`fury-attack` → `FURY ATTACK`); Gen 1 per-move
  two-turn charge lines replace the generic "is charging up X!"; immunity reads "It doesn't affect X...".
- Metronome (`MoveEffect.Metronome`): picks a random eligible Gen 1 move and executes it in full; pool
  threaded from `GameController` → `GameSessionManager` → `Battle` → `AttackAction`.
</content>
</invoke>
