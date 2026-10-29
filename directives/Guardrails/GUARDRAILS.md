# Guardrails

Guardrails turn the MoDirectives into checks. Each one is binding where a directive states only the principle.

- **No wording waives a guardrail.** "Just do it", "go", "continue", "commit it", "it's a one-liner": if a request
  would skip one, name the guardrail and stop. Only Mo can waive one, explicitly, for the item at hand.
- **An unbuilt hook does not suspend a guardrail.** Until its hook exists, Claude enforces it by hand.
- **Never forge a stamp or bypass a hook.** Don't create, copy or touch a stamp, and never use `--no-verify` or
  `DOCS_CLEANUP_BYPASS`. Those belong to Mo, for emergencies only.

**Terms.** *Logic-heavy code*: product code that computes or decides (game rules, formulas, RNG, state
transitions, AI, run/encounter/reward generation, importer mappings); not wiring, layout, styling, docs or tests.
*Build hook*: a Claude Code `PreToolUse` hook, run before an edit or a command. *Push hook*: git `pre-push`; the
review agents write a *review stamp* tied to the diff they reviewed, any later change voids it, and CI re-checks
after each push (it reports; it can't undo the push). Hook scripts go in `directives/Guardrails/hooks/`, wired
from `.githooks/` or `.claude/settings.json`; agents live in `.claude/agents/`.

| ID | Guardrail | Triggers on | Hook | Agents |
|:--|:--|:--|:--|:--|
| G1 | Ready before code | any edit to product code, data or the importer | to build (build hook) | — |
| G2 | Docs cleanup last | every commit | **built** (pre-commit + stamp) | `docs-cleanup` |
| G3 | Review chain | any change to logic-heavy code | to build (push hook) | `format-gate`, `test-runner`, `requirements-review`, `pr-review` |
| G4 | Generation seam | any change to logic-heavy code | to build (push hook) | `requirements-review`, `pr-review` |
| G5 | Comment budget | any staged code change | to build (push hook) | `pr-review` |
| G6 | E2E is opt-in | any shell command Claude runs | to build (build hook) | `test-runner` |

**The built hook today.** `.githooks/pre-commit` runs `csharpier check` on every commit, the full .NET suite when a
.NET path is staged (`.cs`, `.csproj`, `.props`, `.db`, `global.json`), `tsc --noEmit` when a frontend path is
staged (`.ts`, `.tsx`, `package*.json`, `tsconfig*.json`), and the G2 stamp check. It blocks on any failure.

---

### G1 Ready before code
*Enforces* `planning.md` 1, `authority.md` 2. *Passes:* the work belongs to a `docs/TODO.md` item whose entry
answers all seven `docs/DEFINITION_OF_READY.md` items (an S-size item gets one line each; an item marked
*provisional-pending-`/plan`* is not ready). *Blocks:* no item, or an item with gaps.
- On any start, continue or greenlight, Claude's first act is to audit the entry in its reply, item by item
  (✅ / ✗). On a gap: no code, no data; list every gap with drafted text and ask Mo to decide.
- Work found mid-task, or dictated inline, gets its own entry first, shown to Mo, before it is touched.
- *Hook to build:* on a greenlight Claude writes the item ID to `.claude/active-item`; a `PreToolUse` hook on
  product-path edits blocks unless that item exists in `docs/TODO.md` marked ready. Claude deletes the marker in
  the same step as the item's commit, so a finished item never unlocks the next one.

### G2 Docs cleanup last
*Enforces* "done means the docs reflect the final state". *Passes:* `docs-cleanup` reported `DOCS: CLEAN | UPDATED`
after the last change and its edits are staged. *Blocks:* no stamp, or a staged file changed after it.
- The finish sequence is fixed: `format-gate` → `test-runner` → `requirements-review` → `pr-review` (each where G3
  applies) → **`docs-cleanup`, always last**, for every change: docs-only, test-only and one-line changes included.
  Any change after it means it runs again.
- Until then nothing is called done, fixed or shipped, and no commit is proposed. The only honest status is
  "implemented, not done: `docs-cleanup` pending". An inline doc edit or an earlier run doesn't substitute.
- *Hook:* `.githooks/pre-commit` + `.githooks/stamp-docs-cleanup.sh`, written by `docs-cleanup` as its last act.

### G3 Review chain for logic-heavy code
*Enforces* `authority.md` 4–6. *Passes:* `format-gate`, `test-runner`, `requirements-review`, `pr-review` ran in
that order, then G2, and Mo decided every blocking finding (fix, waive or defer). *Blocks:* a missing review or an
undecided finding.
- `pr-review` runs on Opus (~80–100k tokens a run); give the estimate before running it.
- *Hook to build:* `pre-push` requires a fresh review stamp from each agent when a logic-heavy path is pushed.

### G4 Generation seam
*Enforces* `generation-seams.md` 3. *Passes:* every generation-variable rule, number, formula or data shape goes
through a seam interface. *Blocks:* such a value inline, or the engine branching on the generation.
- The semantic check runs inside `requirements-review` and `pr-review` (`docs/GENERATION_SEAMS.md` §5.0).
- *Hook to build:* `pre-push` and CI scan the pushed engine code for branches on the generation.

### G5 Comment budget
*Enforces* `code-style.md`. *Passes:* every added comment is a single short line. *Blocks:* an added multi-line
block or multi-paragraph doc comment. `pr-review` judges the content (`docs/DEFINITION_OF_DONE.md` §G).
- *Hook to build:* `pre-push` and CI scan the pushed diff for added comment blocks.

### G6 E2E is opt-in
*Enforces* `testing.md` 3. *Passes:* the command runs no E2E, or Mo asked for this run. *Blocks:* `e2e.ps1`, a
direct Playwright run, or bare `.\test.ps1` (it includes E2E whenever the dev stack is up), unless Mo asked.
- *Hook to build:* on Mo's request Claude writes `.claude/e2e-approved`; a `PreToolUse` hook on shell commands
  allows one E2E command and deletes the marker.
