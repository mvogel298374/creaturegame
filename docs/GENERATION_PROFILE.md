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

---

## 2. What a `GenerationProfile` is

### 2.1 The bundle

| Slice | Today | Under the profile |
|:--|:--|:--|
| Battle math | 4 seams, `Gen1*.Instance` defaults ✅ | selected by the profile; the seams themselves are unchanged |
| **Type roster** — which types *exist* | ❌ none. `DamageType` holds all 18 (incl. Steel/Dark/Fairy); nothing states Gen 1 has **15** | on the profile |
| **Content scope** — species/moves/items | ❌ none. Gen 1 only because the DBs hold only Gen 1 | a filter seam (documented no-op today) |
| **Region + biome roster** | ⚠️ partial — `Biomes.For(Region)` exists; `Region` is an enum; `Biomes.Kanto` is the only roster | region selected by the profile |
| **Starter roster** | ❌ hardcoded client-side | on the profile |
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
| Region roster | Kanto | a 2-biome fake region | `Biomes.For` is the only door |
| Theme + menu layout | Gen 1 grammar, 2×2 grid | a distinct token set + list layout | the theme is data, not CSS defaults |

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

### 4.1 Where the seams actually enter today (surveyed 2026-07-29)

Only **three** places in product code choose a generation. Everything else inherits by default:

| Seam | Where it enters today | Stage 1 change |
|:--|:--|:--|
| `ITypeChart` | `GameSessionManager.cs:166` — `Gen1TypeChart.Instance` passed positionally into the `RunDirector` | read from the profile |
| `IEvolutionRules` | `EncounterFactory.cs:421` — `Gen1EvolutionRules.Instance` hardcoded in `ResolvePlayerEvolutionAsync` | read from the profile |
| `IStatCalculator` | `EncounterFactory.cs:493` — `new Gen1StatCalculator(rng)`, **seeded per creature** so a fixed seed reproduces DVs. (`Creature.cs:404`'s property default is only the unseeded fallback.) | profile exposes a **factory**, not a singleton — Stage 1b |
| `IBattleRules` | **nowhere in the web layer** — never passed; `Battle` falls back to `Gen1BattleRules.Instance` internally | thread explicitly from the profile |

**A fifth site, found during Stage 1a:** `EncounterFactory.ActiveGeneration = 1` — a hardcoded `private const int`
driving **six** learnset/evolution DB queries, plus a duplicate `PlayerOverviewDto.ActiveGeneration = 1`. Not a
seam, but a **second source of truth for the generation**, and the most concrete "Gen 1 by assumption" in the
repo. Folded into Stage 1b (see §4.5).

### 4.5 Stage 1 is split — 1a (shipped) and 1b

**1a** did the axis and the `GameSessionManager` composition point: the profile type, registry, run-parameter
threading, and explicit reads of `TypeChart` / `BattleRules` / `EvolutionRules` / the AI.

**1b** does `EncounterFactory` — the `IStatCalculator` thread *and* the `ActiveGeneration` constant. Kept
together because they are one coherent chunk in one file (every `BuildCreature` caller sits directly beside an
`ActiveGeneration` query), and split from 1a because it is ~39 call sites across 5 test files, which would have
buried 1a's architecture in mechanical churn.

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

**(a) The type roster — real work, do it now.** `DamageType` holds all 18 types and stays gen-blind (correct —
the enum is a vocabulary, not a claim). What is missing is a statement of **which types exist in this
generation**. Gen 1 = the 15. This matters concretely: `ENCOUNTER_DESIGN.md §2.3`'s Kanto roster is sized so
that **all 15 Gen 1 types are homed**, an invariant a 17-type generation would re-derive. Put the roster on the
profile and have the biome/type-badge/UI paths read it.

**(b) Species / move / item filtering — documented no-op stubs.** Per `GENERATION_SEAMS.md §5.0`:

> *"If 'yes' but you're not building Gen 2 yet: still add the seam member, implement the Gen 1 value, and — when
> the data layout is what differs — make the Gen 1 implementation a documented stub that shows the generic
> shape."*

So: add the accessor, have Gen 1 return everything, and **document the generic shape**. The actual
`GenerationIntroduced` columns and filtered queries (`GetSpeciesForGenerationAsync`, etc.) are **importer/schema
work** and stay in `TODO.md` → *Multi-Generation*, explicitly sequenced after this.

> **Why stubs rather than nothing:** the alternative is that a future gen has to find every unfiltered
> `ToListAsync()` by archaeology. `GENERATION_SEAMS.md` calls this "cheap now and removes a future archaeology
> dig" — that judgement is already repo policy, not a new claim.

**Falsification leg:** `TestAltProfile` reports 17 types and is observed by whatever consumes the roster.

---

## 6. Stage 3 — region, biomes, starters

**Goal:** the region and starter set come from the profile.

**Smaller than it looks** — the socket already exists. `creaturegame/Creatures/Biome.cs` has a `Region` enum, a
`BiomeDefinition` record, and **`Biomes.For(Region)`**. `ENCOUNTER_DESIGN.md §1` anticipated exactly this:

> *"a new region is largely a new biome set, not new loop code."*

**Work:** put `Region` on the profile and have the run setup ask the profile instead of assuming Kanto; move the
starter roster (currently hardcoded client-side in `StarterSelection.tsx`) onto the profile so it is
server-authoritative and per-gen.

**Backend-only.** Zero importer/DB change.

**Watch:** `Biomes.Playable` / `RandomConnectedMap` must keep working off whatever roster the profile supplies —
including a small one. The fake region in `TestAltProfile` is deliberately tiny (2 biomes) to pin that a thin
roster doesn't break map generation.

---

## 7. Stage 4 — presentation: theme + menu structure

> `/plan` **complete** (2026-07-29) — decisions 5 and 6 above. No longer provisional.

This is the largest new surface and the only stage with no existing precedent in the repo.

### 7.1 Visual theme — Gen 1's grammar, not its palette

The hook already exists: `src/index.css` declares a full design-token block under `:root` (`--clr-*`, `--font`,
`--fs-*`, `--sp-*`, `--border-w`, `--radius`). Every screen reads them. Note `--radius: 0px` is already
commented *"square = classic pixel-game feel"* — the intent is there; the palette is not.

**Approach:** keep `:root` as the shared *structural* token set; add a per-generation override block selected by
an attribute on the document root (the same mechanism the repo's own artifact guidance uses for theming). Gen 1
supplies: hard 2px borders, square corners, blocky menu boxes, the classic HUD arrangement, and a palette that
evokes RBY without collapsing to four greens.

**Explicitly preserved** (this is why decision 5 rejected authentic DMG):
- **Per-type badge colours** (`TypeBadge.css`) — they carry real information.
- **The contrast tuning** in `index.css` — the comments record deliberate legibility work
  (*"brightened for small-stat legibility"*, *"nudged up from #555 for contrast"*). Do not regress it.

> ⚠️ **This is a visible change to the game people already play**, not a no-op behind a flag. The current look is
> modern dark-blue/red; after this it reads as Gen 1. That is the intent, but it should surprise nobody.

### 7.2 Menu structure — the 2×2 command grid

`ActionMenu` in `src/pages/BattleScreen.tsx` renders a flat button list of `FIGHT` / `BAG` / `SWITCH` /
`CHECK POKEMON`. Gen 1's battle menu is a **2×2 command grid**. Restructure to the grid, keeping **exactly**
today's four verbs and all their existing gating (`canAct`, `canSwitch`, the always-available read-only
`CHECK`).

**The layout is profile data, the verbs are not** — the profile supplies a layout descriptor
(`grid2x2` | `list`); the action set is fixed by the engine. That is §2.3's boundary rule in code: a later
generation may re-arrange the menu, never re-populate it.

> **Scope note (user, 2026-07-29): Stage 4 covers the WHOLE menu surface, not just the battle command menu.**
> When this stage is picked up, every menu gets the Gen 1 redesign — the move/attack select, **CHECK POKEMON**
> (`src/pages/CreatureOverview.tsx`), the BAG, the party/switch screens, and the run prompts in
> `src/components/modals/` (13 files behind the shared `<Modal>`). The 2×2 command grid is the *entry point* to
> that redesign, not the whole of it.
>
> Two things this does **not** change, per §2.3: which actions exist, and which prompts the run raises. Every
> modal stays `'blocking'` by construction (each parks a server-side await — see `TODO.md` → *Tech Debt*, the
> `<Modal>` refactor), so a redesign may restyle and re-lay-out them but must not make one dismissable.
>
> This widens Stage 4 considerably. Re-estimate it when it is greenlit rather than treating the §7.2 sketch as
> the full extent.

### 7.3 Where the client learns the generation

Two paths, and it needs **both**:
- **Immediate:** the client picked it, so it rides the existing route state — `StarterSelection.tsx` already does
  `nav('/battle', { state: { species, gameId, level } })`; add `generation`.
- **Authoritative:** the server must also echo it, or a **reconnect** re-mounts `BattleScreen` with no route
  state and the theme silently reverts. This repo has been bitten by exactly this class of bug before — see the
  Settings-modal trap in `TODO.md` (a page nav tore down the live SignalR connection). Treat the server echo as
  required, not optional.

### 7.4 Theming the picker itself

The generation is chosen *on* `StarterSelection`, so that screen cannot already be themed by the choice.
**Proposal:** boot in the default profile's theme and let the picker **live-preview** on change — near-free with
CSS custom properties, and it makes the choice legible before committing.

### 7.5 Deferred / open

- **Phaser scene theming** (`BattleCanvas`, `BattleScene`) — the canvas is themed independently of the CSS
  tokens. Sprite *sets* (Gen 1 RBY sprites vs modern artwork) are a per-gen **asset** question with an import
  cost; flagged, not scoped here.
- **Cry audio** already routes through `Audio.getMasterVolume()` in `BattleScene.ts`; whether cries are per-gen
  assets is the same deferred asset question.

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
| 2 | **Design pass for anything significant** | ✅ Stage 4 `/plan` complete (decisions 5–6). Stages 1–3, 5 are plumbing/backend. |
| 3 | **Gen-variable surface named** | ✅ §2.1 (variable) and §2.2 (invariant), incl. the three surfaces with **no seam today**: type roster, content scope, starters. `RunRules` and node kinds confirmed **not** gen-variable. |
| 4 | **Gen 1 source of truth** | `GENERATION_SEAMS.md` (seam catalogue + §2 domain table); `ENCOUNTER_DESIGN.md §2.3` (Kanto roster + the all-15-types invariant); `DESIGN_GUIDES.md` (Generation Architecture Principle). |
| 5 | **Data vs runtime boundary** | Stages 1, 3, 4 = runtime/composition only, **zero importer or DB change**. Stage 2 = runtime *sockets* only; the schema work (`GenerationIntroduced`, per-gen rows) is **importer** work, deferred to *Multi-Generation*. |
| 6 | **The quirk to test** | Not a single mechanic — **two profiles through one code path** (§3). Each stage's leg of `TestAltProfile` is the assertion. |
| 7 | **Dependencies** | None blocking. Independent of `save.db`, Catch, the Item cluster. **Precedes** *Multi-Generation*, which gains a socket from this work. |

---

## 10. Open / deferred

- **Sequencing against the backlog is undecided** — not yet slotted against *Item Acquisition · Bag Persistence ·
  Catch* or *Game Loop & Progression*.
- **Per-gen sprite/cry asset sets** — flagged in §7.5, not scoped.
- **The `Multi-Generation` schema work** stays deferred; this design gives it a socket, not an implementation.
- **A second real generation** is explicitly *not* in scope. When one is built, the first thing it should do is
  delete `TestAltProfile`'s reason for existing — replace the probe with a real profile.
