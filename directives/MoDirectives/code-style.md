# Code style rules

1. **No comments by default.** A comment is at most one short line: a non-obvious fact about that exact line,
   or a pointer to the doc that owns the reasoning.
2. **Reasoning lives in markdown.** Algorithms, formulas, tuning, protocols, decisions and rejected
   alternatives go in a design doc, in the same commit as the code.
3. **Fix the comments you touch.** Delete any comment your change makes stale. Never add comments to match
   the surrounding code.

Enforced by the Comment budget guardrail (G5). Full rule: `docs/DEV_STANDARDS.md` → Design Rationale Placement.
