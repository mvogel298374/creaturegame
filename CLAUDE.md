# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The system `dotnet` at `C:\Program Files\dotnet\dotnet.exe` is a runtime-only install with no SDK. Use the user-local SDK at `C:\Users\USER\.dotnet\dotnet.exe` (NET 9.0.200) for all build and test commands.

```powershell
& "C:\Users\USER\.dotnet\dotnet.exe" build                          # Build all projects
& "C:\Users\USER\.dotnet\dotnet.exe" run --project creaturegame     # Run the battle simulator
& "C:\Users\USER\.dotnet\dotnet.exe" run --project PokeApiConnector # Import data from PokeAPI
& "C:\Users\USER\.dotnet\dotnet.exe" test tests/creaturegame.Tests  # Run all tests
```

To run a single test by name:
```powershell
& "C:\Users\USER\.dotnet\dotnet.exe" test tests/creaturegame.Tests --filter "FullyQualifiedName~<MethodName>"
```

EF Core migration commands require `DOTNET_ROOT` set so `dotnet-ef` finds the user-local SDK instead of the system runtime-only install:
```powershell
$env:DOTNET_ROOT = "C:\Users\USER\.dotnet"; $env:PATH = "C:\Users\USER\.dotnet;C:\Users\USER\.dotnet\tools;$env:PATH"
& "C:\Users\USER\.dotnet\dotnet.exe" ef migrations add <MigrationName> --project creaturegame --context <ContextName> --output-dir DB/Migrations/<Moves|Pokemon>
& "C:\Users\USER\.dotnet\dotnet.exe" ef migrations remove --project creaturegame --context <ContextName>
```

## Architecture

Three-project .NET 9 solution:

- **creaturegame** — Core battle simulator (console app). Namespaced `creaturegame.*`.
- **PokeApiConnector** — One-shot data importer that fetches from PokeAPI and writes to SQLite. Namespaced `PokeApiConnector.*`.
- **tests/creaturegame.Tests** — xUnit unit tests. Tests live under `Unit/` and `Integration/` subdirectories; namespaces must match folder structure.

### Data flow

PokeApiConnector fetches Gen 1 Pokémon and moves (IDs 1–165) from `pokeapi.co`, persists them to `pokemon.db` and `moves.db` (SQLite). The main app loads from those databases via **Entity Framework Core** (`GameDbContext`) using `PokemonService` / `AttackService`, constructs `Creature` instances with Gen 1 stat formulas, then runs them through `Battle`.

### Battle system

`Battle` drives a turn loop: each side submits an `IBattleAction` (currently only `AttackAction`), actions are sorted by `Priority` → Speed → random tie-break, then executed in order via `ExecuteAsync()`. `DamageCalculator` computes Gen 1 damage (base power × Attack/Defense ratio × STAB 1.5× × type effectiveness × random variance 217–255/255). Type effectiveness comes from an injected `ITypeChart`; `Gen1TypeChart` is the only implementation and preserves Gen 1 quirks (Ghost → Psychic = 0×, Poison → Bug = 2×, no Steel/Dark/Fairy types).

### Key patterns

- **`ITypeChart`** — strategy interface; swap implementations to change generation rules.
- **`IBattleAction`** — encapsulates a single turn action; `Priority` + `ExecuteAsync()`.
- **`IBattleInput`** (planned) — abstracts move selection (console, AI, UI).
- All DB reads use `AsNoTracking()` before upserts. All DB operations are async.
- Schema uses EF Core migrations (in `creaturegame/DB/Migrations/`). `EnsureDatabaseCreated()` calls `Database.Migrate()` — run `PokeApiConnector` on a fresh setup to create and populate the databases. Add new migrations with `dotnet ef migrations add` (see migration command above).

## Agent Profiles (AI_CONTEXT.md)

Prefix messages to set context:

| Command | Profile | Knowledge base |
|:--------|:--------|:---------------|
| `/plan` | Lead Game Designer | `DESIGN_GUIDES.md` — design before implementation |
| `/dev`  | Senior .NET Engineer | `DEV_STANDARDS.md` — clean, testable, EF-optimized code |
| `/sync` | Data sync review | Compare `moves.db` schema vs current models |
| `/test` | Verification | Write and run tests for the current module |

When no command is given, use judgment to blend profiles. If a request is ambiguous, ask whether it's `/plan` or `/dev`.

## Design Principles

The target is a **true Gen 1 Pokémon battle clone** with future layers inspired by roguelikes, autobattlers, and the Pokémon Infinite Fusion mod. Preserve Gen 1 accuracy (mechanics, quirks, formulas) before extending. See `DESIGN_GUIDES.md` for type-balancing and move-import mapping rules.

## Current TODO State

Completed: Gen 1 type chart, damage calculation, turn ordering, PP tracking (via `PokemonAttack`), stat formulas, growth rates.

Active priorities (in order):
1. **PP Tracking** — switch `Creature.MoveSet` to `List<PokemonAttack>`; Struggle fallback when all PP = 0.
2. **Move Priority Fix** — read `move.Priority` in `AttackAction` instead of hardcoding 0.
3. **Status Conditions** — apply Burn/Paralysis/Poison/Sleep/Freeze; end-of-turn damage in `Battle`.
4. **Move Selection** — replace `MoveSet[0]` hardcode with `IBattleInput` abstraction.
5. **XP & Catch System** — Gen 1 XP formula on faint; `CatchRate`-based catch mechanic.
6. **Learnset System** — `PokemonLearnset` DB table; import from PokeAPI; populate moveset on init.

Tech debt to clear: decide whether `Traits` becomes the Abilities layer or is removed; add `.editorconfig` / `.gitattributes`; resolve `Creature` class/namespace collision; remove `Creature.Attack()` footgun.

## Communication Style

Before running any command or making changes, state in plain language what you are about to do and why — which files will be affected, what the command does, and what outcome you expect. One or two sentences is enough; silence is not.

PowerShell commands are auto-approved, but commands that are system-wide or far-reaching (registry edits, installing software, modifying global environment variables, deleting files outside the repo, etc.) should still be explicitly called out and confirmed with the user before running.

## Coding Conventions

- C# 13 / .NET 9; implicit usings and nullable reference types enabled.
- Use primary constructors for DTOs and simple data structures (keep models EF-compatible).
- Enable and handle `Nullable` for all API/DB response types (`int?`, `string?`).
- Wrap API and DB calls in `try-catch` with console logging.
- Test method names state what they test — no `Test`/`test` prefix or suffix.