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

### Same-named creatures stay distinct in battle
- When two creatures share a display name (a wild creature vs. the player's same-species creature, a drafted
  twin on the bench, two nicknamed alike), every hit, HP change, status, faint, heal and evolution lands on the
  correct sprite and party row — a wild creature never takes the player's sprite's damage or HP, and healing or
  evolving a benched twin never moves the lead's HP bar or renames the on-field creature.
- Names are shown exactly as they are, so the same name can appear on both sides of a fight; a name is display
  text only and is never what decides which creature something applies to.
- After a page refresh mid-battle (including after a switch-in), the next turn prompt re-establishes which
  creature is on the field, so its attacks, HP and status keep landing on the correct side.
- Design detail → `ARCHITECTURE.md` §2.2 ("Creatures are identified by id").  History → `TODO_ARCHIVE.md` →
  *Creature Identity — id-keyed events*.

## 3. Encounters, biomes & acquisition

### Evolution-chain level floor on species selection
- A wild, Elite, Boss, or themed-draft encounter never offers an already-evolved species below the level it
  takes to reach that form — e.g. a Charizard cannot appear below level 36 (the level Charmeleon evolves at).
- A trade-evolved species (no in-run trading exists) uses this roguelite's trade-evolution stand-in level as
  its floor instead.
- A stone-evolved species (e.g. Exeggcutor) has a floor of level 30 (or its pre-evolution's floor, if higher) —
  real Gen 1 puts no level on a stone, but a player never fights an evolved form before it could plausibly exist.
- If a biome's themed pool has no species at or below the rolled level, the encounter uses the themed species
  with the lowest floor, never an arbitrary evolved form from the pool.
- Design detail → `ENCOUNTER_DESIGN.md` §3.8. History → `TODO_ARCHIVE.md` → *Species selection respects each
  species' evolution-chain floor*.

### Boss/Strong-tier enemy movesets are species-legal
- A Boss- or Strong-tier enemy's moveset is always drawn from moves that species could legally know (its
  level-up learnset plus any TM/HM it can legally learn) — it can never carry a move outside that pool.
- Boss and Strong compute the identical moveset for a given species; Boss's edge over Strong comes entirely
  from its higher DVs, level, and BST, not from a wider move pool.
- A Boss- or Strong-tier enemy never holds a move above its level: a level-up move is allowed only at or above the
  level that species learns it (an L10 Psyduck cannot roll Hydro Pump, learned at 52).
- A move the species learns only by TM/HM is allowed only at or above that move's fixed per-move level floor (the
  earliest level the TM is obtainable in Red/Blue); all 55 TM/HM moves carry a floor.
- Design detail → `ENCOUNTER_DESIGN.md` §3.5, `DATA_IMPORT.md` §4.1.1. History → `TODO_ARCHIVE.md` → *Boss/Strong
  "Optimal" moveset could hand a species moves it could never legally learn* and *Level-Gated Strong/Boss Movesets —
  implementation*.

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
- A cured status stays cured: a creature healed of its status (status-cure item, Full Restore, or the
  Treasure/Mystery quick-heal) does not re-enter afflicted on its next switch-in or the next battle's opening.
- **X-item stat boosts (X Attack etc., Guard Spec., Dire Hit) do NOT show this screen** and always apply to
  the creature currently on the field — Gen 1 has no per-party-member storage for a stat stage, so there is
  no benched target to pick.
- Design detail → `GENERATION_SEAMS.md` §5.0.2 (gen-invariance judgment, including why X-items are the
  exception), `ARCHITECTURE.md` §2.11, `STATE_MODEL.md` §2 (status cure clears both halves). History →
  `TODO_ARCHIVE.md` → *In-Battle Item Party-Targeting — items other than Revive can target any living party
  member*; *Repo-sweep R1 — curing a status didn't clear `CarriedStatus`*.

### TM/HM — Move-Teach Rewards
- A reward-choice card can offer a **move a party member could legally learn** (a real Gen 1 TM-learnable move
  for that species — fainted members are eligible too, matching Gen 1's own TM-use rule) instead of an item —
  shown with its type, power, accuracy and PP. No physical TM item exists; there is nothing to hold in the bag.
- The substitution chance depends on the reward's source: 5% on a Wild/Elite battle win, 20% on a Treasure/
  Mystery node, 35% on a Boss win — always falling back to a normal item reward when no party member has an
  eligible, not-already-known move. On a Boss win the move-teach card always occupies the *second* item slot
  (never the first, which is the one that can carry the Boss-only Revive) and counts as Rare for the gold bag's
  rarity scaling.
- Picking the card opens a "Teach {move} to a Pokémon?" screen listing every current party member, each marked
  ABLE or NOT ABLE up front. **This is a roguelite QoL improvement, not a literal Gen 1 reproduction** — the real
  games let you pick any party member and only tell you *afterward* if it can't learn the move; showing legality
  before selection is a deliberate deviation, not a fidelity claim. Only an ABLE member is selectable. Choosing
  one runs the same forget-a-move flow a level-up learn uses on a full moveset; declining the screen still keeps
  the reward pick itself, just skips the teach.
- Gen 1's 5 HMs are not offered — only TM-learnable moves.
- Design detail → `ENCOUNTER_DESIGN.md` §5.1. History → `TODO_ARCHIVE.md` → *TM/HM — Move-Teach Rewards*.

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
- The EVOLUTION prompt and the POKÉ CENTER (recovery) modal wear the same double-line frame under the Gen 1
  profile, with ink title, sub-line and sprite glow; their buttons use the skinned action buttons. With no
  profile applied they keep their original dark card.
- The MOVE-REPLACEMENT prompt (learning a move with a full moveset) and the GAME OVER card wear the same
  double-line frame under the Gen 1 profile, with ink title, dim sub-line and ink text. GAME OVER is ink-only
  (no red accent; the greyed faint sprite carries the beat). With no profile applied they keep their original
  dark cards, including the red game-over styling.
- The ACQUISITION offer (themed draft / boss catch), including its "release which member" party-swap picker and
  release confirmation, wears the same double-line frame under the Gen 1 profile: ink title, dim sub-line, ink
  question, no violet accent. The swap buttons are ink-bordered and invert on hover/focus; the lead (who can't be
  released) is dimmed. Type badges keep their own colours. With no profile applied it keeps its original violet
  card.
- The REWARD pick (pick-1-of-3 after a win / Treasure / Mystery) and the SHOP wear the same double-line frame
  under the Gen 1 profile: ink title and sub-line, ink-on-fill cards/rows with a grain texture, and ink-bordered
  buttons that invert on hover/focus (a disabled Buy dims). Rarity shows as a coloured tag chip carrying the
  rarity word (Common grey, Uncommon green, Rare blue, Epic purple) — the one colour exception; every other
  accent (gold bag, Quick Heal, move-teach, card/row borders) is ink. With no profile applied they keep their
  original dark gold-accented cards with rarity-coloured borders.
- The roster pickers — the LEAD choice, the forced SWITCH-IN, and the TM-teach target modal, plus the in-battle
  SWITCH menu and CHECK POKEMON's party picker — render party members as cards. Under the Gen 1 profile the modals
  wear the same double-line frame; the cards are ink-bordered with a grain texture and invert on hover/focus. The
  current lead carries an inner ink ring (with its "current"/"OUT" word), fainted members are greyed, and a member
  who can't be chosen (e.g. TM "Unable") is dimmed and does not invert. The HP bar keeps its green/yellow/red
  thresholds. With no profile applied they keep their original dark sky-accented cards.
- The ENCOUNTER overlay — the compact corner peek, the pinned full-screen Run Map (RUN MAP title, island/biome
  names, close ×, Town Map, "Encounter Path" ladder, legend) and the node ladder — wears the Gen 1 profile as a
  framed full-screen window (double-line frame, grain ground; the peek is a plain boxed card). Ladder tiles are
  ink-bordered squares with a grain texture; completed nodes are dimmed; the current node is an inverted block
  with the ◄ marker. Node kinds are told apart by glyph and label only — no red
  Boss or pink Poké Center accent. The type pills in the Town Map caption are light with an ink border; their
  icon frames keep the type colour. With no profile applied the overlay keeps its original dark deep-night map
  with gold, red and pink accents.
- Design detail → `GENERATION_PROFILE.md` §7.3.  History → `TODO_ARCHIVE.md` → *Generation Profile 4d+ ·
  Level-up stat panel — Kanto Sage skin*.

## 7. Web / session layer

### Dev Mode (dev-only — off in production)
- The server flag `DevMode:Enabled` (env `DevMode__Enabled`) gates all dev features; it defaults on in the
  Development environment and off in Production. Every dev endpoint returns 404 when it is off.
- `GET /api/dev/status` reports `{ enabled }`; the Settings panel shows a "Dev mode" toggle only when the server
  reports enabled. The choice persists per browser (default off), and a "DEV" badge shows in the HUD while on.
- With dev mode on, clicking/tapping the enemy nameplate in battle opens a read-only CHECK POKEMON sheet for the
  foe (species, level, HP, stats, DVs, Stat-Exp, status, moves + PP; no party picker, no Exp rows, no stat
  stages) via `GET /api/dev/{gameId}/enemy` (404 if no enemy is active yet).
- With dev mode on, damage ranges are shown: the combat log prints `took 37 damage (32–38)!` (an exact amount
  prints one number), each fight-menu move button has a DMG line, and every CHECK POKEMON move row (player and
  dev enemy sheets) has a DMG entry. Ranges are non-crit (a crit's actual damage can exceed the printed range)
  and measured against the current foe; fixed/level-based moves show one number, OHKO = foe's HP, Super Fang =
  half the foe's HP; status moves show none. Menu ranges come from `GET /api/dev/{gameId}/damage-ranges`;
  with dev off the server never sends them.
- Test support: with dev mode enabled server-side, a seeded start with `?forceDraft=1` makes every win offer a
  themed draft (cadence and roll skipped; the fought-pool limit still applies). Ignored when dev mode is off.
- Design detail / rationale → `TODO_ARCHIVE.md` → *Dev Mode* (no separate design doc).  History → same entry.

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
- A modal answer sent during a reconnect window is not lost: if the server rejects it, the modal re-opens; a quick
  shop BUY then LEAVE are both honoured, in order.
- **Not covered:** a refresh while a between-node prompt (route choice, shop, reward, recovery, acquisition,
  lead choice, switch-in) is open, rather than during an active battle, still hangs on reconnect.
- Design detail → `ARCHITECTURE.md` §2.7 (Web session lifecycle). History → `TODO_ARCHIVE.md` → *Session Resume
  — refresh/reopen-safe `gameId` persistence*.
