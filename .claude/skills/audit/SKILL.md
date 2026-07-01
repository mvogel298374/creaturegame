---
name: audit
description: Pre-commit fidelity + generation-seam audit for a move/mechanic change (typically one Gen 1 attack-coverage batch). Run AFTER implementing and BEFORE proposing a commit. Encodes the recurring-leak checklist and spawns the seam-reviewer subagent, so the audit is a required step, not a post-hoc discovery.
---

# /audit — pre-commit fidelity & seam gate

Run this on the **uncommitted diff** after implementing a batch (or any battle/stat/move change) and
**before proposing a commit**. The deterministic half (format + tests) is also enforced by the
`.githooks/pre-commit` hook; this skill adds the parts a shell hook can't do — the Gen-1 fidelity
reasoning and the LLM seam review. Do not skip it because the hook exists; the hook is the backstop,
this is the actual audit.

Authoritative rubric (re-read each run): `GENERATION_SEAMS.md` §5.0 + §5.0.1, `DATA_IMPORT.md` §4.1/§5.5,
`DESIGN_GUIDES.md`, and the batch playbook in memory.

## Steps

1. **Self-check — interaction / shadowing.** For every new move or mechanic, ask: does it interact with
   an existing branch in a way that shadows or is shadowed? (e.g. a new `MoveEffect`/`DamageCategory`
   arm vs the type-immunity guard, the lock-in selection chain, Counter/Bide damage recording.) Confirm
   the new code is actually reachable and the precedence is right.

2. **Self-check — data change needs a data pin.** Any value that came from the importer (effect, power,
   accuracy, secondary chance, stat delta, category) that Gen 1 differs from modern on MUST have a pin
   in `SecondaryChanceDataContractTests` (or equivalent), or a re-import can silently revert it. List
   each data change and the test that pins it.

3. **Gen-1 fidelity pass (the recurring leak class).** Per the KNOWN LEAK CLASS in `GENERATION_SEAMS.md`
   §5.0.1: any **success condition or damage modifier** on a `DamageCategory`/`AttackAction` branch is
   almost always **gen-variable** even if the move exists in every gen — it belongs on `IBattleRules`,
   not inline; never mutate `creature.Attributes` to fake a modifier; never trust an inline "Gen 1 rule"
   comment without checking the real Gen 1 source. The rare genuinely-invariant condition (e.g. Dream
   Eater requires sleep) may stay inline — but justify it with the litmus "would this change in Gen 2?".
   Verify type immunities explicitly (Ghost↔Normal/Fighting 0×, the Ghost→Psychic Gen 1 bug, Poison/Fire
   status immunity, etc.). **Test the quirk, not just the outcome.**

4. **Spawn the seam-reviewer.** Launch the `seam-reviewer` subagent (Sonnet) on the uncommitted diff via
   the Agent tool. Give it the batch summary + the design calls to scrutinize. Relay its VERDICT verbatim:
   - **BLOCK** → fix every blocker and re-verify (read the fix + a guarding test, or re-spawn) before any commit.
   - **PASS-WITH-ADVISORIES** → fix or consciously defer each advisory with a one-line reason.
   - **CLEAN** → proceed.
   When a new failure mode is found, append it to the reviewer's Failure log so it strengthens over time.

5. **Format + tests (the deterministic gate).** Run:
   - `dotnet csharpier check .`
   - `dotnet test tests/creaturegame.Tests`
   - `.\test.ps1 -Web` if the frontend changed.
   Relay the TEST SUMMARY / pass counts verbatim (e.g. "730/730"). These also run in the pre-commit hook,
   but run them here so the audit report is complete and failures are caught before you write the message.

6. **Report, then commit.** Produce a short audit table — each new move → type/immunity interactions →
   dead-branch check → secondary chance pinned? — plus the seam-reviewer verdict and the test counts.
   Only then update `TODO.md` + the batch-playbook memory and **propose** the commit (commits need
   explicit user approval per `CLAUDE.md`). If anything is unresolved, stop and surface it instead.
