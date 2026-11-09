---
name: trace-turn
description: Pause one xUnit test in Rider's debugger through test.ps1 and read the live battle state at a chosen line. Use to diagnose a wrong turn order, a seam returning the wrong value, or any battle behaviour that test output and CG_BATTLE_LOG can't explain.
---

# Trace a turn

Every call below needs `rootFolder: "J:/creaturegame/creaturegame"`. Load the `mcp__rider__xdebug_*` tools through
ToolSearch if they are deferred. If they are missing or Rider is closed, ask Mo to open Rider with this solution and
stop. Never launch the test from Rider's debugger: every test run goes through `.\test.ps1`.

This is diagnosis. It never counts as the G3 test step, and it doesn't authorise an engine change (G1).

1. **Choose the test and the lines.** Filter to exactly one test, because a wider filter pauses on every test that
   reaches the line. Pick executable lines, usually the call to or result of an `IBattleRules` member.

2. **Set breakpoints before starting.** Call `xdebug_set_breakpoint` with `filePath`, `line`, and a C# `condition`
   when the line runs often (for example `check == PreMoveCheck.Recharge`). Confirm the returned `lineText` is the
   line you meant. Don't use logpoints: Rider doesn't report their output on .NET.

3. **Start the test.** With the PowerShell tool and `run_in_background`:
   `.\test.ps1 -Filter "FullyQualifiedName~<TestName>" -WaitForDebugger`

4. **Wait for the process ID.** With the Bash tool and `run_in_background`, poll the test's output file until it
   matches `Process Id|TEST SUMMARY|Build FAILED`, with a bounded loop. Read `Process Id: <pid>, Name: testhost` from
   it. Anything else means the build or the filter failed.

5. **Attach.** `xdebug_attach_to_process` with `pid`, `debuggerKind: "netcore"`, `timeout: 60000`.

6. **Get past the startup break.** `xdebug_control_session` with `WAIT_FOR_PAUSE` stops first in the test platform's
   `DebuggerBreakpoint.Break`. That stop is expected, as is a "Didn't find the associated module" error at this
   point. Call `RESUME` once; it returns paused at your first breakpoint, with `frameValues`.

7. **Inspect.** Read `frameValues`, then `xdebug_get_stack`, `xdebug_evaluate_expression` and
   `xdebug_get_value_by_path`. Step or `RESUME` to move on; frame indexes go stale after any move. Evaluate only
   side-effect-free expressions, since a call that rolls the RNG changes the battle. Never use `xdebug_set_variable`.
   Mo's exception breakpoints can also pause the session on a thrown exception; `RESUME` past those.

8. **Clean up, every time.** `xdebug_remove_breakpoint` with `owner: "agent"` (never `user`, which holds Mo's). Then
   `RESUME` until the status is `stopped`, and relay the `TEST SUMMARY` verbatim. If the test never gets attached, it
   waits forever: stop the background task, then `Stop-Process -Id <pid>` on that exact PID.

9. **Report** each observed value with its `file:line`, then what Gen 1 does according to the pret disassembly
   (`directives/MoDirectives/generation-seams.md` rule 2), and whether they differ.
