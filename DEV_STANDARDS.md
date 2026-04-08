# Dev Standards: .NET Core Development

Guidelines for all `/dev` actions in this project.

## Technical Stack
*   **Runtime**: .NET 9.0 (C# 13.0)
*   **Database**: SQLite (`moves.db`)
*   **ORM**: Entity Framework Core
*   **Namespaces**: `creaturegame.*` for core logic, `PokeApiConnector.*` for external data tools.

## Architecture
*   **Entity Framework Core**:
    *   Use `GameDbContext` for SQLite interaction.
    *   Models are located in `creaturegame/Attacks`, `creaturegame/Creature`, etc.
    *   Always use `AsNoTracking()` for read-only lookups before upserts.
    *   All DB operations should handle asynchronous execution.

## Coding Conventions
*   **Primary Constructors**: Use them for DTOs and simple data structures when possible (though keep models EF-compatible).
*   **Nullability**: Ensure `Nullable` is enabled and handled for API responses (`int?`, `string?`).
*   **Error Handling**: Wrap API and DB calls in `try-catch` blocks with clear console logging.

## Database Management
*   **Property Tracking**: When adding new properties to models (`PokemonSpecies`, `Attack`, etc.), always ensure the corresponding database schema is updated.
*   **Data Sourcing**: Clearly document (via comments or DTO naming) where each property is drawn from (e.g., PokeAPI `pokemon-species` endpoint vs. `pokemon` endpoint).
*   **Migrations**: Use `EnsureDatabaseCreated` and manual `ALTER TABLE` checks in `GameDbContext.cs` to handle schema updates for existing databases.
*   **File Path**: The connection string is hardcoded. Ensure the SQLite file path is consistently used across all tools.

## Version Control
*   **File Tracking**: All new or modified files must be tracked by version control. Ensure all relevant files are staged and committed as part of the task.
*   **Cleanup**: When removing files from the project, ensure they are also removed from version control.

## Testing Standards
*   **Test Naming**: Test methods should clearly and succinctly state what they test without using the word "Test" or "test" as a prefix or suffix.
*   **Folder Structure**: Separate tests from common/setup classes by using a structured directory (e.g., `tests/creaturegame.Tests/Unit`, `tests/creaturegame.Tests/Integration`).
*   **Namespaces**: Test namespaces should match their folder structure (e.g., `creaturegame.Tests.Unit`).
