# Agent Context & Action Definitions

Profiles and slash-commands for this project's multi-agent workflow. Prefix a message with a command to
set the context; with none, use judgment to blend profiles (ask `/plan` vs `/dev` if ambiguous).

## Agent Profiles

### `/plan` — Lead Game Designer & Pokémon Mechanics Specialist
Designing a true Pokémon battle clone (with roguelike / autobattler / Infinite-Fusion influences).
Architectural and conceptual design *before* implementation. Knowledge base: `DESIGN_GUIDES.md`.

### `/dev` — Senior .NET Core Software Engineer
Clean, testable, EF-optimized C# 13 / .NET 9; PokeAPI integration. Knowledge base: `DEV_STANDARDS.md`.
**Definition of done (battle/stat/move work):** clear the generation-agnostic checklist in
`GENERATION_SEAMS.md §5.0` *as part of the feature* — no inline gen-variable magic numbers, no direct
`Attributes.Attack/Special/Defense` reads in damage math, everything gen-variable behind the seams. Not a
follow-up cleanup. (That file is the source of truth for the rule; don't restate it, run it.)

## Action Commands

| Command | Action |
| :--- | :--- |
| `/plan` | Analyze the conceptual side and propose a design. |
| `/dev` | Implement the current plan or fix a technical issue. |
| `/sync` | Review the database schema (`moves.db`) vs the current models. |
| `/test` | Write and run tests for the current module. |
| `/audit` | Pre-commit fidelity gate — the seam/fidelity checklist + `seam-reviewer` on the diff (a real Skill, not a profile prefix). |

---

## Model Strategy

Default to **Sonnet** for the main session; reserve **Opus** for hard reasoning. Mapping:

| Work | Model |
|:-----|:------|
| `/plan` discussion, `/sync`, `/test`, data verification, doc/TODO edits, routine `/dev` (coverage rows, wiring an existing mechanic) | **Sonnet** |
| New generation seam design, a tricky battle-math batch, a high-risk refactor of a central method (`AttackAction`, `Battle`) | **Opus** |

Bring Opus in either way:
- **Full switch** — `/model opus` when the whole task is hard.
- **Delegate** — stay on Sonnet and hand the hard job to the **`opus-engineer`** subagent
  (`.claude/agents/opus-engineer.md`, pinned to Opus) via the Agent tool. Prefer this when the rest of the
  turn is routine.

**Cold-start cost:** a subagent re-derives context from scratch, so brief it tightly — name the exact files
to read (e.g. `GENERATION_SEAMS.md §5.0`, the specific `Combat/*.cs`), state the seam constraints inline,
and give a concrete done-condition. A vague brief makes the agent re-read the whole doc set and erases the
saving. `opus-engineer` implements; it does not replace the `/audit` + `seam-reviewer` gate before commit.

---

## Tooling & Automation

The recurring failure mode here is a **generation-seam / Gen-1 fidelity leak** — a gen-variable rule
hardcoded inline, a data value that reverts on re-import, a missed type-immunity — that passes the tests
green yet is wrong. These tools catch that class **before commit**. The two gates are split deliberately: a
deterministic shell hook (format + tests) and an LLM skill (the reasoning a shell can't do).

### `/audit` — pre-commit fidelity & seam gate (Skill)
`.claude/skills/audit/SKILL.md`. Run after implementing a battle/stat/move change and **before proposing a
commit**. Does the two self-checks (interaction/shadowing; every Gen-1 data change has a pin), the
gen-variable success-condition pass (`GENERATION_SEAMS.md §5.0.1`), spawns **`seam-reviewer`** on the diff,
runs CSharpier + tests, and reports an audit table. Resolve every BLOCK before committing.

### `seam-reviewer` (Subagent)
`.claude/agents/seam-reviewer.md` (Sonnet). Reviews one uncommitted change set against this repo's rubric
(`GENERATION_SEAMS.md §5.0/§5.0.1`, `DATA_IMPORT.md §4.1/§5.5`) and returns a verdict (BLOCK /
PASS-WITH-ADVISORIES / CLEAN) plus a failure log it grows over time. It reviews — it does not fix. Invoked
automatically by `/audit`, or directly via the Agent tool. A BLOCK is a real seam break; fix before commit.

### Pre-commit hook (`.githooks/pre-commit`)
Deterministic backstop: `csharpier check .` always, plus the full `dotnet test` suite when `.cs` is staged
(pure docs/data commits skip it). Blocks on failure. Enable once per clone: `git config core.hooksPath
.githooks`. Emergency bypass (avoid): `git commit --no-verify`.

### CSharpier (formatter)
Version-pinned local tool (`.config/dotnet-tools.json`); config `.csharpierrc.json`; EF migrations excluded
(`.csharpierignore`). `dotnet tool restore` once per clone; `dotnet csharpier format .` to format, `… check
.` as the gate. **Do not hand-align code** — let the formatter own whitespace (the one-shot reformat is in
`.git-blame-ignore-revs`).

### MCP servers (data & UI inspection)
- **`sqlite-moves` / `sqlite-pokemon`** — read/verify `moves.db` / `pokemon.db` rows after a
  `PokeApiConnector` run, without throwaway code. Treat as read-only: fix data by editing the importer +
  re-running it (the `.db` files are gitignored; the importer mapping is the committed artifact).
- **`puppeteer`** — drives the running web UI to confirm the full game flow (title → select → battle →
  status → faint → end) for changes tests can't capture. Start the dev stack (`.\dev.ps1`), then follow the
  UI checklist.

---

## External Resources
- **PokeAPI** ([pokeapi.co](https://pokeapi.co/)) — used by `PokeApiConnector` only, never at runtime:
  `/pokemon/{name|id}` (stats & types), `/move/{name|id}`, `/type/{name|id}` (damage relations).
- Maintain `pokemon.db` and `moves.db` integrity at all times.

## See Also

| File | Role |
|:-----|:-----|
| `CLAUDE.md` | Session setup, architecture, build commands, model strategy — loaded automatically |
| `TODO.md` | Authoritative active task list (done work → `TODO_ARCHIVE.md`) |
| `DESIGN_GUIDES.md` | Gen 1 mechanics & design constraints (`/plan`) |
| `DEV_STANDARDS.md` | .NET/EF coding conventions (`/dev`) |
</content>
