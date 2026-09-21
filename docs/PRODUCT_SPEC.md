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

### Nicknaming on acquisition
- Every acquisition path — starter selection, themed draft, and boss catch — prompts the player to enter a
  nickname (max 10 characters, silently truncated if longer) for the incoming creature; leaving it blank or
  cancelling declines and keeps the species name, matching Gen 1's Y/N nickname prompt.
- CHECK POKEMON shows the species name alongside the nickname whenever the two differ; the battle nameplate,
  party strip, and log show the nickname only — except the one-time evolution announcement, which always names
  the newly-evolved species (e.g. "SPROUT evolved into IVYSAUR!"), never a repeated nickname.
- A nickname survives evolution; a creature still on its species-default name adopts the new species name on
  evolving as before.
- A nickname set on one creature has no effect on any other acquisition's default — every newly offered
  creature defaults to its own species name.
- Design detail / plan → `docs/TODO_ARCHIVE.md` → *Creature Naming — nickname on acquisition (session-scoped)*.
  History → same section.

*(rest of this domain not yet populated)*

## 2. Battle system

### CHECK POKEMON party-member picker
- Whenever the party has more than one member, CHECK POKEMON shows a picker row of party cards above the
  INFO/STATS/MOVES panel; selecting a card loads and displays that slot's full sheet, including a fainted or
  benched member — not just the active/lead creature.
- With exactly one party member, no picker renders and CHECK POKEMON shows that sole creature's sheet directly,
  as before this feature.
- The picker reuses the same party-card component the SWITCH menu and the lead/switch-in prompts already render.
- Design detail / plan → `docs/TODO_ARCHIVE.md` → *CHECK POKEMON party-member picker*. History → same section.

## 3. Encounters, biomes & acquisition

### Evolution-chain level floor on species selection
- A wild, Elite, Boss, or themed-draft encounter never offers an already-evolved species below the level it
  takes to reach that form — e.g. a Charizard cannot appear below level 36 (the level Charmeleon evolves at).
- A trade-evolved species (no in-run trading exists) uses this roguelite's trade-evolution stand-in level as
  its floor instead.
- A stone-evolved species (e.g. Exeggcutor) has **no** level floor from this rule — a stone can be used at any
  level in Gen 1, so a stone-evolved species can still appear at a low level.
- Design detail → `ENCOUNTER_DESIGN.md` §3.8. History → `TODO_ARCHIVE.md` → *Species selection respects each
  species' evolution-chain floor*.

## 4. Progression (XP, leveling, evolution)
*(not yet populated)*

## 5. Economy (bag, wallet, shop, rewards)

### In-battle item party-targeting
- Using a Potion/Full Restore-class healing item, a status cure, an Ether/Elixir-class PP restore, or a
  Revive/Max Revive shows a party-selection screen before the item is applied, matching Gen 1's "Use item on
  which POKÉMON?" prompt — not just the creature currently on the field.
- Revive/Max Revive only offer **fainted** party members as valid targets; the other three categories above
  only offer **living** members. Picking an ineligible member (e.g. a full-HP Potion, a status cure with
  nothing to cure) refuses with "It won't have any effect!" — no item consumed, no announce.
- Ether/Elixir on a benched member reads and restores PP against *that member's own* moveset, not the active
  creature's.
- **X-item stat boosts (X Attack etc., Guard Spec., Dire Hit) do NOT show this screen** and always apply to
  the creature currently on the field — Gen 1 has no per-party-member storage for a stat stage, so there is
  no benched target to pick.
- Design detail → `GENERATION_SEAMS.md` §5.0.2 (gen-invariance judgment, including why X-items are the
  exception), `ARCHITECTURE.md` §2.11. History → `TODO_ARCHIVE.md` → *In-Battle Item Party-Targeting — items
  other than Revive can target any living party member*.

## 6. Generation & presentation

### Level-up stat panel (Gen 1 "Kanto Sage" skin)
- Under the Gen 1 profile, the level-up stat panel (shown on a level-up, bottom-right above the battle menu,
  until the player's next input) renders as an ink-on-fill box with the same double-line frame as the battle
  log: square corners, no drop shadow, `--ks-*` palette colours only.
- The title reads `LEVEL UP!` on its own line, with the creature's `NAME · Lv N` in a dim sub-line beneath it.
- Each stat row shows the gain in bold ink (`+N`) and the new total in the dim tone; the panel's content,
  position and dismissal are the same as with no generation skin.
- With no generation profile applied, the panel keeps its original dark look.
- The NICKNAME modal (starter pick and acquisition) wears the same double-line frame under the Gen 1 profile:
  ink title, dim sub-line, text input on the fog tone with an ink border and a thickened focus ring; OK / CANCEL
  use the skinned action buttons. With no profile applied it keeps its original dark card.
- Design detail → `GENERATION_PROFILE.md` §7.3.  History → `TODO_ARCHIVE.md` → *Generation Profile 4d+ ·
  Level-up stat panel — Kanto Sage skin*.

## 7. Web / session layer

### Session resume (refresh/reopen survival)
- Refreshing, closing/reopening the tab, or reloading a bookmarked `/battle` URL during a run does not lose it:
  the client persists the active run's `gameId`/species/level/generation to `localStorage` on run start, and
  the page reattaches to the same server-side run on reload.
- The Title Screen shows a `▶ CONTINUE — {species} (Lv {level})` button whenever a persisted run exists,
  alongside NEW GAME.
- A reconnect during an active run restores full interactivity within the server's 40-second reconnect grace
  window — battle state (enemy/player sprite, HP, move list) when mid-fight, plus the Town Map overlay and the
  current biome's encounter ladder, each re-sent from whatever the server still has live.
- If the run can no longer be resumed (grace window expired, or the server no longer knows the `gameId`), the
  player is bounced to the Title Screen with a "Couldn't connect to the run — it may have expired." notice,
  instead of hanging on "Connecting…".
- A run's persisted entry is cleared when the run ends normally or the player quits — neither offers a stale
  Continue afterward.
- **Not covered:** a refresh while a between-node prompt (route choice, shop, reward, recovery, acquisition,
  lead choice, switch-in) is open, rather than during an active battle, still hangs on reconnect.
- Design detail → `ARCHITECTURE.md` §2.7 (Web session lifecycle). History → `TODO_ARCHIVE.md` → *Session Resume
  — refresh/reopen-safe `gameId` persistence*.
