# ENCOUNTER_DESIGN.md — the roguelite encounter layer (design v0, 2026-06-27)

> **Status: design, not yet code.** This is the `/plan` output for the **Encounter Logic** gate in `TODO.md`
> — the deliberate, balance-aware design that must exist *before* any acquisition/catch mechanic is built. It
> defines *what the player faces*, *how the run is shaped around it*, and *how/whether a creature can be
> acquired*. §1–5 are the target model; §6 maps it onto the code that exists today; §7 is the phased build.
>
> **Companions:** `GAME_LOOP.md` (the run/event model this plugs into — a node is an *event*), `DESIGN_GUIDES.md`
> (Gen 1 mechanics, the roguelite/autobattler/fusion influences), `GENERATION_SEAMS.md` (the seams every
> generation-variable rule rides), `STATE_MODEL.md` (permanent vs transient creature state across a run),
> `TODO.md` (the gate + the deferred acquisition cluster this unblocks).

---

## 0. Why this design exists (the gate)

This is a **roguelite**, not a normal Pokémon game. If the player can acquire *truly random* creatures against
an *undefined* encounter distribution, the power curve balloons and one lucky pickup breaks a run. So three
things must be designed **together**, before any catch code:

1. **Run shape** — how encounters are sequenced and chosen.
2. **What you face** — the encounter pool per place and per depth.
3. **How/whether you can take it** — the acquisition channels and their guardrail.

The seam already exists (`EncounterSelector.PickByBst`, `EncounterFactory.CreateEnemyAsync`,
`EncounterFactory.ScaleWildLevel`, `BattleRunner.RunAsync`). This document turns that mechanism into a design.

---

## 1. Run model — a graph of themed biomes under a regional origin

A run is **not** the current flat endless chain. It plays out across a **graph of biomes**:

- **Biome** — a cluster of branching nodes sharing one **type theme** (e.g. *Bug Forest* → Bug/Grass/Poison).
- **Graph, not a line** — biomes connect by **sensible geographic adjacency** (Forest → Cave → Mountain) and
  may **intersect**: an intersection point lets a path cross from one biome's cluster into an adjacent biome's.
  The player **charts a route** through this graph — route choice is the core roguelite verb.
- **Fair opening** — the **first** route choice guarantees at least one offered biome is a *favourable*
  matchup for the chosen starter (its type hits that biome's theme super-effectively, read off the active
  `ITypeChart`), so a run never opens with only bad lanes. First choice only; later choices are the plain
  neighbour sample (a starter with no super-effective coverage, e.g. pure Normal, falls back to the sample).
- **Regional origin** — every biome belongs to an overarching region: **Kanto** first (matches the Gen 1
  focus), Johto/etc. later. A run is seeded within a region. This "origin" axis lines up with the
  **multi-generation roadmap** (`TODO.md` → *Multi-Generation*): a new region is largely a new biome set, not
  new loop code.

The biome theme is the **cascade root** that ties the whole layer together:

```
biome type theme  ──▶  type-filtered encounter pool  ──▶  fought-only acquisition pool
   (authored)            (PickByBst within theme)            (offer only what you fought here)
```

This single cascade gives theming, balance guardrail, and acquisition source from one authored fact.

---

## 2. Biomes — curated, region-grouped, seeded per run  *(Phase 1 spec)*

### 2.1 Data model  *(✅ implemented — `creaturegame/Creatures/Biome.cs`)*

A biome is **authored design content** (not imported — there is no per-area location table in `pokemon.db`;
`PokemonGameAvailability` carries only `GameVersion` + `AvailabilityType`). It lives in a **static C# registry**
— no migration, unit-testable, and biomes change with design, not with a PokeAPI re-import. (Can graduate to a
JSON static file later if designers need to edit without a rebuild.)

```
enum Region              { Kanto, … }                                   // a content-grouping axis (= the multi-gen axis)
record BiomeDefinition   ( Id, Name, Region, Types[1..3], Neighbours[] ) // Neighbours authored now, TRAVERSED in Phase 3
   .Contains(species)    => Type1 ∈ Types || Type2 ∈ Types              // either-type match
   .HasAnyIn(pool)       => any on-theme species in pool
static Biomes            // the registry — region ⇒ biome list
   .For(region)          -> BiomeDefinition[]                           // "Kanto OWNS these 18"
   .Playable(region,pool)-> BiomeDefinition[]                           // For(region) minus empty biomes
```

`Region` is an **enum** (not a record with an embedded list); the "a region owns its biome list" ownership is
expressed by the `Biomes.For(region)` lookup. Biome *archetypes* (forest, cave, shore) recur across regions, but
every instance is **region- and generation-scoped** — Johto later declares its own biome list over the Gen 2
dex, reusing the archetype, not the loop code. This is the same axis as the multi-generation roadmap, so it is
free forward-compat. **Region naming is flavour only** — a biome need not map to a real in-game place. The
`Neighbours` graph is authored now and guarded for symmetry + full connectivity by `BiomeTests`, but only
*traversed* in Phase 3.

**Seeded per run.** *Which* biomes appear and *how the graph wires up* is randomised per run, riding the per-run
seed that is already done end-to-end (`GameController.Start` → `SeededRandomSource`, `GAME_LOOP.md §6.4`) — same
seed ⇒ same map. **Status ✅ (2026-06-28):** each run draws a seeded **connected subset** of the playable set
(`Biomes.RandomConnectedMap`, `RunBiomeMapSize` = 10 of 18) at setup, threaded into the `RunDirector` as today's
`playableBiomes` — so *which* biomes appear varies per run (same seed ⇒ same map) and the route through it is
seeded too (`BiomeChoiceEvent` option sampling). Per-run graph **re-wiring** + §8 intersection mechanics remain
deferred (only if the subset draw alone doesn't give enough variety).

### 2.2 Pool membership rules (settled — ✅ implemented)

1. **Either-type match.** A species is in a biome's pool if **`Type1` OR `Type2`** is in the biome's theme
   (`BiomeDefinition.Contains`). Inclusive — dual-types aren't orphaned and naturally appear in several biomes
   (a Bug/Flying belongs in a forest *and* on a windy route).
2. **Wild-available only.** The pool is restricted to species with `AvailabilityType == "Wild"` in
   `PokemonGameAvailability` (`EncounterFactory.CreateEnemyAsync`) — excludes legendaries, statics, gifts, and
   fossils (19 of 151 in Gen 1), the canonical lucky-spike hazard. *Resilience:* if no availability rows exist
   (a minimally-seeded DB) the filter falls back to the full dex so the selector never starves. (Version-specific
   Red/Blue/Yellow filtering stays deferred — see `TODO.md` *Known Gaps*.)
3. **Empty biomes never generate.** A biome whose Wild pool is empty for the active generation is **excluded at
   map-generation time** (`Biomes.Playable` returns only non-empty biomes) — it simply isn't placed. Within a
   *valid* biome, if the target-BST band finds no candidate, the band **widens in-theme** to the nearest-BST
   themed species (`PickByBst`'s `MinBy` fallback when a `biome` is passed). **The theme is never broken** — you
   never face an off-theme creature inside a biome.

These rules live on `EncounterSelector.PickByBst` (optional `biome` filter) + `Biomes.Playable`, applied by
`EncounterFactory.CreateEnemyAsync` — see §6.

### 2.3 Kanto roster (18 biomes, all 15 Gen 1 types homed)

Wild-pool sizes verified against `pokemon.db` (distinct Wild species per either-match theme):

| # | Biome | Types | Pool | # | Biome | Types | Pool |
|--:|:--|:--|--:|--:|:--|:--|--:|
| 1 | Meadow Trail | Normal, Flying | 27 | 10 | Magma Ridge | Fire, Rock | 17 |
| 2 | Whispering Woods | Bug, Grass | 24 | 11 | Cinder Hollow | Fire, Ghost | 14 |
| 3 | Bramble Thicket | Grass, Poison | 38 | 12 | Haunted Spire | Ghost, Psychic | 13 |
| 4 | Mire Swamp | Poison, Ground | 45 | 13 | Phantom Marsh | Ghost, Poison | 33 |
| 5 | Crystal Cavern | Rock, Ground | 14 | 14 | Tranquil Lake | Water, Psychic | 34 |
| 6 | Sunbaked Canyon | Ground, Fighting | 20 | 15 | Frostbound Shore | Water, Ice | 27 |
| 7 | Granite Cliffs | Rock, Flying, Fighting | 27 | 16 | Glacier Hollow | Ice, Dragon | 5 |
| 8 | Storm Plateau | Electric, Flying | 23 | 17 | Abyssal Reef | Water, Dragon | 30 |
| 9 | Sparkwire Ruins | Electric, Psychic | 18 | 18 | Verdant Glade | Grass, Normal, Bug | 44 |

Every biome is non-empty; the thinnest (Glacier Hollow, 5) is a deliberate rare biome.

### 2.4 Spread — handling flooding *and* thinning

Wild-species counts per type (either-match): Poison 33, Water 27, Normal 20, Flying 15, Grass 14, Ground 14,
Bug 12, Fire 11, Psychic 10, Electric 8, Rock 6, Fighting 6, Dragon 3, Ghost 3, **Ice 2**. With flat
either-match, the only lever is *how many biomes each type appears in*. Every type lands in **2–3 biomes** —
none stuck in one, none in more than three:

| Type | Biomes | Type | Biomes | Type | Biomes |
|:--|--:|:--|--:|:--|--:|
| Poison | 3 | Ground | 3 | Fighting | 2 |
| Water | 3 | Rock | 3 | Fire | 2 |
| Flying | 3 | Psychic | 3 | Electric | 2 |
| Grass | 3 | Ghost | 3 | Ice | 2 |
| Normal | 2 | Bug | 2 | Dragon | 2 |

- **Flooding** — broad types (Poison 33, Water 27) sit in only 3 of 18 biomes, each paired with a narrower
  partner that gives the biome its character. A run threading ~5–6 biomes meets at most a couple of
  poison/water-heavy pools, not a soup throughout.
- **Thinning** — Electric now lives in **two** biomes (not one electric-only pool); Ice (2), Dragon (3), and
  Ghost (3) each get two homes, always paired with a carrier (Water/Psychic/Fire) so the biome is never
  razor-thin and never trips the "don't generate empty biomes" rule.
- **Known limit (not a Phase 1 blocker).** Pool *sizes* still vary (Mire Swamp 45 vs. Glacier Hollow 5) —
  inherent to flat either-match. The depth-scaled BST band + enemy tier (§3) narrow what actually spawns;
  per-type *weighting* (a mon whose primary type is on-theme spawns likelier than an incidental secondary match)
  is a deferred tuning lever if biomes still feel uneven.

---

## 3. Enemy generation — a strength-tier interface  *(Phase 2 spec)*

Enemy strength is a **strategy seam** — `IEnemyArchetype` — with implementations **Weak / Medium / Strong /
Boss**. Each tier is its own class that decides *which levers it pulls*; it does not hardcode a stat block.

### 3.1 Shape — archetype returns a spec, the factory builds  *(✅ implemented — `EnemyArchetype.cs`)*

The archetype is a **pure function** of the run context, returning a lever spec the factory consumes — so the
tiers are DB-free and unit-testable ("Boss at depth 5 → these levers", no database).

```
record EnemyContext   ( int PlayerLevel, int PlayerBst, int Depth, IRandomSource Rng )
record EnemyTierSpec  ( int TargetBst, int Level, DvQuality Dvs, MoveSelectionStrategy Moves, int MoveCount )
interface IEnemyArchetype { EnemyTierSpec Build(EnemyContext ctx); }   // Weak/Medium/Strong/Boss
// EnemyArchetypes.{Weak,Medium,Strong,Boss} singletons; Default = Medium.
```

The archetype rolls the final `Level` (the depth-scaled band ± its tier offset) and computes the final
`TargetBst` (the depth baseline × its tier multiplier); the BST band *width* stays fixed inside `PickByBst`.
`EncounterFactory` turns the spec into a `Creature` (DB + construction stay in the factory). This composition
layer is **web/run-layer** — tier banding is roguelite tuning, not a Gen 1 mechanic, so it stays out of the
battle seams (as `ScaleWildLevel`'s own doc comment already argues); only the *DV* lever crosses into a seam
(§3.3), because DV ranges are gen-specific.

### 3.2 Depth × tier (orthogonal)

`depth` is threaded `RunDirector → enemySupplier → CreateEnemyAsync`. Biome **depth** sets the *baseline band* —
`targetBst = playerBst + depth × K` and a depth-lifted level band (this **restores the depth curve** `TODO.md`
described as `lead BST + depth × 10`). The **tier** then shifts that baseline up/down. So "Medium @ depth 5" is
genuinely tougher than "Medium @ depth 1." **Phase 3c-2 ✅** replaced the original `battlesWon` proxy with
**biome-position depth** (`RunState.RunDepth` = *nodes traversed* — battle wins + interaction visits), so a foe
scales by how deep into the run/biome it sits; the Boss apex (last node) scales hardest. In the legacy chain
(battles only) `RunDepth == battlesWon`, so that path is unchanged.

### 3.3 The four levers

| Lever | Lands on | Weak → Boss |
|:--|:--|:--|
| **BST** | `PickByBst`'s explicit `targetBst` | tier multiplies the depth-scaled target (band width fixed in `PickByBst`) |
| **Level** | `ScaleWildLevel(depth)` | tier applies a flat level offset to the rolled band value |
| **DVs** | **`DvQuality{Poor,Average,High,Perfect}` on `IStatCalculator.RandomiseDvs`** | Poor 0–7 → Average 0–15 → High 8–15 → Perfect 15 |
| **Moveset** | `MoveSelectionStrategy` (see §3.4) + move count | Base → TmEnhanced → Optimal |

**DV lever — seam-clean.** `DvQuality` is *intent*; the Gen 1 mapping (Poor 0–7, Average 0–15, High 8–15,
Perfect 15, HP DV still derived from the four stat DVs' low bits) lives inside `Gen1StatCalculator`. Gen 3 (IVs 0–31) maps the
same intents differently. **Quality is always explicit** — the no-arg `RandomiseDvs()` is dropped; the player's
construction passes `Average` (still randomized within range, so same-tier creatures aren't clones; only Perfect
is deterministic).

### 3.4 Moveset levels (3-tier quality axis)  *(✅ implemented — `LearnsetMoveSelector`)*

Two new `MoveSelectionStrategy` values (`TmEnhanced`, `Optimal`) — deterministic top-N by a shared `MoveScore`
(power × STAB). **No level gate** — the strong/optimal
pools always pick the best moves for the creature's types; *level only drives stats*, so a boss-grade enemy can
punch above its level (intended).

| Level | Pool | Notes |
|:--|:--|:--|
| **Base** | species **level-up** learnset | current `CanonicalLatest` (player) / `WeightedSmart` (enemy) — unchanged |
| **TmEnhanced** | level-up **+ TM/HM-legal** same-type strong moves | needs real TM/HM data (§3.5) |
| **Optimal** | **any** move, best for the creature's types + coverage | the min-maxed boss-grade set |

### 3.5 Sub-task: import real TM/HM learnability *(gates TmEnhanced)*  *(✅ done — incl. re-import)*

`LearnsetMapper` originally kept **only** `move_learn_method == "level-up"`. Now it also keeps **machine**
(TM/HM) rows, tagged by a new `LearnMethod` field on `PokemonLearnset` (EF migration `AddLearnsetMethod`,
existing rows default `LevelUp`). A full `PokeApiConnector` re-import has been run — `pokemon.db` carries
**2,860 Machine rows across 145 species** (the 6 no-TM-learners like Caterpie/Magikarp have none) alongside the
989 level-up rows. Pinned by `LearnsetImportTests` (mapper) + `MigrationTests` (column + round-trip).
- ⚠️ **Integration hazard (two places change together):** every *level-up* path — base moveset selection,
  player setup, evolution, and `MoveLearning` on level-up — filters `LearnMethod == LevelUp` so TM rows can't
  leak into level-up learning. `CreateEnemyAsync` includes Machine rows **only** for the `TmEnhanced` tier.

### 3.6 Deferred (flagged)

- **Stat-Exp lever** — enemies use natural-gain-only for now; pre-seeding trained Stat-Exp is a later tuning lever.
- **Boss ceiling** — Boss's distinctive design (out-classing the player: can exceed player level, perfect DVs,
  optimal coverage, BST above band) is **revisited in a later phase**; Phase 2 ships Boss as a modest bump over
  Strong.
- **Tier *selection*** — which tier per encounter is **Phase 3** (node types pick it). `CreateEnemyAsync` gains
  an optional `IEnemyArchetype` (default Medium ≈ today at depth 0), the same seam pattern as the biome param.

---

## 4. Acquisition — two gated channels, fought-only

Acquisition is **not** a free in-battle catch. Two channels, each gated on a condition, both bounded by the
**offer-from-fought-only** guardrail (you can only acquire from species you actually faced in the current
biome — already on-theme, and impossible to roll something far outside the band you fought):

| Channel | Gate | What it offers |
|:--|:--|:--|
| **Boss catch** | after a biome **boss** battle | *n%* chance at an in-battle catch of **that boss** — the themed apex; a strong, earned pickup |
| **Themed draft** | every **3rd** encounter | *n%* chance to pick a creature themed to the current biome, bounded by its fought pool |

- ***n%* values are tunable placeholders.** Concrete rates are an implementation/tuning concern, not this
  design pass — they get set against the real curve once the channels exist.
- Boss catch is the **in-battle catch** channel (Gen 1 catch-rate formula — see `TODO.md` → *Catch / Poké Ball
  effect*); the themed draft is the **curated post-battle offer** channel. Both feed the eventual party.
- This is the design that **unblocks** the deferred *Item Acquisition · Bag Persistence · Catch* cluster in
  `TODO.md`, and the dormant **stone evolutions** (a bag-gated acquisition consumer).

---

## 5. Node types — bones for every kind, now

Scaffold every node kind up front (data + event stub), flesh out behaviour later. **All scaffolded ✅ (Phase 3c);
the interaction kinds are reachable bones awaiting behaviour:**

| Node | Kind (`GAME_LOOP.md` taxonomy) | State |
|:--|:--|:--|
| Wild battle | loop-event | ✅ `BattleRunEvent` (Normal tier) |
| Elite / Boss | loop-event | ✅ `BattleRunEvent` on `EncounterTier` Elite/Boss (3c-1); **Boss caps each biome** |
| Rest / Poké Center | interaction-event | ✅ `RecoveryRunEvent` |
| Shop | interaction-event | ✅ **Run Economy → Shop** — `ShopRunEvent`: rolls run-scaled stock (`ShopCalculator`), then a spend-gold buy loop (`ShopOffered` → `ChooseShopActionAsync`, buy/leave) against the `Wallet`/`Bag`. **Affordability-gated:** a biome only keeps a Shop node if the wallet clears the cheapest stock price (`ShopCalculator.MinItemPrice`) when the route is fixed at biome entry — so a broke player never gets a dead, all-unaffordable shop (the opening 0₽ node is never a shop). Purchases respect the Gen 1 **99-per-slot** bag ceiling (`Bag.MaxPerSlot`) — a buy that would overfill is refused before charging |
| Mystery / Event | interaction-event | ✅ **Run Economy → Reward Choice** — `RewardRunEvent`: rolls a wildcard reward (sometimes nothing), else offers a **pick-one-of-N** (`RewardChoiceOffered` → `ChooseRewardAsync`) — one item or the gold bag |
| Treasure / Reward | interaction-event | ✅ **Run Economy → Reward Choice** — `RewardRunEvent`: always rewards; offers a **pick-one-of-N** (two rarity-rolled items or a larger gold bag) — the player takes one, never both |

Each node is an **`IRunEvent` returning a typed `Outcome`** (the target abstraction in `GAME_LOOP.md §3`). The
**biome's seeded node plan is walked by `chooseNextEvent` / `EventForNode`** — the *single* owner of sequence —
so nodes drop in without the loop body changing. `BattleRunner` has graduated into the **`RunDirector`** that
`GAME_LOOP.md §6 Q1` anticipated.

---

## 6. Mapping onto today's code

| This design | Lands on / replaces | Note |
|:--|:--|:--|
| Type-filtered biome pool + Wild filter | `Biome.cs` (new), `EncounterSelector.PickByBst` (biome param), `EncounterFactory.CreateEnemyAsync` | ✅ **done (Phase 1)** — biome param null until Phase 3 supplies one |
| Depth-scaled BST + level band | `ScaleTargetBst`/`ScaleWildLevel`, `CreateEnemyAsync` (`depth`), `BattleRunner` supplier | ✅ **done (Phase 2c)** — depth = `battlesWon` |
| `IEnemyArchetype` tiers + `TmEnhanced`/`Optimal` movesets | `EnemyArchetype.cs` (new), `LearnsetMoveSelector`, `EncounterFactory` | ✅ **done (Phase 2d)** — tier *selection* per encounter is Phase 3 |
| Event model + `chooseNextEvent` | `BattleRunner.RunAsync` (hardcoded `while`) → `RunDirector` + `RunLoop.cs` | ✅ **done (Phase 3a)** — `IRunEvent`/`Outcome`/`RunContext`, single sequencer; battle + recovery first-class |
| Biome graph map traversal | `RunDirector` walks a seeded route (`BiomeChoiceEvent` + `ChooseBiomeAsync` seam); threads the biome into `CreateEnemyAsync` | ✅ **done (Phase 3b)** — 3b-1 backend + 3b-2 map screen; biome mode live (`RunSetup.PlayableBiomes` → session → director; `BiomeChoiceModal`) |
| Node bones | new `IRunEvent` stubs (shop/treasure/mystery/elite/boss) + node-derived tier *selection* | ✅ **done (Phase 3c)** — 3c-1 seeded `BiomeNodePlan` dispatched by `EventForNode`, `EncounterTier` intent (core) → `EnemyArchetypes.For` (web), Boss apex per biome; 3c-2 tuned the interior distribution + biome-position depth (`RunState.RunDepth`) |
| Acquisition channels | deferred `TODO.md` Catch cluster | gated on §1–§5 |

Every touch reuses an existing seam or adds one in the established style; the core engine stays
generation-agnostic and data-agnostic.

---

## 7. Phased build (buildable order, not boil-the-ocean)

1. **Biome model + region grouping** (authored definitions) and the **type-filtered pool** in
   `EncounterSelector` / `EncounterFactory`. ✅ **DONE (2026-06-27)** — `Biome.cs` registry, the three
   membership rules, the verified 18-biome Kanto roster, and the live Wild filter; biome selection is the
   `CreateEnemyAsync` seam Phase 3 fills. (Specced in §2.)
2. **`IEnemyArchetype` tiers** (Weak/Medium/Strong/Boss) + **depth-scaled bands** — replaces flat `playerBst`.
   ✅ **DONE (2026-06-28).** All sub-steps shipped: (2a) real TM/HM learnability + re-import (§3.5) → (2b)
   `DvQuality` seam (4 bands) → (2c) `PickByBst` `targetBst` + `ScaleWildLevel` depth band + `battlesWon`
   threading → (2d) `IEnemyArchetype`/`EnemyTierSpec` + the `TmEnhanced`/`Optimal` moveset strategies.
   (Specced in §3.)
3. **Biome graph + `chooseNextEvent` / `RunDirector`** — map traversal; node kinds land as event stubs (bones).
   **3a ✅ DONE (2026-06-28):** the event model — `RunLoop.cs` (`RunState`/`RunContext`/`Outcome`/`IRunEvent`)
   + `RunDirector` (renamed from `BattleRunner`) holding the single `chooseNextEvent` sequencer, with battle
   and Poké Center recovery as first-class `IRunEvent`s. Behaviour-preserving (endless chain unchanged).
   **3b-1 ✅ DONE (2026-06-28):** the biome-traversal backend — `BiomeChoiceEvent` + `ChooseBiomeAsync` seam,
   the run charts a route through the biome graph (run model: region → choose biome → a per-biome **randomised
   4–6 themed events** capped by a Poké Center → choose the next biome's neighbours → repeat), the current biome
   threads into `CreateEnemyAsync`.
   Gated on a supplied playable set (legacy chain otherwise). **3b-2 ✅ DONE (2026-06-28):** biome mode is live in
   the app — `EncounterFactory.CreatePlayerSetupAsync` computes `Biomes.Playable(Kanto, wildPool)` (same Wild
   filter as encounter generation) into `RunSetup.PlayableBiomes`, threaded session → `RunDirector`; the
   `SignalRInput.ChooseBiomeAsync` override + `BattleHub.ChooseBiome` + the React `BiomeChoiceModal` (biome cards
   with type badges) make the route a real on-screen choice; `BiomeEntered` titles each leg. **3c-1 ✅ DONE
   (2026-06-28):** node-kind bones — a biome's route is a seeded `RunState.BiomeNodePlan` (`RunNodeKind`: wild /
   elite / boss battles + shop / treasure / mystery interaction bones) the director dispatches via
   `EventForNode`, each biome capped by a **Boss** apex (§4). Tier selection respects layering: the core passes a
   generation-agnostic `EncounterTier {Normal,Elite,Boss}` through the enemy supplier and the web maps it to an
   archetype (`EnemyArchetypes.For`), the same intent/mapping split as `DvQuality`. Interaction nodes each have
   their own `IRunEvent` (Treasure/Mystery → `RewardRunEvent`, Shop → `ShopRunEvent`, Rest → `RecoveryRunEvent`).
   **3c-2 ✅
   DONE (2026-06-28):** the interior distribution is now a battle-heavy tuned table (Wild 70 / Elite 18 /
   Treasure 6 / Shop 4 / Mystery 2, independent per slot) and the foe-scaling axis is **biome-position depth**
   (`RunState.RunDepth` = nodes traversed) instead of the `battlesWon` proxy (§3.2). **Phase 3 is complete** —
   the next phase is acquisition (§4 / item 4 below).
4. **Acquisition channels** (boss catch + themed draft, fought-only) — gated on (1)–(3) and the deferred Catch
   cluster.

Each phase is shippable on its own and tightens the existing endless chain toward the designed run.

---

## 8. Open / deferred

- **`n%` acquisition rates** — tuning, set during phase 4 against the real curve.
- **Multi-region** (Johto+) — biome sets per region; rides the multi-generation sprint.
- **Intersection mechanics** — exact rule for how two biomes' clusters join (shared node vs. cross-edge) is a
  phase-3 detail.
- **Currency** — ✅ done (**Run Economy → Reward Choice**): a transient per-run `Wallet`, earned from battle-win
  drops and Treasure/Mystery nodes via a **pick-one-of-N** offer (gold bag **or** one item, never both; web-layer
  `RewardCalculator` policy). **Shop economy** — ✅ done: the `ShopRunEvent` spends the wallet on a per-visit,
  run-scaled stock (`ShopCalculator` — rarity-derived prices, not the unaffordable Gen 1 `Item.Cost`).
