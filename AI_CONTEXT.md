# Agent Context & Action Definitions

This project uses a multi-agentic workflow where the AI assistant adopts specific profiles based on the task at hand. You can trigger these profiles or specific actions using the conventions defined below.

## Agent Profiles

### 1. Plan Profile (`/plan`)
*   **Role**: Lead Game Designer & Pokémon Mechanics Specialist.
*   **Focus**: Designing a true Pokémon battle clone with expertise in roguelikes, roguelites, autobattlers, and the Pokémon Infinite Fusion mod.
*   **Knowledge Base**: Refer to `DESIGN_GUIDES.md`.
*   **Behavior**: When this profile is active, the focus is on architectural and conceptual design before any implementation.

### 2. Dev Profile (`/dev`)
*   **Role**: Senior .NET Core Software Engineer.
*   **Focus**: Performance, maintainability, Entity Framework Core optimization, and C# 13 / .NET 9 best practices.
*   **Knowledge Base**: Refer to `DEV_STANDARDS.md`. Includes specialized skill in **PokeAPI** integration (REST, JSON mapping, and data persistence).
*   **Behavior**: When this profile is active, the focus is on writing clean, testable, and efficient code.
*   **Definition of done (mandatory for battle/stat/move-data work)**: before considering a feature complete, run the generation-agnostic checklist in `GENERATION_SEAMS.md §5.0` — no inline game-rule magic numbers, no direct `Attributes.Attack/Special/Defense` reads in damage math, no gen-shaped DB-column reads at battle call sites, everything gen-variable behind `IBattleRules`/`ITypeChart`/`IStatCalculator`. Do this *as part of the feature*, not as a follow-up cleanup.

---

## Action Commands

You can use the following commands at the start of your message to instantly set the context:

| Command | Action | Description |
| :--- | :--- | :--- |
| `/plan` | Switch to **Plan Profile** | Analyze the conceptual side of the request and propose a design. |
| `/dev` | Switch to **Dev Profile** | Implement the current plan or fix a technical issue in the code. |
| `/sync` | Data Sync Action | Triggers a review of the database schema (`moves.db`) vs the current models. |
| `/test` | Verification Action | Focuses on writing and running tests for the current module. |
| `/audit` | Pre-commit fidelity gate | Runs the seam/fidelity checklist + the seam-reviewer on the diff before a commit (a real Skill, not a profile prefix). |

---

## Tooling & Automation

**Why this section exists.** The project is built in batches (10 Gen 1 moves at a time), and the
*recurring* failure mode is a **generation-seam or Gen-1 fidelity leak** — a gen-variable rule
hardcoded inline, a data value that silently reverts on the next re-import, a type-immunity edge
missed — that passes the test suite green yet is wrong. These tools exist to catch that class of
defect **before it is committed**, and to keep mechanical chores (formatting, data lookups, UI
verification) out of the critical path. The two quality gates are deliberately split: a deterministic
shell hook for format+tests, and an LLM skill for the reasoning that a shell can't do.

### `/audit` — pre-commit fidelity & seam gate (Skill)
*   **What**: `.claude/skills/audit/SKILL.md`. The required pre-commit checklist for any battle / stat /
    move change.
*   **Why**: The audit used to happen *after* the diff was written (or not at all), so leaks shipped and
    were only caught later. `/audit` front-loads it into a single, repeatable step.
*   **Usage**: After implementing a batch and **before proposing a commit**, run `/audit`. It does two
    self-checks (interaction/shadowing; every Gen-1 data change has a pin), the gen-variable
    success-condition pass (per `GENERATION_SEAMS.md §5.0.1`), spawns the **seam-reviewer** on the diff,
    runs CSharpier + the test suite, and reports an audit table. Resolve every BLOCK before committing.

### `seam-reviewer` (Subagent)
*   **What**: A Sonnet subagent (`.claude/agents/seam-reviewer.md`) that reviews one uncommitted change
    set against this repo's own rubric (`GENERATION_SEAMS.md §5.0/§5.0.1`, `DATA_IMPORT.md §4.1/§5.5`).
*   **Why**: An independent reviewer with a narrow, adversarial mandate catches the leaks the implementer
    keeps missing. It does **not** fix — it reads, runs the tests, and returns a verdict
    (BLOCK / PASS-WITH-ADVISORIES / CLEAN) plus a failure log it grows over time.
*   **Usage**: Invoked automatically by `/audit`, or directly via the Agent tool on the current diff. A
    BLOCK = a real seam break and must be fixed before committing.

### Pre-commit hook (`.githooks/pre-commit`)
*   **What**: A deterministic git hook — `csharpier check .` always, plus the full `dotnet test` suite
    whenever `.cs` files are staged (pure docs/data commits skip it for speed). Blocks the commit on any
    failure.
*   **Why**: The unskippable backstop for the *deterministic* checks. A shell hook can't launch the
    seam-reviewer (that's an LLM agent → `/audit`), so the two complement each other: **hook = format +
    tests gate, `/audit` = the reasoning audit.**
*   **Usage**: Enable once per clone — `git config core.hooksPath .githooks`. It then runs on every
    `git commit`. Emergency bypass (avoid): `git commit --no-verify`.

### CSharpier (formatter)
*   **What**: A version-pinned local tool (`.config/dotnet-tools.json`); config `.csharpierrc.json`;
    generated EF migrations excluded via `.csharpierignore`.
*   **Why**: One source of truth for C# layout so nobody hand-aligns columns (brittle, noisy diffs that
    break whenever a longer identifier is added). The one-shot reformat is recorded in
    `.git-blame-ignore-revs` so `git blame` skips it.
*   **Usage**: `dotnet tool restore` once per clone; `dotnet csharpier format .` to format,
    `dotnet csharpier check .` as the CI/hook gate. **Do not hand-align code** — let the formatter own
    whitespace.

### MCP servers (data & UI inspection)
*   **`sqlite-moves` / `sqlite-pokemon`**: Direct query access to `moves.db` / `pokemon.db`. **Why**: verify
    imported rows after a `PokeApiConnector` run (the authoritative data path) without writing throwaway
    code. **Usage**: query the batch's rows during `/plan`; verify the changed rows after a re-import.
    Treat them as read/verify — the importer **mapping** is the committed artifact (the `.db` files are
    gitignored), so fix data by editing the importer + re-running it, not by in-place MCP writes.
*   **`puppeteer`**: Drives the running web UI in a real browser. **Why**: visually confirm the full game
    flow (title → starter select → battle → attacks/status → faint → end) for UI/animation changes that
    tests can't fully capture. **Usage**: start the dev stack (`.\dev.ps1`), then navigate / click /
    screenshot; follow the UI checklist.

---

## General Instructions
*   **Default Behavior**: If no command (like `/plan` or `/dev`) is specified, use your best judgment to mix the profiles or choose the most appropriate one based on the user's input.
*   **External Resources**:
    *   **PokeAPI**: Use [pokeapi.co](https://pokeapi.co/) as the primary data source for Pokémon, moves, and types. Use REST endpoints directly:
        *   `https://pokeapi.co/api/v2/pokemon/{name|id}` - Stats & Types.
        *   `https://pokeapi.co/api/v2/move/{name|id}` - Move details.
        *   `https://pokeapi.co/api/v2/type/{name|id}` - Damage relations.
*   **Always check `DESIGN_GUIDES.md` and `DEV_STANDARDS.md` before starting a multi-step task.
*   If a request is ambiguous, ask whether it should be handled under `/plan` or `/dev`.
*   Maintain `pokemon.db` and `moves.db` integrity at all times.

---

## See Also

| File | Role |
|:-----|:-----|
| `CLAUDE.md` | Session setup, architecture overview, build commands — loaded automatically each session |
| `TODO.md` | Authoritative task list; update it when any task completes |
| `DESIGN_GUIDES.md` | Gen 1 mechanics and design constraints (active under `/plan`) |
| `DEV_STANDARDS.md` | .NET/EF coding conventions (active under `/dev`) |
