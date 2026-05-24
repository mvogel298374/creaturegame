# Design Guides: Pokémon & Game Mechanics

This document outlines the core Pokémon and RPG design principles used in this project. All `/plan` actions must adhere to these guidelines.

## Core Vision: True Pokémon Battle Clone
*   The primary goal is to design and implement a true Pokémon battle clone.
*   The design incorporates deep knowledge and mechanics from:
    *   **Roguelikes and Roguelites**: For progression, randomized elements, and replayability.
    *   **Autobattlers**: For strategic positioning, automated execution phases, or synergistic team building.
    *   **Pokémon Infinite Fusion mod**: For advanced fusion mechanics, custom sprite/stat generation, and expanded movepools.

## Pokémon Move Data Importation
*   **Source**: [PokeAPI](https://pokeapi.co/)
*   **Generation Focus**: Initial focus is on Generation 1 (Moves 1-165).
*   **Data Mapping**:
    *   `Power` → `BaseDamage`
    *   `Accuracy` → `Accuracy`
    *   `PP` → `PowerPointsMax`
    *   `Damage Class` → `AttackType` (Physical/Special)
    *   `Type` → `DamageType` (Mapping to our 18-type system)

## Type Advantages & Balancing
*   The game uses all 18 standard types (Normal, Fire, Water, etc.).
*   Future `/plan` tasks should define the effectiveness matrix (1.0x, 2.0x, 0.5x, 0.0x).
*   Stat growth formulas must consider base stats from the PokeAPI species data.

## Generation Architecture Principle

Any mechanic that differs between Pokémon generations **must** be implemented behind an interface, not hardcoded. A generation switch means swapping implementations — zero changes to core battle logic.

**Canonical seams (use these; extend them; never bypass them):**

| Interface | Governs |
|:----------|:--------|
| `ITypeChart` | Type effectiveness matrix |
| `IBattleRules` | Battle mechanics that vary by generation — crit formula, stat-stage multiplier table, freeze thaw conditions, accuracy quirks, etc. |

**Rules for contributors:**
- Before implementing any mechanic: check whether it is the same in all generations.
- If it differs: add a method or property to `IBattleRules` (or `ITypeChart` if it is purely a type relationship), implement it in `Gen1BattleRules`, and keep the caller generation-agnostic.
- Never query a generation enum or a string like `"gen1"` inside battle logic.
- `Gen1BattleRules.Instance` is the default everywhere; future generation support requires only a new implementation class.

## Move Design Constraints
*   Moves with "Undefined" damage class should be reviewed and assigned a type if they are status moves.
*   Descriptions should prioritize "short effects" for in-game tooltips.

---

## See Also

| File | Role |
|:-----|:-----|
| `CLAUDE.md` | Session setup, architecture overview, build commands — loaded automatically each session |
| `TODO.md` | Authoritative task list; update it when any task completes |
| `AI_CONTEXT.md` | Agent profiles and slash-command definitions |
| `DEV_STANDARDS.md` | .NET/EF coding conventions (implementation counterpart to this file) |
