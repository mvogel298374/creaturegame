# Agent Context & Action Definitions

Profiles and slash-commands for this project's multi-agent workflow. Prefix a message with a command to
set the context; with none, use judgment to blend profiles (ask `/plan` vs `/dev` if ambiguous).

## Agent Profiles

### `/plan` — Lead Game Designer & Pokémon Mechanics Specialist
Designing a true Pokémon battle clone (with roguelike / autobattler / Infinite-Fusion influences).
Architectural and conceptual design *before* implementation. Knowledge base: `DESIGN_GUIDES.md`.
**Definition of done for a `/plan` pass:** every item in `docs/DEFINITION_OF_READY.md` is covered —
established before planning or resolved during it. Do not exit `/plan` (or hand off to `/dev`) with an
unchecked DoR item; the plan isn't done until the feature is *Ready*. (That file is the checklist; run it.)

### `/dev` — Senior .NET Core Software Engineer
Clean, testable, EF-optimized C# 13 / .NET 9; PokeAPI integration. Knowledge base: `DEV_STANDARDS.md`.
**Definition of done (battle/stat/move work):** clear the generation-agnostic checklist in
`GENERATION_SEAMS.md §5.0` *as part of the feature* — no inline gen-variable magic numbers, no direct
`Attributes.Attack/Special/Defense` reads in damage math, everything gen-variable behind the seams. Not a
follow-up cleanup. (That file is the source of truth for the rule; don't restate it, run it.) **When the
feature is close to done, run the pre-finish gate sequence** (`format-gate`, `test-runner`,
`requirements-review`, `pr-review`) before proposing a commit — see Tooling & Automation.

## Action Commands

| Command | Action |
| :--- | :--- |
| `/plan` | Analyze the conceptual side and propose a design that satisfies every DoR item (`docs/DEFINITION_OF_READY.md`). |
| `/dev` | Implement the current plan or fix a technical issue. |
| `/sync` | Review the database schema (`moves.db`) vs the current models. |
| `/test` | Write and run tests for the current module. |

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
saving. `opus-engineer` implements; it does not replace the `requirements-review` + `pr-review` gate before commit.

---

## Tooling & Automation

The recurring failure mode here is a **generation-seam / Gen-1 fidelity leak** — a gen-variable rule
hardcoded inline, a data value that reverts on re-import, a missed type-immunity — that passes the tests
green yet is wrong. These tools catch that class **before commit**. The gates are split deliberately: a
deterministic shell hook (format + tests) and LLM reasoning a shell can't do.

### Pre-finish gate sequence
When a feature is close to done, the main session runs these **separable** gates before proposing a commit —
each is its own subagent so it can be invoked or edited independently:

1. **`format-gate`** (Subagent, `.claude/agents/format-gate.md`) — the CSharpier gate: `check`, auto-`format`
   + re-check if it fails → `FORMAT: PASS | REFORMATTED | FAIL`.
2. **`test-runner`** (Subagent, `.claude/agents/test-runner.md`) — the full suite via `.\test.ps1`, TEST
   SUMMARY relayed verbatim, failing tests named → `TESTS: PASS | FAIL`.
3. **`requirements-review`** (Subagent, `.claude/agents/requirements-review.md`, Sonnet) — the domain gate,
   for battle/stat/move work. A Pokémon-Gen-1 + roguelite expert that challenges the implementation against
   the DoR-finalized plan, the internal docs, and its own knowledge, and flags undocumented behavior →
   `REQUIREMENTS: MET | DISCREPANCIES`. **Hard gate to the pipeline, soft gate to the user:** a discrepancy
   blocks progress to done/commit and no subagent may clear it; only the **user** adjudicates (fix or waive).
4. **`pr-review`** (Subagent, `.claude/agents/pr-review.md`, **Opus**) — the technical capstone, run **after**
   1–3 are green. Reviews the diff against the technical Definition of Done (`docs/DEFINITION_OF_DONE.md`) —
   generation-seam architecture, code quality, integration completeness, test adequacy, docs/TODO → `PR-READY
   | CHANGES-REQUESTED` (a hard technical gate). Technical quality only; domain fidelity is
   `requirements-review`'s. It treats format/tests/requirements as preconditions.

All four run and report; they don't fix or commit — and `requirements-review`'s discrepancies are cleared
only by the user. The `.githooks/pre-commit` hook still runs CSharpier + tests as the deterministic backstop
at commit time.

> The old `/audit` skill and `seam-reviewer` subagent are **retired.** Their two jobs were split by lane:
> Gen-1 / domain fidelity → `requirements-review`; generation-seam *architecture* (the first invariant) →
> `pr-review`. There is no longer an orchestrator skill — the main session runs the four gates in sequence.

### Pre-commit hook (`.githooks/pre-commit`)
Deterministic backstop: `csharpier check .` always, plus the full `dotnet test` suite when `.cs` is staged
(pure docs/data commits skip it). Blocks on failure. Enable once per clone: `git config core.hooksPath
.githooks`. Emergency bypass (avoid): `git commit --no-verify`.

### CSharpier (formatter)
Version-pinned local tool (`.config/dotnet-tools.json`); config `.csharpierrc.json`; EF migrations excluded
(`.csharpierignore`). `dotnet tool restore` once per clone; `dotnet csharpier format .` to format, `… check
.` as the gate. **Do not hand-align code** — let the formatter own whitespace (the one-shot reformat is in
`.git-blame-ignore-revs`).

### Debug battle narration (`CG_BATTLE_LOG`)
`ConsoleBattleEventEmitter` (in core) narrates a battle to stdout in Gen 1 flavour text — a dev aid for
watching a unit test play out (it is **never** wired into the app; the web client renders via `timeline.ts`).
Many `CoreMechanicsTests` already pass it as their emitter, but it is **silent by default**: output is gated
on the `CG_BATTLE_LOG` env var. To watch a test narrate, set the flag and filter to a small set (every test
using the emitter narrates while it's on, so an unfiltered run is a wall of text):
```powershell
$env:CG_BATTLE_LOG = "1"
dotnet test tests/creaturegame.Tests --filter "FullyQualifiedName~Substitute"
$env:CG_BATTLE_LOG = $null   # turn it back off
```
Any value other than `0`/`false` (or unset/empty) enables it.

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
| `agents/format-gate.md` | CSharpier formatting gate (Subagent) |
| `agents/test-runner.md` | Full test-suite runner (Subagent) |
| `agents/requirements-review.md` | Gen-1 / roguelite domain & requirements gate — hard to pipeline, soft to user (Subagent) |
| `agents/pr-review.md` | Opus technical / PR review incl. seam architecture — checks `DEFINITION_OF_DONE.md` (Subagent) |
| `DEFINITION_OF_READY.md` | DoR — the exit criteria of `/plan` |
| `DEFINITION_OF_DONE.md` | DoD (technical) — the rubric `pr-review` checks |
| `TODO.md` | Authoritative active task list (done work → `TODO_ARCHIVE.md`) |
| `DESIGN_GUIDES.md` | Gen 1 mechanics & design constraints (`/plan`) |
| `DEV_STANDARDS.md` | .NET/EF coding conventions (`/dev`) |
</content>
