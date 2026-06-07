# Dev Standards: .NET Core Development

Guidelines for all `/dev` actions in this project.

## Technical Stack
*   **Runtime**: .NET 9.0 (C# 13.0)
*   **Backend**: ASP.NET Core (`creaturegame.Web`) — REST API, SignalR hub, static file server
*   **Frontend**: Vite + React 18 + TypeScript under `creaturegame.Web/ClientApp/`; SignalR JS client
*   **Database**: SQLite (`pokemon.db`, `moves.db`)
*   **ORM**: Entity Framework Core (two contexts: `PokemonDbContext`, `MovesDbContext`)
*   **Namespaces**: `creaturegame.*` for core logic, `creaturegame.Web.*` for the web host, `PokeApiConnector.*` for the data importer.

## Architecture
*   **Entity Framework Core**:
    *   Use `PokemonDbContext` for species data, `MovesDbContext` for move data. There is no single `GameDbContext`.
    *   Models are located in `creaturegame/Attacks`, `creaturegame/Creature`, `creaturegame/DB`, etc.
    *   Always use `AsNoTracking()` for read-only lookups before upserts.
    *   All DB operations should handle asynchronous execution.
*   **Generation seams** — any mechanic that varies between generations goes behind an interface, never
    hardcoded: `ITypeChart` (type matrix), `IBattleRules` (crit/accuracy/stat-stage/duration/denominator
    rules — add methods here, implement in `Gen1BattleRules`, never query a generation enum in battle
    logic), `IStatCalculator` (stat formulas, DV/IV, Stat-Exp/EV). The leaks that actually cost us aren't
    `if (gen == 1)` — they're inline gen-variable magic numbers (`* 1.5`, `< 50`), direct
    `Attributes.Attack/Special/Defense` reads in damage math (breaks on the Gen 2 Special split), and
    gen-shaped DB-column reads at battle call sites. **Clear the gen-agnostic checklist + definition-of-done
    in `GENERATION_SEAMS.md §5.0` as part of the feature, before it lands** — deferring it is the trap (the
    debt compounds invisibly until a generation switch forces it all at once). That file is the source of
    truth; this is the summary.

## Coding Conventions
*   **Primary Constructors**: Use them for DTOs and simple data structures when possible (though keep models EF-compatible).
*   **Nullability**: Ensure `Nullable` is enabled and handled for API responses (`int?`, `string?`).
*   **Error Handling**: Wrap API and DB calls in `try-catch` blocks with clear console logging.
*   **Formatting**: [CSharpier](https://csharpier.com) is the source of truth for C# layout — do **not**
    hand-align code into columns; let the formatter own whitespace. Commands + tool details: `CLAUDE.md`
    (commands) and `AI_CONTEXT.md` → **Tooling & Automation**.

## Database Management
*   **Property Tracking**: When adding new properties to models (`PokemonSpecies`, `Attack`, etc.), always add an EF Core migration — see `CLAUDE.md` for the migration command. Never use raw `ALTER TABLE`.
*   **Data Sourcing**: Clearly document (via comments or DTO naming) where each property is drawn from (e.g., PokeAPI `pokemon-species` endpoint vs. `pokemon` endpoint).
*   **Migrations**: All schema changes go through `dotnet ef migrations add <Name>`. `EnsureDatabaseCreated()` calls `Database.Migrate()` which applies all pending migrations automatically.
*   **File Path**: The connection string is hardcoded. Ensure the SQLite file path is consistently used across all tools.

## Version Control
*   **File Tracking**: All new or modified files must be tracked by version control. Ensure all relevant files are staged and committed as part of the task.
*   **Cleanup**: When removing files from the project, ensure they are also removed from version control.

## Pre-commit Gate
Two layers catch the seam/fidelity leak before commit: the **`/audit` skill** (LLM — the recurring-leak
checklist + `seam-reviewer` on the diff) and the **`.githooks/pre-commit`** hook (deterministic — CSharpier
+ tests). Both are documented in `AI_CONTEXT.md` → **Tooling & Automation**; run `/audit` before proposing
a commit on any battle/stat/move change.

## Testing Standards
*   **Test Naming**: Test methods should clearly and succinctly state what they test without using the word "Test" or "test" as a prefix or suffix.
*   **Folder Structure**: Separate tests from common/setup classes by using a structured directory (e.g., `tests/creaturegame.Tests/Unit`, `tests/creaturegame.Tests/Integration`).
*   **Namespaces**: Test namespaces should match their folder structure (e.g., `creaturegame.Tests.Unit`).

---

## See Also

| File | Role |
|:-----|:-----|
| `CLAUDE.md` | Session setup, architecture overview, build commands — loaded automatically each session |
| `TODO.md` | Authoritative task list; update it when any task completes |
| `AI_CONTEXT.md` | Agent profiles and slash-command definitions |
| `DESIGN_GUIDES.md` | Gen 1 mechanics and design constraints (design counterpart to this file) |
| `STATE_MODEL.md` | Deep-dive: `Creature` permanent/transient state split (`BattleState`) — patterns + Gen 1 domain logic |
| `GENERATION_SEAMS.md` | Deep-dive: generation seams (`ITypeChart`/`IBattleRules`/`IStatCalculator`) — patterns + per-gen domain logic |
| `DATA_IMPORT.md` | Deep-dive: the `PokeApiConnector` import pipeline — import-vs-runtime boundary, PokeAPI→model mapping, Gen 1 decisions |
