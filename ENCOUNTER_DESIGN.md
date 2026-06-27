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
seed ⇒ same map.

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

## 3. Enemy generation — a strength-tier interface

Enemy strength is a **strategy seam** — `IEnemyArchetype` — with implementations **Weak / Medium / Strong /
Boss**. Each tier decides *which levers it pulls* to compose an enemy; it does not hardcode a stat block.

| Lever | Composed from | Weak → Boss progression |
|:--|:--|:--|
| **Moveset quality** | `MoveSelectionStrategy` (`CanonicalLatest` / `WeightedSmart`, + a future "optimal") | sparse/suboptimal → optimal coverage |
| **DV / Stat-Exp** | `IStatCalculator.RandomiseDvs` / Stat-Exp | low rolls → max DVs + Stat-Exp |
| **BST band** | `EncounterSelector.PickByBst` window | tightens / raises by tier |
| **Level** | `EncounterFactory.ScaleWildLevel` band | tier offsets the band |

**Depth and tier are orthogonal.** Biome **depth** sets the *baseline band* — BST and level climb as the run
goes deeper (this **restores the depth curve** that `TODO.md` described as `lead BST + depth × 10` but which was
never actually coded; today `CreateEnemyAsync` passes raw `playerBst` with no depth term). The **tier** then
*modulates* within/above that band. So "Medium in biome 5" is genuinely tougher than "Medium in biome 1."

This composition layer lives in the **web/run layer** (alongside `EncounterFactory` / `ScaleWildLevel`), keeping
the engine generation-agnostic. Wild-level and tier banding are run-layer tuning choices, not Gen 1 mechanics,
so they stay out of the battle seams — exactly as `ScaleWildLevel`'s own doc comment already argues.

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

Scaffold every node kind up front (data + event stub), flesh out behaviour later:

| Node | Kind (`GAME_LOOP.md` taxonomy) | State |
|:--|:--|:--|
| Wild battle | loop-event | exists (`Battle`) |
| Elite / Boss | loop-event | bones — a battle variant on a tougher tier |
| Rest / Poké Center | interaction-event | exists (inline in `BattleRunner`) |
| Shop | interaction-event | bones |
| Mystery / Event | interaction-event | bones |
| Treasure / Reward | interaction-event | bones |

Each node is an **`IRunEvent` returning a typed `Outcome`** (the target abstraction in `GAME_LOOP.md §3`). The
**biome graph is walked by `chooseNextEvent`** — the *single* owner of sequence — so nodes drop in without the
loop body changing. `BattleRunner` graduates into the **`RunDirector`** that `GAME_LOOP.md §6 Q1` anticipates.

---

## 6. Mapping onto today's code

| This design | Lands on / replaces | Note |
|:--|:--|:--|
| Type-filtered biome pool + Wild filter | `Biome.cs` (new), `EncounterSelector.PickByBst` (biome param), `EncounterFactory.CreateEnemyAsync` | ✅ **done (Phase 1)** — biome param null until Phase 3 supplies one |
| Depth-scaled BST + level band | `CreateEnemyAsync` (raw `playerBst` today), `ScaleWildLevel` | add the depth term the TODO claimed existed |
| `IEnemyArchetype` tiers | new, web/run layer beside `EncounterFactory` | composes existing levers; reuses `MoveSelectionStrategy`, `IStatCalculator` |
| Biome graph + `chooseNextEvent` | `BattleRunner.RunAsync` (hardcoded `while` today) → `RunDirector` | per `GAME_LOOP.md §3` target |
| Node bones | new `IRunEvent` stubs | rest/battle already behave like events |
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
3. **Biome graph + `chooseNextEvent` / `RunDirector`** — map traversal; node kinds land as event stubs (bones).
4. **Acquisition channels** (boss catch + themed draft, fought-only) — gated on (1)–(3) and the deferred Catch
   cluster.

Each phase is shippable on its own and tightens the existing endless chain toward the designed run.

---

## 8. Open / deferred

- **`n%` acquisition rates** — tuning, set during phase 4 against the real curve.
- **Multi-region** (Johto+) — biome sets per region; rides the multi-generation sprint.
- **Intersection mechanics** — exact rule for how two biomes' clusters join (shared node vs. cross-edge) is a
  phase-3 detail.
- **Currency / shop economy** — what the shop spends and where it's earned, designed when the shop node is
  fleshed out.
