# The PokeAPI Data Import — Developer Guide

> **Audience:** new team members / junior engineers who need to (re)populate the game's
> data or extend what we import.
> **Scope:** what `PokeApiConnector` does, the import-vs-runtime boundary it enforces,
> how each entity is mapped from PokeAPI's JSON into our schema, and the Gen 1 domain
> decisions baked into that mapping.
>
> **See also:** `DESIGN_GUIDES.md` (the move-mapping table + import-vs-runtime rule),
> `GENERATION_SEAMS.md` (how the engine consumes this data), `DEV_STANDARDS.md`.

---

## 1. TL;DR

`PokeApiConnector` is a **one-shot command-line importer**. It fetches Gen 1 data from
[pokeapi.co](https://pokeapi.co), writes it into our two SQLite databases, and downloads
sprite/cry assets into the web app's `wwwroot`. Run it once on a fresh checkout; after
that the game never touches PokeAPI again.

```powershell
& "C:\Users\USER\.dotnet\dotnet.exe" run --project PokeApiConnector
```

| It populates | With |
|:-------------|:-----|
| `moves.db` (`MovesDbContext`) | Gen 1 moves → `Attack` rows |
| `pokemon.db` (`PokemonDbContext`) | Gen 1 species → `PokemonSpecies` rows + `PokemonGameAvailability` rows |
| `creaturegame.Web/wwwroot/sprites/{front,back}/{id}.png` | Battle sprites |
| `creaturegame.Web/wwwroot/audio/cries/{id}.ogg` | Legacy 8-bit cries |

**Re-running is safe** — every step is idempotent (DB upserts by ID; asset downloads
skip files that already exist).

---

## 2. The cardinal rule: import-time vs. runtime

This is the single most important thing to understand, and it's an architectural
decision, not an implementation detail:

> **At runtime the game reads *only* our own SQLite DBs and static files. PokeAPI is
> never called by the game server or the frontend.**

`PokeApiConnector` is the *only* code allowed to talk to PokeAPI, and it runs offline,
ahead of time. Why this matters:

- **Offline & deterministic:** the game works with no internet and isn't at the mercy of
  PokeAPI uptime, rate limits, or schema changes.
- **Stability:** PokeAPI returns *current* data; mechanics drift across generations. We
  freeze a curated Gen 1 snapshot at import time so gameplay never silently changes
  under us.
- **Performance:** local SQLite reads vs. hundreds of HTTP round-trips per session.

**Consequence for you:** to change or extend game data, edit the importer / the DB /
the model (with a migration) and re-run — **never add a runtime PokeAPI call.** See
`DESIGN_GUIDES.md` → "Data Architecture: Import vs. Runtime".

---

## 3. The pipeline (`Program.cs`)

Six sequential steps:

1. **Ensure databases** — `EnsureDatabaseCreated()` on both contexts runs EF migrations
   (`Database.Migrate()`), creating/updating `moves.db` and `pokemon.db`.
2. **Import moves** — `MoveImport.FetchMovesByGeneration(1)`.
3. **Import species** — `PokemonImport.FetchPokemonByGeneration(1)`.
4. **Seed game availability** — `GameAvailabilitySeeder.SeedGen1Async()`.
5. **Download sprites** — `SpriteDownloader.DownloadAllAsync()`.
6. **Download cries** — `CryDownloader.DownloadAllAsync()`.

Steps 2 and 3 follow the same **index-then-detail** shape: hit the *generation* endpoint
(`/generation/1/`) to get the list of move/species URLs, then fetch each entity's detail
endpoint and map it.

---

## 4. The domain: what we extract and the Gen 1 decisions in the mapping

### 4.1 Moves (`MoveImport` → `Attack`)
For each move, `GET /move/{id}` → `PokeApiMove` DTO → `MapToAttack`. Core stat fields
(`BaseDamage`/power, `Accuracy`, `PowerPointsMax`/pp, `EffectChance`, and `DamageType`) are
**resolved to their Gen 1 values** (see immediately below), *not* copied from PokeAPI's current
data; `Priority` and `Description` (English short-effect) are taken as-is.

#### Gen 1 value resolution (`past_values`) — the default fidelity mechanism
PokeAPI returns each move's **current** (latest-generation) stats, and many Gen 1 moves differ:
special moves were stronger (Flamethrower/Ice Beam/Surf 95→90, Hydro Pump/Blizzard 120→110),
Blizzard was 90% accurate (→70%), and several moves were a different **type** (Bite/Gust/Karate
Chop/Sand Attack were Normal). PokeAPI records this in each move's **`past_values`** array: every
entry holds the value a field had *before* it changed, tagged with the `version_group` where the
change took effect — so the recorded value applied in all *earlier* generations, Gen 1 included.

`MapToAttack` resolves the Gen 1 value of `power`, `accuracy`, `pp`, `effect_chance`, and `type`
by taking the **earliest** `past_values` entry that specifies each field (PokeAPI lists them
oldest-first); a field with no entry kept its current value in Gen 1 too. This is the **single,
data-driven source** of Gen 1 move fidelity — there are deliberately **no per-move hardcoded
stat/type corrections**. It is the move-side analogue of `past_types` for species typing (§4.2),
and it *superseded* an earlier hardcoded type-correction switch.

**The one documented exception.** `past_values` only covers `power/accuracy/pp/effect_chance/
type`. Where a Gen 1 fact falls outside those — a secondary's *target stat*, an ailment, priority,
or a whole-effect rewrite — add a **small, explicitly-commented override** after the mapping,
sourced from an authority (Bulbapedia / a Gen 1 disassembly), never from PokeAPI's current data.
Today the list is exactly one: **Acid** (Gen 1 lowers the target's *Defense* at 33%; PokeAPI's
structured data says Sp. Def at 10% and its `past_values` is empty). Keep this list short — see
the layered strategy and its limits in §5.5.

The remaining Gen 1 logic lives in the categorisation:

- **`AttackType`** — for damaging moves, **derived from the move's (Gen-1-resolved) type** (Gen 1 rule),
  *not* PokeAPI's `damage_class`. Gen 1 physical types are Normal/Fighting/Flying/Poison/
  Ground/Rock/Bug/Ghost; everything else is Special. Status moves → `Undefined`. *(PokeAPI's
  `damage_class` is the Gen 4+ per-move split — it would wrongly make Fire Punch physical
  and Hyper Beam special; see the resolved note in §6.)*
- **Status ailment** → `StatusEffect` (paralysis/sleep/burn/poison/freeze/bad-poison),
  with `EffectChance` taken from `meta.ailment_chance` for damaging moves.
- **`IsHighCrit`** ← `meta.crit_rate > 0` (Slash, Razor Leaf, etc.).
- **`DamageCategory`** — mostly from `meta.category` (`damage-heal` → Drain, `ohko` →
  OHKO), but PokeAPI doesn't model several Gen 1 mechanics, so those are **special-cased
  by move ID**:
  - 120/153 Self-Destruct/Explosion → `SelfDestruct`
  - 69/101 Seismic Toss/Night Shade → `LevelBased`
  - 162 Super Fang → `SuperFang`
  - 49 Sonic Boom → `Fixed` (20), 82 Dragon Rage → `Fixed` (40)
- **`DrainPercent`** ← `meta.drain` for drain moves (default 50%).
- **`NeverMisses`** — Swift (ID 129) bypasses the accuracy roll.
- **Stat-stage effects** ← first `stat_changes` entry → `StatEffectStat/Delta/Target/
  Chance` (Swords Dance, Growl, …). Pure status moves always land (chance 100); secondary
  effects on damaging moves use the **Gen-1-resolved** `EffectChance`. (At runtime the engine
  reads that chance through `IBattleRules.GetSecondaryEffectChance`, not the column directly —
  see `GENERATION_SEAMS.md`.)
- **Special `MoveEffect`** by name: Haze, Leech Seed, Hyper Beam (Recharge), binding
  moves (Wrap/Bind/Clamp/Fire Spin), two-turn moves (Fly/Dig/Solar Beam/Razor Wind/
  Sky Attack), Metronome, and flinch (from `meta.flinch_chance`).

### 4.2 Species (`PokemonImport` → `PokemonSpecies`)
Each species needs **two** PokeAPI endpoints, because the data is split:

- `GET /pokemon/{id}` → stats, types, base experience.
- `GET /pokemon-species/{id}` → growth rate, capture rate, Pokédex flavor text.

`MapToSpecies` combines them. Gen 1 specifics:

- **Hard cap at 151** — the generation endpoint can include relations, so we skip
  `Id > 151`.
- **Stats** are pulled by name from the stats array. Crucially, **`BaseSpecial` ←
  `special-attack`**: Gen 1 had a *single* Special stat, so we deliberately take
  special-attack as the combined value (the engine's `GetOffensiveStat`/`GetDefensiveStat`
  both read it — see `GENERATION_SEAMS.md`).
- **Types via `past_types`** — PokeAPI returns *current* types; `past_types` records what
  changed and when. `Gen1TypeSlots` picks the earliest pre-Gen-6 historical entry if one
  exists, otherwise the current types. This is how a Pokémon whose typing changed in a
  later generation is imported with its **Gen 1 typing**, not today's.
- **Growth rate / catch rate / Pokédex entry** mapped from the species endpoint (flavor
  text has its form-feed/newline control chars stripped).

### 4.3 Game availability (`GameAvailabilitySeeder`)
This is **hand-curated domain knowledge**, not an API import, because PokeAPI doesn't
cleanly express Gen 1 per-version obtainability. It encodes:

- **Version exclusivity** — which species are *absent* from Red / Blue / Yellow (and thus
  trade-only), as hand-maintained sets with comments (e.g. Sandshrew is Blue-only).
- **Availability type** per `(species, version)` — `Wild` by default, overridden to
  `Static` (legendaries/Snorlax), `Event` (Mew), `Gift` (fossils, Hitmons, Lapras),
  `GameCorner` (Porygon, Yellow's Scyther/Pinsir/Eevee), or `Trade` (NPC in-game trades).

It clears and re-seeds cleanly each run (`ExecuteDeleteAsync` then bulk insert of all 151
× 3 versions minus exclusions).

### 4.4 Sprites & cries (`SpriteDownloader`, `CryDownloader`)
Pull PNGs (front/back) and OGG cries for IDs 1–151 into `wwwroot`, located by walking up
to the solution root. Both are **idempotent**: a file that already exists is skipped, so
re-runs only fetch what's missing. The frontend's Phaser canvas serves these as static
files (front for the enemy, back for the player); cries fall back to a Web Audio synth if
an OGG is missing.

---

## 5. Patterns & practices — and *why*

### 5.1 Import/runtime separation (the headline pattern)
Already covered in §2, but as a *pattern*: this is a **batch ETL boundary**. The importer
is the Extract+Transform+Load stage; the game is a pure consumer of the loaded store.
Keeping them in separate projects (`PokeApiConnector` vs. `creaturegame`/`.Web`) makes the
boundary physical — the engine can't accidentally depend on a live API.

### 5.2 DTOs + mapping = an anti-corruption layer
The `PokeApi*` classes mirror PokeAPI's JSON shape exactly; `MapToAttack` / `MapToSpecies`
translate that external shape into *our* domain model (`Attack`, `PokemonSpecies`). All
of PokeAPI's quirks, naming, and nullability are absorbed in this one layer — the battle
engine never sees a PokeAPI type. When the external schema changes, only the importer
changes. *When integrating any third-party data source, put a mapping layer between it and
your domain model.*

### 5.3 Idempotent upsert
Each entity is read with `AsNoTracking()` and then `Add`-ed or `Update`-d by primary key,
so re-running the importer is safe and converges to the same state. Trade-off we accepted:
it `SaveChangesAsync()` **per record** (hundreds of small writes). That's slow, but for a
one-shot tool run occasionally it's not worth batching — simplicity and obvious progress
logging win.

### 5.4 Encode what the API can't express — explicitly and with comments
Two places do this: move categorisation special-cased by **ID** (§4.1) and game
availability curated by **hand** (§4.3). The practice: when the source data lacks the
structure you need, encode the domain knowledge directly, **comment every magic value**
with what it represents (`// 120/153 Self-Destruct/Explosion`), and keep it all in the
importer so the runtime model stays clean. The comments are the spec.

### 5.5 Historical correctness at import time (`past_types` / `past_values`)
Importing *Gen 1* values rather than today's is a deliberate correctness step: the importer's
job is "produce an accurate Gen 1 snapshot," not "copy the API." Two parallel mechanisms do this
from PokeAPI's own historical fields — **`past_types`** for species typing (§4.2) and
**`past_values`** for move power/accuracy/pp/effect_chance/type (§4.1) — and both follow the same
rule: *take the earliest historical entry; fall back to current only when no history is recorded.*
Strict-clone fidelity is decided here, once, rather than patched in the engine.

**Reach for the fidelity layers in this order — it's the intended strategy, not a suggestion:**

1. **The historical-field resolver** (`past_values` / `past_types`) — automatic, covers the
   systematic numeric/type drift, needs no per-move code. This is the default and should handle
   the overwhelming majority of moves.
2. **A targeted, commented override in the importer** — *only* for a Gen 1 fact the historical
   fields **can't express** (a secondary's target stat, an ailment, priority, a mechanic). Verify
   it against an authority (Bulbapedia / the pret/pokered disassembly) and keep the list short.
   Acid is the sole current entry.
3. **The runtime seams** (`IBattleRules` / `ITypeChart` / `IStatCalculator`) — for anything that
   is a *formula or mechanic* difference rather than stored move/species **data**. Mechanic
   differences never belong in the importer; data differences never belong in the engine.

**Limits to respect (be honest — these *will* bite if ignored):**

- `past_values` covers only `power/accuracy/pp/effect_chance/type`. **Stat-target changes** (Acid),
  ailment/secondary-chance changes PokeAPI didn't record, priority, target, crit rate, and
  **whole-effect rewrites** are invisible to it — and PokeAPI's *structured* data can lag its own
  flavor text. When a move's Gen 1 secondary looks off, cross-check Bulbapedia; do not assume the
  resolver caught it.
- "Earliest entry = Gen 1" relies on PokeAPI listing `past_values` **oldest-first**. For a field
  that changed more than once this ordering is load-bearing; if it is ever violated, sort entries
  by their `version_group`'s generation instead of trusting array order.
- Contract tests assert what we *coded*, so they won't flag a wrong-but-consistent data value.
  The real safety net is the **per-move review each coverage batch** — decode the row and
  sanity-check power/accuracy/secondary against Gen 1 before trusting it.

### 5.6 Resilience: fail a record, not the run
Every fetch is wrapped in try/catch with `EnsureSuccessStatusCode`, and a failure on one
move/species logs and continues. A flaky network drops a few rows you can recover on the
next run, rather than aborting the whole import.

---

## 6. Known limitations / gotchas (be honest about these)

- **Physical/Special split fidelity — RESOLVED.** Originally `AttackType` was copied from
  PokeAPI's per-move `damage_class`, which miscategorised **18 of 110** Gen 1 damaging
  moves (e.g. Hyper Beam, the elemental punches, Gust) and so computed their damage off the
  wrong stat. Now derived from the move's type (§4.1); the existing `moves.db` rows were
  corrected in place by the same deterministic rule.
- **Gen 1 move-data fidelity is resolver-based, with blind spots.** `past_values` (§4.1) auto-
  corrects power/accuracy/pp/effect_chance/type, but it does **not** see stat-target changes,
  ailment/secondary-chance changes PokeAPI omitted, priority, or whole-effect rewrites — those need
  a verified override (layer 2) and won't be caught by tests. See §5.5 for the strategy + limits.
- **`new HttpClient()` per request.** Convenient but not best practice (socket pressure at
  scale). Tolerable here given the one-shot, low-volume nature; would be a shared client /
  `IHttpClientFactory` in long-running code.
- **Per-record `SaveChanges`.** Many round-trips; fine for a one-shot tool, not a pattern
  to copy into the web host.
- **Hardcoded Gen 1.** Move/species ranges and mappings assume Gen 1; generalising to
  other generations is future work (see the Multi-Generation section of `TODO.md`).
- **Direct `new MovesDbContext()` / `new PokemonDbContext()`.** Acceptable for a CLI tool
  (parameterless ctor + hardcoded path in `OnConfiguring`). The *web host* deliberately
  does **not** do this — it uses `IDbContextFactory` DI (see `Program.cs` of
  `creaturegame.Web`). Don't copy the CLI pattern into the server.

---

## 7. When to run it

- **Fresh checkout / new machine:** required — the `.db` files and assets aren't checked
  in; the importer creates and fills them.
- **After a schema migration** that adds a column the importer populates: re-run so
  existing rows get the new data (upsert updates them in place).
- **Extending the dataset** (a new move field, learnsets, more generations): edit the
  importer + model + add a migration, then re-run. Never reach for a runtime API call.
