# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Key Files — read on demand, by trigger

**This file (`CLAUDE.md`) is the only always-on primer.** The rest below are reference docs: read one
*when the task you're starting matches its trigger* — not preemptively at session start. (Reading all of
them up front burns ~25k tokens before the work is even scoped; almost none of it primes any single task.)

| File | Read it when… |
|:-----|:--------------|
| `ARCHITECTURE.md` | you need the **why** behind a design decision, the system map, or the full doc catalog (its §5 indexes every doc in the repo). The decision-log entry point. |
| `docs/TODO.md` | starting or finishing any task — it's the **authoritative** active task list. Always update it when a task completes. (Finished work is in `docs/TODO_ARCHIVE.md`; read that only to recover the history of a done item.) |
| `.claude/AI_CONTEXT.md` | you need a slash-command/profile definition (`/plan`, `/dev`, `/sync`, `/test`) or the **Tooling & Automation** reference (the pre-finish gate sequence — `format-gate`, `test-runner`, `requirements-review`, `pr-review` — the pre-commit hook, CSharpier, MCP servers). |
| `docs/DESIGN_GUIDES.md` | doing `/plan` (design) work — Gen 1 mechanics, type-balancing, move-import mapping. |
| `docs/DEFINITION_OF_READY.md` | doing `/plan` — the DoR checklist that is `/plan`'s exit criteria (a plan isn't done until every item is covered). |
| `docs/DEV_STANDARDS.md` | doing `/dev` (implementation) work — .NET/EF coding conventions and architecture rules. |
| `docs/DEFINITION_OF_DONE.md` | finishing a feature — the technical DoD the `pr-review` subagent checks. |
| `docs/STATE_MODEL.md` | touching battle state — the `Creature` permanent/transient split (`BattleState`). |
| `docs/GAME_LOOP.md` | working on the **run/roguelite loop** — the game-loop ↔ event model (battle & heal as events), the logic-drives-sequence rule, and the target event abstraction. |
| `docs/ENCOUNTER_DESIGN.md` | working on **encounters/acquisition** — the biome-graph run model, the `IEnemyArchetype` strength tiers, the type-themed pool, and the two gated acquisition channels (boss catch + themed draft). |
| `docs/GENERATION_SEAMS.md` | adding a gen-variable rule or a new generation — the seams (`ITypeChart`, `IBattleRules`, `IStatCalculator`) + the §5.0 gen-agnostic checklist. |
| `docs/DATA_IMPORT.md` | changing imported data — the `PokeApiConnector` pipeline, import-vs-runtime boundary, PokeAPI→model mapping. |

## Commands

> **`dotnet` = your SDK 9.0.200 install** (pinned in `global.json`). In the commands below, plain `dotnet`
> assumes the SDK is on your PATH. If your system `dotnet` is a runtime-only install with no SDK, invoke the
> SDK's full path instead — e.g. on this dev machine the SDK lives at `C:\Users\USER\.dotnet\dotnet.exe`, so
> every `dotnet …` below becomes `& "C:\Users\USER\.dotnet\dotnet.exe" …`. Substitute your own SDK path.

```powershell
dotnet build                          # Build all projects
dotnet run --project PokeApiConnector # Import data from PokeAPI
dotnet test tests/creaturegame.Tests  # Run all tests
```

To start the full dev environment (backend + Vite frontend + browser):
```powershell
.\dev.ps1
```
**Run this directly at the repo root.** It spawns two child `pwsh` windows (backend on `:5100`, frontend on `:5173`) and opens the browser once Vite is ready. If the browser does not open automatically after ~60 s, navigate to `http://localhost:5173` manually. Pass `-NoBrowser` (`.\dev.ps1 -NoBrowser`) to start the stack without auto-opening a tab — the usual choice when driving the app from the agent/tests.

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
dotnet test tests/creaturegame.Tests --filter "FullyQualifiedName~<MethodName>"
```

Formatting & the pre-commit gate (see `AI_CONTEXT.md` → **Tooling & Automation** for the why):
```powershell
dotnet tool restore          # once per clone — installs the pinned CSharpier
dotnet csharpier format .    # format C# (do NOT hand-align)
dotnet csharpier check .     # what the hook/CI runs
git config core.hooksPath .githooks                         # once per clone — arms .githooks/pre-commit
```
The `.githooks/pre-commit` hook runs `csharpier check` (always) + the full test suite (when `.cs` is staged) and **blocks the commit on failure**. When a feature is close to done, run the pre-finish gate sequence before proposing a commit — the **`format-gate`** subagent (CSharpier), the **`test-runner`** subagent (full suite), for battle/stat/move work the **`requirements-review`** subagent (Gen-1 / roguelite domain fidelity), and finally the **`pr-review`** subagent (Opus, technical DoD incl. generation-seam architecture, from `docs/DEFINITION_OF_DONE.md`). Each is a separate subagent so it can be invoked or edited on its own.

**Both review gates are hard to the pipeline, soft to the user — only the user clears a finding, never a subagent or you.** That applies to a `pr-review` **CHANGES-REQUESTED** exactly as it does to a `requirements-review` discrepancy: report the findings + fix cost + your recommendation, then **stop and let the user decide** (fix / waive / defer). Never run a fix→re-review loop on your own initiative — apply the agreed fix and report your own verification; a second Opus pass to confirm a small fix is waste.

**`pr-review` is Opus and costs real money (~80–100k tokens a run) — it is not automatic.** Run it when the diff touches **product code** (engine, web layer, importer) or a generation seam. **Skip it for a test-only or docs-only diff** unless the user asks — gates 1–2 and the pre-commit hook already cover those. Borderline: state the cost and let the user choose. More generally, when a mandated step would cost more than the change it checks, say so up front rather than spending silently.

EF Core migration commands require `DOTNET_ROOT` set so `dotnet-ef` finds the user-local SDK instead of the system runtime-only install:
```powershell
# Point DOTNET_ROOT/PATH at your SDK dir so dotnet-ef resolves it (example dir: C:\Users\USER\.dotnet)
$env:DOTNET_ROOT = "$HOME\.dotnet"; $env:PATH = "$HOME\.dotnet;$HOME\.dotnet\tools;$env:PATH"
dotnet ef migrations add <MigrationName> --project creaturegame --context <ContextName> --output-dir DB/Migrations/<Moves|Pokemon|Items>
dotnet ef migrations remove --project creaturegame --context <ContextName>
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

## Agent Profiles (.claude/AI_CONTEXT.md)

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

See `docs/TODO.md` for the full prioritised task list, completed items, and tech debt. Always update `docs/TODO.md` when finishing a task.

## Permissions

- **File edits and creation are always allowed** — make changes to existing files or create new ones without asking for confirmation first.
- **Git commits require explicit approval** — stage changes and propose a commit message, but do not run `git commit` until the user confirms. **Once a commit is approved, always push it to `origin`** (`master`) as part of the same step — no separate confirmation is needed for the push; approving the commit approves the push.
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