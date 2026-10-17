# Battle Sim – TODO

> **Active work only.** Finished work lives in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md); design lives in the design
> docs (`ARCHITECTURE.md` §5 indexes them). **See also:** `CLAUDE.md` (doc layers, docs tree, commands) · `AI_CONTEXT.md`
> (profiles) · `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions).
>
> Rebuilt 2026-10-03: the finished Generation Profile stages, the closed tech-debt/Known-Gap records and the old
> tier narrative moved verbatim into the archive (→ *TODO.md rehaul*). Nothing here is approved work until the
> user greenlights it, one item at a time.

## How to read this file

- **Ordering** (user ruling, 2026-10-03): by **cost-to-benefit and by what an item unblocks** — cheap things and
  things that open up other items first, fewest blockers first. §1 is that ranking; §2–§8 are the same items
  grouped by area, with the detail.
- **Cost:** **S** ≈ one file / a few hours · **M** ≈ a day, a few files · **L** ≈ multi-day or needs a `/plan`.
- **Status tags:** **verified** (confirmed in code or against a primary source) · **unverified** (a reviewer's
  reading of the code, not yet reproduced — check before acting) · **designed** (a design doc exists) ·
  **needs-decision** / **needs-plan**.
- **Evidence tags** (used in the entries): **reproduced** (shown by running something) · **source-confirmed** (matched
  against a cited pokered file:line plus ours) · **read-confirmed** (code traced, nothing run; the weakest) ·
  **needs-runtime** (needs a browser, E2E or Fly run, so it is handed to the user).
- **Gen 1 facts** are checked against the primary source (pret/pokered for Gen 1, pret/pokecrystal for Gen 2), not
  against the repo's own docs or comments — both have been wrong (see §9).
- **Comment budget:** new code carries no comments beyond one short line; logic documentation goes in markdown
  (`DEV_STANDARDS.md` → *Design Rationale Placement*).
- **Gate 1 — nothing is implemented until its entry is Ready** (`directives/Guardrails/GUARDRAILS.md` → G1,
  `DEFINITION_OF_READY.md`). The **Ready?** column in §1 is the standing audit: ✅ every DoR item is answered; ⚠️
  designed but provisional or awaiting your decision; ✗ missing items (named). Each open entry also carries an inline
  **DoR (V16)** marker (audited 2026-10-17) naming its gaps with drafted text; keep it current as the entry changes.
  **A greenlight is only valid on ✅.**
  If you say "go" on a ⚠️ or ✗ row, the assistant will answer with the gap list instead of code, and ask you to
  decide or confirm the drafted text.
- **An entry must answer** (one line each for an S-size item): *intent + acceptance condition · design status ·
  gen-variable surface · Gen 1 source of truth · data vs runtime · the quirk the tests assert · dependencies*.
- **Gate 2 — nothing is done until `docs-cleanup` has run last.** A finished item is removed from this file (its
  record moves to the archive) by that agent, not by hand; the pre-commit hook blocks a commit without its stamp.

---

## 1. Now / next — ranked by cost-to-benefit and unblocking value

| # | Item | Cost | Ready? (DoR) | Why this slot / what it unblocks |
|:-:|:-----|:----:|:-------------|:---------------------------------|
| 1 | **`-StartStack` backend leak** (§7.1 Part A) | S | ✗ parked: leak not reproduced (2026-10-10); un-park condition in §7.1 | Parts B and C are done. Not actionable until the user reproduces it; §1 #3 and #6 do not wait on it. |
| 3 | **Server test gaps — `GameSessionManager` lifecycle + the `RunFaulted` path** (§6) | M | ⚠️ needs the injectable-clock design (V16 drafted `TimeProvider` and the acceptance list) | Unblocks the R1d tests and closes a long-open Tier-4 item. |
| 5 | **R1d-a — replay an open blocking prompt** (§2) | M | ⚠️ designed, but provisional-pending-`/plan`; V16: that is the only gap (a short server-side `/plan`), with no decision of its own | The only reconnect item that strands a run; highest player value. |
| 6 | **CI E2E step** (§6) | M | ✗ no design (runner, stack boot, caching) or acceptance | The only automatic E2E coverage; makes "agents don't run E2E" safe, and unblocks checking the reconnect spec. |
| 7 | **R1d-d, R1d-c** — refresh-state snapshot + sprite; second-tab policy (§2) | M each | ⚠️ provisional-pending-`/plan` (decisions made 2026-10-17) | Both extend (a)'s replay work. |
| 8 | **Gen 1 fidelity — the damage/accuracy core** (§3.1) | L | ✗ needs the Gen 1 source per claim, the seam surface, and the quirks to pin | Verify-first; large blast radius (it touches every damage number). |
| 9 | **End-of-turn residual phase** (§4.2) | L | ✗ needs a `/plan` | A `/plan`, `opus-engineer`, and both review gates; rewrites `Battle`'s turn loop. |
| 10 | **Features** — Catch, `save.db`, progressive difficulty, Generation Profile 4d+, … (§4) | L | ✗ each needs a `/plan` | Catch and stone evolutions are unblocked by the bag-scope ruling (`ARCHITECTURE.md` §2.12); `save.db` is declined for now. |

---

## 2. Reliability — reconnect (designed in [`RECONNECT_RESILIENCE.md`](RECONNECT_RESILIENCE.md), provisional-pending-`/plan`)

Verified in code 2026-10-03. Fixed and archived already: the flee stale-cache, the 60 s grace, the client replay
dedupe, lost modal answers, the `RunFaulted` notice, and the abandon-timer race (R1d-b). The server replaying on every reconnect is an **accepted
design** (it cannot tell a refresh from a blip), not a bug.

**User rulings 2026-10-17:** last tab wins and the displaced tab is told; `SessionTakenOver` and `RunMapSnapshot`
are transport notices outside the event model, like `RunFaulted`; `BattleStarted` gains `PlayerSpeciesId`.

- **R1d-a — an open blocking prompt is not replayed (strands the run).** Ten prompts park the run on a
  `SignalRInput` handshake and none is cached, so after a refresh *or a blip* the modal never reappears, the server
  stays parked, and reconnect cancels the abandon timer — the player can only QUIT. **Acceptance:** refresh or blip
  with any of the ten prompts open → the modal reappears and is answerable and the run continues; an answered
  prompt never reappears; a replayed shop shows the current balance. **Design:** the emitter caches the open
  prompt and replays it last; cleared by one `PromptAnswered` hook. **Needs-decision:** design points in the doc.
  **DoR (V16):** ✗ not Ready — gap **2** only. The doc's R1d-a section answers 3–7 (gen surface none, runtime/web
  only, quirk "replays exactly while open, never after it is answered; a shop replays the current balance",
  dependencies none). *Gap 2, drafted:* a short `/plan` ratifies the server design; the client needs nothing new
  (`expandEvent` → `SHOW_*`). It has no decision of its own.
- **R1d-c — a second tab on the same `gameId` silently takes over.** **Policy (user 2026-10-17):** last tab wins,
  with the displaced tab told via a `SessionTakenOver` notice that must **not** clear `activeGame` (it is shared
  `localStorage`). **Acceptance:** second tab → first tab shows the notice and returns to Title; the second
  continues; a plain refresh never shows the notice.
  **DoR:** ✗ not Ready — gap **2** only (the provisional client half needs its `/plan`; the policy and
  transport-notice decisions are made). 3–7 are in the doc: gen surface none; runtime/web plus a small client
  change; quirk "a plain refresh never shows the notice, and `activeGame` survives"; dependencies none.
- **R1d-d — a refresh loses the route trail, node pin, boss framing and the player's sprite.** **Acceptance:** a
  refresh at any point restores the full trail and pin, keeps boss framing, and shows the current lead's sprite.
  **Design:** an absolute `RunMapSnapshot` sent last and applied as an ordered step (not an immediate action —
  the client's queue lags arrival), plus `PlayerSpeciesId` on `BattleStarted` and a reducer-held
  `playerSpeciesId` (also fixes the post-evolution sprite, §5).
  **DoR:** ✗ not Ready — gaps **2** (provisional-pending-`/plan`; the transport-notice and `PlayerSpeciesId`
  decisions are made, 2026-10-17) and **7** (sequenced after R1d-a, which shares `ReplayLastKnownState`). 3–6 are in the doc: gen surface none;
  runtime/web plus client; quirk "a blip after three biomes leaves `routePath` unchanged, a refresh restores all
  three".
- **Test plan** (in the doc): C# unit tests, Vitest, and a new opt-in `reconnect.spec.ts` driven by a Dev-Mode-gated
  `DropConnection` hub method. It also closes three open verification gaps: replay-after-blip has never been
  checked in a browser; the `useBattleHub` wiring of `replayDedupe` is untested (only the pure helper is); the 60 s
  grace rests on SignalR's documented 0/2/10/30 s schedule, not a measured run.
  **DoR (V16):** part of R1d-a/c/d, not a separate item.
- **`RunFaulted` server-half test gap** — nothing pins `SendRunFaulted` or the `GameSessionManager` catch-all call
  (the client handler's mapping is covered by R1d-c's `transportNotices` extraction).
  **DoR (V16):** ✗ not Ready — gaps **1, 7**. *Drafted:* fold into §6 "`GameSessionManager` connection lifecycle"
  (same class, same harness). Acceptance: a run whose loop throws reaches the connection as `RunFaulted`, and the
  catch-all calls `SendRunFaulted` once. Gen surface none; Gen 1 source n/a; runtime/web; no quirk; depends on that
  entry's test harness.

---

## 3. Correctness and Gen 1 fidelity

All items below are **unverified** (a reviewer's reading) unless marked; the Gen 1 value in each must be checked
against pret/pokered before the engine or data changes (`GEN_DIFFERENCES.md` is a secondary source). Fixing a
data/engine item usually also means updating the test that pins the wrong value.

### 3.1 Engine

- **Damage/accuracy core (L):** `DamageCalculator.cs:246-247` skips the `2L/5` and `/50` floors `GEN_DIFFERENCES.md`
  documents (~24% high at L12); `Gen1BattleRules.cs:215` uses a 3-based accuracy/evasion table (Gen 1 uses the
  standard table); crit multiplier should be ≈(2L+5)/(L+5), not a flat 2× (`Gen1BattleRules.cs` crit block, ~`:287`); multi-hit continues past a broken
  Substitute (`AttackAction.ResolveDamage`'s multi-hit loop); recoil/drain/Struggle use full calculated damage, not
  HP actually lost (`AttackAction.ExecuteAsync` Struggle recoil, `ResolveDamage` drain); Ice can freeze Ice /
  Electric can paralyse Electric (`Gen1BattleRules.CanReceiveStatus`); Fly/Dig are not untargetable on the charge turn (not listed as a
  known gap); `DamageCalculator` applies no stat cap after the stage multiplier (Gen 1 reportedly clamps a staged
  stat to 1–999) and no ÷4 scaling of both stats when either exceeds 255 (found by `requirements-review`
  2026-10-14, during the stat-stage seam item; unverified against pokered).
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 3, 4, 6** (line cites refreshed 2026-10-17). *Drafted:* first a verify
  slice that gives each claim a pokered cite, then one entry per confirmed claim. Each entry gets: acceptance, a
  test pinning pokered's number; its seam member (most are already `IBattleRules`/`DamageCalculator` inputs);
  `opus-engineer` plus both gates, since `DamageCalculator`/`AttackAction` are central. Data vs runtime: runtime.
  Dependencies: none.
- **Turn-order/status edges — verified 2026-10-12 (§7.6 V8); all four claims hold.** Verdicts and pokered cites:
  `TODO_ARCHIVE.md` → "§7.6 V8 — turn-order/status edges". The fixes are the two entries below.
- **Gen 1 status-check order in `CanAct` (from §7.6 V8 — claims 2–4) — M, ✗ not Ready.** *Intent:* `CanAct` runs
  pokered's order, owns the recharge check, and full paralysis or a confusion self-hit cancels
  Bide/Thrash/charge/trap. Sleep, freeze, flinch and trap infliction clear a pending recharge. *Acceptance:* each
  reproduction in the V8 archive record flips (one lost turn, counters tick in Gen 1 order, the lock-in is cleared with no Thrash
  confusion). *Gaps:* a design pass (does the order become an `IBattleRules` seam? Gen 2's order is unchecked,
  verify at pokecrystal; moving recharge out of `AttackAction` touches a central method, so `opus-engineer` + both
  review gates). *Gen 1 source:* `core.asm:3328-3464`, `:1181-1191`, `effects.asm:36-40`/`:249`/`:992`/`:1091` (full cites in the V8 archive record). *Data vs runtime:* runtime only. *Quirk to assert:* Thrash
  cancelled by full paralysis leaves the user unconfused. *Dependencies:* none.
  **DoR (V16):** ✗ not Ready — gaps **2** (design pass, central) and **3** (seam or not, settled in that pass; check
  Gen 2's order at pokecrystal). 1 and 4–7 are answered.
- **A replacement frees the trapped foe (from §7.6 V8 — claim 1) — S, ✗ not Ready.** *Intent:* when the player's
  binder leaves the field, the foe's `BindingTurnsRemaining` clears. *Acceptance:* a `Battle`-level test where the
  player's binder faints mid-bind and the switch-in's first turn lets the foe act. *Gaps:* touches `Battle`
  (central, outside a V-slice fix); does the seam question above cover it? *Gen 1 source:* `core.asm:1744-1749`,
  `:5711-5717`. *Data vs runtime:* runtime. *Quirk to assert:* the bind ends on the send-out, not on its counter.
  *Dependencies:* none.
  **DoR (V16):** ✗ not Ready — gaps **2** (touches `Battle`: `opus-engineer` plus both gates, or fold into the
  `CanAct` design pass above) and **3**. *Gap 3, drafted:* gen-invariant if Gen 2 also frees the bind on send-out;
  verify at pokecrystal before calling it.
- **Smaller Gen 1 gaps I — Psywave max, binding odds, Stat Exp fixed 2026-10-12 (§7.6 V9).** Verdicts and fixes:
  `TODO_ARCHIVE.md` → "§7.6 V9 — smaller Gen 1 gaps I". The confusion self-hit claim stays open as the entry below.
- **Gen 1 confusion self-hit damage (from §7.6 V9) — S, ✗ not Ready.** *Intent:* no variance; Attack and Defense
  with stages and Burn applied; the foe's Reflect doubles the Defense. *Gaps:* `DamageCalculator` is central;
  seam surface (variance on/off, which side's screen).
  *Gen 1 source:* `core.asm:3695-3696`, `:3674-3683`, `:4044-4055` (full cites in the V9 archive record). *Quirk to assert:* the foe's Reflect weakens your self-hit.
  **User ruling 2026-10-17:** replicate the foe-Reflect quirk, behind a seam.
  **DoR:** ✗ not Ready — gap **2** (central `DamageCalculator`: a design pass, then `opus-engineer` plus both
  gates); 1, 3 and 5 are drafted for your confirmation, 7 is resolved. *Drafted:* acceptance, a forced-roll test
  pins the self-hit with no variance roll, staged Attack/Defense and Burn applied, and the foe's Reflect doubling
  the Defense. Seam: an `IBattleRules` member for variance and for which side's screen applies (Gen 2 read at
  pokecrystal in the design pass). Runtime only. Dependencies: none.
  **Remaining steps:** (1) you confirm the drafted lines; (2) a design pass names the seam members and reads Gen 2,
  shown to you; (3) then it is Ready for `opus-engineer` plus both review gates.
- **Foe Psywave can deal 0 (from §7.6 V9) — S, ✗ not Ready.** Gen 1 rolls the player's Psywave in `[1, b)` and
  the foe's in `[0, b)`, b = L + floor(L/2) (`core.asm:4657-4670`, `:4776-4789`); ours rolls `[1, b)` for both.
  **User ruling 2026-10-17:** replicate it behind a seam (it stayed open from 2026-10-12).
  **DoR:** ✗ not Ready — gap **2** (the seam shape: `PsywaveDamageRange`/`RollPsywaveDamage` take only the source
  creature, so they need the attacker's side; the call site is `AttackAction`, central, so `pr-review` applies).
  *Drafted:* acceptance, a forced 0 roll on the foe's Psywave deals 0 and leaves the player's HP unchanged, and the
  player's minimum stays 1; the damage-range preview (`DamageCalculator`) shows the foe's 0. Seam: the existing
  Psywave members, given the side; Gen 2 read at pokecrystal. Gen 1 source: cited above. Runtime only. Quirk: only
  the foe can roll 0. Dependencies: none.
  **Remaining steps:** (1) you confirm the drafted lines; (2) a short design note fixes how the seam learns the
  attacker's side, shown to you; (3) then it is Ready (S, `pr-review` applies).
- **Smaller Gen 1 gaps II — verified, trainer XP / Transform crit speed / Substitute-at-cost fixed 2026-10-12 (§7.6 V10).**
  Verdicts, F1–F3 and the review rulings: `TODO_ARCHIVE.md` → "§7.6 V10 — smaller Gen 1 gaps II". The remainder stays
  open as the entries below.
- **Gen 1 Mist, thaw and Leech Seed fidelity (from §7.6 V10) — M, ✗ not Ready.** *Intent:* Mist only blocks
  primary stat-down moves, as a miss before the accuracy roll; a Substitute stops a Fire thaw; the Leech Seed drain
  is multiplied by the Toxic counter. *Gaps:* touches `AttackAction`/`Battle` (central, `opus-engineer` + both
  gates); Leech Seed overlaps the end-of-turn residual `/plan` (§4.2); the `StatDropBlocked` wire event would lose
  its Gen 1 use (TS client leg). *Gen 1 source:* Mist `core.asm:5266-5307`; Leech Seed `core.asm:546-591`; thaw
  `effects.asm:194-204` (full cites in the V10 archive record).
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 3, 5, 6, 7**. *Drafted:* split in two.
  - **Mist + thaw**, no dependency. Acceptance: Mist turns a primary stat-down move into a miss before the accuracy
    roll and leaves a secondary stat drop alone; a Fire hit on a substituted frozen foe doesn't thaw it. Quirk: Mist
    doesn't stop Aurora Beam's secondary drop.
  - **Leech Seed × Toxic counter**, after §4.2. Quirk: the drain grows with the Toxic counter.

  Both are `IBattleRules` members, runtime only, `opus-engineer` plus both gates.
- **Gen 1 XP participant split order (from §7.6 V10) — S, ✗ not Ready.** Gen 1 divides the base exp and the Stat
  Exp yields by the participant count first (`experience.asm:294`). *Gaps:* touches `Battle`, and it's a seam question
  (`SplitXpAmongParticipants`'s contract changes; composition order is in `GENERATION_SEAMS.md`).
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 3, 5, 6, 7**. *Drafted:*
  - **Acceptance:** with two participants, each gets pokered's XP and Stat Exp for a species whose yield is odd.
  - **Design and seam:** the seam's split runs before the trainer and level factors, so `SplitXpAmongParticipants`
    moves ahead in the composition; it touches `Battle`, so both gates apply.
  - **Data vs runtime:** runtime only.
  - **Quirk:** dividing first loses the remainder that dividing last would keep.
  - **Dependencies:** none.
- **Smaller Gen 1 gaps III (new, unverified beyond the cite):** the foe's stat-down moves fail 25% of the time in a
  non-link battle (`effects.asm:549-554`), a Gen 1 AI handicap.
  **User ruling 2026-10-17:** replicate it behind an `IBattleRules` flag.
  **DoR:** ✗ not Ready — confirm the drafted lines below and it is ✅ Ready (the fix re-reads the cite before
  coding). The same claim was filed again in §3.2 ("AI stat-down 25% fail") and is merged here. *Drafted:*
  - **Acceptance:** a forced roll through the rules seam makes the foe's Growl fail one roll in four; the player's
    never does.
  - **Design:** it lives in the stat-down effect, which isn't a central method.
  - **Seam:** an `IBattleRules` flag (Gen 2's behaviour unverified).
  - **Data vs runtime:** runtime only.
  - **Quirk:** only the enemy side rolls.
  - **Dependencies:** none.
  - **Remaining steps:** you confirm the drafted lines; then it is Ready, with no design pass.
- **Substitute blocks sleep/paralysis status moves; Gen 1 lets them through (source-confirmed 2026-10-17, §7.6
  V15) — S–M, ✗ not Ready.** pokered (master): `SleepEffect` (`engine/battle/effects.asm:26-77`) and
  `ParalyzeEffect_` (`engine/battle/move_effects/paralyze.asm`, reached from `effects.asm:1165`) never call
  `CheckTargetSubstitute` (`effects.asm:1436`). `PoisonEffect` (`:87`), `FreezeBurnParalyzeEffect` (`:197`),
  `StatModifierDownEffect` (`:556`), `FlinchSideEffect` (`:972`) and `ConfusionEffect` (`:1121`) do, so
  Supersonic/Confuse Ray and stat-lowering moves stay blocked, as ours already are. `MoveHitTest`'s substitute
  branch (`engine/battle/core.asm:5251-5258`) was meant only for drain/Dream Eater and is bugged so it never fires.
  Ours blocks every status condition behind a substitute (`AttackAction.cs:676-678`), and
  `SubstituteContractTests.ShieldsTheUserFromTheFoesStatus` pins Thunder Wave as blocked. *Gaps:* touches
  `AttackAction` (central) and needs a seam (Gen 2+ blocks all status moves), e.g. an `IBattleRules` member saying
  which pure status moves a substitute stops; the pinned test flips. *Acceptance:* in a full battle, Sleep Powder,
  Hypnosis, Thunder Wave, Stun Spore and Glare land on a substituted foe; Toxic/Poison Powder, Confuse Ray and a
  damaging move's secondary status stay blocked; a Gen 2-style profile blocks all of them.
  **DoR (V16):** ✗ not Ready — gaps **2** (central `AttackAction`: `opus-engineer` plus both gates) and **3** (seam
  member to design), plus **5, 6, 7** (drafted). *Drafted:* runtime only; quirk "Thunder Wave lands through a substitute but Toxic
  doesn't"; dependencies none.
- **Gen 1 Disable countdown: the lock can end early compared with pokered (source-confirmed 2026-10-17, §7.6 V15) —
  M, ✗ not Ready.** pokered decrements the counter in the disabled mon's own pre-move check
  (`engine/battle/core.asm` `CheckPlayerStatusConditions:3328` → `.AnyMoveDisabledCheck:3394`; the enemy mirror is
  `CheckEnemyStatusConditions:5677`). The sleep, freeze, trapped, flinch and recharge checks before it
  (`:3355-3384`) jump past it, and a turn spent switching or using an item never reaches it. Ours ticks both
  creatures' counters at the end of every turn (`StatusResolver.TickTurnCounters`, `Battle.cs:223-224`), including
  the turn Disable lands when its user moved second. The 1–8 roll itself is fixed (V15). *Gaps:* the countdown moves
  into the pre-move status check, which touches `Battle`/`StatusResolver` and the faint-skip handling (central), and
  needs a seam (Gen 2+ timing differs); the binding-trap countdown that shares `TickTurnCounters` must be checked
  against pokered in the same pass. *Acceptance:* a full-battle test with seeded rolls pins the observable lock
  length against pokered: a roll of N keeps the move locked for N of the disabled creature's own move attempts, and
  sleep, flinch, recharge, switch and item turns don't count down.
  **DoR (V16):** ✗ not Ready — gaps **2** (central), **3** (seam member to design), **5** (drafted) and **7** (the
  binding-trap check). *Drafted:* runtime only; best paired with the `CanAct` status-order design pass above, since both move
  logic into the pre-move check.
- **Roar/Whirlwind: the scared-off side still acts (source-confirmed 2026-10-17, V16) — S–M, ✗ not Ready.** Our
  action loop has no flee check (`Battle.cs:183-209`; the flee gate only runs after both actions and the residuals,
  `:316-319`), so the scared-off foe still acts, which can end the run via a faint with a healthy bench
  (`GAME_LOOP.md:135`). pokered's `MainInBattleLoop` returns the moment `wEscapedFromBattle` is set
  (`core.asm:419-421`, `:430-432`, `:443-445`, `:457-459`): no second action, no residual. A second gap, found in
  V16: in a wild battle, pokered's `SwitchAndTeleportEffect` (`effects.asm:814-838`) makes the move fail when the user
  is lower-level and `rand[0, userLv + targetLv] < targetLv / 4`. Ours always succeeds (`MoveEffects.cs:520-548`).
  **User ruling 2026-10-17:** fix both, with a proper generation seam.
  **DoR:** ✗ not Ready — gap **2** (design pass, central `Battle`); 1, 5 and 6 are drafted below for your
  confirmation. 3, 4 and 7 are answered.
  - **Acceptance:** a full battle where a faster Roar ends the turn with no foe action and no residual tick, and a
    forced roll makes a lower-level Roar fail.
  - **Design:** touches `Battle` (central), so `opus-engineer` plus both gates; `GAME_LOOP.md:135` is updated.
  - **Seam (ruled):** both behaviours sit behind `IBattleRules`. The design pass names the members (whether a flee
    ends the turn at once; the flee success roll) and reads Gen 2's values at pokecrystal.
  - **Data vs runtime:** runtime only.
  - **Quirk:** the slower foe never acts after being scared off.
  - **Dependencies:** none (the go-ahead is given).
- **Seam leak / AI / validation — verified and fixed 2026-10-14 (§7.6 V11).** AI weights, `MoveTurnChoice` trust and the
  `Biome.cs` crash fixed; verdicts, F1–F4 and the review rulings: `TODO_ARCHIVE.md` → "§7.6 V11 — seam leak, AI weights,
  move validation, map connectivity". Its stat-stage seam leak shipped 2026-10-14 (`TODO_ARCHIVE.md` → "Stat-stage choice
  behind the seam").

### 3.2 Data and importer

- **Item descriptions (found 2026-10-11, V7; unverified):** PokeAPI's modern text as-is (only X Accuracy's is
  overridden), so Dire Hit / Guard Spec and the X-items describe modern effects. Check before a descriptions pass.
  (V7 record: `TODO_ARCHIVE.md` → "§7.6 V7".)
  **DoR (V16):** ✗ not Ready — gaps **1, 3, 4, 5, 6, 7**. *Drafted:*
  - **Acceptance:** every battle item's description states its Gen 1 effect; an importer test pins the Dire Hit,
    Guard Spec and X-item texts.
  - **Seam:** descriptions become per-generation item content (`items.db` is per-gen).
  - **Gen 1 source:** the Gen 1 games show no item descriptions, so the text describes pokered's item effect
    (`engine/items/item_effects.asm`).
  - **Data vs runtime:** data, via a layer-2 override in the importer.
  - **Quirk:** Dire Hit's text must not promise a raised crit rate (Gen 1's Focus Energy bug).
  - **Dependencies:** none.
- **Found 2026-10-11 during V7 / the item seam (unverified beyond the citation; each needs its own DoR):**
  - *AI stat-down 25% fail:* duplicate of §3.1 "Smaller Gen 1 gaps III", which holds the audit (V16).
  - *A failed OHKO says "missed", Gen 1–2 say "<target> is unaffected!" (found 2026-10-11, second review of "Gen 1
    roll order", archived; source-confirmed, predates it):* the OHKO effect sets `wCriticalHitOrOHKO` to `$ff` and
    `PrintMoveFailureText` then prints `UnaffectedText` (pokered `engine/battle/core.asm:3783`); Gen 2 the same
    (pokecrystal `effect_commands.asm:2211-2214`). We emit `MoveMissed`. Needs a new event or message on the wire
    (C# + TS), so its own DoR.
    **DoR (V16):** ✗ not Ready — gaps **1, 2, 3, 5, 6, 7**. *Drafted:*
    - **Acceptance:** a failed OHKO (the target is faster or higher-level) emits an "unaffected" event, the client
      prints "<target> is unaffected!", and a real miss still says "missed".
    - **Design:** S, but it touches `AttackAction`'s OHKO branch (central), so `pr-review` applies.
    - **Seam:** which outcome a failed OHKO reports (Gen 1–2 agree; Gen 3+ unchecked).
    - **Data vs runtime:** runtime plus the wire (C# and TS, field guard).
    - **Quirk:** a Gen 1 failed OHKO is not a miss.
    - **Dependencies:** none.
  - *Struggle skips the accuracy roll* (found during "Gen 1 roll order", archived): `AttackAction.Connects`
    treats Struggle as never-miss; in pokered it goes through `MoveHitTest` like any move (so the 1/256 miss applies).
    Unverified beyond that reading; needs its own DoR. **Fixing it must flip
    `RollOrderContractTests.StruggleRollsCritAndDamageWithNoAccuracyDrawMirroringTheKnownGap`.**
  **DoR (V16), Struggle:** ✗ not Ready — gaps **1, 2, 3, 4, 5, 7**, left as filed per the ruling below. When
  reopened, the draft is:
  - **Acceptance:** Struggle rolls accuracy, so a forced 255 roll misses.
  - **Design:** central `AttackAction`, so `opus-engineer` plus both gates.
  - **Seam:** `IBattleRules` (Gen 2's Struggle is unchecked).
  - **Gen 1 source:** cite `MoveHitTest` (`core.asm:5228`).
  - **Data vs runtime:** runtime only.
  - **Dependencies:** none.

  User ruling 2026-10-11: leave this one as filed; the other two became entries and shipped (`TODO_ARCHIVE.md` → "Battle randomness fully seeded; Dire Hit / Guard Spec narration"). The
  roll-order work that surfaced the last two: `TODO_ARCHIVE.md` → "Gen 1 roll order in `AttackAction`".
- **Importer hardening follow-ups (the §1 #4 item shipped #4a–#4e 2026-10-11; record in `TODO_ARCHIVE.md` → "§1 #4
  Importer hardening — step #4e"). Two candidates remain, neither DoR'd:**
  - **Empty-but-parseable generation list (found in #4a review):** an empty generation list (moves/items) still
    reports clean. **User ruling 2026-10-17:** an empty list counts as a failure (S).
    **DoR:** ✅ Ready (drafts confirmed by the user 2026-10-17).
    - **Acceptance:** a test feeds an empty generation list (moves, then items) and the run reports that stage failed.
    - **Design:** none needed (importer, no central method).
    - **Gen surface:** none.
    - **Gen 1 source:** n/a (tooling).
    - **Data vs runtime:** importer only, no `*.db` change.
    - **Quirk:** an empty but parseable list fails the stage instead of reporting clean.
    - **Dependencies:** none.
  - **Species row and its learnset as one unit (S, found in #4d review; user asked to file it 2026-10-11):**
    `FetchPokemonDataByUrl` saves the species row on its own, then `ImportLearnset` swaps the learnset in a separate
    transaction, so a failed learnset leaves the new stats beside the old learnset until a re-run
    (`DATA_IMPORT.md` §4.6; the species is reported failed). Likely fix: open the transaction in
    `FetchPokemonDataByUrl` around both saves. Needs its mini-DoR (acceptance: a failed learnset leaves the species
    row unchanged too; test: the #4d trigger test also asserts the old stats).
    **DoR:** ✅ Ready (drafts confirmed by the user 2026-10-17):
    - **Gen surface:** none (transaction scope).
    - **Gen 1 source:** n/a.
    - **Data vs runtime:** importer only, no `*.db` change.
    - **Dependencies:** none.

    1, 2 and 6 are answered above (S, importer, no central method).
- **Lickitung is wild in Yellow but never appears wild (S; from V6's `requirements-review` 2026-10-11).** The
  availability type is never shown; its only reader is the wild pool (`EncounterFactory.cs:311`, `:473`, any
  version's `Wild` row). Lickitung has `Trade` in all three versions, so it can't appear as a wild enemy. **User
  ruling 2026-10-17:** fix Lickitung only; the other Yellow corrections and the evolve-only rule have no in-play
  effect and are settled in §9.
  - **Acceptance:** `GameAvailabilitySeeder.TypeOverrides` sets Yellow Lickitung `Trade` → `Wild`; a
    `BuildGen1Rows()` test pins it; a full importer re-run changes only that row in `pokemon.db` (diffed vs `HEAD`).
  - **Design:** none (one seeder override). **Gen surface:** none (Gen 1 data). **Gen 1 source:** pokeyellow
    `data/wild/maps/CeruleanCaveB1F.asm`; `data/events/trades.asm:18` trades it *away* for Dugtrio. **Data vs
    runtime:** data (seeder + re-import). **Quirk:** Lickitung is wild in Yellow, a trade in Red/Blue.
    **Dependencies:** none. *Risk:* a larger wild pool can shift seeded-run tests; any that move are re-pinned.
  - **DoR:** ✅ Ready.
- **Dev server reads a `-wal`-stale bin copy (S; from V6).** **User ruling 2026-10-17:** every data change is a
  full importer re-run (`MoDirectives/data.md` rule 1), so the mechanism is option (a), the importer's checkpoint
  on exit; no other writer is supported.
  - **DoR:** ✗ not Ready — confirm the restated acceptance and it is ✅ Ready. *Acceptance (restated):* after an
    importer run no root `*.db-wal` holds unflushed pages, and the next build's bin copy matches the root (a
    scripted repro: import, build, compare). This retires `ClaudeDirectives/data.md` rule 2 (flush SQLite MCP
    writes), a layer-3 change proposed in the same commit.
  - *Scope + acceptance:* a write sitting in a root `-wal` reaches the dev server after a build. Acceptance: write a
    row via a WAL connection without checkpointing, build, and the bin copy shows the row (scripted repro, then a
    test if the chosen mechanism is code).
  - *Options:* (a) the importer runs `PRAGMA wal_checkpoint(TRUNCATE)` before exit — covers the importer only, not
    MCP writes; (b) the Web csproj also copies `*.db-wal` — covers both, but `PreserveNewest` keys on each file's own
    mtime and a stale bin `-wal` must not outlive a fresh `.db`; (c) switch the committed `.db` files to
    `journal_mode=DELETE` — no `-wal` ever exists, at the cost of SQLite's WAL concurrency, which a read-only game
    host does not use. **Recommendation: (c), plus (a) as a belt** — it removes the failure mode instead of copying it.
  - *Gen-variable surface:* none. *Gen 1 source:* n/a (tooling). *Data vs runtime:* data-file setting / build;
    no runtime code. *Quirk the test asserts:* the bin copy matches the root after an uncheckpointed write.
    *Dependencies:* none.
- **Item `Cost` is modern, and drives rarity + the starting bag (S–M; from V6).** **User ruling 2026-10-17:**
  behind a generation seam — option (a), Gen 1 prices as per-generation data, plus the `ThenBy(Id)` tie-break.
  - **DoR:** ✗ not Ready — gap **2**: the `RarityOf` bands were tuned on modern prices, so the design step drafts
    the new bands (and whether the bands go per-generation too) and shows them to you before any code.
  - *Scope + acceptance:* either (a) import Gen 1 prices (a layer-2 price table from `pokered
    data/items/prices.asm`; `items.db` re-imported; `RarityOf` bands re-checked against the new numbers — X-items
    become Common, Full Heal Uncommon, PP items 0 → Common), or (b) keep modern prices and only make the starting
    bag deterministic (tie-break `OrderBy(Cost).ThenBy(Id)`, so Antidote + Burn Heal by id). Either way
    `DATA_IMPORT.md:344` is rewritten to say `Cost` drives rarity and the starting bag. Acceptance (a): an
    `items.db` contract test pins the Gen 1 prices and a `RarityOf` test pins the new bands; (b): a test pins the
    starting-bag pair independent of row order.
  - *Recommendation: (b) now, (a) only with a balance pass* — the rarity bands were tuned against the modern
    numbers, and shop prices are already rarity-derived (`ShopCalculator`), so Gen 1 prices buy fidelity in a
    number the player never sees while shifting loot odds.
  - *Gen-variable surface:* (a) makes `Cost` per-generation data (fine — `items.db` is per-gen content); the
    rarity bands are gen-invariant `RunRules`-style tuning. *Gen 1 source:* `pokered data/items/prices.asm`.
    *Data vs runtime:* (a) data + a band check; (b) runtime one-liner. *Quirk:* PP items cost 0 in Gen 1 (not sold).
    *Dependencies:* none.

---

## 4. Features and roadmap

Everything kept; nothing waived.

### 4.1 Item acquisition · bag persistence · catch

Item acquisition itself is done (the Run Economy). **Bag scope is decided: per-run** (`ARCHITECTURE.md` §2.12) — the
bag, wallet and party reset when a run ends; a roguelite meta-unlock layer is the eventual direction but unplanned.
Open: the two pieces below.
Poké Balls are imported data only (`ItemEffects.For(Ball)` returns null ⇒ `ItemUseFailed`; the frontend hides them
via `bag.ts`), and `CatchRate` is imported and now Gen 1-correct. "Catch" is likely a misnomer: treat it as one
channel of a broader acquisition layer.

- [ ] **Catch / Poké Ball effect** *(needs-plan; not gated on persistence — a caught creature joins the in-memory
  `Party` and is lost with the run).* `BallItemEffect : IItemEffect` for `ItemCategory.Ball`, registered in
  `ItemEffects.All`, with a "catching" state/outcome in `Battle`. Gen 1 formula:
  `floor((MaxHP × 3 − HP × 2) × CatchRate / (MaxHP × 3))` vs. a 0–255 roll (per-ball modifier lives in the formula,
  not the `Item` row). Event `CaptureAttempted(string TargetName, int TargetId, bool Caught)` (carries the creature
  id — `WebEventContractTests` requires it); a `BattleEnded` variant `reason: "Caught"`. A caught creature goes into
  the existing `Party`. Phaser throw/shake/catch animation.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 3, 4, 6**.
  - **4:** the one-line formula above looks like a simplification. pokered's `ItemUseBall`
    (`engine/items/item_effects.asm`) rolls against the ball's range, subtracts a status bonus, compares the catch
    rate, then the HP factor. Verify and cite it before the `/plan`.
  - **3:** the capture formula is gen-variable (Gen 2 changed it), so it needs a seam, a rules member or an
    `ICatchRules`.
  - **Answered:** 5 (both: imported `CatchRate` plus runtime) and 7 (bag scope decided).
  - **Drafted acceptance:** in a seeded wild battle a ball at 1 HP with the matching roll catches, the creature
    joins `Party`, and the battle ends `"Caught"`; trainer-tier foes refuse the ball.
- [ ] **Stone evolutions** — the `Stone` trigger and `IEvolutionRules.StoneUsed` are built and dormant, waiting only
  on stones becoming obtainable as items (per-run bag; no persistence needed). (A flat `StoneEvolutionLevel` of 30 floors stone-evolved species in wild/draft selection.)
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 4, 5, 6, 7**. 3 is answered: `EvolutionContext.StoneUsed` and
  `Gen1EvolutionRules` already exist (`Gen1EvolutionRules.cs:71`, tested). *Drafted:*
  - **Acceptance:** using a Fire Stone from the bag on Vulpix evolves it, and a mismatched stone does nothing.
  - **Design and dependency:** a design call on how stones enter the run (shop, drop or reward), which the
    acquisition layer owns.
  - **Data vs runtime:** whether stones are in `items.db` (importer allowlist) plus runtime.
  - **Gen 1 source:** pokered's stone item effect (`item_effects.asm`).
  - **Quirk:** a stone works outside a level threshold.

### 4.2 Run loop and progression

- [ ] **`PlayerSave`/`SavedCreature` in `save.db`** — **DECLINED for the development phase (user, 2026-10-03):
  no server-restart-surviving saves** (`ARCHITECTURE.md` §2.12); kept here only as the parked design. Auto-save after
  each battle, party-management UI; the heavier persistence beyond the lightweight Session Resume (survives a
  server restart/redeploy, not just a client refresh). When revisited it is two tiers — a mid-run snapshot and a
  cross-run profile for the roguelite meta unlocks (unplanned). **Fly deploys must stay single-machine until session state is externalised here:**
  `GameSessionManager` is in-process, so a second machine 404s any REST call routed to the machine that never saw
  `/start`; the workflow pins `flyctl deploy --ha=false` — don't remove it or raise `min_machines_running`
  (`ARCHITECTURE.md` §2.7).
  *DoR (V16): declined by user ruling, so it is not auditable until revisited.*
- [ ] **Progressive difficulty** beyond `targetBst = lead BST + depth × 10`; trainer encounters at milestones.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 4, 6, 7**; needs a `/plan`. *Drafted:*
  - **Seam:** gen-invariant `RunRules` tuning; trainer rosters per region would be profile content.
  - **Data vs runtime:** runtime.
  - **Dependencies:** none (the per-area floor was settled 2026-10-17, §9).
- [ ] **Gen 1 has no end-of-turn residual phase** *(needs-plan, L)*. Verified against pret/pokered
  `engine/battle/core.asm` (`MainInBattleLoop`, 2026-09-20): each creature ticks its own poison/burn/Leech Seed right
  after its own action — A acts → A's residual → if A fainted, A's faint handler (B never acts) → else B acts → B's
  residual. `Battle.cs` (~179-231) instead runs both actions, then `TickTurnCounters`, then both residuals. Three
  discrepancies: (a) the slower creature acts even when the faster would have died to its own tick; (b) the faster
  creature's tick lands after the slower one's attack, not before; (c) a residual-caused double faint is impossible
  in Gen 1 but our engine can produce it. `RunDirectorTests.Runner_DoubleFaintFromEndOfTurnPoison_EndsTheRun_
  ButStillCountsTheWin` pins a path Gen 1 cannot produce (the mutual-KO ruling stands for genuine trades like
  Explosion). The fix belongs behind an `IBattleRules` seam beside `FaintEndsTurnImmediately`; design rationale in
  `GEN_DIFFERENCES.md` → Status Quirks and `GENERATION_SEAMS.md` §2. Needs a `/plan`, `opus-engineer`, both gates.
  **User ruling 2026-10-17:** its own item, behind a generation seam; "slightly modernized but functionally the
  same" is acceptable — the Gen 1 order of effects and its outcomes are the requirement.
  **DoR:** ✗ not Ready — gap **2** (the `/plan`); 1, 5 and 6 are drafted below for your confirmation, 7 is
  resolved. 3 (seam beside `FaintEndsTurnImmediately`) and 4 (`MainInBattleLoop`, `core.asm:413-462`) are
  answered. *Drafted:*
  - **Acceptance:** (a)–(c) each flip in a full-battle test, and the `RunDirectorTests` double-faint test is
    re-pinned to a path Gen 1 can produce.
  - **Data vs runtime:** runtime only.
  - **Quirk:** the faster creature's own poison tick lands before the slower one acts.
  - **Dependencies:** none. The Leech Seed half of §3.1 "Mist, thaw and Leech Seed" waits on this.
- [ ] **Enemy encounter pool ignores game version** — filter by `PokemonGameAvailability` once a version selector
  exists.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 3, 5, 6, 7**; gap 7 blocks the rest (no version selector exists).
  *Drafted for when it does:*
  - **Acceptance:** with Blue selected, no Red-exclusive species appears.
  - **Data vs runtime:** runtime over existing availability data.
  - **Seam:** versions are profile content.
- [ ] **Enemy Pokémon do not evolve** — wire into level-up when the loop wants it.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 4, 6, 7**. It's a product call ("when the loop wants it") with no
  acceptance. 3 is answered: `IEvolutionRules` is the seam. *Drafted:* acceptance, an enemy that levels past its
  evolution level between encounters appears evolved; runtime only.

### 4.3 Generation Profile — presentation catalog (Stage 4d+) and the falsification rule

Design: [`GENERATION_PROFILE.md`](GENERATION_PROFILE.md). Stages 1–3 and 4a/4b/4c (generation channel, the Kanto
Sage skin, the grid Town Map) are done and archived. The standing 2026-08-04 commitment keeps this ahead of Tiers
2–3 in the user's own ordering; cost-to-benefit places the cheap items above it in §1.

- [ ] **4d+ — the surface catalog, jointly iterated** (each surface its own greenlit mini-plan): move select, battle
  HUD, CHECK POKEMON (the main overview), BAG (a full skin beyond the 2026-08-23 legibility fix), the `PartyStrip`
  chips (`.party-chip*`), Title/StarterSelection (including the generation picker). The battle command menu is
  settled (2×2 grid, verbs fixed). Already skinned: the encounter overlay, every run-prompt modal, the SWITCH menu
  cards, CHECK POKEMON's party picker, and the level-up panel.
  - **Level-up panel follow-ups.** *Flagged, not decided — a domain claim for `requirements-review`:* whether Gen 1's
    real level-up box lists HP at all (recollection: four rows, ATTACK/DEFENSE/SPEED/SPECIAL) and whether it shows
    gains first, then totals on a keypress; today's panel shows five rows with gain + total together. Also:
    `.battle-screen` and `.battle-log` still carry their own copies of the double-frame recipe that
    `components/modals/ModalFrame.css` now shares. `level-up.spec.ts` is opt-in — recommend `.\e2e.ps1 -Spec level-up`.
    **DoR (V16):** ✗ not Ready — gap **4** first: verify pokered's level-up stats box (rows, gains vs totals) before
    any change. Then 1 and 6 follow from it. Client-only; gen surface is the profile's theme.
  **DoR (V16), 4d+ catalog:** ✗ not Ready by design — gaps **1, 2** per surface. Each surface gets its own
  mini-plan with you.
- [ ] **Stage 5 — the falsification harness** is a standing requirement, not a final stage: each stage ships its
  leg of `TestAltProfile`, because upward compatibility is unfalsifiable with one profile.
  *DoR (V16): a standing requirement on other items, not an item.*
- Phaser/canvas and per-gen sprite/cry assets stay deferred (`GENERATION_PROFILE.md` §7.6). *DoR (V16): a deferral,
  not an item.*

### 4.4 Multi-Generation data model and schema (deferred to a Gen 2 sprint)

The stat-selection abstraction is done. Open:

**DoR (V16), whole subsection:** ✗ not Ready. Each entry below waits on a Gen 2 sprint `/plan` (gaps **1, 2, 6, 7**
each). Their seam and data-vs-runtime lines are already written in the text. One exception is worth splitting out
now, below.

- [ ] **`Attributes` Special split:** `Special` → `SpAtk` + `SpDef` (keep `Special` as a Gen 1 computed alias);
  `Creature.BaseSpecial`/`DvSpecial`/`ExpSpecial` split in parallel, and `StatStages`/`StageStat` gain SpAtk/SpDef
  stages (`IBattleRules.OffensiveStage`/`DefensiveStage` already route the damage stage, so a Gen 2 rules set
  returns them with no `DamageCalculator` change). In the same pass, replace `StatStageMoveEvaluator.StageOf`
  (`MoveEvaluators.cs:129-137`, a copy of `StatStages.Of`) with `Stages.Of(stat)` so the AI can't drift from the
  engine when the new stages land (`pr-review` advisory, 2026-10-14).
  **DoR, the `StageOf` half:** ✅ Ready (S; drafts confirmed by the user 2026-10-17); ships independent of Gen 2.
  `MoveEvaluators.cs:129` still holds the copy.
  - **Acceptance:** `StatStageMoveEvaluator` reads `Stages.Of`, the copy is deleted, and the AI tests are unchanged
    and green.
  - **Gen surface:** none.
  - **Data vs runtime:** runtime.
  - **Quirk:** none.
  - **Dependencies:** none.

  Confirm it and it gets its own ✅ entry.
- [ ] **`PokemonSpecies` per-generation schema:** separate timeless identity (`Id`, `Name`, `PokedexEntry`,
  `GrowthRate`) from a new `PokemonSpeciesGenData` table (`SpeciesId`, `Generation`, types, base stats, base
  experience, catch rate; Gen 3+ adds abilities). The importer stores one row per species per generation; the engine
  queries by active generation. Base stats resolve from PokeAPI's `past_stats`; base experience and catch rate have
  no history and come from curated per-generation tables (`DATA_IMPORT.md` §4.2).
- [ ] **Move per-generation data:** resolve a field for generation *G* as the earliest `past_values` entry whose
  version-group generation is **> G**, else the current value ("earliest = Gen 1" is the G=1 case). One `Attack` row
  per `(moveId, generation)` or resolve on demand; the layer-2 override table per-generation too. Mechanic
  differences stay on the seams, never in per-gen move data.
- [ ] **Generation filtering:** `Attack.GenerationIntroduced` + `PokemonSpecies.GenerationIntroduced` columns (set on
  import), then replace `Gen1ContentScope`'s identity pass-through with
  `all.Where(x => x.GenerationIntroduced <= (int)profile.Generation)` (`<=`, not `==`). The runtime socket
  (`GenerationProfile.ContentScope`) already routes every catalog read; no new call sites.
- [ ] **Per-generation ITEM data** — the third catalog, with no schema at all. PokeAPI gives no generation signal for
  items (`DATA_IMPORT.md` §4.5), so a second generation needs an `Item.GenerationIntroduced` column, a
  per-generation allowlist, and the `Gen1ContentScope.Items` identity replaced by the same `<=` filter. Gameplay
  numbers follow the moves' layer-2 pattern; an item re-added without its `ApplyGen1Gameplay`-equivalent override
  imports with zero amounts (silently broken). Max Revive returns here as data; pinned meanwhile by
  `ItemImportTests.Gen1BattleItemNames_ExcludesMaxRevive_TheItemsCatalogIsOneGenerationsContent`.
- [ ] **Importer Gen-1-isms to generation-scope** (inventory from a grep of `PokeApiConnector`, 2026-10-03; fine while
  Gen 1 is the only generation; scope each via `GenerationImportScope` when a second one is planned):
  - *Species:* `PokemonImport.Gen1TypeSlots` + its `PreGen6`/`GenOrder` tables (types = earliest pre-Gen-6
    `past_types` entry); `SingleSpecialAsOf`'s `generation == 1` branch is intentional (the Special split is a model
    change); the generation-list DTO is named `Gen1Response` though both fetchers use it for any generation.
  - *Moves:* `MoveImport` — `BuildGen1Attack`, `ApplyGen1Corrections`, the `Gen1MoveEffects` map, and
    **`Gen1PhysicalTypes`/`Gen1DamageCategory` (the Gen 1 type-based physical/special split — a rule living in the
    importer, which Gen 2+ changes per move)**; `MoveMinLevels`.
  - *Evolutions:* `EvolutionImport` (`Gen1`, `MaxGen1SpeciesId`) and `EvolutionMapper` (`MaxGen1SpeciesId`,
    `Gen1Stones`, `ExtractGen1Edges`).
  - *Items:* `ItemImport.ImportGen1BattleItemsAsync`, `ItemMapper.Gen1BattleItemNames`, `ApplyGen1Gameplay`.
  - *Availability and assets:* `GameAvailabilitySeeder` (`SeedGen1Async`, the 1–151 loop, the Red/Blue/Yellow tables);
    `SpriteDownloader`/`CryDownloader` (`LastId = 151`).
  - *Entry point:* `Program.cs` passes `1` to both fetchers and calls the `Gen1`-named stages directly.

### 4.5 Presentation polish

- [ ] **Move-specific attack animations (grouped, not per-move).** Today every move plays one generic lunge + a
  type-neutral white tint + `playDamageShake`. Map each move to one of ≈5–7 families keyed off data we already
  have (`DamageType`, `AttackType`) plus a few special cases: physical contact (the current lunge), projectile/ranged
  special, status/self-buff, two-turn/charge, multi-hit/flurry; plus tinting the flash by the move's type colour.
  **Plumbing:** `MoveUsed` carries only `(AttackerName, AttackerId, MoveName)`, so project `DamageType` + `AttackType`
  onto it and its `SignalRBattleEventEmitter` mapping (the recurring web-event field-projection gap — add the field
  guard), then a pure `moveAnimationFamily(type, category, slug)` map, per-family `BridgeCommand`s and `BattleScene`
  handlers each still emitting `animationComplete`. Worth doing in the same pass: read the hit-flash tint and the log's
  "super effective" emphasis from **one** shared type-colour source (`TypeBadge`'s palette). Design notes:
  `SPRITE_PRESENTATION.md` §3.3–3.5.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 6, 7**; a volatile frontend design, so it needs a `/plan`. *Drafted:*
  - **Seam:** the family keys off per-generation move data (`DamageType`/`AttackType`), and the colours are theme.
  - **Data vs runtime:** wire (C# and TS) plus client.
  - **Acceptance:** each family plays its own animation and still emits `animationComplete`; a Vitest test pins the
    `moveAnimationFamily` map.
- [ ] **Text feel — typewriter log scroll + menu blips.** The battle log renders ~15–20 ms/char; a cursor-move blip
  and a confirm blip on menu navigation. Pure client polish, no wire change; pairs well with the "Kanto Sage"
  pass as one "this is Gen 1 now" moment.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 4, 6, 7**. *Drafted:*
  - **Acceptance:** log lines type out at the set rate and a key press completes the line; menu moves and confirms
    blip.
  - **Gen 1 source:** the games' text speed setting and sound IDs (pokered `audio/`) for fidelity.
  - **Data vs runtime:** client only, plus blip assets.
  - **Dependencies:** none, though skipping the typewriter must not stall the timeline.
- [ ] **Sprite presentation FX — joint mini-plan not yet scheduled.** `SPRITE_PRESENTATION.md` §3 sketches eight
  unratified ideas. Recommendation: §3.1 (confirm `pixelArt: true`) is a one-line check, do it standalone; §3.4
  (type-coloured particles) folds into the animations item above (same wire dependency); §3.2 (a grounding shadow)
  is the strongest candidate for a joint sketch → ratify session.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 6**; needs the joint sketch session, then a DoR per ratified idea. §3.1's
  one-line check could go first as its own S item.
- [ ] *(small)* **Escape = B-cancel on the prompts that have a negative answer** — evolution→CANCEL,
  acquisition→DECLINE, shop→LEAVE, move-replacement→don't-learn. The four required choices (`RouteChoice`,
  `RewardChoice`, `LeadChoice`, `SwitchIn`) stay blocking. Give those four `dismiss={{ onEscape: … }}`
  (the wrapper already supports it). Needs Vitest coverage and a `requirements-review` pass on the B-cancel claim.
  **DoR (V16):** ✗ not Ready — gaps **1, 3, 4, 5, 6, 7** (all drafted except 4, which needs a pokered cite). "Those four" means the four negative-answer prompts, not the four
  required ones. *Drafted:*
  - **Acceptance:** Escape on each of the four sends its negative answer once, and Escape on a required prompt
    does nothing.
  - **Gen 1 source:** cite where pokered treats B as CANCEL at the evolution and learn-move prompts.
  - **Quirk:** B cancels an evolution in Gen 1.
  - **Gen surface:** the key map is theme, so none.
  - **Data vs runtime:** client.
  - **Dependencies:** none.

  Vitest covers the key mapping, since it's pure logic.

### 4.6 Dev and test tooling features

- [ ] `ConsoleInput : IBattleInput` — a numbered move menu for terminal play (low priority).
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 7**. Which host runs it? The engine is a class library with no entry
  point. Gen surface none.
- [ ] *(small)* **Stat stages in the dev enemy overview** — `PlayerOverviewDto` carries none; add them to the DTO (or
  a dev-only extension) and render in `CreatureOverview` `enemy` mode.
  **DoR (V16):** ✗ not Ready — gaps **1, 3, 5, 6, 7**, all drafted here; confirm them and it is ✅ Ready. 2 is
  answered: no design call.
  - **Acceptance:** with Dev Mode on, the enemy overview lists each non-zero stage, and with it off the DTO
    carries none.
  - **Gen surface:** none (stages are gen-invariant here).
  - **Data vs runtime:** web DTO plus client.
  - **Quirk:** none.
  - **Dependencies:** none.
- [ ] **Dev Mode — next features (shell, not ready: needs a `/plan` and a full DoR entry).** More dev actions behind
  the same `DevController` gate (404 when the flag is off; `forceDraft` is the existing test-support precedent).
  Candidates, unranked: force the first enemy's species/encounter (removes the seed walks behind the slow
  `voluntary-switch`/`poke-center` specs); set level or force an evolution; force a battle-win drop; grant gold/items;
  skip to node. **Acceptance / design / gen surface / Gen 1 source / data-vs-runtime / quirk / dependencies:** to be
  filled in. Current behaviour → `PRODUCT_SPEC.md` → *Dev Mode*.
  **DoR (V16):** ✗ not Ready — gaps **1–7** (a shell). Pick one candidate first. Forcing the first enemy's species
  would speed up the two slowest specs.
- [ ] **Agent profiles → project skills (M, ✗ not Ready — user 2026-10-13; needs a full `/plan`, user
  2026-10-17).** *Intent:* turn `/plan`, `/dev`,
  `/sync` and `/test` from prose in `.claude/AI_CONTEXT.md` into Claude Code skills under `.claude/skills/<name>/`,
  invocable by name and loading their knowledge base (DoR, design guides, dev standards, the seam checklist) on
  use. *Acceptance:* each profile is a skill that loads when invoked; `AI_CONTEXT.md` keeps only the tooling
  reference; the `CLAUDE.md` *Profiles* section points at the skills. *Design:* ✗ open: one skill per profile or
  merged; whether `/test` overlaps `test-runner`; whether `/sync` survives. *Gen surface:* none. *Gen 1 source:*
  n/a (tooling). *Data vs runtime:* neither. *Quirk to assert:* invoking a skill loads its knowledge base, checked
  by invoking each once. *Dependencies:* none.
  **DoR:** ✗ not Ready — gap **2**: a full `/plan` answers the three design questions. Starting recommendation:
  one skill each for `/plan` and `/dev`; fold `/test` into `test-runner`'s doc; retire `/sync` unless a schema pass
  is planned.
- [ ] `data-testid` attributes — deferred; specs lean on stable semantic classes. Add only where a class proves
  brittle.
- [ ] §8 visual-regression canvas snapshots — skipped for maintenance cost.

*DoR (V16): the two lines above are deferred or skipped by decision, not items.*

### 4.7 User documentation

- [ ] `/help` route or modal — starter selection, battle controls, status icons, level picker.
- [ ] Expand `README.md` — architecture decisions (two-DB model, `IBattleRules` pattern, how to add a move effect or a
  generation).
- [ ] Adapt `GEN_DIFFERENCES.md` into a player-facing "what makes Gen 1 different" explainer.

**DoR (V16), all three:** ✗ not Ready — gaps **1, 2** (scope and acceptance; `/help` is a frontend design). Gen
surface: `/help` and the explainer describe the active generation, so they are profile content. 4–7 are n/a or none.

---

## 5. UX, accessibility and robustness (all unverified unless noted)

- **Modal accessibility:** `Modal.tsx:35-44` has `aria-modal` but no focus trap, initial focus or inert background —
  Tab still reaches QUIT (abandons the run, no confirmation); the battle log has no `role="log"`/`aria-live`.
  **Verified in a browser 2026-10-03** (route-choice modal over the pinned map): Tab leaves the modal after the
  offered towns — focus goes to `<body>`, then the MAP toggle, the settings gear, then the pinned map *behind* the
  modal (its close button, then its towns). Not checked: what Enter does on those towns (probably inert).
  **DoR (V16):** ✗ not Ready — gaps **1, 3, 5, 6, 7**, all drafted here. Confirm them and choose the test route,
  then it is ✅ Ready (S–M; client, no central method).
  - **Acceptance:** with a modal open, Tab and Shift+Tab cycle only inside it, the first control gets focus on
    open, the background is `inert`, and the battle log has `role="log"` with `aria-live="polite"`.
  - **Test route:** an E2E spec (no DOM harness by rule), so it needs your run.
  - **Gen surface:** none.
  - **Data vs runtime:** client.
  - **Quirk:** Tab from the last control wraps to the first, never to QUIT.
  - **Dependencies:** none.
- **Double-submit and `/battle` "Connecting…" forever — fixed 2026-10-17 (§7.6 V12).** Verdicts, F1–F2 and the
  rulings: `TODO_ARCHIVE.md` → "§7.6 V12 — frontend double-submit, /battle dead end, battle log".
- **Battle log unbounded — reproduced 2026-10-17 (§7.6 V12); S, ✅ Ready (cap *N* = 1000, user 2026-10-17).** *Intent:*
  the log keeps only the last *N* lines. *Evidence:* `battleReducer.ts:276` appends and only `initialState`
  (`:203`) sets `log`, so it never resets, not even per encounter; a scratch Vitest kept 5,000 of 5,000 lines.
  *Acceptance:* a reducer test pins that the log holds at most *N* lines and drops the oldest first. *Gaps:* *N*
  (suggested 200); 10 E2E specs read `.battle-log` text (`helpers.ts`, `battle.spec.ts`, `status.spec.ts`, …), so
  each must be checked for a wait on a line older than *N*. *Gen surface:* none. *Gen 1 source:* n/a. *Data vs
  runtime:* client runtime only. *Quirk to assert:* the oldest line goes first. *Dependencies:* none. The companion
  claim, that the whole `BattleScreen` re-renders on every dispatch, is **read-confirmed** (the reducer lives in its
  hook and the log lines aren't memoized); its cost in a long run is **needs-runtime** (unmeasured).
  **DoR:** ✅ Ready — *N* = 1000 (user ruling 2026-10-17). At 1000 no E2E spec plausibly waits on an older line;
  the fix reads the 10 specs to confirm that, and recommends `.\e2e.ps1 -Spec battle` if any doubt remains.
- **Phaser resize and entry pacing — fixed 2026-10-17 (§7.6 V13).** Verdicts and fixes FA–FB:
  `TODO_ARCHIVE.md` → "§7.6 V13 — Phaser resize/entry pacing and frontend nits".
- **Transform's scale pulse may revert a resize (found 2026-10-17 during V13; unverified, S).** `transformSprite`'s
  yoyo tween returns to the scale captured at its start (`BattleScene.ts` `morph`), so a resize mid-pulse would
  restore the old scale. Read only; needs its own mini-DoR and a reproduction.
  **DoR (V16):** ✗ not Ready — gaps **1, 3, 5, 6, 7**; reproduce first. *Drafted:*
  - **Acceptance:** a resize during the Transform pulse leaves the sprite at the new layout's scale (Puppeteer:
    trigger Transform, resize mid-tween, measure).
  - **Gen surface:** none.
  - **Data vs runtime:** client.
  - **Quirk:** the pulse ends at the current scale, not the captured one.
  - **Dependencies:** none.
- **Player species is taken from the starter** (`BattleScreen.tsx:77,237,400`, `activeGame.ts`) — game-over shows the
  starter's sprite with the final creature's name after an evolution or lead change; Continue also shows the
  starter's name/level. Designed in R1d-d (§2). *DoR (V16): covered by R1d-d's audit.*
- **Frontend nits — verified and fixed 2026-10-17 (§7.6 V13).** Verdicts (incl. the partly refuted unknown-reward
  claim) and fixes FC–FH: `TODO_ARCHIVE.md` → "§7.6 V13 — Phaser resize/entry pacing and frontend nits".
- **Type-badge contrast (reproduced 2026-10-17, §7.6 V13) — S.** *Intent:* every `TypeBadge` meets WCAG AA (4.5:1)
  for its label. *Evidence:* computed ratios: 13 of 18 below 4.5:1, 7 at or under 2.5:1 (Ice 1.85, Rock 1.99, Fire
  2.07, Bug 2.13, Fairy 2.18, Flying 2.38, Grass 2.39). **User ruling 2026-10-17:** darken the failing background
  colours, keeping white text and each type's hue; every `typeColor()` consumer (map glyphs, chips) darkens with
  them. *Acceptance:* a Vitest test computes white on every `TYPE_COLORS` entry and asserts ≥4.5:1. *Gen surface:*
  none. *Gen 1 source:* n/a. *Data vs runtime:* client styling. *Quirk to assert:* none. *Dependencies:* none.
  **DoR:** ✅ Ready.

---

## 6. Test and CI infrastructure

**Standing guidance** (not tasks):
- *A seed is not determinism.* A seed fixes the server's RNG stream, but the client's move sequence draws from it, so
  a spec that races (polling, `waitFor` timeouts, a swallowed click) plays out a different run under load. A seeded
  spec is deterministic only if its driving loop is **paced** — settle each turn before the next input — or it is
  written not to care, or it bypasses a gated offer under Dev Mode (`forceDraft`). See `e2e/README.md`.
- *Vitest owns pure decision logic; Playwright owns anything needing the full stack or the DOM.* Do not add a second
  DOM harness (`jsdom`/RTL) to re-assert what E2E already renders.
- *E2E is user-only for the agent* (2026-07-26): the suite is ~4 min for 38 tests and the only one with real flakes,
  so the `test-runner` gate runs `.\test.ps1 -Dotnet -Web`, reports that E2E did not run, and may only *recommend*
  the narrowest `.\e2e.ps1 -Spec <file>`.

**Open:**
- [ ] **CI E2E step** (M) that boots backend + frontend, runs headless, tears down. Now the only automatic E2E
  coverage there is, and so more load-bearing than when it was written: the local gate never runs E2E and
  `test.ps1` skips it when the stack is down, so nothing catches a red suite until someone asks for a run.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 6, 7**. *Drafted:*
  - **Acceptance:** a workflow on a non-deploying trigger boots the stack, runs `.\e2e.ps1` headless, uploads the
    report, and tears down; one green run is recorded.
  - **Design:** runner OS, how the stack boots (`dev.ps1` or published binaries), Playwright browser caching, and
    whether it blocks the deploy.
  - **Dependencies:** share setup with §7.1 "Run the fast suites before deploying"; design both together.
  - **Gen surface:** none.
  - **Data vs runtime:** CI only.
- [ ] **`GameSessionManager` connection lifecycle** (M) — abandon grace, pending-session eviction TTL and the
  run-loop `Task.Run` are covered by neither suite (entangled with `IHubContext` + `Task.Run` + wall-clock timers;
  needs an injectable clock). Also untested: `SetItemChoice`'s unknown-id fallback (`:457-464`), `DetachConnection`'s
  stale-connection guard (`:679-680`) — reachable without SignalR, and the paths behind §2.
  (`ActiveBattle.ScheduleAbandon`/`OnGraceElapsed`/`CancelAbandon`, `:765-796`, are now covered by `AbandonTimerTests`.)
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 6**. Cites re-checked 2026-10-17: the fallback is at `:457-462` and the
  stale guard at `:679-680`. *Drafted:*
  - **Design:** an injectable clock seam (`TimeProvider`, built into .NET 8+) threaded into the grace and eviction
    timers.
  - **Acceptance:** tests drive each of these with a fake clock, with no real waits:
    - a pending session evicted at the TTL;
    - a disconnected run abandoned after the grace;
    - an unknown item id advancing the turn;
    - a stale connection's detach ignored;
    - the `RunFaulted` server half (folded in from §2).
  - **Quirk:** a reconnect inside the grace cancels the abandon.
  - **Gen surface:** none.
  - **Data vs runtime:** runtime/web.
  - **Dependencies:** none.
- [ ] *(small)* **`voluntary-switch.spec.ts` is still slow (~3 min standalone).** Its seed walk forces a themed draft
  via `forceDraft` yet still burns several seeds before reaching a switchable turn; investigate why seeds 1–3 fail
  even with the draft forced (e.g. battle-one losses) or give the walk a faster-fail path.
  **DoR (V16):** ✗ not Ready — gaps **1, 7** (investigate first). *Drafted:* acceptance, the spec passes standalone
  in under 60 s on your run. A likely dependency is §4.6 Dev Mode "force the first enemy". Gen surface none; test
  only.
- [ ] *(low)* **`evolution.spec.ts` doesn't pin the nameplate-follows-evolution fix** — its ALLOW case reads the
  nameplate only after the *next* `BattleStarted`, so it would pass without the fix. Add an assertion right after the
  morph. Not verified by a Playwright run (E2E is user-only).
  **DoR (V16):** ✗ not Ready — gap **1** (drafted below), plus your go-ahead on `.\e2e.ps1 -Spec evolution`.
  Acceptance (drafted):
  the ALLOW case asserts the evolved name in the nameplate before the next `BattleStarted`. Gen surface none; test
  only; no dependencies.
- **Flake: process-wide `SqliteConnection.ClearAllPools()` in test `Dispose` (S, found 2026-10-11).** The pre-commit
  run for importer #4a failed twice in web tests (`EncounterFactoryMinLevelTests`,
  `EncounterFactoryGenerationProfileTests`) with `SQLite Error 5: unable to delete/modify user-function due to active
  statements`, thrown while a pooled connection was returned — four following runs passed. Likely cause: four test
  classes clear **every** SQLite pool from `Dispose` (`ItemsDbServiceTests.cs:185`, `StatusConditionIntegrationTests.cs:130`,
  `MigrationTests.cs:340`, `LearnsetIntegrationTests.cs:244`) while xUnit runs other classes in parallel. The new
  `TempImportDatabases` avoids it with `Pooling=False` instead; the same switch is the likely fix for those four.
  **Not Ready:** needs its mini-DoR (acceptance: no `ClearAllPools` outside a non-parallel collection; test: N
  consecutive suite runs green) and a reproduction to confirm the cause.
  **DoR (V16):** ✗ not Ready — gap **1** (a reproduction confirming the cause; the four `ClearAllPools` sites are
  still there). *Drafted:* N = 10 runs of `.\test.ps1 -Dotnet` green. Gen surface none; Gen 1 source n/a; test only;
  no dependencies.
  *DoR (V16), flake watch below: a watch, not an item.*
- **Flake watch:** `endless-chain.spec.ts` *"a run ends when the player faints"* failed once in a full 2026-07-26 run
  (no `Run over` line after 1m10s) but passes in 7.3 s alone — consistent with the documented "a long run
  accumulates abandoned server-side runs" degradation, not a code defect.
- [ ] *(small)* **Tighten `encounter-map.spec.ts` and `cadence.spec.ts`** (S, from §7.6 V14, ✗ not Ready).
  `encounter-map.spec.ts:94` ignores `playToNextEncounter`'s returned log, so a loss surfaces later as a confusing
  ladder failure (read-confirmed). `cadence.spec.ts`'s `usedWhileFull` accepts any "used X!" line while the enemy
  is at full HP — the enemy acting first, a status move or a miss pass it, and the run is unseeded (read-confirmed).
  *Drafted mini-DoR:* acceptance — encounter-map asserts the returned log shows a win before the ladder step;
  cadence seeds its run and requires the player's own damaging move followed by an enemy HP drop. Gen-variable
  surface: none. Test: the user runs `.\e2e.ps1 -Spec encounter-map` and `.\e2e.ps1 -Spec cadence` (E2E is
  user-only). **Gap:** the user's go-ahead on the E2E runs; cadence needs a seed that reaches a hit on turn one.
  **DoR (V16):** ✗ not Ready — confirmed as above (your E2E go-ahead and the cadence seed).
- [ ] *(small)* **Deduplicate the test helpers** (M, from §7.6 V14, ✗ not Ready). 10 `Fighter(...)` helpers with
  3 signatures in `Integration/Flow` and the seeded-start preamble in three E2E files (`helpers.startBattle`,
  `encounter-map.spec.ts` `gotoOpeningRouteChoice`, `reward-drop.spec.ts` `startSeededRun`) (read-confirmed).
  *Drafted mini-DoR:* acceptance — one `Fighter` builder in `TestSupport/` covering the three signatures, one
  seeded-start helper in `e2e/helpers.ts`; no behaviour change. Gen-variable surface: none. Test: the fast suites,
  plus the user's run of the three affected specs. **Gap:** the user's go-ahead on the E2E runs.
  **DoR (V16):** ✗ not Ready — confirmed as above. The C# `Fighter` half needs no E2E and could split off as its
  own ✅ item.

---

## 7. Infrastructure, tech debt and hygiene

### 7.1 Infra, CI/deploy and scripts (unverified)

- **Run the fast suites before deploying (M, low priority — user ruling 2026-10-11).** *Verified (§7.6 V2):*
  `fly-deploy.yml` goes checkout → setup-flyctl → `flyctl deploy`, and it is the repo's only workflow, so no suite
  ever runs on GitHub; an RC tag on a red commit deploys anyway, and the local pre-commit hook is the only net.
  **Not Ready:** needs a design pass for which suites (the `test-runner` gate set: .NET + `tsc` + Vitest), the CI
  .NET (SDK 9.0.200 pin) and Node setup, `needs:` ordering ahead of the deploy job, and how it shares setup with
  §1 #6 (CI E2E step). Acceptance and test approach to be written then; the result is only observable on an RC
  deploy or a non-deploying trigger added for the purpose.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 6, 7**, as the entry says. *Drafted:* acceptance, a red commit's RC tag
  fails before `flyctl deploy` runs (proven on a throwaway tag or a `workflow_dispatch` dry run). Design this
  together with §6 "CI E2E step". Gen surface none; CI only.
- **Dev-script and pre-commit-hook gaps — §1 #1, only Part A remains (parked).** B and C are done (see
  `TODO_ARCHIVE.md`). Tooling only: no `/plan` needed; gen-variable surface none; no importer, engine or `*.db` change;
  no dependencies. Verification is **manual reproduction** (nothing in xUnit covers these scripts); no Pester/shell
  harness (user ruling 2026-10-10).
  - **A — `-StartStack` backend leak** (`test.ps1:139,166-169`, `e2e.ps1:183,358-360`). *Not reproduced
    (2026-10-10) — parked, not Ready.* The reviewer's reading was that `Start-Process dotnet run` returns the wrapper
    and the `finally` kills only that PID. Starting the backend exactly as the scripts do and killing it with
    `Stop-Process` on the returned PID released `:5100` and left no `creaturegame.Web` process. Only the start/kill
    mechanism was tested; no `-StartStack` E2E run (user-only). **To un-park:** the user runs `.\e2e.ps1 -Spec <small>
    -StartStack` (also on a failing run and on Ctrl-C) and `Get-NetTCPConnection -LocalPort 5100` still shows a
    listener afterward. Then re-draft the acceptance condition against that observed case.
    **DoR (V16):** ✗ parked — gap **1** until your reproduction. 2–7 are answered above.
- **Pre-commit hook — deferred from the item above (user ruling 2026-10-10; each needs its own entry before work):**
  the hook checks the working tree, not the staged snapshot (`:14,22`; fix needs `git stash --keep-index` or a temp
  worktree, **M**, riskier); hardcoded dotnet path (`:11`; portability only).
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 6** for each.
  - **Staged-snapshot (drafted):** acceptance, a commit whose staged file differs from the working copy is checked
    on the staged content.
  - **Dotnet path (drafted):** acceptance, the hook finds the SDK from `global.json` / `PATH` and falls back to the
    user folder.
  - **Both:** tooling only, verified by manual reproduction.
- **Abuse protection for the public endpoints (M, low priority — user ruling 2026-10-11; shelved, user
  2026-10-17: not needed now, the numbers below stay as the starting point when reopened).** *Verified (§7.6 V3,
  `TODO_ARCHIVE.md`):* no rate limit on `POST /api/game/start`, no cap on concurrent runs or connections, no Fly
  concurrency block, and a run in an open idle tab never ends (unclaimed runs are evicted after 2 min, disconnected
  ones after the 60 s grace). **Not Ready — needs your numbers and one UX call.** Suggested starting
  points, to confirm or change: about **10 starts per minute per visitor** (needs `UseForwardedHeaders` reading
  Fly's client IP, since the limit is per visitor); **200 live runs** in total (pending + active), with `/start`
  answering 503 above it; a Fly `[http_service.concurrency]` block at **soft 200 / hard 250** connections; and
  ending a connected run after **30 minutes with no input**, which needs the player-facing notice designed
  (§2's `RunFaulted`-style transport notice is the likely shape). Acceptance and tests to be written once the
  numbers are set.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 6** wait on your numbers and the idle-notice UX call. Gen surface none;
  runtime/web plus `fly.toml`; depends on `UseForwardedHeaders` (§9).

### 7.2 Dependencies

`npm audit` 15 (1 critical `vitest` 2.1.0 — dev-only, not exposed by `vitest run`; 6 high incl. `vite`/`esbuild`/
`@playwright/test`; the only one shipped to players is `react-router-dom` 6.30.3, moderate) — `npm audit fix` clears
most. `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 High (GHSA-2m69-gcr7-jv3q) via EF Core Sqlite 9.0.6: low practical risk,
invisible in builds.

**DoR (V16):** ✗ not Ready — gaps **1, 7** (6 is n/a: no quirk); the counts above date from the original sweep, so re-run `npm audit`
first. *Drafted:*
- **Acceptance:** `npm audit` shows no critical or high advisory in a shipped dependency, the fast suites are green,
  and you run E2E (the `vite`/`@playwright/test` bumps can break it).
- **Gen surface:** none.
- **Data vs runtime:** dependencies only.
- **Dependencies:** your E2E go-ahead.

### 7.3 Watch items — do not refactor speculatively

*DoR (V16): watch items are triggers, not greenlightable items, so they are not audited. The exception is the dead
code line, marked below.*

- [ ] `Creature.FullHeal` and `ReviveItemEffect` duplicate `ClearStatus`'s reset instead of routing through it
  (moved from §3.2, V16); route them through it the next time either is touched.

- [ ] `AttackAction` still has large methods after the roll-order rewrite (`ExecuteAsync` ~190 lines, `ResolveDamage`
  ~115, `TryApplyStatEffect` ~65; the old `ResolvePreDamageGates` is gone, split into `TryRollBeforeHitTest`,
  `RollStrike`, `Connects` and friends); central and well-tested — revisit only if a change makes it hurt.
- [ ] `Console.WriteLine` is the web layer's whole logging strategy (no `ILogger<T>`); do it as a mechanical pass the
  first time a real Fly.io debugging need bites.
- [ ] `Battle`'s constructor is at 15 parameters (recounted 2026-10-17); apply the `RunDirectorOptions` precedent (a `BattleOptions` record
  for the optional tail) the next time a feature has to touch the signature anyway.
- [ ] `EncounterFactory`'s profile threading is growing the same way (`BuildCreature` is at 8 parameters;
  `profile` + `allMoves` + `rng` travel together). The next time a feature widens these signatures, consider a small
  run-scoped parameter object that keeps the threading explicit and required.
- [ ] `BiomeDefinition.MapX`/`MapY` are now vestigial (the procedural layout replaced them); removing them means
  updating `BiomeTests.cs`'s pinning test. Low priority.
- [ ] `IslandLayoutGenerator` is fuzz-validated for 2–12 biomes (matched to `RunBiomeMapSize` = 10); it needs
  hardening first if a future roster grows past ~12 or `RunBiomeMapSize` is raised.

### 7.4 Stale comments and docs (unverified; code comments should shrink toward the comment budget as touched)

The stale comments the sweep listed were fixed 2026-10-03 (`bag.ts`, `timeline.ts`, `Wallet.cs`, `AttackService.cs`,
`Gen1TypeChart.cs`, `BattleIntegrationTests.cs`, `battle-ui-cues.spec.ts`, and the three `TODO.md` pointers in
`EvolutionImport.cs`, `MoveImport.cs`, `PartyExpShareTests.cs`).

- [ ] **Audit the "Gen 2+" claims against pret/pokecrystal (S–M, unverified).** The Gen 2+ type-chart claims in
  `Gen1TypeChart` and `GEN_DIFFERENCES.md` were wrong. `IBattleRules.cs` carries ~20 more per-generation XML docs
  and `Gen1BattleRules.cs`/`MoveEffects.cs` a handful of inline comments stating what Gen 2+ does (crit, variance,
  binding duration, status denominators, OHKO rule, Roar/Whirlwind, confusion, Conversion, Leech Seed, Physical/
  Special split). The Gen 1 value in each is what the engine implements and tests; it is the *Gen 2+ half* that
  has gone unchecked. Verify each against pokecrystal and trim the docs toward the comment budget as they are
  touched.
  **DoR (V16):** ✗ not Ready — gaps **1, 6** (drafted here; confirm them and it is ✅ Ready; S–M, about 60–100k tokens
  for the pokecrystal reads).
  - **Acceptance:** every Gen 2+ statement in `IBattleRules.cs`, `Gen1BattleRules.cs` and `MoveEffects.cs` carries a
    pokecrystal file:line or is deleted. A wrong one is corrected, and any new gap it reveals gets its own entry.
  - **Quirk:** none (docs and comments only).
  - **Answered:** 2 (process), 3 (n/a), 4 (pokecrystal), 5 (comments only), 7 (none).

### 7.5 Speculative, unverified — check before filing as real

Nothing open. The list was verified and its findings fixed or filed in §7.6 V15 (`TODO_ARCHIVE.md` → "§7.6 V15 —
speculative list (§7.5)").

### 7.6 Verification pass over the unverified findings (§1 #2) — complete

Done 2026-10-17 (V1–V16, nothing open). The per-slice records, the process, the verdict-tag legend and the slice index
are in `TODO_ARCHIVE.md` → "§7.6 V16 — full Gate 1 audit" (the pass as a whole) and "§7.6 V1" … "§7.6 V15". The
V16 audit's summary table is archived there too; the standing audit is the inline **DoR (V16)** marker on each open
entry above, kept current as entries change.

---

## 8. Open decisions (each needs the user; recommendation first)

None open. The 2026-10-17 rulings are recorded in each entry. New decisions go here as a row: the decision, the
recommendation, and what it unblocks.

---

## 9. Settled — do not re-raise

- **RC tags may point at any commit** — accepted policy (user ruling 2026-10-11, §7.6 V2): the deploy trigger
  matches the tag name only; no on-`master` check (`MoDirectives/authority.md` rule 3).
- **The production container runs as root** — accepted (user ruling 2026-10-11, §7.6 V2): the runtime stage keeps
  the `aspnet` image's default user; don't re-file `USER $APP_UID`.
- **The Docker build re-downloads sprites on any source change, and base images are tag-pinned** — accepted (user
  ruling 2026-10-11, §7.6 V4): the `assets` stage's `COPY . .` and the tag pins stay; don't re-file layer-cache
  tuning or digest pinning.
- **No `UseForwardedHeaders`; server exhaustion unmeasured** — accepted for now (user ruling 2026-10-11, §7.6 V3):
  nothing reads the client IP or scheme today. Revisit together with the abuse-protection entry in §7.1, whose
  per-visitor rate limit would need it.
- **Replay on every reconnect** is the accepted design (the server can't tell a blip from a refresh); the client
  tolerates it (`replayDedupe`).
- **RNG seam closed:** don't re-file the `AlwaysHit`/`AlwaysCrit` shim idea, the unseeded web composition root, or
  "Roll\* ignores the battle seed" (production per-run web seed also closed 2026-06-17).
- **Event wire contract** is guarded field-by-field automatically (`EveryBattleEventProjectsAllOfItsFields`); don't
  re-file "add a guard per event" — a one-off test is only for values/semantics.
- **TypeScript is typechecked** (`tsc --noEmit` in the pre-commit hook and `test.ps1`); `tsconfig` covers `e2e/` as
  well as `src/` — keep it that way.
- **The frontend stays deliberately un-linted and un-formatted** (user ruling 2026-07-17); `tsc` is the only frontend
  gate. Don't re-file it.
- **DB services skip try/catch** — decided, not a gap: they are thin EF pass-throughs, and every real caller wraps the
  operation at its boundary (`DEV_STANDARDS.md` → Error Handling).
- **Every run prompt is `'blocking'` by construction** — each parks a server-side await, so dismissing one would
  strand the run; don't re-file "the modals should close on Escape". (The Escape-as-B-cancel item in §4.5 covers only
  the four prompts that have a negative answer.)
- **BST tier bands stay as they are** (user ruling 2026-10-06): the tier multipliers and `K` were tuned on modern
  base stats and are not retuned after the real-Gen-1 import — no measured problem. Reopen only on a playtest report.
- **The other Yellow availability corrections and an evolve-only rule** — not pursued (user ruling 2026-10-17):
  the type is never shown and only gates the wild pool, so Eevee `GameCorner` → `Gift`, Wigglytuff → `GameCorner`
  and a rule for evolve-only forms change nothing in play. Only Lickitung is worth fixing (§3.2).
- **The map peek may cover the player nameplate at small sizes** — accepted (user ruling 2026-10-17): at 800×600
  the 2.6 s auto-peek covers `.player-corner`; the risk is accepted at that resolution (reproduced in §7.6 V15).
- **No per-area encounter floor** — accepted for now (user ruling 2026-10-17): `PickByBst`/`ScaleWildLevel` select
  by BST band only, and the evolution floor (`EvolutionMinLevel.Compute`, `ENCOUNTER_DESIGN.md` §3.8) already
  keeps evolved forms out of low levels. Reopen on a playtest report.
- **The client chooses the run `Seed`** — kept (user ruling 2026-10-17): `?seed=N` drives the E2E and Puppeteer
  reproductions; don't re-file "the server should assign it" (surfaced by §7.6 V4).
- **`RunLoop.cs`'s ~28 types are fine** — a cohesive vocabulary file; don't split it on a type-count metric.
- **`RunLoop`, `RewardRunEvent`, `AcquisitionResolution`, `CompositeEvaluator` and `MoveEvaluators` are tested** —
  refuted 2026-10-17 (§7.6 V15): named tests reach each; don't re-file them as "untested types".
- **WAIVED (user):** the `SignalRInput` cancel/prompt race (abandoned-run task leak) — until a save/persistence
  layer exists; no pinning test for `items.db`'s contents (production ships the committed clean db);
  `StarterSelection` seeding from `DEFAULT_GENERATION` (the 4d+ generation picker replaces the line); the Settings
  difficulty dial's self-referential-scaling limitation; the encounter map's dead-end-fallback orphan-waypoint edge
  (unreachable in a connected run map).
- **Max Revive** is out of `items.db` and the roster; the scaffolding a future generation needs is the schema, not a
  stray row (§4.4).
- **Repo docs and comments can be wrong about Gen 1/Gen 2:** `GEN_DIFFERENCES.md` and three code comments misstated the
  Gen 2 type-chart changes (corrected 2026-10-03 against pret/pokecrystal). Verify against the primary source.
- **The 2026-07-19 repo-wide PR-audit is fully closed** — don't re-file "Repo-wide PR-audit findings" as an open
  section.
