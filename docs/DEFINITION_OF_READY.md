# Definition of Ready (DoR)

**The exit criteria for a `/plan` pass.** A task is not *Ready* to implement until every item below is
covered — either already established before planning, or resolved during the `/plan` discussion. `/plan`
owns this list: do not exit a plan (or hand off to `/dev`) with an unchecked DoR item. The plan isn't done
until the feature is Ready.

Read this when doing `/plan` work. The design knowledge itself lives in `DESIGN_GUIDES.md`; this file is the
checklist that says a plan is complete.

## A feature is Ready when…

1. **Captured in `TODO.md`** with scoped intent and an explicit acceptance condition (what "working" means
   for this feature).
2. **Design pass done for anything significant.** New generation seams, central-method changes
   (`AttackAction`, `Battle`, `DamageCalculator`), and volatile node/frontend designs have had a `/plan`
   pass. Volatile designs are marked **provisional-pending-`/plan`** until then, not implemented on a guess.
3. **Gen-variable surface named up front.** The plan states which rules/values are generation-variable
   (and therefore belong on `IBattleRules` / `ITypeChart` / `IStatCalculator`) versus gen-invariant. Apply
   the litmus: "when we build Gen 2, will this value/layout change?"
4. **Gen 1 source of truth identified.** The authoritative Gen 1 behavior is named and its source pointed
   to (`DESIGN_GUIDES.md`, the real Gen 1 mechanics) — not left to an assumed inline "gen 1" belief to be
   discovered mid-implementation.
5. **Data vs runtime boundary drawn.** The plan says whether the change is an importer change, an engine
   change, or both — and any Gen 1 data value that differs from modern is flagged as needing a pin.
6. **The quirk to test is stated.** The plan names the gen-variable behavior the tests must assert (the
   *quirk*, e.g. "damage doubled because Defense was halved" / "fails on Speed, not level"), not just the
   outcome.
7. **Dependencies unblocked.** Anything this feature depends on (another mechanic, a data import, a seam)
   is either already in place or explicitly sequenced ahead of it.

## Relationship to Done

DoR is the front gate (`/plan`); the [Definition of Done](DEFINITION_OF_DONE.md) is the back gate
(pre-finish review). A feature that was never Ready is hard to declare Done — the acceptance condition from
DoR #1 and the quirk from DoR #6 are exactly what the finish-time reviews check against.
