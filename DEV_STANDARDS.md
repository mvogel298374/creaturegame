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

## Database File Path Management
*   The connection string is hardcoded to `J:/creaturegame/creaturegame/creaturegame/bin/Debug/net9.0/moves.db`.
*   Ensure the SQLite file path is consistently used across all tools.
