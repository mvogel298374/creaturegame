# PRODUCT_SPEC.md — what the game does today

> **Status: skeleton, growing incrementally from 2026-09-13.** This doc does not yet cover the whole feature
> set — it grows one entry per finished feature from here forward (see **How this doc grows**, below), not
> from a one-time backfill. Until a domain below has its own entries, treat `TODO_ARCHIVE.md` + the code as
> the record for it, same as before this doc existed.

## What this is, and what it isn't

This is the **current-state spec** — a requirement list of what the game does *right now*, as of the latest
commit. Not a changelog, not a design rationale doc. Three docs answer three different questions; keep them
that way:

| Question | Doc |
|:--|:--|
| **What does the game do today?** | **This file.** Present tense. No history, no "why," no "used to be." |
| **Why is it built this way?** | `ARCHITECTURE.md` (system-wide decisions) + the per-domain design docs (`ENCOUNTER_DESIGN.md`, `GAME_LOOP.md`, `GENERATION_SEAMS.md`, `GENERATION_PROFILE.md`, `STATE_MODEL.md`, `DESIGN_GUIDES.md`). Rationale, trade-offs, traps, the seam a mechanic lives behind. |
| **How did we get here?** | `TODO_ARCHIVE.md`. Chronological. Phases, stages, dates, what a feature looked like before it shipped, dead ends. |

A reader who wants to know *"can the player currently do X"* should never have to read a code comment or
reconstruct it from `TODO_ARCHIVE.md`'s history to find out — that's this file's whole job. A reader who then
wants to know *why* follows this file's pointer into the design doc that owns it.

**Audience:** a fresh Claude session scoping a task, the user reviewing current scope at a glance, and
(eventually, once it's complete enough) the seed for real player-facing documentation. Written so any of those
three readers can trust it without cross-checking the code.

## How this doc grows

This is **not** a manual chore layered on top of `docs/TODO.md`'s discipline — it rides the same rail. The
`docs-cleanup` gate (the mandatory step-1 of the pre-finish sequence, `.claude/agents/docs-cleanup.md`) adds or
updates this file's entry for the finished feature in the **same pass** it archives the `TODO.md` write-up, and
that edit is staged into the same finishing commit. Nobody schedules a separate "update PRODUCT_SPEC.md" task.

**Entry format** — keep every entry to this shape, no more (§1's first entry, below, is a worked example):
```
### <Feature name>
- <One-line current behavior, as a checkable fact.>
- <Another, if the feature has more than one player-visible rule.>
- Design detail / rationale → `SOME_DOC.md` §n.  History → `TODO_ARCHIVE.md` → *Feature name*.
```
No prose paragraphs, no "we decided," no dates unless the fact itself is dated (e.g. a numeric balance value
that's explicitly provisional). If you find yourself explaining *why* a rule exists, that sentence belongs in
the linked design doc instead — pull it there and leave only the pointer here.

---

## 1. Run structure & game loop

### Run difficulty
- The player picks Easy / Normal / Hard at run start (`StarterSelection`); it sets the run's XP-curve pace and
  innate bench Exp-Share, nothing else — combat challenge is unaffected.
- Normal reproduces the numbers every run used before the difficulty selector existed.
- Design detail → `GENERATION_SEAMS.md` (`RunRules` / XP curve). History → `TODO_ARCHIVE.md` → *Difficulty →
  XP bonus*.

*(rest of this domain not yet populated)*

## 2. Battle system
*(not yet populated)*

## 3. Encounters, biomes & acquisition
*(not yet populated)*

## 4. Progression (XP, leveling, evolution)
*(not yet populated)*

## 5. Economy (bag, wallet, shop, rewards)
*(not yet populated)*

## 6. Generation & presentation
*(not yet populated)*

## 7. Web / session layer
*(not yet populated)*
