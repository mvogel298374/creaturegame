# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Key Files — read on demand, by trigger

**This file (`CLAUDE.md`) is the only always-on primer.** The rest below are reference docs: read one
*when the task you're starting matches its trigger* — not preemptively at session start. (Reading all of
them up front burns ~25k tokens before the work is even scoped; almost none of it primes any single task.)

| File | Read it when… |
|:-----|:--------------|
| `TODO.md` | starting or finishing any task — it's the **authoritative** active task list. Always update it when a task completes. (Finished work is in `TODO_ARCHIVE.md`; read that only to recover the history of a done item.) |
| `AI_CONTEXT.md` | you need a slash-command/profile definition (`/plan`, `/dev`, `/sync`, `/test`, `/audit`) or the **Tooling & Automation** reference (the `/audit` skill, `seam-reviewer`, pre-commit hook, CSharpier, MCP servers). |
| `DESIGN_GUIDES.md` | doing `/plan` (design) work — Gen 1 mechanics, type-balancing, move-import mapping. |
| `DEV_STANDARDS.md` | doing `/dev` (implementation) work — .NET/EF coding conventions and architecture rules. |
| `STATE_MODEL.md` | touching battle state — the `Creature` permanent/transient split (`BattleState`). |
| `GAME_LOOP.md` | working on the **run/roguelite loop** — the game-loop ↔ event model (battle & heal as events), the logic-drives-sequence rule, and the target event abstraction. |
| `GENERATION_SEAMS.md` | adding a gen-variable rule or a new generation — the seams (`ITypeChart`, `IBattleRules`, `IStatCalculator`) + the §5.0 gen-agnostic checklist. |
| `DATA_IMPORT.md` | changing imported data — the `PokeApiConnector` pipeline, import-vs-runtime boundary, PokeAPI→model mapping. |

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
**Run this directly at the repo root.** It spawns two child `pwsh` windows (backend on `:5100`, frontend on `:5173`) and opens the browser once Vite is ready. If the browser does not open automatically after ~60 s, navigate to `http://localhost:5173` manually.

> **For the AI agent:** invoking `.\dev.ps1` through your PowerShell tool **is** the correct, supported way to start the stack — that already counts as "running it directly," and it works (the script's own `Start-Process` calls spawn the child windows independently of your non-interactive shell). The parent script blocks up to ~60 s waiting on Vite, so give the call a ≥90 s timeout (or use `run_in_background`). The only thing to avoid is *adding another layer* yourself — do **not** call it as `Start-Process pwsh -Command ".\dev.ps1"` or `pwsh -Command ".\dev.ps1"`, which detaches the windows and throws away the readiness wait. Plain `.\dev.ps1` is right. Do not refuse this task or push it back to the user; you are expected to start/stop the stack autonomously (see `AI_CONTEXT.md` and the dev-stack memory).

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

Formatting & the pre-commit gate (see `AI_CONTEXT.md` → **Tooling & Automation** for the why):
```powershell
& "C:\Users\USER\.dotnet\dotnet.exe" tool restore          # once per clone — installs the pinned CSharpier
& "C:\Users\USER\.dotnet\dotnet.exe" csharpier format .    # format C# (do NOT hand-align)
& "C:\Users\USER\.dotnet\dotnet.exe" csharpier check .     # what the hook/CI runs
git config core.hooksPath .githooks                         # once per clone — arms .githooks/pre-commit
```
The `.githooks/pre-commit` hook runs `csharpier check` (always) + the full test suite (when `.cs` is staged) and **blocks the commit on failure**. For battle/stat/move changes, run the `/audit` skill before proposing the commit — it adds the seam/fidelity review the hook can't do.

EF Core migration commands require `DOTNET_ROOT` set so `dotnet-ef` finds the user-local SDK instead of the system runtime-only install:
```powershell
$env:DOTNET_ROOT = "C:\Users\USER\.dotnet"; $env:PATH = "C:\Users\USER\.dotnet;C:\Users\USER\.dotnet\tools;$env:PATH"
& "C:\Users\USER\.dotnet\dotnet.exe" ef migrations add <MigrationName> --project creaturegame --context <ContextName> --output-dir DB/Migrations/<Moves|Pokemon|Items>
& "C:\Users\USER\.dotnet\dotnet.exe" ef migrations remove --project creaturegame --context <ContextName>
```

## Architecture

Four-project .NET 9 solution:

- **creaturegame** — Core battle engine **class library** (no entry point). Namespaced `creaturegame.*`. Referenced by `creaturegame.Web` and `creaturegame.Tests`.
- **creaturegame.Web** — ASP.NET Core host: REST API, SignalR hub, static file server. Hosts a Vite + React + TypeScript frontend under `ClientApp/`. Run via `.\dev.ps1`.
- **PokeApiConnector** — One-shot data importer that fetches from PokeAPI and writes to SQLite. Namespaced `PokeApiConnector.*`.
- **tests/creaturegame.Tests** — xUnit unit tests. Tests live under `Unit/` and `Integration/` subdirectories; namespaces must match folder structure.

### Data flow

PokeApiConnector fetches Gen 1 Pokémon, moves (IDs 1–165), and the battle-usable items from `pokeapi.co`, persisting them to `pokemon.db`, `moves.db`, and `items.db` (SQLite). The main app loads from those databases via **Entity Framework Core** (`PokemonDbContext` / `MovesDbContext` / `ItemsDbContext`) using `PokemonService` / `AttackService` / `ItemService`, constructs `Creature` instances with Gen 1 stat formulas, then runs them through `Battle`. (Items have no `/generation` list endpoint, so their Gen 1 set is a hand-curated roster — see `DATA_IMPORT.md` §4.5.)

### Battle system

`Battle` drives a turn loop: each side submits an `IBattleAction` (`AttackAction`, or `ItemAction` when the player uses a bag item), actions are sorted by `Priority` → effective Speed (stage-adjusted, Paralysis quartered) → random tie-break, then executed in order via `ExecuteAsync()`. (Items resolve first — `ItemAction.Priority` sits above any move; the per-action `CanAct`/dead-target guards apply only to `AttackAction`.) `DamageCalculator` computes Gen 1 damage (base power × Attack/Defense ratio × STAB 1.5× × type effectiveness × stat stage multipliers × critical hit 2× × random variance 217–255/255). All generation-specific rules — stat stage tables, crit formula, accuracy scale, freeze thaw, status damage rates — are delegated to `IBattleRules`; `Gen1BattleRules` is the only implementation. Type effectiveness comes from `ITypeChart`; `Gen1TypeChart` preserves Gen 1 quirks (Ghost → Psychic = 0×, Poison → Bug = 2×, no Steel/Dark/Fairy types, Ice → Fire = 1×). Accuracy uses the Gen 1 0–255 internal scale; a roll of 255 always misses (1/256 bug).

### Key patterns

- **`ITypeChart`** — strategy interface; swap implementations to change the type effectiveness matrix.
- **`IBattleRules`** — strategy interface for all generation-variable mechanics: stat stage multipliers, accuracy/evasion scale, crit formula, freeze thaw, sleep duration, status damage rates. `Gen1BattleRules.Instance` is the singleton default everywhere.
- **`IBattleAction`** — encapsulates a single turn action; `Priority` + `ExecuteAsync()`. `AttackAction` (a move/Struggle) and `ItemAction` (use a bag item) are the implementations.
- **`IBattleInput`** — abstracts the side's turn choice (console, AI, UI). `ChooseMoveAsync` picks a move; the additive `ChooseTurnActionAsync` returns a `TurnChoice` (FIGHT `MoveTurnChoice` / ITEM `ItemTurnChoice`) and defaults to delegating to `ChooseMoveAsync`, so only the interactive player input offers the bag. `AutoSelectInput` is the current default.
- **`IItemEffect` / `ItemEffects`** — the item-effect registry, keyed by `ItemCategory` (the item-side analogue of `IMoveEffect` / `MoveEffects`): Heal, StatusCure, PpRestore, X-item. Item *amounts* are data (read off the `Item` row); Revive/Ball are deferred (`ItemEffects.For` returns null). A transient **`Bag`** (item-id → qty, not yet persisted) gates and is consumed by `ItemAction`.
- **`StatStages`** — class on `Creature`; holds per-battle Attack/Defense/Special/Speed/Accuracy/Evasion stages clamped to [−6, +6]; cleared between battles. `Raise(stat, delta)` / `Of(stat)` are the generic accessors.
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

## Model Strategy (cost vs. depth)

Default to **Sonnet** for the main session — `/plan` discussion, `/sync`, `/test`, data verification,
doc/TODO edits, and routine `/dev` work are all well within its range. Reserve **Opus** for the genuinely
hard reasoning: designing a new generation seam, a tricky battle-math batch, or a high-risk refactor of a
central method (`AttackAction`, `Battle`).

Two ways to bring Opus in: switch the whole session with `/model opus` for a hard stretch, **or** stay on
Sonnet and delegate the hard job to the **`opus-engineer`** subagent (`.claude/agents/opus-engineer.md`),
which is pinned to Opus and returns a focused diff. Prefer delegation when the rest of the turn is routine;
prefer a full switch when the whole task is hard. See `AI_CONTEXT.md` → **Model Strategy** for the
profile→model mapping and how to brief the subagent so its cold start stays cheap.

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