# creaturegame

A Gen 1 Pokémon battle simulator written in C# / .NET 9, designed from the ground up for correctness, extensibility, and creative expansion.

The core goal is a faithful Gen 1 battle engine — accurate damage formula, type chart quirks, stat formulas, PP tracking, turn ordering — with an architecture that makes swapping generations, adding roguelike mechanics, or wiring up a UI a matter of implementing an interface rather than rewriting the engine.

---

## Projects

| Project | Purpose |
|---|---|
| `creaturegame` | Core battle engine **class library** — damage, type chart, status, stat stages, crits |
| `creaturegame.Web` | ASP.NET Core host — REST API, SignalR hub, Vite + React frontend. Run via `.\dev.ps1` |
| `PokeApiConnector` | One-shot importer: fetches Gen 1 data from PokéAPI and writes to SQLite |
| `tests/creaturegame.Tests` | xUnit unit and integration tests (78 tests) |

---

## Quick start

**Prerequisites:** .NET SDK 9.0.200 (user-local install at `C:\Users\USER\.dotnet\dotnet.exe` — see `global.json`), Node.js for the frontend.

```powershell
# 1. Populate the databases (required once on a fresh clone)
& "C:\Users\USER\.dotnet\dotnet.exe" run --project PokeApiConnector

# 2. Start the full dev environment (backend :5100 + frontend :5173 + browser)
.\dev.ps1

# 3. Run all tests
& "C:\Users\USER\.dotnet\dotnet.exe" test tests/creaturegame.Tests
```

`PokeApiConnector` fetches all Gen 1 Pokémon and moves (IDs 1–165) from [pokeapi.co](https://pokeapi.co) and writes them to `pokemon.db` and `moves.db` at the solution root, and downloads battle sprites to `creaturegame.Web/wwwroot/sprites/`. These files are excluded from git — regenerate them any time by re-running the importer.

---

## Architecture

### Data flow

```
PokéAPI → PokeApiConnector → pokemon.db / moves.db → PokemonDbContext / MovesDbContext → PokemonService / AttackService → Creature → Battle
```

### Battle engine

`Battle` drives a turn loop. Each side submits an `IBattleAction`; actions are sorted by `Priority` → Speed → random tie-break, then resolved in order via `ExecuteAsync()`.

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

| Interface | Responsibility | Swap to... |
|---|---|---|
| `ITypeChart` | Type effectiveness matrix | `Gen2TypeChart`, `Gen9TypeChart`, custom |
| `IBattleRules` | All gen-variable mechanics: stat stages, crit formula, accuracy scale, sleep/freeze/status rules | `Gen2BattleRules`, etc. |
| `IBattleAction` | A single turn action | Status moves, items, flee |
| `IBattleInput` | Move selection source | Console menu, AI, network, UI |

Changing generation means a new `ITypeChart` + `IBattleRules` — the engine and calculator need no changes.

### Database / schema

Both SQLite databases are managed by **EF Core migrations** (`creaturegame/DB/Migrations/`). `EnsureDatabaseCreated()` on each context calls `Database.Migrate()`, so a fresh `PokeApiConnector` run creates, migrates, and populates everything in one step. Add new migrations with:

```powershell
$env:DOTNET_ROOT = "C:\Users\USER\.dotnet"
$env:PATH = "C:\Users\USER\.dotnet;C:\Users\USER\.dotnet\tools;$env:PATH"
& "C:\Users\USER\.dotnet\dotnet.exe" ef migrations add <Name> --project creaturegame --context <ContextName> --output-dir DB/Migrations/<Moves|Pokemon>
```

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
- Stat Exp (EVs) use the `sqrt(StatExp) / 4` bonus formula
- Stat formula differs between HP and all other stats
- Sleep lasts 1–7 turns (Gen 2+: 2–5); Freeze is permanent until hit by an appropriate Fire move

---

## Roadmap

For the concrete, prioritised list of what's actively being worked on, see [`TODO.md`](TODO.md).

**Battle engine — completed:**
- Gen 1 damage formula, STAB, type chart quirks, random variance
- PP tracking, Struggle, move priority
- Status conditions — Burn, Paralysis, Poison, Sleep, Freeze, Confusion (Gen 1 rules)
- Stat stages (±6), accuracy/evasion stages, Gen 1 1/256 miss bug
- Critical hits — Base Speed formula, high-crit moves, Gen 1 bypass of stages and Burn

**In progress / next:**
- Move effects layer (stat-stage moves, Haze, etc.)
- Move selection UI (`ConsoleInput`, then AI variants)
- Web UI battle screen — live SignalR event feed, move menu, text log
- XP & catch system; learnset system

**Loose future goals:**
- Generation switching — new `ITypeChart` + `IBattleRules` pair
- Roguelike / autobattler mode
- Pokémon Infinite Fusion-inspired creature composition
- Abilities layer

---

## Project conventions

- C# 13 / .NET 9; implicit usings and nullable reference types enabled
- `ITypeChart` is the primary generation-switching seam — preserve it
- All DB reads use `AsNoTracking()`; all DB operations are async
- Test method names describe what they test — no `Test` prefix/suffix
- See `CLAUDE.md` for full agent and coding guidelines
