# Rider practice

Lessons Claude derived from working with Rider's MCP server. They serve `MoDirectives/testing.md`, `code-style.md`
and `data.md`.

1. **Refactor with Rider.** A rename or signature change across files uses `rename_refactoring` or
   `change_api_signature` with `preview=true` first, then CSharpier and a `.\test.ps1 -Filter` run. Preview doesn't
   check conflicts, and removing a parameter leaves references inside method bodies to fix by hand.
2. **Quick mid-task check.** Use `lint_files` at `min_severity: error` instead of a full build.
3. **Hard-to-grep names.** Use `search_symbol` when a name is too common for Grep.
4. **Edits stay in Edit and Write.** Don't use Rider's `apply_patch` or `create_new_file`, so there is one edit path.
5. **Debug tests only through `trace-turn`.** Never point `xdebug_start_debugger_session` at a test: every test run
   goes through `.\test.ps1`. The tool stays allowed because debugging the web host is a legitimate use.
6. **Basics.** Always pass `rootFolder`, or Rider refuses the call. `execute_run_configuration`,
   `execute_terminal_command`, `reformat_file`, `execute_sql_query` and `execute_tool` are off limits; the denies live
   only in `.claude/settings.local.json`, so a new clone won't have them. If Rider is closed, fall back to Grep and a
   build, except for the G3 lint step, which stops.
