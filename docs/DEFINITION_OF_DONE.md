# Definition of Done (DoD) — technical

The **technical** done-criteria for a feature, checked by the `pr-review` subagent (Opus) as the capstone of
the pre-finish gate sequence. Scope is **technical / PR quality**, including **generation-seam architecture**
(the project's first invariant).

One lane is deliberately **not** here, because another reviewer owns it:
- **Domain / Gen-1 requirements fidelity** — is the mechanic faithful, is the *value* the right Gen-1 number,
  does it match the finalized plan and the docs → owned by the `requirements-review` subagent.

`pr-review` treats requirements fidelity as a **precondition** — it does not re-derive it. (Requirements is a
hard gate to the pipeline but soft to the user: only the user waives a requirements discrepancy.)

## Preconditions (green before `pr-review` runs)
- `format-gate` — CSharpier `PASS` or `REFORMATTED`.
- `test-runner` — full suite green.
- `requirements-review` — `REQUIREMENTS: MET`, or every discrepancy user-adjudicated (fixed or explicitly
  waived by the user).

## Technical DoD

**A. Generation-seam architecture** (the first invariant)
- No generation check in battle logic (`if (generation == …)`, `"gen1"` strings, `Generation` enum
  inspection). The `Generation` *data column* on learnsets is fine.
- No gen-variable game-rule magic number inline — multipliers, divisors, thresholds, durations, chances,
  stage values live on `IBattleRules` / `ITypeChart` / `IStatCalculator`, not in the branch. (Gen-invariant
  constants inline are fine — 4 move slots, the `+2`, confusion base power 40.)
- Damage/effect math reads stats via `rules.GetOffensiveStat` / `GetDefensiveStat`, never
  `creature.Attributes.Attack/Special/Defense` directly; no `Attributes` mutation to fake a modifier.
- Every new seam member carries a per-generation XML doc.
- Litmus: "when we build Gen 2, will this value/layout change?" → if yes, it belongs on a seam.

**B. Architecture & design**
- New code is reachable and integrates cleanly — no dead/unreachable branches, no broad rule that shadows an
  existing narrow handler of the same concern.
- No overengineering or scope creep: only what the task requires. No speculative abstractions, no error
  handling for states that can't occur, no half-finished implementations.
- Central-method changes (`AttackAction`, `Battle`, `DamageCalculator`) preserve existing control-flow
  contracts — a "whenever the target takes damage" hook fires in **every** damage-category branch (or via one
  shared helper), not just the Standard path. Transient effects that mutate permanent state are undone inside
  `ResetBattleState`, not only at battle end. A condition that gates an effect is snapshotted at impact,
  before the same action mutates what it reads.

**C. Code quality & conventions** (`DEV_STANDARDS.md`)
- C# 13 / .NET 9 conventions: primary constructors for DTOs, nullable handled on API/DB response types, async
  DB reads with `AsNoTracking()`, API/DB calls wrapped in try/catch with logging.
- Naming reads with the surrounding code; no needless duplication or recompute; test names state what they
  test (no `Test` prefix/suffix). Whitespace is CSharpier's — no hand alignment.

**D. Integration completeness**
- **Any new field on a client-facing wire DTO** — a SignalR `BattleEvent` / `MoveInfo`, *or* a REST payload
  (`BagItemView`, `BagItem`, and the like) — carries both its server-side projection **and** a field-level
  guard test that pins that projection. Engine/unit tests pass while a wire projection is silently missing or
  mis-wired (negated), so this is verified directly, per field. This is a **required** lane, not an advisory:
  a new wire field without a projection guard is `CHANGES-REQUESTED`. "The projection is buried in a
  live-session read, so it's hard to test" is not a waiver — extract a pure helper to make it testable
  (precedent: `GameSessionManager.ProjectBagView`, `EncounterFactory.BuildStartingBag`) and pin it.

**E. Test quality (technical)**
- Coverage matches the change surface: edge and error paths exercised, not just the happy path. A data value
  Gen 1 differs from modern on has a pinning contract test (`SecondaryChanceDataContractTests` or equivalent)
  so a re-import can't silently revert it. (Whether the value is the *correct* Gen-1 number is the requirements
  reviewer's concern; that a pin *exists* is technical.)

**F. Docs & process**
- Any documented model the change alters is updated (`STATE_MODEL`, `GAME_LOOP`, `ENCOUNTER_DESIGN`,
  `GENERATION_SEAMS`, `DATA_IMPORT`, …).
- `TODO.md` updated (task → done / archive) — this is produced by the mandatory `docs-cleanup` gate (step 1 of
  the pre-finish sequence) and must already be staged in the finishing commit by the time `pr-review` runs.
- Commit message proposed; commit only on explicit user approval.

## Verdict
`pr-review` returns **PR-READY** or **CHANGES-REQUESTED**, with each requested change tied to a DoD lane and
an exact `file:line`. `CHANGES-REQUESTED` is a hard technical gate — resolve before commit.
