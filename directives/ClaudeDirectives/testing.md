# Testing practice

Lessons Claude derived from its own failures. They serve `MoDirectives/testing.md`.

1. **Delegate whole-suite runs only, and state the scope.** Re-check a single test inline with
   `.\test.ps1 -Filter`. One green run is enough; never re-run only to confirm a pass.
2. **On an E2E failure, open the failure screenshot first.** More than once it showed the app correct and
   the spec wrong.
3. **Test the quirk, not the outcome.** Assert why the result happened ("damage is higher because Defense
   was halved"), not just that it happened ("it fainted").
4. **A hang means suspect your own new test first.** The fast suites finish in seconds. A test that runs a
   whole run needs a guaranteed ending, or it loops forever.
5. **Proving a test can fail needs a fresh build.** Restore sabotaged code with Edit, not a file copy that
   keeps the old timestamp, and build with `--no-incremental`. When the source says X and the test says
   not-X, suspect a stale build before the code.
6. **Break your own code before a review.** Test the cases your code branches around (empty collections,
   abstract types, the negated condition in your own guard), not only the happy path.
7. **A data override needs a data pin in the same change.** Behaviour tests force the roll, so they stay
   green when a re-import silently reverts the data.
