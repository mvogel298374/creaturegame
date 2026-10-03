# Definition of Ready (DoR)

**The exit criteria for a `/plan` pass.** A task is not *Ready* to implement until every item below is
covered — either already established before planning, or resolved during the `/plan` discussion. `/plan`
owns this list: do not exit a plan (or hand off to `/dev`) with an unchecked DoR item. The plan isn't done
until the feature is Ready.

Read this when doing `/plan` work. The design knowledge itself lives in `DESIGN_GUIDES.md`; this file is the
checklist that says a plan is complete.

## Enforcement — no code before Ready (Gate 1)

DoR is not only `/plan`'s exit criteria; it is a **hard gate in front of every implementation**, small ones
included. Every planned item lives in `docs/TODO.md` (a row in §1 plus a detail entry) and answers all seven
items below **before** any code, data change or importer run starts. Minimal entry for an S-size fix — one line
each, all seven present: *intent + acceptance · design status · gen-variable surface · Gen 1 source · data vs
runtime · quirk to test · dependencies*.

**The assistant holds the user to this.** When asked to start, continue or greenlight an item (in any wording,
including "just do it"), the assistant's first act is to audit the entry against this checklist in its reply, item
by item (✅ / ✗). Any gap — missing, vague, or *provisional-pending-`/plan`* — means **no code**: it lists the gaps,
drafts proposed text, and asks the user to confirm or decide. A greenlight is valid only for a complete entry.
Work discovered mid-implementation gets its own entry and passes the gate before it is fixed. `docs-cleanup`
reports any finished item whose entry lacked a DoR field as a Gate-1 breach.

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
