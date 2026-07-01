# Design Guides: Pokémon & Game Mechanics

This document outlines the core Pokémon and RPG design principles used in this project. All `/plan` actions must adhere to these guidelines.

## Core Vision: True Pokémon Battle Clone
*   The primary goal is to design and implement a true Pokémon battle clone.
*   The design incorporates deep knowledge and mechanics from:
    *   **Roguelikes and Roguelites**: For progression, randomized elements, and replayability.
    *   **Autobattlers**: For strategic positioning, automated execution phases, or synergistic team building.
    *   **Pokémon Infinite Fusion mod**: For advanced fusion mechanics, custom sprite/stat generation, and expanded movepools.

## Data Architecture: Import vs. Runtime

At runtime the game reads exclusively from **our own SQLite databases and static files**.
PokeAPI is never called by the game server or the frontend.

| Layer | Data source |
|:------|:------------|
| Species stats, types, growth rates | `pokemon.db` (our DB) |
| Move names, power, accuracy, PP, status effects | `moves.db` (our DB) |
| Battle sprites (front/back PNGs) | `wwwroot/sprites/` (our static files) |

`PokeApiConnector` is a **one-time import tool** that populates the DBs and downloads
sprite assets. Once run, it is not needed again unless you want to re-import or extend the
dataset. Keeping PokeAPI out of the runtime path means the game works offline and is not
affected by external API changes or outages.

**If you want to change or extend data** — edit the database directly, add new migration
fields to the model, or replace/supplement the importer. Do not add runtime PokeAPI calls.

## Pokémon Data Import (PokeApiConnector)
*   **Import source**: [PokeAPI](https://pokeapi.co/) — used by `PokeApiConnector` only
*   **Generation focus**: Generation 1 (Pokémon IDs 1–151, Move IDs 1–165)
*   **Move data mapping**:
    *   `Power` → `BaseDamage`
    *   `Accuracy` → `Accuracy`
    *   `PP` → `PowerPointsMax`
    *   `Damage Class` → `AttackType` (Physical/Special)
    *   `Type` → `DamageType` (mapped to our 18-type enum)
*   **Sprite download**: front and back battle sprites saved to `wwwroot/sprites/front/{id}.png`
    and `wwwroot/sprites/back/{id}.png` — served as static files, never fetched at runtime

## Type Advantages & Balancing
*   The `DamageType` enum defines all 18 modern types, but **Gen 1 only used 15** — Dark and Steel arrived in Gen 2 (→ 17), Fairy in Gen 6 (→ 18).
*   The effectiveness matrix is implemented in `Gen1TypeChart` — the 15 Gen 1 types, with the Gen 1 quirks preserved (Ghost→Psychic = 0×, Poison→Bug = 2×; no Steel/Dark/Fairy matchups).
*   Stat growth formulas use base stats from `pokemon.db`; `Gen1StatCalculator` implements the Gen 1 formula.

## Generation Architecture Principle

Any mechanic that differs between Pokémon generations **must** be implemented behind an interface, not hardcoded. A generation switch means swapping implementations — zero changes to core battle logic.

**Canonical seams (use these; extend them; never bypass them):**

| Interface | Governs |
|:----------|:--------|
| `ITypeChart` | Type effectiveness matrix |
| `IBattleRules` | Battle mechanics that vary by generation — crit formula, damage variance, stat-stage multiplier table, accuracy scale, freeze/thaw rules, status damage rates, XP formula, **stat selection** (`GetOffensiveStat` / `GetDefensiveStat`) |
| `IStatCalculator` | Stat calculation formulas — HP and other stat formula, DV randomisation, Stat Exp scaling. Gen 3+ will swap for a 0–31 IV / 252-cap EV implementation. |

Before implementing any mechanic, check whether it is the same in all generations; if it differs, it goes
on a seam (caller stays generation-agnostic, never branches on a generation enum). `Gen1BattleRules.Instance`
is the default everywhere; a new generation is a new implementation class, not edits to battle logic. The
implementation rules + the gen-agnostic checklist live in `GENERATION_SEAMS.md §5.0` (read before `/dev`
work on a gen-variable rule).

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
| `GENERATION_SEAMS.md` | Developer deep-dive on the generation seams that implement the architecture principle above |
| `GAME_LOOP.md` | The run/roguelite loop — the game-loop ↔ event model a battle is one event inside |
| `ENCOUNTER_DESIGN.md` | The roguelite encounter layer — biome-graph run model, enemy strength tiers, type-themed pools, gated acquisition |
| `DATA_IMPORT.md` | Developer deep-dive on the `PokeApiConnector` import pipeline and the import-vs-runtime boundary |
