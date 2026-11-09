---
name: lint-diff
description: The G3 lint step. Runs Rider's code analysis over the C# files in the working diff and reports LINT PASS, FINDINGS or N/A. Use after the format step and before the test step on any logic-heavy change, or whenever a diff needs a fast compile-and-analysis check.
---

# Lint the diff

Binding rule: `directives/Guardrails/GUARDRAILS.md` G3. Run it after the format step and before the test step. Any
later code change voids it.

1. **List the changes.** Run `.\.claude\skills\lint-diff\changed-cs.ps1` from the repo root with the PowerShell
   tool. It prints `[{ "file": ..., "ranges": [[start, end], ...] | "all" }]`, covering staged, unstaged and untracked
   C#, without deleted files or EF migrations. On `[]`, report `LINT: N/A` and stop.

2. **Run the analysis.** Call `mcp__rider__lint_files`, loading it through ToolSearch if it is deferred:
   - `files`: every `file` from step 1
   - `min_severity`: `"warning"`
   - `timeout`: `60000`
   - `rootFolder`: `"J:/creaturegame/creaturegame"` (Rider refuses the call without it)

   Retry once on a transport error. If the tool is missing or Rider is closed, the step cannot run: name G3, ask Mo
   to open Rider with this solution (and to restart Claude Code if the tools were never loaded), and stop. Nothing
   substitutes for this step, and only Mo can waive it.

3. **Sort the results.** `{"items":[]}` means no problems.

   | Severity | Treatment |
   |:--|:--|
   | `ERROR` | Blocks, on any line of any listed file |
   | `WARNING` | A finding only when its line is inside the file's `ranges` (or `ranges` is `"all"`) |
   | `WEAK WARNING` | Ignored. It is style noise, some of which contradicts CSharpier |

   A file with `timedOut: true` or a `notAnalyzedReason`, or a top-level `more: true`, was not fully analysed. Re-run
   those files alone; if they still don't complete, report them as findings. Never report them as PASS.

4. **Report** one line, `LINT: PASS`, `LINT: FINDINGS` or `LINT: N/A`, then each finding as `file:line`, severity
   and message.

5. **Act on findings.** Fix an `ERROR`. A `WARNING` on a changed line is fixed, or goes to Mo under
   `directives/MoDirectives/authority.md` rules 4 and 5. After any fix, return to the format step.

Never use Rider's reformat tools here: CSharpier owns formatting.
