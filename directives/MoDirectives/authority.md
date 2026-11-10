# Authority rules

This document states who decides what. `directives/Guardrails/GUARDRAILS.md` is binding for the details: which
actions each rule gates, and how that is enforced.

1. **Every commit needs its own approval.** Approval to do work is never approval to commit it, and approval
   for one commit never carries over to the next.
2. **Every item needs its own greenlight, and only a ready item can get one.** A go-ahead covers the one item
   in flight, never the rest of a list or backlog. A greenlight is valid only for an item that meets the
   Definition of Ready (`docs/DEFINITION_OF_READY.md`); an incomplete item is answered with its gaps, not
   with work. When an item is finished, report what's done and what's next, then wait.
3. **Approving a commit approves its push; a deploy is mine to call.** Push an approved commit to `origin` in
   the same step. A commit is not a release candidate by default, and you don't ask after each push. I make a
   commit a release candidate whenever I choose; only then do you tag it (`v<X.Y.Z>-rc.<N>`, the next free
   number) and push the tag, which deploys. Never tag on your own initiative.
4. **I decide on review findings that matter.** A finding that changes behaviour, scope, a test's assertion
   or a seam, or one you'd waive or defer, comes to me with its fix cost and your recommendation; I fix, waive
   or defer it. A finding that is cheap, inside the item, and changes only wording, a comment or a doc, you
   fix without asking and list in your report with one line each. When in doubt, it comes to me. No agent
   clears a finding, and no fix-and-re-review loop runs without my say.
5. **No self-waiving.** When a concrete, cheap improvement is named, by a review or by you, either do it or
   ask me to waive it. Calling it advisory, or pointing to the same gap elsewhere, is not a waiver. Judge
   whether something is testable by its cheapest form, not its most expensive.
6. **Estimate cost before large work.** For any obviously large task, such as a multi-step implementation,
   a broad audit or an expensive subagent run, give an estimated token cost when you ask for the greenlight,
   so I know the cost before I decide. If a mandated step would cost more than the change it checks, say so
   before running it.
7. **The dev stack is yours to manage.** Start, stop and restart it whenever a task needs it, without asking
   and without reporting its status unless it matters to the result.
8. **Opus is the default model.** The main session runs on Opus. Each subagent pins the model that fits its
   job; the current spread is the model table in `CLAUDE.md`, and changing it is my call. Implementation stays
   in the main session. Delegate to `opus-engineer` only for a contained job where a fresh agent is efficient:
   the brief is complete and it needn't re-read what the session already knows.
