# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Key Files (read these at the start of every session)

| File | Purpose |
|:-----|:--------|
| `TODO.md` | **Authoritative** task list — priorities, done items, tech debt. Always update it when finishing a task. |
| `AI_CONTEXT.md` | Agent profiles and slash-command definitions (`/plan`, `/dev`, `/sync`, `/test`). |
| `DESIGN_GUIDES.md` | Gen 1 mechanics, type-balancing rules, move-import mapping. Read before `/plan` work. |
| `DEV_STANDARDS.md` | .NET/EF coding conventions and architecture rules. Read before `/dev` work. |
| `STATE_MODEL.md` | Deep-dive reference: the `Creature` permanent/transient state split (`BattleState`) — patterns + Gen 1 domain logic. Read when touching battle state. |
| `GENERATION_SEAMS.md` | Deep-dive reference: the generation seams (`ITypeChart`, `IBattleRules`, `IStatCalculator`) — patterns + per-gen domain logic. Read before adding a gen-variable rule or a new generation. |
| `DATA_IMPORT.md` | Deep-dive reference: the `PokeApiConnector` import pipeline — import-vs-runtime boundary, PokeAPI→model mapping, Gen 1 data decisions. Read before changing imported data. |

## Commands

The system `dotnet` at `C:\Program Files\dotnet\dotnet.exe` is a runtime-only install with no SDK. Use the user-local SDK at `C:\Users\USER\.dotnet\dotnet.exe` (NET 9.0.200) for all build and test commands.

```powershell
& "C:\Users\USER\.dotnet\dotnet.exe" build                          # Build all projects
& "C:\Users\USER\.dotnet\dotnet.exe" run --project PokeApiConnector # Import data from PokeAPI
& "C:\Users\USER\.dotnet\dotnet.exe" test tests/creaturegame.Tests  # Run all tests
```

To start the full dev environment (backend + Vite frontend + browser):
```powershell
.\dev.ps1
```
**Run this directly in a PowerShell terminal at the repo root — do NOT wrap it in `Start-Process` or call it via `-Command ".\dev.ps1"`.** It spawns two child `pwsh` windows (backend on `:5100`, frontend on `:5173`) and opens the browser once Vite is ready. If the browser does not open automatically after ~60 s, navigate to `http://localhost:5173` manually.

To run **all** test suites at once (one summary, CI-friendly exit code) — .NET unit (xUnit), frontend unit (Vitest), and frontend E2E (Playwright):
```powershell
.\test.ps1                  # all suites (E2E skipped if the dev stack isn't running)
.\test.ps1 -Dotnet          # only .NET unit
.\test.ps1 -Web             # only Vitest
.\test.ps1 -E2E -StartStack # only Playwright, starting/stopping the backend itself
```
Playwright E2E needs the app running (`.\dev.ps1`); without `-StartStack` it's skipped with a notice when the backend isn't on `:5100`. Frontend-only test commands also work directly: `npm test` / `npm run test:e2e` in `creaturegame.Web/ClientApp`.

To run a single .NET test by name:
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

Four-project .NET 9 solution:

- **creaturegame** — Core battle engine **class library** (no entry point). Namespaced `creaturegame.*`. Referenced by `creaturegame.Web` and `creaturegame.Tests`.
- **creaturegame.Web** — ASP.NET Core host: REST API, SignalR hub, static file server. Hosts a Vite + React + TypeScript frontend under `ClientApp/`. Run via `.\dev.ps1`.
- **PokeApiConnector** — One-shot data importer that fetches from PokeAPI and writes to SQLite. Namespaced `PokeApiConnector.*`.
- **tests/creaturegame.Tests** — xUnit unit tests. Tests live under `Unit/` and `Integration/` subdirectories; namespaces must match folder structure.

### Data flow

PokeApiConnector fetches Gen 1 Pokémon and moves (IDs 1–165) from `pokeapi.co`, persists them to `pokemon.db` and `moves.db` (SQLite). The main app loads from those databases via **Entity Framework Core** (`PokemonDbContext` / `MovesDbContext`) using `PokemonService` / `AttackService`, constructs `Creature` instances with Gen 1 stat formulas, then runs them through `Battle`.

### Battle system

`Battle` drives a turn loop: each side submits an `IBattleAction` (currently only `AttackAction`), actions are sorted by `Priority` → effective Speed (stage-adjusted, Paralysis quartered) → random tie-break, then executed in order via `ExecuteAsync()`. `DamageCalculator` computes Gen 1 damage (base power × Attack/Defense ratio × STAB 1.5× × type effectiveness × stat stage multipliers × critical hit 2× × random variance 217–255/255). All generation-specific rules — stat stage tables, crit formula, accuracy scale, freeze thaw, status damage rates — are delegated to `IBattleRules`; `Gen1BattleRules` is the only implementation. Type effectiveness comes from `ITypeChart`; `Gen1TypeChart` preserves Gen 1 quirks (Ghost → Psychic = 0×, Poison → Bug = 2×, no Steel/Dark/Fairy types, Ice → Fire = 1×). Accuracy uses the Gen 1 0–255 internal scale; a roll of 255 always misses (1/256 bug).

### Key patterns

- **`ITypeChart`** — strategy interface; swap implementations to change the type effectiveness matrix.
- **`IBattleRules`** — strategy interface for all generation-variable mechanics: stat stage multipliers, accuracy/evasion scale, crit formula, freeze thaw, sleep duration, status damage rates. `Gen1BattleRules.Instance` is the singleton default everywhere.
- **`IBattleAction`** — encapsulates a single turn action; `Priority` + `ExecuteAsync()`.
- **`IBattleInput`** — abstracts move selection (console, AI, UI); `AutoSelectInput` is the current default.
- **`StatStages`** — struct on `Creature`; holds per-battle Attack/Defense/Special/Speed/Accuracy/Evasion stages clamped to [−6, +6]; cleared between battles.
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

## TODO State

See `TODO.md` for the full prioritised task list, completed items, and tech debt. Always update `TODO.md` when finishing a task.

## Permissions

- **File edits and creation are always allowed** — make changes to existing files or create new ones without asking for confirmation first.
- **Git commits require explicit approval** — stage changes and propose a commit message, but do not run `git commit` until the user confirms.
- **Dev stack is freely managed** — start, stop, restart, or kill the dev servers (backend `:5100`, Vite `:5173`) whenever a task needs it (build lock, fresh-code reload, test run), without asking. Don't append their running status to responses as a routine heads-up; only mention it when directly relevant (e.g. a test failed because the backend was down).

## Communication Style

Before running any command or making changes, state in plain language what you are about to do and why — which files will be affected, what the command does, and what outcome you expect. One or two sentences is enough; silence is not.

PowerShell commands are auto-approved, but commands that are system-wide or far-reaching (registry edits, installing software, modifying global environment variables, deleting files outside the repo, etc.) should still be explicitly called out and confirmed with the user before running.

## Coding Conventions

- C# 13 / .NET 9; implicit usings and nullable reference types enabled.
- Use primary constructors for DTOs and simple data structures (keep models EF-compatible).
- Enable and handle `Nullable` for all API/DB response types (`int?`, `string?`).
- Wrap API and DB calls in `try-catch` with console logging.
- Test method names state what they test — no `Test`/`test` prefix or suffix.