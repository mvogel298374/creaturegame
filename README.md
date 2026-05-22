# creaturegame

A Gen 1 Pokémon battle simulator written in C# / .NET 9, designed from the ground up for correctness, extensibility, and creative expansion.

The core goal is a faithful Gen 1 battle engine — accurate damage formula, type chart quirks, stat formulas, PP tracking, turn ordering — with an architecture that makes swapping generations, adding roguelike mechanics, or wiring up a UI a matter of implementing an interface rather than rewriting the engine.

---

## Projects

| Project | Purpose |
|---|---|
| `creaturegame` | Core battle simulator (console app) |
| `PokeApiConnector` | One-shot importer: fetches Gen 1 data from PokéAPI and writes to SQLite |
| `tests/creaturegame.Tests` | xUnit unit and integration tests |

---

## Quick start

**Prerequisites:** .NET SDK 9.0.200 (user-local install at `C:\Users\USER\.dotnet\dotnet.exe` — see `global.json`).

```powershell
# 1. Populate the databases (required once on a fresh clone)
& "C:\Users\USER\.dotnet\dotnet.exe" run --project PokeApiConnector

# 2. Run the battle simulator
& "C:\Users\USER\.dotnet\dotnet.exe" run --project creaturegame

# 3. Run all tests
& "C:\Users\USER\.dotnet\dotnet.exe" test tests/creaturegame.Tests
```

`PokeApiConnector` fetches all Gen 1 Pokémon and moves (IDs 1–165) from [pokeapi.co](https://pokeapi.co) and writes them to `pokemon.db` and `moves.db` at the solution root. These files are excluded from git — regenerate them any time by re-running the importer.

---

## Architecture

### Data flow

```
PokéAPI → PokeApiConnector → pokemon.db / moves.db → GameDbContext → PokemonService / AttackService → Creature → Battle
```

### Battle engine

`Battle` drives a turn loop. Each side submits an `IBattleAction`; actions are sorted by `Priority` → Speed → random tie-break, then resolved in order via `ExecuteAsync()`.

`DamageCalculator` implements the Gen 1 formula:

```
Damage = ( (2 × Level / 5 + 2) × BasePower × Attack/Defense ) / 50 + 2
       × STAB (1.5× if applicable)
       × type effectiveness (from ITypeChart)
       × random roll (217–255 / 255)
```

### Key extension points

| Interface | Responsibility | Swap to... |
|---|---|---|
| `ITypeChart` | Type effectiveness matrix | `Gen2TypeChart`, `Gen9TypeChart`, custom |
| `IBattleAction` | A single turn action | Status moves, items, flee |
| `IBattleInput` *(planned)* | Move selection source | Console menu, AI, network, UI |

Changing generation rules means providing a new `ITypeChart` implementation — the battle engine and damage calculator need no changes.

### Database / schema

Both SQLite databases are managed by **EF Core migrations** (`creaturegame/DB/Migrations/`). `EnsureDatabaseCreated()` on each context calls `Database.Migrate()`, so a fresh `PokeApiConnector` run creates, migrates, and populates everything in one step. Add new migrations with:

```powershell
$env:DOTNET_ROOT = "C:\Users\USER\.dotnet"
$env:PATH = "C:\Users\USER\.dotnet;C:\Users\USER\.dotnet\tools;$env:PATH"
& "C:\Users\USER\.dotnet\dotnet.exe" ef migrations add <Name> --project creaturegame --context <ContextName> --output-dir DB/Migrations/<Moves|Pokemon>
```

---

## Gen 1 accuracy

The following Gen 1 quirks are intentionally preserved:

- **Ghost → Psychic = 0×** (should be 2×; famous RBY bug)
- **Poison → Bug = 2×** (changed to 0.5× in Gen 2)
- No Steel, Dark, or Fairy types
- HP DV derived from lowest bits of Attack, Defense, Speed, and Special DVs
- Stat Exp (EVs) use the `sqrt(StatExp) / 4` bonus formula
- Stat formula differs between HP and all other stats

---

## Roadmap

**Battle mechanics (in priority order):**
1. Move Priority — read `move.Priority` field instead of hardcoding 0
2. Status conditions — Burn, Paralysis, Poison, Sleep, Freeze with correct Gen 1 effects and end-of-turn damage
3. Move selection — `IBattleInput` abstraction replacing the hardcoded `MoveSet[0]`
4. XP & catch system — Gen 1 XP formula on faint; catch rate mechanic
5. Learnset system — per-species moveset populated from PokeAPI data

**Future layers:**
- Generation switching — plug in a new `ITypeChart` + stat formula set
- Roguelike / autobattler mode
- Pokémon Infinite Fusion-inspired creature composition
- Abilities layer (`Traits` system, currently scaffolded)

---

## Project conventions

- C# 13 / .NET 9; implicit usings and nullable reference types enabled
- `ITypeChart` is the primary generation-switching seam — preserve it
- All DB reads use `AsNoTracking()`; all DB operations are async
- Test method names describe what they test — no `Test` prefix/suffix
- See `CLAUDE.md` for full agent and coding guidelines
