# creaturegame

A Gen 1 Pokémon battle simulator written in C# / .NET 9, designed from the ground up for correctness, extensibility, and creative expansion.

The core goal is a faithful Gen 1 battle engine — accurate damage formula, type chart quirks, stat formulas, PP tracking, turn ordering — with an architecture that makes swapping generations, adding roguelike mechanics, or wiring up a UI a matter of implementing an interface rather than rewriting the engine. On top of the engine sits a playable web client (React + Phaser 3 + SignalR) and an emerging roguelite run layer.

> **Disclaimer.** This is an unofficial, non-commercial fan project and is not affiliated with, endorsed by, or sponsored by Nintendo, Game Freak, or The Pokémon Company. Pokémon and all associated names, data, sprites, and audio are their trademarks and copyright. **No game assets are stored in this repository** — species data and sprites are fetched at build time from the community-run [PokéAPI](https://pokeapi.co) into local, git-ignored files. The MIT license (see [`LICENSE`](LICENSE)) covers only the original source code here.

---

## Projects

| Project | Purpose |
|---|---|
| `creaturegame` | Core battle engine **class library** — damage, type chart, status, stat stages, crits, move effects |
| `creaturegame.Web` | ASP.NET Core host — REST API, SignalR hub, and a Vite + React + Phaser 3 frontend under `ClientApp/`. Run via `.\dev.ps1` |
| `PokeApiConnector` | One-shot importer: fetches Gen 1 data from PokéAPI and writes to SQLite |
| `tests/creaturegame.Tests` | xUnit unit + integration tests — 600+ test methods, including a per-move Gen 1 fidelity-contract suite |

---

## Quick start

**Prerequisites:** the .NET SDK 9.0.200 (pinned in [`global.json`](global.json)), plus Node.js for the frontend.

```powershell
# 1. Populate the databases + download sprites (required once on a fresh clone)
dotnet run --project PokeApiConnector

# 2. Start the full dev environment (backend :5100 + frontend :5173 + browser)
.\dev.ps1

# 3. Run all test suites (.NET unit, Vitest, Playwright E2E)
.\test.ps1
```

`PokeApiConnector` fetches all Gen 1 Pokémon, moves (IDs 1–165), and the battle-usable items from [pokeapi.co](https://pokeapi.co) and writes them to `pokemon.db`, `moves.db`, and `items.db` at the solution root, and downloads battle sprites to `creaturegame.Web/wwwroot/sprites/`. **These files are git-ignored** — regenerate them any time by re-running the importer.

> If `dotnet` isn't on your PATH, invoke your SDK 9.0.200 install directly (this project was developed against a user-local install, e.g. `& "$HOME\.dotnet\dotnet.exe"`). The individual `dotnet build` / `test` commands work the same way.

---

## Architecture

For the design decisions and the *why* behind the system's shape, see [`ARCHITECTURE.md`](ARCHITECTURE.md); its §5 indexes every doc in the repo.

### Data flow

```
PokéAPI → PokeApiConnector → pokemon.db / moves.db / items.db → EF Core DbContexts → domain services → Creature → Battle
```

All runtime data comes from the local SQLite databases and static files — there are **no live calls to PokéAPI at runtime**. The importer runs once; after that the game works fully offline.

### Battle engine

`Battle` drives a turn loop. Each side submits an `IBattleAction`; actions are sorted by `Priority` → effective Speed → random tie-break, then resolved in order via `ExecuteAsync()`.

`DamageCalculator` implements the Gen 1 formula:

```
Damage = ( (2 × Level / 5 + 2) × Attack × BasePower / Defense ) / 50 + 2
       × STAB (1.5× if applicable)
       × type effectiveness (from ITypeChart)
       × stat stage multipliers (via IBattleRules)
       × critical hit (2× — Gen 1 crits bypass stages and Burn penalty)
       × random roll (217–255 / 255)
```

Accuracy uses an internal 0–255 scale; a roll of 255 always misses even on 100%-accurate moves (the Gen 1 "1/256 miss bug").

### Key extension points

All generation-specific behaviour lives behind interfaces, so a new generation is a new pair of implementations — the engine and calculator don't change.

| Interface | Responsibility | Swap to... |
|---|---|---|
| `ITypeChart` | Type effectiveness matrix | `Gen2TypeChart`, `Gen9TypeChart`, custom |
| `IBattleRules` | All gen-variable mechanics: stat stages, crit formula, accuracy scale, sleep/freeze/status rules | `Gen2BattleRules`, etc. |
| `IStatCalculator` | Stat formulas (DVs/Stat-Exp vs. IVs/EVs/natures) | `Gen3StatCalculator`, etc. |
| `IBattleAction` | A single turn action | Attacks, items, flee |
| `IBattleInput` | Turn-choice source | Console menu, AI, network/UI |

### Database / schema

Three SQLite databases are managed by **EF Core migrations** (`creaturegame/DB/Migrations/`). `EnsureDatabaseCreated()` on each context calls `Database.Migrate()`, so a fresh `PokeApiConnector` run creates, migrates, and populates everything in one step. See [`docs/DATA_IMPORT.md`](docs/DATA_IMPORT.md) for the import pipeline and the import-vs-runtime boundary.

---

## Gen 1 quirks preserved

The following Gen 1 behaviours are intentionally kept accurate:

**Type chart:**
- **Ghost → Psychic = 0×** (should be 2×; famous RBY bug)
- **Poison → Bug = 2×** (changed to 0.5× in Gen 2)
- **Ice → Fire = 1×** (changed to 0.5× in Gen 2)
- **Bug → Poison/Psychic = 2×** (both changed in Gen 2)
- No Steel, Dark, or Fairy types

**Accuracy:**
- Internal 0–255 scale (not the Gen 2+ 0–100 scale)
- A roll of 255 always misses — **1/256 miss bug** applies to all moves including 100%-accurate ones
- Accuracy and evasion stages use the 3/9…9/3 table (distinct from the 2/8…8/2 damage-stat table)

**Critical hits:**
- Crit chance = `floor(BaseSpeed / 2) / 256`; high-crit moves (Slash, Crabhammer, etc.) multiply numerator by 8
- Crits deal 2× and **bypass all stat stage modifiers and the Burn Attack penalty**

**Stats / formulas:**
- HP DV derived from lowest bits of Attack, Defense, Speed, and Special DVs
- Stat Exp (EVs) use the `sqrt(StatExp) / 4` bonus formula; a win adds the defeated foe's base stats to the victor's Stat Exp (capped 65535/stat), realized into stats on the next level-up
- Stat formula differs between HP and all other stats
- Sleep lasts 1–7 turns (Gen 2+: 2–5); Freeze is permanent until hit by an appropriate Fire move

The full mechanic-by-mechanic reference lives in [`docs/GEN_DIFFERENCES.md`](docs/GEN_DIFFERENCES.md).

---

## Status

The Gen 1 battle engine is **feature-complete**: all 165 moves, status conditions, stat stages, crits, PP/Struggle, XP & level-up, the learnset system, EV/Stat-Exp gain, evolution, and an in-battle item system are implemented and covered by tests. A roguelite run layer (biome-graph encounters, enemy strength tiers, a live map) sits on top and is actively being extended.

For the concrete, prioritised list of what's being worked on next, see [`docs/TODO.md`](docs/TODO.md).

**Loose future goals:**
- Generation switching — a new `ITypeChart` + `IBattleRules` + `IStatCalculator` set
- Deeper roguelite / acquisition layer (catch, party, persistence)
- Pokémon Infinite Fusion-inspired creature composition
- Abilities layer

---

## Project conventions

- C# 13 / .NET 9; implicit usings and nullable reference types enabled
- `ITypeChart` / `IBattleRules` / `IStatCalculator` are the generation-switching seams — preserve them
- All DB reads use `AsNoTracking()`; all DB operations are async
- Test method names describe what they test — no `Test` prefix/suffix
- See [`CLAUDE.md`](CLAUDE.md) and [`docs/DEV_STANDARDS.md`](docs/DEV_STANDARDS.md) for the full engineering guidelines
