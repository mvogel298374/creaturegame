# GENERATION_PROFILE.md — the generation as a product-wide axis (design v1, 2026-07-29)

> **Audience:** whoever implements the generation switch.
> **Scope:** turning "generation" from a *battle-math* axis into a **product-wide** one — content, region,
> menus and look — while keeping the engine generation-blind.
>
> **See also:** `GENERATION_SEAMS.md` (the four battle seams + the §5.0 gen-agnostic checklist — read it first;
> this doc extends its model outward), `ENCOUNTER_DESIGN.md` (the biome/region layer this plugs into),
> `DEFINITION_OF_READY.md` (the DoR this design closes), `docs/TODO.md` → *Generation Profile* (the task entry).

---

## 0. Why this design exists (the gate)

`GENERATION_SEAMS.md §7` already names the missing piece:

> *"Generation selection: today everything defaults to the Gen 1 singletons. When more than one generation
> exists, a single composition point (where `Battle` and `Creature` are built) will choose the implementation
> set — still no branching inside the engine."*

That composition point does not exist yet, and — more importantly — **the four seams only cover battle math.**
Everything else in the product is Gen 1 *by assumption*: the species/move/item content is Gen 1 only because the
databases hold nothing else; the region is Kanto because `Biomes.Kanto` is the only roster; the UI has no
generation concept at all.

This design makes the generation a **first-class, player-selected run parameter** that selects one coherent
**profile**, and makes every implicit "it's Gen 1 because nothing else exists" assumption explicit.

**It does not build a second generation.** Gen 1 is the only real profile. *Upward compatibility* is the
deliverable.

---

## 1. Decisions locked with the user (2026-07-29)

1. **Design against Gen 1 alone.** No Gen 2 content. The Gen-2 *content/schema* work stays in `TODO.md` →
   *Multi-Generation*.
2. **Presentation is per-gen in both senses** — visual reskin **and** menu structure.
3. **The roguelite layer is FLAVOUR-ONLY.** Same node kinds, same run flow, same possibilities. Only *content*
   and *look* are generation-specific. This is what keeps **`RunRules` gen-neutral**, as
   `GENERATION_SEAMS.md` designed it.
4. **One generation per run, chosen at run start**, threaded exactly like `Difficulty`.
5. **Skin fidelity: Gen 1's layout *grammar*, not its palette.** Hard borders, square corners, blocky menus, the
   classic HUD arrangement — with a palette that stays readable. The authentic 4-colour DMG green was
   **rejected**: it would discard the per-type badge colours (`TypeBadge.css`) and the deliberate contrast tuning
   already in `index.css`.
6. **Menu structure: Gen 1's 2×2 command grid with today's four verbs** (`FIGHT`/`SWITCH`/`BAG`/`CHECK`). The
   literal `FIGHT`/`PKMN`/`ITEM`/`RUN` was **rejected**: `RUN` is not a turn action in this engine
   (`TurnChoice` is Move/Item/Switch/Struggle — `escapable` governs *move*-driven flee only), so it would mean
   adding a player flee feature end-to-end, which contradicts decision 3.

**Added 2026-07-31 (Stage 4 design v2 — see §7):**

7. **Per-gen presentation = adaptation, not restructure.** Every UI surface keeps its *bones* — the same
   general usability and idea — but each generation adapts each surface to its own idiom, and a generation
   *may* adjust surface-level functionality where its idiom calls for it. That is a **new, explicitly ratified
   decision per surface per generation**, not a standing licence: §2.3 still governs the run layer (which
   choices/nodes/verbs exist may not vary).
8. **Surface-by-surface joint iteration.** "Going over every detail in a short explainer will not be precise
   enough" (user) — Stage 4 is a framework plus an ordered surface catalog (§7.5); each surface's per-gen
   design is settled in a **joint mini-plan** when its turn comes. No surface is designed by fiat in this doc.
   The battle command menu is already settled: decision 6's 2×2 grid stands.
9. **The region map becomes a rigid tile grid** — authored grid geometry (server data), rendered as a classic
   Gen 1 Town Map (squares on a grid, orthogonal routes). **The grid renderer is a shared toolkit for all
   generations**, but the map-presentation seam is built so each generation can have a different map — using
   all, some, or none of the shared tools; how far a given generation loosens the grid rules is decided within
   *that* generation's map design. Gen 1 uses the strict grid.

**Added 2026-08-03 (Stage 4b's ratified token recipe — see §7.3):**

10. **"Kanto Sage," a strict colour-budget palette.** Ink-on-neutral (`#15130F` on white boxes over a `#C9D0C5`
    "Fog" field) everywhere, with colour spent on exactly four things: the HP bar (red `#D6362B`), the XP bar
    (blue `#2F63AF`), type badges (unchanged), and sprite art. Double-line window chrome (the RBY dialogue-box
    idiom) and an invert-block selection cursor, chosen over a single hairline frame and a blinking ▶ arrow.
    Settled interactively (a live mockup, not a prose description) per decision 8's own reasoning.

**Added 2026-08-05 (caught mid-sketch on Stage 4c's Town Map — see §7.4):**

11. **No smooth/organic/modern curves — but a curve used as a fixed, recognizable symbol is fine.** Gen 1's
    visual language is strictly rectilinear and grid-aligned for anything *shaped by hand* — hard corners,
    straight edges, blocky forms built from a tile grid. This governs every surface, including ones whose
    *subject* is naturally organic — a coastline, a body of water, foliage: a first Town Map sketch drew the
    coastline as a smoothed bezier blob and water as a continuous curved wave-line texture, and both were
    **wrong per this grammar**, rebuilt as a rectilinear (stepped, grid-aligned) coastline and a flat water
    tone (user's call, 2026-08-05: *"no smooth organic shapes! ... all graphical things for gen1 are purely
    made through simple graphics, including water"*).
    >
    > **Refined the next day (2026-08-06) — the rule targets organic/modern smoothness, not curvature
    > itself:** *"we aren't against ALL curves, just nothing modern, organic or non symbolic."* A curve is
    > fine when it **is** the symbol — a small, fixed, iconographic mark instantly read as one thing, the
    > same way Gen 1 itself draws a Poké Ball as a circle or a status icon as a simple glyph. What stays
    > forbidden is a curve doing *shape* work: a smoothed silhouette (an organic coastline), a soft modern
    > chrome corner, a gradient or flowing texture standing in for a material. Applied to the Town Map: the
    > water went from a continuous wave-*texture* (forbidden — a curve shaping a surface) to a uniform flat
    > tone sparsely broken up by small `~`-style wave *glyphs* (fine — each one a discrete, repeated,
    > instantly-legible symbol, exactly like `mapGlyphs.tsx`'s existing type icons).
    >
    Treat this as a standing constraint on every future surface in the §7.5 catalog, not a one-off fix to the
    map: if a design reaches for a curve, ask whether it is *one small fixed symbol* (allowed) or a
    *silhouette/texture/chrome* being smoothed (not allowed) — that distinction, not "curve vs. no curve," is
    the actual rule.

---

## 2. What a `GenerationProfile` is

### 2.1 The bundle

| Slice | Today | Under the profile |
|:--|:--|:--|
| Battle math | 4 seams, `Gen1*.Instance` defaults ✅ | selected by the profile; the seams themselves are unchanged |
| **Type roster** — which types *exist* | ❌ none. `DamageType` holds all 18 (incl. Steel/Dark/Fairy); nothing states Gen 1 has **15** | on the profile |
| **Content scope** — species/moves/items | ❌ none. Gen 1 only because the DBs hold only Gen 1 | ✅ `IContentScope` on the profile (Stage 2b) — a filter seam, documented no-op for Gen 1 |
| **Region + biome roster** | ⚠️ partial — `Biomes.For(Region)` exists; `Region` is an enum; `Biomes.Kanto` is the only roster | ✅ `Region` + `BiomeRoster` on the profile (Stage 3) — the run setup reads the roster, `Biomes.For` stays the one door |
| **Starter roster** | ❌ ~~hardcoded client-side~~ *(stale premise — see §6: the picker fetched the whole dex from the unscoped `/api/species`)* | ✅ the profile's species scope, served server-authoritatively by `SpeciesController.GetAll(?generation=)` (Stage 3) |
| **Presentation theme** | ⚠️ `:root` design tokens exist in `index.css`, but no switch, and the palette is modern dark-blue/red | on the profile |
| **Menu structure** | ❌ no abstraction | on the profile |

### 2.2 What the profile is **not** — the gen-invariant list

Named explicitly so it is not eroded by accident:

- **`RunRules`** — the roguelite dial bag (`XpMultiplierEarly/Late`, `BenchXpShare`). Deliberately *not* a
  generation seam; it is keyed to **`Difficulty`**, an orthogonal axis.
- **Node kinds** — Wild/Elite/Boss, Rest, Shop, Mystery, Treasure.
- **The biome-graph model** and the route-choice verb; boss-caps-a-biome; the opening-node rule.
- **The two acquisition channels** (themed draft, boss catch) and their cadence.
- **The event/wire model** and the `IRunEvent`/`Outcome` abstraction.
- **Party size 6.**

> A future generation adds **content and chrome, not loop mechanics.** If a later generation ever tempts us to
> change the loop, that is a **new decision**, not an extension of this one.

### 2.3 The boundary rule (the line decision 3 draws)

When something looks like it might be per-gen, apply this:

> **The *presentation* of a choice may vary by generation. *Which choices exist* may not.**

So: the battle command menu may be a 2×2 grid in one generation and a list in another (presentation). It may not
gain a `RUN` verb in one and lack it in another (that is a possibility, not a presentation). Likewise a shop may
*look* like a Poké Mart counter, but the Shop node exists in every generation.

This is the direct analogue of `GENERATION_SEAMS.md §4.2`'s "never branch on the generation" — same instinct,
applied one layer out.

---

## 3. The unfalsifiability problem — and `TestAltProfile`

> ⚠️ **This is the single biggest risk in the whole feature, and it is not optional to address.**

**You cannot prove a seam is generation-agnostic when only one implementation exists.** That is precisely the
trap `GENERATION_SEAMS.md §5.0.1` documents: two real leaks (the OHKO success condition, the Self-Destruct
Defense halving) that **passed review and passed tests**, sat in the codebase, and surfaced only in a later
audit. Both hid inside moves that exist in *every* generation — which is exactly why nobody looked.

A single-profile "generation system" is that failure mode at architecture scale: every seam looks fine, every
test is green, and the first real second generation discovers the whole thing leaks.

**The mitigation: a deliberately fake second profile, in the test project only.**

`TestAltProfile` flips a handful of values purely so every seam has **two** implementations:

| Slice | Gen 1 | `TestAltProfile` | What it proves |
|:--|:--|:--|:--|
| Accuracy scale | 0–255, 1/256 miss bug | 0–100, no bug | the scale is read, not assumed |
| Special stat | combined | split (Sp.Atk/Sp.Def) | `GetOffensiveStat`/`GetDefensiveStat` are actually routed through |
| Type roster | 15 | 17 (adds Dark, Steel) | nothing hardcodes "15 types" |
| Content scope | everything (identity) | only catalog ids ≤ 20 | every catalog read *asks* the scope — invisible under Gen 1's identity |
| Region roster | Kanto | a 2-biome fake region | `Biomes.For` is the only door |
| Theme + menu layout | Gen 1 grammar, 2×2 grid | a distinct token set + list layout *(client-side — the Vitest alt presentation, §7.2)* | the theme is data, not CSS defaults |
| Map | strict grid Town Map over Kanto's authored geometry | a 2-biome fake region with grid coords + one route; an alt map skin (client-side) | geometry rides the profile's roster; the renderer is selected by the registry, not assumed |

**Rules for it, so it can never be mistaken for real work:**
- It lives in `tests/`, never in shippable code, and is **never registered** in the runtime profile registry.
- It is **not Gen 2** and must not be named, documented, or discussed as if it were. It is a probe, not a plan.
- It carries no fidelity claim whatsoever — its values are chosen to be *different*, not to be *correct*.

**Each stage below lands with its leg of this profile.** A stage without one has demonstrated nothing.

---

## 4. Stage 1 — Generation as a run parameter

**Goal:** the profile exists, is chosen at run start, and Gen 1 reproduces today byte-for-byte.

**This is plumbing, not invention** — `Difficulty` (shipped 2026-07-22) already proved this exact rail
end-to-end:

```
StartGameRequest.Generation
  → GameController  (case-insensitive parse, falls back to Gen 1)
  → RegisterSession
  → PendingSession  (record already carries Difficulty; add Generation beside it)
  → AttachConnection
  → GenerationProfile lookup  (mirrors GameSessionManager.RunRulesFor / RunTuningByDifficulty)
  → threaded into EncounterFactory + Battle/Creature construction
```

**Shape:**
- `GenerationProfile` — a record bundling the four seams + the content/region/theme slices added in Stages 2–4.
  Start with just the seams; each later stage adds a property.
- A registry (`ProfileFor(Generation)`), the direct analogue of `RunTuningByDifficulty`.
- **Both the parse and the lookup `internal`**, so tests exercise the real code path rather than a duplicate —
  the gap `requirements-review` caught on `Difficulty` and the reason `ParseDifficulty`/`RunRulesFor` are
  `internal` today.

### 4.1 Where the seams enter (surveyed 2026-07-29 · **all four threaded as of Stage 1b**)

The survey below is kept as the historical record of *what was wrong*, because the pre-Stage-1 column is the
evidence for why the feature was needed — every one of these was Gen 1 by construction, and none of them would
have failed a test. All four are now read from the run's profile.

| Seam | Where it entered **before** Stage 1 | Now |
|:--|:--|:--|
| `ITypeChart` | `GameSessionManager.cs` — `Gen1TypeChart.Instance` passed positionally into the `RunDirector` | ✅ read from the profile (Stage 1a) |
| `IEvolutionRules` | `EncounterFactory` — `Gen1EvolutionRules.Instance` hardcoded in `ResolvePlayerEvolutionAsync` | ✅ read from the profile (Stage 1a); Stage 1b then widened that method to take the **whole profile**, so the generation used to query edges can't disagree with the rules used to judge them |
| `IStatCalculator` | `EncounterFactory.BuildCreature` — `new Gen1StatCalculator(rng)`, **seeded per creature** so a fixed seed reproduces DVs. (`Creature.StatCalculator`'s property default is only the unseeded fallback.) | ✅ `profile.BuildStatCalculator(rng)` (Stage 1b) — the profile exposes a **factory**, not a singleton, precisely so it can be seeded per run |
| `IBattleRules` | **nowhere in the web layer** — never passed at all; `Battle` fell back to `Gen1BattleRules.Instance` internally. The sharpest case: dropping the thread again would leave the whole suite green | ✅ `BuildRunOptions` sets `Rules = profile.BattleRules` (Stage 1a) |

**A fifth site, found during Stage 1a:** `EncounterFactory.ActiveGeneration = 1` — a hardcoded `private const int`
driving **six** learnset/evolution DB queries, plus a duplicate `PlayerOverviewDto.ActiveGeneration = 1`. Not a
seam, but a **second source of truth for the generation**, and the most concrete "Gen 1 by assumption" in the
repo. Folded into Stage 1b (see §4.5) — both consts are **deleted** as of Stage 1b (2026-07-29); the queries now
filter on `(int)profile.Generation` and `PlayerOverviewDto.From` stamps the run's real generation.

**A sixth site, closed by Stage 2b (2026-07-30):** the species / `PokemonGameAvailability` pool behind the
wild-encounter selector and the biome map was *not* generation-filtered — `ComputePlayableBiomesAsync` and
`CreateEnemyAsync` drew from the whole dex. That was never an oversight of Stage 1b (which scoped only the
learnset/evolution reads) nor of Stage 2a (the type roster); it was Stage 2b's content-scope work, and every
catalog read on the run path now goes through `profile.ContentScope` (§5(b)). The one species read still
unscoped at the time was `SpeciesController.GetAll`, the pre-run starter picker — handed to Stage 3, which is
where a generation first exists to ask. **✅ Closed by Stage 3 (2026-07-31)** — see §6.

### 4.5 Stage 1 is split — 1a (shipped) and 1b

**1a** did the axis and the `GameSessionManager` composition point: the profile type, registry, run-parameter
threading, and explicit reads of `TypeChart` / `BattleRules` / `EvolutionRules` / the AI.

**1b** ✅ **DONE (2026-07-29)** — `EncounterFactory`'s `IStatCalculator` thread *and* the `ActiveGeneration`
constant. Kept together because they were one coherent chunk in one file (every `BuildCreature` caller sat
directly beside an `ActiveGeneration` query), and split from 1a because it was ~39 call sites across 5 test
files, which would have buried 1a's architecture in mechanical churn. `ResolvePlayerEvolutionAsync` was widened
to take the whole `GenerationProfile` rather than a bare `IEvolutionRules` in the same pass, so the generation
used to query evolution edges and the rules used to judge them can never disagree. Falsification leg:
`TestAltProfile.BuildStatCalculator` now returns an `AltStatCalculator` stamping a sentinel DV of 99 on every
stat (previously it returned a plain `Gen1StatCalculator`, which made it useless as a probe). Full write-up →
`TODO.md` → *Generation Profile* → Stage 1b.

> **The parameters 1b adds must be `required`, never defaulted.** A `GenerationProfile? profile = null` with a
> `?? Gen1Profile.Instance` fallback would reintroduce §4.2's hazard at the very layer the feature is trying to
> close it — and would do so while looking like the existing house style.

### 4.2 ⚠️ The central hazard: the null-coalescing default hides a missed thread

`GENERATION_SEAMS.md §4.3` celebrates `rules ?? Gen1BattleRules.Instance` as "zero ceremony for the common
path" — and it is, **while Gen 1 is the only generation**. The moment a second profile exists, that same default
becomes the feature's sharpest failure mode:

> **A path that forgets to thread the profile does not crash, and does not fail a test. It silently runs Gen 1.**

There are **nine** such `?? Gen1*.Instance` fallbacks across `AttackAction`, `Battle`, `DamageCalculator`,
`StatusResolver` and `BattleRunEvent`, plus the `Creature.StatCalculator` property default. Each is an
independent opportunity for a future generation to be quietly half-applied — and every existing test stays green,
because Gen 1 *is* the expected value today.

**This is §5.0.1's lesson at architecture scale**, and it is the concrete reason `TestAltProfile` (§3) is
ship-blocking rather than nice-to-have: an alternate profile is the *only* thing that can observe a silent
fallback, because it is the only context where "we got Gen 1" is a wrong answer.

**Do not "fix" this by deleting the defaults.** They carry the library's test ergonomics and every direct `Battle`
caller. The fix is that the **web composition root passes all four explicitly**, and the harness proves it did.

### 4.3 A fifth candidate to decide on — the AI

`GameSessionManager.cs:168` builds `new AiBattleInput(new Gen1TrainerAi(rng: session.Rng))`. It is
**generation-named but is not one of the four seams**. Stage 1 should make an explicit call: either put the AI on
the profile too, or record that AI behaviour is deliberately gen-invariant in this roguelite. Left undecided it
becomes exactly the kind of implicit Gen 1 assumption this whole feature exists to remove.

### 4.4 Acceptance and tests

**Acceptance:** a Gen 1 run is **byte-for-byte identical** to today — verified the way `DifficultyTests` verified
the Normal preset was a true no-op (that test compared the preset's values field-by-field against the old
hardcoded ones; do the same for the profile).

**Falsification leg:** a test asserting `TestAltProfile` produces different battle math through the *same*
`RunDirector`/`Battle` construction path, with no engine change — which is simultaneously the regression test for
§4.2's silent-fallback hazard.

---

## 5. Stage 2 — content scope

**Goal:** establish *where* content filtering is asked for, so a later generation has a socket. Mostly stubs.

**Two halves, deliberately split:**

**(a) The type roster — real work, do it now.** ✅ **DONE (2026-07-30, Stage 2a).** `DamageType` holds all 18
types and stays gen-blind (correct — the enum is a vocabulary, not a claim). What was missing is a statement of
**which types exist in this generation**. Gen 1 = the 15. This matters concretely: `ENCOUNTER_DESIGN.md §2.3`'s
Kanto roster is sized so that **all 15 Gen 1 types are homed**, an invariant a 17-type generation would
re-derive.

**As built:** `GenerationProfile.TypeRoster` (`required IReadOnlySet<DamageType>`); `Gen1Profile` supplies the 15
in `DamageType` declaration order, so a reader diffing it against the enum sees exactly three members missing.
The consumer is the region-content invariant, now production code: **`Biomes.UnhomedTypes(region, roster)`**
(plus `Biomes.HomedTypes(region)`), which takes the roster as an **argument** rather than assuming 15 — that
parameter *is* the upward compatibility, since a later generation must re-derive the check, not inherit Gen 1's
answer. *(As-built note: Stage 3 (2026-07-31) re-signatured both from `Region` to a biome-roster parameter —
`UnhomedTypes(biomes, roster)` / `HomedTypes(biomes)` — so the check runs against the profile's `BiomeRoster`;
see §6.)* `BiomeTests`' own hardcoded 15-type array is **deleted**; it now reads `Gen1Profile.Instance.TypeRoster`.
That array was a second source of truth for the roster, the same species of hazard Stage 1b removed when it
deleted `EncounterFactory.ActiveGeneration`.

> **What deliberately did NOT change: the client's three per-type tables — and only one of them is a vocabulary.**
> An earlier draft of this note claimed they all "keep every type"; `requirements-review` found that false, and it
> is corrected here rather than quietly deleted, because the wrong version is the interesting part.
>
> | Table | Keys | What it actually is |
> |:--|:--|:--|
> | `components/TypeBadge.tsx` — `TYPE_COLORS` | **18** + grey fallback | a genuine vocabulary, gen-blind like `DamageType` |
> | `battle/bossTrainer.ts` — `NAMES_BY_TYPE` | **15** + `GENERIC` fallback | a second copy of "Gen 1's roster" |
> | `pages/mapGlyphs.tsx` — `TYPE_ICON` | **15** + `t-Normal` fallback | a third copy, and it says so: *"all 15 Gen 1 types"* |
>
> So two of the three are exactly the second-source-of-truth hazard Stage 2a deleted from `BiomeTests` — still
> standing, on the client. (The repo already half-knew: `bossTrainer.test.ts` carries a `// no Gen-1 Steel pool`
> comment.) They are **not** harmless-by-nature; they are harmless-for-now because each falls back gracefully, so
> an unknown type degrades to a generic name / the Normal glyph rather than breaking.
>
> **Handed to Stage 4, deliberately, not waived (user's call, 2026-07-30).** Wiring them to the roster requires
> the client to *have* the roster, and the mechanism for that — the client learning the run's generation over
> route state **plus** a server echo — is precisely what §7.2 (sub-stage 4a) builds. Doing it here would
> duplicate that channel before it exists. **§7.2 names both tables as 4a's wiring work**, so this is a tracked
> handoff, not an archaeology dig.
>
> **✅ Closed by Stage 4a (2026-07-31)** — both tables are now measured against the profile's roster over the
> server echo; see §7.2.

**Honest scope note:** nothing in the *runtime* decides anything from the roster yet — the wild-encounter pool
and the biome map are gated on content, which is (b) and Stage 3. Stage 2a makes the roster a stated fact with a
checked invariant and a substitutable value; it does not claim more.

**(b) Species / move / item filtering — documented no-op stubs.** ✅ **DONE (2026-07-30, Stage 2b).** Per
`GENERATION_SEAMS.md §5.0`:

> *"If 'yes' but you're not building Gen 2 yet: still add the seam member, implement the Gen 1 value, and — when
> the data layout is what differs — make the Gen 1 implementation a documented stub that shows the generic
> shape."*

**As built:** `IContentScope` — three accessors (`Species`, `Moves`, `Items`), each `IQueryable<T> → IQueryable<T>`
— on the profile as `GenerationProfile.ContentScope`. `Gen1ContentScope` is the documented stub: every accessor
returns its query untouched, and its doc names the exact fix (`all.Where(x => x.GenerationIntroduced <= 1)`,
`<=` not `==`) and the fact that it becomes **wrong** the day a second generation's rows are imported. The actual
`GenerationIntroduced` columns and filtered queries are **importer/schema work** and stay in `TODO.md` →
*Multi-Generation*, explicitly sequenced after this.

**`IQueryable`, not a predicate — the choice that makes the stub more than a gesture.** A
`Func<PokemonSpecies, bool>` would materialise the whole table before filtering and would have to be re-plumbed
when the real filter lands. Composing onto the query means the eventual `Where` is translated to SQL by EF and
the out-of-scope rows are never read. So the seam is already the right *shape*; only the implementation is
outstanding.

**All eight catalog reads in `EncounterFactory` go through it** — the starter lookup, the run's move pool, the
run's item catalog, the biome map's species pool, the wild-encounter pool, the draft's fought pool, the
boss-catch lookup and the evolved-form lookup. The rule enforced is *"no unscoped catalog read in this file"*,
including the one read (the evolved form) where the scope is arguably redundant because the evolution edges are
already generation-filtered: a rule a reviewer can check at a glance beats a judgement call that must be re-made
per site, and the redundancy costs nothing.

**The stub's premise had to be made true first.** `Gen1ContentScope`'s identity is justified by *"the catalogs
hold one generation's content"* — and when the seam was written that was **false**: `items.db` held **Max
Revive**, a Gen-2 item imported as forward scaffolding (`DATA_IMPORT.md` §4.5) and kept away from players by a
name-matched hold-out in `RewardCalculator.UsableItems`. So the identity was "correct" only via a second,
unrelated mechanism — the exact duplicated-source-of-truth hazard this feature keeps deleting.
`requirements-review` caught it. **User's call (2026-07-30): fix the premise, not the wording** — the item was
removed from the import roster and from `items.db`, and the hold-out deleted, so reward eligibility is
categorical again. It returns through the per-generation item schema (`TODO.md` → *Multi-Generation* →
*Per-generation ITEM data*, added for this). The rule it establishes is worth carrying forward: **the scaffolding
a future generation needs is the schema, not a stray row** — a row that cannot say which generation it belongs to
is indistinguishable from Gen 1 content to every consumer not specially taught otherwise.

**What the scope deliberately does not cover, and why that is not an omission:** learnsets and evolution edges
carry an explicit `Generation` column and Stage 1b already filters their six queries on it — they solved the
problem this seam is a placeholder for. `PokemonGameAvailability` is keyed by species id and only ever
intersected with the already-scoped species pool.

**One consequence worth naming:** `ComputePlayableBiomesAsync` was explicitly *not* generation-scoped before
(its own doc comment said so), and now is — so a generation gets **the biomes its own content can fill**, since
a biome with no on-theme species in scope is not playable. That connects Stage 2a's roster invariant to a real
runtime decision for the first time.

> **Why stubs rather than nothing:** the alternative is that a future gen has to find every unfiltered
> `ToListAsync()` by archaeology. `GENERATION_SEAMS.md` calls this "cheap now and removes a future archaeology
> dig" — that judgement is already repo policy, not a new claim.

> **Handed to Stage 3, not waived: `SpeciesController.GetAll`.** The starter picker serves the whole dex,
> unscoped, and is the one species read on no run path at all — it answers *before* a run exists, so there is no
> profile to ask. Scoping it needs the generation to be known at pick time, which is exactly what Stage 3's
> server-authoritative starter roster establishes. Tracked in §6 rather than left for archaeology.
> **✅ Closed by Stage 3 (2026-07-31)** — the request names the generation; see §6.

**Falsification leg:** ✅ shipped with 2a. `TestAltProfile.TypeRoster` is Gen 1's 15 **plus Dark and Steel** —
built by adding to Gen 1's set rather than re-listing 17 by hand, so the two rosters can't drift into an
accidental difference unrelated to the probe. Kanto homes neither extra, so
`BiomeTests.UnhomedTypes_IsMeasuredAgainstTheProfilesRoster_NotAFixedGen1List` asserts exactly `[Steel, Dark]`
comes back. **Verified by sabotage:** re-hardcoding Gen 1's roster inside `UnhomedTypes` fails that one test
while all 25 other biome tests — `Kanto_HomesEveryGen1Type` included — stay green, which is the concrete
demonstration that the Gen 1 case alone could never have caught it.

**Falsification leg (b):** `TestAltProfile.ContentScope` admits only catalog ids ≤ 20 — species, moves and items
alike. An id ceiling is nothing like a real generation's rule, which is the point: it cannot be mistaken for the
`GenerationIntroduced` filter, while sharing its shape (a `Where` composed onto the query). **One probe per
catalog read, not per method**, because Gen 1's scope is an *identity function* — a site that skipped it
entirely behaves identically to one that uses it, so only a site-by-site probe can tell them apart. Each probe
carries its own Gen 1 control, so none can pass by the read being broken for everyone.

**Verified by sabotage, twice.** Reverting all eight sites to unscoped queries fails **exactly** the eight new
probes while all eight Stage 1b probes stay green. Then reverting *only* `ComputePlayableBiomesAsync` fails
**exactly one** test — proving the biome probe pins its own read rather than riding on the starter-species probe
that shares its entry point.

Two call sites needed a sharper instrument than the ceiling, and the reason is recorded because it is easy to
re-derive wrongly: **the biome map** (ids 1–20 still cover enough Kanto themes to leave more than
`RunBiomeMapSize` biomes playable, so the map comes back capped either way — measured, not assumed; narrowing to
the first five species leaves seven) and **the evolved form** (every Gen 1 evolution line starting under id 20
also ends under it). Both use a small predicate scope defined in the test file. A third interaction is worth
knowing about for the next stage: adding the scope to `TestAltProfile` **broke a Stage 1b probe**, which caught
a boss species (Gyarados, 130) that the new scope filters out — the probe was fixed to use an in-scope species,
since the stat seam is what it tests. Expect each future slice to have this effect on the existing probes.

---

## 6. Stage 3 — region, biomes, starters ✅ DONE (2026-07-31)

**Goal:** the region and starter set come from the profile.

**Smaller than it looks** — the socket already existed. `creaturegame/Creatures/Biome.cs` has a `Region` enum, a
`BiomeDefinition` record, and **`Biomes.For(Region)`**. `ENCOUNTER_DESIGN.md §1` anticipated exactly this:

> *"a new region is largely a new biome set, not new loop code."*

**As built — the region half.** Two slices, not one: `GenerationProfile.Region` (**identity/presentation only,
never branched on** — the `Generation` sibling, kept for logging and §7.2's client echo) and
`GenerationProfile.BiomeRoster` (the consumed content — `Gen1Profile` reads it through `Biomes.For(Region.Kanto)`,
which **stays the only door** to the authored registry). The plan's literal text named only `Region`; the second
slice was escalated by `requirements-review` and **ratified by the user (2026-07-31) as the pattern for future
region-bearing slices: one identity enum + one falsifiable content list.** **The roster is the falsifiable slice, and that is why it
exists:** the `Region` enum has a single member, so a profile carrying only the enum could never be told apart
from a hardcoded Kanto — only a substituted biome *list* can observe the thread. The pair can't drift: a
`GenerationProfileTests` coherence pin asserts every rostered biome carries the profile's region.
`Biomes.HomedTypes`/`UnhomedTypes`/`Playable` were re-signatured from `Region` to a biome-roster parameter — a
low-blast-radius change, though not uniformly so: `HomedTypes`/`UnhomedTypes`' callers were one-day-old test code
(Stage 2a), while `Playable`'s sole caller is the month-old production `ComputePlayableBiomesAsync`, updated in
the same diff — so the §5(a) coverage invariant and the playability filter run against
whatever roster a profile supplies; `EncounterFactory.ComputePlayableBiomesAsync` now reads `profile.BiomeRoster`,
deleting the repo's **last hardcoded `Region.Kanto` outside the authored registry** — the biome-layer sibling of
Stage 1b's `ActiveGeneration` deletion.

**As built — the starter half, and a premise correction.** This section previously claimed the starter roster was
*"hardcoded client-side in `StarterSelection.tsx`"* — **that was stale**: the picker has always fetched the full
dex from `/api/species` (the Stage 2b handoff below) and any species is pickable, which is the roguelite's
deliberate design and is unchanged. What "server-authoritative starter roster" concretely meant, therefore, was
scoping that endpoint: `SpeciesController.GetAll` now takes `?generation=`, parses it with **the same boundary
contract as game start** (`GameController.ParseGeneration` — missing/unrecognised ⇒ Gen 1, so a stale client
keeps seeing today's dex), resolves the profile, and serves its `ContentScope`-scoped dex through a named
`SpeciesSummaryDto` (wire-verified live: byte-identical camelCase JSON, 151 rows, `?generation=one` parses).
**There is no curated per-gen starter subset** — the starter roster *is* the generation's species scope; a
curated trio would be a new design decision, not part of this stage. The wrong premise is kept visible here
(same policy as §5(a)'s corrected claim) rather than silently rewritten. **Ratified by the user (2026-07-31,
via `requirements-review` escalation):** "starter roster" means the whole dex scoped per generation — the
roguelite's any-species-pickable design stands, and no curated-starters feature is planned; if one is ever
wanted it is a new TODO item, not a reopening of this stage.

> **The Stage 2b handoff is closed.** `SpeciesController.GetAll` was the one species read on no run path — it
> answers *before* a run exists, so there was no profile to ask. The request naming the generation is what
> resolves that structural gap, and the run-start starter lookup (already scoped since 2b) stays the enforcing
> validator of the eventual pick.

**Backend-only, as designed.** Zero importer/DB change; the client is untouched — it sends no `generation` yet
because no picker UI exists until Stage 4 (§7.2–7.3), and the fallback contract makes that a Gen 1 no-op.

**Falsification legs (verified by sabotage, twice):** `TestAltProfile.BiomeRoster` is a connected two-biome fake
region, its themes chosen from the probe's own constraints (fillable by wild-available species with ids ≤ 20, so
the alt content scope can't starve the map for an unrelated reason) — and deliberately **thinner than
`RunBiomeMapSize`**, so the run-map probe simultaneously pins the watch note below. Re-hardcoding Kanto inside
`ComputePlayableBiomesAsync` fails exactly the new run-map probe while the 43 other biome/profile tests stay
green; unscoping the dex read fails exactly the `DexFor` probe. Rider cleanups shipped alongside (the learnset
query dedup, the `Registered` allocation) are archived in `TODO_ARCHIVE.md` → *Tech-Debt cleanups*.

**Watch (now pinned, not aspirational):** `Biomes.Playable` / `RandomConnectedMap` keep working off whatever
roster the profile supplies — a roster below the map cap yields itself as the whole run map
(`RandomConnectedMap` returns everything when the count exceeds the pool), asserted by the two-biome probe.

---

## 7. Stage 4 — presentation: per-generation UI + the Town Map (design v2, 2026-07-31)

> `/plan` v2 — decisions 5–9 above. **Supersedes the v1 sketch (2026-07-29)**, which scoped this as a one-shot
> "Gen 1 grammar" restyle plus a whole-menu-surface redesign executed in a single pass. What v2 changes: the
> per-surface designs are **not** settled here — they are settled jointly, one surface at a time (decision 8) —
> and the region map redesign (the grid Town Map, decision 9) joins the stage. What v2 keeps: the theme-token
> mechanism, the generation channel, the 2×2 battle command grid (decision 6), and the boundary rule.

This is the largest new surface and the only stage with no existing precedent in the repo. It is built as
**three infrastructure sub-stages (4a–4c) plus an open-ended, jointly-iterated surface catalog (4d+)** — each
sub-stage independently shippable and separately greenlit.

### 7.1 The shape — bones invariant, per-gen adaptation, jointly iterated

Decision 7 refines §2.3 rather than replacing it. Three tiers:

| Tier | Rule | Example |
|:--|:--|:--|
| **Run layer** (choices, nodes, verbs, prompts) | invariant across generations — §2.3 unchanged | no gen gains a `RUN` verb or loses the Shop node |
| **Surface bones** (what a screen is for, its general usability) | invariant — every gen has a battle menu, a bag view, a party view, a map | the bag lists items and uses one; that never varies |
| **Surface idiom** (layout, chrome, and *surface-level functionality*) | **per-gen**, one ratified decision per surface per generation | Gen 1's battle menu is a 2×2 grid; a later gen's may differ |

"Surface-level functionality may vary" (decision 7) is deliberately narrow: it admits things like a
generation's overview screen exposing an extra per-gen stat panel — it does **not** admit new run
possibilities. When a surface iteration proposes a functionality difference, that proposal is escalated in the
joint mini-plan, never assumed. Every modal stays `'blocking'` by construction (each parks a server-side
await — see `TODO.md` → *Tech Debt*, the `<Modal>` refactor); a redesign may restyle and re-lay-out them but
must not make one dismissable.

### 7.2 Sub-stage 4a — the generation channel + the client presentation registry ✅ DONE (2026-07-31)

> **As built:** the echo carrier is a new session-layer event, **`RunPresentationRevealed(Generation,
> TypeRoster)`** — emitted on *every* hub attach (first connect, ahead of the run task's events, and the
> reconnect rebind branch), built by the pure `GameSessionManager.BuildPresentationEvent(profile)`. The client
> half is `src/generations/presentation.ts` (registry + `applyGenerationTheme` + the roster-coverage check);
> the two 15-type tables are re-framed as asset inventories measured against the delivered roster
> (`hasBossNamePool` / `hasTypeIcon`). Falsification legs shipped server-side (TestAltProfile's 17-type roster
> through `BuildPresentationEvent`) and client-side (alt-registry + alt-roster Vitest probes). Verified live
> over the hub on both attach paths. Full record → `TODO.md` → *Generation Profile* → Stage 4a.

The infrastructure everything else stands on. **Where the client learns the generation — two paths, and it
needs both:**
- **Immediate:** the client picked it, so it rides the existing route state — `StarterSelection.tsx` already
  does `nav('/battle', { state: { species, gameId, level } })`; add `generation`.
- **Authoritative:** the server must also echo it, or a **reconnect** re-mounts `BattleScreen` with no route
  state and the theme silently reverts. This repo has been bitten by exactly this class of bug before — see the
  Settings-modal trap in `TODO.md` (a page nav tore down the live SignalR connection). Treat the server echo as
  required, not optional. *(Concrete carrier is a 4a implementation decision: candidates are a field on an
  existing at-attach event or a small run-presentation event emitted on attach/re-attach, with the REST
  fallback already in place — `PlayerOverviewDto` has stamped the run's generation since Stage 1b. Whichever
  carrier, it takes the standard emitter projection + field-guard treatment.)*

**The echo's payload is the generation id plus the profile's type roster.** The roster half closes the Stage 2a
handoff: `battle/bossTrainer.ts`'s `NAMES_BY_TYPE` and `pages/mapGlyphs.tsx`'s `TYPE_ICON` each independently
encode "the 15" (see §5(a)'s survey), the same second-source-of-truth hazard Stage 2a removed from `BiomeTests`.
**4a wires both to the delivered roster.** Both currently degrade gracefully (a generic name / the `t-Normal`
glyph), so this is a consistency fix, not a bug fix. `TypeBadge.tsx` (18 colours) is a real vocabulary and
stays gen-blind.

**The client presentation registry** — the client-side analogue of `GenerationProfiles`: a `src/generations/`
module mapping generation id → a `GenerationPresentation` (theme selector, menu-layout descriptors as the
surface catalog grows, map presentation per §7.4). The document root carries `data-generation`; CSS override
blocks key off it. Components read the registry, never a hardcoded default.

**Falsification leg (client-side):** `TestAltProfile` is C#-side and cannot probe the client, so 4a lands its
analogue in Vitest — a test-only alt `GenerationPresentation` proving the theme attribute, the roster-driven
tables, and (later) the menu descriptors are read from the registry, not baked in. Same rules as §3: never
shipped, never named as a real generation.

### 7.3 Sub-stage 4b — the Gen 1 skin: grammar, not palette

The hook already exists: `src/index.css` declares a full design-token block under `:root` (`--clr-*`, `--font`,
`--fs-*`, `--sp-*`, `--border-w`, `--radius`). Every screen reads them. Note `--radius: 0px` is already
commented *"square = classic pixel-game feel"* — the intent is there; the palette is not.

**Approach:** keep `:root` as the shared *structural* token set; add a per-generation override block selected by
4a's `data-generation` attribute. Gen 1 supplies: hard 2px borders, square corners, blocky menu boxes, the
classic HUD arrangement, and a palette that evokes RBY without collapsing to four greens.

**Explicitly preserved** (this is why decision 5 rejected authentic DMG):
- **Per-type badge colours** (`TypeBadge.css`) — they carry real information.
- **The contrast tuning** in `index.css` — the comments record deliberate legibility work
  (*"brightened for small-stat legibility"*, *"nudged up from #555 for contrast"*). Do not regress it.

**Theming the picker itself:** the generation is chosen *on* `StarterSelection`, so that screen cannot already
be themed by the choice. Boot in the default profile's theme and let the picker **live-preview** on change —
near-free with CSS custom properties, and it makes the choice legible before committing.

> ⚠️ **This is a visible change to the game people already play**, not a no-op behind a flag. The current look is
> modern dark-blue/red; after this it reads as Gen 1. That is the intent, but it should surprise nobody.

**Ratified with the user (2026-08-03) — the token recipe, "Kanto Sage."** Sketched and iterated as an interactive
mockup (three palettes × two frame styles × two cursor styles × four outer-tone variants, reskinning five real
HUD chunks live — nameplates/HP/XP bars, the dialogue box, the 2×2 command grid, move-select) rather than
described in prose, per decision 8's "not precise enough in a short explainer" call. This closed the *ratify*
step of decision 8's sketch → ratify → build → gates sequence.

**Built (2026-08-04).** The `[data-generation="gen1"]` token override block ships in two parts, both verified
live end-to-end (Puppeteer through Title → StarterSelection → route choice → battle → CHECK POKEMON →
Settings, resting *and* invert-block hover/focus states):
- The five HUD chunks the mockup itself covered — nameplates, HP/XP bars, the battle log (double-line
  dialogue-box chrome), the 2×2 command grid, and move-select.
- Extended the same day, per the user's direction to apply the scheme to every "basic view/frame" rather than
  just the battle HUD: Title Screen, StarterSelection, Settings (screen + in-battle modal + panel), CHECK
  POKEMON, and the route-choice modal's outer frame (background/border/chrome — "biome select").

Both parts use explicit per-selector overrides, not a blanket redefinition of the shared `--clr-*` tokens —
those tokens are also read by the surfaces still deliberately dark (below), and a global flip either leaks
into those or collides two now-identical tokens into invisible hover text (caught and fixed twice: `.btn`/
`.btn-new-game`'s hover state, and two inherited-near-white-text bugs in CHECK POKEMON). The fix pattern used
throughout: give each new root surface its own `color: ink`, so anything not individually patched still
inherits correctly instead of silently keeping the old default.

**Deliberately still dark** — each its own future catalog turn (§7.5), not a gap in this pass: BAG's item
list, the run map's own node/territory/edge content and the full-screen pinned map, reward/shop/acquire/
recovery/battle-end modals' literal thematic accent colours (tuned against the old dark background — a
partial flip to a light box would have broken their contrast), the party strip, drop-toasts, the node ladder.

**Follow-up same day: the battlefield backdrop.** `.battle-field`'s deep-navy/forest-green gradient (the
Phaser canvas's backdrop, behind the creature sprites) was tuned against the old dark HUD and, once the
surrounding chrome went ink-on-fog, read as the one leftover saturated surface fighting the sprites for
attention instead of the sprites being the picture's colour. Muted to pale sky/ground tones from the same
Fog family — sprite art (already one of the four budgeted colours) is now the thing that visibly pops.

The picker-live-preview line below is moot until a second generation is registered — there's nothing to
preview *between* yet.

*What carries over unchanged* (confirmed already true of today's tokens, not a new decision): `--font` is already
monospace (`'JetBrains Mono', 'Courier New', Courier, monospace`), `--radius` is already `0px`, `--border-w` is
already `2px`. The gap was never "not blocky enough" — it was palette, border *boldness*, window chrome, and the
selection convention.

**A strict colour budget, not a repaint.** Every frame, label, and button surface is ink-on-neutral; colour is
spent on exactly four things and nothing else:
- **HP bar — red** `#D6362B`.
- **XP bar — blue** `#2F63AF`.
- **Type badges** — unchanged (`TypeBadge.tsx`'s existing 18-colour map; decision 5's preservation rule, confirmed
  still correct).
- **Sprite art** — the Phaser canvas creature itself; out of `index.css`'s scope but named here as one of the
  budget's four sources, not an oversight.
- *(Battle-log emphasis — e.g. "It's super effective!" — already has its own treatment in the running app; out of
  scope for this token set, not a gap.)*

**The neutrals:**
| Token | Value | Role |
|:--|:--|:--|
| Ink | `#15130F` | borders, text — near-black, warm-tinted, not pure `#000` |
| Box fill | `#FFFFFF` | interior of every boxed surface (nameplate, dialogue, menu button, move row) |
| Outer field | `#C9D0C5` ("Fog") | the field behind the boxes — a muted sage-grey, picked over three alternatives (Stone `#D2CEC3`, Slate `#C7CBD2`, Ash `#B6B9BD`) specifically to keep the "Kanto Sage" identity without reading as tan/khaki (the first pass's `#DED8BC` — user's call, 2026-08-03: *"not a big fan of the tan"*) |
| Dim text | `#57503F` | secondary labels — bar numbers, PP counts |

**Window chrome — double-line frame.** The RBY dialogue-box idiom: an outer `2px solid` ink border, a `3px`
inset ring of the fill colour, then a further `5px` inset ink ring — reads as a proper double frame rather than
a single hairline. Applies to the outer screen shell and the dialogue/log box; command-grid buttons and move
rows stay a plain bold (`3px`) single border, matching the real RBY split between the main window/textbox frame
and simpler menu-cell borders.

**Selection convention — invert block.** The selected command / move row swaps fill↔ink (solid ink background,
fill-colour text) rather than a blinking ▶ arrow. Chosen over the arrow in the same sitting.

**Not yet decided:** exact values for Cartridge Grey / Poké Center Red under this same colour-budget discipline
(they still carry their earlier, more colourful draft values) — moot unless a later surface wants an alternate
palette option; Kanto Sage is the one ratified for shipping. The mockup itself (an interactive HTML page, not
committed to the repo) is not the source of truth going forward — this table is.

**Clarified during the build (2026-08-04): the four-colour budget governs chrome, not gameplay signal.**
Two cases came up that the ratified list didn't spell out:
- **HP bar green/yellow/mid/high thresholds** — confirmed with the user to keep the existing green→yellow→red
  shift as remaining HP drops; only the *low* endpoint is pinned to the recipe's `#D6362B`. Reading "HP bar —
  red" as "the bar is flatly red" would have deleted a real gameplay cue, not just simplified decoration.
- **Move-select's STAB corner tag, ×N effectiveness pill, power-tier pill, low-PP red** — left exactly as they
  were; same reasoning as the HP thresholds, extended without a separate round-trip. The one visible casualty:
  the STAB button's translucent gold left-border/background wash (tuned for the old dark panel) doesn't survive
  on white — the button structure falls back to a slightly thicker ink edge, and the gold *corner tag* (unaffected)
  still carries the actual signal.
The general rule going forward: the budget applies to **decorative chrome** (frames, fills, button states); a
colour that exists to tell the player something *in the moment* (health remaining, type matchup, low resources)
is gameplay signal and sits outside it, the same as accessibility-motivated colour would.

### 7.4 Sub-stage 4c — the Town Map: a rigid grid region map

Decision 9. The current region map (`RegionMap` in `BattleScreen.tsx`) is painterly: free-floating waypoints at
authored percent coords (`BiomeDefinition.MapX/MapY`, 0–100), screen-blended type-colour territories, curved
gradient edges. It becomes a **classic Gen 1 Town Map**: biome squares on a rigid tile grid, straight
orthogonal routes, a bouncing you-are-here cursor (ratified below, superseding the earlier "blinking"
placeholder language).

**Geometry is server data, authored per region; skin is client presentation, per generation.** That split is
what makes "grid for all, loosenable per gen" coherent: every region authors grid geometry; each generation's
map presentation decides how faithfully to render it.

**Server (authored geometry — `Biome.cs` registry work, zero importer/DB change):**
- `BiomeDefinition.MapX/MapY` (free 0–100 percent) are **replaced** by integer grid-cell coords on an authored
  region canvas (canvas dimensions are part of the region's authored data; the RBY Town Map's ~20×18 is the
  reference scale, exact size a 4c authoring choice). The 18 Kanto biomes are re-authored onto the grid.
- **Routes are authored cell paths, not derived lines** — each neighbour edge carries an orthogonal path of
  grid cells from biome to biome, the way RBY routes are *things on the map*. Considered and rejected:
  deriving L-shaped connectors client-side (no authoring cost, but overlaps/crossings are unavoidable in a
  dense 18-node graph rendered per-run as an arbitrary 10-biome subset, and the result reads as a diagram, not
  a map). Authored paths make the map an authored artifact exactly like the biome roster itself.
- **Validity is code-checked, not eyeballed** (`BiomeTests`): every neighbour pair has exactly one route; each
  path is contiguous and orthogonal; endpoints meet their biomes' cells; no cell is used by two biomes, or by
  two routes, or by a route and a biome (except endpoints); everything fits the canvas.
- **Wire:** `RegionMapRevealed`'s per-biome view carries the grid coords in place of `MapX/MapY`, plus the
  route paths (filtered to the playable subset like `Neighbours` today, both endpoints in-subset). Canvas
  dimensions ride the same event. Standard treatment: emitter projection + the generic field guard + a
  value-level `WebEventContractTests` pin.
- **`TestAltProfile` leg:** its two-biome fake region gets grid coords and one authored route, so the Stage 3
  run-map probe keeps proving the geometry rides the profile's roster — and the validity tests run against the
  alt region too, proving they check *any* authored region, not Kanto by name.

**Client (the shared grid toolkit + the per-gen seam):**
- A grid renderer replaces the painterly `RegionMap`: biome = square tile (ink-outline, colour spent only on
  the tile's type glyph(s) — see the sketch-iteration note below), routes = drawn cell paths, current biome =
  a bouncing-chevron cursor (the RBY town-map idiom), travelled routes highlighted
  (`regionMap.ts`'s `travelledEdgeKeys` logic survives unchanged — edges are still biome-id pairs), offered
  biomes flash as the selectable route picks.
- **Interaction contracts are untouched:** `RouteChoiceMap` stays a blocking modal with the same focus
  management and aria semantics; `RunMapPanel`'s pinned/peek behaviour, the Escape rule, and the node ladder
  all stay. This is a presentation swap under stable bones (§7.1).
- **The per-gen seam:** the registry's (§7.2) map-presentation slot selects renderer + skin. Gen 1 = the
  strict grid. The painterly renderer is retired, not kept as a live alternative — but the toolkit (grid
  geometry types, travelled-route logic, subset filtering) is shaped as importable pieces so a future
  generation's map can use all, some, or none of it (decision 9). How far that generation loosens the grid is
  decided in *its* map design, not pre-engineered here.
- The in-biome **node ladder** (the Slay-the-Spire encounter path) is deliberately *not* part of 4c — it is a
  separate surface in the catalog (§7.5), so its look is settled in its own joint iteration.

**Sketch → ratify record (2026-08-05), decision 8's process applied to 4c.** The colour-budget question raised
alongside 4b — "biome = square tile, full type-colour fill" predates decision 10's colour budget (§7.3 /
§1 decision 10 — colour reserved for HP/XP bars, type badges, and sprite art, everything else ink-on-neutral)
— is now resolved, along with several more choices, via an interactive HTML sketch (same "live mockup, not
prose" approach as 4b's Kanto Sage ratification) drawn over the real `Biome.cs` registry:

- **Tile colour: ink-outline, not a full-tile wash.** Settles that question in favour of the budget-
  preserving option — the tile is a plain ink-bordered white square; colour is spent only on the type
  glyph(s) inside it, matching decision 10's discipline instead of introducing a second, looser rule.
- **Tile glyph = the biome's full type list, not just its primary type.** Each of a biome's types gets its
  own small icon in its own colour, laid out in a row inside the tile. Two biomes that merely share a
  primary type (e.g. Granite Cliffs' Rock/Flying/Fighting vs. Crystal Cavern's Rock/Ground) no longer read
  identically. Still drawn from the same 15-icon `mapGlyphs` vocabulary — not bespoke art per biome name.
- **Route line: dotted for untravelled, a thick solid ink line for travelled.** No longer an open style
  question — both states are one rule, not a per-generation toggle.
- **Cursor: a bouncing chevron**, not a blinking ring — supersedes the "blinking cursor" placeholder
  language used when 4c was first scoped (now corrected above).
- **Hover reveals name + type(s) via a status caption**, not a floating per-tile label — a caption band
  below the map updates to whatever biome is hovered/focused, reverting to the current biome on
  mouseleave/blur. Floating labels were rejected because RBY-density tile spacing makes them collide.
- **Per-run map is a small "island" (5–10 biomes), not the whole registry at once.** The run already caps
  its playable subgraph today (`EncounterFactory.RunBiomeMapSize`, currently a flat `10`); this narrows that
  to a random 5–10 and reframes it as a self-contained island the player sails away from, rather than one
  sprawling continent view of all 18 biomes. Small, well-scoped follow-up to the existing constant + subgraph
  picker, not a new subsystem.
- **Islands are connected by a Boss-gated, non-forced transition — a run-flow rule, not a style question.**
  Beating an island's Boss unlocks sailing to the next island, but doesn't eject the player — the rest of
  the current island stays explorable afterward at their own pace, so today's "revisit the same subgraph
  forever on a dead end" fallback (§ the `BiomeChoiceEvent` doc comment) gets a real endpoint instead of
  looping in place. **Open implementation detail, not blocking the presentation work above:** whether "the
  island's Boss" is the existing per-biome `BossBattle` ladder node (already the toughest node, already used
  for the post-win catch chance) elevated to also gate the island, or a distinguished single capstone
  encounter — a `RunDirector`/`EncounterFactory` question for whoever picks up that slice of the build.
- **The island is drawn as an actual landmass, water, and small terrain features, per decision 11.** A first
  pass drew the coastline as a smoothed bezier blob and water as a continuous curved wave-line texture; both
  were wrong per decision 11 (added by this same iteration). The **coastline** is a stepped, grid-aligned
  orthogonal polygon (corner and mid-edge notches for character, every segment horizontal or vertical, never
  a curve) — that half of the rule is a flat "no curves" ban, no exception. The **water** went through two
  passes: first a straight-edged pixel-checker dither (technically rectilinear, but read as a busy modern
  texture, not a Gen 1 material), then — after decision 11's same-day refinement (curves are fine *as a fixed
  symbol*, not as shape/texture) — a **uniform flat tone broken up by a handful of small `~`-style wave
  glyphs**, sparse and scattered like the land features rather than tiled edge-to-edge, each one a discrete
  repeated symbol exactly like a `mapGlyphs.tsx` type icon. Plus a handful of small tree/rock motifs (plain
  triangles and straight-edged polygons) scattered in the gaps between nodes. Still flat ink-on-neutral —
  shape and a few small symbols do the "water vs. land" distinction, not a new colour.

**Sketch revised again (2026-08-06) — the type glyph is dropped, and the grid structure is now locked.**
Two more rounds on the same interactive sketch, past the record above:
- **The per-biome type glyph is gone.** On reflection it wasn't earning its complexity — the hover caption
  already carries a town's name and type(s), so the map surface itself no longer needs to encode type at
  all. A town is now a plain pixel house: hollow (outline only) if unvisited, solid ink if visited or
  current — a legibility mark, not decoration.
- **Land and water became real *tiled textures* instead of a flat tone.** Land got a sparse dot grain (the
  same technique as the shipped Kanto Sage paper-grain); water got a denser pixel-checker dither — distinct
  materials at a glance, still built from flat ink-on-neutral shapes (rectangles), no new colour.
- **The grid structure itself is now locked down (user's call, 2026-08-06): one biome per grid cell,
  authored orthogonal routes, identity-on-hover.** Future passes on this sub-stage don't revisit that
  structure — only its surface treatment.

**Next for this sub-stage (flagged 2026-08-06, not yet started): replace the sketch's procedural SVG/CSS
textures with real graphic assets.** Everything drawn so far — the dot-grain land, the checker-dither water,
the drawn pixel-house town marker, the tree/rock motifs — is code-drawn (patterns and paths), a stand-in for
layout and rendering-style decisions, not final art. The user wants **actual authored graphics** (tile/sprite
art) for the map before or as part of the real client build. Open, not yet decided: the art source (hand-
authored fresh vs. an existing asset pack), the format (raster tiles vs. vector), and whether it rides the
same import path as the creature sprites (`docs/SPRITE_PRESENTATION.md`) or is its own pipeline. Whoever picks
this up next should treat it as its own short sketch → ratify step (decision 8's process), same as everything
else in this sub-stage.

**Still open for 4c's build:** exact final grid dimensions and the collision-checked route authoring pass
(`BiomeTests`) for whichever biomes end up in a real island; map-scale (comfortable vs. compact) has no
ratified answer yet either. Both are presentation-only, unblocked by anything above.

### 7.5 Sub-stage 4d+ — the surface catalog (joint iteration)

Decision 8's process: each surface below gets a **short joint mini-plan** (sketch → ratify with the user →
build → gates), one at a time, each separately greenlit. The catalog is ordered as a default; re-order freely
at greenlight time.

1. **Battle command menu** — *settled by decision 6*: Gen 1 = the 2×2 grid, today's four verbs, all existing
   gating (`canAct`, `canSwitch`, the always-available read-only `CHECK`). The layout is presentation data
   (registry descriptor, e.g. `grid2x2 | list`); the verb set is fixed by the engine — §2.3 in code: a later
   generation may re-arrange the menu, never re-populate it.
2. **Move select** (the FIGHT submenu — move list, PP/type readout).
3. **Battle HUD** (nameplates, HP bars, status badges, the log).
4. **CHECK POKEMON** (`src/pages/CreatureOverview.tsx`).
5. **BAG** (the in-battle item menu).
6. **Party surfaces** (`PartyStrip`, `SwitchInModal`, `LeadChoiceModal`).
7. **Run prompts** (the remaining `components/modals/` — Acquisition, RewardChoice, Shop, MoveReplacement,
   Evolution, …).
8. **Title + StarterSelection** — including the **generation picker** itself (the run-start control that
   drives everything above; live-preview per §7.3).
9. **Node ladder / encounter path** (the in-biome view the Town Map hands off to).

Each iteration inherits the standing rules: bones invariant, blocking modals stay blocking, functionality
deltas are escalated (§7.1), and anything touching the wire takes the field-guard treatment.

### 7.6 Deferred / open

- **Phaser scene theming** (`BattleCanvas`, `BattleScene`) — the canvas is themed independently of the CSS
  tokens. Sprite *sets* (Gen 1 RBY sprites vs modern artwork) are a per-gen **asset** question with an import
  cost; flagged, not scoped here.
- **Cry audio** already routes through `Audio.getMasterVolume()` in `BattleScene.ts`; whether cries are per-gen
  assets is the same deferred asset question.
- **The Kanto grid authoring draft** (canvas size, the 18 placements, the route paths) — produced and reviewed
  as part of 4c, not pre-authored here.
- ~~**The echo carrier** (which event/field the server echo rides)~~ — decided in 4a: the dedicated
  session-layer `RunPresentationRevealed` event (see §7.2's as-built note).

---

## 8. Stage 5 — the falsification harness

Not a final stage so much as a **standing requirement**: each stage above lands with its leg of `TestAltProfile`
(§3). Ship-blocking for the feature as a whole, because it is the only thing that turns "upward compatible" from
an aspiration into a tested claim.

---

## 9. DoR coverage

| # | Item | Status |
|:--|:--|:--|
| 1 | **Captured + acceptance condition** | `TODO.md` → *Generation Profile*. **Acceptance:** a Gen 1 run is byte-for-byte identical to today; `TestAltProfile` demonstrably changes rules, content roster, region and chrome through the *same* code paths; **zero** `if (generation == …)` in the engine. |
| 2 | **Design pass for anything significant** | ✅ Stage 4 `/plan` v2 complete (decisions 5–9, 2026-07-31): the framework (4a–4c) is Ready; the per-surface designs (4d+) are **deliberately provisional-pending-their-joint-mini-plan** — DoR #2's provisional mechanism, by design (decision 8), not an unchecked item. Stages 1–3, 5 are plumbing/backend. |
| 3 | **Gen-variable surface named** | ✅ §2.1 (variable) and §2.2 (invariant), incl. the three surfaces with **no seam today**: type roster, content scope, starters. `RunRules` and node kinds confirmed **not** gen-variable. |
| 4 | **Gen 1 source of truth** | `GENERATION_SEAMS.md` (seam catalogue + §2 domain table); `ENCOUNTER_DESIGN.md §2.3` (Kanto roster + the all-15-types invariant); `DESIGN_GUIDES.md` (Generation Architecture Principle). |
| 5 | **Data vs runtime boundary** | Stages 1, 3, 4 = runtime/composition only, **zero importer or DB change**. Stage 2 = runtime *sockets* only; the schema work (`GenerationIntroduced`, per-gen rows) is **importer** work, deferred to *Multi-Generation*. |
| 6 | **The quirk to test** | Not a single mechanic — **two profiles through one code path** (§3). Each stage's leg of `TestAltProfile` is the assertion. |
| 7 | **Dependencies** | None blocking. Independent of `save.db`, Catch, the Item cluster. **Precedes** *Multi-Generation*, which gains a socket from this work. |

---

## 10. Open / deferred

- **Sequencing against the backlog is undecided** — not yet slotted against *Item Acquisition · Bag Persistence ·
  Catch* or *Game Loop & Progression*.
- **Per-gen sprite/cry asset sets** — flagged in §7.6, not scoped.
- **The `Multi-Generation` schema work** stays deferred; this design gives it a socket, not an implementation.
- **A second real generation** is explicitly *not* in scope. When one is built, the first thing it should do is
  delete `TestAltProfile`'s reason for existing — replace the probe with a real profile.
