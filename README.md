# creaturegame

A Gen 1 Pokémon battle simulator written in C# / .NET 9, designed from the ground up for correctness, extensibility, and creative expansion.

The core goal is a faithful Gen 1 battle engine — accurate damage formula, type chart quirks, stat formulas, PP tracking, turn ordering — with an architecture that makes swapping generations, adding roguelike mechanics, or wiring up a UI a matter of implementing an interface rather than rewriting the engine. On top of the engine sits a playable web client (React + Phaser 3 + SignalR) and an emerging roguelite run layer.

> **Disclaimer.** This is an unofficial, non-commercial fan project, provided entirely for free — no money is charged and the author gains no monetary benefit from it (no sales, donations, ads, or other revenue). It is not affiliated with, endorsed by, or sponsored by Nintendo, Game Freak, or The Pokémon Company. Pokémon and all associated names, data, sprites, and audio are their trademarks and copyright. **No game assets are stored in this repository** — species data and sprites are fetched at build time from the community-run [PokéAPI](https://pokeapi.co) into local, git-ignored files. The MIT license (see [`LICENSE`](LICENSE)) covers only the original source code here.

---

## Projects

| Project | Purpose |
|---|---|
| `creaturegame` | Core battle engine **class library** — damage, type chart, status, stat stages, crits, move effects |
| `creaturegame.Web` | ASP.NET Core host — REST API, SignalR hub, and a Vite + React + Phaser 3 frontend under `ClientApp/`. Run via `.\dev.ps1` |
| `PokeApiConnector` | One-shot importer: fetches Gen 1 data from PokéAPI and writes to SQLite |
| `tests/creaturegame.Tests` | xUnit unit + integration tests — 670+ test methods, including a per-move Gen 1 fidelity-contract suite |

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

## Gen 1 fidelity

Faithfulness to Gen 1 is the point, so the engine deliberately reproduces the era's quirks and bugs rather than "fixing" them — for example the **Ghost → Psychic = 0×** type bug, the **1/256 miss** on 100%-accurate moves, the special DV-based HP formula and Stat Exp gain, and Gen 1's permanent Freeze. These live behind the generation seams (`ITypeChart` / `IBattleRules` / `IStatCalculator`), so swapping a later generation in doesn't mean editing them out.

The full mechanic-by-mechanic breakdown (type-chart differences, accuracy scale, crit formula, stat formulas, status timing) is in [`docs/GEN_DIFFERENCES.md`](docs/GEN_DIFFERENCES.md).

---

## Status

The Gen 1 battle engine is **feature-complete**: all 165 moves, status conditions, stat stages, crits, PP/Struggle, XP & level-up, the learnset system, EV/Stat-Exp gain, evolution, and an in-battle item system are implemented and covered by tests.

On top of the engine sits a playable **roguelite run layer**, also live:
- **Biome-graph encounters** — each run draws a seeded connected subset of Kanto's biomes; the player picks a biome, routes through a randomised 4–6 themed events (wild / elite / boss / shop / treasure / mystery) capped by a Poké Center, then chooses the next biome.
- **Depth-scaled difficulty** — `IEnemyArchetype` strength tiers and biome-position depth bands scale foes as a run deepens.
- **Run economy** — earned gold with a gold HUD, and a rarity-rolled **reward choice**: a win (or a Treasure/Mystery node) offers a pick-1-of-3 of rarity-coloured items or a ₽ bag, with Boss nodes skewed toward premium replenishing items.
- **Level-aware XP curve** — a roguelite XP ramp (kept separate from the Gen 1 `IBattleRules` seam) plus the Gen 1 trainer ×1.5 bonus for Elite/Boss nodes.

Still being built out: **acquisition channels** (boss catch + themed draft), the **Shop** node, bag persistence, and a party / save layer.

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
