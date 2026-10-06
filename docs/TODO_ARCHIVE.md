# Battle Sim – Done / Archive

Historical record of completed work, split out of `TODO.md` to keep the live task list (and the
per-session read) small. Nothing here is pending. The per-batch logs are kept verbatim because they
double as a fidelity record and the `seam-reviewer` references these patterns.

> **Live tasks:** `TODO.md` · **See also:** `CLAUDE.md`, `AI_CONTEXT.md`, `DESIGN_GUIDES.md`, `DEV_STANDARDS.md`

---

## `dev.ps1` readiness + logs and `stop-dev.ps1` ownership ✅ DONE (2026-10-06)

**Was** `TODO.md` §7.1 "`dev.ps1` readiness + logs and `stop-dev.ps1` ownership (S–M)", part of §1 row 1. Tooling only;
not player-visible. The other §7.1 bullets (`-StartStack` leak, `test.ps1 -E2E -StartStack` exit 0, pre-commit hook gaps,
CI/deploy, Script/Docker polish) stay open in `TODO.md`.

**Problem.** `dev.ps1` waited only on Vite, so it reported "Ready" and opened the browser while the backend was still
compiling; it exited 0 even on timeout; the servers' output lived only in the `-NoExit` windows; the self-clean ran only
when a port was busy, so empty wrapper windows left by a crashed server piled up; and `stop-dev.ps1` killed *any*
listener on :5100/:5173 (another project's Vite included).

**Scope (as shipped).**
- (a) `dev.ps1` also polls `GET /api/dev/status` (200 in Development), names which side did not respond, exits 0 on
  ready / 1 on timeout; `-TimeoutSeconds` (default 60).
- (b) Both servers tee output to `.dev/backend.log` and `.dev/frontend.log` (git-ignored, truncated each start); the
  paths are printed on timeout.
- (c) `dev.ps1` always runs `stop-dev.ps1` (the port-busy precondition is gone).
- (d) `stop-dev.ps1` kills a port listener, in the first pass and the straggler pass, only if its own or an ancestor's
  command line has a path under the repo root (never the caller's own chain); a foreign holder is left running and named
  as "left alone". The straggler pass refreshes its process snapshot. When clear it says nothing of this repo's is running.
- (e) After the self-clean, `dev.ps1` re-checks both ports and, if a non-repo process still holds one, exits 1 naming
  the port, PID and process (otherwise Vite auto-bumps to :5174 and the readiness wait passes against the foreign server).
- Docs: `CLAUDE.md` `dev.ps1` paragraph (exit code, logs) and `.gitignore` (`.dev/`).

**Tee-Object tradeoff (measured 2026-10-06).** The output and the Ctrl+R / `h + enter` hints survive, but colour is lost
(output is no longer a TTY; accepted). Emoji/box characters garble unless each wrapper first sets
`[Console]::OutputEncoding = [Text.Encoding]::UTF8` (verified to fix both the log and the window). The Ctrl+R keypress
itself was not exercised.

**Acceptance (five scenarios, run by hand by the assistant, all passing 2026-10-06; the scripts have no harness).**
(1) `.\dev.ps1 -NoBrowser` returns only once `/api/dev/status` and :5173 both respond, exits 0, both log files exist with
startup output. (2) `.\dev.ps1 -TimeoutSeconds 1` exits 1, names the side(s) not yet up, prints the log paths. (3) With
both servers killed but their `-NoExit` windows left open, `.\dev.ps1` closes those windows. (4) With a foreign listener
on :5173, `.\stop-dev.ps1` leaves it running and says so, and `.\dev.ps1` exits 1 naming :5173 and the PID without
starting anything. (5) `.\stop-dev.ps1` still stops the whole repo stack (servers + wrappers) and returns `$true`
(`$false` when already clear).

**Gate-1 note.** The entry carried scope, acceptance, design, gen surface (none), Gen 1 source (n/a), data-vs-runtime
(neither), quirk (none; scenarios instead) and dependencies. No breach.

---

## X Accuracy — Gen 1 "skip the accuracy check" flag ✅ DONE (2026-10-04)

**Was** `TODO.md` §1 row 1 / §3.2 "X Accuracy (M)", the third item of "Three data/engine fixes". Design approved by the
user 2026-10-04. Player-visible -> `PRODUCT_SPEC.md` §5 *In-battle item party-targeting*. The unverified side findings
(gate order, Dire Hit/Guard Spec second use, item descriptions) stay open in `TODO.md` §3.2 "Gen 1 gate-order and Dire
Hit/Guard Spec gaps".

**Problem.** X Accuracy was imported as a +1 accuracy stage (the Gen 3+ shape). Gen 1 `MoveHitTest` (pret/pokered
`engine/battle/core.asm`) returns at once when the user has `USING_X_ACCURACY` ("always hit regardless of
accuracy/evasion") — after the Dream Eater, Swift, Dig/Fly and **Mist** checks, and without the random roll (so no 1/256
miss).

**Acceptance (as shipped).** With the Gen 1 rules, using X Accuracy sets a per-battle flag (item consumed, including a
second use). While set, the creature's moves skip the accuracy roll with no RNG draw — a hit on a roll that would miss,
even at +6 foe Evasion — and a failed OHKO speed check is not rescued; Mist still blocks a stat drop; the flag clears on
the per-battle reset (including a switch-out/switch-in) and with Haze on both sides. Under rules that do not bypass (the
alt-profile double) X Accuracy raises accuracy one stage as before.

**Design corrections found at source (the TODO draft was wrong on all three).**
1. *Bypass vs. stage:* Gen 1-2 skip the accuracy check (pokecrystal `BattleCommand_CheckHit`); Gen 3+ is a +1 stage
   (pokeemerald `ItemUseInBattle_StatIncrease`). Hence a rules-seam member, not a data change.
2. *A second use is consumed* in Gen 1 (pokered `ItemUseXAccuracy` has no refusal). The "won't have any effect" refusal is
   Gen 2 — a separate, still-unmodeled seam member.
3. *No narration of its own:* a dedicated `XAccuracyApplied` event was designed, then deliberately removed — `ItemUsed` is
   the whole narration; no active-effect indicator (user decision).

**Shipped.**
- `IBattleRules.XAccuracyBypassesAccuracyCheck` (Gen 1 true; `TestAltProfile` false; `DelegatingBattleRules` passes
  through). `BattleState.UsingXAccuracy`, cleared by the per-battle reset and by Haze on both sides.
- `ItemEffectContext.Rules` and `ItemAction`'s `rules` parameter are now **required** (the silent Gen 1 default was removed
  at `requirements-review`'s request). `BattleBoostItemEffect` sets the flag instead of raising the stage when the rules
  bypass.
- `AttackAction.ResolvePreDamageGates` skips the threshold and RNG draw when the flag is set; the OHKO speed gate runs
  first, so it is not rescued. This engine has no untargetable Dig/Fly turn — a recorded gap (`TODO.md` §3.1).
- Data: the x-accuracy row is otherwise unchanged (+1 accuracy stage) but `ItemMapper.ApplyGen1Gameplay` overrides its
  `Description` to "Your moves skip the accuracy check for the rest of the battle."; `items.db`'s one row updated to match
  (verified row-by-row vs HEAD: 28 rows, one changed, Description only; WAL checkpointed). `DATA_IMPORT.md` records the
  exception. Gen-variable surface: the bypass itself and (unmodeled) the Gen 2 second-use refusal — `GENERATION_SEAMS.md`
  §2 row + §5.0.2, `GENERATION_PROFILE.md` §3, `STATE_MODEL.md`, `GEN_DIFFERENCES.md`.
- **Tests:** `XAccuracyContractTests` (10) incl. `XAccuracyMakesAFasterOhkoUserLandThroughMaxedEvasion`;
  `ItemEffectTests`, `ItemActionBattleTests`, `BattleForcedSwitchTests`, `UniqueMoveEffectContractTests` additions;
  `ItemImportTests` description pin.
- **Sources verified:** pret/pokered `engine/battle/core.asm` `MoveHitTest`, `engine/items/item_effects.asm`
  `ItemUseXAccuracy`, `haze.asm` `HazeEffect_`/`CureVolatileStatuses`; pokecrystal `BattleCommand_CheckHit`; pokeemerald
  `ItemUseInBattle_StatIncrease`.

**Gates.** `format-gate` PASS (311 files); `test-runner` .NET 1742/1742, tsc clean, Vitest 311/311;
`requirements-review` 7 discrepancies, all fixed at user direction; `pr-review` CHANGES-REQUESTED on 3 comment-budget
items, all fixed at user direction (comment-only; no second pass).

---

## Psychic's Special-drop chance + Full Restore on a statused full-HP creature ✅ DONE (2026-10-04)

**Was** `TODO.md` §1 row 1 / §3.2 items (a) and (b) of "Three data/engine fixes". The third item, X Accuracy, shipped
separately (see the X Accuracy section above). Both fixes verified at pret/pokered 2026-10-04. Player-visible (Full Restore) →
`PRODUCT_SPEC.md` §5 *In-battle item party-targeting*.

**(a) Psychic's Special-drop chance (S).** `moves.db` carried 10 in `EffectChance` and `StatEffectChance`; Gen 1 is
33% (`engine/battle/effects.asm` `StatModifierDownEffect`: `cp 33 percent + 1`; `data/moves/moves.asm`
`PSYCHIC_M, SPECIAL_DOWN_SIDE_EFFECT, 90, PSYCHIC_TYPE, 100, 10`, the only move with that effect).
- **Acceptance:** Psychic's imported row carries 33/33 and a re-import cannot restore 10.
- **Shipped:** `"psychic"` joins the 33% group in `MoveImport.ApplyGen1Corrections` (as `acid`); the `moves.db` Psychic
  row `EffectChance`/`StatEffectChance` 10 -> 33 (verified row-by-row vs HEAD: one row changed; the WAL was
  checkpointed into `moves.db` first, since the Web csproj copies `*.db` only).
- **Tests:** `MoveMappingTests.Layer2Correction_Psychic_SpecialDropChanceIs33`; `("psychic", 33)` rows in both
  `SecondaryChanceDataContractTests` theories; the wrong `// 10%` comment in `SecondaryEffectContractTests` removed.
- Gen-variable surface: none (move data, resolved at import — `ARCHITECTURE.md` §2.6). Data vs runtime: importer +
  `moves.db`, no engine change. `DATA_IMPORT.md` deliberately does not list individual corrections (its policy: the
  switch itself is the record).

**(b) Full Restore on a full-HP creature with a status (S).** `HealingItemEffect.CanApply` required HP < max, so it
refused. Gen 1 `ItemUseMedicine` (`engine/items/item_effects.asm`) at full HP turns FULL_RESTORE into FULL_HEAL when a
status is present (cures it, item consumed) and refuses only when there is none.
- **Acceptance:** full HP + status -> status cured, item consumed, no zero-amount `Healed` event; full HP and no
  status -> still refused, not consumed.
- **Shipped:** `ItemEffects.cs` `HealingItemEffect.CanApply`/`Apply` plus private `CuresStatus`: at full HP with a
  major status a `CuresAllStatus` item cures it and is consumed with no `Healed` event. Confusion-only and fainted
  targets stay refused; Potion at full HP with a status stays refused. Gen-variable surface: none identified
  (`GENERATION_SEAMS.md` §5.0.2 note added). Data vs runtime: runtime only (the item row was correct).
- **Tests:** `ItemEffectTests` (cure with no `Healed`; no-status refused; Potion at full HP with status refused;
  confusion-only; fainted with status; Toxic counter reset; Sleep counter cleared); `ItemActionBattleTests`
  `UsingFullRestore_*` (consumed / not consumed); `timeline.test.ts` "narrates a status-only Full Restore".
- **Open aside (not done):** `Creature.FullHeal` and `ReviveItemEffect` duplicate `ClearStatus`'s reset instead of
  routing through it (kept in `TODO.md`).

**Gates.** `format-gate` PASS; `test-runner` .NET + tsc + Vitest green; `requirements-review` 4 discrepancies, all
fixed at user direction (confusion-only test, counter/fainted tests, docs, Healed-omission narration confirmed intended
+ Vitest case); `pr-review` CHANGES-REQUESTED on 5 comment-budget lines, all deleted at user direction (no second
pass).

---

## E2E repairs after the species-data import ✅ COMPLETE (2026-10-06)

**Was** `TODO.md` §1 row 5 / §6 "E2E repairs after the species-data import" (S). Phase 1 implemented 2026-10-04 and
confirmed by the user's 2026-10-06 full-suite run (36/38); phase 2 (the two remaining failures, below) implemented and
confirmed green by the user 2026-10-06 (`.\e2e.ps1 -Spec evolution` and `-Spec reward-drop` pass). Test-only; no
player-visible behavior, so no `PRODUCT_SPEC.md` entry.

**Phase 2 — the two remaining failures (traced from failure screenshots, specs and `helpers.ts`; trace zips not
unpacked).**
- **`evolution.spec.ts` ALLOW — a helper race, not the species data.** The screenshot showed the evolved CHARMELEON
  killed by a Boss PINSIR with `Run over — 3 wins` in the log, which `expectRunFlowsOn` counts as the run continuing.
  But `playCurrentRunUntil` re-read the log mid-iteration (`helpers.ts:393`) and returned `false` on "Run over" without
  re-asking the caller's `reached` predicate, so a run ending between the loop's two checks reported a stall.
  **Fix:** that line returns `reached(page)` instead of `false`; callers whose predicate ignores "Run over" still get
  `false`.
- **`reward-drop.spec.ts` — seed 1 no longer rolled a drop.** The screenshot showed `BATTLES WON 1` and the run dead in
  battle 2 with no reward modal; `BattleDropChance` is 0.85 (`RewardCalculator.cs:24`), so seed 1 landed the 15%.
  **Fix:** the spec walks a short seed list (1-6) until the reward modal appears, as its own comment prescribed — no Dev
  Mode drop-forcing flag (product code for a test-only problem).
- **Acceptance:** `.\e2e.ps1 -Spec evolution` and `-Spec reward-drop` pass when the user runs them — met 2026-10-06.
  Design: test-only. Gen-variable surface: none. Gen 1 source: pokered base stats (the 85% drop rate is run-layer
  tuning, not Gen 1). Data vs runtime: neither (spec/helper code only). Quirk asserted: a run ending ("Run over") after
  an answered evolution offer is the run flowing on, and the evolved form is the active creature. Dependencies: none
  (the BST tier-band decision settled 2026-10-06, `TODO.md` §9).

**Phase 1 record.**

**Context.** A 2026-10-04 full-suite user run went 34/38; the failures follow the species-data import (real Gen 1
base stats and base experience). Causes traced from screenshots, specs and helpers; trace/video not opened.

**What shipped.**
- `e2e/starter-select.spec.ts`: Charizard BST pin 449 -> 425 (real Gen 1: 78+84+78+100+85).
- `e2e/helpers.ts`: new exported `answerPokeCenterIfPresent` (clicks HEAL on
  `.recovery-modal[aria-label="Poké Center recovery"]`); `playCurrentRunUntil` calls it by default, beside
  `leaveShopIfPresent`/`dismissRewardChoiceIfPresent`. New `PlayOpts.pokeCenter?: 'heal' | 'leave'` (default `'heal'`).
- `e2e/poke-center.spec.ts`: passes `pokeCenter: 'leave'` to its `walkSeedsUntil`, because its target is that modal
  (same auto-answer race as `drafts: 'leave'`). A design addition beyond the original TODO text.

**Confirmed (user run, 2026-10-06, 36/38):** `starter-select`, `poke-center` pass. `forced-switch` (originally a
phase-2 suspect, seed-walking to a party of two) also passes with no change. `evolution` failed for a different,
helper-side reason (phase 2, above).

**Gates.** `format-gate` N/A (no `.cs`); `test-runner` `.\test.ps1 -Web` PASS (tsc clean, Vitest 310/310);
`pr-review` skipped (test-only); `requirements-review` N/A.

---

## `test.ps1 -Filter` — one sanctioned path for single-test runs ✅ DONE (2026-10-04)

**Was** `TODO.md` §1 row 2 / §7.1 "`test.ps1 -Filter` (S)". Dev-script change; no player-visible behavior, so no
`PRODUCT_SPEC.md` entry.

**Why.** `test.ps1` could not run a subset of the .NET suite, so `CLAUDE.md` documented a raw `dotnet test --filter`
that bypassed its `TEST SUMMARY`; the rule is that every test run goes through `test.ps1`.

**Acceptance.** `.\test.ps1 -Filter AbandonTimerTests` runs only the matching tests and prints the normal summary
block with the filtered counts; with no `-Filter` the behaviour is unchanged; `CLAUDE.md`'s single-test command
points at it. Gen-variable surface: none. Gen 1 source: N/A (tooling). Data vs runtime: neither. Dependencies: none.

**What shipped** (differs slightly from the drafted entry).
- New `[string]$Filter` parameter on `test.ps1`. A bare word becomes `FullyQualifiedName~<word>`; any value
  containing `=~!|&()` is passed to `dotnet test --filter` as-is.
- `-Filter` with no suite switch implies `-Dotnet` only, so it can never trigger the "run everything incl. E2E"
  default. With only `-Web`/`-E2E` given, it is ignored with a notice.
- A filter that matches nothing reports `FAIL '0/0 - filter matched no tests'` and exits 1 (the draft said a `0/0`
  report; `dotnet test` itself exits 0 on zero matches, which must not read as a pass).
- `CLAUDE.md`'s "run a subset by name" command is now `.\test.ps1 -Filter <ClassOrMethodName>`; the raw
  `dotnet test --filter` mentions in `.claude/AI_CONTEXT.md` and `.claude/agents/test-runner.md` were retargeted.

**Verification.** Run directly: `-Filter AbandonTimerTests` gave PASS 3/3; `-Filter NoSuchTestAnywhere` gave FAIL 0/0,
exit 1.

**Gates.** `format-gate` N/A (no `.cs`); `test-runner` replaced by the direct verification above; `pr-review` and
`requirements-review` N/A (dev script).

**Not covered.** The pre-commit hook's own direct `dotnet test` call remains in the "Pre-commit hook gaps" item.

---

## R1d-b — the abandon-timer race ✅ DONE (2026-10-03)

**Was** `TODO.md` §1 row 2 / §2 "R1d-b", designed in `RECONNECT_RESILIENCE.md` (section removed from that doc once
shipped). Web-layer reliability fix; no player-visible behavior change, so no `PRODUCT_SPEC.md` entry.

**What was wrong.** `ActiveBattle.ScheduleAbandon`'s continuation tested task state (`t.IsCanceled`), not whether
*that* timer was still the current one. If the grace delay completed at the instant a reconnect called
`CancelAbandon()`, the continuation was already scheduled and called `Input.Cancel()` on a live run, killing the
run on a successful reconnect.

**Acceptance.** A timer whose grace elapsed after it was cancelled or replaced never cancels the input.

**What changed.** `creaturegame.Web/Battle/GameSessionManager.cs`: `ScheduleAbandon` returns the armed
`CancellationTokenSource`; its continuation calls `OnGraceElapsed(cts)`, which, under `_lock`, acts only if `cts` is
still the current `_abandonCts` (reference-equal) and not cancelled, then nulls the field and calls `Input.Cancel()`
inside the lock (`Cancel` completes TCSs with `RunContinuationsAsynchronously`, so no re-entrancy). `CancelAbandon`
already nulls the field, which makes the reference check work.

**Tests.** `tests/creaturegame.Tests/Integration/Web/AbandonTimerTests.cs`, deterministic (no sleeps): the current
timer cancels the input; a reconnect-cancelled timer does not; a replaced timer does not.

**Gates.** `format-gate` pass; `test-runner` and `pr-review` skipped by the user (tests run manually);
`requirements-review` not applicable.

---

## Bag scope decision — per-run; meta layer later; no server-restart saves ✅ DECIDED (2026-10-03)

**Was** `TODO.md` §1 #1 / §8 "Bag scope" (*per-run vs. meta-progression*), which gated bag persistence, `save.db`,
Catch and stone evolutions. Decision record (the why, consequences, code pointers) lives in `ARCHITECTURE.md` §2.12.

**Rulings (user)**
- The `Bag`, `Wallet` and party are **per-run**: built fresh at run start, gone when the run ends.
- A roguelite **meta-unlock layer** is the eventual direction; **unplanned** (needs its own `/plan`).
- **No server-restart-surviving saves** (`save.db` / `PlayerSave`) during the development phase — declined and parked
  (design kept in `TODO.md` §4.2); only the client-side session resume exists.

**Effect:** Catch and stone evolutions are no longer gated on persistence. Doc-only; no player-visible change, so no
`PRODUCT_SPEC.md` entry.

---

## Process gates hardening — DoR hard gate + docs-cleanup last + hook stamp ✅ DONE (2026-10-03)

**Why.** A 2026-10-03 audit of `TODO.md` showed the Definition of Ready and `docs-cleanup` had not been upheld:
resolved items were still open, code comments and `GEN_DIFFERENCES.md` stated wrong Gen 2 type-chart facts, and
designs (the R1d reconnect designs) sat inside the TODO. The user directed that both gates become non-negotiable and
mechanically checked. Process tooling only — no player-visible behavior, so no `PRODUCT_SPEC.md` entry.

**What changed**
- `CLAUDE.md` — new top-level **Process gates** section. **Gate 1 — Ready before code:** no implementation starts on
  a `TODO.md` entry that is not DoR-complete; the assistant audits the entry against the DoR in its reply and, if
  gaps exist, refuses and lists them. **Gate 2 — Done means `docs-cleanup` ran, last.**
- **Gate order (fixed):** `format-gate` → `test-runner` → `requirements-review` (battle/stat/move work) →
  `pr-review` (product code or a seam) → **`docs-cleanup` always last**. It was step 1; moved last so it reports
  against the final tree. Any later change (code or docs) re-runs it. Updated in `CLAUDE.md`,
  `.claude/AI_CONTEXT.md` (list reordered and renumbered), `docs/DEFINITION_OF_DONE.md`,
  `.claude/agents/pr-review.md`, `.claude/agents/docs-cleanup.md`, and `docs/PRODUCT_SPEC.md`'s "How this doc grows".
- `docs/DEFINITION_OF_READY.md` — new **Enforcement — no code before Ready** section.
- `docs/TODO.md` — Gate-1/Gate-2 bullets, an entry template, and a **Ready?** (DoR audit) column on the §1 table.
- `.claude/agents/docs-cleanup.md` — gained the **Gate-1 audit** (report a `GATE-1 BREACH` for an item whose entry
  lacked DoR fields or did not exist) and the stamp as its last act.

**Mechanical enforcement.** `.githooks/stamp-docs-cleanup.sh` writes `.git/docs-cleanup.stamp`; it is run as
`docs-cleanup`'s last tool call. `.githooks/pre-commit` blocks a commit if there is no stamp, or if any staged file
is newer than it. `.githooks/post-commit` consumes (deletes) the stamp so every commit needs a fresh one.
`DOCS_CLEANUP_BYPASS` exists but is human-only (the hook warns and passes). Tested with five scenarios: no stamp →
blocked; fresh stamp → passes; staged file touched after the stamp → blocked; bypass → warns and passes;
post-commit removes the stamp. All behaved correctly.

**Honest breaches committed before the rule existed (same session, 2026-10-03)**
- This work itself had no prior `TODO.md` entry — implemented directly on the user's conversational instruction.
- The R1d reconnect designs were written, and the species base-stat / `BaseExperience` fix (`9b4a7e9`) was
  implemented, without DoR-complete entries (the species fix had a TODO bullet but lacked the DoR fields).
- Commits `2aa3f08` and `be2bac7` were made without a `docs-cleanup` run (the user instructed the commit
  explicitly; the batch was itself a docs reorganisation).

---

## TODO.md rehaul — finished records moved out of the live list ✅ DONE (2026-10-03)

Everything below was **copied verbatim** from `TODO.md` when it was rebuilt as an active-work-only file
(the rule: *finished work lives here, not in the TODO*). Nothing was summarised or edited; the blocks keep
their original wording, including section-relative phrases such as "below" and "above". Open items that
sat inside these sections were carried into the new `TODO.md`. Headings inside each block are shown as
they were.

### 1. The old header narrative: "Current state (2026-09-29)", the Tier 1–5 list and the notes under it

````markdown
## Current state (2026-09-29)

The Gen 1 battle engine is **feature-complete** (all 165 moves, XP & level-up, learnsets, AI move selection,
EV / Stat-Exp gain, evolution, in-battle item system incl. **Revive/Max Revive** and full party-targeting for
Healing/StatusCure/PpRestore/Revive), and the roguelite run layer on top is playable end-to-end: the
**Encounter Logic** biome-graph run (biome pick → randomised 4–6 nodes → Poké Center → next biome, per-run
randomised **Town Map**, depth-scaled foes), the full **roster** (party of 6, both post-battle acquisition
channels — themed draft + boss catch, between-biome lead choice, forced faint-switch, and the voluntary
**In-Combat Switching**), the **Run Economy** (gold + rewards + the spend-gold **Shop node**), the **Reward
Choice** modal (now including **TM/HM — Move-Teach Rewards**, a second move-acquisition channel offering a
legal TM move as a reward-card pick — no HMs), the **level-aware XP curve + trainer bonus + Innate Party XP Share**,
**Creature Naming** (a cancelable nickname on every acquisition path), **Creature Identity** (per-run creature
ids; the event wire and client route by id, not display name), **Session Resume** (refresh/reopen-safe
`gameId` persistence), the **CHECK POKEMON party-member picker**, the **Settings Menu** (sound volume + a
three-tier difficulty/XP-pace dial), and **Generation Profile** Stages 1–4c (the generation axis, content
scoping, and the Kanto Sage-skinned Town Map) are all done and archived (→ `TODO_ARCHIVE.md`).

**Next up — tiered, restructured 2026-09-27 after a full pass over every open item in this file** (the
2026-09-12 tiering's Tiers 0/1/3/3b are now fully shipped and archived, and are folded out of the list below —
see `TODO_ARCHIVE.md` for their full records). Tiers are ordering, not strict sequence: Tier 1 is a standing
user-sequenced commitment (2026-08-04) that stays ahead of Tiers 2–3 regardless; the rest is priority, not a
hard dependency chain.

- **Tier 1 — Generation Profile Stage 4d+** (the jointly-iterated surface catalog) — make Gen 1 an explicit,
  swappable profile so a generation switch changes content, menus and look, not just battle math. **`/plan`
  DONE (2026-07-29; Stage 4 re-planned as v2 on 2026-07-31)** — full design in
  [`GENERATION_PROFILE.md`](GENERATION_PROFILE.md). **Stages 1–3 complete; Stage 4: 4a/4b/4c shipped, 4d+
  open** (Stage 5 is the standing falsification rule) — task entry + staging below. **Sequenced ahead of Tiers
  2–3 (2026-08-04, user's call).**
- **Tier 2 — Item Acquisition · Bag Persistence · Catch** — the deferred cluster, unblocked by the acquisition
  channels. Bag-scope decision (per-run vs. meta-progression) first, then `BallItemEffect`/catch
  formula/animation. *(Item acquisition itself is already done via the Run Economy; bag persistence + catch
  remain.)*
- **Tier 3 — Game Loop & Progression** — progressive difficulty (good pairing point for the remaining
  per-area encounter-table question — see *Known Gaps* — now that evolved forms, stone lines included, are
  level-floored via **Species selection respects each species' evolution-chain floor**), the `PlayerSave`/`save.db`
  layer (+ the heavier session-persistence option beyond the lightweight **Session Resume** already shipped),
  Stone evolutions (waits on Catch above). Party + between-biome lead + forced-switch are done.
- **Tier 4 — opportunistic polish + test-infra loose ends:** Web UI Polish (move-specific animations, text
  feel, sprite FX joint sketch, Escape=B-cancel, `ConsoleInput`), and the test-infra items below (CI E2E step, `data-testid`, visual-regression, the
  `evolution.spec.ts` gap, `GameSessionManager` connection-lifecycle coverage).
- **Tier 5 — reference/housekeeping, no urgency:** Multi-Generation Data Model & Schema, User Documentation,
  and the "watch, don't refactor speculatively" Tech Debt items.

**Repo-wide code review sweep (2026-10-02) — findings awaiting adjudication**, tiered **R1–R4** in
*Repo-Wide Code Review Sweep* below (R1 = players get stuck or lose control, R2 = correctness/fidelity/safety,
R3 = robustness/UX/a11y, R4 = hygiene/nits). Separate from Tiers 1–5; the user places each item.

**Open, unplanned, not placed in the tier list above:** Gen 1 has no end-of-turn residual phase — `Battle`'s
turn shape differs from the real games' (see *Known Gaps* below). Needs a `/plan` before it can be tiered.

**E2E flakiness note** (kept for the lesson, not as open work): `status.spec.ts` **fixed 2026-07-15** — root
cause was a spec asserting a transient badge, not an engine bug; see *Browser-Based UI Testing* for the
seed-≠-determinism lesson it taught. Still live: `endless-chain.spec.ts` *"a run ends when the player faints"*
failed once in a full 2026-07-26 suite run — no `Run over` log line after 1m10s — but passes in **7.3 s** run
alone; consistent with the documented "a long run accumulates abandoned server-side runs" degradation, not a
code defect. `voluntary-switch.spec.ts` was failing standalone until 2026-10-02 (draft gate + seed drift after
`0eb0d53`); fixed with a Dev-Mode `forceDraft` — only its ~3 min runtime remains open (*Browser-Based UI Testing*).
(Web UI polish, Multi-Generation groundwork, User Documentation, and test-infra items are Tiers
4–5 above, not repeated here.)

**Settings Menu** — sound volume + difficulty→XP bonus, both shipped and archived (→ `TODO_ARCHIVE.md` →
*Settings Menu — sound volume + difficulty (XP bonus) controls*); the difficulty dial's self-referential-scaling
limitation is a known, user-waived follow-up, not open work.
````

### 2. Item Acquisition · Bag Persistence · Catch — the intro, "built vs. stubbed" and the DONE item-acquisition gate

````markdown
## Item Acquisition · Bag Persistence · Catch  ⟵ item acquisition DONE via Run Economy; bag persistence + catch remain

**One interlocked cluster, deliberately deferred together** — each depended on the previous and on the
Encounter Logic gate, which has since shipped (Encounter Logic Phase 4, archived) and cleared item acquisition
itself (via the Run Economy, below). Bag persistence and catch are what remain open:
- **Acquisition** can't be designed until the encounter / eligibility model exists (drop rates are meaningless
  against an undefined distribution).
- **Bag persistence** is meaningless until acquisition defines *what's* in the bag and *when* it's earned.
- **Catch** is just one acquisition channel, and a random high-BST catch is the canonical balance hazard.

> **"Catch" is likely a misnomer.** The player may receive Pokémon several ways — in-battle capture,
> post-battle rewards, gifts/offers, picking from a curated set. Treat this as a broader **acquisition** layer
> when designed; in-battle "catch" is one channel, not the whole feature.

### Current state — built vs. stubbed (code anchors)
- **Bag is transient** — `Items/Bag.cs` is in-memory `id → qty`, reseeded every run, never saved. Per-run:
  consumed items stay gone; the Poké Center refills HP/PP/status, not the bag.
- **Item acquisition (the item side) is now DONE** — the **Run Economy** replaced the old ×20 test loadout:
  `EncounterFactory.BuildStartingBag` seeds a curated modest start and battle-win + Treasure/Mystery drops grow
  it (web-layer `RewardCalculator` policy). So *item* acquisition is solved; **bag persistence** and **catch**
  (below) are the remaining, still-deferred pieces of this cluster.
- **Poké Balls are imported data only** — mapped to `ItemCategory.Ball`, but `ItemEffects.For(Ball)` returns
  null ⇒ `ItemUseFailed`. The frontend hides Ball via `bag.ts isUsableInBattle` (Revive shipped 2026-07-19 and
  is now conditionally shown — see `TODO_ARCHIVE.md` → Revive Items). `CatchRate` is already imported on
  `PokemonSpecies` ✓.

### 1 — Item acquisition (the design gate) · ✅ DONE via Run Economy
- [x] The item-acquisition model is the **Run Economy** (see archive): battle-win drops + Treasure/Mystery
  rewards, gated by the web-layer `RewardCalculator` (skewed rates so a lucky early haul can't trivialise a run),
  replacing the fixed loadout. The between-encounter **Shop node** (spending gold) also shipped and is archived
  (→ `TODO_ARCHIVE.md` → *Run Economy — gold, item rewards, transient bag & Treasure/Mystery nodes*, "Shop node"
  follow-up). Nothing remains open under this heading.
````

### 3. Game Loop & Progression — the finished bullets and the section preamble

````markdown
## Game Loop & Progression

**Prerequisites:** Catch Mechanic, `PlayerDbContext` / `save.db`. Intentionally deferred until combat fidelity
is fully ironed out (the battle sim is the foundation). The **Endless Battle Chain** (done) is the first minimal
slice; the items below are what it deliberately leaves out.
````

### 4. Game Loop & Progression — voluntary switching (done)

````markdown
- [x] **Voluntary in-battle switching** — a SWITCH turn action to swap the active creature mid-fight. ✅ DONE
  (2026-07-25) as **In-Combat Switching** (all three stages incl. the out-of-PP menu affordance); full record
  archived in `TODO_ARCHIVE.md`.
````

### 5. Game Loop & Progression — refresh/reconnect-safe session handling (done)

````markdown
- [x] **Refresh/reconnect-safe session handling** — the lightweight `gameId`-persistence option. ✅ DONE
  (2026-09-14) as **Session Resume**; full record archived in `TODO_ARCHIVE.md`. The heavier `PlayerSave`/
  `save.db` option (survives a server restart/redeploy, not just a client refresh) stays deferred to Tier 3
  (this section).
````

### 6. Game Loop & Progression — cross-encounter status persistence (done)

````markdown
- [x] **Cross-encounter status persistence** — DONE (2026-06-10); major status carries across chain encounters,
  volatiles reset per battle. See `STATE_MODEL.md §2` and `TODO_ARCHIVE.md`.
````

### 7. Generation Profile — the goal, the locked decisions, and Stages 1a–4c (incl. the Kanto Sage ornament pass and all four Town Map steps)

````markdown
## Generation Profile — make Gen 1 an explicit, swappable profile  ⟵ OPEN, `/plan` DONE (2026-07-29)

> **Full design: [`GENERATION_PROFILE.md`](GENERATION_PROFILE.md).** This entry is the task; that doc is the
> design (staging detail, the boundary rule, the falsification harness, DoR coverage).

**The goal.** A generation switch should change the game *completely* — content, region, menus and look, not just
battle math. Today "generation" is a **battle-math axis only**: four seams injected where `Battle`/`Creature` are
built. Everything else is Gen 1 **by assumption, not by seam**.

**Designed against Gen 1 alone — no Gen 2 content is built here. Upward compatibility is the deliverable.**

**Decisions locked with the user (2026-07-29):** (1) Gen 1 only, upward-compatible; (2) presentation is per-gen in
both senses — reskin **and** menu structure; (3) the roguelite layer is **flavour-only** (same node kinds, same
flow, same possibilities — this is what keeps `RunRules` gen-neutral); (4) one generation per run, chosen at run
start, threaded like `Difficulty`; (5) skin = Gen 1's layout **grammar**, not its palette (authentic 4-colour DMG
green rejected — it would discard the type-badge colours and the contrast tuning in `index.css`); (6) battle menu
= Gen 1's **2×2 grid with today's four verbs** (literal `FIGHT`/`PKMN`/`ITEM`/`RUN` rejected — `RUN` is not a turn
action in this engine, so it would mean adding a flee feature, contradicting decision 3).

> ⚠️ **Ship-blocking risk: upward compatibility is unfalsifiable with one profile.** You cannot prove a seam is
> generation-agnostic when only one implementation exists — exactly the trap `GENERATION_SEAMS.md §5.0.1`
> documents (two leaks that passed review *and* tests). Mitigation: a **test-only `TestAltProfile`** giving every
> seam a second implementation. It is **not Gen 2** and carries no fidelity claim. Each stage lands with its leg
> of it; a stage without one has demonstrated nothing.

- [x] **Stage 1a — the axis + the `GameSessionManager` composition point** ✅ DONE (2026-07-29). New
  `creaturegame/Generations/` namespace: `Generation` enum, `GenerationProfile` record (all-`required`
  properties, so a new slice breaks every profile that omits it — a compile error as the reminder),
  `Gen1Profile`, `GenerationProfiles` registry. Threaded `StartGameRequest.Generation` → `ParseGeneration` →
  `RegisterSession` → `PendingSession` → `AttachConnection` → `ProfileFor`, mirroring `Difficulty`; parse +
  lookup `internal` so tests hit the real path. `TypeChart`, `BattleRules` (previously never passed — `Battle`
  fell back internally), `EvolutionRules` and the AI are now read off the profile **explicitly**. Registry
  **throws** on an unregistered generation rather than serving Gen 1, with the boundary parse guaranteeing it
  never sees untrusted input. Covered by `GenerationProfileTests` (14 cases) + `TestAltProfile`, Stage 1's
  falsification leg.
  - **AI decision (the §4.3 open question): the AI is on the profile.** `Gen1TrainerAi` is generation-*named*
    but documents itself as a "generation-blind selection policy" whose Gen 1 leanings live in its evaluators —
    so the whole construction is exposed as one `BuildAi` factory rather than pretending the policy class is
    per-generation.
- [x] **Stage 1b — `EncounterFactory`'s generation-awareness** ✅ DONE (2026-07-29). `IStatCalculator` threaded:
  `EncounterFactory.BuildCreature` now calls `profile.BuildStatCalculator(rng)` instead of hardcoding
  `new Gen1StatCalculator(rng)`. Profile passed to all 4 `BuildCreature` callers and threaded through the public
  entry points `CreatePlayerSetupAsync`, `CreateEnemyAsync`, `BuildDraftSupplier`, `BuildBossCatchSupplier` — all
  **required, never defaulted** (a `?? Gen1…` default would reintroduce the silent-fallback hazard the feature
  exists to remove).
  `EncounterFactory.ActiveGeneration` (the hardcoded `private const int = 1`) is **deleted**; its 6
  learnset/evolution DB queries now filter on `(int)profile.Generation` — this was the repo's most concrete
  "Gen 1 by assumption" and a second source of truth for the generation. `ResolvePlayerEvolutionAsync` now takes
  the whole `GenerationProfile` instead of a bare `IEvolutionRules`, so the generation used to QUERY edges and
  the rules used to JUDGE them can never disagree.
  The duplicate `PlayerOverviewDto.ActiveGeneration = 1` const is also deleted: `From(Creature, Generation)` now
  stamps the run's real generation, backed by a new `ActiveBattle.Generation` field (carried from the claimed
  `PendingSession`) and `GameSessionManager.GetGeneration(gameId)`, which returns null (→ 404) rather than
  defaulting to Gen 1 for an unknown run.
  **Falsification leg (Stage 5's standing requirement):** `TestAltProfile.BuildStatCalculator` previously returned
  `new Gen1StatCalculator(rng)`, making it useless as a probe — threading the profile and forgetting to thread it
  produced identical creatures. It now returns an `AltStatCalculator` stamping a sentinel DV of 99 (outside Gen 1's
  0–15 range) on every stat, exposed as `TestAltProfile.SentinelDv`.
  Covered by `EncounterFactoryGenerationProfileTests` (8 tests: **all four** `BuildCreature` callers probed —
  player, enemy, themed draft, boss catch — plus 2 data-filter probes over a
  `Gen1Profile.Instance with { Generation = (Generation)2 }` profile the DB has no rows for, and 2 controls
  proving the probes aren't vacuous); verified restoring both hardcodes fails exactly the probes while both
  controls still pass. The two REST-side legs the encounter probes can't reach are pinned separately:
  `PlayerOverviewDtoTests.From_StampsTheRunsGeneration_NotAHardcodedGen1` (the DTO's generation stamp, asserted
  with a non-Gen-1 value so re-hardcoding `1` cannot stay green) and `GenerationProfileTests`'
  `GetGeneration_*` pair (the `RegisterSession` → session → REST read chain, incl. null-not-Gen-1 for an
  unknown run).
  *(This absorbed what Stage 2 scoped as "where content filtering is asked for" — `ActiveGeneration` already
  was that filter, so wiring it here was cheaper than inventing a parallel socket.)*
- [x] **Stage 2a — the type roster** ✅ DONE (2026-07-30). `GenerationProfile.TypeRoster`
  (`required IReadOnlySet<DamageType>`) states **which types exist in this generation**; `Gen1Profile` supplies
  the 15 in `DamageType` declaration order, so diffing it against the enum shows exactly the three later
  arrivals missing. `DamageType` itself keeps all 18 and stays gen-blind — it is a vocabulary, not a claim.
  The consumer is the region-content invariant, promoted to production code: **`Biomes.UnhomedTypes(region,
  roster)`** + `Biomes.HomedTypes(region)` *(as-built note: Stage 3 re-signatured both to take a biome roster —
  `UnhomedTypes(biomes, roster)` / `HomedTypes(biomes)` — see the Stage 3 entry)*. The roster is a **parameter,
  not a constant** — that is the upward
  compatibility, since a 17-type generation must re-derive "every type is homed" rather than inherit Gen 1's
  answer (`ENCOUNTER_DESIGN.md §2.3`). `BiomeTests`' own hardcoded 15-type array is **deleted** in favour of
  `Gen1Profile.Instance.TypeRoster` — it was a second source of truth for the roster, the same hazard Stage 1b
  removed with `EncounterFactory.ActiveGeneration`.
  **Falsification leg:** `TestAltProfile.TypeRoster` = Gen 1's 15 **plus Dark and Steel**, built by adding to
  Gen 1's set so the two can't drift apart for reasons unrelated to the probe. Kanto homes neither, so
  `UnhomedTypes_IsMeasuredAgainstTheProfilesRoster_NotAFixedGen1List` pins that exactly `[Steel, Dark]` comes
  back. **Verified by sabotage:** re-hardcoding Gen 1's roster inside `UnhomedTypes` fails that test alone while
  the other 25 biome tests (incl. `Kanto_HomesEveryGen1Type`) stay green. Also
  `Gen1Profile_RostersThe15Gen1Types_AndNoneOfTheLaterArrivals`, which names the three absences rather than only
  counting to 15 (a count alone would survive swapping Fairy in for Ghost).
  **Deliberately unchanged, and corrected mid-review:** the client has **three** per-type tables, and only
  `TypeBadge.tsx` (18 colours) is a real gen-blind vocabulary. `bossTrainer.ts`'s `NAMES_BY_TYPE` and
  `mapGlyphs.tsx`'s `TYPE_ICON` each hold **15** — a second and third copy of Gen 1's roster, i.e. the very hazard
  this stage deleted from `BiomeTests`, still standing on the client. (`requirements-review` caught this; the
  write-up had claimed all three "keep every type". The wrong claim is kept visible in `GENERATION_PROFILE.md`
  §5(a) rather than deleted.) **Handed to Stage 4, not waived** (user, 2026-07-30): wiring them needs the client
  to *hold* the roster, which needs §7.2's generation channel — Stage 4's own work. Both degrade gracefully today
  (generic name / `t-Normal` glyph), so it is a single-source-of-truth fix, not a bug fix. Tracked in
  `GENERATION_PROFILE.md` §7.2's scope note. **Honest scope:** no *runtime* decision reads the roster yet — the
  encounter pool and biome map are gated on content, which is 2b and Stage 3; the invariant is enforced by a unit
  test, not by anything a content author editing `Biomes.Kanto` would hit (user-accepted 2026-07-30).
- [x] **Stage 2b — species / move / item content filtering** ✅ DONE (2026-07-30). `IContentScope` —
  `Species` / `Moves` / `Items`, each `IQueryable<T> → IQueryable<T>` — is now a profile slice
  (`GenerationProfile.ContentScope`), with `Gen1ContentScope` as the **documented no-op stub** of
  `GENERATION_SEAMS.md §5.0`: every accessor returns its query untouched. Its doc names the exact fix it is a
  placeholder for (`all.Where(x => x.GenerationIntroduced <= 1)` — `<=`, not `==`) and states plainly that the
  stub becomes **wrong** the day a second generation's rows are imported, so the schema work has one place to
  land. Those columns and their import stay in *Multi-Generation* below.
  **`IQueryable`, not a predicate:** a `Func<T,bool>` would materialise the whole table before filtering and
  need re-plumbing later; composing onto the query means the eventual `Where` is translated to SQL by EF. The
  seam is already the right shape — only the implementation is outstanding.
  **All eight catalog reads in `EncounterFactory`** go through it: starter lookup, the run's move pool, the run's
  item catalog, the biome map's species pool, the wild-encounter pool, the draft's fought pool, the boss-catch
  lookup, the evolved-form lookup. The rule is *"no unscoped catalog read in this file"* — kept even for the
  evolved-form read, where the scope is redundant (the edges are already generation-filtered), because a rule a
  reviewer checks at a glance beats a per-site judgement call and the redundancy costs nothing. Learnsets and
  evolution edges need no scope member (they carry a real `Generation` column, filtered since Stage 1b); nor does
  `PokemonGameAvailability` (keyed by species id, only ever intersected with the scoped pool).
  **A consequence, not just a socket:** `ComputePlayableBiomesAsync` was explicitly *not* generation-scoped
  before — its doc comment said so — and now is, so a generation gets **the biomes its own content can fill**.
  That is the first *runtime* decision to read content scope, and it answers Stage 2a's honest-scope caveat that
  nothing yet did.
  **Falsification leg:** `TestAltProfile.ContentScope` admits only ids ≤ 20 across all three catalogs — an id
  ceiling being deliberately unlike any real generation's rule while sharing its shape. **One probe per catalog
  read, not per method**, because Gen 1's scope is an *identity function*: a site that skipped it entirely is
  indistinguishable from one that uses it, from inside Gen 1. Each probe carries its own Gen 1 control.
  **Verified by sabotage twice:** unscoping all eight sites fails exactly the eight new probes while all eight
  Stage 1b probes stay green; unscoping *only* `ComputePlayableBiomesAsync` fails exactly one, proving the biome
  probe pins its own read and not the starter lookup that shares its entry point. Two sites needed a tighter
  purpose-built scope than the ceiling and the reasons are recorded in `GENERATION_PROFILE.md` §5 (the biome map:
  ids 1–20 still fill more than `RunBiomeMapSize` biomes — measured; the evolved form: every Gen 1 line starting
  under id 20 also ends under it). Adding the slice also **broke a Stage 1b probe** whose boss species (Gyarados,
  130) the new scope filters out — fixed to an in-scope species, and worth expecting from each future slice.
  **Handed to Stage 3, not waived:** `SpeciesController.GetAll` still serves the unscoped dex. It is the one
  species read on no run path — it answers *before* a run exists, so there is no profile to ask — and it is
  exactly the starter picker Stage 3 makes server-authoritative. Ratified by the user 2026-07-30, along with the
  decision to keep `ComputePlayableBiomesAsync` scoped (i.e. *"a biome no in-scope species can fill is not
  playable"* is the intended cross-generation invariant, not an over-reach of a stubs-only stage).
  **The stub's premise was false, and was fixed rather than reworded** (`requirements-review` finding, user's call
  2026-07-30): `Gen1ContentScope`'s identity is justified by "the catalogs hold one generation's content", but
  `items.db` held **Max Revive**, a Gen-2 item imported as forward scaffolding and kept from players by a
  name-matched hold-out in `RewardCalculator.UsableItems` — so the seam was resting on a second, unrelated
  mechanism. The item is now **out of the import roster and out of `items.db`**, and the hold-out is **deleted**;
  eligibility there is categorical again, with a test pinning that no name-based filter returns. Max Revive comes
  back through the per-generation item schema — see the new *Per-generation ITEM data* item under
  *Multi-Generation* below, which is the scaffolding the user asked for in its place. Rule established: *the
  scaffolding a future generation needs is the schema, not a stray row.*
  **⚖️ WAIVED (user, 2026-07-30) — no test pins `items.db`'s actual contents.** `pr-review` raised it as a
  blocker: `ItemImport` is upsert-only, so it never deletes a row for a slug dropped from the allowlist, and a
  developer re-importing over a pre-2026-07-30 `items.db` would keep Max Revive in the catalog — where, with the
  `RewardCalculator` hold-out now gone, it would actually drop and stock. The new guard asserts the C# roster,
  not the table, so nothing in the suite would object. **Waived because production cannot ship it:** the
  Dockerfile copies the committed `items.db`, which is clean (28 rows, `revive`/50 only). The proposed fix, if
  this is ever revisited, is a live-db contract test asserting the `Items` name set equals
  `ItemMapper.Gen1BattleItemNames` exactly (both 28), mirroring `PokemonEvolutionDataContractTests` — ~15 lines.
  **Do not re-raise as a new finding.**
- [x] **Stage 3 — region, biomes, starters onto the profile** ✅ DONE (2026-07-31). Two new profile slices:
  `GenerationProfile.Region` (**identity/presentation only, never branched on** — the `Generation` sibling, kept
  for logging and Stage 4's client echo) and `GenerationProfile.BiomeRoster` (the consumed content;
  `Gen1Profile` reads it through **`Biomes.For(Region.Kanto)` — still the one door** to the authored registry).
  **The roster, not the enum, is the falsifiable slice** — `Region` has a single member, so only a substituted
  biome *list* can prove the run setup asks the profile; a coherence test pins the pair can't drift (every
  rostered biome carries the profile's region). `Biomes.HomedTypes`/`UnhomedTypes`/`Playable` now take a biome
  roster instead of a `Region`, so the coverage invariant and playability filter run against whatever roster a
  profile supplies; `EncounterFactory.ComputePlayableBiomesAsync` reads `profile.BiomeRoster` — deleting the
  repo's **last hardcoded `Region.Kanto` outside the authored registry**.
  **Starters: the design doc's premise was stale and is corrected, not implemented as written.** Nothing was
  "hardcoded client-side" — `StarterSelection.tsx` has always fetched the full dex from `/api/species` and any
  species is pickable (deliberate roguelite design, unchanged). What "server-authoritative starter roster"
  actually meant here: `SpeciesController.GetAll` (Stage 2b's handed-off unscoped read — the one species read
  that answers before a run exists) now takes `?generation=`, parses it with **the same boundary contract as
  game start** (`GameController.ParseGeneration` — a stale client that sends nothing still gets the Gen 1 dex),
  and serves the profile's `ContentScope`-scoped dex via a named `SpeciesSummaryDto` (wire-verified live:
  byte-identical camelCase shape, 151 rows, `?generation=one` parses). So which starters are offerable is now
  decided server-side by the profile — there is no curated per-gen starter subset, and introducing one would be
  a *new design decision*, not part of this stage.
  **Falsification legs, verified by sabotage twice:** `TestAltProfile.BiomeRoster` = a connected 2-biome fake
  region (themes pickable from the probe's own constraints — fillable by wild species with ids ≤ 20) —
  deliberately **below `RunBiomeMapSize`**, so the run-map probe simultaneously pins §6's watch note that a
  roster thinner than the map cap yields itself rather than breaking map generation. Re-hardcoding Kanto in
  `ComputePlayableBiomesAsync` fails exactly the new run-map probe (43 others green); unscoping the dex read
  fails exactly the `DexFor` probe (18 others green). Zero importer/DB change; client untouched (Stage 4 sends
  the generation when a picker exists).
  **Riders (both filed 2026-07-31, both scheduled for "when Stage 3 touches the file"):** the 5-site learnset
  query duplication collapsed into `EncounterFactory.LoadLearnsetsAsync` (one home for the generation-filtered
  learnset read), and `GenerationProfiles.Registered` no longer allocates per call (materialised once,
  declared below `ByGeneration` per the static-init order trap `Gen1Profile.Gen1Types` documents).
- [ ] **Stage 4 — presentation: per-generation UI + the Town Map.** `/plan` **v2 done (2026-07-31)** —
  supersedes the 2026-07-29 sketch; full design in `GENERATION_PROFILE.md` §7 (decisions 7–9 in its §1). The
  user's reframing: a **complete per-gen visual overhaul where the bones stay the same** — same usability, same
  idea per surface, but each generation adapts each surface to its own idiom (surface-level functionality may
  vary only as an explicitly ratified per-surface decision; the run layer stays invariant) — settled
  **jointly, one surface at a time**, not in one pass. Plus: the region map becomes a **rigid grid Town Map**
  (RBY-style — biome squares on an authored grid, authored orthogonal route cell-paths, blinking cursor),
  grid-for-all-generations with a per-gen map-presentation seam. Staged build:
  - [x] **4a — generation channel + client presentation registry** ✅ DONE (2026-07-31). The echo carrier
    (§7.6's open decision) is a new **`RunPresentationRevealed(Generation, TypeRoster)`** event emitted by the
    **session layer** on *every* hub attach — first connect (leads the run's events, before the run task
    starts) and the reconnect rebind branch alike — built by the pure
    `GameSessionManager.BuildPresentationEvent(profile)` (internal, like `BuildRunOptions`, so the
    roster-off-the-profile read is pinnable). Client: `src/generations/presentation.ts` — the registry
    (`presentationFor`, boundary-contract fallback to Gen 1 mirroring `ParseGeneration`),
    `applyGenerationTheme` (`data-generation` on the document root; default stamped at boot in `main.tsx`,
    re-stamped by `BattleScreen` from echo-then-route-state), and the roster-coverage check
    (`missingTypeAssets`/`warnOnMissingTypeAssets`). `StarterSelection` sends `generation` in the start body +
    route state (constant `'One'` until the 4d+ picker). The two 15-type tables are re-framed as **asset
    inventories, not roster claims** — `bossTrainer.hasBossNamePool` + `mapGlyphs.hasTypeIcon` feed the
    coverage check, which measures them against the *delivered* roster (the Stage 2a handoff closed: the
    roster is now single-sourced from the profile via the wire; a rostered type without assets degrades
    gracefully and warns). Wire: `RunPresentation` timeline arm (**control-plane `now`**, so theming never
    queues behind a mid-flight animation on reconnect) + `battleReducer` `generation`/`typeRoster` state +
    the auto field guard. Falsification legs: `BuildPresentationEvent_RosterComesOffTheProfile` (TestAltProfile
    → 17 incl. Dark/Steel), and Vitest's alt-registry + alt-roster probes (`presentation.test.ts` — registry
    param proves the flow is data-driven; the 17-type roster surfaces exactly `[Dark, Steel]` as gaps).
    **Verified live** (hub script, both paths): first attach leads with the echo (`One`, the 15), a
    detach/re-attach re-echoes it; `data-generation="gen1"` present in the booted app.
    **`requirements-review` (2026-07-31): 3 findings, adjudicated by the user — 2 fixed, 1 waived.**
    (1) *Fixed:* `GAME_LOOP.md` §5 now documents the new **session-layer event category** this created —
    `RunPresentationRevealed` is emitted per *attach* by `GameSessionManager`, outside the loop's
    same-seed-same-sequence guarantee, with the category's rules (presentation-only + idempotent, else it
    belongs in an `IRunEvent`). (2) *Fixed:* the echo's timing claims are now pinned by
    `AttachConnection_EchoesThePresentation_OnFirstAttach_AndAgainOnReconnect` — a recording `IHubContext`
    + a gate-blocked DB factory park the run task deterministically, asserting echo-leads-the-stream on
    first attach and re-echo-to-the-new-connection (not the old) on reconnect; `AttachConnection`'s first
    automated coverage. (3) **⚖️ WAIVED (user, 2026-07-31):** `StarterSelection` seeds the player's choice
    from `presentation.ts`'s `DEFAULT_GENERATION` (the absent-data fallback constant) — conceptually two
    roles in one constant, accepted as the interim placeholder; the 4d+ generation picker replaces the line
    wholesale. Do not re-raise.
    **`pr-review` (2026-07-31): CHANGES-REQUESTED → all three recommended fixes applied (user's call), now
    PR-ready.** (1) the **registry-drift guard** — `WebEventContractTests.EveryRegisteredGeneration_
    HasAClientPresentationEntry` asserts every `GenerationProfiles.Registered` member has an `id: '<Name>'`
    entry in `presentation.ts` (without it, a future generation's runs would silently theme as Gen 1 with
    every suite green — the generation-leg sibling of the timeline-arm guard); (2) **the theme un-stamps on
    unmount** — `BattleScreen` resets `data-generation` to the default when leaving the run, so `main.tsx`'s
    pre-run-screens-start-default invariant holds on in-SPA navigation, not just cold boot (invisible until
    4b's per-gen CSS, cheap now); (3) **one emitter per run** — `ActiveBattle.Emitter` is set at claim and
    the reconnect re-echo reuses it instead of constructing a second `SignalRBattleEventEmitter` (identical
    today; diverges silently the moment the emitter gains state). Three advisories deferred (unguarded
    test-only registry fallback; `as string` vs `?? []` asymmetry in the timeline arm; dual
    generation encodings — REST numeric vs wire name — to consolidate when 4b/4d touches either).
  - [x] **4b — the Gen 1 skin (2026-08-04).** ✅ The `[data-generation="gen1"]` token override block ("Kanto
    Sage" — `GENERATION_PROFILE.md` §7.3 / §1 decision 10) is built and verified live (Puppeteer, a full run
    through Title → StarterSelection → route choice → battle → CHECK POKEMON → Settings).
    - **The five ratified battle-HUD chunks** (the original mockup's scope): nameplates, HP/XP bars, the
      battle log (dialogue box, double-line chrome), the 2×2 command grid, move-select — plain-bold-border
      resting state and invert-block hover/focus, both confirmed. STAB/type/effectiveness/power-tier pills
      stay their existing functional colours on purpose, same call as the HP high/mid thresholds — none of
      those are decoration, so the four-colour budget doesn't apply to them.
    - **Extended the same day, per the user's direction ("apply to all basic views/frames"):** Title Screen,
      StarterSelection, Settings (screen + in-battle modal + panel), CHECK POKEMON, and the route-choice
      modal's outer frame ("biome select") — reskinned in full, background through generic button chrome.
      Two invisible-text bugs caught and fixed during verification (`.overview-title`, the INFO tab's field
      values inheriting the old near-white default) — the fix pattern used throughout: give each new root
      surface its own `color: ink` so anything not individually patched still inherits correctly, rather than
      chasing every descendant selector by hand.
    - **Deliberately NOT touched** — the "detailed" layer, left for each surface's own future catalog turn
      (§7.5): BAG's item list, the run map's own node/territory/edge content and the full-screen pinned map,
      reward/shop/acquire/recovery/battle-end modals' literal thematic accent colours (their backgrounds
      stayed dark on purpose — those colours were tuned against the old dark background and a partial flip
      would have broken contrast), the party strip, drop-toasts, the node ladder.
    - The "picker live-preview" phrase from the original line is moot today — there's only one registered
      generation, so there's nothing yet to pick between; revisit once a second generation exists.
  - [x] **Kanto Sage — ornamental detail pass** ✅ DONE (2026-08-05, raised 2026-08-04). The shipped skin (4b)
    was deliberately flat and restrained — ink-on-neutral, no texture, no ornament. This added one small layer
    on top, not a repaint: a corner glyph on the double-line window chrome, plus a subtle grain texture on the
    flat fields. Sketched and ratified as a live interactive mockup (decision 8's process, same as 4b's own
    mockup) offering 4 corner-motif candidates (Step Notch / Filled Pip / Cross Tick / Bracket Hook) and 4
    field-texture candidates (Ordered Dither / Diagonal Hatch / Grain / Stipple) side by side against the real
    frame recipe. **Ratified: Step Notch + Grain.**
    - **Corner artifacts** — `.battle-screen`, `.battle-log`, `.route-choice-modal` (§7.3's three double-line-
      frame surfaces) each get a small ink staircase-notch glyph near each corner, via a new `--ks-corners`
      token (four tiny inline-SVG data URIs, one per orientation) in `index.css`. **Built inset 8px from the
      edge, not straddling the border like the mockup** — the frame's own inset box-shadow ring (`inset 0 0 0
      3px fill, inset 0 0 0 8px ink`) paints *on top of* the background, so a motif flush at the corner would
      sit mostly underneath it and barely show; inset 8px clears the ring instead. A DOM-based ornament could
      have straddled the border the way the mockup did, but all three surfaces are `overflow: hidden` or
      `overflow-y: auto`, which would clip anything poking past the edge anyway — background-image was the
      right call independent of the ring issue.
    - **Field texture** — a new `--ks-grain` token (9 low-alpha `radial-gradient` dots, tiled 34px) on `body`
      (the Fog field), `.btn` (shared chrome across Title/StarterSelection/Settings), and the battle HUD's own
      white boxes (`.battle-panel`, `.nameplate`, `.action-btn`, `.move-btn`/`.move-btn--stab`). **Deliberately
      plain `background-image` on both additions, never `::before`/`::after`:** `.battle-log` and
      `.route-choice-modal` are `overflow-y: auto`, and a pseudo-element there would be swept into the box's
      own scrolled content and visibly drift out of view as it scrolls — a box's own background never does,
      regardless of how far its content is scrolled.
    - **Real trap hit and fixed during the sketch, not the build:** the first mockup pass generated the
      dither/grain/stipple textures on a `<canvas>` via JS at load and injected the result as a data-URI
      `background-image`; only the diagonal hatch was plain CSS. The user could see the hatch faintly but none
      of the other three — the canvas-drawing script was silently failing in the hosted artifact context
      before paint. Rewritten as pure static CSS gradients (a checkerboard for dither, layered
      `radial-gradient`s for grain/stipple) with zero JS/canvas dependency, which is also why the *shipped*
      `--ks-grain` token is a plain gradient list rather than a generated asset.
    - **Deliberately out of scope this pass:** StarterSelection's own bespoke white boxes, Settings'
      panels/modal, and CHECK POKEMON — grain landed on the shared `.btn` chrome and the battle HUD only, not
      every individual white-box selector those files declare. Left for whenever those surfaces get their own
      catalog turn (§7.5), same as 4b's own "detailed layer" carve-outs. `.route-choice-modal`'s unconditional
      `border-radius: 12px` (no gen1 override) is a pre-existing Stage 4b gap, not introduced here — the square
      corner motif may touch that curve; not fixed in this pass.
    - Verified live in-browser by the user. Puppeteer was used only during the sketch/ratify mockup phase (and
      to diagnose the canvas-rendering trap above); dropped for the actual app build and the final visual
      check per the user's mid-session call that it was burning too many tokens for this kind of iteration.
  - [x] **4c — the Town Map** ✅ DONE (2026-08-18): `RegionMapRevealed` wire update (+ field guards), client
    grid renderer replacing the painterly `RegionMap` (interaction contracts unchanged; `travelledEdgeKeys`
    survives); `TestAltProfile`'s fake region gets grid geometry.
    **The grid structure itself is locked (2026-08-06)** — one biome per grid cell, orthogonal routes,
    identity-on-hover — from a multi-round sketch → ratify pass; see `GENERATION_PROFILE.md` §7.4's
    sketch-ratify record for the full history (route/cursor style, the Boss-gated island size, decision 11's
    "no organic curves" rule). Tile art is also locked (2026-08-18) — Kenney's "Monochrome RPG" (CC0), vendored
    static asset, 4-colour recolour onto the existing `--ks-*` tokens; see §7.4 decision 12 (visually verify
    every tile pick against the real source before use — the standing rule that pass established) and the
    locked tile-pick list.
    **Layout is procedurally generated per run, not hand-authored (revised 2026-08-18, user's call)** —
    supersedes the original "authored Kanto grid" plan; see §7.4's revision note for the full rationale
    (a fresh sparse per-run island is a smaller problem than laying out the whole dense 18-biome registry, so
    full procedural generation is back in scope where it was rejected before). **Built backend-first, all four
    steps shipped 2026-08-18:**
    1. [x] **`IslandLayoutGenerator`** ✅ DONE (2026-08-18) — pure, deterministic (seeded from the run's own
       `IRandomSource`, no separate seed), takes the run's already-chosen biome subgraph (from the existing
       `Biomes.RandomConnectedMap`, untouched) and produces grid coords + orthogonal collision-free routes.
       Two-phase: a fast primary BFS placement + a real local-backtracking router (undo-and-swap the most
       recently committed edge when one gets stuck, rather than restarting with a different global order);
       a fallback placement (exhaustive ring search, provably can't itself fail to find a free cell) retried
       across a few spacing levels and reshuffled attempts when primary doesn't pan out. Fuzz-tested
       (`IslandLayoutGeneratorTests`) against the real `Biomes.Kanto` registry across sizes 2–12 × 15 seeds
       (pre-commit-hook-fast, ~2s) for the validity invariants (no overlaps, axis-aligned only, every edge
       routed, fallback itself valid, reproducible from seed). **Three real bugs found and fixed during
       build, not just tuning** — kept as design notes in the source since they're the reason the final
       shape looks the way it does: (1) a ring-search that always scanned from the same corner silently
       recreated the exact diagonal-clustering pathology it replaced, on any open BFS-chain placement; (2) a
       routing search margin that was a fixed constant instead of scaling with the placement's own spread,
       so a genuinely sparse/planar graph could still fail to route once nodes were spread out; (3) greedy
       sequential routing with no backtracking is inherently order-dependent — trying several static global
       orderings worked but didn't scale, real local backtracking did. No wire/DB/client touched — pure unit
       tests, no database.
    2. [x] **Wire into `RunDirector`** ✅ DONE (2026-08-18) — computed exactly once per island, at biome-mode run
       start (right where `RunAsync` already emits `BuildRegionMap()`), via
       `IslandLayoutGenerator.Generate(_playableBiomes, _rng ?? SystemRandomSource.Instance)` — the same shared
       `IRandomSource` every other per-run roll draws from, never a fresh one — and cached on
       `RunState.IslandLayout` (a settable property, like `CurrentBiome`; `RunDirector` exposes a forwarding
       `internal IslandLayout?` test seam) for step 3 to read when it builds the wire payload. Covered by
       `RunDirectorIslandLayoutTests` (matches a bare `Generate` call off the same seed; caches the whole
       playable set even when the run only ever visits one biome; stays `null` outside biome mode).
       **`requirements-review` (2026-08-18) caught it living on the wrong object at first** — it originally
       lived on a private `RunDirector` field, but §7.4's ratified plan says "cached on `RunState`," and
       `RunDirector` owns non-serializable collaborators (DB-backed suppliers, the emitter) that make it the
       wrong home regardless of the doc: the **user's framing settled it** — a save/resume layer would snapshot
       `RunState`, not `RunDirector`, and a future **multiple-islands-per-run** feature (confirmed as a
       "definite" future feature) would need to *reassign* the layout at an island boundary exactly the way
       `CurrentBiome` is already reassigned at a biome boundary — both are `RunState`-shaped needs. Moved;
       `RunState`'s class doc gained a note explaining why this presentation-ish field rides along despite
       `chooseNextEvent` never reading it (precedented by `BattlesWon`/`RunDepth`, which already double as
       run-summary data). Zero doc changes needed — `GENERATION_PROFILE.md` §7.4 already said `RunState`; the
       code just didn't match it yet. Full suite re-verified green after the move (1493/1493, unchanged — the
       `RunDirector` forwarding property kept every existing test compiling untouched).
       **Real gap found and fixed, not just wired:** the full test suite turned up 4 failures in
       `RunDirectorBiomeTests` — they hand `RunDirector` the **un-sampled, full 18-biome `Biomes.Kanto`
       registry** as `PlayableBiomes` directly (to test the 3-of-N route-offer sample, nothing to do with the
       grid), which is bigger than `IslandLayoutGenerator`'s fuzz-validated range (sizes 2–12, matched to
       `EncounterFactory.RunBiomeMapSize` = 10, the only real caller's actual ceiling). On the full registry —
       whose several 3-cycles (e.g. meadow-trail↔whispering-woods↔bramble-thicket) add "cross" edges a
       spanning-tree-shaped layout doesn't have to fight — the generator isn't just occasionally unlucky: one
       seed exhausted every fallback level and threw, and even the seeds that *succeeded* took 40–49 seconds
       each. **User's call (2026-08-18):** fix the tests to match how `RunDirector` is actually ever called in
       production — `Biomes.RandomConnectedMap(Biomes.Kanto, EncounterFactory.RunBiomeMapSize, source)` off a
       single shared source (mirroring `EncounterFactory.CreatePlayerSetupAsync`), same as every real caller —
       rather than hardening the generator for a size no real run produces. All 4 tests pass fast (~1s total)
       against the sampled subset; full suite green (1492/1492, 3s). **Follow-up, not blocking:** if a future
       generation's biome roster grows past ~12, or `RunBiomeMapSize` is ever raised, `IslandLayoutGenerator`
       will need hardening for that range first — the scale cliff is real, just currently unreachable from any
       actual caller. Not scheduled; revisit if either precondition changes.
    3. [x] **`RegionMapRevealed` wire update** ✅ DONE (2026-08-18) — `RegionMapRevealed` now carries `Width`/
       `Height` (the grid canvas) and `Routes` (`IslandRoute`, reused as-is from `IslandLayoutGenerator` rather
       than duplicated into a wire-only type); `RegionMapBiome.MapX`/`MapY` (authored 0–100) are replaced by
       `X`/`Y` (the procedurally laid-out grid cell). `RunDirector.BuildRegionMap()` reads `_state.IslandLayout`
       (step 2, guaranteed non-null — it's only ever called immediately after `_state.IslandLayout` is computed)
       instead of the old authored `BiomeDefinition.MapX/MapY`. `SignalRBattleEventEmitter`'s projection updated
       to match. Covered by: the mechanical reflection guard (`EveryBattleEventProjectsAllOfItsFields`, no changes
       needed — it already probes list-typed fields with a real nested instance, so it caught the shape
       automatically); the hand-pinned value-level wire test, renamed and rewritten for the new fields
       (`RegionMapRevealed_Projection_CarriesBiomeSubFieldsGridCoordsAndRoutes` — grid `Width`/`Height`, a
       biome's `X`/`Y`, and a route's cell path survive in order); `RunDirectorNodeTests`' existing emission
       test extended to assert the *real* `BuildRegionMap()` output (in-bounds grid positions, a real route) not
       just the record shape; and the Stage 5 falsification leg
       (`RunDirectorIslandLayoutTests.BiomeMode_RegionMapRevealed_LaysOutRealGridGeometry_ForAnyProfilesBiomeRoster`)
       — runs `TestAltProfile`'s two-biome fake region (deliberately not Kanto-shaped) through the real
       `RunDirector` → `IslandLayoutGenerator` → `BuildRegionMap` → `RegionMapRevealed` pipeline and asserts
       genuine grid geometry, proving the Town Map is generation-agnostic rather than hardcoded to Kanto's
       shape. Full suite green (1493/1493 .NET, `tsc` clean, 199/199 Vitest).
       **The interim client breakage this step deliberately left open was resolved the same day — see step 4.**
    4. [x] **Client grid renderer, wired to the locked tile art** ✅ DONE (2026-08-18) — the real Kenney art,
       recovered byte-verified from the `Town Map — tileset ratification sketch` mockup artifact (decision 12
       re-confirmed against the freshly re-downloaded source sheet, not trusted from memory) rather than
       re-picked from scratch: three tree species (#14/#15/#16), a rock cluster (#30), a boulder (#105), a
       signpost (#67), the town marker (#109 shuttered/unvisited, #110 open-door/visited-or-current), and the
       coastline edge trim (#2, 4 pre-rotated copies) — all vendored as `--ks-tm-*` data-URI custom properties
       in `index.css` (the same inline-asset convention as `--ks-corners`/`--ks-grain`, not separate files),
       plus a new `--ks-grain-water` (the existing grain recipe, tones inverted). Land/water themselves are
       **not** tileset art — they reuse the app's own `--ks-grain` procedural texture (free, already proven).
       `timeline.ts`'s `RegionBiome`/new `RegionRoute`/`RegionRouteCell` types + the `RegionMapRevealed` case
       arm, and `battleReducer.ts`'s state, now carry the grid shape (`regionWidth`/`regionHeight`/
       `regionRoutes` alongside `regionBiomes`) instead of the old percent coords.
       **A real algorithm/design gap found and resolved before the render work, not papered over:**
       `IslandLayoutGenerator` (step 1) only ever produces a *sparse* graph — a cell per biome plus a thin
       one-cell corridor per route, nothing else — but decision 11 calls for an actual landmass with a
       coastline and scattered terrain, not a bare path over open water. Visualized several real generated
       layouts before building anything (`Biomes.RandomConnectedMap` → `IslandLayoutGenerator.Generate` on
       real seeds) and confirmed the gap is real — e.g. a 10-biome island left genuine blank gaps inside its
       own bounding box. **User's call:** synthesize the fuller landmass **client-side at render time** (an
       8-directional dilation of the sparse core by `TOWN_MAP_DILATION` = 1 cell, tunable), not in the backend
       — zero wire/DB change, stays inside step 4's own scope. New pure module `townMapLayout.ts`
       (`coreLandCells`/`dilateLand`/`coastSides`/`scatterFor`, 14 Vitest cases) does the derivation; the
       component only renders.
       **A real bug found via manual verification, not just code review:** offered/choosable towns used the
       HTML `disabled` attribute to block clicking on non-offered ones — but a `disabled` button suppresses
       `mouseenter`/`focus` in every browser, so hovering *any* non-offered town silently did nothing,
       breaking the ratified "hover or focus any tile to see its name" behaviour. Caught live in the running
       dev app (Puppeteer), not by a test. Fixed with `tabIndex={choosable ? 0 : -1}` instead of `disabled`
       (mouse hover always fires; only actionable towns are keyboard-tab-stops; `onClick` was already
       conditionally undefined on a non-offered town, so removing `disabled` doesn't make it clickable).
       **Verified live** (Puppeteer, `.\dev.ps1`, full flow): the grid renders — land/water grain, coastline
       trim on every land/water boundary, scatter props, dotted untravelled / solid travelled routes, the
       shuttered→open-door marker transition on entering a biome, the bouncing chevron over the current
       biome, the hover/focus caption (defaulting to the current biome, updating on hover of *any* town, own
       bug above included) — across the pinned full-screen `RunMapPanel` and the blocking `RouteChoiceMap`
       modal, and at a narrow (480px) viewport with no overflow (the grid's percentage/aspect-ratio-based
       sizing needed no responsive breakpoint, unlike the old fixed-rem waypoint discs it replaced).
       **Left for the eventual `BiomeDefinition.MapX`/`MapY` cleanup, not done here:** removing those now
       fully-vestigial fields and updating `BiomeTests.cs`'s pinning test — a real but small, low-priority
       tidy-up (§7.4 already flagged it in step 3's note); not scheduled.
       **`pr-review` (2026-08-18): CHANGES-REQUESTED → both blockers + all 7 recommended fixes applied
       (user's call), now PR-ready.** Blockers: (1) the E2E suite's DOM contract was renamed out from under
       it — `.region-node`/`.region-node--offered`/`.region-node--current`/`.region-edge` no longer exist;
       fixed across `e2e/helpers.ts` + 3 spec files to the new `.town-map-town`/`.town-map-town--offered`/
       `.town-map-route` classes, plus a new dedicated `town-map-town--current` class (parity with the old
       markup, not just an `aria-current` query) — **not run** (E2E is user-only per policy; recommend
       `.\e2e.ps1 -Spec encounter-map` to confirm). (2) the hover/focus caption was near-unreadable in the
       pinned full-screen Run Map — its `--ks-dim`/`--ks-ink` tokens are parchment-surface colours, but that
       panel's own ground was still the old dark "deep-night" theme (then deliberately unskinned; skinned
       2026-10-01); confirmed empirically (`getComputedStyle` + a fresh screenshot showed genuine dark-on-dark, not
       a false positive) and fixed with a `.map-overworld`-scoped light-on-dark override. Recommended fixes:
       a real logic bug where `biomeCaptionStatus` mislabelled an already-visited-but-still-offered biome as
       "offered, unvisited" on nearly every route choice after the first (every neighbour stays offered with
       no visited filter — `BiomeChoiceEvent.PickOptions`) — fixed the priority order and moved the helper
       into `townMapLayout.ts` with 5 new pinned cases; the read-only `RunMapPanel` made every town
       `tabIndex={-1}` so a keyboard user couldn't reach any biome name — fixed to `tabIndex={onChoose ?
       (choosable ? 0 : -1) : 0}`; non-offered towns in the choice modal had no `aria-disabled`; the map
       legend still showed gold swatches for the new black-ink map; the route-choice modal's height-capped
       stage sizing had the *same* "100%/100% background-size stretches non-square cells" defect class the
       grain-tile fix was written to avoid, just reached via `width:100%` + independent `max-height` fighting
       each other — fixed to bound both dimensions via `max-width`/`max-height` with neither force-set; a
       missing `prefers-reduced-motion` guard on the marker hover-scale transition; and an unmemoized
       land/dilation recompute on every hover (`useMemo`, kept above the early-return per rules of hooks).
       Full suite re-verified green after all fixes (1493/1493 .NET, `tsc` clean, 218/218 Vitest — the prior
       213 + 5 new `biomeCaptionStatus` cases); the caption/legend fix re-verified live via Puppeteer
       (`getComputedStyle` before/after + screenshots).
       **Manual bugfixing pass (2026-08-19), same day, playing the actual shipped feature.** Five more real
       findings, each verified live via Puppeteer against the exact seed that exposed it (not just reasoned
       about statically) — full design rationale for each in `GENERATION_PROFILE.md` §7.4's own record of this
       pass; summary here:
       1. **Stage sizing/centring was genuinely broken, not a Firefox-only quirk** — an `aspect-ratio` box with
          both dimensions `auto`, sized as a flex item, doesn't reliably resolve in any engine (one real seed
          collapsed the stage to ~8px). Fixed with a definite computed width (`min(100%, 62vh × ratio)` via new
          `--tm-w`/`--tm-h` CSS custom properties `TownMapGrid` sets inline) instead of leaving both dimensions
          to the browser's own aspect-ratio auto-sizing.
       2. **Exterior water margin** — the server's own canvas margin (`IslandLayoutGenerator.Margin`, 1 cell) is
          measured against the sparse core, but the client's own dilation (step 4's original build) already
          grows that core outward first, so the rendered land could reach the canvas edge with no visible
          margin left at all. New `TOWN_MAP_RENDER_PADDING` (1 cell) adds render-only breathing room,
          independent of that interaction — wire `Width`/`Height`/positions never change.
       3. **A jagged coastline** — new `JAGGED_EDGE_SKIP_ODDS` (deterministic 1-in-6 thinning), applied only to
          cells `dilateLand` is *adding* (the dilation ring), never to a core cell already present — the
          uniform-fixed-radius dilation read as one smooth rounded-rectangle outline; a user ask, not a defect.
       4. **Interior water pockets — a real bug, found while fixing #2/#3, not requested.** The sparse graph can
          leave a gap wider than the dilation radius between two nearby-but-unconnected path segments (the
          server's own node spacing is wider than one dilation step), fully surrounded by land once dilation
          finishes — a small lake in the middle of the island. New `fillInteriorPockets` (a border-seeded
          4-directional flood fill; whatever water it never reaches gets converted to land) closes it.
       5. **A random Japanese-sounding island name** — new `islandName`, two-part compounds from real short
          Japanese words common in actual place-name compounds (Fuji+yama, Yoko+hama, Kuro+kawa, …), romanized,
          "Island" appended (e.g. "Asagawa Island"). Deterministic from the run's sorted biome-id set (no
          server seed threaded to the client, same reasoning as `scatterFor`) — stable for the whole run.
          Shown in `RouteChoiceMap`'s intro line and `RunMapPanel`'s pinned topbar.

       All five in `townMapLayout.ts` (now `dilateLand` + `fillInteriorPockets` + `islandName`, 33 Vitest cases
       total) except #1, which is CSS/component-only. Full suite re-verified green after every fix (232/232
       Vitest, `tsc` clean); #1/#4 re-verified against the exact seed that exposed them, not just re-tested in
       general.
````

### 8. Generation Profile — the BAG readability regression (done 2026-08-23)

````markdown
  - [x] **⚠️ BAG readability regression** ✅ DONE (2026-08-23) — `.bag-item`/`.bag-pp-prompt`/`.bag-gold*`
    (`BattleScreen.css`) only ever inherited the global dark-theme `--clr-text` (near-white) on a transparent
    background, unreadable against the light Kanto Sage `--ks-fog` panel ground they now sit on. Same failure
    mode as the Town Map caption bug fixed 2026-08-18 (parchment-surface tokens vs. an unskinned dark ground,
    just inverted). Fixed with `[data-generation="gen1"]` override rules for `.bag-item` (+ hover/focus-visible
    invert), `.bag-item-qty`/`.bag-item-desc`/`.bag-group-label`, `.bag-empty`, `.bag-pp-prompt`, and the
    `.bag-gold`/`.bag-gold-label`/`.bag-gold-coin`/`.bag-gold-amount` money box — the same ink-on-fill /
    invert-block pattern already used for `.action-btn`/`.move-btn`. `ReviveTargetPicker` reuses the same
    `.bag-item*` classes so it's covered automatically; `PpTargetPicker` already reused `.move-btn` and needed
    no change. Verified live via Puppeteer (screenshot before/after + hover state) in an actual battle's BAG
    menu — reads correctly, hover-inverts cleanly. CSS-only, no test changes. **The rest of the 4d+ BAG
    mini-plan (a full skin pass beyond this legibility fix) is still open** — see the surface catalog above.
````

### 9. Browser-Based UI Testing — the "Done and archived" index

````markdown
**Done and archived** (→ `TODO_ARCHIVE.md`): seed plumbing, the Run Economy reward-modal E2E, the spec-rot
recovery and the inter-test flakiness pass are all under *"Browser-Based UI Testing — seed plumbing, spec-rot
recovery & the flakiness pass"*; the between-encounter modal E2Es under *"Other between-encounter modal E2Es"*;
the In-Combat Switching UI contract in the *"In-Combat Switching"* 2026-07-26 addendum; the evolution
nameplate/action-prompt lag under *"Evolution nameplate doesn't follow until the next battle starts"* (2026-07-28);
and its sibling *"Party strip shows a stale name after an on-field evolution"* (2026-07-29) — see the one
known-still-open follow-up (the regression-insurance E2E coverage gap) below.
````

### 10. Frontend Unit Coverage — the 2026-07-05 "Done" paragraph

````markdown
**Done (2026-07-05):** extracted the pure `battleReducer` out of `useBattleHub` (`hooks/battleReducer.ts`,
type-only imports → zero runtime deps) and added `battleReducer.test.ts` — the edge transitions a live
playthrough can't deterministically force (name-mismatch HP/status no-ops, `XP_GAIN` clamp, the level-up→
move-replacement supersede, the `BATTLE_STARTED` enemy-nameplate reset, biome-choice which has no E2E spec).
Plus `format`/`fetchError` unit tests (the backend-unreachable path is invisible to E2E). 84 → 107 Vitest tests.
````

### 11. Tech Debt / Cleanup — the "Done & archived" index

````markdown
**Done & archived** — full write-ups in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) → *Tech-Debt cleanups*:

- *2026-06-20 → 22 code-review + Architecture Review #7 pass:* (A) `MoveSet` cross-thread mutation →
  lock-free copy-on-write; (B) `AttackAction.ExecuteAsync` split into `ResolveDamage` +
  `ResolvePreDamageGates`; (C) repo-wide comment-density pass; (D) minor comment/dead-field batch; the
  **RNG seam** (CLOSED — do not re-file the `AlwaysHit`/`AlwaysCrit` shim idea, the
  unseeded-web-composition-root, or "Roll\* ignores the battle seed"); and Architecture Review #7
  (`SecondaryHits` seam dedup, `MoveImport.MapToAttack` split + `MoveMappingTests`).
- *2026-07-04:* `bag.ts` re-encoded the engine's effect registry → backend-projected `UsableInBattle`.
- *2026-07-16:* **event wire contract guarded by name but not by field** → the generic
  `EveryBattleEventProjectsAllOfItsFields` (nested records + union variants). Don't re-file "add a
  field-level guard per event" — presence is now automatic; a one-off test is only for *values/semantics*.
- *2026-07-16:* **TypeScript typechecked by no gate** → `tsc --noEmit` in the pre-commit hook (on staged
  `.ts`/`.tsx`) + a `TypeScript` row in `test.ps1`; `tsconfig` now covers `e2e/` as well as `src/`
  (**keep it that way**).
- *2026-07-16:* **`RunDirector`'s 25-parameter constructor** → a `RunDirectorOptions` record (commit `7875d64`).
- *2026-07-17:* **No ESLint/Prettier in `ClientApp/`** — **decided, not deferred: the frontend stays
  deliberately un-linted and un-formatted** (user ruling). The typecheck (`tsc`) is the only frontend gate.
  Don't re-file this as tech debt; the rule now lives in `DEV_STANDARDS.md` → *Coding Conventions*.
- *2026-07-17:* **`RunDirector.cs` was 1058 lines holding 9 types** → the 6 `IRunEvent` classes + 2 resolution
  helpers split one-per-file into `Combat/RunEvents/` (which keeps `namespace creaturegame.Combat`, per the
  `Combat/Ai/` precedent); the `PlayerAttackTypes`/`CreatureTypes` duplication collapsed into `Creature.Types`.
  **`RunLoop.cs`'s ~28 types are fine** — a cohesive vocabulary file; don't let a type-count metric split it.
- *2026-07-17:* **`Creature/` and `Creatures/` both declared `namespace creaturegame.Creatures`** → the 9 files
  merged into `Creatures/`; the `Creature/` directory is gone. Pure file move (`git mv`), no code changed.
- *2026-07-17:* **csproj boilerplate copy-pasted across all four projects** → a root `Directory.Build.props`
  carrying the shared `TargetFramework`/`ImplicitUsings`/`Nullable` **plus `TreatWarningsAsErrors`** (verified
  clean first, so a new warning now fails the build). Closes the *No `Directory.Build.props`* debt below.
- *2026-07-17:* **`BattleScreen.tsx` was 1317 lines with 13 hand-rolled modal overlays** → a shared `<Modal>` with
  an explicit **`dismiss`** prop (`'blocking'` vs `{ onEscape }`) + the escape rule in one `useEscapeKey` hook; the
  8 prompts + `BattleEndedOverlay` lifted into `components/modals/`. **`BattleScreen.tsx` is now 842 lines with zero
  hand-rolled overlays.** Every run prompt is `'blocking'` **by construction, not by taste** — each parks a
  server-side await, so dismissing one would strand the run; don't re-file "the modals should close on Escape".
  The pinned map is the one escapable overlay and calls `useEscapeKey` directly (it *is* the full-screen surface,
  so it can't share the wrapper's overlay+card DOM). CSS untouched.
- *2026-07-20:* **DB services (`PokemonService`/`AttackService`/`ItemService`) skip try/catch** — **decided,
  not a gap: the convention was wrong, not the code.** They're thin EF pass-throughs with no partial state to
  clean up and nothing to do differently on failure; every real caller already wraps the whole operation at
  its actual boundary (`GameController.Start`, `GameSessionManager`'s session task) and logs there. Amended
  `CLAUDE.md` → *Coding Conventions* to "wrapped at the call boundary" instead of adding matching-but-inert
  catch blocks three layers down. Don't re-file this as a DB-services gap. This was the last open item from
  the 2026-07-19 repo-wide PR-audit; the other four findings are individually archived in `TODO_ARCHIVE.md`
  ("0× type immunity does not gate secondary effects", "Leech Seed drain borrows PoisonDamageDenominator",
  "Paralysis Speed quartering is an inline gen-variable magic number", "Haze over-resets") and a fifth
  (`SignalRInput` cancel/prompt race) was deliberately never filed — waived by the user while game state
  stays transient (memory `project_waived_cancel_race`).
````

### 12. Tech Debt / Cleanup — the 2026-07-31 review-pass note (its one remaining item is carried in the new TODO)

````markdown
**Filed 2026-07-31 from a review pass over the Generation Profile Stage 1a–2b commits** (`e603478`…`fa952e4`;
the threading discipline itself is sound — every seam explicit, no `?? Gen1…` default reintroduced, each stage
carries its `TestAltProfile` leg). Two of the three closed the same day as Stage 3 riders (full write-ups in
`TODO_ARCHIVE.md` → *Tech-Debt cleanups*): the **five-site learnset-query duplication** in `EncounterFactory`
(→ one `LoadLearnsetsAsync` home) and the **per-call `GenerationProfiles.Registered` allocation** (→ materialised
once). One remains:
````

### 13. Tech Debt / Cleanup — the closed 2026-07-19 PR-audit note

````markdown
*(The 2026-07-19 repo-wide PR-audit is now fully closed — all five findings resolved: four fixed & archived in
`TODO_ARCHIVE.md`, the DB-services try/catch convention decided above, and the `SignalRInput` cancel/prompt race
deliberately waived by the user (memory `project_waived_cancel_race`). Don't re-file "Repo-wide PR-audit
findings" as an open section.)*
````

### 14. Known Gaps — the TM/HM and poison-tick items (both resolved)

````markdown
- ~~**Player-facing TM/HM items don't exist at all**~~ — **SHIPPED 2026-09-28** as **TM/HM — Move-Teach
  Rewards**: a reward-choice move-teach card (substituting an item-reward slot, not a bag item) + an
  ABLE/NOT-ABLE "teach to a Pokémon?" party-target picker (a roguelite QoL improvement over real Gen 1, which
  only tells you afterward — not a literal reproduction), reusing the existing forget-a-move flow. The
  reward-choice design that emerged with the user dropped the item/bag/`ItemCategory` scope this write-up
  originally assumed entirely — no physical TM item exists, so Tier 2 bag persistence was never a dependency.
  Full record → `TODO_ARCHIVE.md` → *TM/HM — Move-Teach Rewards*.
- ~~**Possible bug: poison-tick timing vs. a same-turn faint**~~ — **RESOLVED 2026-09-13**: poison ticking the
  turn it's applied is correct Gen-1 behaviour (not a bug); end-of-turn residual (poison/burn/Leech Seed) still
  firing for the survivor after either side had already fainted from a direct hit that same turn **was** a real
  bug, now fixed — `Battle.cs`'s end-of-turn residual block is gated on both creatures still being alive via the
  new `IBattleRules.FaintEndsTurnImmediately` seam. Review of that fix (`requirements-review`/`pr-review`,
  2026-09-13) also found and fixed a companion bug on the same rule: Hyper Beam's recharge flag was being set
  even on a KO hit, contradicting the Hyper-Beam-no-recharge-on-KO rule already documented in
  `GEN_DIFFERENCES.md`, and reachable via forced-switch (the enemy's stale recharge flag could wrongly skip its
  next turn against the newcomer). See `TODO_ARCHIVE.md` → *End-of-turn residual (poison/burn/Leech Seed) fired
  even after a same-turn faint* for the full write-up (both bugs, the seam refactor, and the counter-tick
  split). **The one narrower question from this same investigation was answered 2026-09-20 and turned into a
  larger, still-open item** — see the next entry below.
````

### 15. Known Gaps — the endless-chain double-faint and phantom stat-cap items (both resolved)

````markdown
- ~~**Endless-chain double-faint**~~ — **RESOLVED 2026-07-28**: a mutual end-of-turn DoT double-faint now counts
  as the player's win and promotes a survivor whenever the party has a live bench member; it only remains a loss
  for a **lone** creature with nobody left to promote (`RunDirectorTests.Runner_DoubleFaintFromEndOfTurnPoison_EndsTheRun_ButStillCountsTheWin`
  — note both the class and the name, neither of which matches the formerly-cited
  `BattleRunnerTests.…_CountsAsLoss_NotAWin`: that class doesn't exist in this repo, and the test was renamed when
  the win-tally decision flipped its `BattlesWon` pin 0 → 1). See
  `TODO_ARCHIVE.md` → *Mutual KO ends the run even with a live bench*.
- ~~**Phantom stat-cap message**~~ — **FIXED 2026-07-19** (see `TODO_ARCHIVE.md` → *Stat-cap message fidelity*).
````

### 16. Repo-Wide Code Review Sweep — the intro and the "Clean (checked, no findings)" record

````markdown
## Repo-Wide Code Review Sweep (2026-10-02) ⟵ OPEN, adjudicated one item at a time (fixed items → archive)

Six read-only Opus reviewers, one per slice (core engine · ASP.NET/SignalR backend · importer + data layer ·
React/Phaser frontend · tests · infra/security). **Every item still listed below is an unadjudicated finding** —
the user decides fix / waive / defer per item; nothing here is approved work until they say so (fixed items move
to `TODO_ARCHIVE.md`). Items already waived/closed elsewhere
(SignalRInput cancel race, reconnect event-replay *absence*, rules-RNG seeding, items.db pin) were excluded.
**Gen 1 claims were quoted from the reviewers' memory of pokered, not fetched — verify each against
`GEN_DIFFERENCES.md` / pokered before changing the engine or data** (see the *challenge gen fidelity* rule).
Fixing a data/engine item usually also means updating the test that currently pins the wrong value.
````

### 17. Repo-Wide Code Review Sweep — "Clean (checked, no findings)"

````markdown
**Clean (checked, no findings):** Dev Mode gating (server flag; ranges stripped at the emitter's single send
point), client-supplied index/id bounds checks, CORS, event wire projection + REST DTOs, XSS (`dangerouslySetInnerHTML`/
`innerHTML`/`eval` absent), SignalR + Phaser listener cleanup, secrets in tracked files/history, EF schema vs
snapshots vs live dbs, runtime queries (no N+1), Gen 1 type chart and XP curves, HP DV derivation, island layout
generation.
````

### 18. Database Architecture (reference) — covered by `ARCHITECTURE.md` §2.5

````markdown
## Database Architecture (reference)

**Two-database model:**
- `pokemon.db` / `PokemonDbContext` — species, base stats, types, growth/catch rates, learnsets, game
  availability, evolution chains.
- `moves.db` / `MovesDbContext` — moves, damage type, accuracy, PP, stat/status effects.
- `items.db` / `ItemsDbContext` — battle-usable items (Gen 1 roster + gameplay numbers).

**Where new tables go:** Pokémon-world data (egg groups, …) → `pokemon.db`; move-world data → `moves.db`; item
data → `items.db`; player save state (party, caught Pokémon, bag) → `save.db` / `PlayerDbContext` (deferred
until Catch).
````

---

## Repo-sweep R1 — species base stats and BaseExperience were modern, not Gen 1 ✅ DONE (2026-10-03)

Two R1 findings from the 2026-10-02 repo-wide review, fixed together (importer + data only; no engine or seam
interface change).

**The defects (as filed).**
- *Base stats modern:* `BaseSpecial` was taken from Sp. Atk (Chansey 35 vs Gen 1 Special 105, Tentacruel 80/120,
  Gyarados 60/100, Articuno 95/125, Golduck 95/80, …) and the Gen 6/7 stat buffs were never undone (Pikachu Def
  40/30, Beedrill Atk 90/80, Dugtrio Atk 100/80, Farfetch'd Atk 90/65, …). The old import was wrong for 57 species.
- *BaseExperience modern* (`PokemonImport.cs`, feeds `Gen1BattleRules` XP yield): Chansey 395/255, Mew 270/64,
  Magikarp 40/20, Pikachu 112/82. Differed from pokered for 145 of 148 name-matched species.

**The fix.**
- Base stats now resolve *as of a generation* from PokeAPI's `past_stats`: new `PastStats` DTO on
  `PokeApiPokemon`; `SpeciesStatResolver.BaseStatsAsOf(pokeData, generation)` (an entry tagged generation X lists
  stats that applied up to and including X; entries >= target are layered newest->oldest so the closest wins).
  Gen 1's single Special is the `special` stat of the generation-i entry (present for all 151).
  `PokemonImport.MapToSpecies` takes the `generation` param threaded from `FetchPokemonByGeneration`;
  `SingleSpecialAsOf` throws `NotSupportedException` for generation != 1 (the Gen 2 Special split needs two
  columns = a model change).
- BaseExperience has no PokeAPI history, so Gen 1 values are the curated table
  `PokeApiConnector/Generation_1/Gen1BaseExperience.cs` (source: the `db N ; base exp` line of every pret/pokered
  `data/pokemon/base_stats/*.asm`, fetched 2026-10-03), reached through `SpeciesBaseExperience.For(generation,
  speciesId)`, which throws `NotSupportedException` for a generation with no table rather than silently importing
  the modern value.
- CatchRate is likewise a curated table, `PokeApiConnector/Generation_1/Gen1CatchRate.cs` (pokered `db N ; catch
  rate`, fetched 2026-10-03), reached through `SpeciesCatchRate.For(generation, speciesId)` (throws
  `NotSupportedException` for an uncurated generation). It no longer comes from PokeAPI's `capture_rate`; only
  Raticate differed (127 -> 90).
- **Fail loudly, for real.** `PokemonImport.FetchPokemonByGeneration` resolves `GenerationImportScope.For(generation)`
  first (throws `NotSupportedException` for an unsupported generation before any I/O), has no blanket catch around
  the generation fetch, and returns the list of failed species urls; `FetchPokemonDataByUrl` returns bool and
  catches everything except `NotSupportedException`. `Program.cs` `ReportFailedSpecies` lists failures and exits
  non-zero (both the `species` command and the end of the full run, after the other stages ran). A missing
  hp/attack/defense/speed stat is a failure too (`Require(...)` throws `InvalidOperationException`, like the
  Special rule) instead of `GetValueOrDefault` silently yielding 0.
- **Learnset import respects the generation.** New `GenerationImportScope` record (`For(generation)` throws
  `NotSupportedException` for anything but 1) holds `LearnsetVersionGroup` ("red-blue"), `MaxMoveId` (165) and
  `MaxSpeciesId` (151). `LearnsetMapper.ExtractLearnset(pokemon, generation)` and `ImportLearnset(…, generation)`
  read it (was a hard-coded `Gen1` const / `ExtractGen1Learnset`); the dex cap reads `scope.MaxSpeciesId`.
- New importer command `dotnet run --project PokeApiConnector -- species` (species only; leaves moves, items and
  evolutions alone).
- `pokemon.db` is a genuinely fresh `-- species` import (taken rather than hand-patched), so the committed DB
  equals what the importer produces. `PokemonSpecies` changed in the five stat/exp columns + `CatchRate`
  (Raticate) + 14 `PokedexEntry` texts (PokeAPI's first English flavor text drifted upstream; this also fixes
  Charizard #6's corrupted character) — the 14 text changes are an accepted, intentional part of the commit.
  Learnset / evolution / game-availability tables are identical in content (learnset autoincrement ids reshuffled
  by the importer's delete+insert — expected).
- Design placement: `DATA_IMPORT.md` §4.2/§4.6 (past_stats resolution, curated base-exp and catch-rate,
  fail-loudly rule, generation scope, `species` command; the old "BaseSpecial <- special-attack is correct" claim
  was wrong and is corrected), `ARCHITECTURE.md` §2.6, `GENERATION_SEAMS.md` ("Adding a whole new generation" —
  data-side checklist), `GEN_DIFFERENCES.md`.

**Verification (primary source).** Fetched all 151 pret/pokered base_stats files + PokeAPI `/pokemon/1..151`.
Resolving `past_stats` for Gen 1 reproduces pokered's hp/atk/def/spd/spc exactly for all 151 (3 names differ only by
filename: nidoranf/nidoranm/mrmime). All 151 BaseExperience values now match pokered. §5.0 generation-agnostic
checklist: no seam touched, no new engine constants, no direct stat reads, no gen checks in the engine.

**Tests.** `Unit/SpeciesStatResolverTests` (incl. a falsification test: generation 1 vs 6 resolve differently, so
nothing is hard-coded to Gen 1); `Unit/SpeciesBaseExperienceTests` (incl. fails-loudly for an uncurated
generation); `Integration/Gen1SpeciesDataContractTests` pins the live `pokemon.db` against
`TestSupport/PokeredGen1Species.cs`, an independent 151-row pokered snapshot (now incl. CatchRate) — verified to
FAIL against the old DB (e.g. Venusaur exp 236 vs 208) and pass on the new one. No pre-existing test pinned the old
values. `Unit/SpeciesMappingTests` pins the DTO -> `PokemonSpecies` wiring via the public
`PokemonImport.MapToSpecies` (missing stat/Special throws, unsupported generation throws,
`FetchPokemonByGeneration(2)` throws offline, Raticate catch rate); sabotage-verified (mapper reading Sp.Atk again
fails the mapping tests). `LearnsetImportTests` call `ExtractLearnset(…, 1)` plus an unsupported-generation test.

**Not done / open (→ `TODO.md` R1c).** BST shift vs encounter tiers and seed-dependent E2E specs (E2E not run);
the remaining Gen-1-isms in the importer outside species/learnsets.

---

## Repo-sweep R1 — reconnect replay mishandled (server flee-cache + 60 s grace, client dedupe) ✅ DONE (2026-10-03)

Found by the 2026-10-02 repo-wide code review sweep. Original finding: a flee (`CreatureFled`, no `BattleEnded`)
left the cached `BattleStarted`/`TurnStarted` alive so a later reconnect revived a finished battle with a dead
move menu; replay fires on every reconnect; a replayed `BattleStarted` bumped `encounterIndexRef` (spurious "A new
challenger approaches!", enemy re-slide, HP flashes 1/1); a replayed `MAP_BIOME_ENTERED` duplicated `routePath` and
left `mapPin` wrong; and the 40 s grace was shorter than the client's 0/2/10/30 s retry schedule (last attempt
≈42 s).

1. **Server — flee clears the replay cache.** `SignalRBattleEventEmitter` clears the battle-scoped cache on
   `CreatureFled` exactly as on `BattleEnded`. **Test:** new case in `SessionResumeTests`.
2. **Server — `ReconnectGrace` 40 s → 60 s** (`GameSessionManager`), sized to outlast the 0/2/10/30 s schedule.
3. **Client — idempotent replay.** New pure helper `battle/replayDedupe.ts` (+ `replayDedupe.test.ts`), wired into
   `useBattleHub`'s `OnBattleEvent` handler: skips a `BattleStarted` whose `enemyId` is already on screen, and a
   `BiomeEntered`/`BiomeNodePlanRevealed` for a biome already accepted (checked against arrival-order refs, not
   reducer state, which lags behind the animation queue). `TurnStarted` is deliberately never skipped (re-applying
   it is the designed self-correction).

**Not done (open in `TODO.md`):** no browser/E2E verification of replay-after-blip; the hook wiring is untested
(only the pure helper is); the 60 s grace rests on SignalR's documented default schedule, not a browser measurement.
**Accepted design, not a bug:** the server still replays on *every* reconnect because it cannot tell a refresh
(needs the replay) from a blip (state intact); the client tolerates the repeat instead.

Files: `creaturegame.Web/Battle/{GameSessionManager,SignalRBattleEventEmitter}.cs`,
`ClientApp/src/{battle/replayDedupe.ts,battle/replayDedupe.test.ts,hooks/useBattleHub.ts}`,
`tests/creaturegame.Tests/Unit/SessionResumeTests.cs`. Design note → `ARCHITECTURE.md` §2.7 ("The reconnect replay
is idempotent on the client…"). Player-visible → `PRODUCT_SPEC.md` → Session resume.

---

## Repo-sweep R1/R2/R3 — five small fixes (modal z-index, slower-flincher, RunFaulted, level-up panel, e2e.ps1 crash) ✅ DONE (2026-10-03)

Found by the 2026-10-02 repo-wide code review sweep; the sweep's remaining items (and the unfixed remainders of
items 3 and 5 below) are still open in `TODO.md`.

1. **R1 — pinned map hid blocking prompts.** `.modal-overlay` z-index raised 10 → 70
   (`components/modals/Modal.css`) so every blocking prompt sits above the pinned Run Map (`.encounter-map--pinned`,
   z 60).
   **`RouteChoiceMap` focus — closed, browser-verified (2026-10-03).** The suspected "focus pulled to a covered
   town" was never a separate defect: the focus-pull landed on a town hidden under the pinned map, and raising the
   modal above it makes the focused town the visible one. Playwright, seeded run (seed=1/BULBASAUR), pinned map
   forced open via DOM click on `.map-toggle-btn` while the route-choice modal was up: overlay z 70 vs map z 60;
   the first offered town (`.town-map-town--offered`) is `document.activeElement` and a hit-test at its centre
   lands inside `.route-choice-modal`; screenshot shows the card fully above the map. The residual keyboard issue
   (Tab escaping the modal) is the open R3 "Modal accessibility" item in `TODO.md`. Also: a Playwright run on the
   non-E2E path (title → VENUSAUR → nickname) rendered fine with no page errors; an earlier blank `#root` in
   headless Puppeteer did not reproduce and is unexplained (not filed as a bug).
2. **R1 — flinch from a slower attacker carried into the next turn.** `Battle.cs` now clears `IsFlinched` on both
   creatures at end of turn (beside the `HazeSuppressedStatus` reset), so a flinch set after the target already
   acted is no longer consumed by its next turn. **Test:** `FlinchContractTests.SlowerFlincherDoesNotCostTargetItsNextTurn`.
   **Substitute blocks flinch — also done (2026-10-03).** `FlinchEffect` (`MoveEffects.cs`) is now gated on
   `!ctx.TargetShieldedBySubstitute`. Verified against the primary source (pret/pokered master, fetched
   2026-10-03, not memory): `engine/battle/effects.asm` `FlinchSideEffect:` begins `call CheckTargetSubstitute` /
   `ret nz` (tests `HAS_SUBSTITUTE_UP` on the target's `wBattleStatus2`); `engine/battle/core.asm`
   `AttackSubstitute` (~lines 4849-4900) clears `HAS_SUBSTITUTE_UP` when the decoy breaks and then
   `.nullifyEffect` zeroes the attacker's move effect, so no side effect runs on the breaking hit either — this
   matches the pre-existing `_targetShieldedAtImpact` snapshot in `AttackAction`. Not generation-variable (Gen 2+
   block it too), so no `IBattleRules` seam. **Tests:** `FlinchContractTests.SubstituteShieldsTheTargetFromFlinch`
   (Theory, 200 / 1 = decoy survives / decoy breaks), both cases verified to FAIL with the gate removed. Rationale
   lives in `GEN_DIFFERENCES.md` → Status Quirks.
3. **R1 — a faulted run task never told the client.** `GameSessionManager`'s run-task catch-all now calls the new
   `SignalRBattleEventEmitter.SendRunFaulted()`, which sends a transport-level `RunFaulted` message on
   `OnBattleEvent` (not a `BattleEvent`; never cached, so never replayed). `useBattleHub.ts` handles it like a
   failed resume: clears the persisted active game and navigates to the Title Screen with the notice "The run hit
   an unexpected server error and ended." The server does not close the socket. **No test covers this path** (the
   emitter send, the catch-all call, and the client handler are all unpinned) — a remaining gap, noted in `TODO.md`.
4. **R3 — level-up panel covered the player nameplate.** `LevelUpStatPanel` is now the first child of
   `.player-corner` (BattleScreen.tsx), so it stacks in-flow above the party strip and nameplate; the
   independent `bottom/right` anchor and z-index were removed from `.levelup-panel` (`BattleScreen.css`).
5. **R2 dev scripts — `e2e.ps1` failure path crashed.** The "No JSON report was written" line passed `-f` args
   to `Write-Host` as a second `-ForegroundColor`; the format expression is now parenthesised. The other dev-script
   bullets (`-StartStack` leaving the backend running, `stop-dev.ps1` killing :5173, `test.ps1 -E2E -StartStack`
   exiting 0 when the backend never starts) remain open in `TODO.md`.

Files: `creaturegame/Combat/Battle.cs`, `creaturegame.Web/Battle/{GameSessionManager,SignalRBattleEventEmitter}.cs`,
`ClientApp/src/{hooks/useBattleHub.ts,pages/BattleScreen.tsx,pages/BattleScreen.css,components/modals/Modal.css}`,
`e2e.ps1`, `tests/creaturegame.Tests/Integration/Gen1Attacks/FlinchContractTests.cs`. Design note → `ARCHITECTURE.md`
§2.7 ("A crashed run is reported by a transport-level `RunFaulted`…").

---

## E2E `voluntary-switch.spec.ts` failing standalone — Dev-Mode `forceDraft` ✅ DONE (2026-10-02)

**Symptom:** the last E2E (test 38 of 38) failed standalone — `walkSeedsUntil` exhausted all 8, then all 16, seeds
without ever reaching a switchable turn (a party of two).

**Root cause:** a party of two needs a themed draft, which is gated on every 3rd win × a 55% roll
(`DraftCalculator`), and a Lv30 lead often dies after 2 wins to the first gate boss's crit. Commit `0eb0d53`
("Level-gate Strong/Boss movesets") also changed encounter generation, so what each seed plays drifted — the
seed walk that used to land a draft no longer did. (Another instance of the "seed is not determinism" lesson in
`TODO.md` → *Browser-Based UI Testing*.)

**Fix:** a Dev-Mode-gated `forceDraft`. Client `?forceDraft=1` on the seeded `/select` URL
(`utils/startGameRequest.ts`) → `StartGameRequest.ForceDraft` → `GameSessionManager.RegisterSession(forceDraft)`
stores `forceDraft && devMode.Enabled` → `EncounterFactory.BuildDraftSupplier(forceOffer)` skips the cadence + roll
(the fought-pool guardrail is kept). E2E helpers `startBattle`/`walkSeedsUntil` gained a `forceDraft` option; the
spec walks seeds `[1,2,3,4]` with `forceDraft` and asserts `/api/dev/status` is enabled first; `e2e.ps1` and
`test.ps1 -StartStack` set `DevMode__Enabled=true` explicitly. Rationale → `ARCHITECTURE.md` §2.7 ("E2E reaches a
party of two by a Dev-Mode forceDraft"). Files: `GameController.cs`, `GameSessionManager.cs`, `EncounterFactory.cs`,
`StarterSelection.tsx`, `startGameRequest.ts`, `e2e/helpers.ts`, `e2e/voluntary-switch.spec.ts`, `e2e.ps1`,
`test.ps1`. Tests: `DevModeTests` (3 gate cases), `EncounterFactoryDraftTests` (2 force-offer cases),
`startGameRequest.test.ts`. Verified: spec passes standalone (2/2).

**Open follow-up (live in `TODO.md` → *Browser-Based UI Testing*):** the spec still takes ~3.0 min — it burns several
seeds before forcing lands a switchable turn.

---

## Repo-sweep R1 — lost modal answers soft-lock the run (parts a + b) ✅ DONE (2026-10-02)

Found by the 2026-10-02 repo-wide code review sweep (R1 "players get stuck or lose control"; the sweep's remaining
items are still open in `TODO.md`). One root cause, three reports: an answer to a blocking prompt could be lost in
transit. **Parts (a) and (b) are fixed; part (c) is not** (see below).

**(a) Server — shop BUY then LEAVE dropped the LEAVE.** Every `SignalRInput.Set*` completed only a
currently-pending handshake, so a shop answer landing between two prompts was discarded; a BUY followed immediately
by a LEAVE left the server looping in the shop. **Fix:** `SignalRInput.SetShopAction` / `ChooseShopActionAsync` now
keep a lock-guarded shop-action backlog — open from the first shop prompt until a LEAVE is consumed or `Cancel`,
cleared on LEAVE — so back-to-back answers are consumed in order. **Tests:** 5 new `SignalRInputTests`, each bounded
with a 5 s `WaitAsync` so a regression fails instead of hanging; sabotage-verified.

**(b) Client — modals hid before `invoke` resolved.** Every blocking-modal answer in `useBattleHub` hid its modal
immediately, so an answer rejected during the reconnect window left the player with no modal and no way to answer.
**Fix:** new `hooks/answerPrompt.ts` (`submitPromptAnswer`) plus a `RESTORE_PROMPT` reducer action / `PromptKey` type —
the modal still hides at once, but re-opens if the hub invoke is rejected (restoring only into an empty slot).
**Tests:** 6 new Vitest tests (`answerPrompt.test.ts`); sabotage-verified.

**Decision (user, 2026-10-02):** the targeted fix (backlog + re-open-on-rejection) was chosen over a prompt-id/ack
protocol or a client-only restore. Design rationale → `ARCHITECTURE.md` §2.7 ("A modal answer must not be lost in
transit…"). Files: `creaturegame.Web/Battle/SignalRInput.cs`, `ClientApp/src/hooks/{answerPrompt.ts,battleReducer.ts,
useBattleHub.ts}`, `ClientApp/src/battle/timeline.ts`.

**Part (c) — still open:** the server's reconnect replay (`SignalRBattleEventEmitter`) doesn't re-open an *open*
prompt after a refresh. This is the already-documented Known Gap in `TODO.md` → *Known Gaps* ("Session Resume doesn't
cover a reconnect during a between-node blocking prompt"); it was not duplicated as a new item.

### R1b follow-ups 1–5 from this fix's `pr-review` (verdict PR-READY) ✅ DONE (2026-10-03); item 6 ruled ✅ (2026-10-04); items 7–9 ✅ DONE (2026-10-06)

The cheap, no-behaviour-change follow-ups the review recommended (nine in all; numbers kept).

**Items 7–9 — three advisories, all S, run-layer, runtime-only, no Gen 1 surface (✅ DONE 2026-10-06).** Not
player-visible beyond a cosmetic balance fix; no `PRODUCT_SPEC.md` entry.

7. **Restored shop shows the current balance.** `battleReducer.ts` `RESTORE_PROMPT` used the balance captured at the
   LEAVE click, so a `SHOP_PURCHASED` landing between the click and the restore left `shop.balance` stale vs. `gold`.
   Now applies `state.gold` to `shop.balance` when restoring the shop. Pinned by a new `battleReducer.test.ts` case
   (`SHOW_SHOP`(100) → `HIDE_SHOP` → `SHOP_PURCHASED`(70) → `RESTORE_PROMPT` with the stale value → balance 70).
8. **The ten `answerPrompt` routes are a tested table.** Extracted into `hooks/promptAnswers.ts` (`PROMPT_ANSWERS`:
   key, hide action, hub method); `useBattleHub.ts` reads from it. `hooks/promptAnswers.test.ts` pins each entry and
   runs each hide action through the real reducer. **Fallback taken:** the agreed acceptance was "drive each callback
   with a fake hub connection, fallback = extract a pure table"; the fallback was used because the repo has no
   `@testing-library`/jsdom/`renderHook` and the hook builds its connection internally. **Known residual gap:**
   nothing asserts that each `useBattleHub` callback picks the right table entry (a swapped key in a callback would
   pass); closing it needs a hook-test harness or injectable connection.
9. **`CloseShopIfLeaving` closes on any non-BUY answer.** `SignalRInput.cs` now returns on `action is BuyShopItem`
   (previously `action is not LeaveShop` ended the shop only on LEAVE), matching `ShopRunEvent`. No new test:
   `SignalRInputTests` already pin buy-leaves-open / leave-closes.

Gates: format-gate PASS, test-runner 2066/2066 (E2E not run); `pr-review` skipped by user decision.

**Item 6 — ruled by the user, 2026-10-04: intended.** Rapid repeated shop BUY clicks are now all honoured where
some were previously dropped; the dropped click was the bug. No code change.

1. **Test the branch the fix exists for.** All five original shop tests sent the second answer *before* awaiting
   `first`, so they only hit the "pending TCS already completed" path. Added
   `SignalRInputTests.ShopActions_LeaveArrivingAfterBuyWasConsumed_IsServedByTheNextPrompt` (send BUY, await the
   first prompt so it is cleared, send LEAVE, assert the next `ChooseShopActionAsync` is already completed with
   LEAVE — the "no prompt pending, shop open" branch).
2. **Test that serving a queued LEAVE clears the backlog.** Added
   `SignalRInputTests.ShopActions_ServingAQueuedLeave_ClearsTheRestOfTheBacklog` (BUY, LEAVE, stray BUY all sent
   before awaiting; first = BUY, second = LEAVE, a third prompt is not completed).
3. **`RESTORE_PROMPT`'s `key` is now tied to its `value` by type.** The `RESTORE_PROMPT` action in `timeline.ts` is
   a mapped type over `PromptKey`; `submitPromptAnswer` / `PromptAnswer` / `answerPrompt` are generic over
   `K extends PromptKey`. One `as Action` cast remains inside the generic `submitPromptAnswer` (TypeScript can't
   narrow the mapped union through a generic `K`). Verified a mismatched key/value in a concrete literal no longer
   compiles. Types only.
4. **Documented the accepted "rejected-after-sent" trade-off** in `ARCHITECTURE.md` §2.7 ("Accepted trade-off"
   paragraph): SignalR also rejects an already-sent call when the connection closes before the reply, so the
   server may have consumed the answer while the client re-opens a modal that is no longer live. Accepted because
   it is mostly harmless (a one-shot answer is dropped; a shop BUY is dropped because the shop is closed); a
   prompt-id protocol would cover it.
5. **`Cancel_MidShop_…` now pins what it pins.** Renamed `Cancel_WithANonEmptyShopBacklog_StillMakesTheNextShopPromptThrow`
   with an explanatory comment (it passes regardless of backlog clearing, because `_cancelled` never resets and
   `ChooseShopActionAsync` throws at its cancelled check first); `SignalRInput.Cancel`'s
   `_shopOpen = false; _shopBacklog.Clear()` lines are marked as housekeeping.

Files: `tests/creaturegame.Tests/Integration/Web/SignalRInputTests.cs`, `creaturegame.Web/Battle/SignalRInput.cs`,
`ClientApp/src/{battle/timeline.ts,hooks/answerPrompt.ts,hooks/useBattleHub.ts}`, `ARCHITECTURE.md`.

---

## Repo-sweep R1 — curing a status didn't clear `CarriedStatus` ✅ DONE (2026-10-02)

Found by the 2026-10-02 repo-wide code review sweep (R1 "Engine"; the sweep's remaining items are still open in
`TODO.md`). **Bug:** `HealingItemEffect.ClearStatus` (`ItemEffects.cs`) cleared only the battle-half status
(`Battle.Status`/`SleepTurns`/`ToxicCounter`), not the permanent-half `Creature.CarriedStatus` that the engine
re-applies on the next entry. Two live scenarios: (1) an **Antidote-line / Full Heal / Full Restore item used on a
bench member** — the cure was undone the moment that member next switched in (`Battle.BringInMember` re-applies
`CarriedStatus`); (2) the **Treasure/Mystery-node quick-heal on the lead** (`RewardResolution.ApplyHeal`, which
goes through the same `ClearStatus`) — the lead re-opened the next battle afflicted again (`BattleRunEvent`
re-applies the lead's `CarriedStatus` at the opening). `ReviveItemEffect` already cleared it; `ClearStatus` did not.

**Fix:** `ClearStatus` now also nulls `Creature.CarriedStatus`, so every cure path (status-cure items, Full Restore,
the quick-heal reward) clears both halves. `STATE_MODEL.md` §2 gained a "Curing a status clears both halves"
paragraph. **Tests (sabotage-verified red without the fix):** `ItemEffectTests` ×2 (cure items clear
`CarriedStatus`), `QuickHealRewardTests` ×1 (quick-heal on the lead), `BattleVoluntarySwitchTests` ×1
(end-to-end through a real voluntary switch: cured bench member enters clean). Player-visible: a cured creature no
longer re-enters afflicted.

---

## Repo-sweep R2 — two latent 1/256-miss test flakes + a vacuous resume assertion ✅ DONE (2026-10-02)

**Vacuous assertion (also R2, fixed 2026-10-02):** `SessionResumeTests.cs` →
`ReplayLastKnownState_ResendsEveryCachedEvent_InItsNaturalOrder_ToWhicheverConnectionIsNowCurrent` compared
`(string, object)` tuples by reference against a fresh-per-send payload, so the "conn-1 got nothing new" check could
never match. Replaced with a count check: conn-1's event count captured before the replay must be unchanged after
it. Test-only, not player-visible.

**Flakes:** found by the 2026-10-02 repo-wide code review sweep (R2 "Test flakes"; the sweep's remaining items are still open
in `TODO.md`). `BattleIntegrationTests.cs` — `Battle_SlowerCreature_ActionSkipped_WhenKilledFirst` and
`Battle_EndOfTurnResidualSkipped_WhenOwnAttackFaintsTheOpponentThatTurn` — ran unseeded Gen 1 rules, so the Gen 1
1/256 always-miss (≈0.4% per test per run) could let the enemy act / let the poison tick and fail the assertion.
Fix (test-only): both now construct `Battle` with `rules: AlwaysHitRules.Instance` and
`rng: new SeededRandomSource(0)`, matching the existing deterministic tests. No product change, not player-visible.

---

## Dev Mode — server-gated debug switch + enemy overview + damage-roll ranges ✅ DONE (2026-10-02)

*(Moved here from `TODO.md` → *Dev Mode*; planned and implemented 2026-10-02. Open follow-ups — enemy stat
stages, further dev actions — stay in `TODO.md` → *Dev Mode — open follow-ups*.)*

**Why:** the user wants to play deep runs and inspect what the game is doing. First feature: an enemy-creature
overview (the CHECK POKEMON sheet, for the *foe*). The switch is **server-gated** so the deployed Fly app can
never leak enemy stats or expose debug actions, whatever a client sets in localStorage.

**Design (ratified 2026-10-02):**
- **Server flag is the authority.** `DevModeOptions` (config key `DevMode:Enabled`); default
  `builder.Environment.IsDevelopment()` — `dev.ps1` launches with `ASPNETCORE_ENVIRONMENT=Development`, Fly runs
  Production, so prod is off with no extra config. An env var (`DevMode__Enabled`) overrides either way.
- **Client toggle is a second, per-viewer layer.** `GET /api/dev/status` → `{ enabled }`; the Settings panel
  shows the "Dev mode" toggle **only when the server says enabled**, persisted as `devMode: boolean` (default
  off) in `utils/settings.ts`. A small "DEV" badge shows in the HUD while it's on.
- **Every dev endpoint re-checks the server flag** (404 when disabled) — hiding the toggle is UX, not security.
- **Dev actions hang off one gate** (`DevController`).

**Item 1 — flag + toggle (ec000a6):** `DevModeOptions` + DI registration + `DevController` `GET /api/dev/status`;
`Settings.devMode` + `useDevMode` hook; toggle in `SettingsPanel`; HUD DEV badge.

**Item 2 — enemy overview:** `GET /api/dev/{gameId}/enemy` in `DevController` (404 when the server flag is off or
the run has no enemy yet), reusing `PlayerOverviewDto` over `GameSessionManager.GetEnemyCreature(gameId)`
(`ActiveBattle.Enemy`, recorded by the `RunDirector` enemy supplier; display-only read, same no-lock reasoning as
`GetPlayerCreature`). Client: with dev mode on, clicking/tapping the enemy nameplate opens `EnemyOverviewModal`,
which wraps `CreatureOverview` in `enemy` mode (no party picker, no Exp rows).
*Deviations from the plan:* the endpoint is under `/api/dev/` (not `/api/game/`); the trigger is click/tap on the
nameplate, not hover (works on touch); stat stages are **not** shown (`PlayerOverviewDto` has none) — open
follow-up in `TODO.md`.

**Item 3 — damage-roll ranges (low–high):** show each attack's min–max damage in the combat log, the fight menu
and CHECK POKEMON (player sheet and dev enemy sheet), all dev-gated.
- **Single source of truth = the real formula.** `DamageCalculator.EstimateRange` reuses `ComputeDamage` with the
  variance pinned to the rules' bounds — no second copy of the formula. `DamageCalculator.ScreenMultiplier` is
  shared with `AttackAction` so Reflect/Light Screen agree.
- **Seam (gen-agnostic DoD):** `IBattleRules.DamageVarianceRange` and `IBattleRules.PsywaveDamageRange(source)`
  added; `Gen1BattleRules` reads both its roll (`RollDamageVariance`, `RollPsywaveDamage`) and its range from the
  same constants/helper (217–255/255; Psywave 1..floor(1.5 x level)), so range and roll cannot disagree.
- **Per category:** Standard/Drain/SelfDestruct → min–max; Fixed/LevelBased → exact; OHKO → target's current HP;
  SuperFang → half the target's current HP; Psywave → from the seam; status/other (incl. Counter/Bide) → none
  (null). Multi-hit is per hit. Immune (0x) → 0.
- **Decisions ratified 2026-10-02:** NO crit ranges anywhere — every range is non-crit, so a crit hit's actual
  damage can exceed its printed range; and ranges are always measured against the **current** foe (enemy side =
  the foe's moves vs the active player creature; benched slots measured as if sent in).
- **Combat log — computed engine-side, stripped at the web emitter.** `DamageDealt` gains nullable
  `MinDamage`/`MaxDamage` stamped by `AttackAction` (`_hitRange`). The engine is gate-ignorant;
  `SignalRBattleEventEmitter(includeDamageRange)` withholds them unless `DevModeOptions.Enabled`
  (`GameSessionManager` takes an optional `DevModeOptions`). Client: `took 37 damage (32–38)!`
  (`formatDamageRange`; an exact amount prints one number).
- **Menus / CHECK POKEMON:** `GET /api/dev/{gameId}/damage-ranges?side=player|enemy&slot=n` (404 when dev off).
  Client hook `useDamageRanges`; a DMG line on each fight-menu move button and a DMG entry on every CHECK POKEMON
  move row, including the dev enemy sheet.
- **Tests:** `DamageRangeTests` (min <= every rolled damage <= max across seeds, per category, stages/Burn/screens,
  seam), `DevModeTests` (endpoint 404/200, emitter strips fields when off), Vitest `formatDamageRange`/timeline.
  Not automated: the fight-menu / CHECK POKEMON rendering (no DOM harness).

**Tests:** C# — `DevModeTests` (flag default/override, endpoints 404 off / 200 on); Vitest — settings round-trip
(`devMode` default/validation) and `enemyOverviewUrl`. The Settings toggle's hidden-when-server-disabled
rendering and the nameplate click → modal flow have no automated test (no DOM harness; verified by reading only).

---

## Level-Gated Strong/Boss Movesets ✅ DONE (2026-10-02)

*(Moved here from `TODO.md` → *Level-Gated Strong/Boss Movesets*; the full record, including the review follow-ups
and the user-adjudicated waivers, lives here. Planned and implemented 2026-10-02.)*

**Bug:** an L10 Elite Psyduck could open with Hydro Pump. `LearnsetMoveSelector.Select` skipped the learn-level
check for `TmEnhanced` (Strong) and `Optimal` (Boss) (`LearnsetMoveSelector.cs`, the `Optimal or TmEnhanced`
branch), and `EncounterFactory.CreateEnemyAsync` fed those tiers every level-up row plus every Machine row.
Psyduck's Hydro Pump is a level-up row at 52, ignored. Base tiers already honored `LearnLevel <= level`. (Follow-on to
*Boss/Strong "Optimal" moveset could hand a species moves it could never legally learn*, 2026-09-27, which made the
pool species-legal but left it level-ungated.)

**Why not "global min level the move appears at":** `PokemonLearnset` has Gyarados learning Hydro Pump at level 1 (a
starting move), so the global minimum is 1 and gates nothing. 18 of the 55 TM-learnable moves have no level-up row on
any species, so they'd have no floor at all. `PokemonGameAvailability` carries no encounter levels either
(Wild/Static/etc. per version only).

**Legality rule (decided 2026-10-02):** a move is legal for a creature at level L if
1. the species learns it by **level-up** → only if `LearnLevel <= L` (the species' own row wins; no new data), or
2. the species learns it **only by TM/HM** → only if `L >= Attack.MinLevel`.

`Attack.MinLevel` (new nullable column, `moves.db`) is a **hand-curated floor for every TM/HM move** (all 55), keyed
by when the TM is obtainable in Red/Blue (`PokeApiConnector/PokeAPI/MoveMinLevels.cs`; source: pokemondb.net Red/Blue
TM + HM tables; a TM with several sources takes the earliest). Null for every non-machine move.
*Changed from the original plan (2026-10-02):* the plan derived a level-up minimum for TMs that other species also
level-up learn, and curated only the 18 TM-only moves. Data showed the level-up minimum is a poor proxy for a TM (Body
Slam → 25, Mega Punch → 20, ~half the TMs have none) and `MinLevel` is only ever read for Machine rows, so one curated
table over all 55 is simpler and more accurate. No power-curve fallback was needed.

**As built:**
- **Selector** — the `TmEnhanced`/`Optimal` branch of `LearnsetMoveSelector.Select` gates level-up rows on
  `LearnLevel <= level` and Machine rows on a `machineFloor` (`Func<Attack,int>?`). It is a **required** parameter on
  `SelectWithFallback` (pass `null` explicitly for "no floor", which leaves Machine rows ungated — required so a
  future Strong/Boss caller can't forget it, a `pr-review` advisory). `EncounterFactory` passes
  `MachineFloorOrThrow`, which **throws** on a TM/HM move with no `MinLevel` (a stale `moves.db`) rather than passing
  it ungated; it is a named method so the Boss-oracle test uses the factory's exact policy. Tests: `LearnsetMoveSelectorTests` (Psyduck-style level-up gate, Machine floor, rewritten
  strong-tier legality cases).
- **Empty-gate fallback — resolved, no code change.** The worry was `SelectWithFallback`'s *random* fallback bypassing
  the gate when the gated pool is empty. A fallback to the level-up pick is dead code: any level-up row legal at the
  level already passes the strong-tier gate, so it could never add a move. The gate can only empty if a species has no
  level-1 level-up row, and every species has one (data check 2026-10-02), so the random fallback stays the "importer
  not run" last resort only. Invariant pinned by `StrongTierMoveLevelGateTests.EverySpecies_HasALevelOneLevelUpMove_…`.
- **Data** — `Attack.MinLevel` + `AddMoveMinLevel` migration; the curated table (`MoveMinLevels`), set by
  `MoveImport.MapToAttack` on a full import (so `Moves.Update` can't wipe it), plus an offline
  `dotnet run --project PokeApiConnector -- move-levels` that re-applies it to an existing `moves.db` without the
  network — used to update the committed `moves.db` (55 moves). `EncounterFactory.BuildCreature` passes the floor.
  Tests: `MoveMappingTests` (mapper), `StrongTierMoveLevelGateTests` (live-DB pin: every Machine-learned move has a
  floor and no other move does; Strong/Boss enemies never hold a move before its legality level over 60 seeds each),
  and `Boss_MovesetMatchesTmEnhanced…` now recomputes with the floor.
  **Gotcha:** the dev stack/MCP keeps `moves.db` open in WAL mode, so an edit can sit in `moves.db-wal` and leave the
  committed file unchanged — run `PRAGMA wal_checkpoint(TRUNCATE)` (done here) before committing a `.db`. Floors are
  an approximation of party level at each TM's earliest source; `requirements-review` should sanity-check the table.
- **Tests** — the planned test list is covered by the three suites above (Psyduck L10-vs-L52 gate, TM floor
  below/at/above, move-less guard via the level-1 invariant pin, `MinLevel` mapper + live-DB pins, rewritten
  `LearnsetMoveSelectorTests` strong-tier cases).
- **Docs** — `ENCOUNTER_DESIGN.md` §3.5 rewritten (level-legality rule, why a curated table rather than a derived
  minimum, why no empty-gate fallback, the two-places-change-together hazard); `DATA_IMPORT.md` §4.1.1 (the table,
  `move-levels` command, pins), §3 standalone-stage list, §6 WAL gotcha; `LearnsetMoveSelector` enum docs and
  `Attack.MinLevel` comment cut to pointers.

**Review follow-ups (all gates ran 2026-10-02; `requirements-review` raised 7 items, `pr-review` requested 3 docs fixes
+ 2 recommended + 1 advisory — all applied; `pr-review` was not re-run, per the repo's no-fix-loop rule):**
- **Stone-evolved encounter floor (user's direction).** `Gen1EvolutionRules.StoneEvolutionLevel = 30` makes a stone
  edge floor encounters at 30 (via `IEvolutionRules.MinLevelFor`; it never changes when an evolution fires). Previously
  `0`, so a stone-evolved form carrying level-1 power moves (Nidoking Thrash, Arcanine Take Down…) could be fought at
  level 5. A flat tuning constant like `TradeEvolutionLevel`, not a canonical value. `ENCOUNTER_DESIGN.md` §3.8 +
  `PRODUCT_SPEC.md` §3 updated; tests in `EvolutionMinLevelTests` / `Gen1EvolutionRulesTests`.
- **`FilterByMinLevel` fallback (user's direction).** The wild/Elite/Boss `fallback: true` path now returns the themed
  species with the *lowest* floor (every tie) instead of the whole themed pool. Cannot fire against today's Kanto
  rosters. Tests in `EncounterFactoryFilterByMinLevelTests`.
- **Floor policy.** A Machine move with no `MinLevel` throws (`MachineFloorOrThrow`); `machineFloor` is required on
  `SelectWithFallback`; `MoveMappingTests.CuratedFloors_PinKnownRedBlueSources` pins seven anchor floors; the
  level-1-row invariant pin moved to `StrongTierMoveLevelGateTests`; `EncounterFactoryMachineFloorTests` covers the
  throw (including through the real selector). Floors verified: Body Slam (S.S. Anne) and Dream Eater (Viridian City,
  after Giovanni) against several sources; Psychic/Mimic stay at 40 (Saffron's Mr. Psychic/Copycat need no Silph).
- **Adjudicated by the user:** *accepted* — a species' own level-up row beats a TM (`LearnsetMapper` keeps a move that
  is both as level-up only; matches Gen 1), `MinLevel` rides the planned per-`(moveId, generation)` `Attack` split (no
  generation key now), floors as an approximate tuning table against this game's level curve, a gated pool that holds
  only weak moves; *waived* — Gyarados' level-1 Hydro Pump (genuine Gen 1: Bite/Dragon Rage/Hydro Pump/Leer at level 1);
  *confirmed* — Boss has no moveset edge over Strong (it never did after the 2026-09-27 legality fix); a Boss punches
  above through a higher level (stats/DVs/BST), or later other levers such as a party, never a wider or ungated pool.
- **Not run / open observations:** E2E was not run (opt-in) — seed-walking specs (`walkSeedsUntil`) may land on
  different species below level 30 now that stone forms are filtered; recommend `.\e2e.ps1 -Spec <spec>` if a spec
  flakes. Drafts decline more often below level 30 in biomes whose fought pool is mostly stone-evolved forms (a
  consequence of the tuning call, not a defect).

---

## Frontend tech debt — segment `BattleScreen.css` ✅ DONE (2026-10-01)

*(Moved here from `TODO.md` → Generation Profile → Stage 4d+. A pure-move refactor; no player-visible change.)*

**Problem.** `pages/BattleScreen.css` was 2,479 lines / 34 sections with ~30 recent commits touching it, held ~12
modals' CSS inside a *page* stylesheet, `Modal.tsx` imported it from `pages/` (component→page dependency inverted),
and skinning one modal took 3 edits ~1,400 lines apart.

**What shipped (CSS + import lines only; no component logic).** `BattleScreen.css` 2,479 → 1,741 lines. Modal CSS
moved **verbatim** into `creaturegame.Web/ClientApp/src/components/modals/`:
- `Modal.css` — backdrop `.modal-overlay` + its `--corner` variant + the `modal-fade` / `modal-rise` keyframes.
- `ModalFrame.css` — the shared Kanto Sage double-frame rule + the roomier-padding rule. Imported by `Modal.tsx`, so
  it loads before every modal sheet. **A new simple modal joins by adding its card class to BOTH selector lists.**
- `MoveReplacementModal.css`, `BattleEndedOverlay.css`, `RewardChoiceModal.css`, `ShopModal.css`,
  `NicknameModal.css`, `AcquisitionModal.css`.
- `RecoveryCard.css` — Evolution + Poké Center share `.recovery-modal`.
- `RosterPicker.css` — the `.lead-*` family: `LeadChoiceModal`, `SwitchInModal`, `MoveTeachTargetModal`,
  `PartyCard`, plus CHECK POKEMON (`BattleScreen.tsx`) and `CreatureOverview`, which import it explicitly.
Each modal `.tsx` imports its own sheet right after `import { Modal }`.

**Deliberately left in `BattleScreen.css`:** the level-up panel's base + title/table skin (not a modal; its frame
rule lives in `ModalFrame.css`), the route-choice modal (interleaved with the Town Map family and the shared
reduced-motion block), the `levelup-pop` keyframes (used by both `.modal-overlay` and `.levelup-panel`),
biome-title/sub, the `.bag-gold` box, drop-hover.

**Known remaining wart:** `Modal.tsx` still imports `../../pages/BattleScreen.css` (the modals' buttons/tokens —
`.action-btn`, `.move-btn` — live there), so the component→page dependency is only partly inverted. Fixing it means
extracting shared button chrome — its own item (not yet filed).

**Verification.** (1) Line-multiset proof: all 2,255 non-blank original lines present exactly once across old + new
files; the only non-verbatim edits are comment/header text (stale "up with `.levelup-panel`" pointers →
`ModalFrame.css`, file header comments, one pointer comment left in `BattleScreen.css`). (2) Computed-style A/B in a
real browser (Puppeteer against the dev server): 306 element/theme measurements across 9 modal markups ×
{gen1, no profile}, hashed per element, old monolith vs new split — 0 mismatches; baseline confirmed genuine
(monolith loaded, new sheets blank). (3) `tsc` clean, Vitest 283/283, `vite build` OK.
**Not run:** Playwright E2E (opt-in, user-run — recommend `.\e2e.ps1 -Spec level-up`); `pr-review` skipped on
purpose (CSS/imports only). The A/B used hand-built markup mirroring each component's nesting, not live gameplay, so
a modal whose real markup has a class the snippet lacked is not covered.

---

## Creature Identity — id-keyed events ✅ COMPLETE (2026-09-29, all 4 stages)

*(Moved here from `TODO.md` → *Creature Identity — id-keyed events*, Tier 1.5. Planned 2026-09-28, built in four
stages 2026-09-28/29. The write-up below is the plan-time text with per-stage outcomes; "the interim stop-gap" and
"the client still routes by name until Stage 3" describe the world **before** Stage 3. Note the stop-gap (an
`"Enemy <NAME>"` rename in `Battle.StartFightAsync`) lived only in the uncommitted working tree during development
and was removed in Stage 3 — **it never reached a commit**, so no released build ever had it. Current design →
`ARCHITECTURE.md` §2.2, "Creatures are identified by id".)*

**Caveats carried forward:** a bare `Battle` outside `RunDirector` has `0/0` ids (see Stage 3). E2E status is in
the post-review addendum below.

**Problem.** Every creature-referencing `BattleEvent` carries a display *name* (`AttackerName`, `TargetName`,
`CreatureName`, …), and the client decides which side an event lands on by comparing that name to the player's
(`timeline.ts` `side()`, `battleReducer.ts` `UPDATE_HP`/`UPDATE_STATUS`/`CLEAR_STATUS`/`LEAD_CHANGED`/
`CREATURE_RENAMED`). The engine is fine — it routes by object reference (`AttackAction.Source/Target`,
`_participants`) — so this is purely a wire/client defect. **Bug that surfaced it (2026-09-28):** a wild PIDGEY vs
the player's un-nicknamed PIDGEY sent the enemy's hit FX onto the player's sprite. **Interim stop-gap (working
tree only, never committed — removed in Stage 3):** `Battle.StartFightAsync` renamed a colliding enemy to
`"Enemy <NAME>"` for the fight. It covered enemy-vs-party only. **Not covered (likely, unverified — Stage 0 proves or disproves):** two same-named
*party* members (a drafted twin, or two nicknamed alike) — a Potion on a benched twin would move the *lead's* HP
bar (`UPDATE_HP` matches `playerName`), and `LEAD_CHANGED` / `CREATURE_RENAMED` `find` the first name match.
`Creature Naming` (archive) made collisions likelier and its plan recorded "no code looks up a creature by
`Name`" — true of the engine, false of the client.

**Decision (user, 2026-09-28): identity, not a stop-gap.** Names stay for *display text*; ids do all *routing*.

### Design
- **`Creature.Id : int`** — the individual's identity, **stable across evolution, Transform/Mimic and nickname
  changes** (it is the creature, not the species: `SpeciesId`/`Name` both legitimately change; `Id` never does).
  `0` = unassigned.
- **Minted by a per-run `CreatureIdSource`** (monotonic counter owned by the run, threaded to
  `EncounterFactory.BuildCreature` — the single builder for starter / draft / boss catch / wild enemies). **Not
  `Guid.NewGuid()`:** the run promises same-seed → same event sequence (`GAME_LOOP.md` §5); a random id would
  break it. A counter created in the deterministic order the run already builds creatures keeps it. **Not a
  process-global counter** either (concurrent games + test order would leak into wire values).
- **Events keep the name field (display) and gain an `…Id` sibling (routing)** — additive, so the log text
  path is untouched. `BattleStarted`/`TurnStarted` carry `PlayerId`/`EnemyId`; every other creature event carries
  the id of the creature it names. Party wire types (`PartyMemberDto`, `PartyUpdated`, `LeadChanged`,
  `CreatureSwitchedIn`, acquisition events) carry ids, so two same-named members are distinguishable.
- **Client:** `ExpandContext.playerName` → `playerId`; `side(id)`; reducer holds `playerId`/`enemyId` and matches
  `UPDATE_*` by id; `PartyMember.id`; `LEAD_CHANGED`/`CREATURE_RENAMED` find by id. Names only ever reach
  `LOG`/nameplate text.
- **Inbound stays slot-keyed** (`UseItem`, `RespondAcquisition`, `ForgetMove`, switch) — already unambiguous;
  do not migrate it. Revisit only if slots stop being stable (e.g. a PC box).
- **Retire the stop-gap** in the same stage that makes the client id-routed (delete the rename + its tests →
  replace with id probes), so the workaround doesn't outlive its reason.

### Staging (each stage independently shippable; stop for greenlight between them)
- [x] **Stage 0 — prove the gap** ✅ DONE (2026-09-28, tests only, no product change). **The party half is a
  real bug, not latent.** Two legs: (a) engine/wire —
  `ItemActionBattleTests.UsingPotion_OnABenchTwin_…` shows `ItemEffects` heals the right member by reference but
  emits `Healed` named identically to the lead's (a characterization test; Stage 2 replaces its assertion with
  "the two ids differ"); (b) client — two `it.fails` reducer cases in `battleReducer.test.ts` assert the correct
  behaviour and are verified to fail on their real assertions: a benched twin's heal moves the **lead's** HP bar
  (`UPDATE_HP`), and a benched twin's evolution renames the **on-field** creature (`CREATURE_RENAMED`). Stage 3
  turns red on them by design — that is the cue to drop the `.fails` and pass ids. **Not tested, by design:**
  `LEAD_CHANGED`'s first-name-match `find` — the correct answer isn't expressible without an id in the action,
  so it is covered by Stage 3's id probes instead.
- [x] **Stage 1 — engine identity** ✅ DONE (2026-09-28, no wire change). `Creature.Id` (`internal set`, 0 =
  unassigned), `CreatureIdSource` (counter from 1, `Assign` is idempotent, `HighWater` for the future save
  layer), owned by **`RunState.Ids`**. **Deviation from the plan as drafted:** minting is in the core
  `RunDirector`/`RunState`, *not* `EncounterFactory.BuildCreature` — the factory is a singleton shared across
  runs, so it cannot own per-run state, and threading a source through its four public entry points would have
  been churn for nothing. `RunState`'s constructor identifies the starting party (starter = id 1);
  `RunDirector` wraps the enemy, draft and boss-catch suppliers so every creature entering the run is
  identified at the one place all three pass through. Tests: `CreatureIdSourceTests` (unique/monotonic/
  idempotent/per-run-scoped, survives nickname + identity snapshot-restore), `EvolveToTests` (id survives
  evolution), `CreatureIdentityRunTests` (a real run with a same-named wild twin *and* a same-named drafted twin:
  all ids unique and non-zero, entry order, same seed ⇒ same ids). **Verified by sabotage:** bypassing the foe
  wrapper fails exactly the two foe probes (both foes `Id 0`); the same-seed test was strengthened to assert
  non-zero, since two runs of zeros would otherwise pass vacuously.
- [x] **Stage 2 — wire** ✅ DONE (2026-09-29; server side only — the client still routes by name until Stage 3).
  ~60 event records + `PartyMemberInfo`/`MoveTeachCandidateInfo` gained an `int …Id` sibling beside each
  creature-naming string (`TargetName`→`TargetId`, `Name`→`Id`, …), **required and un-defaulted** so the compiler
  flags every construction site (146 sites; a forgotten id can't compile, let alone default to 0 silently).
  Names stay for display text. Shapes worth knowing for Stage 3: `EvolutionOffered`/`CreatureEvolved` carry **one
  `CreatureId`** (from/to are the same creature, so no `FromId`/`ToId`); `TransformedInto` carries both
  `CreatureId` and `TargetId`; `CreatureAcquired.ReplacedId` is `int?` (null on an open-slot deposit);
  `BattleEnded.WinnerId`, `RunEnded.FinalCreatureId`, `SwitchInOffered.FaintedId`. `MapEvent` and both nested
  projections (`ProjectPartyMember`, `ProjectMoveTeachCandidate`) project them, so the party-hydrate REST snapshot
  carries ids too. Wire field names are the camelCase of the record's (`targetId`, `creatureId`, `id`).
  **Two new generic guards in `WebEventContractTests`:** `EveryCreatureNameOnTheWireHasAnIdSibling` (reflects over
  every event *and* nested payload record; any `…Name` string not in the `NonCreatureNameProperties` allowlist or
  the `NonCreaturePayloadTypes` set — `RegionMapBiome`, `BiomeOption`, `MoveInfo` — must have an int sibling, so a
  future event can't ship name-only) and `EveryCreatureIdProjectsUnderItsOwnNameWithItsOwnValue` (gives each id a
  distinct sentinel and asserts it arrives under its own name — catches a crossed wire the presence-only field
  guard can't). **Verified by sabotage:** `EnemyId = e.PlayerId` in the `BattleStarted` arm fails exactly the value
  guard (`sent 1003, arrived as 1001`); un-allowlisting `ScreenName` fails exactly the sibling guard. Engine-level:
  `SameNameBattleTests.SameNamedCombatants_EveryEventIdentifiesItsCreatureById` (real `Battle`, ids only — stays
  valid when Stage 3 removes the rename) and the Stage 0 characterization test is **replaced** by
  `UsingPotion_OnABenchTwin_…CarryingTheTwinsIdNotTheLeads`. Console emitter unchanged (text).
  *Mechanics note:* the call-site edit was a regex over `new <Event>(<expr>.Name` → `…, <expr>.Id` (98 sites) plus
  16 hand edits the compiler enumerated; every regex case's first argument was a `Creature`, checked, because an
  `Attack` also has `.Name`/`.Id` and would have compiled silently.
- [x] **Stage 3 — client routing** ✅ DONE (2026-09-29). `ExpandContext.playerName` → `playerId`; `side(id)`;
  reducer state gains `playerId`/`enemyId` (names kept for display) and `UPDATE_HP`/`UPDATE_STATUS`/
  `CLEAR_STATUS`/`LEAD_CHANGED`/`CREATURE_RENAMED`/`SWITCHED_IN`/`PARTY_SET` all key on ids; `PartyMember` and
  `MoveTeachCandidate` carry `id`; `nextPlayerName` → `nextPlayerId`. **A simplification, not just a port:**
  `CreatureEvolved` no longer touches player identity at all (the id is stable across the rename), so the old
  "guard on the from-name" special case is gone. `Battle.StartFightAsync`'s interim `"Enemy X"` rename (never
  committed) is **deleted** (names now stay identical on the wire; `SameNameBattleTests` rewritten to assert that). **The two Stage 0
  `it.fails` cases are now ordinary passing tests**, alongside new same-name cases: wild-vs-player HP/status,
  lead-vs-benched-twin heal, twin evolution (both directions), `LEAD_CHANGED` onto the promoted twin (not the
  first row with that name), `PARTY_SET` not hijacked by a same-named lead, and the whole `expandEvent` side
  family (shake, lunge, faint, Transform, status, win/loss). **`routing.test.ts`** is the "no name-routing"
  guard: it scans `timeline.ts`/`battleReducer.ts`/`playerIdentity.ts` (comments stripped) for any equality
  against a `*Name`, with a self-test that the pattern still matches the old shapes so it can't go vacuous —
  **verified by sabotage** (a reintroduced `=== state.playerName` fails exactly the reducer's assertion). Needed a
  two-line `src/raw-imports.d.ts` for Vite's `?raw` import. **Verified live:** a real run over SignalR shows
  camelCase ids on the wire (`playerId`/`enemyId`, `attackerId` → `targetId`) and the enemy's hit carrying the
  *player's* id. **Not verified in a browser** — the reported same-name fight itself wasn't reproduced end to end
  (E2E is user-opt-in): the engine (`SameNamedCombatants_…`) and client (`same-named combatants…`) halves are each
  pinned, and the live probe pins the seam between them.
  **Known limit, by design:** a bare `Battle` outside `RunDirector` (i.e. tests) has `0/0` combatants — ids are
  assigned by `RunDirector`, which is the only production entry.
- [x] **Post-review addendum (2026-09-29).** `pr-review` returned CHANGES-REQUESTED (one blocker + four small
  items), all resolved: the value guard now walks **nested** records (`PartyMemberInfo.Id`,
  `MoveTeachCandidateInfo.Id` — sabotage-verified: mapping `Id = SpeciesId` fails five nested paths); the boss-catch
  id wrapper is pinned by `BossCatch_WhenOffered_AndAccepted…` (bypassing it fails exactly that test); doc pointers
  repointed to `ARCHITECTURE.md` §2.2; `routing.test.ts` also scans `useBattleHub.ts` and catches loose `==`/`!=`
  (its header lists what it still can't: `.includes(name)`, `switch (name)`, name-keyed `Map`s); `STATE_MODEL.md`
  notes that a save layer must persist `RunState.Ids.HighWater`. **Resume follow-up (a `pr-review` advisory,
  pre-existing under name routing):** the reconnect replay re-sends the *cached* `BattleStarted` (the encounter's
  original lead) plus the latest `TurnStarted`, so a refresh after a mid-battle switch-in left the client believing
  the outgoing creature was fighting. `TurnStarted` now restates identity — `nextPlayerId` handles it and the
  `TURN_STARTED` action re-asserts `playerId`/`enemyId`/names — so the next turn prompt self-corrects (tests
  simulate the stale-replay sequence). **Possibly still open, unrelated to ids and NOT verified:** the *player sprite's
  species* after such a refresh — `TurnStarted` carries no species id, so nothing on this path re-derives it; whether
  the remounted scene shows the wrong species was not checked.

### DoR coverage
1. **Acceptance:** with a same-named player/enemy, a same-named benched twin, and a nickname equal to the enemy's
   species, every damage/HP/status/faint/switch effect lands on the correct sprite and HUD row; the reflection
   guard fails if a creature event lacks an id; no name-based routing remains client-side.
2. **Design pass:** this plan; central-method impact is limited to `BuildCreature` + `Battle`'s event emission
   (adds ids, changes no battle math). No frontend *visual* design — provisional flag not needed.
3. **Gen-variable surface:** **none — identity is gen-invariant** (litmus: Gen 2 changes no id semantics). It is
   an enabler for later-gen shapes (doubles need >1 creature per side, which name→side routing cannot express).
   No `IBattleRules` member; `TestAltProfile` needs no new slice (state this in the Stage 1 PR).
4. **Gen 1 source of truth:** N/A — infrastructure, not a mechanic. Nothing to assert against the cartridge.
5. **Data vs runtime:** runtime + wire + client only. **No importer/DB change.** (Future save layer persists
   `Id` and the source's high-water mark.)
6. **Quirk to test:** the collision cases in (1), incl. the **falsification** requirement — verify by sabotage
   (route one arm by name again → exactly its probe fails, others stay green).
7. **Dependencies:** none blocking. Should land **before** Catch (`CaptureAttempted(TargetName, …)` would
   otherwise ship name-keyed) and before `PlayerSave`/`SavedCreature`. `Fusion` (design-guide inspiration) would
   mint a *new* id for the fused creature and retire both parents' — decide when planned, not now.

### Risks / open questions
- **Test churn:** many tests assert `AttackerName == "Player"`. Names stay on events, so they keep passing;
  only routing tests change. Tests constructing `new Creature("X")` get `Id 0` — the guard must treat 0 as
  "unassigned" and tests that exercise routing assign ids explicitly.
- **Wire size:** one int per creature reference — negligible.
- **Session Resume:** ids survive a client refresh (server holds the run). A *server restart* is the save
  layer's problem (persist the counter with the creature ids).

---

## TM/HM — Move-Teach Rewards ✅ SHIPPED (2026-09-28)

*(Moved here from `TODO.md` → *TM/HM — Move-Teach Rewards*. `/plan` and full `/dev` implementation both landed
2026-09-28, same session.)*

**The goal.** A second move-acquisition channel beyond level-up + evolution: occasionally, as a battle-win drop
or a reward-node pick, the player is offered a **move a current party member could legally learn** (Gen 1's real
TM/HM learnset, already imported), shown with its stats, and picking it teaches that move on the spot.

**Decisions locked with the user (2026-09-28), as built:**
1. **Delivery is reward-choice, not an inventory item.** No Bag entry, no `ItemCategory.Tm`, no `IItemEffect`,
   no out-of-battle UI surface. A move-teach is one more `RewardOption` kind (`MoveTeachRewardOption`) in the
   existing pick-one-of-N `RewardChoice` flow (`RewardChoiceModal`, `RewardCalculator`). Picking the card teaches
   the move immediately — nothing is carried in a bag, so the Tier 2 bag-persistence question was never a
   dependency.
2. **TMs only, no HMs.** Gen 1's 5 HMs exist to gate field interactions this engine's route-graph overworld has
   no equivalent of — out of scope, unforced.
3. **Rarity — substitution chance, provisional/tunable like every other `RewardCalculator` constant:** a
   move-teach offer substitutes the **first** item-reward slot (mirrors how `TryRollHeal` already substitutes
   the second), gated first on "does any live party member have an eligible legal-and-unknown Machine move" — no
   candidate anywhere in the party always falls back to a normal item roll. **Shipped rates**
   (`RewardCalculator.MoveTeachChanceFor`): WildBattle/EliteBattle **5%**, Treasure/Mystery **20%**, BossBattle
   **35%**.
4. **Legality reuses the exact Machine-row data `TmEnhanced`/`Optimal` already query** — no new import, no new
   `items.db` table, no TM-number↔move catalog. `PokemonLearnset.Method == LearnMethod.Machine` rows are the
   sole legality gate. Cosmetic real Gen 1 TM numbering was flagged as a nice-to-have and **not built**.
5. **Target selection is a "Teach [move] to a Pokémon?" screen, not a bound card — a roguelite QoL improvement,
   not a literal Gen 1 reproduction.** The reward card offers the move alone; picking it opens a party-list
   prompt — every live member marked **ABLE**/**NOT ABLE** up front (per that member's own Machine-row legality
   + whether it already knows the move). **Corrected during `requirements-review` (2026-09-28):** real Gen 1
   doesn't pre-flag legality — it lets you pick any party member and only tells you *afterward*, via a "But,
   {POKÉMON} can't learn {move}." message, if it can't learn it. Showing ABLE/NOT ABLE before selection is a
   deliberate UX improvement over the source games, not a fidelity claim; the write-up originally overstated
   this as "a real Gen 1 screen," which was wrong and is corrected here rather than silently fixed. Only an ABLE
   row is selectable; picking one runs the existing move-replacement flow (free-slot auto-learn, or the
   `MoveReplacementRequired` forget-a-move prompt) exactly as it does for a level-up learn. **Built as reusable
   infrastructure** — the ABLE/NOT ABLE picker + its legality query are generic, not wired one-off to this reward
   path alone (`MoveTeachTargetModal`/`ChooseMoveTeachTargetAsync`/`MoveLearning.TeachMoveAsync`).

**Superseded scope.** The original 2026-09-27 Known-Gaps sizing assumed a physical bag item (`ItemImport` work, a
new `ItemCategory.Tm` + `IItemEffect`, single-use-vs-infinite scarcity gated on bag persistence). None of that
was needed under the reward-choice design — the whole item/effect/bag/scarcity layer drops out because there is
no item to hold.

**Gen 1 fidelity note.** The *moves offered* are Gen-1-authentic (real TM/HM-learnable moves per species, gated
by the same Machine data the enemy AI already trusts). The *delivery mechanism* — a roguelite reward-choice card
instead of a held, reusable bag item — is a deliberate adaptation, the same class of choice as the existing
Draft/Boss-catch/Reward-Choice systems layered on top of Gen 1 combat fidelity.

**Gen-variable surface: none new.** `PokemonLearnset.Generation` + `IContentScope` already gate Machine rows by
generation — no new `IBattleRules`/seam member was needed.

**As built:**
- **Backend:** `RunLoop.cs` — `MoveTeachRewardOption(Attack Move, bool[] AbleBySlot) : RewardOption` and
  `RewardContext.Party` (the live party snapshot a reward roll needs to see). `IBattleInput
  .ChooseMoveTeachTargetAsync` (default auto-picks the first able member; only the interactive web input blocks
  on a real choice). `BattleEvents.cs` — `MoveTeachTargetRequired` (move stats + per-member `Candidates`) and
  `MoveTeachCandidateInfo` (species/name/level/HP/status + `Able`). `MoveLearning.TeachMoveAsync` — a new
  single-move entry point extracted alongside `LearnMovesForLevelAsync`, sharing the same
  `MoveLearned`/`MoveForgotten`/`MoveLearnDeclined` events and the forget-a-move prompt. `RewardResolution.cs` —
  after `RewardGranted` fires, a `MoveTeachRewardOption` pick raises `MoveTeachTargetRequired`, awaits
  `ChooseMoveTeachTargetAsync`, and an in-range ABLE slot runs `MoveLearning.TeachMoveAsync`; an ineligible/
  stale/declined pick is a no-op (reward already granted, only the teach is skipped).
  `RewardCalculator.RollMoveTeachOption` (web layer, `internal` for direct testing) builds the candidate pool
  from the whole party's Machine-legal-and-unknown moves, weighted by `LearnsetMoveSelector.MoveScore` (made
  `public` for this) using each move's *best-fit* party member's score. `EncounterFactory
  .LoadMachineLearnsetsAsync` loads every content-scoped species' Machine learnset **once per run** (species id
  → legal move ids) — needed up front since a later draft/boss-catch pick can bring in any species, not just the
  starter — carried on the new `RunSetup.MachineMovesBySpecies` / `PendingSession.MachineMovesBySpecies` and
  threaded through `GameSessionManager`/`GameController`. `SignalRInput.SetMoveTeachTargetChoice` +
  `BattleHub`/`SignalRBattleEventEmitter` wiring complete the web round-trip.
- **Frontend:** `RewardChoiceModal.tsx` — a `moveTeach` reward card (move name, `TypeBadge`, PWR/ACC/PP line).
  New `MoveTeachTargetModal.tsx` — the ABLE/NOT-ABLE party-list "Teach {move}?" screen, reusing `PartyCard`
  (disabled + "· Unable" note on a NOT ABLE member) and a "Don't teach" decline button. `timeline.ts`/
  `battleReducer.ts`/`useBattleHub.ts` carry the new `moveTeach` option kind and the `MoveTeachTargetRequired`
  prompt; `BattleScreen.tsx` renders the new modal.
- **Tests:** `RewardCalculatorTests` — 4 new cases on `RollMoveTeachOption` (empty legality dict → null; a
  legality row for a different species never leaks in; a member who already knows the move yields no candidate
  for that member; a mixed-party roll marks only the eligible slot ABLE, pinning the two distinct NOT-ABLE
  reasons — no Machine row vs. already-known — land on the same `false` verdict without collapsing into one
  check). `WebEventContractTests` — 2 new `ProjectionExceptions` entries (`MoveTeachRewardOption.Move`/
  `.AbleBySlot`, deliberately absent under their own names) + 2 value-level projection tests
  (`RewardChoiceOffered_Projection_CarriesMoveTeachOptionFields`,
  `MoveTeachTargetRequired_Projection_CarriesMoveStatsAndCandidateAbility` — the latter pins a NOT ABLE
  candidate projects `Able: false` rather than being dropped or defaulted true). `timeline.test.ts` — 1 new case
  for the `moveTeach` timeline arm. Full suite green: 1573 .NET, 259 Vitest, `tsc` clean.

**`requirements-review` (2026-09-28): 5 findings, all resolved (3 fixed outright, 2 adjudicated by the user).**
1. **Fixed — real bug:** PokeAPI's `move_learn_method == "machine"` (→ `LearnMethod.Machine`) does **not**
   distinguish TM from HM, so a species' genuine Machine row for an HM (verified live: Squirtle→Surf,
   Charmander→Strength, both species→Cut) was leaking into the move-teach pool — directly contradicting locked
   decision #2 above. Fixed with a hand-verified exclusion list, the same domain-knowledge-in-code pattern
   `DATA_IMPORT.md` uses everywhere PokeAPI can't express a Gen 1 fact: `RewardCalculator.Gen1HmMoveIds =
   [15, 19, 57, 70, 148]` (Cut/Fly/Surf/Strength/Flash), filtered out of the candidate pool before scoring.
   Pinned by `RollMoveTeachOption_NeverOffersAnHm_EvenWhenTheSpeciesHasARealMachineRowForIt`.
2. **Fixed — real test-coverage gap:** the apply path (`RewardResolution.ApplyMoveTeachAsync` →
   `MoveLearning.TeachMoveAsync` → `IBattleInput.ChooseMoveTeachTargetAsync`) had zero coverage — only the roll
   (`RollMoveTeachOption`) and the wire projection were tested. New `MoveTeachRewardTests.cs` (core project,
   mirrors `QuickHealRewardTests.cs`'s end-to-end-through-`RewardResolution` shape): auto-learn into a free
   slot; the full-moveset forget-a-move prompt; and — the specific Gen 1 quirk flagged — a **fainted** party
   member is still a fully valid, selectable teach target (nothing in the path gates on `Creature.IsAlive`).
3. **Fixed — overstated fidelity claim:** decision #5's "a real Gen 1 ... screen" framing was wrong. Real Gen 1
   doesn't pre-flag ABLE/NOT ABLE — it lets you pick any member and only tells you *afterward* if it can't
   learn the move. Corrected in both this entry (decision #5, above) and `PRODUCT_SPEC.md` to name the
   ABLE/NOT-ABLE upfront picker as a deliberate roguelite QoL improvement, not a reproduction.
4. **Fixed — user's call: give move-teach a fixed rarity for gold scaling.** A move-teach substituting the
   first item slot meant `BuildChoice`'s `bestRarity` (which scales the accompanying gold bag) only ever saw
   surviving `ItemRewardOption`s — so whenever move-teach fired, the gold bag silently priced at the `Common`
   floor instead of whatever rarity the substituted item slot would have rolled (up to ~43% less gold, worst on
   Boss). Fixed by extracting the rarity resolution into `RewardCalculator.ResolveBestRarity` (now directly
   unit-testable without an RNG) and having it count a `MoveTeachRewardOption` as `RewardRarity.Rare` — it has
   no `Item`/`Cost` of its own to classify by `RarityOf`, and Rare reflects that it's a build-crafting pick, not
   a throwaway. Pinned by 3 new `ResolveBestRarity_*` tests (treats move-teach as Rare when no item survives;
   an Epic item still outranks it; falls back to Common when nothing rolled).
5. **Fixed — user's call: protect Boss's Revive odds.** The same substitution measurably cut the Boss-only
   Revive item's effective drop rate on the exact node (Boss, 35% substitution chance) where it's already the
   sole channel for that item — both item slots could roll Revive before, and move-teach displacing the first
   removed one of the two shots. Fixed: on Boss nodes only, move-teach now substitutes the **second** item slot
   instead of the first, so the first slot's own item roll — the one Revive can occupy — is never at risk,
   regardless of whether move-teach fires that visit. Every other tier is Revive-ineligible anyway (`RollItemOption`'s
   own Boss-only gate), so the slot ordering doesn't matter there — move-teach still substitutes the first slot
   off-Boss. Pinned by `RollRewardChoice_OnBoss_MoveTeachNeverDisplacesTheFirstItemSlot` (200 seeds, asserts the
   invariant on every seed that actually rolled a move-teach, and that at least one did — not vacuously true).

**`pr-review` (2026-09-28): CHANGES-REQUESTED → all 3 blockers fixed, most RECOMMENDED items also applied.**
1. **Fixed — a real seam-architecture leak.** `RewardCalculator.Gen1HmMoveIds` (the fix for
   `requirements-review` finding #1 above) was itself a Gen 1 content hardcode in web-layer runtime code — a
   later generation's HM roster differs (Gen 2 adds Waterfall/Whirlpool), so under a second profile it would
   have silently mis-classified those moves as TMs with every test green, exactly the "silently runs Gen 1"
   hazard `GenerationProfile.cs`'s own class doc warns about, and it directly contradicted this entry's own
   "Gen-variable surface: none new" claim. Fixed by promoting it to a real profile slice:
   `GenerationProfile.HmMoveIds` (required, so every profile must supply one), `Gen1Profile`'s real five ids,
   `TestAltProfile`'s deliberately-empty set as the falsification leg. The exclusion moved into
   `EncounterFactory.LoadMachineLearnsetsAsync` (the dictionary it returns is TM-only by construction now), and
   `RewardCalculator.RollMoveTeachOption` no longer filters by move id at all — it trusts whatever legality
   dictionary it's handed. Pinned by 2 new `EncounterFactoryGenerationProfileTests` (the HM exclusion reads
   `profile.HmMoveIds`, not a hardcoded list — verified against the real DB, Squirtle→Surf; and the read is
   content-scoped like every other catalog read in the file). `GENERATION_PROFILE.md` §2.1 gained a row for the
   new slice.
2. **Fixed — the missing falsification legs above** were this finding; folded into item 1 rather than listed
   twice (a new catalog read with no `TestAltProfile` probe is a ship-blocking gap in this codebase's own
   convention, `GENERATION_PROFILE.md` §5(b)/§8).
3. **Fixed — stale/inaccurate docs.** `ENCOUNTER_DESIGN.md` §5.1 now states the Boss second-slot rule, the
   Rare-for-gold rule, and the roll-time `AbleBySlot` constraint (with its actual dependency named — the reward
   roll must precede any evolution/draft offer within the same win, per `BattleRunEvent
   .GrantBattleRewardAsync`); the `RunLoop.cs` comment that used to be the sole record of that reasoning is now
   a pointer. "Live party member" (wrong — a fainted member is a valid target) corrected in `PRODUCT_SPEC.md`
   and the `RollMoveTeachOption` XML doc.
4. **Fixed — RECOMMENDED, the untested defensive path.** `RewardResolution.ApplyMoveTeachAsync`'s guard against
   a stale/tampered client pick (a NOT-ABLE slot, an out-of-range slot, or an explicit decline) had no coverage.
   New `ScriptedInput.TeachesSlot` + 4 `MoveTeachRewardTests` cases (decline still grants the reward but teaches
   nothing; a NOT-ABLE pick is a no-op; an out-of-range pick is a no-op; a `MoveTeachTargetRequired`'s
   `Candidates[i].Able` matches the roll's `AbleBySlot` exactly).
5. **Fixed — RECOMMENDED, two more `RewardCalculator` invariants.** New seeded-loop tests: off-Boss, move-teach
   always sits at index 0 when it fires (the Boss rule's inverse case); a party/legality pairing with no
   eligible candidate never rolls a move-teach and always leaves an item in the first slot (the "always falls
   back" claim, previously asserted only in prose).
6. **Fixed — RECOMMENDED, the leftover "real Gen 1" phrasing** (`requirements-review` finding #3 corrected the
   claim in the archive/spec but missed several code comments): swept `BattleEvents.cs`, `IBattleInput.cs`,
   `RewardResolution.cs`, `timeline.ts` (×2), and this file's own decision #5 above to point at
   `ENCOUNTER_DESIGN.md` §5.1's corrected framing instead of re-asserting "a real Gen 1 screen."
7. **Fixed — RECOMMENDED, stale `docs/TODO.md` pointers.** Every inline `<c>docs/TODO.md</c>` reference to this
   feature (`RunLoop.cs`, `RewardCalculator.cs` ×2, `EncounterFactory.cs` ×2, `GameSessionManager.cs`,
   `BattleHub.cs`, `WebEventContractTests.cs`) now points at `docs/TODO_ARCHIVE.md`, since the feature is
   archived, not active. `TODO.md`'s own *Known Gaps* pointer entry also had the "real Gen 1 ABLE/NOT-ABLE"
   phrasing, corrected there too.
8. **Fixed — RECOMMENDED, one more Vitest case.** No test parsed a `kind: 'moveTeach'` option's actual move
   fields (only the null-defaulted fields on other kinds were exercised); added one to `timeline.test.ts`.
9. **Deferred — advisory-level, not fixed:** a `TestAltProfile`-style wiring test for `RewardContext.Party`
   itself (proving `BattleRunEvent`/`RewardRunEvent` thread the run's real party, not a stale/default one) and a
   `battleReducer.test.ts` SHOW/HIDE pair test for `MOVE_TEACH_TARGET`. Both are real gaps of the same
   "nullable-default could silently disable the feature" shape this codebase watches for, but lower-urgency than
   the fixed items — left for whenever this file is next touched, not scheduled.
   Full suite green after all fixes: 1589 .NET, 261 Vitest, `tsc` clean, CSharpier clean.

---

## Boss/Strong "Optimal" moveset could hand a species moves it could never legally learn ✅ FIXED (2026-09-27)

*(Moved here from `TODO.md` → *Known Gaps*; the design was decided and written up 2026-09-27, then implemented
and tested the same day.)*

**The bug.** `MoveSelectionStrategy.Optimal` (the Boss tier) picked its top-N moves from the *entire* DB move
pool (`LearnsetMoveSelector.cs`'s `Select()`, `SelectBest(movesById.Values, ...)`), with zero learnability
filter — e.g. a wild Boss Scyther could roll Hydro Pump. This was the *documented* intent
(`ENCOUNTER_DESIGN.md` §3.5's table said "**any** move… ignoring legality"), not a regression — the design
itself let bosses "cheat" past Gen 1 legality, contradicting the "true Gen 1 clone" principle (`CLAUDE.md` →
Design Principles) and the Strong tier's own already-correct behaviour (`TmEnhanced` only draws from the
species' real learnset rows).

**Decided design (user's call).** `Optimal` is redefined to mean "the best moveset the species could
*legitimately* have," not "the best moveset in the abstract" — it draws from the same legal pool as
`TmEnhanced` (level-up + TM/HM `Machine` rows), never a move outside it. Since neither strategy is level-gated
and both are deterministic top-N by the same `MoveScore`, **Boss and Strong now compute the identical moveset
for a given species** — there's only one legitimate "best legal kit," so nothing is lost by them agreeing. Boss
stays distinctly stronger than Strong entirely through the other three `EnemyTierSpec` levers, which already
exceed Strong's: Perfect DVs (vs. High), level +6 (vs. +3), BST ×1.20 (vs. ×1.10) — see `EnemyArchetype.cs`'s
`BossArchetype`/`StrongArchetype`. Considered and declined: giving `Optimal` its own coverage-aware algorithm
(e.g. preferring a complementary coverage move over a second same-type move) instead of reusing `TmEnhanced`'s
plain top-N-by-score — rejected as unneeded complexity for what was asked.

**As built:**
- `LearnsetMoveSelector.cs` — `Select()`'s `Optimal` branch now shares `TmEnhanced`'s species-legal pool
  (`if (strategy is MoveSelectionStrategy.Optimal or MoveSelectionStrategy.TmEnhanced)` over the caller-supplied
  `learnset` rows), instead of `SelectBest(movesById.Values, ...)` over the whole DB. `MoveSelectionStrategy
  .Optimal`'s XML doc rewritten to match (no longer says "the entire move pool, ignoring legality").
- `EncounterFactory.cs` (~428-433) — `allowedMethods` now loads `LearnMethod.Machine` rows for `Optimal` too,
  not just `TmEnhanced` (previously `Optimal` didn't even fetch TM/HM data, which didn't matter while the
  selector ignored the learnset entirely for that strategy).
- `ENCOUNTER_DESIGN.md` §3.5's table + §3.6's integration-hazard note updated to match (Optimal no longer
  documented as "any move, ignoring legality").
- **Tests:** `LearnsetMoveSelectorTests` — `Learnset_Optimal_PicksHighestScoreMovesFromAllMoves_IgnoringLearnsetAndLevel`
  rewritten into `Learnset_Optimal_PicksBestSpeciesLegalMoves_IgnoringLevel_ExcludingIllegal` (asserts legality,
  mirroring the `TmEnhanced` test); added `Learnset_Optimal_AndTmEnhanced_AgreeOnTheSameSpecies` (pins the two
  tiers now compute identical results); fixed `Learnset_MaxMoves_CapsTheMovesetSize` (needed a matching
  learnset since `Optimal` no longer ignores it). `RunSeedReproducibilityTests` —
  `Boss_MovesetMatchesTmEnhancedOnTheSameLegalPool` added: builds 30 Boss enemies across seeds against the
  live DB, independently recomputes each species' expected `TmEnhanced` moveset from its real `Learnsets`
  rows (`Generation == 1`, `LevelUp`/`Machine` only), and asserts an *exact* match — catches not just an
  illegal move but a regression in which learn methods `EncounterFactory.CreateEnemyAsync` loads for
  `Optimal` (the §3.6 "two places change together" hazard), which a weaker "is it legal" check would miss.
  `LearnsetMoveSelector.cs`'s class summary + `SelectBest` comment also updated (caught by `pr-review`) —
  neither described Optimal as ranking a "wider"/"full" pool anymore. Full .NET suite green (1567 passed).

---

## Generation Profile 4d+ · Level-up stat panel — Kanto Sage skin ✅ COMPLETE (2026-09-22)

*(Moved here from `TODO.md` → Generation Profile → Stage 4d+. Two follow-ups stay live there: the Gen-1
level-up-box-contents domain question and the shared double-frame recipe — see the end of this section. Add-ons
for the nickname modal (2026-09-22) and the evolution + Poké Center modals and the move-replacement + game-over cards (both 2026-10-01) are recorded below.)*

**Scope: skin only, no behaviour change.** `LevelUpStatPanel` (`BattleScreen.tsx`, `.levelup-*` in
`BattleScreen.css`) is a non-blocking corner panel (bottom-right above the menu, persists until the next input,
also raised for bench Exp-Share level-ups with the creature named). Before this it had **no
`[data-generation="gen1"]` override** — a dark navy box, sky-blue title, `#44cc44` gains, 4px radius and soft drop
shadow sitting on the light Fog field (the level-up half of the "level-up modal and reward modal" surfaces flagged
2026-08-23, user-reported while playing).

**Design (interactive mockup reviewed with the user 2026-09-22; scratch file, not committed):** one
`[data-generation="gen1"] .levelup-*` override block. Frame = the `.battle-log` double-line recipe (2px ink border +
3px fill ring + 5px ink ring, `--ks-corners` step-notches, radius 0, no drop shadow) on `--ks-fill`; title splits to
`LEVEL UP!` (ink) over a dim `NAME · Lv N` sub-line (**the literal `LEVEL UP!` text stays** — `level-up.spec.ts`
asserts it); **gain = bold ink (user's call)** — the `#44cc44` green is outside the four-colour budget and only
~1.65:1 on `--ks-fill`; blue would read as an XP cue; total = `--ks-dim`. Unchanged: class names
(`.levelup-stat/gain/total`), column padding, the 160ms `levelup-pop`, position, z-index (7, above the party strip),
persist-until-input, and the **base dark rules** (the fallback for a profile with no override).

**As built:**
- `BattleScreen.tsx` — the title `<div className="levelup-title">` now wraps its pieces in spans
  (`levelup-name`, `levelup-sep`, `levelup-head`, `levelup-sep--tail`, `levelup-lv`); `textContent` is identical
  ("NAME · LEVEL UP! · Lv N"), so the base look and `level-up.spec.ts` are unaffected. The spans let the skin re-flow
  the line.
- `BattleScreen.css` — new `[data-generation="gen1"] .levelup-*` block after the base rules (~844): root gets its own
  `color: var(--ks-ink)` (DoR #6b — inherited-colour trap), the double frame + corner notches, a flex re-flow of
  the title (`.levelup-head` `order: -1; flex-basis: 100%` puts LEVEL UP! on its own line, `.levelup-sep--tail`
  hidden), name/sep/Lv dim and un-bolded, `.levelup-gain` ink (bold retained from the base rule), `.levelup-total`
  `--ks-dim` at full opacity. **No `td` padding shorthand** (DoR #6a — this exact panel was bitten before; the
  column-spacing geometry is untouched).
- **DoR:** #3 gen-variable = presentation only, scoped under `[data-generation]`, no seam touched; content/timing/
  persistence gen-invariant. #4 source of truth = `GENERATION_PROFILE.md` §7.3. #5 CSS + span markup only — no
  importer/engine/wire/DB. #7 no dependencies.

**Verified live (2026-09-22, Puppeteer, real battle — seed 1, CHARIZARD Lv5, `playToLevelUp`):** under
`data-generation="gen1"` — ink-on-fill, double-line frame, radius 0, no shadow, bold-ink gains, dim totals, LEVEL UP!
on its own line; the column-spacing geometry (gap 32px, padding 32px) still satisfies `level-up.spec.ts`'s "column
spacing" assertion; with `data-generation` removed, every computed style equals the original base dark rules
(fallback preserved).

**Add-on, same commit (2026-09-22) — NICKNAME modal skinned with the same recipe.** The user asked for
`.nickname-modal` (`components/modals/NicknameModal.tsx`, used by `StarterSelection` and `AcquisitionModal`) to get the
exact same modal CSS. **CSS only, no TSX change.**
- **Shared selector, not a copy:** `.nickname-modal` was added to the level-up frame rule as a selector list
  (`[data-generation="gen1"] .levelup-panel, [data-generation="gen1"] .nickname-modal`) — ONE shared double-line-frame
  rule for the two surfaces. This partly does the "share the recipe instead of copying a 4th time" follow-up for these
  two; `.battle-screen` / `.battle-log` still carry their own copies.
- A small block beside the nickname base rules: padding kept roomier (+8px to clear the ring), title in ink, sub-line
  `--ks-dim`, input on `--ks-fog` with a 2px ink border and a thickened inset-ring focus cue, dim placeholder.
  OK / CANCEL are `.action-btn` — already skinned, unchanged.
- **Verified live (Puppeteer, starter-select screen, seed 1, CHARIZARD):** computed styles match the level-up frame;
  the focus ring confirmed with real page focus; with `data-generation` removed, every value equals the original
  purple dark card (fallback preserved).
- **Not run:** E2E (opt-in; the nickname flow is exercised by `helpers.startBattle` in every spec). Fast tests were
  green before this CSS-only add-on and were not re-run (CSS cannot affect them).

**Not run (level-up panel):** the E2E `level-up.spec.ts` (opt-in, user-run — recommended `.\e2e.ps1 -Spec level-up`).
The bench-attributed variant was not separately screenshotted (same component/markup path). `pr-review` skipped
(CSS/markup-only diff).

**Add-on #2 (2026-10-01) — EVOLUTION prompt + POKÉ CENTER (recovery) modals skinned with the same recipe.** Both
render through the one `.recovery-modal` card (`EvolutionPromptModal.tsx`, `RecoveryModal.tsx`). **CSS only
(`BattleScreen.css`), no TSX change.**
- **Shared selector again:** `.recovery-modal` joined the Kanto Sage double-frame selector list (now level-up panel +
  nickname modal + recovery modal) and the nickname modal's roomier-padding override (clears the ring).
- A small `[data-generation="gen1"]` block restyles `.recovery-title` / `.recovery-sub` / `.recovery-glow` /
  `.recovery-sprite` to ink. Buttons are already-skinned `.action-btn`s, unchanged.
- **Deliberately not touched:** `.acquire-modal` (carries the party-swap picker and a violet accent — needs its own
  mini-plan), the reward modal, the shop.
- **Verified live (Puppeteer, injected markup + screenshot, 2026-10-01).** No tests run (CSS-only); E2E not run.

**Add-on #3 (2026-10-01) — "Tier 1" of the remaining-modals ordering: MOVE-REPLACEMENT prompt + GAME OVER card
skinned (`components/modals/`).** Same recipe: the card class is added to BOTH selector lists in
`ModalFrame.css` (the double-frame rule + the roomier-padding override) and a few ink-colour overrides go in the
modal's own sheet. **CSS only, no TSX change.**
- **`MoveReplacementModal` (`.move-replace-modal`):** added to both `ModalFrame.css` lists;
  `MoveReplacementModal.css` gets gen1 ink title / dim sub / ink question. Its `.move-btn`, `.btn-ghost`
  ("Don't learn") and `.action-btn` (YES/NO) were already skinned; the `--corner` overlay anchoring is unchanged
  (the roomier padding fits the bottom-right anchor). Verified live (Puppeteer screenshot of the choose step, real
  markup incl. `.move-name` and `.btn-ghost.action-back`, in the `--corner` overlay). The confirm step
  (question + YES/NO) was not separately screenshotted.
- **`BattleEndedOverlay` (`.battle-end-modal`, the GAME OVER card):** added to both `ModalFrame.css` lists;
  `BattleEndedOverlay.css` gets gen1 ink title / dim sub / ink stats + values. **Design decision (user,
  2026-10-01): INK-ONLY — no red accent.** The red border / glow / title were the dark skin's game-over signal;
  dropped under the four-colour budget, and the greyed faint sprite carries the beat. The heavier
  `.battle-end-overlay` backdrop is unchanged and independent of the card skin. With no profile applied the
  original dark card (red game-over included) is kept. Verified live (Puppeteer screenshot).
- **Pre-existing bug fixed while there (a base-rule change, not skin-only, in `BattleEndedOverlay.css`):**
  `.battle-end-stats td { padding: 2px 0 }` out-specified `.battle-end-stat`'s `padding-right`, so label and value
  touched ("FINAL LEVELLv14") in BOTH skins (computed `padding-right` was 0px on the default dark skin). Now
  vertical-padding-only — the same trap/fix as `.levelup-table td`. Verified before/after by screenshot. No test
  covers the game-over table geometry.
- **Verification:** `tsc` clean, Vitest 283/283; live Puppeteer checks above. **Not run:** Playwright E2E
  (opt-in, user-run), `pr-review` (skipped — CSS only), and no computed-style A/B (that was for the pure-move
  `BattleScreen.css` split).

**Add-on #4 (2026-10-01) — "Tier 2" of the remaining-modals ordering: ACQUISITION offer (`AcquisitionModal`,
`.acquire-modal`) skinned, incl. its party-swap picker and release-confirm step (`components/modals/`).** Same
frame + an existing recipe (the BAG `.bag-item` one), so no separate plan/mockup was needed. **CSS only, no TSX
change.**
- `.acquire-modal` added to BOTH `ModalFrame.css` lists (double-frame rule + roomier padding).
  `AcquisitionModal.css` gets gen1 ink title / dim sub / ink question.
- **Violet accent dropped to ink** under the four-colour budget (same call as the recovery card's green): the glow
  becomes the same soft ink halo and the sprite loses its coloured drop-shadow.
- **`.acquire-swap-btn`** (the "release which member" buttons) takes the `.bag-item` recipe — 3px ink border + grain,
  invert-block on hover/focus with the `.acquire-swap-lvl` line flipping with it; disabled (the lead, who can't be
  released) simply dims.
- **Deliberately untouched:** the `TypeBadge` pills (semantic, inline-coloured, already shown unskinned on the other
  Kanto Sage screens; an ELECTRIC pill checked legible on the light card); ADD / DECLINE / YES / NO are
  `.action-btn` and "← BACK" is `.btn-ghost`, both already skinned; the embedded `NicknameModal` was already done.
  With no profile applied the original violet card is kept.
- **Verified live (Puppeteer, real markup):** the offer step (with a real ELECTRIC badge) and the swap picker incl.
  hover-invert and the disabled lead. **Not checked:** the release-confirm step not separately screenshotted (same
  card + `.acquire-question` + `.action-btn` as already-verified pieces); no `tsc`/Vitest (CSS only); Playwright
  E2E not run (opt-in, user-run); `pr-review` skipped (CSS only).

**Add-on #5 (2026-10-01) — "Tier 4" of the remaining-modals ordering: REWARD pick (`RewardChoiceModal`,
`.reward-modal`, the surface the user flagged 2026-08-23 as "still the old pre-Kanto-Sage look") and SHOP
(`ShopModal`, `.shop-modal`) skinned in one pass (`components/modals/`).** **CSS only, no TSX change.** Unlike
Tiers 1-2 this needed a design decision first: both surfaces' colours were semantic (rarity-tinted card/row
borders, gold bag, Quick Heal and move-teach accents) and tuned for the dark ground.
- **Decision (user, 2026-10-01), from a three-way live mockup:** (A) *ink ladder* — rarity expressed in ink only
  (how much chrome a card has); (B) *rarity as gameplay signal* — a coloured rarity tag chip; (C) ladder plus two
  chips. The assistant recommended A; **the user chose B.** Ruling: rarity is gameplay signal (it tells the player
  what a pick is worth at the moment of choosing) and is exempt from the four-colour budget, **narrowly — ONLY the
  rarity tag chip carries colour**; cards/rows stay ink-on-fill; every other per-kind accent (gold bag, Quick Heal,
  move-teach, card/row border tints) drops to ink; the rarity word stays on every chip, so nothing is colour-only.
  The ruling itself is recorded in `GENERATION_PROFILE.md` §7.3 → *Clarified during the build* → "Item rarity".
- **Tokens** (`index.css`): `--ks-rarity-common` `#636760`, `--ks-rarity-uncommon` `#2d7a38`, `--ks-rarity-rare`
  `#2F63AF`, `--ks-rarity-epic` `#7a3fa3` — the old dark-ground hues retuned so white chip text holds on them.
- **Frame:** `.reward-modal` and `.shop-modal` added to BOTH `ModalFrame.css` lists (double-frame rule + roomier
  padding). `RewardChoiceModal.css` / `ShopModal.css` get gen1 ink title / dim sub.
- **Reward cards** take the BAG `.bag-item` recipe: 3px ink border + grain, invert-block on hover/focus, no lift,
  no glow; the rarity chips are coloured and stay coloured through the invert.
- **Shop rows:** ink-on-fill + grain, the rarity left-border tint dropped, same chips. Buy / Leave are ink-on-fill
  with invert on hover; a disabled (unaffordable) Buy dims.
- **Deliberately untouched:** `TypeBadge` on the move-teach card; the loot-drop popup (`.drop-hover` /
  `.drop-chip` — its own gold/green chips floating over the canvas on a dark pill, a separate surface). With no
  profile applied both modals keep their original dark gold-accented look with rarity-coloured borders.
- **Verified live (Puppeteer, real markup):** reward pick with all four rarities + gold + Quick Heal + move-teach
  and a hovered Rare card (invert, blue chip holds); shop with a hovered Buy and an unaffordable disabled Epic item
  (dims; first shot taken mid pop-in animation, retaken settled — not a defect); `tsc` clean; `vite build` OK.
  **Not run:** Vitest (CSS only), Playwright E2E (opt-in, user-run — recommend `.\e2e.ps1 -Spec shop` plus the spec
  that drives the reward pick), `pr-review` (skipped — CSS only).

**Add-on #6 (2026-10-01) — "Tier 3" of the remaining-modals ordering, the last open modal family: the ROSTER
PICKER (`.lead-*`, `components/modals/RosterPicker.css`) skinned in one pass.** Greenlit by the user ("yes, start on
the roster picker"). **CSS only, no TSX change.** One `.lead-card` skin covers five surfaces: the three modals
`LeadChoiceModal`, `SwitchInModal`, `MoveTeachTargetModal` (all `.lead-modal`) **and** the two non-modal surfaces
that render `PartyCard` / `.lead-card` on the light `.battle-panel` — the in-battle SWITCH menu (`SwitchMenu` in
`BattleScreen.tsx`) and CHECK POKEMON's party picker (`CreatureOverview.tsx`, which has its own grid override in
`CreatureOverview.css`).
- **Frame:** `.lead-modal` added to BOTH `ModalFrame.css` lists (double-frame rule + roomier padding). Gen1 rules in
  `RosterPicker.css`: ink title / dim sub-line; `.lead-modal` `min-width` 360px.
- **`.lead-card`** takes the BAG `.bag-item` recipe: 3px ink border + grain, invert-block on hover/focus (guarded
  with `:not(:disabled)`), the sky accent dropped to ink; the level line is dim and flips with the invert.
- **HP read** reuses the nameplate bar recipe (fog track + 1px ink border, 6px tall, low endpoint `--ks-hp`). The
  green/yellow thresholds are kept as gameplay signal, scoped to `.lead-card-hp-fill` so the separate party-strip
  chips are untouched.
- **States:** fainted keeps its base greyed look with an ink hover border; disabled-but-not-fainted-not-current
  (e.g. the TM '· Unable') dims to 0.55 with no invert.
- **Design call (assistant, inside the existing recipe, not escalated — revisitable):** the CURRENT lead is marked by
  the dialogue frame's INNER INK RING (inset 2px fill + 4px ink), NOT a permanent invert, because hover/selection
  already mean "invert block" in this skin and an inverted current card would read the same as a hovered one. The
  '· current' / '· OUT' word stays. The disabled current lead (SWITCH menu '· OUT') is deliberately not dimmed.
- **`CreatureOverview.css`:** the gen1 grid minimum widened 72px → 96px so a 10-char nickname fits the
  thicker-bordered card (measured: 88px borderline, 96px no overflow).
- **One small non-skin fix (base rule, ALL skins):** `.lead-modal .action-back { margin-top: var(--sp-md) }` — the
  move-teach modal's "Don't teach…" ghost button sat flush against the card grid (`action-back`'s `margin-top:auto`
  resolves to 0 in a content-height modal); measured gap now 15px. Pre-existing, made obvious by the 3px borders.
- **Deliberately untouched:** the `PartyStrip` chips (`.party-chip*`); `TypeBadge` pills. With no profile applied
  the original dark sky-accented cards are kept.
- **Verified live (Puppeteer, real markup):** lead-choice modal (6 cards, current ring, a 10-char name, low/mid/high
  HP, hover invert); move-teach modal (TypeBadge sub-line, '· Unable' card stays dim and does not invert on hover,
  fainted-but-able card stays normal as the component intends, the decline-button gap); the in-battle SWITCH menu
  (current lead ringed + not dimmed, hovered member inverts, fainted greyed); CHECK POKEMON picker with
  `CreatureOverview.css` loaded (all names fit at 96px, current ring, hover). `tsc` clean; Vitest 283/283.
  **Not verified:** `SwitchInModal` rendered on its own (same `.lead-modal` + the fainted card already verified in
  the SWITCH menu); the no-profile (dark) skin not re-screenshotted (gen1 rules are attribute-scoped; only the
  `action-back` gap rule touches all skins). **Not run:** Playwright E2E (opt-in, user-run — recommend the specs
  that drive in-combat switching, the forced switch-in and CHECK POKEMON); `pr-review` (skipped — CSS only).

**Add-on #7 (2026-10-01) — the ENCOUNTER OVERLAY + NODE LADDER skinned.** Raised by the user ("the encounter
overlay is still old style"). **CSS only, one file** (`pages/BattleScreen.css`): one cohesive `[data-generation="gen1"]`
block right after the ladder base rules. Surfaces: (1) the compact corner PEEK (`.encounter-map`, the ladder in a
small card, auto-shown at ladder changes); (2) the PINNED full-screen Run Map (`.encounter-map--pinned`: top bar with
RUN MAP title + island/biome names + close ×, the Town Map in the middle, the "Encounter Path" ladder panel, the
legend bar); (3) the NODE LADDER (`.ladder-*`) in both; (4) the shared `.type-chip` pills in the Town Map caption
(also shown in the `RouteChoiceMap` host).
- **Pinned = a framed full-screen window:** fill ground, 2px border + the double-line inset ring + corner notches,
  `padding: 10px` so the top-bar/legend/ladder rules sit inside the ring; the dark skin's surveyor grid becomes the
  shared `--ks-grain`, the vignette is dropped. Same frame as `.battle-screen` and the route-choice map, so one map
  reads as the same screen in both hosts.
- **Peek = plain boxed card** (fill + grain + 3px ink border, like the nameplates).
- **Chrome:** top bar / title / biome names / legend / ladder panel to ink/dim with 3px ink rules; close × =
  ink-on-fill with invert hover. The Town Map caption takes its `--ks` tokens back (the old host-scoped
  light-on-dark `.map-overworld` override is kept for the no-profile dark ground only).
- **Ladder tiles:** ink-on-fill + grain, 3px, square (the menu-cell recipe); ink connector spine; done dimmed
  (existing opacity); CURRENT = invert block + the existing ◄ marker.
- **Type chips:** fill pill + 2px ink border, ink name; the type-coloured icon frame stays (type colour is a budget
  source).
- **Decision (user, via a 3-option question):** node-kind colours are INK-ONLY — Boss red and Poké Center pink are
  DROPPED; the glyph (skull/heart/sword/star) + label + sub-label ("Boss — Trainer …") already carry the kind, and the
  current node is the invert block. Rejected: keep Boss red as a narrow threat signal; keep both accents. Assistant's
  framing of the difference from item rarity (which the user ruled a gameplay signal, Add-on #5): rarity had no glyph
  and its chip only carried the word, whereas node kinds have distinct glyphs. (Framing is the assistant's, not
  something the user said.) A chrome decision inside the existing colour budget, not a new exception.
- **Design call (assistant, not escalated — revisitable):** the composition — a framed window rather than flat
  full-screen panels.
- **Closes the earlier "deliberately unskinned" note:** the 2026-08-18 `pr-review` fix (caption near-unreadable on the
  pinned map's dark ground) scoped a light-on-dark override because the pinned panel was left dark pending its own
  catalog turn; this is that turn.
- **Verified live (Puppeteer, real run — New Game → Charmander → route pick → first battle → MAP):** the pinned map
  before/after (before: navy ground, gold titling, dark ladder tiles, near-unreadable caption); the padding fix (rules
  inside the frame); the settled peek on the real ladder markup (current node inverted + ◄); the no-profile peek
  unchanged (computed styles: dark `rgba(8,8,18,.92)`, 8px radius, gold-dim icon borders vs Gen 1's fill/3px
  ink/0 radius); `tsc` clean. **Not verified:** any ladder state other than "first node current" — done-node dimming,
  a Boss-current or Rest tile, a long Boss sub-label wrapping (only the base rules cover them); the no-profile PINNED
  map (only the peek's computed styles compared); the route-choice host of the type chips (same `TypeChip` component
  as the pinned caption). Pre-existing, untouched: the peek overlaps the player nameplate in a ~700px-tall window.
  **Not run:** Playwright E2E (opt-in, user-run — recommend `.\e2e.ps1 -Spec encounter-map`), Vitest (CSS only),
  `pr-review` (skipped — CSS only).

**Left open (live in `TODO.md` → 4d+):** (1) the Gen-1 level-up-box-contents domain claim — whether the
real box lists HP at all (recollection: four rows ATTACK/DEFENSE/SPEED/SPECIAL) and whether it shows gains first,
then totals on a keypress; today's five-row gain+total panel was left as-is (behaviour change, not a skin), for
`requirements-review`/a separate item; (2) promote the double-frame recipe to a shared token/selector — **now
mostly done:** level-up panel + nickname + recovery + move-replacement + game-over + acquisition + reward + shop +
roster picker (`.lead-modal`) card share one rule; only the `.battle-screen` / `.battle-log` copies remain.

---

## Level-25 Exeggcutor "with only Hypnosis and Bind" — ✅ CLOSED, NO DEFECT (2026-09-20)

**Decision (user, 2026-09-20):** a fluke — a misread of the move list, not a bug. No code changed. (Moved here from
`TODO.md` → *Known Gaps*.) The distinct **stone-evolution encounter-level gap** the same sighting prompted is a
separate, still-open entry in `TODO.md` → *Known Gaps* ("Wild/draft selection has no evolution-stage or
natural-minimum-level awareness").

**Original report and investigation (raised 2026-09-12).** A level-25 acquired Exeggcutor reportedly had only 2
moves — Hypnosis and "Bind". Checked against the real `pokemon.db`/`moves.db` data (not assumed): Exeggcutor's Gen 1
level-up learnset is exactly **Hypnosis (level 1), Barrage (level 1), Stomp (level 28)** — nothing else, at any
level, by level-up. So **the 2-move count itself is correct and expected**: at level 25, Stomp (needs 28) isn't
unlocked yet, so `CanonicalLatest` (level-up only, up to 4 moves) has only Hypnosis + Barrage available. **The "Bind"
half didn't check out:** Bind (move id 20) is a real, distinct move in `moves.db`, but it appears **nowhere** in
Exeggcutor's *or* pre-evolution Exeggcute's learnset (level-up or machine) — no code path attaches Bind to an
Exeggcutor. The two candidate explanations were (a) the user misread the move name (Barrage is an uncommon name) and
the game showed Barrage — no bug — or (b) the game genuinely displayed Bind — a real move-selection defect. The user
settled it as (a) on 2026-09-20.

---

## Wild encounter level far below the player's — ✅ ACCEPTED AS-IS (2026-09-20)

**Decision (user, 2026-09-20):** keep the `[50%, 80%]`-of-live-level band as tuned. The mechanism is documented in
`ENCOUNTER_DESIGN.md` §3.3; the band is accepted tuning, not an open design question. No code changed.

**Original report and investigation (moved here from `TODO.md` → *Known Gaps*, 2026-09-20).** Reported
(2026-09-12): a level-23 lead ran into a level-11 wild Fearow, contradicting a prior "not possible" call. The joint
code-analysis session happened (2026-09-13) — full formula, worked example, and the key fact it turned up are
written up in **`ENCOUNTER_DESIGN.md` §3.3**: `ScaleWildLevel` reads the lead's **live, current** level at the
moment of each encounter (never the level chosen at run start — that was the user's working hypothesis, and it's
wrong; `BattleRunEvent` re-reads `s.Player.Level` off the same mutable `Creature` instance every node), and at
shallow depth the raw band is a deliberate **[50%, 80%] of that live level** *before* any archetype offset, with
Weak subtracting 3 more. A level-23 lead landing an 11 is that formula hitting its own documented floor — **not a
bug, not a stale depth read, not a mismatch with an older formula version.** This closed questions (1)/(3)/(4) from
the original entry outright and narrowed (2) to the one thing left unanswered: *is a band this wide — down to 50%
of the lead's live level, which only gets more extreme as the lead outlevels a shallow biome — the tuning we
actually want, now that leads reach the 20s well inside a single biome?* The user's answer (2026-09-20): yes, keep it.

---

## Switched-in creature is the active creature ✅ COMPLETE (2026-09-20; core resolved 2026-07-18)

*(Moved here from `TODO.md` 2026-09-20 when its last open residual — the end-of-battle sweep — closed. Text below is
the former `TODO.md` section, kept as the closing record; only the sweep bullet and the accepted-limitation note at
the end are new.)*

**The requirement, in the user's words:** *"A switched-in Pokémon is for all intents and purposes the active
Pokémon, therefore all effects that happen at the end of battle happen to it as well. So it can evolve, it shares
XP, EVs, everything. Just like it would work in Gen 1 / generically in Pokémon."*

**There is no special case for a switched-in creature.** It is not a second-class participant, it does not "wait
until its next clean win", and it is not excluded from any end-of-battle effect. Anything the starting lead would
receive, a creature that took the field receives on the same terms. This governs the forced faint-switch and the
voluntary SWITCH action (both shipped — see **In-Combat Switching** below) alike.

### Why this shipped wrong (kept — it is the reason the gate was tightened)
Neither rule came from Gen 1 or from any design doc. Both were written *by the plan*, then implemented faithfully,
and `requirements-review` returned **MET** because the code matched the plan. The plan even pre-argued the point
(*"i.e. **not** a deviation, and the participant-split Exp remains the documented deferral"*), which suppressed the
domain check instead of inviting it. Two specific traps to recognise again:
- **An implementation convenience written up as design.** The evolution gate existed only because one `levelBefore`
  local belonged to the creature that *started* the battle, so a switched-in finisher "couldn't be compared against
  it". That was a five-line fix, not a design position.
- **A rule that was right by coincidence.** "Finisher earns the XP" happened to match Gen 1 only because the
  outgoing lead had fainted and a fainted participant earns nothing anyway — so it was never tested against the
  real rule, and it would have silently diverged the moment voluntary switching lands with both creatures alive.

→ `requirements-review` now escalates by default and treats plan-asserted domain facts as claims to verify
(`.claude/agents/requirements-review.md`, "Escalate by default" + the recurring-discrepancy log).

### How it closed (2026-07-18 — Innate Party XP Share)
- [x] **Evolution now applies to any creature that levelled this battle**, switched-in or not. `BattleRunEvent`
  takes a **per-party pre-battle level snapshot** (`preLevel`, per member) instead of the single starting-lead
  `levelBefore` local, and a new `EvolutionOrder` helper evolves every creature that levelled — active, forced
  switch-in, or bench — active-first then roster order. The `ReferenceEquals(active, player)` gate is gone.
- [x] **XP / Stat-Exp is SUPERSEDED, not literally "Gen 1 participation".** The user's ruling asked for the Gen 1
  participant split (one pool divided among the creatures sent out); the design session instead chose a
  deliberate **roguelite deviation** — the **Innate Party XP Share** (`RunRules.BenchXpShare`, live `0.5` in the
  web run): the active creature is paid in full (unchanged), then every **living** bench member additionally
  earns `floor(activeAward × BenchXpShare)` XP + full Stat-Exp, running the same level-up + move-learn loop;
  fainted members earn nothing. This is wider and more generous than the literal participant split, and is kept
  out of `IBattleRules` in `RunRules`, alongside the existing XP-curve deviation (see `GENERATION_SEAMS.md`).
  **At the time this closed (2026-07-18), no live conflict** with the requirement above: voluntary switching
  wasn't implemented yet, and a forced switch always leaves the outgoing lead fainted (excluded from any share
  anyway), so the only "switched-in" case then was simply the active creature, paid in full, same as before this
  change. **Once In-Combat Switching shipped (2026-07-25),** a creature switched out mid-battle while still alive
  earned only the flat `BenchXpShare` — an intended divergence when decided, but the case it was decided *about*
  couldn't happen yet. **The user reversed it on 2026-07-26** now that it can: a participant must not be paid
  less than the creature that happened to finish the fight. **Resolved 2026-07-27** by the Gen 1 participation
  split — the award is divided evenly among the live creatures that took the field, and `BenchXpShare` now pays
  only members that never fought. Full record → *Participation XP* (and *Innate Party XP Share* for the share
  itself), below. *(The **Exp. Share / Exp. All item** — a held item that pays a non-participant — stays deferred;
  it's a separate feature from this innate, always-on party share.)*
- [x] The invariant is written into `docs/STATE_MODEL.md` (the party-wide end-of-battle effects section) as a
  documented fact, not a plan claim — future `requirements-review` runs can cite it directly.
- [x] **Residual sweep — CLOSED 2026-09-20: no stray starting-lead references.** Read-only audit (no code changed)
  of `BattleRunEvent`'s post-battle path, `Battle.cs`'s end-of-battle and switch paths, and the web layer's
  `battle.Player` reads (`GameSessionManager` `ActiveCreature` / `PartyMemberAt`). Findings: in `BattleRunEvent`
  the local `player` (the starting lead) is used **only pre-fight** (foe level scaling, entry status); everything
  post-battle reads `s.Player` / `active`. `Battle` restores Transform/Mimic as each creature leaves
  (`RestoreOutgoing`), captures carried status on a voluntary switch-out, and pays XP to the active creature, the
  participants, and the bench separately. Move-learning rides the per-member evolution loop; carried status reads
  `s.Player` (the finisher).

### Accepted limitation (user decision 2026-09-20) — XP multiplier is the finisher's, for everyone
`Battle.cs` (~line 255) computes the level-based XP multiplier (`RunRules.XpMultiplierForLevel`) from the
**finisher's** level (`PlayerCreature.Level` at win time) and applies it to the **whole XP pool**, so other
participants and the bench are paid on the finisher's multiplier rather than their own. For the bench this is by
design (documented as `floor(activeAward × BenchXpShare)`); for a participant switched out while the finisher is
far higher level it is less clearly deliberate. **The user chose to keep it as is** — an observation, not a defect,
and not open work. The current-state note lives in `STATE_MODEL.md` (party-wide end-of-battle effects section).

---

## Species selection respects each species' evolution-chain floor ✅ DONE (2026-09-18)

**The gap.** Wild/draft encounters could spawn an already-evolved species below the level it could ever
legitimately reach that form — e.g. a level-20 Charizard, when Charmeleon only becomes Charizard at level 36.
Surfaced from a live question ("should evolved Pokémon only ever be encountered at the level they could
actually reach that form?"), not a pre-existing `TODO.md` backlog item. `EncounterFactory.PickByBst`'s BST-band
target and `ScaleWildLevel`'s level roll are resolved from completely independent inputs (BST vs. player
level/depth), so nothing previously stopped them disagreeing this way.

**Fix.** New `EvolutionMinLevel.Compute(speciesId, edges, rules)` (`creaturegame/Evolution/EvolutionMinLevel.cs`)
walks `PokemonEvolution` back to each species' root (iteratively, with a visited-set cycle guard) and takes
the highest floor any edge along the chain imposes. The per-trigger interpretation is a seam, not hardcoded
here: it asks the injected `IEvolutionRules.MinLevelFor(edge)` for each edge's own floor.
`Gen1EvolutionRules.MinLevelFor` returns a `Level`-trigger edge's own threshold; a `Trade`-trigger edge's floor
at `TradeEvolutionLevel` (this roguelite's no-trading stand-in already treats trade evolutions as a level-37
floor, so encounters now honor the same rule); `0` for a `Stone`-trigger edge (a stone can be used at any level
in real Gen 1, so it adds no floor of its own). A base form (no incoming edge) floors at 0.

`EncounterFactory.FilterByMinLevelAsync` applies this **before** `PickByBst` runs — filtering the species pool
down to what's reachable at the rolled level, rather than bumping a picked species' level up afterward (which
would let one unlucky species pick spike an encounter's level past the depth curve). Wired into both
`CreateEnemyAsync` (wild/Elite/Boss) and `TryBuildDraftAsync` (the draft reorders to resolve `level` before the
species pick, so it can gate the pool). Boss-catch needs no change — it copies the defeated boss's own species
*and* level, so a mismatch is structurally impossible.

**Two fallback bugs found by `requirements-review` and fixed the same day (still 2026-09-18):** the initial
cut used one unconditional "fall back to the unfiltered pool if nothing survives" rule for every caller, which
turned out to reintroduce the exact bug it fixed in two spots — (1) `CreateEnemyAsync` ran the level filter
*before* `PickByBst`'s biome-theme filter, so its emptiness check could pass against the whole (off-theme) dex
even when the themed-and-eligible slice was empty, racing past `PickByBst`'s own "never break theme"
invariant; fixed by filtering to the biome first. (2) `TryBuildDraftAsync`'s fought-only pool resets near-empty
at every biome entry, so a post-Elite-catch draft roll landing below the catch's floor was a routine
occurrence, not an edge case — and since a draft pick becomes a **permanent party member**, falling back to
the unfiltered pool there would silently hand back the exact under-leveled species this feature exists to
prevent; fixed by passing `fallback: false` and declining the offer instead. See `ENCOUNTER_DESIGN.md` §3.8
for the full writeup of both.

**Scope note — this does not close the broader "Exeggcutor" gap.** A Stone-evolved species (e.g. Exeggcutor,
a Leaf Stone evolution) gets no floor by design, so a stone-evolved species can still surface far below where
the original games would place it. That remains open — see `TODO.md` → *Known Gaps* → "Wild/draft selection
has no evolution-stage or natural-minimum-level awareness."

**Seam fix (same day, `pr-review`).** The first cut of `EvolutionMinLevel.Compute` referenced
`Gen1EvolutionRules.TradeEvolutionLevel` directly instead of going through the generation seam — a Gen-2 run
would have kept Gen 1 trade semantics silently. Fixed by adding `IEvolutionRules.MinLevelFor(edge)` (Gen 1's
implementation carries the Level/Trade/Stone logic above); `Compute` now takes an `IEvolutionRules` and only
walks edges, never branching on trigger itself. Also converted the walk from recursion to an iterative loop
with a visited-set cycle guard (a self-edge/cycle in imported data would otherwise `StackOverflowException`
the host process, not fail cleanly).

**Tests.** Full fast suite green after the fix and its two follow-up passes (.NET 1564/1564, Vitest 259/259).
`EvolutionMinLevelTests` (13 cases) pins the min-level math against the spot-checked Gen 1 chains
(Weedle→Kakuna→Beedrill, Charmander→Charmeleon→Charizard, the four Trade lines, Vulpix→Ninetales,
Poliwag→Poliwhirl→Poliwrath, a malformed cyclic-edge case) and proves `Compute` actually consults the injected
`IEvolutionRules` rather than a hardcoded Gen 1 reference. `EncounterFactoryFilterByMinLevelTests` (new, 3
cases) pins the pure filter's two fallback behaviors DB-free. `EncounterFactoryMinLevelTests` asserts the pool
exclusion actually happens against the live DB: no `CreateEnemyAsync` result falls below its species' floor
across many seeds, with no biome and across all 18 Kanto biomes — though this is an empirical check against
today's roster, not a structural guarantee (see the accepted residual in `ENCOUNTER_DESIGN.md` §3.8).
`EncounterFactoryDraftTests` (extended) asserts a draft whose only fought species is below its floor at the
rolled level declines instead of offering it.

Design write-up: `ENCOUNTER_DESIGN.md` §3.8.

---

## In-Battle Item Party-Targeting — items other than Revive can target any living party member ✅ DONE (2026-09-18)

**The gap.** Real Gen 1 shows a full party-selection screen ("Use item on which POKÉMON?") for items that
act on **persistent per-Pokémon data** — Potion/Full Restore, the status cures, Ether/Elixir, and
Revive/Max Revive — letting the player target **any living party member** (Revive: any **fainted** one), not
only the one currently on the field. `HealingItemEffect`, `StatusCureItemEffect`, and `PpRestoreItemEffect`
hardcoded `ItemEffectContext.User` (the active creature); only `ReviveItemEffect` already worked this way.
`/plan` done 2026-09-18 — checked against `GENERATION_SEAMS.md` §5.0 and confirmed **gen-invariant** (§5.0.2
records the judgment), so this shipped as a pure engine/web/frontend fix, no seam member, no importer/DB
change.

**Scope correction mid-implementation (2026-09-18): BattleStatBoost is explicitly EXCLUDED.** The plan
originally locked in "all four categories, including BattleStatBoost" on the premise that an X-item used on
a benched member is Gen-1-legal but merely wasted (stat stages reset to 0 on that member's next switch-in),
so the fix should let the pick happen rather than special-case it out. That premise was implemented, then
caught as wrong by `requirements-review` *before* commit: Gen 1 stores stat stages (and the Focus Energy /
Mist volatiles) only for the currently active battler — there is no per-party-member slot for a stat stage at
all — so the real games never show the party-selection screen for X-items/Guard Spec/Dire Hit; they apply
immediately to whoever's on the field. Letting the player pick a bench target for these would have shipped a
mechanic the cartridge doesn't have, not closed a fidelity gap. The fix was reverted for this one category
before the commit that follows this archive entry — see `GENERATION_SEAMS.md` §5.0.2 for the corrected,
final judgment. **Lesson for future item-effect work:** "every non-Ball item" is not a safe generalization —
check whether the underlying data the item touches is per-Pokémon-persistent (party-targetable) or
battle-session-only (active-creature-only) before assuming a category follows the group.

**Shipped 2026-09-18 (final, corrected scope):**
1. **Engine (`Combat/ItemEffects.cs`, `ItemAction.cs`).** Added `ItemEffectContext.ResolvedTarget`, a shared
   target-resolution property that resolves `Party`/`TargetPartySlot` to a party member, falling back to
   `User`. `HealingItemEffect`, `StatusCureItemEffect`, `PpRestoreItemEffect` now all read `ctx.ResolvedTarget`
   instead of hardcoding `ctx.User`, each requiring the resolved target to be **alive**.
   `PpRestoreItemEffect`'s `TargetMoveSlot` indexes into the *resolved target's* own moveset (Ether on a
   benched member reads that member's own PP, not the active creature's). `ReviveItemEffect` was refactored
   onto the same `ResolvedTarget` property (unifying its previously-separate target-resolution logic) without
   changing its behavior — it still requires the resolved target to be **fainted**. `BattleBoostItemEffect`
   deliberately keeps reading `ctx.User` directly (never `ResolvedTarget`) — the one category with no
   party-target scope to close. `ItemAction.AnnounceTargetName` was replaced by reading
   `ctx.ResolvedTarget.Name` directly (dedup).
2. **Web.** No wiring change needed — `TargetPartySlot` was already threaded generically end-to-end from
   `ItemTurnChoice` through `Battle`/`ItemAction`/`BattleHub`/`SignalRInput`, built for Revive but never gated
   to it.
3. **Frontend (`battle/bag.ts`, `pages/BattleScreen.tsx`).** `needsPartyTarget(item)` now returns true for
   every category **except BattleStatBoost**; `partyTargetMode(item)` returns `'fainted' | 'living'` (Revive
   vs. everything else) for the categories that do need a pick. `ReviveTargetPicker` was generalized into
   `PartyTargetPicker`, parameterized by mode and disabling ineligible members. `BagMenu`'s `pick()` shows the
   party picker only when `needsPartyTarget` is true — an X-item still goes straight to `onUse(item.id, null,
   null)`, unchanged from before this feature. For Ether/Elixir (`needsMoveTarget`), picking a party member
   then shows the existing `PpTargetPicker` scoped to that member's own moveset — fetching it via
   `GET /api/game/{gameId}/player/{slot}` (reusing the CHECK POKEMON per-slot endpoint, `overviewSlotUrl`
   helper, 2026-09-16) when the picked member isn't the active creature.

**Tests:** `Unit/ItemEffectTests.cs` — bench-target + fainted-target-refusal cases for Healing/StatusCure/
PpRestore, plus one regression test (`XAttack_IgnoresAnyPartyTargetSlot_AlwaysBoostsTheActiveCreature`)
pinning that `BattleBoostItemEffect` ignores any supplied `TargetPartySlot` and always resolves `ctx.User`.
One new full-`Battle` integration case in `Integration/ItemActionBattleTests.cs`
(`UsingPotion_OnABenchMember_HealsThatMemberNotTheActiveOne`). `bag.test.ts` updated for the corrected
`needsPartyTarget`/`partyTargetMode` split (plus new coverage for `moveSourceForPartyPick`, see below). Full
suite green: 1544 .NET tests, 259 Vitest tests, clean `tsc --noEmit`, clean CSharpier.

**Manually verified in-browser:** bag → Potion → party picker appears listing the party → picking the (only,
full-HP) member correctly refuses ("It won't have any effect!"), and after taking damage, using it again
correctly heals and announces "Used POTION on BULBASAUR!". The bench-target case specifically was not
live-verified (would have required grinding to a second party member in a fresh run) — covered by the
engine/integration tests above instead.

**Design-doc updates:** `GENERATION_SEAMS.md` §5.0.2 records the corrected gen-invariance judgment (split by
category, X-items excluded); `ARCHITECTURE.md` §2.11 and `PRODUCT_SPEC.md` §5 describe the final shipped
state.

**Gate adjustments (`pr-review`, 2026-09-18, CHANGES-REQUESTED → both blockers fixed):**
1. **Missing `PartyUpdated` on the three new categories.** `ReviveItemEffect` alone emitted the roster-panel
   repaint after a bench-targeting use; Healing/StatusCure/PpRestore took the bench-targeting *capability*
   without inheriting that hook, so a bench heal/cure/PP-restore would have left the party strip stale until
   an unrelated later snapshot. Fixed by moving the emit out of `ReviveItemEffect.Apply` and into
   `ItemAction.ExecuteAsync` — `if (_party is { } party && !ReferenceEquals(ctx.ResolvedTarget, Source))` —
   so every category gets it uniformly off one hook, and the active-creature case (no snapshot needed) is a
   single shared guard rather than four per-effect judgment calls. `UsingRevive_RestoresAFaintedBenchMemberAndConsumes`
   was strengthened to assert the snapshot's HP *and* cleared status; `UsingPotion_OnABenchMember_...` gained
   the same HP assertion; a new `UsingPotion_OnTheActiveCreature_EmitsNoPartySnapshot` pins the negative case.
   Two now-redundant unit-level assertions in `ItemEffectTests.cs` (which drive the effect directly, bypassing
   `ItemAction`) were removed since the effect no longer owns this emit.
2. **Stale `TargetPartySlot` contract docs.** `IBattleInput.ItemTurnChoice`, `BattleHub.UseItem`, and
   `useBattleHub.useItem`'s doc comments still described the parameter as Revive-only after this feature
   broadened it; corrected on all three wire legs, plus a stale "self-targeting items" phrase in
   `GameSessionManager.ProjectBagView`'s doc comment.

Also addressed from the same review (non-blocking): an overstated `PartyTargetPicker` comment (eligibility is
alive/fainted only, not full precondition-awareness); a 4th copy of the X-item rationale in `bag.ts` trimmed
to a `GENERATION_SEAMS.md` §5.0.2 pointer; a `GENERATION_SEAMS.md` XP-tuning pointer corrected to name
*Reward Visibility & XP Pacing* (where the multiplier anchors actually live) alongside *Participation XP*; a
stale `ReviveTargetPicker` name in a `BattleScreen.css` comment; `ClearStatus`'s parameter renamed
`user`→`target`; and the `handlePartyPick` `!gameId` guard (unreachable in practice) now surfaces
`moveFetchError` instead of silently returning. The bundled, unrelated `GENERATION_SEAMS.md` prose-condensation
pass from earlier in the same session was reviewed and kept in this commit (`pr-review` verified no content
was lost — it survives in `STATE_MODEL.md` / `TODO_ARCHIVE.md`).

---

## CHECK POKEMON party-member picker ✅ COMPLETE (2026-09-16)

**The gap, raised 2026-09-12 by the user, scoped to Tier 3:** `CreatureOverview.tsx` fetched exactly one
endpoint, `GET /api/game/{gameId}/player`, with no slot/index parameter and no UI to choose a bench member — it
rendered whatever came back. That endpoint's backing call, `GameSessionManager.GetPlayerCreature` →
`ActiveCreature(battle.Party, battle.Player)`, was hardcoded to resolve **the active/lead creature only**; there
was no per-slot read path. The party roster was already wired for other surfaces (`GET /api/game/{gameId}/party`
+ `PartyUpdated`/`PartyStrip` return a lightweight per-member summary used by the party strip and the SWITCH
menu), but that summary wasn't enough for CHECK POKEMON's INFO/STATS/MOVES tabs, which need the full
`PlayerOverviewDto` (actual stats, DVs, Stat-Exp, XP, full move data) — until this feature, only ever built from
the active creature.

**Shipped 2026-09-16, both pieces of the gap:**
1. **Backend:** `GameSessionManager.PartyMemberAt` — a pure resolution rule mirroring the existing
   `ActiveCreature` pattern — plus a `GetPlayerCreature(gameId, slot)` overload and an internal test-only seam
   `ActivePartyFor(gameId)`. New endpoint `GET api/game/{gameId}/player/{slot}` (`GameController.GetPlayerSlot`)
   reads an arbitrary party slot, fainted/benched included, 404 on unknown game or out-of-range slot. The
   existing no-slot `/player` endpoint is untouched.
2. **Frontend:** `CreatureOverview.tsx` now takes a `party: PartyMember[]` prop, tracks a selected slot
   (default: the lead), fetches from the new slotted endpoint, and renders a picker row of `PartyCard`s (reusing
   the same shared component `SwitchMenu`/`LeadChoiceModal`/`SwitchInModal` already use — no disabled states,
   since fainted/benched members are exactly what this view is for) whenever there's more than one party member.
   `BattleScreen.tsx` passes `party={state.party}` through at the call site. Minor CSS addition in
   `CreatureOverview.css` for the picker container plus a Kanto Sage gen1-skin border-color patch consistent
   with the header's existing pattern.

**Tests:** `PartySlotResolutionTests.cs` (pure `PartyMemberAt` rule: valid slot, out-of-range,
pending-session-slot-0-only, a fainted member returned, lead-tracking across a switch), `PlayerSlotEndpointTests.cs`
(end-to-end `GameController.GetPlayerSlot` coverage against a genuine multi-member party wired into an active
battle — each slot returns its own member's sheet, a fainted bench member is still returned, 404 on out-of-range
slot, 404 on unknown game id, and that its slot ordering matches `GetParty`'s projection ordinal-for-ordinal), and
`overviewPicker.test.ts` (the frontend picker rules pulled into a pure helper — `pr-review` 2026-09-18 flagged the
backend integration tests as the only coverage of a feature whose whole reason to exist is client-side picking;
`defaultOverviewSlot`/`showOverviewPicker`/`overviewSlotUrl` now pin the default-slot fallback, the show/hide
gate, and the resume-safe URL fallback directly).

**Manually verified in-browser:** single-member CHECK POKEMON regression-verified against the new endpoint (the
picker correctly stays hidden with only one party member). Multi-member picker *rendering* itself was **not**
verified live in-browser (three attempts to reach a 2-member party via real gameplay were lost to RNG crits at
the boss fight) — that gap is narrowed to rendering only now that `overviewPicker.test.ts` pins the picker's
decision logic; a live multi-member playtest remains open, unblocking follow-up work if a rendering-level defect
ever surfaces there.

---

## Session Resume — refresh/reopen-safe `gameId` persistence ✅ COMPLETE (2026-09-14)

**The gap, raised 2026-09-12, scoped to Tier 3 (lightweight only) on 2026-09-12:** `BattleScreen` only ever read
`gameId` from react-router nav state (`location.state?.gameId`, set once by `StarterSelection`'s
`nav('/battle', {state:{...}})`) — a hard refresh, closed/reopened tab, or pasted/bookmarked `/battle` URL wiped
that state even though the server's reconnect infrastructure (`ARCHITECTURE.md` §2.7: 40s `ReconnectGrace` after
a dropped connection, per-event connection re-resolution, gold/party rehydrated on `onreconnected`) might still
have the run alive. The heavier `PlayerSave`/`save.db`-backed resume (survives a server restart/redeploy, not
just a client refresh) stays deferred to Tier 5, unaffected by this.

`/plan` done 2026-09-14 — gen-variable surface: none (pure web-session plumbing, no `IBattleRules`/`ITypeChart`/
`IStatCalculator` touched, no importer/DB change, no `save.db` need, matching the Settings Menu precedent for
"when a feature doesn't need persistence infra"). Shipped and manually verified in-browser 2026-09-14. All five
planned pieces landed, plus two additional real bugs found and fixed live during manual verification — not in
the original plan.

**A second gap found during the `/plan` itself, fixed as a prerequisite:** if a client attached with an unknown
or already-expired `gameId` (the realistic case for a stale/late resume attempt), `GameSessionManager
.AttachConnection` used to no-op — no active battle, no pending session, nothing thrown. The SignalR connection
itself still succeeded, so the client's `conn.start().then(...)` resolved and the UI sat on "Connecting…"
forever with no error, no timeout, no way out but a manual reload. A resume feature that can attempt a stale
`gameId` needed this fixed first, independent of the persistence work itself — see piece 4 below.

**Shipped — the five planned pieces:**
1. **Persist on run start.** `ClientApp/src/utils/activeGame.ts` (+ `activeGame.test.ts`, mirrors the existing
   `utils/settings.ts` `localStorage` pattern): `saveActiveGame({gameId, species, level, generation})` (stamps
   `savedAt`), `loadActiveGame()`, `clearActiveGame()`. Key: `creaturegame.activeGame`.
   `StarterSelection.confirm()` calls `saveActiveGame` right before its existing
   `nav('/battle', {state:{...}})` — same shape, so the persisted entry and the nav-state shape never drift
   apart.
2. **`BattleScreen` falls back when nav state is empty.** When `location.state` itself is absent (the direct
   refresh/reopen/bookmark case), all four fields (`gameId`/`species`/`level`/`generation`) are read from the
   persisted entry instead of just `gameId` — so the pre-first-server-event render (species sprite, HP estimate,
   generation theme) works, not just the SignalR reattach.
3. **Title Screen "Continue" affordance.** `TitleScreen` reads `loadActiveGame()` on mount; if an entry exists,
   renders a `▶ CONTINUE — {species.name} (Lv {level})` button that calls `nav('/battle', {state:
   loadActiveGame()})` — the *same* nav-state shape `StarterSelection` produces, so `BattleScreen` needs no
   "was this a Continue click" branch.
4. **Fix the silent-hang bug.** `GameSessionManager.AttachConnection` now returns `bool` (attached vs.
   unknown/expired `gameId`, both existing early-return paths). `BattleHub.OnConnectedAsync` throws
   `HubException` when it returns `false` — turns the infinite "Connecting…" hang into a normal, catchable
   connection failure.
5. **Failed-resume UX.** `useBattleHub.ts`'s guarded `bounceForFailedConnection` (both its `conn.start().catch(...)`
   and, per bug 7 below, `conn.onclose(...)`) calls `clearActiveGame()` and navigates to `/` with
   `state:{notice: "Couldn't connect to the run — it may have expired."}`; `TitleScreen` renders
   `location.state?.notice` if present. `clearActiveGame()` is also called when a run ends normally
   (`state.phase === 'ended'`) and when the player hits QUIT — an explicitly-quit or -finished run doesn't offer
   to "Continue" back into it.

**Two additional real bugs found and fixed during manual in-browser verification — not in the original plan:**
6. **Full-remount reconnect deadlock.** A hard refresh restarts the client's React state at `initialState`, and
   the state-establishing events (below) each fire exactly once, so a reconnect after a refresh reattached the
   transport but left the UI stuck on "Connecting…" forever (a real deadlock, verified live). Fixed by having
   `SignalRBattleEventEmitter` (web layer) cache the last such event in each of three lifetimes as it passes
   through, and exposing `ReplayLastKnownState()`, which `GameSessionManager.ReEstablishClient` calls from the
   reconnect branch. `ActiveBattle.Emitter` was retyped from `IBattleEventEmitter?` to the concrete
   `SignalRBattleEventEmitter?` (the only construction site) so the reconnect branch can call it.
7. **`HubException` fires too late to reject `conn.start()`.** A `HubException` thrown from `OnConnectedAsync`
   closes the connection *after* the transport handshake completes, so `conn.start()` in `useBattleHub.ts`
   **resolves** rather than rejects — the existing `.catch()` never fired, so the client still hung silently on
   a truly-expired resume attempt (verified live: waited out the 40s `ReconnectGrace`, confirmed the hang, then
   fixed it). Fixed by adding a `conn.onclose(...)` handler alongside the existing `.catch()`, both funneling
   through one guarded `bounceForFailedConnection` helper — guarded by a `torndown` flag so an intentional
   teardown (QUIT, battle end, unmount, React StrictMode's dev-only double-invoke) never bounces a player who
   already left on purpose. The StrictMode false-positive was also found and fixed live during this same pass.

**`pr-review` round (CHANGES-REQUESTED, both addressed before commit):**
- **The replay cache was incomplete.** Bug 6 above shipped covering only `BattleStarted`/`TurnStarted` — but
  `RegionMapRevealed` (once per run), `BiomeEntered`, and `BiomeNodePlanRevealed` (once per biome) are *also*
  state-establishing and were never replayed, so after any successful resume the Encounter Map overlay stayed
  empty and boss-trainer framing degraded to generic for the rest of the run. Extended the same cache to all
  five events, across three lifetimes: run-scoped (`RegionMapRevealed`, set once, never cleared), biome-scoped
  (`BiomeEntered`/`BiomeNodePlanRevealed`, replaced each new biome, *not* cleared by `BattleEnded`/`RunEnded` —
  the current biome persists across and after its battles), battle-scoped (unchanged from bug 6). Replay order:
  presentation echo → map → biome → node plan → battle → turn.
- **Design-rationale placement.** The session-model change (reject-on-unknown-`gameId`, the replay cache, the
  client's persisted-resume + bounce policy) had no design-doc entry — 8 code comments pointed at this archive
  section instead, which the repo's own rule (`DEV_STANDARDS.md` → Design Rationale Placement) treats as the
  same defect as no comment at all. Added the "Session resume corollary" to `ARCHITECTURE.md` §2.7 and trimmed
  all 8 comments to point at it.
- **Recommended fixes also applied:** (1) a real thread-safety bug — `ReplayLastKnownState` runs on the SignalR
  hub thread while `Emit` runs on the run's background task thread, both reading/writing the cache fields with
  no barrier, and the replay's own re-entry into `Emit`'s cache-update logic could wipe a concurrently-live
  event — fixed by splitting `Emit` into cache-update + a private `Send` (the actual dispatch), with
  `ReplayLastKnownState` calling `Send` directly (bypassing the cache-update entirely) and the cache fields
  marked `volatile`; (2) the reconnect branch's two-call sequence (echo + replay) had no dedicated test — pulled
  into `GameSessionManager.ReEstablishClient` and pinned against a primed emitter; (3) the original
  `AttachConnection` true-return test had a real race (its `NoDbEncounterFactory`-backed run task could fault
  and remove itself from the active set before the reconnect assertion ran) — fixed by switching it to the
  deterministic gated factory, now shared as `TestSupport/BlockedEncounterFactory.cs` (promoted from a duplicate
  inline in `GenerationProfileTests.cs`, pure dedup).

**Tests:** `SessionResumeTests.cs` (11 facts — `AttachConnection` bool-return coverage, the five-event
replay/cache-lifetime coverage, and the `ReEstablishClient` order pin). `RecordingHubContext`,
`NoDbEncounterFactory`, and `BlockedEncounterFactory` were extracted from `GenerationProfileTests.cs` into
`tests/creaturegame.Tests/TestSupport/` for reuse (pure dedup, no behavior change there). `activeGame.test.ts`
(round-trip save/load/clear; `loadActiveGame()` returns `null` on an empty/corrupt store).

**Manually verified in-browser (Puppeteer):** fresh run has no Continue button; hard refresh mid-battle
reattaches with full interactivity (enemy sprite/name/level/HP, player HP, move list with STAB/effectiveness, a
real attack resolves server-side); Continue button from Title Screen works within the grace window; after the
40s reconnect grace genuinely expires, Continue now cleanly bounces to Title with the "Couldn't connect to the
run — it may have expired." notice instead of hanging.

**Known, deliberately out-of-scope gap — not a defect in what shipped:** the replay cache only covers a
reconnect *during an active run/biome/battle*. A refresh while a between-node blocking prompt is open (route
choice, shop, reward-choice, recovery, acquisition, lead-choice, switch-in) is **not** covered — those events
aren't cached/replayed, so that case is unchanged from before this feature (still hangs on "Connecting…", no
worse than pre-existing, just not newly fixed by this pass). Tracked as a named follow-up in `TODO.md` →
*Known Gaps*.

---

## Creature Naming — nickname on acquisition (session-scoped) ✅ COMPLETE (2026-09-14, Stages A + B)

**The ask, in the user's words:** *"a feature for all pokemon acquisition paths where we can give the pokemon a
name (within the session context)."* Session-scoped is explicit — this is not asking for `save.db` persistence
(there is none yet; see `TODO.md` → **Game Loop & Progression**), just the ability to set a display name for the
run's lifetime, the way Gen 1 asks "Do you want to give a nickname to X?" whenever a Pokémon joins the party.
`/plan` finished 2026-09-13; Stage A shipped 2026-09-14; Stage B shipped 2026-09-14. Every acquisition path —
starter, themed draft, boss catch — now offers the nickname step; there is no further open work under this
feature.

**Current state at plan time, checked in code:** `Creature.Name` (`creaturegame/Creatures/Creature.cs`) was a
plain settable `string`, populated from the species name (uppercase) at creation (`EncounterFactory.BuildCreature`,
the single builder shared by the starter, the themed-draft supplier, and the boss-catch supplier — `new
Creature(species.Name.ToUpper())`) — no separate `Nickname` field, and `Name` is what every surface already
displays (nameplates, battle log, party strip). Confirmed no code anywhere looks up a creature by `Name` as an
identity key (no `.Name ==` / `Find`/`FirstOrDefault` matches in the engine; *2026-09-29 note: true of the engine,
but the web client did key on names — fixed by *Creature Identity — id-keyed events*, above*) — every internal reference is by
slot/reference/`SpeciesId`, so setting `Name` to an arbitrary player-chosen string at creation time doesn't
collide with anything downstream. Confirmed the wire already carried `Name` end-to-end with **zero schema change
needed**: `BattleStarted.PlayerName` and the acquisition events (`CreatureAcquired`, `PartyUpdated`'s
`PartyProjection.Snapshot`) all echo the live `Creature.Name` field, so setting it *before* the creature is
registered/deposited is sufficient — a presentation + wiring feature, not a data-model or event-schema change.

**Scope decisions (`/plan` pass):**
- **Optional, with species-name default** — matches Gen 1's Y/N decline behaviour exactly; a blank/whitespace
  nickname is not an error, it's "declined," and the creature keeps the name `BuildCreature` already gave it.
- **Max length 10, silently truncated (not rejected).** **Gen 1 source of truth:** Red/Blue's nickname entry
  caps at 10 characters (the in-game keyboard has no more slots). *(Gen 6+ later raised the cap to 12; noted as
  a theoretical future-generation difference, not implemented — kept as a plain constant for now, the same call
  already made for party size 6 and draft cadence in Encounter Logic Phase 4: run/UI-layer tuning, not a seam.
  `GENERATION_PROFILE.md`'s surface catalog, Tier 2, is the natural home if a later generation's own limit is
  ever modeled.)* An over-length input truncates rather than errors, matching this repo's existing
  fallback-not-reject convention (`GameController.ParseDifficulty`/`ParseGeneration`).
- **Gen-variable surface: none.** Pure presentation + one shared engine-side helper; no
  `IBattleRules`/`ITypeChart`/`IStatCalculator` touched.
- **Data vs runtime boundary:** a new shared helper in the **core** `creaturegame` lib (needed by both
  `creaturegame.Web`'s `GameController` and the core lib's `AcquisitionResolution`), plus wiring at the three
  acquisition touchpoints. Zero importer/DB change; session-scoped only — no `save.db`.

**Two gaps found asking the user to confirm the plan (2026-09-14), folded in before implementation:**
1. **Species name must stay visible in CHECK POKEMON once `Name` can be a nickname.** `PlayerOverviewDto` /
   `CreatureOverview.tsx` showed only `Creature.Name` — nothing else on the creature recorded the species' own
   display name, so nicknaming would silently make the species name unrecoverable on that screen.
2. **Evolution clobbered the display name unconditionally** (`Creature.Evolve`: `Name = newForm.Name.ToUpper()`,
   no condition) — a latent bug that only mattered once `Name` could hold a player nickname: pre-feature it was
   a no-op (a pre-nickname creature's `Name` already *is* its species name), but once nicknames shipped this
   would erase a nickname on the creature's very next level-up. Gen 1 preserves nicknames through evolution —
   fixed in the same change.
3. **The nickname entry is its own cancelable step, not just an inline field** — matching Gen 1's own "Do you
   want to give a nickname to X?" prompt, which can be back-cancelled without losing the Pokémon/undoing the
   accept.

**One shared fix underlies #1 and #2 — `Creature.SpeciesName`:** a new field holding the species' own display
name, independent of `Name` (which may now be a nickname). `Creature(string name)` sets **both** `Name` and
`SpeciesName` to the same value at construction — every existing caller/test is unaffected, since nothing
diverges the two until a nickname is applied. `Evolve(newForm)` became: `if (Name == SpeciesName) Name =
newForm.Name.ToUpper();` (only the *display* name is still the default — safe to advance it) **then**
`SpeciesName = newForm.Name.ToUpper();` (always advances) — so an un-nicknamed creature evolves exactly as
before, and a nicknamed one keeps its nickname across the evolve.

**Shared building blocks:**
- **`creaturegame/Creatures/NicknameRules.cs`** (core lib) — `public const int MaxLength = 10;` and
  `public static string Normalize(string? raw, string fallback)`: trims, falls back to `fallback` (the
  creature's existing species-derived `Name`) on null/blank/whitespace-only, else truncates to `MaxLength`.
  Pure function, directly unit-testable (`NicknameRulesTests`: blank/null/whitespace → fallback; exact-length
  kept; over-length truncated; surrounding whitespace trimmed before the length check).
- **`PlayerOverviewDto` gained `SpeciesName`** (from `c.SpeciesName`); `PlayerOverview.ts` mirrors it;
  `CreatureOverview.tsx`'s header shows it next to the nickname **only when it differs from `Name`** (an
  un-nicknamed creature's header is pixel-identical to pre-feature — `data.name === data.speciesName` there).
- **`components/modals/NicknameModal.tsx`** (shared by Stages A and B) — a text input (`maxLength=10`,
  mirroring `NicknameRules.MaxLength`; placeholder = the species default name) with **OK** and **CANCEL**
  buttons, plus Escape-to-cancel (`dismiss={{ onEscape }}` — this step only ever runs *after* the
  accept/confirm decision and *before* the network call, so it draws only state the client already has and
  leaving it costs nothing, same reasoning as the existing Settings modal). Cancel/Escape and OK-with-blank-text
  both resolve to "no nickname" (the caller applies `NicknameRules.Normalize` either way); a single callback,
  `onDone: (nickname: string | null) => void`.

**Stage A — Starter selection + shared groundwork ✅ DONE (2026-09-14):** `Creature.SpeciesName` + the
`EvolveTo` nickname-preservation fix, `NicknameRules` (core lib), `PlayerOverviewDto`/`PlayerOverview.ts`/
`CreatureOverview.tsx`'s species-name display, the shared `NicknameModal` component, and the starter path
itself. `StartGameRequest` gained `string? Nickname`. `GameController.Start` calls `setup.Player.Name =
NicknameRules.Normalize(req.Nickname, setup.Player.Name);` **before** `RegisterSession`, so
`BattleStarted.PlayerName` already reflects it with no event change. Frontend: `StarterSelection.tsx`'s CONFIRM
button opens `NicknameModal` (client-side only, no request yet) instead of POSTing immediately; `onDone` fires
the existing `confirm()` POST with `nickname: nickname?.trim() || undefined` added to the body. Covered by
`NicknameRulesTests`, two new `EvolveToTests` cases (nickname preserved / default still advances), two new
`PlayerOverviewDtoTests` cases (species name mirrors an un-nicknamed `Name`; reported separately from a
nicknamed one), and `GameControllerNicknameTests` (the real `Start` wiring line against the live DB) —
`.\test.ps1 -Dotnet -Web` green, no regressions. No Vitest component coverage added: this repo's Vitest suite is
pure-logic `.ts` tests only (no React Testing Library / jsdom is installed for `.tsx` component tests) —
verified instead live in-browser (Puppeteer): nickname entered → OK → battle nameplate and CHECK POKEMON both
show it; CHECK POKEMON also shows the species name alongside it; CANCEL discards the typed nickname and the run
still starts normally with the species-default name (not aborted).

**`requirements-review` (2026-09-14) found 3 discrepancies on Stage A, user-adjudicated:** (1) **fixed** —
`NicknameRules.Normalize` now uppercases every nickname (`NicknameRulesTests` extended); Gen 1's real
nickname-entry screen offered uppercase letters only (no lowercase keyboard until Gen 2), so a mixed-case
nickname was impossible in the actual games — `NicknameModal`'s input also previews this visually
(`text-transform: uppercase`, cosmetic only; `Normalize` is the real enforcement). (2) **fixed** — the plan's
own "Out of scope" note wrongly claimed the CHECK POKEMON species-name display "matches Gen 1"; corrected to
call it what it is, a modern QoL addition (Gen 1 has no screen where a nickname and species name appear
together at all). (3) **fixed** — feasible and cheap (this repo already has a live-DB
`EncounterFactory`/`GameSessionManager` test pattern, `SpeciesControllerTests`/`EncounterFactoryDraftTests`), so
added `GameControllerNicknameTests` rather than waiving: exercises the real `GameController.Start` wiring line
against the live `pokemon.db`, asserting a supplied nickname is applied (uppercased) and a null/blank/whitespace
one falls back to the species default. Verified as sound by the same review, no change needed: the
10-character cap, the blank-input decline behaviour, and the `Name == SpeciesName` evolution check (which
mirrors the real games' own "nickname it the same to freeze the name through evolution" mechanic) are all
Gen-1-faithful as designed.

**`pr-review` (2026-09-14) then found 1 stale-gate blocker + 1 real defect on Stage A, both fixed:** (1) the
format gate's PASS was stale (`GameControllerNicknameTests.cs` landed after it ran) — re-ran
`csharpier format .`, clean. (2) **the evolution announcement repeated a nickname instead of naming the
species** — `CreatureEvolved` carried only `ToName` (the creature's live display name, which the Stage A fix
now preserves as a nickname through evolution), so a nicknamed creature's "X evolved into Y!" log line read
"SPROUT evolved into SPROUT!" — reachable immediately since the *starter* can be nicknamed and evolve mid-run,
and it directly contradicted `PRODUCT_SPEC.md`'s new "a nickname survives evolution" claim. Fixed per the
user's framing ("show the evolved name once, but otherwise the game should use the nickname"): `CreatureEvolved`
gained a `ToSpeciesName` field (the evolved species' own name, sourced from the identity already computed
pre-evolution in `BattleRunEvent.TryEvolveAsync`); the one-time announcement (`ConsoleBattleEventEmitter`,
`timeline.ts`'s log line) now reads `ToSpeciesName`, while `ToName` keeps driving
`CREATURE_RENAMED`/identity retargeting (the live nickname) everywhere else, unchanged. New/updated coverage:
two `WebEventContractTests` cases (the field projects; a nicknamed case where `ToName`/`ToSpeciesName` diverge),
a `RunDirectorEvolutionTests` nicknamed-player case, and two `timeline.test.ts` cases (the existing un-nicknamed
case plus a new nicknamed one). **3 further RECOMMENDED cleanups also taken:** `STATE_MODEL.md`'s
permanent-fields row now lists both name fields; `StarterSelection.tsx`'s POST-body construction (including the
nickname-presence and seed-parsing rules) became a pure `utils/startGameRequest.ts` module with its own Vitest
coverage, closing the promised "Vitest coverage for `StarterSelection`'s request body" without needing a
component-test harness this repo doesn't have.

**Stage B — Themed draft / boss catch accept ✅ DONE (2026-09-14).** Shared plumbing — both channels reuse
`AcquisitionResolution`, so there is no separate Stage C; `ThemedDraft`/`BossCatch` share Stage B's plumbing
entirely (`AcquisitionResolution` is channel-agnostic already). Shipped exactly per the pre-written design:
- `AcquisitionDecision` (`creaturegame/Combat/IBattleInput.cs`) gained `Nickname` (+ `Add(nickname)`/
  `Replace(slot, nickname)` overloads).
- `AcquisitionResolution.OfferAndDepositAsync` applies `NicknameRules.Normalize(decision.Nickname, offered.Name)`
  before both the open-slot and full-party-swap deposit branches, before `party.Add`/`party.Replace` and before
  emitting `CreatureAcquired`/`PartyUpdated` — so both emitted events already carry the chosen name (no event
  schema change, same reasoning as Stage A).
- `BattleHub.RespondAcquisition` gained a third `string? nickname` param, forwarded into `AcquisitionDecision`;
  `SignalRInput.SetAcquisitionDecision`/`AcquisitionContext`/`ChooseAcquisitionAsync` unchanged internally (the
  decision object already flows through).
- Frontend: `AcquisitionModal.tsx` now opens the shared `NicknameModal` (from Stage A) after the ADD button or
  the swap-confirm YES button, before calling `onRespond(true, slot, nickname)` — the offer/swap/confirm-release
  phases stay `dismiss="blocking"` exactly as before (a real server await); only the new nickname phase is
  escapable, and escaping it still completes the accept (with no nickname), never re-opens the swap picker or
  declines the offer. `useBattleHub.ts`'s `respondAcquisition` forwards the nickname arg to the SignalR hub call.
- Tests added: `RunDirectorAcquisitionTests` gained 3 new cases (open-slot nickname applied, blank nickname
  keeps species default, full-party-swap nickname applied+truncated); `BattleScenario.cs`'s
  `ScriptedInput.AcceptsAcquisition`/`AcceptsAcquisitionReplacing` gained optional nickname params.
- **Also fixed while doing this (a real gap Stage A left behind, not scope creep):** Stage A shipped without
  updating `creaturegame.Web/ClientApp/e2e/helpers.ts` — the starter's CONFIRM click now opens a nickname modal
  that nothing in the E2E suite answered, so `startBattle()` (used by nearly every E2E spec) would have stalled
  on it once E2E actually ran against post-Stage-A code. Fixed by adding `answerNicknameIfPresent(page)` to
  `helpers.ts` and wiring it into both `startBattle()` (after CONFIRM) and `playCurrentRunUntil()`'s loop (after
  an ADD click, for the new Stage B nickname step too). Also updated `e2e/acquisition.spec.ts`'s existing "ADD
  deposits" test to clear the new nickname step, and added a new test asserting a typed nickname (not the
  species name) reaches the party chip.
- `.\test.ps1 -Dotnet -Web` green (1516 .NET + 242 Vitest + typecheck clean, including the `e2e/*.ts` files,
  which share the same tsconfig). CSharpier clean. **E2E itself was not run** (opt-in, user-only per repo
  policy) — the Stage B `helpers.ts` fix has not been confirmed against a live browser run; recommend an `.\e2e.ps1
  -Spec acquisition` pass (or the full suite) before treating it as fully verified, though the fix is a
  straightforward mechanical wiring match to the existing `.acquire-modal`/DECLINE handling pattern already in
  that file.

**Quirks the tests assert, both stages:** blank/whitespace/cancelled nickname on every path ⇒ species-default
name unchanged (the Gen 1 decline case); an over-length nickname is truncated to 10 chars, never
rejected/errored; a nickname set on the *starter* has no effect on a later draft/boss-catch offer's default
(each new creature still defaults to its own species name — nicknames never leak across acquisitions); the
full-party swap path honours the nickname exactly like the open-slot path (same `Normalize` call site, both
branches); a nicknamed creature keeps its nickname across `Evolve` while an un-nicknamed one still adopts the
new species name (the `Name == SpeciesName` branch, both directions); `CreatureOverview` shows the species name
only when it differs from the nickname.

**Out of scope (both stages):** any persistence beyond the session (no `save.db`), profanity/validation
filtering (no in-game text-entry keyboard is being modeled, and this is single-player with no shared/visible
naming), showing the species name anywhere besides CHECK POKEMON (nameplate/party strip/log stay
nickname-only — a **modern QoL choice**, not a Gen-1-parity claim: Gen 1 has no screen where a nickname and
species name appear together at all; that pairing is a later-generation, Gen 3+/6+ Summary-screen convention),
and renaming an already-acquired party member after the fact (this feature is nickname-**on-acquisition** only,
per the ask).

---

## Comment Condensation Pass — deep cut + extract to docs ✅ DONE (2026-09-13, all 6 batches)

**Raised by the user (2026-09-13):** the codebase's comments were mostly deliberate design-rationale prose
(a survey found genuine restate-the-obvious filler only in `PokeApiConnector` and a few frontend leaf files),
but since every comment here was written by this session with no external-API audience, the user's call was to
**deep-cut anyway — but pull anything genuinely useful into `docs/*.md` first**, so the code shrinks to
near-minimal and the docs carry the record. Full method (the 3-outcome rule: delete / migrate-then-cut / keep
minimally, plus the extraction-target table) is in the session's plan file; the short version is: before
condensing any comment, check whether the fact is already on a seam member's own XML doc (e.g.
`IBattleRules.FaintEndsTurnImmediately` — this codebase's own established "one canonical explanation, pointers
everywhere else" convention) or in `STATE_MODEL.md`/`GENERATION_SEAMS.md`/`ENCOUNTER_DESIGN.md`/
`DATA_IMPORT.md`/`TODO_ARCHIVE.md`; migrate first if not, then cut to a short pointer. Each batch is verified
(build + CSharpier + full `.NET` suite) and independently audited by a second agent for lost seam/trap content
and doc duplication/staleness before moving on — staged deliberately, one batch reviewed and approved before
the next (not a single giant sweep).

**Progress (all 6 batches shipped and committed):**
- **Batch 1 — `PokeApiConnector/`** (~20 files) ✅ DONE (2026-09-13). Extracted into `DATA_IMPORT.md`: new
  §4.6 Learnsets, §4.7 Evolutions (previously undocumented), the full `Gen1MoveEffects` special-move-effects
  catalog folded into §4.1, a missing Psywave entry, the `-- assets` CLI stage. Also caught and fixed stale doc
  claims found along the way (a "the corrections list is exactly one: Acid" line that no longer matched the
  ~14-entry `ApplyGen1Corrections` switch; a miscounted pipeline-step list; a missing item-sprite step/table
  row). One real miss from the first pass, caught by the user and fixed: two "this DB column is already
  multi-gen-ready" signposts (`PokemonImport`/`MoveImport`) were deleted with no replacement — restored as
  short pointers to this section, and a matching one added to `EvolutionImport` for consistency.
- **Batch 2 — core engine** (`Battle.cs`, `AttackAction.cs`, `DamageCalculator.cs`, `RunDirector.cs`,
  `RunEvents/BattleRunEvent.cs` + `LeadChoiceEvent.cs`; the four smaller `RunEvents/` files were already lean,
  left as-is) ✅ DONE (2026-09-13), independently audited. Almost entirely pointer-condensation, not
  fresh extraction: `STATE_MODEL.md` §2 and `GENERATION_SEAMS.md` already fully covered the participant-XP
  split, mutual-KO/`PlayerWon` semantics, and the `RunRules`-is-not-a-seam distinction. Two genuinely new,
  previously-undocumented sections added to `STATE_MODEL.md` §2: Haze's narrow field-by-field reset (vs. a full
  `BattleState` wipe) and why Mimic/Transform identity reverts before any reset, not just at battle end.
- **Batch 3 — `creaturegame.Web/Battle/`** (`EncounterFactory.cs`, `EnemyArchetype.cs`,
  `GameSessionManager.cs`, `RewardCalculator.cs`, `Hubs/BattleHub.cs`, `SignalRBattleEventEmitter.cs`) ✅ DONE
  (2026-09-13), independently audited. Mostly pointer-condensation onto `ENCOUNTER_DESIGN.md`,
  `GENERATION_PROFILE.md`, and `GAME_LOOP.md`/`ARCHITECTURE.md` (both already covered the reconnect/session-
  lifecycle and RNG-threading material in full, so `GameSessionManager`/`SignalRBattleEventEmitter`/`BattleHub`
  condensed to pointers with almost no fresh extraction there). One genuinely new section added:
  `ENCOUNTER_DESIGN.md` §5.1 "Reward roll mechanics" — the `RewardCalculator.cs` rarity-band/gold-formula/
  category-bias/Quick-Heal design that had no doc home before. `BattleHub.cs` got a structural condensation, not
  just pointer-cutting: one canonical class-level doc replaced ~12 near-identical "mirrors X — fire-and-forget…"
  method docs. Caught and fixed one stale doc claim along the way (`EncounterFactory.CreateEnemyAsync`'s XML doc
  said `depth` was the run's `battlesWon`; Phase 3c-2 replaced that proxy with biome-position `RunState.RunDepth`
  months ago and the doc never caught up). The independent audit caught two real misses from the first pass,
  both fixed: §5.1's gold-bag formula was transcribed missing a `/10` divisor (a 10× error — `base × level ×
  skew × 0.2 × rarityFactor`, not `× 2`); and `BattleHub.cs`'s new class-doc promised "exact fallback noted per
  method where it isn't the obvious first option" but `BuyShopItem`/`RespondAcquisition`/`ChooseLead`/
  `ChooseSwitch` had lost exactly those non-first-option fallback notes — restored.
- **Batch 4 — frontend** (`BattleScreen.tsx`, `useBattleHub.ts`, `battleReducer.ts`, `timeline.ts`,
  `BattleScene.ts`, the 9 modal components) ✅ DONE (2026-09-13), independently audited. **Not a uniform
  deep-cut like Batches 1-3** — `battleReducer.ts` and all 9 modals were reviewed and left unchanged, judged
  already appropriately minimal (short, non-duplicated, code-adjacent). Large stretches of `timeline.ts` and
  `BattleScreen.tsx` were deliberately left mostly intact too: this is where the frontend's real sequencing/UI
  trap-knowledge concentrates (blocking-modal semantics per event, queued-vs-immediate ordering, true-species
  tracking) with no pre-existing doc home, unlike the backend batches where `ENCOUNTER_DESIGN.md`/
  `GENERATION_PROFILE.md`/`GAME_LOOP.md` already covered most of it — cutting it for its own sake would have
  been a real loss, not a cleanup. What *did* condense: `ARCHITECTURE.md` §2.8-duplicate rationale (the
  queued-events pattern, stated once there already); the repeated "backend blocks server-side, timeline idles
  here" note restated near-verbatim across ~8 `expandEvent` switch-cases, now stated once canonically with
  per-case deviations (Shop's iterative non-hide) called out explicitly; and the same repeated-boilerplate
  pattern in `useBattleHub.ts`'s ~10 modal-answer callbacks (`dispatch(HIDE_X)` + hub invoke), condensed the
  same way `BattleHub.cs` was in Batch 3. New doc content: `SPRITE_PRESENTATION.md` §1.3 gained a "True-species
  tracking" subsection (the three tracked ids — `playerSpeciesId`/`playerTrueSpeciesId`/`initialPlayerSpeciesId`
  — and why Transform updates only the first while evolution/switch update both) and §1.6 gained a
  "Volume-routing gotcha" note (Phaser's `SoundManager` bypasses the master-volume slider unless scaled
  explicitly) — both previously undocumented. Caught one real bug along the way (not a doc-staleness fix this
  time): `BattleScreen.tsx`'s `NodeLadder` had the same explanatory comment written twice back-to-back, a
  leftover from a prior edit — deleted the duplicate. The independent audit caught two real misses, both fixed:
  `BattleScene.ts`'s listener-teardown comment lost the specific consequence it was warning about (a bridge
  listener firing on a destroyed scene throws and *freezes the battle queue* — distinct from, and cut alongside,
  the HMR-leak rationale that correctly survived) — restored as its own line; and `timeline.ts`'s
  `MoveReplacementRequired` case still carried the old verbatim "backend blocks / timeline idles here"
  restatement the rest of the file's cases had already been trimmed of once the canonical note went in —
  trimmed to match.
- **Batch 5 — mop-up** (`Evolution/`, `Items/`, `DB/` services, `Controllers/`, and the frontend leaves
  `moveMenu.ts`/`playerIdentity.ts`/`presentation.ts`/`movePower.ts`/`regionMap.ts`/`bag.ts`/`bossTrainer.ts`/
  `townMapLayout.ts`) ✅ DONE (2026-09-13). Lightest-touch batch yet — most of this code turned out to already
  be either a seam's own canonical doc (`Evolution/`'s XML docs *are* the source `GENERATION_SEAMS.md`'s
  one-line table row points at, not a duplicate of it — left untouched) or small pure-helper modules whose
  density is earned (`townMapLayout.ts`'s tuning constants carry dated user-feedback rationale
  `GENERATION_PROFILE.md` §7.4 explicitly leaves to this file, not to itself). Three genuine issues found and
  fixed: (1) **two stale doc claims** — `Item.cs` and `ItemService.cs` both still said "the bag / use-in-battle
  layer is not built yet," long since untrue (`Bag`, `ItemAction`, the `IItemEffect` registry all shipped);
  updated to point at the real registry. (2) **restate-the-obvious filler** — every method on `AttackService.cs`
  had a doc comment that added nothing beyond the method name (`UpsertAttackAsync`: "Adds a new attack to the
  database or updates it if it already exists by ID."); deleted. (3) **dead code, flagged not removed** —
  while touching `AttackService.cs`, found `GetRandomAttackAsync`/`GiveDefaultMoveAsync`/`GiveRandomMoveAsync`
  have zero callers anywhere in the repo, including tests (pre-date `LearnsetMoveSelector`-based move
  assignment) — left a one-line flag in the file rather than deleting, since this is a comment-only pass; a
  follow-up tech-debt item to actually remove them belongs in a real cleanup pass, not buried in this one.
  `presentation.ts` had two passages duplicating `GENERATION_PROFILE.md` §7.2/§5(a) near-verbatim (the
  two-path generation-reveal explanation, the type-asset-inventory rationale) — condensed to pointers.
  `GameController.Start`'s seed/profile comments condensed onto `ARCHITECTURE.md` §2.10. Full .NET suite +
  Vitest + tsc green throughout.
- **Batch 6 — tests** ✅ DONE (2026-09-13). Confirmed light as predicted: sampled the highest comment-
  density files in both suites (`WebEventContractTests.cs`, `GenerationProfileTests.cs`, and the outlier
  `TestAltProfile.cs` at ~52% comment lines) plus comment-density ratios across the whole tree (~11% both
  suites, unremarkable). Verdict: test comments are almost entirely per-test/per-probe rationale ("why this
  specific assertion, why this specific fake value") that is inherently test-local, not duplicated design prose
  — the same shape as a good test name, just longer. One real exception found and fixed: `TestAltProfile.cs`'s
  class-level `<remarks>` (the "why a falsification harness has to exist" narrative) substantially restated
  `GENERATION_PROFILE.md` §3, which documents the same methodology more completely (incl. the full
  slice-by-slice table) — condensed to a pointer; the per-member remarks (why *this* fake value, specifically)
  stayed, since those aren't in the doc. No other file warranted a change. **This closed the multi-batch
  Comment Condensation Pass across all 6 batches.**

Each batch stopped for review before the next; "continue" was given per-batch, not as blanket approval for the
whole list.

**Follow-on (2026-09-13, same day):** the pass surfaced a recurring question — where does *why*-rationale
belong when it's player-visible design intent, not an internal comment? That was formalized separately as a new
`docs/PRODUCT_SPEC.md` (current-state feature spec) plus a `DEV_STANDARDS.md`/`DEFINITION_OF_DONE.md` rule
(§G, "Design Rationale Placement"), with matching updates to the `pr-review` and `docs-cleanup` subagent
definitions, `AI_CONTEXT.md`, `CLAUDE.md`, and `ARCHITECTURE.md`. That follow-on is process/governance work,
not a player-visible feature — no `PRODUCT_SPEC.md` entry of its own.

**Commits:** Batches 1-2 in `30a1b77`; Batches 3-6 in `b615640`; the PRODUCT_SPEC.md/DoD follow-on in `7105b54`.

---

## End-of-turn residual (poison/burn/Leech Seed) fired even after a same-turn faint ✅ DONE (2026-09-13)

**Raised 2026-09-12** from this battle log (a Tier 1 item, one of two joint code-analysis questions flagged that
day):
```
VENOMOTH used POISON POWDER!
RATICATE was poisoned!
RATICATE is hurt by Poison!
RATICATE used QUICK ATTACK!
VENOMOTH took 17 damage!
RATICATE is hurt by Poison!
VENOMOTH fainted!
```
Two distinct questions were logged, unanswered at the time:
1. **Does poison tick on the turn it's applied?** — confirmed **correct as-is, not a bug**. Gen 1's own rule is
   that a status applied this turn already ticks at this turn's end-of-turn phase; no code change needed for
   this half.
2. **Does end-of-turn residual still fire for the survivor on the turn the opponent faints from a direct hit?**
   *(Framing note, 2026-09-20: "end-of-turn phase" here is the engine's model, not Gen 1's — Gen 1 ticks each
   creature right after its own action; see the closing note at the bottom of this section.)*
   — confirmed a **real bug**. `Battle.cs`'s turn loop ran `StatusResolver.ApplyEndOfTurnDamage` for both
   creatures, plus `ApplyLeechSeedDrain` both directions, **unconditionally** right after the action-execution
   loop, regardless of whether either side had already fainted that same turn from a direct hit. Per Smogon's
   RBY Mechanics Guide: *"If a Pokémon faints, the turn ends there and then. Therefore, any end-of-turn
   effects... are skipped."* Once either side faints during the turn's actions, the **entire** end-of-turn
   phase is skipped, not just the fainted creature's own residual — so in the reported log, Raticate's second
   poison tick should never have fired once Quick Attack had already dropped Venomoth to 0 HP that same turn.

**Fix.** Wrapped the end-of-turn residual block in `Battle.cs`'s turn loop (`ApplyEndOfTurnDamage` for both
sides + `ApplyLeechSeedDrain` both directions) in `if (PlayerCreature.IsAlive() && EnemyCreature.IsAlive())`.

**Test fallout.** `tests/creaturegame.Tests/Unit/PartyExpShareTests.cs`'s
`MutualKo_FaintedFinisherEarnsNothingAndIsExcludedFromTheDivisor` had built its "finisher faints on the winning
turn" scenario using end-of-turn Burn — no longer reachable after the fix (correctly, since Gen 1 doesn't allow
it). Switched the scenario to Recoil (a same-action effect, like Take Down/Double-Edge, that resolves inside the
move's own execution, before the end-of-turn phase), which still exercises the same code path.

**New regression test.** `Battle_EndOfTurnResidualSkipped_WhenOwnAttackFaintsTheOpponentThatTurn`
(`tests/creaturegame.Tests/Integration/BattleIntegrationTests.cs`). Verified by sabotage: temporarily reverted
the `IsAlive()` guard, confirmed the test failed, restored the fix, confirmed it passed. Full .NET suite green.

**The fix grew after `requirements-review` and `pr-review` (2026-09-13) — three follow-on changes, same root
rule:**

1. **A second real bug, found while reviewing the first fix: Hyper Beam's recharge flag ignored a KO.**
   `AttackAction.cs` set `Source.Battle.IsRecharging = true` whenever the move dealt any damage — including a
   hit that KO'd the target — contradicting the rule `docs/GEN_DIFFERENCES.md` already documented ("Hyper Beam
   does NOT require a recharge turn if it KOs the target") and the exact same "a faint ends things there and
   then" principle as the residual fix above. Reachable specifically through the forced-switch path:
   `EnemyCreature` is never reset mid-battle (only the player's active creature is, on a switch-in), so a stale
   `IsRecharging` flag set by a KO hit would carry over and wrongly skip the enemy's very next turn — against
   the newcomer, who took no part in the hit that supposedly earned the recharge. Fixed by additionally gating
   the flag-set on the target still being alive. **New regression test:**
   `BattleForcedSwitchTests.EnemyRechargeMoveThatFaintsTheLead_DoesNotAlsoSkipItsNextTurnAgainstTheSwitchIn`.
2. **Architecture fix (`pr-review` blocker): the rule is now a named seam member, not a bare conditional.**
   Both the residual guard in `Battle.cs` and the Hyper Beam recharge check in `AttackAction.cs` independently
   encoded "a faint ends the turn" as an inline check — duplicated generation-variable logic with no single
   source of truth. Extracted to `IBattleRules.FaintEndsTurnImmediately` (`Gen1BattleRules.Instance`: `true`;
   Gen 2 removed this rule, per `docs/GEN_DIFFERENCES.md`); both call sites now read the same member, and
   `DelegatingBattleRules` (test double) delegates it.
3. **Architecture fix (`pr-review` blocker): the guard had over-reached into the Disable-lock and
   binding-trap countdowns.** Those counters are *not* part of the faint-ends-turn rule — Gen 1 ticks them down
   every turn regardless of a mid-turn faint, unlike the residual-damage half, which the rule does skip. Split
   `StatusResolver.ApplyEndOfTurnDamage` into two methods: the new `StatusResolver.TickTurnCounters` (Disable +
   binding countdown only), called unconditionally every turn for both creatures, and the original
   `ApplyEndOfTurnDamage` (status damage only), which stays behind the `FaintEndsTurnImmediately` guard.
4. **Two more regression tests (`pr-review`'s recommended coverage).**
   `BattleForcedSwitchTests.ActiveFaints_SameTurnItPoisonsTheEnemy_SkipsTheEnemysOwnResidualThatTurn` covers the
   battle-**CONTINUES** branch (a forced switch-in) — the surviving enemy's own just-inflicted poison must also
   be skipped that turn, and the same assertion pins the Leech Seed sibling of the same guarded block. This
   closes the gap `pr-review` flagged where only the battle-**ENDS** branch (this entry's original regression
   test, above) had coverage.

**Docs.** `docs/GEN_DIFFERENCES.md` → "Status Quirks" now states the general faint-ends-turn rule, cross-
references the Hyper Beam corollary, and notes the Disable/binding-counter exception. `Battle.cs`'s `PlayerWon`
XML doc comment was also corrected (an advisory `pr-review` finding, not a code bug) — it used to describe a
direct-hit KO as finishable by the winner's own end-of-turn residual that same turn, which is no longer true
after this fix.

**One question deliberately left open, not fixed here** — "does Gen 1's faint-ends-the-turn rule also cover a faint
caused BY the residual phase itself, not just a direct hit?" (raised 2026-09-13 by `requirements-review`; the
second `ApplyEndOfTurnDamage` call still ran after the first faint). **ANSWERED 2026-09-20 — and the answer is
larger than the question.** Reading pret/pokered `engine/battle/core.asm` (`MainInBattleLoop`, lines 413-468)
shows Gen 1 has **no end-of-turn residual phase at all**: `HandlePoisonBurnLeechSeed` keys off `hWhoseTurn`, so
each creature takes its **own** poison/burn/Leech Seed tick **immediately after its own action** (A acts → A's own
tick → if A fainted, jump to A's faint handler and B never acts; else B acts → B's own tick → B's faint handler).
So a residual-caused double-faint is impossible in Gen 1, the faster creature's tick lands *before* the slower one
acts, and the slower creature doesn't act if the faster one dies to its own tick. The engine's two-actions-then-
both-ticks turn shape differs on all three counts; the 2026-09-13 fix above only covers a faint from a direct hit
(and is consistent with the real order — a direct-hit KO precedes the not-yet-run ticks). Now tracked as an open,
unplanned item in `TODO.md` → *Known Gaps* ("Gen 1 has no end-of-turn residual phase"); the rationale is in
`GEN_DIFFERENCES.md` → Status Quirks.

**Verified.** Full .NET suite green, including the three new regression tests added in the follow-on work.

---

## Town Map scatter tiles and town markers carried an opaque white background instead of the ground's grain/fill ✅ DONE (2026-09-12)

**Raised 2026-09-12 by the user, in two passes same day.** On the Town Map grid, the `.town-map-scatter` tiles
(trees, mushroom-like rock cluster, boulder, signpost — the vendored Kenney "Monochrome RPG" sprites,
`--ks-tm-tree-a/b/c`/`--ks-tm-rock`/`--ks-tm-boulder`/`--ks-tm-signpost` in `index.css`) sat on plain white,
while the surrounding `.town-map-cell` ground used the darker, spotted `--ks-fill` + `--ks-grain` texture — so
every scatter tile read as a white square with an icon on it, not blended into the island. The first pass fixed
the six scatter sprites and flagged the town-marker sprites (`--ks-tm-town-open/shut` — the town/door icons the
player clicks to enter a node) as an unchecked "presumably need checking too" follow-up; the user asked for that
follow-up same day, and it turned out to have the identical root cause and fix.

**Root cause, verified not assumed, and pinned down further than the initial report.** Decoding the
`--ks-tm-tree-a` base64 PNG showed it fully opaque (alpha 255 everywhere) with `(255,255,255)` white corner
pixels. Re-downloading the real Kenney "Monochrome RPG" source zip fresh from kenney.nl (per the
`feedback_verify_sprites_before_use` memory — not from memory/assumption) and diffing byte-for-byte confirmed
the six embedded scatter sprites (`--ks-tm-tree-a/b/c`, `--ks-tm-rock`, `--ks-tm-boulder`, `--ks-tm-signpost`)
are exact recolors of the documented tile picks (#14/#15/#16/#30/#105/#67 — see `GENERATION_PROFILE.md`
decision 12) — every non-white pixel matches exactly, so this was **not** a mis-picked tile. The actual cause:
`--ks-fill` used to be literal `#FFFFFF` and the sprites were recolor-exported against that (byte-exact at the
time). `--ks-fill` was later darkened to `#E8E4D6` (commit "Darken Kanto Sage box fill to fix HP-bar clash") but
the sprite PNGs were never re-baked, so they still carried the stale literal-white opaque pixels. The coastline
(`--ks-tm-coast-*`) sprites were already confirmed fine in earlier work (proper alpha transparency). The
follow-up pass re-ran the same byte-for-byte check against the town-marker sprites and confirmed
`--ks-tm-town-open` and `--ks-tm-town-shut` are exact recolors of tiles #110 (open door) and #109 (shuttered) —
matching `GENERATION_PROFILE.md`'s documented pick exactly — so this was the same issue, not a new root cause.

**Fix.** For each of the eight sprites (six scatter + two town markers), every pixel that was exactly
`(255,255,255,255)` was set to alpha 0 (transparent); every other pixel (the already-correctly-recolored ink/dim/
fog linework) was left byte-for-byte unchanged — letting the sprite's `background-image` show the cell's own
`--ks-fill`/`--ks-grain` through it, matching the coastline-trim approach (`background-size: contain` already in
place). Only file touched across both passes: `creaturegame.Web/ClientApp/src/index.css` (eight base64 data-URI
values swapped total — six then two — nothing else).

**Verified.** Both passes re-decoded the edited `index.css` and diffed every pixel of the fixed sprites against
the pre-fix versions — non-white pixels unchanged, white pixels now transparent, for all 16×16 pixels × 8
sprites total. The six-sprite pass was also verified live in-browser (Puppeteer, `.\dev.ps1` stack): started a
run, reached the route-choice Town Map, and confirmed every scatter prop (trees, rock clusters, boulder,
signposts) now blends into the grainy land texture with no white squares. The two-sprite town-marker follow-up
was **not** confirmed live in-browser — the Puppeteer MCP server had disconnected mid-session — so it was
verified instead via a composited before/after render (sprite drawn over the `--ks-fill` background color),
which showed the same white-square-to-blended-background pattern as the first pass; this is a real
methodological difference from the first half's live confirmation, not an equivalent check, and is recorded
here rather than glossed over. Full fast suite green after both passes (.NET 1494/1494, TypeScript clean,
Vitest 232/232).

---

## Route-choice bottom legend duplicates the map hover, with no click affordance ✅ DONE (2026-09-12)

**Raised and fixed same day (2026-09-12).** In `RouteChoiceMap` (`BattleScreen.tsx`), the map's per-town
hover/focus already drove a caption band (`.town-map-caption` inside `TownMapGrid`) showing the hovered biome's
name + status + type chips. Below the map, a separate static `.route-choice-legend` row listed the same info
(name + type chips) for all three offered biomes, but it was inert — not clickable, not focusable, just a
redundant echo.

**Fix.** Deleted the `.route-choice-legend` div block from `RouteChoiceMap` in
`creaturegame.Web/ClientApp/src/pages/BattleScreen.tsx` — the map hover caption is now the sole source of choice
info. Removed the now-dead CSS from `BattleScreen.css`: the `.route-choice-legend` /
`.route-choice-legend-item` / `.route-choice-legend-name` rule blocks, and trimmed the Kanto Sage
`[data-generation="gen1"]` ink-color override selector list down to just `.route-choice-modal .biome-title,
.route-choice-modal .biome-sub` (dropping the two legend selectors). Pure UI deletion — no wire/state/engine
change, no generation seam.

**Verified.** `tsc --noEmit` clean; full fast suite green (.NET 1494/1494, TypeScript clean, Vitest 232/232).

---

## Party strip shows a stale name after an on-field evolution ✅ DONE (2026-07-29)

**The defect (found 2026-07-28).** The party panel is fed **only** by `PartyUpdated` snapshots (plus the
connect-time `/party` hydrate). `TryEvolveAsync` in `BattleRunEvent.cs` renamed the creature and emitted
`CreatureEvolved`, but pushed no `PartyUpdated` afterward — so the roster row kept the pre-evolution species name
until some later event happened to resync it (a reward/acquisition/lead-change snapshot). The nameplate/HUD were
already correct (see *Evolution nameplate doesn't follow until the next battle starts* below, 2026-07-28) —
driven directly by `CreatureEvolved` client-side — so the inconsistency was strip-vs-nameplate, visible side by
side: same family as *Party strip shows a stale level for the on-field creature* (2026-07-27) but a different
trigger (evolution, not level-up) and a different field (name, not level).

**Fix.** `BattleRunEvent.cs`: `TryEvolveAsync` now returns `Task<bool>` (true only on an actual morph; false on
no-resolver / no-evolution / player cancel). The evolution loop accumulates `anyEvolved` across the party and
pushes **one** `PartyUpdated` snapshot after the loop, coalescing a multi-creature evolution batch into a single
repaint rather than one push per creature.

**Key detail worth recording.** The win's own level-up snapshot (emitted inside `Battle`) could **not** cover
this, because it fires **before** the evolution runs and therefore carries the pre-evolution name. Verified by
temporarily disabling the new emit: the last snapshot read "CHARMANDER" while the nameplate read "CHARMELEON" —
the exact reported defect.

**Test.** `RunDirectorEvolutionTests.Runner_OnFieldEvolution_PushesAPartySnapshotCarryingTheNewName` — asserts the
last `PartyUpdated` carries the new name/speciesId and is ordered after `CreatureEvolved`. Confirmed to fail
without the fix.

---

## Evolution nameplate doesn't follow until the next battle starts ✅ FIXED (2026-07-28)

**The defect (found 2026-07-26, while writing `evolution.spec.ts`).** `useBattleHub`'s side-split ref and the
reducer's `state.playerName` — the value `BattleScreen` renders in the nameplate and the `"What will X do?"`
action prompt — were retargeted on `BattleStarted`, `CreatureSwitchedIn`, and `LeadChanged`, but not on
`CreatureEvolved`. Between encounters the log read `CHARMANDER evolved into CHARMELEON!` while the nameplate
and prompt still said "What will CHARMANDER do?" under the new CHARMELEON sprite. Self-corrected at the next
`BattleStarted`, so cosmetic and transient, but a real, visible inconsistency — and the last unfixed leg of the
recurring **web event field-projection gap**: `BattleStarted`/`CreatureSwitchedIn` were already handled,
`LeadChanged` was fixed the same day in commit `b53f0ff`, and `CreatureEvolved` was the straggler.

**Fix landed in two passes (both needed — the first alone did not close the visible symptom):**
1. `battle/playerIdentity.ts` (new) — extracted the four-rule "which creature is the player" decision
   (`BattleStarted`/`CreatureSwitchedIn`/`LeadChanged`/`CreatureEvolved`) out of `useBattleHub.ts` into a pure
   `nextPlayerName` helper, and added the `CreatureEvolved` case for the **event side-split ref**
   (`playerNameRef`, used by `expandEvent` to attribute later moves/damage to the player vs. the enemy side).
   Guarded on the OLD name matching the current player — evolution is party-wide (`BattleRunEvent`'s
   `EvolutionOrder` offers it to every member that levelled) — so an unguarded retarget would have handed player
   identity, and therefore future move/damage attribution, to a bench creature. Covered by `playerIdentity.test.ts`
   (6 cases, including the bench-evolution guard). This closed a real, separate hazard, but left the visible HUD
   text (a different piece of state) still stale.
2. `timeline.ts` / `battleReducer.ts` — the visible symptom needed a second, separate fix: a new
   `CREATURE_RENAMED` action (carries both `fromName` and `toName`), dispatched in the `CreatureEvolved` timeline
   case **after** `anim()` so the nameplate flips together with the sprite morph rather than before it; the
   reducer's `CREATURE_RENAMED` case renames `state.playerName` only when `action.fromName === state.playerName`
   (same bench-evolution guard, keyed on the *old* name since that's what the HUD still holds at that point).
   Mirrors the existing `LEAD_CHANGED`/`SWITCHED_IN` reducer pattern — which is why `LeadChanged`/
   `CreatureSwitchedIn` never had this gap in the first place. Covered by two new `battleReducer.test.ts` cases
   (on-field rename / bench-evolution no-op) and an extended `timeline.test.ts` `CreatureEvolved` case asserting
   the dispatch carries both names and lands after the morph index.

**Verification:** full suite green — .NET 1430/1430, TypeScript clean, Vitest 187/187.

**Known still-open, deliberately not touched by this fix (see `TODO.md`):**
- `evolution.spec.ts` still reads the nameplate only after the run is playable again (i.e. after the *next*
  `BattleStarted`), so it would not catch a regression of this exact fix. Not extended here — E2E is user-only
  per the repo's agent rules, and an unverified assertion isn't worth adding blind.

**Closed the same family, one day later:** the **party strip** had the same staleness on a different,
engine-side leg — `TryEvolveAsync` in `BattleRunEvent.cs` emitted no `PartyUpdated` after an evolution, so the
roster row kept the pre-evolution name until some later snapshot happened to resync it. Same family as commit
`2880158` ("Party strip follows the on-field creature's level-up") but a separate leg that no client-only fix
could close. Fixed 2026-07-29 — see *Party strip shows a stale name after an on-field evolution* above.

---

## Mutual KO ends the run even with a live bench ✅ DONE (2026-07-28)

**The defect (found 2026-07-27 by `pr-review`).** When the active creature and the enemy fainted on the same
turn — Self-Destruct/Explosion, Struggle recoil, or end-of-turn Burn/Poison/Leech — the battle was a **win** (the
enemy-faint check runs first in `Battle.cs`), but `BattleRunEvent` read the post-battle `s.Player`, found it dead,
and **ended the run as a loss**. That contradicted the rule Encounter Logic Phase 4 Stage 3 established: *the run
ends only when the **whole party** is down*. A player with five healthy creatures on the bench lost the run
because their lead traded itself for the kill.

**Resolution (2026-07-28) — the fork settled.** The `/plan` fork was: (a) a mutual KO is the player's **win**,
banking the reward/XP and continuing with a surviving lead; (b) a draw/loss for the encounter but not the run
(no reward, run continues); (c) keep today's behaviour and make it intentional. **The user picked (a).**

**What shipped:**
- `creaturegame/Combat/Battle.cs` — a new `public bool PlayerWon` property, set in the enemy-faint branch, so a
  win is recorded independently of whether the finisher survived it. The end-of-battle `BattleEnded` winner name
  is now keyed on `PlayerWon` instead of `PlayerCreature.IsAlive()` (the old expression named the also-fainted
  enemy as the winner, telling the client the player LOST). The enemy-faint branch now also emits
  `CreatureFainted` for the player when it went down too — otherwise the client never played the player-side
  faint animation and its creature sat at an empty HP bar through the victory.
- `creaturegame/Combat/RunEvents/BattleRunEvent.cs` — the `if (!active.IsAlive()) return new BattleOutcome(false);`
  guard became `if (!active.IsAlive() && !(battle.PlayerWon && await PromoteSurvivorAsync(s, ctx, active)))`. The
  new `PromoteSurvivorAsync` **prompts** the player for the next lead, reusing the forced-switch prompt
  (`SwitchInOffered` + `IBattleInput.ChooseSwitchInAsync`) — "your active creature fainted, someone must take
  over" is exactly that prompt's situation, and its modal already disables fainted members. The result is a
  **lead reassignment, not a send-in**: it emits `LeadChanged` + `PartyUpdated` (no `CreatureSwitchedIn` — nobody
  takes the field). A stale/out-of-range/fainted pick is corrected to the first standing member. Runs *before*
  the reward/draft rolls, so those read a live lead. Returns `false` when the whole party is down, which still
  ends the run as a loss. Also: the post-battle `active.CarriedStatus = CaptureCarriedStatus(active)` is now
  skipped when the finisher fainted (previously inert only because every revive path happens to clear
  `CarriedStatus` — a "right by coincidence" dependency the mutual-KO path would otherwise have exposed).
- `creaturegame/Creatures/Party.cs` — `FirstLiveIndex()` and `CorrectSwitchInPick(int)`. Both prompts that pick a
  creature by index now share them (`Battle`'s mid-battle forced faint-switch and the run loop's promotion), so
  "never put a corpse on the field, and never strand the run on a bad pick" has one home instead of two verbatim
  copies that could drift. `Battle.FirstLiveMemberIndex` is now a one-liner over it (it only adds the null-party
  case, i.e. the legacy single-creature battle).
- **Client (`useBattleHub.ts` / `battleReducer.ts` / `timeline.ts`)** — a lead swap is the one way "who the player
  is" changes *without* anyone taking the field, so no `CreatureSwitchedIn` announces it, and `LeadChanged` only
  swapped the sprite and logged a line. Left as-is the promoted survivor rendered under the corpse's name at an
  empty HP bar for the whole post-battle stretch (reward modal, draft modal, and — for a Boss-node mutual KO — the
  Poké Center → biome-choice run), name-keyed `UPDATE_HP`/`CLEAR_STATUS` for the new lead were **dropped** (so a
  heal would log while the bar stayed at zero), and `Lv` never self-corrected, since no later event carries a
  level. Fixed by a new `LEAD_CHANGED` action that retargets the player HUD (filling level/HP/status from the
  roster it holds), a `PARTY_SET` re-sync from the lead row **guarded on the name already matching** so it can
  refresh but never *retarget*, and `playerNameRef` updating on `LeadChanged` so the side split follows. No new
  wire field: `PartyUpdated` already carries all five. **This also closes the same latent gap on the between-biome
  `LeadChoiceEvent` path**, where it was masked (a Poké Center heal precedes it and a `BattleStarted` follows).
  Found by `pr-review`, which correctly rejected the claim that no client change was needed.
- Tests: `RunDirectorForcedSwitchTests.MutualKo_WithALiveBenchMember_CountsTheWin_AndPromptsForTheNextLeadInsteadOfEndingTheRun`
  (which also pins the full win sequence and, via a capturing `RewardSupplier`, that the reward roll sees the
  **promoted survivor** — the promotion-before-reward ordering is load-bearing, because a 0-HP `PlayerCondition`
  maximises both the heal chance and its size in `RewardCalculator.TryRollHeal`),
  `…MutualKo_WithNoStandingBenchMember_EndsTheRun_AndNeverRaisesThePrompt` (the whole-party-down case: no prompt is
  raised, since a modal with nothing to pick would park an unanswerable blocking await),
  `…MutualKo_APickNamingTheFaintedFinisher_IsCorrectedToTheFirstStandingMember`; three `battleReducer.test.ts`
  cases for the HUD retarget / re-sync / never-retarget rules and a `timeline.test.ts` assertion for the new
  dispatch;
  `BattleForcedSwitchTests.DoubleFaint_WithALiveBenchMember_OffersNoSwitch_ButStillCountsAsTheWin` (renamed from
  `…AndKeepsTheLossSemantics`, winner pin flipped "Foe"→"Lead", plus an ordered `["Foe","Lead"]` `CreatureFainted`
  assertion); and `RunDirectorTests.Runner_DoubleFaintFromEndOfTurnPoison_EndsTheRun_ButStillCountsTheWin`
  (renamed from `…CountsAsLoss_NotAWin`) — a **lone** creature still ends the run, since there is nobody to
  promote, but its `BattlesWon` pin moved 0 → 1 with the win-tally decision below.

**Settled alongside it (user, 2026-07-28) — record as decided, not open:**
- A fainted finisher earns **zero** XP for the kill (excluded from the participation divisor) — confirmed as
  matching the Gen 1 quirk. Pre-existing behaviour from commit `d4acd05` (Participation XP), unchanged here.
- A mutual KO fires the **full** win sequence — the gold/item reward roll **and** the themed-draft/boss-catch
  acquisition offer — not XP alone.
- The next lead is a **player pick**, not a silent auto-promotion. The first implementation auto-promoted the first
  standing member in roster order (justified by Gen 1's overworld default-to-slot-order); `requirements-review`
  challenged it as a third, novel promotion mechanism that borrowed the vocabulary of an existing player-choice
  feature while removing the choice, and the user chose the prompt.
- A mutual KO that takes the **last** creature still **counts in the win tally** (`if (battle.PlayerWon)
  s.BattlesWon++`, above the run-over guard — deliberately not the old unconditional `++`, which would have
  credited ordinary losses). The run ends, but the win that ended it is not unmade. Raised during the gates and
  settled with the user; `pr-review` agreed it was worth changing.

**Superseded framing.** This closes the "masked in the shipped web run" caveat left in `TODO_ARCHIVE.md` →
*Participation XP*: that entry's `IsAlive()` guards were live for direct `Battle` callers and the endless chain
from 2026-07-27, but a mutual KO still discarded the correctly-computed XP by ending the run — now it doesn't,
whenever a bench member survives. Docs updated: `GAME_LOOP.md` (the forced-switch-on-faint row), `STATE_MODEL.md`
(the party-wide end-of-battle effects section).

---

## Party strip shows a stale level for the on-field creature ✅ DONE (2026-07-27)

**The defect (found by `pr-review` 2026-07-27, same day as Participation XP).** The party panel is fed **only**
by `PartyUpdated` snapshots (plus the connect-time `/party` hydrate), and `Battle` emitted that snapshot after a
win only when an **off-field** creature levelled. So when the *active* creature levels up and nobody else does,
its row in the party strip kept the old level until some later party-carrying event happened to refresh it. The
nameplate/HUD were correct — they're driven by `LeveledUp` directly — so the inconsistency was strip-vs-nameplate,
visible side by side. Pre-existing (it predates the participation split, which only hoisted the emit), cosmetic,
and self-corrected at the next snapshot.

**Fix.** At the post-win award site in `Battle.cs`, the `offFieldLevelled` flag was renamed `anyLevelled` and now
also folds in the **active** creature's own `RunLevelUpLoopAsync` result (previously only
`PayOtherParticipantsAsync` + `ShareExperienceWithBenchAsync` fed it). So a win where only the on-field creature
levels now also pushes a `PartyUpdated` snapshot, and the party strip no longer disagrees with the nameplate. The
comment at the emit site explains why the on-field creature needs the snapshot too (its nameplate/HUD follow
`LeveledUp` directly, its strip row does not).

Pinned by `PartyExpShareTests.ActiveCreatureLevellingAlone_StillPushesAPartySnapshot` (lead at level 5 that levels
off the win, high-level bench with `BenchXpShare = 0` so nothing off-field levels; asserts the only `LeveledUp`
events are the active's, and that the last `PartyUpdated` snapshot carries the lead's new level).

---

## Participation XP — a creature that fought earns a full share ✅ DONE (2026-07-27)

**Shipped as the Gen 1 participation split.** `Battle` now tracks a per-battle participant set (`_participants`,
written by the opening lead in `StartFightAsync` and by `BringInMember` — the shared tail of *both* the forced
faint-switch and the voluntary SWITCH, so one write covers both). On a win the award is divided evenly among the
**live** participants; a fainted participant earns nothing and is excluded from the divisor. New helpers
`LiveParticipants()` / `PayOtherParticipantsAsync()` sit beside the reworked `ShareExperienceWithBenchAsync()`,
which now skips participants and pays only never-deployed living members.

**The division itself is on the generation seam** — `IBattleRules.SplitXpAmongParticipants(award, liveCount)`,
implemented by `Gen1BattleRules` as `floor(award / liveCount)`. `Battle` only decides *who* participated (which is
gen-invariant); *whether and how* the award divides is generation-variable, so it belongs behind the seam. Added
after `requirements-review` raised the placement (user's call, 2026-07-27), rather than left as inline arithmetic.
Pinned by `ExperienceAndLevelingTests.Gen1XpFormula_DividesTheAwardAmongLiveParticipants`.

**The four forks, as settled in `/plan` (2026-07-27):**
1. **Even split among live participants** (Gen 1-faithful) — *not* a full award each. `fullAward / N`.
2. **The run XP curve is keyed once on the finisher's level**; the scaled award is then split, so every
   participant earns the identical number.
3. **The bench share formula is untouched** — still `floor(fullAward × BenchXpShare)`, off the full award.
4. **Surfacing:** `ExperienceGained` gained an `OnBench` flag. A switched-out participant's award is logged but
   does **not** move the on-field XP bar (`timeline.ts` gates `XP_GAIN` on it — the manual TS leg of the
   web-event field-projection gap). A never-deployed member's share was originally left silent until it leveled;
   fixed 2026-08-23 to also emit an attributed `OnBench: true` award — see `Innate Party XP Share`, below.

> ⚠️ **Known limitation, deliberately shipped as-is (user-decided 2026-07-27).** Decisions 1 + 3 take their
> figures from different bases, so a creature that **never took the field** can out-earn one that fought:
>
> | Difficulty | Participant (of a 100 award, 2 live participants) | Bench (never fought) |
> |:--|--:|--:|
> | Easy (0.75) | 50 | **75** ← bench earns more |
> | Normal (0.5) | 50 | **50** ← identical |
> | Hard (0.25) | 50 | 25 |
>
> Re-basing the bench share off the split share was proposed and **declined** — the bench formula stays as it is.
> Pinned by `PartyExpShareTests.BenchShareIsTakenOffTheFullAward_SoANonParticipantCanOutEarnAParticipant` so the
> inversion is a decision on the record, not a regression someone "fixes" by accident. **Do not re-file this as a
> bug.** If it is ever revisited, the fix is one line at the share site.

**Edge closed at the `pr-review` gate — the mutual KO.** The enemy-faint check runs *before* the player-faint
branch, so a finisher that dies on the same turn it wins (Self-Destruct/Explosion, Struggle recoil, or end-of-turn
Burn/Poison/Leech — all resolved earlier in the turn) reaches the award site **already fainted**. The first cut
re-admitted it via `LiveParticipants()`'s fallback insert and paid it at the award site unconditionally, so a
fainted creature was counted in the divisor and paid a share — contradicting this feature's own rule — and its
`ExperienceGained` went out with `OnBench: false`, filling the XP bar of a creature that had just fainted. Both
sites are now gated on `IsAlive()`: the fainted finisher earns nothing, the award goes undivided to whoever fought
and survived, and `liveParticipants` may legitimately be `0` (documented on the seam). **At the time this shipped
(2026-07-27), this was masked in the web run** — `BattleRunEvent` ended the run as a loss whenever the finisher
was dead, discarding the correctly-computed XP — live only for direct `Battle` callers and the endless chain.
**Un-masked 2026-07-28**: a mutual KO now counts as the player's win and promotes a surviving bench member instead
of ending the run, so this divisor logic is live in the shipped web run too — see `TODO_ARCHIVE.md` → *Mutual KO
ends the run even with a live bench*. Pinned by
`PartyExpShareTests.MutualKo_FaintedFinisherEarnsNothingAndIsExcludedFromTheDivisor`.

**Why participation is tracked on `Battle`, not `Creature.BattleState`** (the sketch below proposed the latter):
`ResetBattleState()` only ever reaches the *active* creature and the enemy (`StartFightAsync`) or an incoming
member (`BringInMember`) — a creature sitting on the bench is **never** reset, so a per-creature flag set in
battle 1 would still read true in battle 2 and silently pay a participant's share to a creature that never took
the field. A `Battle`-owned set is battle-scoped by construction (`BattleRunEvent` builds a fresh `Battle` per
encounter). Guarded by `PartyExpShareTests.ParticipationDoesNotLeakIntoTheNextBattle`.

**A wrong Gen-history comment was corrected on the way through.** `IBattleRules.CalculateXpAwarded`'s XML doc read
*"Gen 5+: additionally divides the gain by the number of participants"* — backwards. The participant divisor `s`
("the number of Pokémon that participated in the battle and have not fainted", Bulbapedia → *Experience*) is
present **from Gen 1**, holds through Gen 5, and was **removed in Gen 6**. That stale comment caused
`requirements-review` to report the whole feature as built on an inverted premise — i.e. it argued the even split
was a Gen 5+ mechanic and that "full award each" was the Gen-1-faithful reading. Verified against Bulbapedia and
corrected in `IBattleRules`, `GENERATION_SEAMS.md` (the gen-differences table) and here. **Don't restore the old
wording** — it will re-trigger the same false finding.

Docs updated: `STATE_MODEL.md` (the party-wide end-of-battle invariants — now a citable fact, not a plan claim),
`GENERATION_SEAMS.md` (the new seam row + the innate-share paragraph), and the `RunRules.BenchXpShare` doc comment.

<details><summary>Original write-up (raised 2026-07-26)</summary>

**The requirement, in the user's words:** *"a pokemon that was actively involved in a battle should receive equal
xp to any other active pokemon."*

This is the same principle as *Switched-in creature is the active creature* (above in this file), applied to the creature
that switched *out*: taking the field is what makes you a participant, and participants are not ranked by who
happened to be standing there when the enemy fainted.

**Today's behaviour (the defect).** `Battle.ShareExperienceWithBenchAsync(activeAward)` pays whoever is
`PlayerCreature` at battle end the **full** award (at the award site), and every *other* living member — including
one that fought most of the battle and was switched out — the flat `floor(activeAward × RunRules.BenchXpShare)`.
Participation is never recorded, so the engine currently cannot tell a creature that fought from one that sat on
the bench all battle. At the shipped web difficulties (`BenchXpShare` 0.75 / 0.5 / 0.25) a switched-out
participant loses 25–75% of its award purely for having been switched.

**Target behaviour.** Every creature that took the field during the battle earns a full participant share; a
creature that never entered keeps the innate bench share. Fainted members still earn nothing (Gen 1).

**⚠️ Open design question — needs `/plan` before implementation.** "Equal to any other active creature" has two
readings, and they move the numbers in opposite directions:
- **(a) Each participant gets the full award** (roguelite-generous, matches the wording most directly). Nobody is
  worse off than today; a 2-participant battle pays out more in total than a 1-participant one.
- **(b) The award is split evenly among participants** (Gen 1-faithful — the cartridge divides XP between every
  Pokémon that was sent out). Equal, but it *reduces* what the finisher earns today, so it's a nerf to the
  current single-creature run and interacts with the level-aware XP curve + trainer bonus.

Given the repo's "Gen 1 accuracy before extending" principle vs. the fact that the innate party share is already
a deliberate roguelite divergence (wider and more generous than the literal participant split), this is a genuine
fork the user should settle in `/plan`. **Do not pick one while implementing.**

**Implementation sketch (once the fork is settled).**
- **Participation flag.** A transient per-battle `bool` on `Creature.BattleState` (see `STATE_MODEL.md` — it is
  battle-scoped state, cleared with the rest), set wherever a creature takes the field: the battle-start lead and
  `Battle.BringInMember` (the shared tail of *both* switch paths, so forced and voluntary are covered by one
  write). Must survive being switched out — it records "fought", not "is out".
- **Award site.** `ShareExperienceWithBenchAsync` splits its loop three ways instead of two: participants (full
  or split share per the fork), living non-participants (`BenchXpShare`), fainted (nothing). The active creature
  is still paid at the award site — keep the "paid once" invariant explicit, it is the easy double-pay bug here.
- **Stat-Exp.** Already granted in full to every living member and deliberately not fractionalised — leave it be;
  this change is about the XP award only.
- **Seam check.** Lives in `RunRules`, not `IBattleRules` — participation-vs-bench payout is roguelite tuning, not
  a generation-variable rule. Run the `GENERATION_SEAMS.md` §5.0 checklist as part of the work.
- **Tests.** A switched-out participant earns the same as the finisher; a never-deployed bench member still earns
  only the bench share; a fainted participant earns nothing; the finisher isn't paid twice. `RunRules` with
  `BenchXpShare = 0` still pays participants (the flag, not the share, gates it).

</details>

---

## Other between-encounter modal E2Es ✅ DONE (2026-07-26)

Closed the last E2E gap in *Browser-Based UI Testing* (`TODO.md`): the between-encounter blocking modals other
than the reward-choice (already covered by `reward-drop.spec.ts`) had no Playwright coverage. Four new specs,
each asserting **both** answers and that the run flows on either way (every one of these parks a server-side
await, so a stuck prompt strands the run):

- `poke-center.spec.ts` (Heal / Skip) — the most expensive reach in the suite (a whole biome, 4–6 nodes ending
  in the Boss, has to be won). Lead is **MEWTWO @ L50**, not for flavour: enemy strength is self-referential
  (`EncounterFactory.ScaleTargetBst = playerBst + depth×10`), so raising the starting *level* buys nothing — the
  foe re-scales to match. Raising the starting *BST* does, because the scaling saturates: at 680 the target runs
  off the top of the Gen 1 roster and `PickByBst` can only return the closest (weaker) species it has. A
  CHARIZARD @ L40 died on node 4 of 6 to a GYARADOS in a water-themed biome — needing ~5 wins in a row turns a
  per-battle coin flip into a ~3% reach.
- `move-replacement.spec.ts` (forget / decline, incl. the two-step confirm) — `learnset.spec.ts` had recorded
  this modal as "not reliably reachable without the seed"; the seed plumbing closes it. Lead is **VICTREEBEL
  @ L12**: one of only four Gen 1 species whose *fifth* level-up move lands below level 16 (four moves at level
  1, a fifth at 13), so starting at 12 puts the prompt one level-up away, and its 490 BST means wins pay well.
- `evolution.spec.ts` (Allow / Cancel — Gen 1 B-cancel) — lead is **CHARMANDER @ L15** (evolves at 16). Two
  rejected leads made the selection rule explicit, and both plausible heuristics are wrong on their own:
  **CATERPIE @ L5** (evolves at 7 — with WEEDLE the earliest in Gen 1) has 21 max HP and died on the biome's
  Elite before the seventh level every run, so *"fewest levels to climb"* fails; **DRAGONAIR @ L54** (BST 350,
  the sturdiest level-up evolver in the game) then won four battles and **gained no level at all** —
  `Run over — 4 wins, reached level 54` — so *"highest BST"*, the lever `poke-center.spec.ts` correctly uses
  for a whole-biome reach, fails too. XP required per level grows cubically with level while XP earned grows
  only linearly with the (level-matched) enemy, so a high-level lead effectively never levels, and no level-up
  means no evolution check. **A level-up-gated reach must be low-level to cross at all** — the same reason
  `move-replacement.spec.ts` sits at L12.
  The two answers run as two tests with **disjoint seed lists** (1–10 / 11–20). Merging them into one run was
  tried first, because Gen 1's B-cancel re-offers at the next level-up and that would have made the re-offer
  itself assertable; it is not reachable in practice, needing two level-ups in one run with the second taken
  while carrying the form you just declined to upgrade. Across 24 seeded runs (12 CHARMANDER, 12 DRAGONAIR) not
  one got there — the cancelled run kept dying to the biome Boss that came next, which is the cost of
  cancelling working as designed, not a defect. Re-offer stays covered at the .NET layer.
- `acquisition.spec.ts` (ADD / DECLINE on the themed draft) — the offer both switch specs were already clicking
  through blind to grow the party past one, now asserted directly: ADD deposits into the party (the party strip
  appears, which only renders above one member); DECLINE is a sequencing no-op that leaves the party alone and
  the run flowing.

**Shared driver:** the new `walkSeedsUntil` helper (`e2e/helpers.ts`) — walks a list of seeds (default
`[1..8]`), replaying `startBattle` + clearing every between-node modal (draft/lead-choice/shop/reward) until a
caller-supplied `reached(page)` predicate holds, an accompanying `isShowing(locator)` probe, and a
`chooseBestMove` helper (picks the highest `power × type-effectiveness × STAB` move off the menu's own cues,
`.move-pow`/`.move-eff`/`.move-stab`) — needed because these reaches are several battles deep and a
first-available-move autoplayer (fine for a single-turn spec) reliably loses the run before the state under
test exists. Extracted from the two copy-pasted seed-walk loops already in `forced-switch.spec.ts` /
`voluntary-switch.spec.ts`, both reworked onto the shared driver in the same change. The loop body is also
exported on its own as **`playCurrentRunUntil`** (same driver, no restart) so a spec can carry on with the run
it already reached instead of paying for a second walk, and the draft answer is a policy —
**`drafts: 'accept' | 'decline' | 'leave'`** — because the three cases are genuinely different: `accept` is the
only way a party grows past one, `leave` is for the spec whose target *is* that modal, and `decline` keeps the
run flowing while holding the party at one, which matters because *every* creature that levels is eligible for
the level-up prompts — a drafted second creature can raise the very modal a spec is waiting on and fail its
identity assertions.

**A prompt raised by a level-up is answered on a *win*, so what follows it is the intermission — not another
turn.** `move-replacement.spec.ts` first asserted that FIGHT re-enables straight after the answer and failed
against a completely healthy run: the reward-choice modal was up and FIGHT was correctly `action-btn--waiting`.
The right claim is that the between-encounter flow clears and the next battle becomes playable. (The same shape
is why that spec now reads the moveset back through CHECK POKEMON a whole encounter later — it asserts the
moveset *persisted*, not merely that the modal rendered.) One more DOM fact the first cut got wrong: a party
chip carries its species name only in the sprite's `alt` and its own `title` — visibly it is a sprite, a level
and a LEAD tag, so asserting on its rendered text reads back `"Lv31LEADLv21"`. See below (**In-Combat
Switching** → 2026-07-26 addendum) for what `voluntary-switch.spec.ts` itself absorbed.

**Note on `level` defaults:** `walkSeedsUntil` defaults to **level 30**, not the starter minimum — a level-5
lone starter frequently wipes before the draft cadence comes round (an Elite's VAPOREON ended every one of
eight seeded runs standalone at level 5), burning the whole seed list on runs that never reach the state under
test. Specs that need the lead to *faint* (`forced-switch.spec.ts`) pass `level: 5` explicitly.

---

## In-Combat Switching — voluntary in-battle party switching ✅ COMPLETE (2026-07-25)

Confirmed a core feature by the user (2026-07-13) — a first-class "SWITCH" turn action so the player can swap the
active creature **mid-battle**, like the mainline games. Distinct from — and much larger than — Phase 4's lead
management: Stage 1d only picks the lead **between biomes** (no engine change); Stage 3 only handles a **forced**
switch when the lead faints. This feature is the **voluntary, any-turn** switch: choose SWITCH instead of
FIGHT/BAG, pick a benched creature, and it comes in at the cost of your turn. `/plan` done 2026-07-24.

**The actual hard part, found during `/plan` (not in the original scope note).** `AttackAction.Target` is a
`Creature` reference captured **at construction time**, and `Battle.StartFightAsync` builds the enemy's
`AttackAction` (`Target = PlayerCreature`) immediately after building the player's action — **before** the turn
queue is sorted or executed. If the player's action this turn is a switch and it (correctly) resolves at higher
priority than the enemy's move, `Battle.PlayerCreature` gets reassigned to the incoming creature, but the
already-built `enemyAction.Target` still points at the **old, benched** creature object — the enemy would hit the
Pokémon that just left the field, not the one that just came in. Nothing in the pre-existing test suite caught
this because forced-switch (Stage 3) only reassigns `PlayerCreature` *after* both of a turn's actions have already
executed, never mid-turn. **Fix:** the enemy-side target now resolves live off `Battle`'s current `PlayerCreature`
rather than a value snapshotted at construction, via an `internal Retarget(Creature)` `Battle` calls on any
still-queued action right after a switch executes. This was the one piece that was genuinely a central
`Battle`/`AttackAction` turn-resolution change; everything else was wiring a fourth `TurnChoice` through a pattern
already shipped three times (Acquisition, LeadChoice, forced SwitchIn).

**Design (as built):**
- **Engine.** `SwitchTurnChoice(int PartyIndex) : TurnChoice` (sibling of `MoveTurnChoice`/`ItemTurnChoice`).
  `SwitchAction : IBattleAction` at `SwitchPriority = 7`, above `ItemAction.ItemPriority` (6) — Gen 1 switching
  resolves before even an item use, so it always beats the enemy's move regardless of speed. Execution: restore
  any Mimic/Transform on the outgoing creature, `party.SetLead(index)`, reassign `Battle.PlayerCreature`,
  `ResetBattleState()`, re-apply the incoming creature's own `CarriedStatus` — this is `TrySwitchInAsync`'s
  existing body, extracted so the forced and voluntary paths share one implementation (`RestoreOutgoing` +
  `BringInMember`). Plus the retarget fix above. `BuildPlayerActionAsync` gained a third branch for
  `SwitchTurnChoice` alongside FIGHT/ITEM.
- **Trap gate.** A creature with `Battle.BindingTurnsRemaining > 0` cannot execute a `SwitchAction` (`Battle.CanSwitchTo`) —
  that's the entire point of Wrap/Bind/Clamp/Fire Spin. This is **not** the same gate as `StatusResolver.CanAct`:
  sleep, paralysis, confusion, and flinch do **not** block switching in Gen 1, only trapping does.
  **Corrected pin during `/plan`:** an earlier draft said this belonged on `IBattleRules` — wrong; "trapped ⇒
  can't switch" is invariant across every generation, and `BindingTurnsRemaining` is an ordinary counter check,
  same class as `ItemAction.ItemPriority` being judged gen-invariant in `GENERATION_SEAMS.md §5.0.2`. No new seam
  member.
- **Struggle vs. true lock-in — two different rules, don't conflate them (a correction made during `/plan`):**
  - **Struggle (all moves out of PP)** does **not** block BAG or SWITCH in Gen 1 — the full menu still shows;
    only *choosing* FIGHT with nothing selectable resolves to Struggle. This uncovered a **pre-existing bug**:
    `BuildPlayerActionAsync` used to return Struggle unconditionally without ever consulting
    `ChooseTurnActionAsync` when `!CanSelectAnyMove`, so BAG was already silently unreachable out-of-PP. Fixed as
    part of this work: the turn choice is still offered; only FIGHT with no valid move auto-resolves to Struggle.
  - **True lock-in** (Rampage/Thrash/Petal Dance, Bide, a two-turn charge, and the Gen-1-specific quirk where the
    Wrap/Bind/Clamp/Fire Spin *user* is also forced to keep repeating it) blocks everything, no menu at all — this
    was already correct as shipped (`LockInMechanics.ForcedMove` bypasses `ChooseTurnActionAsync` entirely) and
    needed no change; SWITCH is bypassed for free by the same early return.
- **Gen 1 fidelity (DoR #4):** switching resets stat stages and volatile conditions (confusion, Leech Seed,
  Disable, substitute, two-turn/charge lock, …) but **keeps major status** on the creature (reuses Stage 3's
  `ResetBattleState()`/`CarriedStatus` machinery, zero new surface). No hazards, no abilities, no Pursuit, no
  Baton Pass — all post-Gen-1, out of scope by construction.
- **Events + wire.** No new `IBattleInput` method needed (unlike forced SwitchIn) — this rides the *existing*
  `ChooseTurnActionAsync`/`TurnRequest` seam already carrying `MoveRequest`/`ItemRequest`: a `SwitchRequest(int
  Index)` case, a `ChooseSwitch(int)` hub method completing the same `_turnTcs`, mapped to `SwitchTurnChoice`.
  `CreatureSwitchedIn`/`PartyUpdated` (already existing from Stage 3) are reused as-is; no new `CreatureSwitchedOut`
  event was needed.
- **Frontend.** A fourth `ActionMenu` button (SWITCH) opening a **dismissable** party picker (reusing
  `PartyCard`/`SwitchInModal`'s grid, unlike the forced modal's `dismiss="blocking"`) — Back returns to the menu
  with no turn spent; the *active* member is greyed out in addition to fainted ones. A single `CanSwitch` boolean
  is projected onto `TurnStarted` (same precedent as `DisabledMove` on `MoveInfo`) so the client can grey out
  SWITCH proactively, backed by a server-side no-op as defense in depth. **One field, not two** — the trapped
  case folds into `CanSwitch` via `CanSwitchTo`'s `BindingTurnsRemaining` check, so no separate `IsTrapped` signal
  shipped. The cost is that the client can't tell *why* SWITCH is greyed (no bench / trapped / locked-in all
  collapse to one disabled button); split the field if that distinction is ever wanted in the UI copy.
- **Malformed/stale switch pick.** Unlike the forced switch (which must send someone in), a voluntary switch has
  a safe fallback: an invalid index (fainted / out-of-range / the already-active slot) is treated as if FIGHT had
  been chosen with the default move, rather than stranding the turn.
- **Enemy AI switching** stays a later refinement — shipped player-only, per the original scope note.

**Gen-variable surface (DoR #3): none.** Switch-first turn order is gen-invariant (inline constant, same precedent
as `ItemAction.ItemPriority`); the reset-volatiles-keep-status rule reuses Stage 3's existing mechanism;
trapping-blocks-switch is ordinary engine logic. Zero new `IBattleRules`/`ITypeChart`/`IStatCalculator` members;
zero importer/DB change.

**Staged build (each increment shipped and greenlit separately, decided 2026-07-24):**

1. **Stage A — the engine core, DONE (2026-07-24, Opus).** Shipped: the **retarget fix** (`AttackAction.Target` is
   reassignable via an `internal Retarget`; after a voluntary switch resolves, `Battle` repoints the enemy's
   still-queued action onto the creature that came in — proven by a slower-enemy and a +1-priority-enemy test);
   `SwitchAction : IBattleAction` at `SwitchPriority = 7`; the trap gate (`Battle.CanSwitchTo`); and the
   Struggle-menu fix (`BuildPlayerActionAsync` now consults the whole-turn menu even out of PP, so BAG/SWITCH stay
   reachable and only *choosing FIGHT* with nothing selectable resolves to Struggle). New `SwitchTurnChoice` +
   `StruggleTurnChoice` turn choices; the default `IBattleInput.ChooseTurnActionAsync` returns `StruggleTurnChoice`
   out of PP instead of throwing. The forced (Stage 3) and voluntary switch-out paths share one send-in
   implementation (`RestoreOutgoing` + `BringInMember`); the voluntary path additionally **captures the outgoing
   creature's major status** onto its `CarriedStatus` (`CaptureOutgoingStatus`) so status persists on switch-out
   (re-enters ailed; benches ailed) while volatiles reset — Gen 1 fidelity. Covered by **`BattleVoluntarySwitchTests`**
   (retarget slower + priority, trap-refused, status-persists-volatiles-reset, out-of-PP-reaches-SWITCH,
   illegal-pick→FIGHT incl. active-slot/out-of-range/fainted, true-lock-in-bypasses-menu,
   incoming-faints→forced-path-takes-over). **Web-leg interim (since removed):** `SignalRInput.ChooseTurnActionAsync`
   returned `StruggleTurnChoice` immediately when out of PP — preserving the pre-existing auto-Struggle web
   behaviour exactly (no live regression). **Stage C deleted this guard**; see the out-of-PP menu-affordance
   paragraph below for the shipped behaviour.
2. **Stage B — wire, DONE (2026-07-25).** The voluntary SWITCH command rides the existing one-per-turn
   `ChooseTurnActionAsync`/`TurnRequest` handshake (no new `IBattleInput` method): `SignalRInput` gained a
   `SwitchRequest(int Index)` mapped to `SwitchTurnChoice`, plus `SetSwitchChoice(int)`;
   `GameSessionManager.SetSwitchChoice` routes it; `BattleHub.ChooseSwitch(int)` is the hub entry point. Covered by
   `SignalRInputTests.SetSwitchChoice_YieldsASwitchChoiceForThatPartyIndex`. No new server→client event (Stage 3's
   `CreatureSwitchedIn`/`PartyUpdated` are reused), so no new `WebEventContractTests` guard was needed.
3. **Stage C — frontend, DONE (2026-07-25).** A 4th `ActionMenu` SWITCH button (2×2 grid) gated on a new
   `TurnStarted.CanSwitch` signal — server-computed in `Battle.CanSwitchThisTurn` (party wired, not locked-in per
   `ILockInMechanic.IsLockedIn`, and a live benched target via `CanSwitchTo`; **deliberately independent of PP**,
   since Gen 1 lets you switch with no usable move) and projected through `SignalRBattleEventEmitter` (the
   reflection contract test auto-guards the field's presence; `TurnStarted_Projection_CarriesCanSwitch` pins its
   value both ways). Opens a
   **dismissable** `SwitchMenu` control-view (like FIGHT/BAG, not a blocking modal) reusing `PartyCard`: the active
   lead (`· OUT`) and fainted members greyed, a live benched member selectable → `chooseSwitch` → `ChooseSwitch` hub
   call → the switch resolves, reusing Stage 3's `CreatureSwitchedIn` send-in ("Go! X!" + nameplate retarget).
   `timeline.ts`/`battleReducer.ts` carry `canSwitch`; `useBattleHub.chooseSwitch`. Covered by Vitest (reducer +
   timeline `canSwitch`), the C# contract test, and **E2E `voluntary-switch.spec.ts`** (seeded run → draft accepted
   → SWITCH enabled → pick benched → "Go! X!" + nameplate retargets + fight continues).
4. `requirements-review` **adjudicated for Stage A (2026-07-24)** — confirmed the Struggle-vs-full-menu and
   trapped-victim (only `BindingTurnsRemaining` blocks switching, not sleep/paralysis/confusion) claims as
   faithful Gen 1, and surfaced three further edges the user then ruled on: **(a) Toxic/Bad Poison downgrades to
   regular Poison on a mid-battle switch-out** (code was right; the wrong `GEN_DIFFERENCES.md` line was corrected)
   — Gen-1 accurate; **(b) Rage blocks switching** like every other lock-in (kept as-is, user's call); **(c)
   switching out during a Hyper Beam recharge turn is allowed** (`CanSwitchTo` intentionally doesn't gate on
   `IsRecharging`; documented in `GEN_DIFFERENCES.md`). All three are covered by named `BattleVoluntarySwitchTests`
   cases, plus an affirmative sleep/paralysis/freeze-still-switches test.

**The quirk tested (DoR #6), above all:** a switch this turn followed by a slower enemy move lands on the incoming
creature, not the one that just left (the retarget bug made into an assertion). Plus: trapped ⇒ can't switch;
volatiles reset / major status persists on switch-out; switch always precedes the enemy's move regardless of
speed/priority; the incoming creature can faint to the same turn's enemy hit with no recursive switch prompt
(that's the *forced* path's job next turn); out-of-PP still reaches BAG/SWITCH (the Struggle-menu fix); true
lock-in still blocks everything (regression, already covered).

**Dependencies:** Stage 3 (forced-switch-on-faint, DONE 2026-07-15) — `Battle` already held the party, the send-in
path (`TrySwitchInAsync`) existed to extract from, and the client had the party-picker modal to fork from.
Independent of `save.db`.

**Out-of-PP menu affordance (shipped 2026-07-25, folding in the interim web-leg guard):** Stage A had left
`SignalRInput` auto-Struggling out of PP (a placeholder so the web turn couldn't strand), with `CanSwitchThisTurn`
gated on `CanSelectAnyMove` to match. Closed as a proper Gen-1 fidelity fix: the interim guard is gone, an
out-of-PP `MoveRequest`→`StruggleTurnChoice` (so **Struggle is a consequence of *choosing FIGHT* with nothing
usable**, driven by the click — never auto-resolved), the client's FIGHT button **spends the turn as Struggle on
the spot** when every move is 0-PP/Disabled instead of opening the move list, and the `CanSelectAnyMove` gate is
dropped so **BAG and SWITCH are reachable at 0 PP** (Gen 1 keeps the whole menu open). Covered by
`SignalRInputTests` (out-of-PP FIGHT→Struggle; SWITCH still honoured out of PP), `BattleVoluntarySwitchTests`
(`TurnStarted.CanSwitch` true out of PP, false for a lone starter / while trapped), and Vitest `moveMenu.test.ts`
(the client `hasUsableMove` predicate that mirrors `CanSelectAnyMove`). The engine already modelled this from
Stage A; the fix was entirely the web leg + the interim gate.

*(`requirements-review` initially shipped this as an explicit **STRUGGLE button** inside the FIGHT submenu — a
second click to confirm. Flagged as a Gen-1 divergence (the cartridge prints "no moves left" and Struggles
immediately on FIGHT, never showing a move list) and **fixed on the user's ruling, 2026-07-25**: FIGHT now
auto-submits, and the submenu's Struggle branch + its `.move-btn--struggle` CSS are gone.)*

**Addendum (2026-07-26) — the rest of the UI contract.** Stage C's E2E left only the happy path
(`voluntary-switch.spec.ts`: seeded run → draft accepted → SWITCH enabled → pick benched → "Go! X!" + nameplate
retargets). Three more behaviours that distinguish the *voluntary* picker from Stage 3's forced modal are now
pinned in the same spec: **SWITCH visible-but-disabled while the starter is alone** (`TurnStarted.CanSwitch`
reaching the DOM, asserted on the very first turn — no seed walk needed, a party of one is the default state),
**the creature already out rendering as a disabled `· OUT` card** (the forced modal has no such card, since the
outgoing creature there has fainted — this is the render that tells the two pickers apart), and **BACK
dismissing the picker with no turn spent** (a dismissable control-view, unlike the forced modal's
`dismiss="blocking"`; no server prompt is parked on it). Folded into the one existing switch-through test rather
than a second test: the suite runs `workers: 1`, so as two tests the seed walks were sequential and identical on
paper, and the second exhausted all eight seeds where the first had already found one (the seed-≠-determinism
drift this suite already documents, hit again — the fix was to stop needing a second walk, not to harden it).
**Deliberate E2E gap, decided the same day:** the out-of-PP menu affordance above has no spec and isn't getting
one — draining a full moveset takes tens of turns, PP refills at every Poké Center, and no low-level moveset is
small enough to burn out reliably; it stays pinned by `SignalRInputTests`, `BattleVoluntarySwitchTests`, and
Vitest `moveMenu.test.ts` alone. Recorded in `e2e/README.md` too. Revisit only if a backend test hook makes the
state forceable.

Touched (engine): `creaturegame/Combat/Battle.cs`, `AttackAction.cs`, `SwitchAction.cs` (new),
`IBattleInput.cs` (`SwitchTurnChoice`/`StruggleTurnChoice` + the guarded default `ChooseTurnActionAsync`),
`CarriedStatus.cs` (shared `Capture` factory) + `RunEvents/BattleRunEvent.cs`, `BattleEvents.cs`
(`TurnStarted.CanSwitch`); (web) `creaturegame.Web/Battle/SignalRInput.cs`, `Battle/GameSessionManager.cs`,
`Battle/SignalRBattleEventEmitter.cs`, `Hubs/BattleHub.cs`; (frontend) `ClientApp/src/battle/timeline.ts`,
`hooks/battleReducer.ts`, `hooks/useBattleHub.ts`, `battle/moveMenu.ts` (new — the `hasUsableMove` mirror of
`Creature.CanSelectAnyMove`), `pages/BattleScreen.tsx` (the SWITCH button, the `SwitchMenu`, and the out-of-PP
`handleFight`); (docs) `GEN_DIFFERENCES.md` (Toxic-downgrade + recharge-switch fixes), `FRONTEND_PLAN.md`,
`GAME_LOOP.md`, `ARCHITECTURE.md`. Tests: `BattleVoluntarySwitchTests`, `SignalRInputTests`, Vitest
reducer/timeline/`moveMenu` cases, `WebEventContractTests` (reflection guard + a `CanSwitch` value pin), E2E
`voluntary-switch.spec.ts`.

---

## `reward-drop.spec.ts` red — misdiagnosed as seed-31 RNG drift ✅ DONE (2026-07-23)

Filed 2026-07-19 as "seed-31 drift": the spec pinned seed 31 expecting CHARIZARD @ L50 to open the biome on a
**Treasure** node (no battle needed before the reward modal). The original filing blamed an unspecified earlier
commit for adding/moving an RNG draw ahead of node planning.

**Actual root cause (not RNG drift):** `creaturegame/Combat/RunDirector.cs`'s `DefaultNodePlan` hardcodes
`plan[0] = RunNodeKind.WildBattle` — a deliberate "soft opening" design rule ("the opening node of a biome is
always a plain wild battle — never an Elite or an interaction node — so a biome can't greet the player with a
difficulty spike or a non-combat slot on entry"). **No seed can ever land a Treasure first** under this rule.
Confirmed live against the dev server: seeds 1–40 all open on a battle, never a Treasure. Exactly when this rule
landed is unknown, but it predates the fix and made the seed-31 premise permanently false, not drifted.

**Fix:** since a battle win funnels through the same `RewardGranted` → reward-choice modal as a Treasure/Mystery
node (one drop-choice UI for every source, per `battleReducer.ts`), the spec now wins the first deterministic
battle instead of relying on a Treasure node. New `WIN_FIRST_BATTLE_SEED = 1` (CHARIZARD @ L50 vs STARMIE, wins
in ~4 turns via the default first-available-move auto-play). Seed 31 was tried first and rejected: under the new
"first node is always a battle" rule it pits CHARIZARD against GENGAR, and CHARIZARD's default first move
(SCRATCH, Normal) is a Gen 1 immunity match (0×) against Ghost, so the naive auto-play loop can't win it — hence
seed 1, not seed 31 patched in place. Also updated the log-line assertion: a battle-win reward logs
`"Found {N}G!"` (`rewardGrantedMsg` in `timeline.ts`), not the old Treasure-sourced `"The {source} held {N}G!"`
text. Renamed the `describe` block: `'Run Economy reward choice (Treasure node)'` →
`'Run Economy reward choice (battle-win drop)'`.

Touched: `creaturegame.Web/ClientApp/e2e/reward-drop.spec.ts` only (test-only diff).

---

## Settings Menu — sound volume + difficulty (XP bonus) controls ✅ DONE (2026-07-21 → 2026-07-22)

*(Moved here from `TODO.md` 2026-09-27 during a full-file TODO audit — both slices had shipped and the full
design/build record was sitting in the active list past completion.)*

**`/plan` done (2026-07-21).** Two independent slices, neither touches a generation seam.

- **Sound volume.** `AudioEngine.ts` had no volume control at all — every sound hardcoded a literal gain
  straight to `a.destination`. Added one persistent `masterGain` node every sound now routes through, plus
  `setMasterVolume`/`getMasterVolume` (clamped 0–1). New `utils/settings.ts` persists to `localStorage`
  (`creaturegame.settings`, `{ masterVolume }`, default `1.0` = unchanged historical behaviour); applied once
  at boot in `main.tsx` before any sound plays — `setMasterVolume` only records a pending value until the
  AudioContext actually exists (first sound played), so applying a persisted setting at load never trips the
  browser's autoplay-policy warning pre-gesture. The actual controls live in a shared `SettingsPanel`
  component with two chrome wrappers: a full-page `/settings` route (`SettingsScreen.tsx`) reached via a
  `.settings-gear-btn` corner icon on `TitleScreen`, and a `SettingsModal` (in `components/modals/`, the
  Modal component's first real use of its escapable `{ onEscape }` dismiss — nothing here parks a
  server-side await, so closing costs nothing) reached via the same icon in-battle.
  > **Real trap hit and fixed during build:** the in-battle icon originally did a page `nav('/settings')`
  > like the Title Screen one. That unmounts `BattleScreen`, tearing down its live SignalR connection —
  > `GameSessionManager.AttachConnection`'s reconnect path resumes the *transport* but never replays the
  > accumulated battle state into a fresh component, so returning left the screen stuck on "Connecting…"
  > (and intermittently crashed on a stale-state read). Fixed by keeping `BattleScreen` mounted and opening
  > `SettingsModal` as local state instead — verified in-browser: settings opened and closed mid-battle,
  > the same `RAZOR LEAF` attack still resolved correctly afterwards. The Title Screen's plain page nav is
  > fine as-is (no live session to protect there).
- **Difficulty → XP bonus.** `RunRules` (`creaturegame/Combat/RunRules.cs`) is already the sanctioned knob for
  this — its own doc comment says it exists to be "trivially exposable as sliders," deliberately outside
  `IBattleRules`/`ITypeChart`/`IStatCalculator`. Today it's one hardcoded `RunTuning` static in
  `GameSessionManager.cs` (`XpMultiplierEarly=1.5, XpMultiplierLate=4.5, BenchXpShare=0.5`). Plan: three named
  presets (Easy/Normal/Hard) — Normal = today's live numbers unchanged (a true no-op regression-wise) —
  threaded exactly like `Level`/`Seed`: `StartGameRequest.Difficulty` → `GameController.Start` →
  `RegisterSession` → `PendingSession` → `AttachConnection` picks the matching preset instead of the static.
  Frontend: a 3-position segmented control (not a raw range input — 3 named tiers, not a continuum) next to
  the existing Level slider on `StarterSelection.tsx`, default Normal, sent in the `/api/game/start` body.
  **Per-run, not a global default** — matches how Level/Seed already work; no new persistence needed.
- **DoR:** gen-variable surface is **none** for both (volume is pure presentation; difficulty only touches
  `RunRules`, already documented as living outside every seam) — no importer/DB change, no `save.db` need
  (volume is `localStorage`; difficulty is a per-run request param like Level/Seed). Independent of every
  other in-flight feature.

- [x] **Sound volume** ✅ DONE (2026-07-21) — `AudioEngine.ts` master-gain plumbing (+ `AudioEngine.test.ts`),
  `utils/settings.ts` (+ `settings.test.ts`), the shared `SettingsPanel`, `SettingsScreen.tsx` + `/settings`
  route, `SettingsModal.tsx`, gear-icon entry points on `TitleScreen` (nav) + `BattleScreen` (modal — see the
  trap above). Verified live in-browser (persistence across reload, in-battle modal, post-modal attack).
  A follow-up gap surfaced independently the same day: Phaser's own `SoundManager` plays OGG cry files
  through a pipeline separate from `AudioEngine`'s Web Audio graph, so the master-gain node never reached
  it — fixed by scaling the cry's playback volume by `Audio.getMasterVolume()` in `BattleScene.ts`.
- [x] **Difficulty → XP bonus** ✅ DONE (2026-07-22) — `Difficulty` enum (Easy/Normal/Hard) +
  `RunTuningByDifficulty` presets in `GameSessionManager.cs` (Normal reproduces the old hardcoded `RunTuning`
  exactly — verified byte-for-byte in `DifficultyTests.cs`, a true no-op), threaded via `StartGameRequest` →
  `GameController.ParseDifficulty` (case-insensitive, falls back to Normal) → `RegisterSession` →
  `PendingSession` → `AttachConnection`, plus the `StarterSelection.tsx` segmented control. Both `ParseDifficulty`
  and the preset lookup (`GameSessionManager.RunRulesFor`) are `internal` specifically so `DifficultyTests.cs`
  exercises the real code path, not a duplicate — a gap `requirements-review` caught (no test had touched
  either). Verified end-to-end in-browser: HARD selected → POST body carries `"difficulty":"Hard"` → run
  starts normally. 1388/1388 .NET (was 1377), 168/168 Vitest, TypeScript clean.
  > **Known limitation, deliberately shipped as-is (user-waived 2026-07-22):** `requirements-review` found
  > that wild-encounter strength is *self-referential* — `EncounterFactory.ScaleTargetBst` is
  > `playerBst + depth×10` and `ScaleWildLevel` is a window on the player's *own current level*, both
  > re-derived from the player's live progression every encounter. So a faster XP pace doesn't make any
  > single fight easier in relative terms — the enemy always re-scales to match whatever level/BST the
  > player currently sits at (and faster evolution can pull in higher-BST species sooner). The dial
  > genuinely only changes *leveling pace*, not combat challenge, despite being labeled "Difficulty." This
  > is exactly what was asked for (an XP-rate dial), so the mechanic ships under that label unchanged.
  > **Flagged to flesh out later, still unscheduled:** either rename to something honest ("Leveling Pace") or
  > add a real difficulty-shaping axis independent of the self-referential scaling (e.g. a flat enemy
  > level/BST offset that doesn't re-normalize to the player) — not scheduled, no target date. Not tracked in
  > `TODO.md` → *Known Gaps*; raise it there if it's ever picked up.

---

## Haze over-resets: it cures the user's own major status ✅ DONE (2026-07-20)

Filed from the 2026-07-19 repo-wide audit: `HazeEffect` (`MoveEffects.cs:76`) called a full
`Creature.ResetBattleState()` on **both** sides. Gen 1 Haze resets both sides' stat stages + volatiles but cures
only the **opponent's** non-volatile status (per pokered's `engine/battle/move_effects/haze.asm`) — the
wholesale reset let a paralyzed (or otherwise statused) user Haze itself healthy, which is not Gen 1 behavior.
The original filing also flagged the reset's Transform/Mimic-reverting scope as unverified.

**Fix, broader than the original one-liner once the pokered citation was run down:**
- New `Creature.ResetForHaze(bool preserveMajorStatus)` — a narrow, field-by-field reset (**not** a wholesale
  `Battle = new BattleState()`) that clears only what Gen 1 Haze actually clears: stat stages, Confused, Disable,
  Mist, Focus Energy, Leech Seed, Reflect/Light Screen, and a Toxic→Poison downgrade. Substitute, Bide, Rampage,
  Rage, Binding, Recharge, Charging, Flinch, `LastMoveUsed`, Counter-memory, and any active Transform/Mimic
  identity are left completely untouched — resolving the original filing's "is Transform/Mimic reverted?"
  question as **no**.
- `HazeEffect.Apply` now calls `ResetForHaze(preserveMajorStatus: true)` on the user and `false` on the target —
  only the target's major status is cured, never the user's own.
- **Second-order fix caught along the way:** curing a target's Sleep/Freeze mid-turn must still forfeit that
  target's already-chosen action for the same turn (a verified Gen 1 quirk). Implemented via a new
  `BattleState.HazeSuppressedStatus` field, consumed once in `StatusResolver.CanAct`. `Battle.cs`'s turn loop
  clears any unconsumed flag at end-of-turn — `requirements-review` caught that without this, the flag could leak
  into the *next* turn and cause a bogus second forfeit when the target is faster than the Haze user; fixed and
  regression-tested.
- `docs/GEN_DIFFERENCES.md` → *Status Quirks* documents Haze's resolved Gen 1 scope.
- Stale doc comments that had incorrectly described Haze as reverting Transform/Mimic were corrected in
  `TransformContractTests.cs`, `MimicContractTests.cs`, `Creature.cs`, and `BattleState.cs`.

Touched: `creaturegame/Creatures/Creature.cs`, `creaturegame/Creatures/BattleState.cs`,
`creaturegame/Combat/StatusResolver.cs`, `creaturegame/Combat/MoveEffects.cs`, `creaturegame/Combat/Battle.cs`,
`docs/GEN_DIFFERENCES.md`; tests in `StatStageTests.cs`, `StatusConditionTests.cs`,
`UniqueMoveEffectContractTests.cs`, `TransformContractTests.cs`, `MimicContractTests.cs`.

**Verification:** two rounds of `requirements-review` (citing pret/pokered's
`engine/battle/move_effects/haze.asm`), both **MET** after the second-order end-of-turn-leak fix; full
pre-finish gate sequence green — 1377/1377 .NET tests, Vitest, TypeScript, CSharpier.

---

## BUG — sprites and cries missing on live (Fly) deploy ✅ DONE (2026-07-20)

Filed 2026-07-20 as the highest-priority active bug — visible breakage on the deployed game (no sprites
rendered; a synthesized placeholder played instead of the real cry).

**Root cause:** `creaturegame.Web/wwwroot/sprites/` and `wwwroot/audio/` are gitignored ("runtime-generated by
`PokeApiConnector`; not source files") — populated only when a dev runs `dotnet run --project PokeApiConnector`
locally. `Dockerfile`'s comment assumed wwwroot "already holds audio/ + sprites/", but the GitHub Actions deploy
job does a clean `actions/checkout` with none of that gitignored content, so every image shipped to Fly had
**zero** sprite PNGs and **zero** cry OGGs baked in — sprites didn't render, and `BattleScene.ts`'s documented
missing-audio fallback (`cache.audio.exists()` stays false → `Audio.playCry` synth) played instead of the real
cry. Local dev never showed this because the dev machine already had the assets on disk from a prior import run.

**Fix (commit `4e3fdef`):** a new `assets` build stage in `Dockerfile` runs a new `PokeApiConnector … -- assets`
mode (`Program.cs`) that calls the existing, idempotent `SpriteDownloader`/`ItemSpriteDownloader`/`CryDownloader`
(sprites/cries need no local DB — species-ID-keyed static URLs; item sprites read the already-committed
`items.db`) during every image build, so the shipped image is self-contained regardless of what the CI checkout
has on disk. The `backend` stage now copies the `assets` stage's `wwwroot/sprites/` + `wwwroot/audio/` output in
before publish. The three downloaders now return their failed-file count instead of `void`, and the `assets`
mode sums them and calls `Environment.Exit(1)` on any failure — a partial download now fails the Docker build
loudly instead of silently shipping another broken image (caught by `pr-review`, verified by a force-fail test
then reverted).

**Verification:**
- `pr-review`: **PR-READY** (0 changes-requested; the 1 recommended finding — silent partial-failure risk — is
  the fail-loud hardening above). `format-gate`: PASS. `test-runner`: PASS (1527/1527 both before and after the
  fail-loud follow-up; E2E skipped — stack down).
- Ran `flyctl deploy --build-only` (builds via Fly's actual Depot remote builder, pushes the image, does **not**
  deploy/touch the live app) from a genuine clean `git worktree` checkout — tracked files only, no gitignored
  `wwwroot` content, i.e. exactly what the GitHub Actions `actions/checkout` step produces. Result: the `assets`
  stage genuinely fetched all 151 sprite pairs + 29 item sprites + 151 cries from scratch, 0 failures.
- **Shipped and confirmed live (2026-07-20):** tagged `v0.1.0-rc.1` on `4e3fdef` → the `Fly Deploy` GitHub Action
  fired, built, and deployed release v3 (`GH_SHA` label on the running machines matches `4e3fdef` exactly). Live
  HTTP checks against `creaturegame.fly.dev` for both early- and late-range species
  (`/sprites/front/1.png`, `/sprites/back/1.png`, `/audio/cries/1.ogg`, `/sprites/front/151.png`,
  `/audio/cries/151.ogg`) all returned `200` with byte counts matching the local build exactly (e.g. 543 / 423 /
  5813 bytes for species 1) — the fix is confirmed working in production, not just in a build test.

---

## Paralysis Speed quartering is an inline gen-variable magic number ✅ DONE (2026-07-20)

Filed from the 2026-07-19 repo-wide audit (ranked first by severity of the two open findings): the Paralysis
Speed handicap in `StatusResolver.EffectiveSpeed` hardcoded `speed /= 4`. The divisor is gen-variable (Gen 1–6:
quarter; Gen 7+: half) and `StatusResolver` is explicitly on `GENERATION_SEAMS.md` §5.0's no-magic-number list,
so it belonged on `IBattleRules`, not inline.

Fixed 2026-07-20: new seam member `IBattleRules.ParalysisSpeedDivisor` with the per-gen XML doc (Gen 1–6: 4;
Gen 7+: 2), implemented as `Gen1BattleRules.ParalysisSpeedDivisor => 4`, pass-through added to the
`DelegatingBattleRules` test double, and `StatusResolver.EffectiveSpeed` now reads the new member instead of the
literal 4. Pinned by `TurnOrderTests.Paralysis_EffectiveSpeedReadsItsOwnSeamMemberNotAHardcodedFour` — a rules
double overrides only the paralysis member to 2 (the Gen 7+ value) and asserts `EffectiveSpeed` follows it,
proving the resolver no longer hardcodes 4. The two pre-existing quartering tests
(`Paralysis_EffectiveSpeedIsQuartered`, `SpeedStage_StacksWithParalysisQuartering`) are untouched and still pass,
confirming the extraction didn't change Gen 1 behavior. Mirrors the shipped `LeechSeedDrainDenominator` precedent
(same commit shape, same audit). `requirements-review`: **MET** (confirmed Gen 1 quarters Speed under Paralysis
and the Gen 7+ half claim against known mechanics; arithmetic unchanged). Full .NET suite green: 1367/1367.

---

## Leech Seed drain borrows PoisonDamageDenominator ✅ DONE (2026-07-20)

Filed from the 2026-07-19 repo-wide audit (promoted from a minor note to a real bug by the user): the end-of-turn
Leech Seed drain in `Battle.ApplyLeechSeedDrain` read `_rules.PoisonDamageDenominator` — the same 1/16 value in
Gen 1, but a *distinct game rule* on the wrong seam member. The two change on different generation boundaries
(Gen 2 raises the drain to 1/8 while Gen 1–5 Poison stays at 1/16), so a later gen changing one and not the other
would have forced a split, and the member name misdocumented what the drain reads.

Fixed 2026-07-20: new seam member `IBattleRules.LeechSeedDrainDenominator` with the per-generation XML doc
(Gen 1: 16; Gen 2+: 8), implemented as `Gen1BattleRules.LeechSeedDrainDenominator => 16`, pass-through added to
the `DelegatingBattleRules` test double, and the drain site now reads the new member. Pinned by
`LeechSeedContractTests.DrainReadsItsOwnSeamMemberNotThePoisonDenominator` — a rules double overrides only the
leech member to 8 (poison untouched at 16) and asserts the drain follows it, proving the drain no longer borrows
the poison member. Full .NET suite green: 1366/1366.

---

## 0× type immunity does not gate secondary effects ✅ DONE (2026-07-19)

Filed from the 2026-07-19 repo-wide audit: the pre-damage immunity halt in `AttackAction` deliberately excluded
`Standard`/`Drain` movers ("folds 0× into 0 damage"), so `TryApplyStatus` / `TryApplyStatEffect` /
`TryApplyMoveEffect` still ran after an immune hit — concretely, **Body Slam vs a Ghost or Thunder vs a
Ground-type dealt 0 damage yet could still paralyze** (30%/10%), Constrict could drop a Ghost's Speed, and
`FlinchEffect` wasn't damage-gated either. The client also got a `DamageDealt(0)` event instead of a no-effect
line for these hits.

Fixed in `creaturegame/Combat/AttackAction.cs` `ResolvePreDamageGates`: the type-immunity halt now also covers
damaging Standard/Drain movers (incl. Struggle), via a new `isDamagingMove` condition that excludes Crash-effect
moves (Jump Kick's dedicated crash-on-immunity branch below still handles those) and leaves Self-Destruct's
detonation path untouched. An immune hit now emits `MoveHadNoEffect` and halts — no `DamageDealt(0)`, no
secondary status/stat-drop/flinch/recoil/trap. The halt is gen-invariant (Struggle going typeless in Gen 4 —
it stays Normal through Gens 1–3 — rides the type chart, not this gate).

New `tests/creaturegame.Tests/Integration/Gen1Attacks/TypeImmunityContractTests.cs` (8 tests), written per
`GENERATION_SEAMS.md §5.0.1` against the quirk itself, not just the outcome: body-slam/thunder/lick
no-paralysis, constrict no-stat-drop, bite no-flinch, take-down no-recoil, no-effect-instead-of-zero-damage,
Struggle-vs-Ghost no-recoil. Stale comment updated on
`StabAndTypeEffectivenessContractTests.GhostMovesDealNoDamageToImmuneTypesInGen1`. Full .NET suite green:
1364/1364.

**Same-day follow-up (pr-review catch):** the new halt initially skipped the lock-in `OnTurnEnd` tick, so a
Thrash locked onto a Ghost never resolved its rampage lock or fired the end-of-lock self-confusion (the miss
branch ticks it; the immunity halt didn't). Fixed by mirroring the miss branch's tick inside the halt; pinned by
`TypeImmunityContractTests.RampageLockedOntoImmuneTargetStillResolvesAndSelfConfuses`.

---

## Leech Seed drain borrows `PoisonDamageDenominator` ✅ DONE (2026-07-19)

Filed from the same 2026-07-19 repo-wide audit: `Battle.ApplyLeechSeedDrain` (`Battle.cs:576`) read
`_rules.PoisonDamageDenominator` for its 1/16 drain. Numerically identical to Poison in Gen 1, but Leech Seed
drain and Poison damage are distinct game rules that don't necessarily move together across generations — reading
one off the other's seam member misdocuments what the drain actually is and would silently desync the moment a
later gen changes one without the other. *(Promoted from a minor audit note to a real bug by the user,
2026-07-19.)*

Fixed: new `IBattleRules.LeechSeedDrainDenominator` member with its own per-generation XML doc (Gen 1 = 16;
Gen 2+ = 8 — noting the two denominators change on different generation boundaries, which is exactly why the
member must not be shared with Poison's). `Gen1BattleRules.LeechSeedDrainDenominator => 16` with the Gen-1
comment; `DelegatingBattleRules` (the test double) got the pass-through. `Battle.ApplyLeechSeedDrain` now reads
the new member instead of `PoisonDamageDenominator`.

Pinned by new `LeechSeedContractTests.DrainReadsItsOwnSeamMemberNotThePoisonDenominator`: a rules double overrides
only the leech member (8) while leaving Poison untouched (16), asserting the drain follows the leech value — this
would fail against the old shared-member code and proves the fix reads the correct seam. Full .NET suite green:
1366/1366.

---

## Stat-cap message fidelity ✅ DONE (2026-07-19)

A stat move used at the ±6 stage cap used to emit a phantom `StatStageChanged` — the log narrated "X's ATTACK
rose!" / "fell!" even though the stage was clamped and didn't move (the clamp itself was always correct; only the
text was wrong). Fixed in `AttackAction.TryApplyStatEffect`: capture the pre-stage, and if `ApplyStageChange`
leaves it unchanged, split by move kind exactly as Gen 1 does (verified against **pokered**
`StatModifierUp/DownEffect` and [Bulbapedia — Stat modifier](https://bulbapedia.bulbagarden.net/wiki/Stat_modifier)):

- **Primary stat move** (a pure status move, `BaseDamage == 0` — Growl / Swords Dance / …) → announce; the
  message is **gen-variable, so it rides the seam**: new `IBattleRules.StatStageCapAnnouncement` (enum
  `StatCapAnnouncement`), Gen 1 → `NothingHappened` → `ButNothingHappened` ("Nothing happened!"), Gen 3+ →
  `WontChangeFurther` (the later-gen "won't rise/drop anymore!" line, not modelled yet). This is the sibling of
  `RedundantConfusionAnnouncement`, and the call site mirrors its dedicated-move-gate + `switch`-on-enum pattern.
- **Side-effect stat drop** on a damaging move (`BaseDamage > 0` — Acid / Aurora Beam / Psychic 2° …) → **silent**
  (the damage line already showed; pokered's early `ret nc`) — the gen-invariant part, kept inline like the
  secondary-confusion-is-silent case.
- A change that *does* move the stage (e.g. a +2 move at +5 → +6) still announces, with the move's own amount.

The Gen-3+/Gen-5+ "won't rise/go any higher" texts are **deliberately not** emitted — wrong generation; they're
reserved as the `WontChangeFurther` seam value for when that gen exists. Covered by four `StatStageMoveContractTests`
cap cases (primary raise/drop, secondary-silent, near-cap-still-announces). A manual GENERATION_SEAMS §5.0 check
caught the message as an inline gen-variable leak and moved it to the seam. This replaces the *Phantom stat-cap
message* Known Gap filed the same day.

---

## Revive Items — in-battle party revive ✅ DONE (2026-07-19)

Engine + web + frontend all shipped in one session; full suite green. Went through the full pre-finish gate
sequence — the four **gate adjustments** (floor rounding, status-clear, Max Revive held out, Boss weight)
are folded into the sections below and summarised at the end.

**Intent.** Make **Revive** a real, extremely-rare Gen-1 item: on the player's turn, open BAG → REVIVE → pick a
**fainted bench member**, and it returns to the bench at **½** max HP. The first item that targets a *benched*
creature rather than the active one. Complements (does not replace) the post-boss Poké Center whole-party heal.
(**Max Revive** — a Gen-2 item — is fully scaffolded but held out of obtainable loot; see the gate note.)

**Scope decisions locked with the user (2026-07-19):**
1. **In-battle, proactive.** Usable on the player's turn (active creature alive), targeting a fainted bench
   member; the revived member returns at ½/full HP but **stays benched** (LeadIndex/active unchanged — Gen 1
   Revive does not switch it in). **No last-stand rescue:** an all-fainted-at-once loss still fires (Gen 1
   white-out) — the player must revive *before* the final KO.
2. **Acquisition = Boss reward + rare shop stock.** Revives enter the loot pool but only Boss nodes can roll
   them (the previously-dormant Boss category-weight lever from the *Reward Choice — pick-1-of-3 rarity rewards*
   work below now makes them a prime Boss reward); other reward tiers (wild/elite/treasure/mystery) never
   surface a revive. The shop may also stock them at their premium (Rare/Epic) price band.
3. **Items.** Revive (→ 50% HP, Rare band) is live loot. Max Revive (→ 100% HP, Epic band) is a **Gen-2** item
   (Red/Blue/Yellow shipped only Revive) — kept fully scaffolded (import + effect support) but **held out of every
   obtainable channel** (reward + shop) until the multi-generation milestone, so the run stays Gen-1-authentic
   (gate decision, 2026-07-19).

**Acceptance condition.** Holding a Revive, with a fainted party member: on the player's turn BAG → REVIVE →
pick that member brings it from 0 → ⌊MaxHP·pct⌋ HP (floored, ≥1), un-faints it, **and clears its status**;
exactly one revive is consumed; the active creature and LeadIndex are unchanged. A revive with **no** fainted
member (or a stale / out-of-range target) is refused via `ItemUseFailed` — nothing announced, nothing consumed
(the Gen-1 "won't have any effect" menu rule). Revive is obtainable **only** from Boss rewards and (rarely) the shop.

**Gen 1 source of truth.** Revive → ½ max HP (Gen 1 **truncates** the fraction, like Recover/Soft-Boiled), and a
revived creature comes back **statusless**; only acts on a *fainted* party member; using it in battle takes the
whole turn (already true of every `ItemAction`). HP only for the *restore*, but the status clear matters here
because this engine's fainted members retain a stale `Battle.Status`/`CarriedStatus` (see the gate note).
**Already imported:** `Item.RevivePercent` = 50 / 100 (`ItemMapper.cs`).

**DoR — gen-variable surface & data boundary.** **No generation seam** — the ½/full amount is Gen-invariant
item *data* (`RevivePercent`), and would be a data pin (not an `IBattleRules`/`ITypeChart` rule) if a future
gen ever changed it. **No importer change, no migration** — the data row already exists; this was a pure
engine + web + frontend change. Reward/shop eligibility is run-layer tuning (`RewardCalculator` /
`ShopCalculator`), not data.

**The one architectural wrinkle.** Every existing in-battle item self-targets the *active* creature
(`ItemEffectContext.User`). Revive is the first to target a **benched** member, so the effect context and the
item-use handshake gained a party-target index (distinct from the existing `TargetMoveSlot` for Ether).

**Implementation:**
- **Engine** — `Party? Party` + `int? TargetPartySlot` added to `ItemEffectContext`; `int? TargetPartySlot`
  added to `ItemTurnChoice`; new `ReviveItemEffect : IItemEffect` (Category `Revive`) registered in
  `ItemEffects.All` — `CanApply`: party wired, slot in range, that member is **fainted**, `RevivePercent > 0`;
  `Apply`: sets HP to `Math.Max(1, MaxHP·pct/100)` (integer division = **floor**, matching Gen-1/`HealEffect`),
  **clears the member's status** (`Battle.Status`/`SleepTurns`/`ToxicCounter`/`CarriedStatus`, mirroring
  `Creature.FullHeal`), emits a new **`Revived`** event **and** a `PartyUpdated` snapshot (so the bench HP + clean
  status show, mirroring `RecoveryRunEvent`). Party + slot threaded through `ItemAction` and
  `Battle.BuildPlayerActionAsync` (`_playerParty` was already in scope).
- **Web** — `BattleHub.UseItem` / `GameSessionManager.SetItemChoice` / `SignalRInput` gained `int? targetPartySlot`;
  `ProjectBagView` takes the party so **Revive's `UsableInBattle` is gated on a fainted member existing**
  (not the static category check the other items use); `RewardCalculator.EligibleCategories` adds `Revive`,
  `RollItemOption` **excludes Revive unless tier == BossBattle** (shop keeps the full usable pool, so revives stay
  Boss-reward-only + shop-only), `UsableItems` **holds out Max Revive** (name-matched) from every obtainable
  channel, and `CategoryWeight(Revive, Boss)` is a deliberately-modest **1.0** (not up-weighted) so a Boss revive
  is occasional, not near-guaranteed. This is also the item that finally makes the *Item System* archive entry's
  "Ball & Revive hidden" note stale — only Ball stays unconditionally hidden now.
- **Frontend** — `bag.ts`: `Revive` group in `CATEGORY_LABELS` + a `needsPartyTarget` helper; the bag menu shows a
  **fainted-member picker** before firing `onUse`; `useBattleHub.useItem` + `BattleScreen` thread `targetPartySlot`;
  the **`Revived`** event was added to the TS event types + log/render (the manual TS leg of the wire-field gap).

**Coverage:** Revive on a fainted member → HP = ⌊MaxHP/2⌋ (floor, discriminated by an odd-HP case: 41 → 20) and
`IsAlive()`; Max Revive → full HP; never below 1 on a tiny pool; a member revived while badly-poisoned comes back
`Status.None` with `CarriedStatus` cleared and the snapshot showing it clean (`ItemEffectTests`). `CanApply`
**false** on a non-fainted member, out-of-range slot, or null party — no announce/consume (`ItemEffectTests` +
`ItemActionBattleTests` `ItemUseFailed`). Revived member **stays benched** — active creature & `LeadIndex`
unchanged; one revive consumed; `Revived` + `PartyUpdated` emitted (`ItemActionBattleTests`). `ProjectBagView`:
Revive `usableInBattle` true **only** when a bench member is fainted (`BagViewProjectionTests`). Boss nodes can
roll a revive; non-boss reward nodes never do; shop can stock one; **Max Revive is held out** of the usable pool
(`RewardCalculatorTests` / `ShopCalculatorTests`). `bag.ts`: the REVIVE group surfaces; `needsPartyTarget` true
for Revive (`bag.test.ts`). `Revived` narrates in `timeline.test.ts`.

**Gate adjustments (pre-finish review, 2026-07-19).** Four findings from `requirements-review` + `pr-review`, all
adjudicated by the user: **(1)** HP restore floors the fraction (Gen-1 truncation), not ceil — matched to
`HealEffect`; **(2)** a revived member is cleared of status/carried-status (this engine keeps a stale status on a
fainted member, so a revive-while-poisoned would otherwise come back afflicted — the `pr-review` blocker); **(3)**
Max Revive is a Gen-2 item, so it's scaffolded but held out of obtainable loot until multi-gen; **(4)** the Boss
category-weight for Revive was dropped 4.0 → 1.0 to honour "extremely rare." (Two E2E specs — `reward-drop`,
`shop` — were red at review time but verified **pre-existing on `master`**, unrelated to this feature.)

**Dependencies (all already in place):** `Party`, `Bag`, and the forced-switch send-in path, all from
*Encounter Logic — Phase 4* (its Stages 1a–3 are archived further below under this same name; not to be confused
with *Encounter Logic (roguelite run layer)*, which is Phases 1–3).

---

## Encounter Logic — Phase 4 — Acquisition & the Roster ✅ COMPLETE (2026-07-12 → 2026-07-15; residual/XP-share defect resolved 2026-07-18)

*(Moved here from `TODO.md` 2026-09-27 during a full-file TODO audit — all five stages had shipped and were sitting
in the active list past completion, the only place this full design + build record existed. Phases 1–3 are a
separate, earlier archive entry, *Encounter Logic (roguelite run layer)*, below.)*

Phases 1–3 (biome model + type-filtered pool, `IEnemyArchetype` tiers + depth bands, `RunDirector` event model
+ live biome-graph map + tuned Boss-capped node curve) are done and archived separately, along with the four
follow-on refinements — per-run biome-map randomisation, randomised 4–6 route length, Roar/Whirlwind→`ForceFlee`,
and the opening-route favourable-matchup guarantee. Full per-phase record (design, pins, seam reviews) in this
file → *Encounter Logic (roguelite run layer)*.

**Phase 4 — Acquisition & the Roster** (the `ENCOUNTER_DESIGN.md §4` piece, and the bridge into
`TODO.md`'s *Item Acquisition · Bag Persistence · Catch* cluster). **`/plan` done** (2026-07-12) — the full design
below (session plan mirrored here for durability; the ephemeral copy was `kind-cooking-moler.md`).

**Scope decisions locked with the user (2026-07-12):**
1. **Proper roster** — a real party (up to 6, the Gen 1 ceiling), lead management, party UI. Not a minimal
   collectible, not a single-slot swap.
2. **Draft first, then catch** — ship the cheaper themed-draft channel first, the boss catch second.
3. **Boss catch = a small post-win chance, NOT an in-battle ball throw.** "Beat the boss → small chance at a
   catch event." The boss is defeated first (you keep the win XP/reward), then a small-% offer to add it. This
   makes **both** channels post-battle acquisition offers reusing the reward-modal pattern — the in-battle Poké
   Ball mechanic (`BallItemEffect`, catch-rate-vs-HP formula) is **out of scope** and stays deferred in the Catch
   cluster below.

**Architecture (what we reuse).** Two new run-layer primitives + one reusable offer:
- **A `Party` container** threaded like `Bag`/`Wallet` (single instance: `EncounterFactory` → `RunSetup` →
  `PendingSession` → `ActiveBattle.Party` → `RunState.Party`; GC'd on run end). `RunState.Player` stays "the
  current **lead**" so `Battle` (which only knows one creature) is untouched.
- **Fought-species tracking** — `RunState.FoughtSpeciesInBiome` (HashSet), populated per encounter in
  `BattleRunEvent`, reset per biome in `RunDirector.Apply`.
- **One reusable "acquisition offer"** — a new blocking prompt mirroring the **Reward Choice** wire end-to-end
  (the ~13-leg path: `RunLoop` option records → `BattleEvents` `AcquisitionOffered`/`CreatureAcquired` →
  `IBattleInput.ChooseAcquisitionAsync` → `SignalRInput` TCS + `Cancel()` → `RunDirector` emit/await/deposit →
  `SignalRBattleEventEmitter` projection + `ProjectCreatureOption` → `BattleHub` → `GameSessionManager` route →
  field-level `WebEventContractTests` guard → `timeline.ts`/`battleReducer.ts`/`useBattleHub.ts`/`BattleScreen`
  modal). Both channels emit the *same* offer; only the *source* + how the offered creature is chosen differ.
- **Gen-variable surface (DoR #3): none.** Party size 6, draft cadence, and the *n%* rates are run-layer tuning
  (web-layer policy like `RewardCalculator`), NOT battle seams. Zero importer/DB change; transient (no `save.db`).

**Staged build (each increment independently shippable + greenlit separately):**
- [x] **Stage 1a/1b — roster foundation** ✅ DONE (2026-07-12, commit `4c2b9b2`): the `Party` container
  (`creaturegame/Creatures/Party.cs` — `MaxSize` 6, `Lead`/`Add`/`IsFull`/`Replace`/`SetLead`), `RunState.Party`
  (`Player` = the lead) + per-biome `FoughtSpeciesInBiome` tracking, and whole-party Poké Center recovery.
  `RunDirector` owns the party internally for now (session threading lands with 1c's UI). Backend-only, no
  wire/UI; covered by `PartyTests` + a `RunDirector` fought-accumulate/reset test. **Known deferral to 1c:**
  whole-party heal is state-correct but only the lead's `PlayerRecovered` is emitted — the bench heal surfaces on
  the wire with the `PartyUpdated` snapshot the panel needs (user-approved deferral).
- [x] **Stage 1c — themed draft, end-to-end** ✅ DONE (2026-07-13): a post-win offer in `BattleRunEvent`
  (`OfferDraftAsync`, after `GrantBattleRewardAsync`/evolution/status-capture), gated by cadence (every 3rd win)
  × a 55% web-policy roll × non-empty fought pool (`DraftCalculator.ShouldOffer` — no RNG drawn on a non-cadence
  win). The offered creature is built web-side by the injected `draftSupplier` (`EncounterFactory.BuildDraftSupplier`
  → `BuildCreature` + `PickByBst` over the pool **intersected to `FoughtSpeciesInBiome`** — the fought-only
  guardrail), scaled to lead/depth. Full acquisition-offer wire (`AcquisitionOffered`/`CreatureAcquired`/
  `AcquisitionDeclined`/`PartyUpdated` + `IBattleInput.ChooseAcquisitionAsync` + `SignalRInput` TCS +
  `AcquisitionResolution.OfferAndDepositAsync` + emitter projections & field-level `WebEventContractTests` guards +
  `BattleHub.RespondAcquisition` + `GameController` `GET /party` hydrate + `timeline`/`battleReducer`/`useBattleHub`
  + `BattleScreen` `PartyStrip` + `AcquisitionModal`). Deposit into `Party` (party-full ⇒ swap-out picker; a
  server-side guard refuses swapping the **lead** — that's Stage 1d). The session owns the single `Party`
  (`GameSessionManager` → `ActiveBattle.Party` → `RunState.Party`). **Stage 1a/1b deferral closed:** the
  whole-party Poké Center heal now emits a `PartyUpdated` snapshot so benched members' restored HP reaches the
  panel. Covered by `RunDirectorAcquisitionTests` (accept/decline-no-op/full-swap/lead-guard), `DraftCalculatorTests`
  (cadence/empty-pool/roll boundary), `EncounterFactoryDraftTests` (fought-only build over the live DB),
  `WebEventContractTests` field guards, and Vitest (reducer + timeline).
- [x] **Stage 1d — lead-swap between biomes** ✅ DONE (2026-07-13) *(between-biome only — NOT in-combat)*: a
  `ChooseLeadAsync` prompt at the biome boundary (after the Poké Center, before the next `BiomeChoiceEvent`), gated
  on `Party.Count > 1` via a one-shot `RunState.LeadChoicePending` flag (set on the Poké Center outcome, cleared by
  the `LeadChoiceEvent`) — reassigns `Party.Lead` (⇒ `RunState.Player`) for the next biome. Lead swaps need no
  status reconciliation because this same stage **implemented the multi-creature carry model**: major out-of-battle
  status now lives per-creature on `Creature.CarriedStatus` (replacing the old single-slot `RunState.CarriedStatus`),
  so each benched member keeps its own ailment and the previous lead's status can never leak onto the switch-in
  (`STATE_MODEL.md §2`; captured by `RunDirector`, cleared by `Creature.FullHeal` = the Poké Center). New `LeadChoiceOffered`/`LeadChanged` events + the full
  wire (`IBattleInput.ChooseLeadAsync` + `SignalRInput` TCS + `GameSessionManager.SetLeadChoice` +
  `BattleHub.ChooseLead` + emitter projections & field guards + `timeline`/`battleReducer`/`useBattleHub` +
  `BattleScreen` `LeadChoiceModal`). Touches **nothing** in the battle engine (`Battle` still sees one creature per
  side). Covered by `RunDirectorLeadChoiceTests` (reassigns-active-creature / boundary order / keep-current no-op /
  out-of-range no-op / status-no-leak both surgically and end-to-end through a declined Poké Center / lone-starter
  never-fires), `PartyTests` (`FullHeal` clears the carried ailment), `WebEventContractTests` field guards, and
  Vitest (reducer + timeline). *(Interim faint
  handling through Stages 1–2 stands: the lead fainting still ends the run.)* Switching mid-fight was a
  **separate, larger** feature — **In-Combat Switching**, shipped 2026-07-25 (full record in this file).
- [x] **Stage 2 — boss catch (post-win chance)** ✅ DONE (2026-07-14): after a **Boss** win, a small *n%* roll
  (`BossCatchCalculator.ShouldOffer`, 20%) → the **same** `AcquisitionOffered` with `source: "BossCatch"` and a
  single option = a fresh full-HP copy of the defeated boss's species at the boss's level (built by
  `EncounterFactory.BuildBossCatchSupplier`, with a learnset so it can level up if it later leads) → into the
  `Party`. Backend-only — reuses all of 1c's offer + roster wire end-to-end (`AcquisitionResolution.OfferAndDepositAsync`,
  the `AcquisitionOffered`/`CreatureAcquired`/`AcquisitionDeclined`/`PartyUpdated` events, the SignalR projection +
  field guards, and the `AcquisitionModal`, which already renders the `BossCatch` source as "Catch!"). Threaded like
  the draft supplier (`RunDirector` → `BattleRunEvent` → `GameSessionManager`). **One acquisition offer per win,
  routed by tier:** a Boss win boss-catches, every other win themed-drafts (never both). The win reward/XP is
  already applied, so the catch is pure upside. Covered by `RunDirectorAcquisitionTests` (accept/decline-no-op/
  no-supplier/channel-distinctness), `BossCatchCalculatorTests` (roll boundary), and `EncounterFactoryBossCatchTests`
  (full-HP boss-species copy over the live DB / roll-miss offers nothing).
- [x] **Stage 3 — forced-switch-on-faint** ✅ DONE (2026-07-15) — the battle-seam party upgrade; `Battle` now holds
  the party and, on the active creature's faint with a live bench member, blocks on a forced (non-dismissable)
  switch-in modal → sends the chosen survivor in against the **same** enemy → continues; the run ends only when the
  **whole party** is down. New `SwitchInOffered`/`CreatureSwitchedIn` events + `ChooseSwitchInAsync` input seam
  (default = first live member) + `SignalRInput` TCS + `BattleHub.RespondSwitchIn` + emitter projections & field
  guards + `timeline`/`battleReducer`/`useBattleHub` (`playerNameRef` retarget on switch-in) + `BattleScreen`
  `SwitchInModal` + a `swapPlayerCreature` Phaser command (slide the incoming back-sprite in; new *true* species so
  a later win's `resetPlayerSprite` keeps it). `BattleRunEvent` re-reads `s.Player` post-battle so win/loss and carried
  status act on the **finisher**. No generation seam (gen-invariant); zero importer/DB change.
  **Known defect, resolved 2026-07-18** — evolution used to be gated to the no-switch case (a switched-in
  finisher that levelled up did not evolve), and XP/Stat-Exp went to the finisher alone. Both came from wrong pins
  in this plan, not from the domain. Evolution is fixed (per-member pre-battle-level snapshot); XP/Stat-Exp
  participation is superseded by the **Innate Party XP Share**, a deliberate roguelite deviation from the Gen-1
  participant split. See this file → *Switched-in creature is the active creature* for the closing
  record.

  **Two edges closed during the pre-finish gates (2026-07-15):** (1) **flee + faint on the same turn** — a
  switch-in `continue`s past the end-of-turn flee gate, so a foe already scared off by Roar/Whirlwind would have
  got a free turn against the incoming creature. The flee is now snapshotted *before* the faint branches and the
  switch is gated on it (`!fledThisTurn && await TrySwitchInAsync()`): a fled foe means there's nobody to send
  anyone in against, so the documented "a faint takes precedence (a KO is a real result)" ordering stands and the
  battle ends as a loss (user-decided 2026-07-15). (2) **the CHECK POKEMON panel read the wrong creature** —
  `ActiveBattle.Player` is captured at session claim and never reassigned, so `GET /api/game/{id}/player` showed
  the *fainted* starter's sheet after a switch. Now resolved live through the new pure
  `GameSessionManager.ActiveCreature(party, starter)` (= `party?.Lead ?? starter`, the `GetParty` precedent).
  *(This debt predated Stage 3 — Stage 1d's between-biome swap already staled the read — but Stage 3 opened the
  common mid-battle path into it; one fix closes both.)* The duplicated entry-status rule was also folded into a
  single `Battle.ApplyEntryStatus` used by both the opening lead and the send-in.

  Covered by `BattleForcedSwitchTests`
  (switch/enemy-state-preserved / no-live-bench = loss / legacy single-creature / carried-status-no-leak /
  stale-pick fallback incl. negative + out-of-range / party-wired **double-faint offers no switch** / incoming
  **neither acts nor takes end-of-turn DoT** on its entry turn / **flee + faint** ends without a switch or a free
  turn), `BattleForcedSwitchIdentityTests` (a **Transform**ed creature that faints into a switch is restored *as it
  leaves* — the end-of-battle restore can't reach a benched creature; driven through the real moves DB),
  `RunDirectorForcedSwitchTests` (run continues past a lead faint + `RunState.Player` tracks
  the finisher / whole-party wipe ends the run), `ActiveCreatureResolutionTests` (the panel follows the lead across
  a switch), `WebEventContractTests` field guards, Vitest (reducer +
  timeline), and **E2E `forced-switch.spec.ts`** (seeded run → draft accepted → lead faints → forced modal with the
  fainted member disabled → pick → "Go! X!", nameplate retargets, battle continues — the DoR's opportunistic E2E,
  now actually covered). **The five Stage 1d / acquisition lead-identity tests that encoded the interim "lead faint ends the
  run" model were updated to Stage-3 reality** — four in `RunDirectorLeadChoiceTests` (assert the lead-choice
  effect via the battler record, not the post-wipe final lead) and `RunDirectorAcquisitionTests`'
  `ThemedDraft_PartyFull_AcceptTargetingTheLead_IsRefusedAsADecline` (asserts the refused swap on the lead's
  **slot**, `Members[0]`, instead of `Party.Lead` — `SetLead` moves `LeadIndex` only and never reorders, so the
  slot assertion is exact where `Party.Lead` is now churned by the post-decline wipe's forced switches).
  *(`CreatureSwitchedIn` also carries a `Level` beyond the signature sketched below — `TurnStarted` carries no
  level and the nameplate needs it.)* This is the Battle-holds-party groundwork the voluntary **In-Combat
  Switching** feature (shipped 2026-07-25, archived in this file) built its SWITCH turn-action on.

  **`/plan` (2026-07-14) — the design as built:** When the **active** creature faints and a bench member is
  still alive, the run **does not end**: the player **picks** the replacement from a forced (non-dismissable)
  party-select modal — "player chooses", the faithful Gen-1 forced-switch, decided with the user 2026-07-14 —
  and it comes in against the **same (damaged) enemy**; the run ends only when the **whole party** is down.
  **This is where `Battle` first learns about the party** — it must hold the benched creatures so it can bring in
  the next one against the live enemy — and it is deliberately the *choose*-a-replacement path (not auto-send-next)
  so it front-loads the in-battle party-select modal + `ChooseSwitchInAsync` prompt that **In-Combat Switching**
  reused (forced + voluntary **share the send-in path**, exactly as designed — that later feature shrank to "add
  the voluntary SWITCH turn-action trigger", with enemy-AI switching still a later refinement). `save.db` stays
  beyond Phase 4.

  **Design (the finalized `/plan`):**
  - **Engine — `Battle` holds the party (the central change).** Add an optional `Party? playerParty = null`
    constructor param (threaded from `BattleRunEvent` as `s.Party`); null keeps the legacy **single-creature**
    behaviour (break-on-faint) so every direct `Battle` caller (tests, the endless chain) is untouched. Make the
    today-readonly `PlayerCreature` a **reassignable** field (the active creature). The faint check already sits at
    the clean **end-of-turn** boundary (after both actions + end-of-turn DoT/Leech), and the **enemy-faint (win)
    check runs first** — so the forced switch only fires on the *isolated* new path **enemy alive + active creature
    fainted**, leaving the existing **double-faint** semantics (`BattleRunnerTests.Runner_DoubleFaint…`) intact. On
    that path: emit `CreatureFainted` (already fires) → if `playerParty` has a live bench member, restore the
    outgoing creature's Mimic/Transform identity *before it leaves* (so a transformed-then-fainted mon can't leak
    its copied moveset/stats), block on `ChooseSwitchInAsync`, then bring the chosen member in and **`continue`** the
    turn loop against the same enemy; if **no** bench member is alive, `break` as today (loss). Bringing a member in
    = `party.SetLead(index)` (⇒ `RunState.Player` and the director's `while (Player.IsAlive())` guard "just work") +
    reassign `PlayerCreature` + `ResetBattleState()` + re-apply **that creature's own** `CarriedStatus` (same as the
    battle-start entry-status path) + emit `CreatureSwitchedIn` + `PartyUpdated`. The replacement **does not act**
    the turn it enters (the turn already resolved) and takes **no** end-of-turn DoT that turn (freshly reset) —
    canonical Gen 1; it acts normally next turn, and the enemy gets **no** free hit.
  - **Input seam.** `IBattleInput.ChooseSwitchInAsync(SwitchInContext) -> int` (index of the chosen live member),
    with a **default** that returns the first live bench member — so `AutoSelectInput` / the AI / headless tests
    never stall and never send in a fainted mon. `SignalRInput` adds the TCS handshake (mirrors the mid-battle
    `ChooseMoveToForgetAsync` and the `ChooseAcquisitionAsync`/`ChooseLeadAsync` prompts); `Cancel()` faults it on
    disconnect. Called from **inside `Battle`** via `_playerInput` (like the move/forget prompts), not from a
    `RunEvent`. A stale / out-of-range / **dead** pick falls back to the first live member (never strands, never
    sends in a fainted creature).
  - **Events + wire (mind the recurring web-event field-projection gap — memory `web_event_field_projection_gap`):**
    two new `BattleEvent`s, each needing its `SignalRBattleEventEmitter` projection **and** a field-level
    `WebEventContractTests` guard — `SwitchInOffered(PartyMemberView[] party, string faintedName)` (client raises the
    forced modal; reuses `PartyProjection.Snapshot`) and `CreatureSwitchedIn(name, speciesId, hp, maxHp, status)`
    (client swaps the canvas sprite + nameplate and logs "… was sent out!"), plus the existing `PartyUpdated`
    snapshot. Named `CreatureSwitchedIn` to align with In-Combat Switching's planned `CreatureSwitchedOut/In` (the
    "switched out" here **is** the `CreatureFainted` already emitted). `BattleHub.RespondSwitchIn(int)` completes the
    TCS; `GameSessionManager` routes it.
  - **Frontend — provisional-pending-refinement (flag per `feedback_plan_durability_and_iteration`).** Shape:
    `timeline.ts` arms `SwitchInOffered` (raise modal / pause) + `CreatureSwitchedIn` (sprite-swap + nameplate + log)
    + `PartyUpdated`; `battleReducer.ts` sets a forced-switch-pending flag (gates the modal) and updates the active
    nameplate/sprite/HP on switch-in; `useBattleHub.ts` adds `respondSwitchIn(index)`; a new **forced (non-closable)**
    `SwitchInModal` reuses `PartyStrip`/`AcquisitionModal` styling — live members selectable, fainted greyed &
    disabled; a Phaser `BridgeCommand` swaps the player sprite to the new species. Finalize the exact component split
    at implementation time.
  - **DoR #3 — gen-variable surface: none.** Forced faint-switch (a fainted mon is replaced; no free hit; no
    turn-order or partial-trap question — those are *voluntary*-switch concerns owned by In-Combat Switching) is
    generation-invariant. No `IBattleRules`/`ITypeChart`/`IStatCalculator` touched; satisfies `GENERATION_SEAMS.md`
    §5.0 trivially. Zero importer/DB change; transient (no `save.db`).
  - **DoR #4 — Gen-1 truth:** incoming resets **volatiles** (stat stages, confusion, Leech Seed, binding, …) but
    **keeps its own major status** (the carry model — status can't leak from the outgoing mon); replacement doesn't
    act the entry turn; enemy keeps its HP/status/stages. Post-win, `BattleRunEvent` captures `CarriedStatus` on
    `s.Player` = the (possibly switched-in) finisher; the fainted member stays at 0 HP on the bench until the next
    Poké Center `FullHeal` — and the Poké Center caps each biome **before** the between-biome lead choice, so a
    fainted member is always healed before it can be re-picked as lead.
    > ⚠️ **This bullet previously pinned two rules that were WRONG** — "XP/Stat-Exp to the finisher only … the DoR's
    > *only the lead earns XP (no Exp Share)* … **not** a deviation" and an evolution gate. Both were invented by
    > this plan, not by the domain, and `requirements-review` returned MET because the code faithfully matched the
    > plan. Corrected by the user 2026-07-15, **resolved 2026-07-18** → see this file →
    > *Switched-in creature is the active creature*. Kept visible rather than silently deleted: the wrong pin is why the defect shipped.
  - **DoR #6 — tests must assert:** (Battle) active faints + live bench ⇒ chosen member sent in, **enemy state
    preserved**, loop continues; active faints + no live bench ⇒ loss; incoming `BattleState` reset + its own
    `CarriedStatus` applied (**status-no-leak** from the outgoing); incoming **doesn't act** its entry turn;
    stale/out-of-range/**dead** pick ⇒ fallback to first live member; **double-faint semantics unchanged**. (Director)
    run continues past a lead faint with a live bench and **ends when the whole party is down**; `RunState.Player`
    tracks the switched-in creature; post-win capture on the finisher. (Wire) `SwitchInOffered` + `CreatureSwitchedIn`
    **field-level** projection guards. (Vitest) reducer switch-in transition + timeline arms. (E2E, opportunistic) a
    seeded run: lead faints → forced modal → pick a replacement → battle continues.
  - **DoR #7 — dependencies:** builds directly on Stages 1a–2 (the `Party`, carry model, and acquisition/lead wire
    precedents). Independent of `save.db`. It is the prerequisite for **In-Combat Switching** (Battle-holds-party).

**DoR #6 — quirks the tests must assert:** fought-only guardrail (never offer an un-fought species; set resets on
biome change ✅ done); cadence + **never a dead offer** when the fought pool is empty; roster cap 6 + party-full
swap; **decline is a sequencing no-op** (`RunDirector` order test); each new offer event **field-level** projects
over SignalR (field guard, not just the type-map test); lead-swap reassigns the active creature deterministically;
whole-party heal ✅ done; (Stage 2) boss-catch chance + boss into party while win XP/reward still applied;
(Stage 3) forced-switch when the bench has a live creature vs. run-loss when it doesn't. **DoR #4 (Gen-1 truth):**
party size 6; **every creature that levelled shares in evolution, and the whole living party shares in XP/Stat-Exp**
(see this file → *Switched-in creature is the active creature*, resolved 2026-07-18 — the earlier "only the lead earns XP (no
Exp Share)" pin was wrong; the eventual fix was the **Innate Party XP Share**, a deliberate deviation from the
literal Gen-1 participant split, not a re-implementation of it); major status persists on benched creatures per
the carry model.

**Out of scope this phase:** the in-battle Poké Ball throw + `BallItemEffect` + catch-rate-vs-HP formula (stays
in `TODO.md`'s Catch cluster); `save.db`/`PlayerDbContext` persistence + cross-run meta-progression; the **Exp.
Share / Exp. All item** (a held item that pays a *non-participant* — distinct from the innate party-wide XP share
that shipped 2026-07-18, see this file → *Switched-in creature is the active creature*). *(Revive, which needed a
fainted-but-revivable party member, shipped 2026-07-19 on top of this stage's `Party` — see this file →
Revive Items. Voluntary in-battle switching — its own planned core feature at the time — shipped 2026-07-25 as
**In-Combat Switching**; see this file.)*

---

## Move-menu strength cue + attack-grid polish (QoL) ✅ DONE (2026-07-18)

`/plan`ned and built the same session. The FIGHT menu already surfaced name, PP, type badge, the STAB tag and
the ×N effectiveness pill, but nothing conveyed a move's raw power — a neutral Tackle (35) and a neutral Hyper
Beam (150) read alike. Added a base-power strength cue for damaging moves.

**Design decisions locked with the user:**
- **"Strength" = the move's raw Gen-1 base power** (static per move), *not* an effective/combined value — the
  player keeps reading it against the separate STAB / ×N pills. This keeps every gen-variable rule (the STAB
  ×1.5, the type chart) engine-side; the client receives only a plain data number.
- **Rendered as a numeric pill, colour-graded on a cool→hot ramp** (steel `#45525f` → blue `#3f6fa3` → indigo
  `#7159c9` → magenta-ember `#b5468a`) so relative strength reads at a glance. The ramp is deliberately
  **distinct from the effectiveness pill's green/amber/red** (which means *matchup*, not power) so the two are
  never confused. Placed beside the type badge (lower-left), leaving the ×N pill's bottom-right corner clear.
  Tier thresholds (`<50` weak / `50–79` mid / `80–109` strong / `≥110` max) are a pure display bucketing.

**Gen-variability:** base power is **move data**, already imported Gen-1-pinned via the importer's `past_values`
(the same `Base.BaseDamage` the STAB condition reads). No runtime gen-constant, no importer change — pure
projection. Fixed-damage / status moves (`BaseDamage 0`) carry no power → no pill (the "no cue" rule STAB/Eff
already follow).

**Data vs runtime:** runtime only. One new `Power` field on `MoveInfo` (`Combat/BattleEvents.cs`), populated in
`Combat/Battle.cs`'s projection from `m.Base.BaseDamage`; **added to the hand-written SignalR projection**
(`SignalRBattleEventEmitter.cs` — this was the one real trap: the projection silently dropped the new field, so
a dev screenshot showed no pill until it was added, and a `WebEventContractTests` field guard now pins it);
mirrored as `power?: number` on the TS `MoveInfo`; the tier→class helper lives in a pure module
`src/battle/movePower.ts` (imported by `BattleScreen.tsx`'s `MoveMenu`).

**Coverage:** `MoveInfoPowerTests` (engine projects base power; fixed-damage move → 0), the
`TurnStarted_MoveProjection_CarriesPower` wire guard, `movePower.test.ts` (tier bucketing + no-pill boundaries),
and a `battle-ui-cues.spec.ts` E2E asserting the pill + tier colour reach the DOM in a real battle (mirroring the
STAB render-layer spec — this cue hit the same projection failure mode). Grid-polish beyond the pill was deferred
as non-blocking visual iteration.

## Innate Party XP Share (roguelite Exp-All) ✅ DONE (2026-07-18)

`/plan`ned and built the same session. Closes the XP/Stat-Exp half of the **Switched-in creature is the active
creature** open defect (then in TODO.md, user ruling 2026-07-15) — not by implementing Gen 1's participant split, but by
**superseding** it with a deliberate roguelite deviation the user chose instead. The defect's *evolution* half was
fixed in the same change. See *Switched-in creature is the active creature* (top of this file) for the closing
record of that defect (evolution fixed, XP/Stat-Exp superseded, residual sweep closed 2026-09-20).

**Design decisions locked with the user this session:**
- **Split model = active full + bench a share**, not Gen 1's participant split and not a flat full-to-all. New
  `RunRules.BenchXpShare` dial (`Combat/RunRules.cs`, default `0.0` = off/no-op — every existing direct-`Battle`
  caller and test is unaffected), set to `0.5` in the live web run (`creaturegame.Web/Battle/GameSessionManager.cs`
  `RunTuning`). A roguelite game-balance knob, deliberately kept **outside** the Gen-1 `IBattleRules` seam — same
  separation as the existing XP-curve deviation (`RunRules.XpMultiplierForLevel`, see `GENERATION_SEAMS.md`).
- **Fainted bench members are excluded** — a fainted participant earns nothing, per Gen 1. "Fainted" here is
  the *current HP state* (`Creature.IsAlive()`, HP > 0, checked at award time), so it also excludes a member left
  KO'd from an earlier unhealed encounter — a deliberate roguelite reading, slightly broader than the literal
  Gen-1 "fainted during this battle" rule (an unhealed KO'd creature shouldn't passively train).
- **Shares both XP and Stat-Exp.** Stat-Exp is granted **in full** to each living member (it's a coarse, capped
  accumulator already — not fractionalised like XP).
- **Bench level-ups are surfaced and overtly attributed**, not silent — each carries the levelling creature's name
  so the player can tell which party member just grew, distinct from the active creature's own level-up panel.

**Implementation:**
- `Combat/Battle.cs` — after the active creature is paid in full (unchanged Gen-1 award), a new private
  `ShareExperienceWithBenchAsync(int activeAward)` pays every **living** bench member
  `floor(activeAward × RunRules.BenchXpShare)` XP + full Stat-Exp, then runs the same level-up + move-learn loop
  used for the active creature. No-op without a party or with a zero share (a direct single-creature `Battle` is
  provably unaffected).

> **Bugfix, 2026-08-23.** Bench XP was silent (no per-member `ExperienceGained`) until it produced a level-up, so
> the battle text log never named a bench member that gained XP without also levelling. `Battle.cs` now emits an
> attributed `ExperienceGained(member.Name, share, OnBench: true)` per living bench member — same convention as a
> switched-out participant's award (`Participation XP`, above) — so the log names every party member that gained
> XP this battle, not just whoever was on the field. `LeveledUp` attribution is unchanged. See
> `tests/creaturegame.Tests/Unit/PartyExpShareTests.cs` and `docs/STATE_MODEL.md`.
- `Combat/BattleEvents.cs` — `LeveledUp` gained a trailing `bool OnBench = false` so the client can tell a
  bench level-up from the active creature's and render an attributed panel without moving the active nameplate.
- `Combat/RunEvents/BattleRunEvent.cs` — replaced the single starting-lead `levelBefore` local +
  `ReferenceEquals(active, player)` evolution gate with a **per-party pre-battle level snapshot** (`preLevel`, one
  entry per party member) and a new `EvolutionOrder` helper; **every** creature that levelled this battle now
  evolves — active, forced switch-in, or bench — active-first then roster order. This is the fix for the
  defect's evolution half; the old `ReferenceEquals` gate and its "KNOWN DEFECT" comment are gone from this file.
- Web: `SignalRBattleEventEmitter` projects `OnBench`; `timeline.ts` branches on it (bench = attributed panel +
  fanfare, no `LEVELED_UP`/`XP_SET` on the active nameplate); `battleReducer`'s `LevelUpPanel` + `SHOW_LEVEL_UP`
  now carry `creatureName`; `BattleScreen.tsx` renders the name in the panel; `MoveReplacementModal`'s confirm
  step is named too.
- Tests: new `tests/creaturegame.Tests/Unit/PartyExpShareTests.cs` (3 cases — bench share applied/withheld from
  the fainted/zero-share no-op), a new bench-path case in `timeline.test.ts`, an updated `battleReducer.test.ts`
  fixture, and `BattleScenario` gained `.Party()`/`.RunRules()` builders for this and future party-aware tests.
  Full suite green.

**Why this is not "the Gen 1 participant split" (do not re-file as such):** Gen 1 divides one Exp/Stat-Exp pool
among only the creatures that were **sent out** and did not faint. The Innate Party XP Share instead pays the
active creature in full and *additionally* shares a fraction with the **whole living bench**, whether or not it
was ever sent out — a wider, more generous, always-on Exp-All-style grant. **At the time this shipped (2026-07-18)**
there was no observable conflict with the "switched-in creature is the active creature" requirement: voluntary
switching wasn't implemented yet, and a forced switch always leaves the outgoing lead fainted (excluded from any
share anyway), so the only "switched-in" case that existed — the finisher — was simply the active creature, paid
in full, exactly as before this change. **The divergence became real once voluntary switching shipped, by
design:** since **In-Combat Switching** (`TODO_ARCHIVE.md`, above), a creature that fought part of a battle and
was then swapped out *while still alive* earns only the flat `BenchXpShare` — the same as a bench member that
never entered — rather than a participation-weighted share. **In-Combat Switching** treated that as a decision
already made here, not a bug to re-open.

> **Superseded 2026-07-26, resolved 2026-07-27.** With voluntary switching actually shipped, the user reversed it:
> *"a pokemon that was actively involved in a battle should receive equal xp to any other active pokemon."* The
> behaviour above was a known defect, not an intended divergence, until the Gen 1 participation split fixed it —
> the award now divides evenly among live participants, and `BenchXpShare` pays only never-deployed members. See
> **Participation XP — a creature that fought earns a full share**, above.

**Docs:** the deviation is written into `docs/GENERATION_SEAMS.md` (alongside the XP-curve deviation) and the
party-wide XP/evolution invariant into `docs/STATE_MODEL.md` (the party-wide end-of-battle effects section) as a
documented fact for future `requirements-review` runs to cite, per the *plan-asserted domain facts are claims* lesson.

---

## Difficulty easing — weak wild encounters + Quick Heal reward ✅ DONE (2026-07-12)

Playtest feedback: the run was overall too hard, and the player wanted an on-the-spot heal among the reward
options. Two run-layer tuning changes (no battle-seam touch; no importer/DB change):

- **Weak wild encounters.** A plain `Normal`-tier wild encounter now rolls the existing **Weak** archetype vs
  **Medium** ~50/50 on the run RNG (`EnemyArchetypes.For(tier, rng)`, wired at `GameSessionManager`). The two
  are **undifferentiated to the player** — the node kind / tier / encounter-map reveal / banner are unchanged;
  only the built enemy's levers differ. *Acceptance:* wild fights vary in strength while presenting identically.
- **Quick Heal reward (smart-random).** A new `HealRewardOption` appears among the pick-one-of-N reward options,
  offered only when the creature has something to restore (hurt / statused / low PP) — **never a dead option** —
  at a base chance lifted by how badly it's needed. When picked it restores only the
  applicable components — a random slice of missing HP (≤ missing), cure status, top non-full PP (Elixir-style)
  — reusing the gen-invariant heal primitives + events (`Healed` / `StatusCleared` / `PpRestored`). Policy in
  the web `RewardCalculator.TryRollHeal`; application in core `RewardResolution.ApplyHeal`. **Boss nodes are
  exempt** (their reward stays elevated, and the post-Boss Poké Center heals anyway). *Acceptance:* a Quick Heal
  option shows up in reward choices when useful (never on Boss nodes) and, when picked, heals the applicable
  HP/status/PP.

**Deferred (considered, not done this pass):** lowering Elite frequency and the foe level-scaling ceiling —
revisit after re-playtest if the weak-encounter mix alone doesn't ease it enough.

---

## Encounter Map — Slay-the-Spire-style route overlay ✅ FEATURE COMPLETE (2026-07-11)

The run made **visible**: a full-screen dark-fantasy overworld overlay showing the region as a node map — biomes
as waypoints wired by their `Neighbours`, the charted route traced through them, and the current biome's node
ladder revealed inline (wild / elite / treasure / shop / mystery … → **Boss** apex, capped by a synthesized Poké
Center rest). Overwhelmingly a **presentation** feature over existing run state plus a few additive events; no
importer change, no DB migration, no battle seam. `/plan` design pass 2026-07-10; sub-decisions ratified with the
user the same day. **All five build phases shipped:**

1. ✅ **Reveal plumbing (backend), 2026-07-10** — `RegionMapRevealed` (playable subset + neighbour edges,
   filtered to the sent subset), `BiomeNodePlanRevealed` (the seeded `BiomeNodePlan`, emitted in `RunDirector.Apply`
   on the biome-choice outcome), and `RunNodeEntered` for **every** node incl. wild (one uniform pin-advance
   signal), each with its `SignalRBattleEventEmitter` projection + field-level `WebEventContractTests` guard. A
   `RunDirector` test proved the reveal is a **sequencing no-op** (the emitted event order is unchanged).
2. ✅ **Current-biome ladder overlay (frontend), 2026-07-10** — the reveal events accumulate into reducer state
   (region graph → route trace → node ladder → pin index); an `EncounterLadder` draws the vertical ladder (icons
   per kind, done/current/upcoming state, "you are here" pin), synthesizing the terminal Poké Center rest
   client-side (it's implied by the model, not a plan node). Live-verified end-to-end.
3. ✅ **Region graph + map-based route choice, 2026-07-10** — authored 2-D coords on the 18 Kanto biomes; the
   between-biome route pick now happens **on the map** (click a highlighted neighbour waypoint → existing
   `chooseBiome`), folding in and replacing the old `BiomeChoiceModal`.
4. ✅ **Polish, 2026-07-10** — a11y (the route choice focuses the first offered waypoint on open; ARIA labels),
   auto-peek + fade at each transition, the Map toggle to reopen, theme-aware type-coloured waypoints.
5. ✅ **Visual overhaul (full-screen dark-fantasy overworld), 2026-07-11** — the corner map was too small; the
   pinned view became a full-screen overworld with a painterly territory layer (type-colour glow + motif),
   neighbour edges as gradient-blended drawn paths, and the ladder as a side panel.

**Feature complete** — ships all four phases + the visual overhaul. Any future work is net-new (e.g. the map as
the persistent run screen). **Follow-up (deferred, user-approved 2026-07-11 — "procedural now, real art later"):**
replace the procedural type-colour+motif territories with **real painted per-biome scenery** (forest/cave/shore
illustrations); needs an image-asset pipeline (store under `ClientApp/public`), with the current `.region-territory`
layer as the drop-in seam. Low priority — the procedural look reads clearly on its own.

---

## Browser-Based UI Testing — seed plumbing, spec-rot recovery & the flakiness pass ✅ DONE (2026-07-05 → 2026-07-08)

The four completed items from *Browser-Based UI Testing (Playwright)* in `TODO.md`, moved here 2026-07-26 so
that section holds active work only. The **seed-≠-determinism** lesson these taught is standing guidance, not a
finished task, so it stays in `TODO.md` (and in `e2e/README.md`) rather than being archived with them.

**Seed plumbing (2026-07-05).** `StarterSelection` forwards an optional `?seed=<int>` URL param into the
`/start` request (the backend already accepted `Seed`), so an E2E can pin a repeatable run. `?e2e=1` still sets
test mode. react-router drops the query on nav from the title, so seeded specs land directly on
`/select?seed=…`. This is the plumbing every later deep-state spec is built on.

**Run Economy reward-modal E2E (2026-07-05).** Closed the known live-verification gap — the reward modal and
the gold credit observed in a browser, not just at the unit/integration layer. Since superseded by the
pick-1-of-3 `RewardChoiceModal` rework; the current spec is `reward-drop.spec.ts` and the accurate record lives
in this file under **"Reward Choice — pick-1-of-3 rarity rewards"** (E2E note).

**E2E harness recovered from spec-rot (2026-07-05).** The suite was fully red for two unrelated reasons:
**biome mode (Phase 3b-2)** added an opening route-choice modal that blocked before every battle (the
`startBattle` helper didn't answer it), and the **Run Economy** starting bag stopped seeding `BattleStatBoost`
items. Fixed `startBattle` to pick the opening biome (`chooseBiomeIfPresent`); fixed `battle.spec` (the first
log line is now the biome banner, not the VS line); removed the two `item-use` specs, since X ATTACK /
GUARD SPEC are no longer battle-1 obtainable — the item-effect logic stays covered by `ItemEffectTests` and bag
grouping by `bag.test.ts`.

**Stabilise inter-test E2E flakiness — a seed-determinism pass (2026-07-08).** `startBattle` gained an optional
`seed` param: when given it lands directly on `/select?e2e=1&seed=…` (the `reward-drop.spec` pattern; the level
slider lives on that screen, so a custom `level` still works), pinning the whole run — enemy, DVs, moves, biome
offer, every battle roll, AI choice. Converted the flaky coin-flip specs to seed 1: `battle-ui-cues` +
`stat-stage` (seeded `startBattle`), `status` (same treatment), and `level-up` (both tests: the `reachLog`
restart loop replaced with seeded `startBattle` + a new `playToLevelUp` helper that stops at the level-up line
*without* dismissing the reward modal the test asserts). `reachLog` stayed for `battle`/`endless-chain`/
`learnset` — not flaky, and their retry keeps them reliable. **Verified:** full `npm run test:e2e` green across
3 consecutive runs, `retries: 0`, with the converted specs running in seconds instead of coin-flip minutes.

---

## Reward Choice — pick-1-of-3 rarity rewards ✅ DONE (2026-07-07)

Commit `1a9f6eb`. Turns every rolled reward from a silent random grant into a **player choice of three** — two
rarity-rolled items **or** a fatter ₽ bag — replacing the old inverse-cost auto-drop (which over-favoured the
flat 200g status cures: ~56% of item drops were single-status cures). Rarer = more expensive, plus agency and
an escape hatch (take gold when neither item fits). All gates green; verified live in a browser.

**Two decisions locked with the user (2026-07-06):** (1) **every** rolled reward presents the modal — wild wins
too, still gated by the ~85% `BattleDropChance` so a no-roll win stays instant; (2) **4-tier rarity**
(`Common / Uncommon / Rare / Epic`), roll table biased upward by node `EncounterTier` (Elite/Boss) and run depth,
so deep Boss nodes can offer two Rares.

**Rarity model (web-layer `RewardCalculator`, provisional).** Roll a rarity per option, then pick an item
uniformly within that rarity's cost band over the real catalog: Common ≤400 · Uncommon 401–1200 · Rare
1201–2500 · Epic >2500. Placeholder weights (sum 100), lifted by tier + per-depth nudge: Wild `C60/U30/R9/E1` →
Elite `C45/U35/R17/E3` → Boss `C30/U35/R25/E10`. The three options: Item A (rarity-rolled), Item B (distinct
from A, cross-band fallback if the pool can't yield a second), ₽ Bag (`×2 × rarityFactor`, scaled to the better
item so passing up a Rare pays more). **Boss nodes** are the premium node — skewed hardest to Rare/Epic, a
second category-bias lever up-weighting `Healing`/`PpRestore` (and `Revive` once functional) over
`BattleStatBoost`, a guaranteed drop (ignore the whiff), and a fatter bag.

**Architecture (mirrors the biome route-choice pattern end-to-end).** Core (`creaturegame.Combat`): gen-agnostic
`RewardChoice`/`RewardOption`/`RewardRarity` vocabulary + `RewardChoiceOffered` event (the pick is announced by
reusing `RewardGranted`, like `BiomeEntered`); `IBattleInput.ChooseRewardAsync(RewardChoiceContext)` → picked
index (default 0, so headless/AI/test runs auto-pilot); the shared `RewardResolution` offer→pick→apply→
`RewardGranted` helper; supplier return type `RunReward` → `RewardChoice`; `GrantBattleReward` becomes a
**blocking** choice event. Wire: `SignalRInput.ChooseRewardAsync` TCS handshake + `BattleHub.ChooseReward` +
`RewardChoiceOffered` emitter projection + field-level guard (the recurring *web event field-projection gap*).
Frontend: 3-card rarity-coloured `RewardChoiceModal` (reuses the biome-modal shell), `SHOW/HIDE_REWARD_CHOICE`
reducer actions + `rewardChoice` state, `timeline.ts` arm, `chooseReward(index)` in `useBattleHub` (removed the
dead auto-ack). E2E: reworked `reward-drop.spec.ts` to drive the choice modal; a shared
`dismissRewardChoiceIfPresent` helper wired into the play loop + `startBattle` (also fixed 3 specs the new
blocking modal had broken).

**⚠ Provisional tuning knobs (retune by playtest, none blocks the feature):** `BattleDropChance` (0.85 — may
want lowering now a wild win can pop a modal); the rarity weight tables + depth-lift; the Boss category-bias
weights; the gold-bag `×2 × rarityFactor` formula; whether Treasure keeps a multi-item feel. **Revive** stays
out of the live pool (dead loot: `ItemEffects.For(Revive)` → null) but its Boss category-bias arm is written and
dormant — auto-joins the moment the Catch/party layer makes Revive usable.

---

## Reward Visibility & XP Pacing ✅ DONE (2026-07-05)

Commit `5e1f770`. Compelling-rewards pass — boost reward *amount* and *visibility*.

- **XP boost (soft level-aware curve).** New **`RunRules`** — a roguelite "game-balance dials" bag kept
  **separate from the Gen-1 `IBattleRules` seam** (which stays untouched) — carries a level-aware XP curve,
  threaded `GameSessionManager → RunDirector → BattleRunEvent → Battle` and applied to the pure Gen-1 award
  (`floor(baseExp × level / 7)`) at faint time. `RunRules.Default` is a 1.0 no-op (all existing callers/tests =
  pure Gen 1); the web run passes a linear ramp `XpMultiplierEarly = 1.5` (L1) → `XpMultiplierLate = 4.5`
  (L100), ~3× around the default L50. Design target: a biome (~4–6 encounters) ≈ **0.8–1.5 levels** across the
  5–100 range — a playtest goal, not a tested invariant (`RunRulesTests` pins curve *shape*, not pacing). The two
  anchors are the tuning dials (slider-ready), provisional. Elite/Boss get the **Gen-1 trainer ×1.5** XP bonus
  (applied in the seam via `CalculateXpAwarded(…, trainerOwned)`, wired by tier in `BattleRunEvent`), stacking on
  the curve so a typical biome trends to the upper end of the band — intended.
- **Drop hover.** Battle-win drops now raise a transient floating loot toast (gold + items) over the field for
  ~2.8 s (`DROP_TOAST_MS`) — inline, non-blocking, `pointer-events: none`, auto-dismissed — in addition to the
  gold-HUD bump + battle-chat line. Reuses existing `RewardGranted` fields (no new wire projection).
  Treasure/Mystery keep their blocking modal.

---

## Run Economy — gold, item rewards, transient bag & Treasure/Mystery nodes ✅ DONE (2026-07-02)

The economy slice of the deferred *Item Acquisition* cluster, `/plan`ned with the user (approved 2026-07-01) and
built in three phases. **Beating a Pokémon can drop gold and/or items; Treasure/Mystery nodes give real rewards;
the run bag is now earned** (a curated modest start that grows through play). Commits `ea41531` (A/B) + `7d9afc5`
(C). **Follow-up — the Shop node (spend-gold purchase modal) — shipped 2026-07-09; see the end of this section.** 1267 tests green.

**Locked design.** Rewards are *generous but skewed* (a low amount almost always, a high amount rare); gold sized
like Gen 1 trainer prize money (`base × foe level × tier`). Gold + bag are **per-run transient** (lost on death,
no `save.db`) — gold modeled like the transient `Bag`, external to `RunState` so `chooseNextEvent` never reads it.
**Guardrail:** reward *policy* (drop rates / gold curve / item eligibility) is run-layer roguelite tuning (same
class as `EncounterFactory.ScaleWildLevel`, **not** a battle seam). The core stays generation/data-agnostic — it
defines reward *types + state + event + where they apply* and consumes an **injected reward supplier** (same
pattern as `enemySupplier`/`checkEvolution`); the concrete policy + item-catalog lookup live in the web layer.

**Phase A — Core (`creaturegame`), generation-agnostic.** `Items/Wallet.cs` (transient per-run gold, mirrors
`Bag`). `Combat/RunLoop.cs` reward vocabulary — `RewardedItem`, `RunReward` (+ `Empty`), `RewardContext(RunNodeKind
Source, EnemyLevel, Depth)`. `Combat/BattleEvents.cs` `RewardGranted(Source, Gold, GoldTotal, ItemNames)`.
`Combat/IBattleInput.cs` default-non-blocking `AcknowledgeRewardAsync` + `RewardAckContext`. `Combat/RunDirector.cs`
ctor gains `Wallet?` + `rewardSupplier` (default = a **no-RNG-draw** `RunReward.Empty` lambda, so seeded runs
without a supplier are byte-identical); `BattleRunEvent.GrantBattleReward` (inline/non-blocking after
`BattlesWon++`, only on a genuine win); new `RewardRunEvent` replaces the stub for Treasure/Mystery (roll → apply
to wallet+bag → emit → **await ack**). Shop keeps its `InteractionStubEvent`.

**Phase B — Web (`creaturegame.Web`), the policy layer.** `Battle/RewardCalculator.cs` (`internal static`, unit-
tested like `ScaleWildLevel`): gold `base × level × skew` (min-of-two-uniforms, low-biased); items weighted by
inverse `Item.Cost`; eligibility = `Healing`/`StatusCure`/`PpRestore`/`BattleStatBoost` (mirrors `ItemEffects`,
excludes Ball/Revive dead-loot); `RollTreasureReward` (guaranteed gold + ≥1 item) / `RollMysteryReward`
(wildcard). `EncounterFactory.BuildStartingBag` (curated cheapest-of-category loadout) replaces the old
`TestBagQuantityEach = 20` seed; `BuildRewardSupplier` closes over the usable subset. `Wallet` threaded through
`RunSetup`/`GameSessionManager`/`SignalRInput`/`BattleHub`; `RewardGranted` wire projection; `GET {gameId}/gold`.

**Phase C — Frontend (`ClientApp/src`).** `timeline.ts` `SET_GOLD`/`SHOW_REWARD`/`HIDE_REWARD`; `RewardGranted`
bumps the gold HUD + logs always, raises the modal only for Treasure/Mystery. `useBattleHub.ts` gold + reward
state, `acknowledgeReward()` (mirrors `respondRecovery`), `/gold` hydrate on load + reconnect. `BattleScreen.tsx`
inline `RewardModal` + run-scoped gold HUD. **The A/B web-path gate** (`GateBlockingRewardNodes`, which remapped
the blocking Treasure/Mystery nodes to wild battles while the client couldn't answer the ack) was **removed** here
now that `acknowledgeReward()` is wired — those nodes run at the full core distribution.

**Audit (A/B).** `/audit` → seam-reviewer **PASS-WITH-ADVISORIES**: no seam breaks; reward policy confirmed
web-side; default no-draw supplier preserves seeded-run RNG. The two advisories (the live Treasure/Mystery
deadlock → fixed by the Phase-C ack + interim gate; `RollGold` tuning constants documented as provisional) are
both resolved. Phase C touched no battle seam, so no separate audit.

**Recurring lesson (memory `feedback_hang_is_a_real_signal`):** a "hanging" test suite here means an infinite-run
test, not a broken harness — every `RunDirector.RunAsync()` test needs a guaranteed faint (a dead-end biome +
constant-pushover supplier loops forever; only a *battle* node can end a run).

**Follow-up — the Shop node ✅ DONE (2026-07-09).** A between-encounter shop that **spends** the transient
`Wallet`. `ShopRunEvent` **replaced** the Phase-A `InteractionStubEvent` (the "Shop keeps its `InteractionStubEvent`"
note above was the 2026-07-02 state) and rolls a per-visit, run-scaled stock via the web-layer `ShopCalculator`
(rarity-derived prices — *not* the unaffordable Gen 1 `Item.Cost`), emits a blocking `ShopOffered`, then runs an
iterative buy loop (`ChooseShopActionAsync` → buy/leave) charging the `Wallet` and filling the `Bag`. Full stack:
core event + `IBattleInput`/`SignalRInput` handshake + `BattleHub.BuyShopItem`/`LeaveShop` +
`SignalRBattleEventEmitter` projection + a React shop modal (`BattleScreen`). Buy-only MVP — selling / restock /
persistence remain out of scope (persistence rides the deferred `save.db` layer). Two refinements from review: the
shop is **affordability-gated** (a biome keeps a Shop node only when the wallet clears `ShopCalculator.MinItemPrice`
at biome entry — no dead 0₽ shop, so the opening node is never a shop), and purchases respect the Gen 1
**99-per-slot** `Bag` ceiling (a buy that would overfill is refused before charging). Covered by `RunDirectorNodeTests`
(buy/leave/no-op/headless/gate/99-cap), `ShopCalculatorTests` (pricing shape + seed), `BagTests` (99-cap),
`WebEventContractTests` (wire projection), Vitest (`timeline` + `battleReducer`), and a Playwright `shop.spec`
(earn gold → buy at a shop).

---

## Encounter Logic (roguelite run layer) ✅ DONE (2026-06-27 → 2026-06-28)

The roguelite **encounter layer** — *what the player faces, how the run is shaped, and the eligibility
guardrail acquisition will ride* — designed with the user (`/plan`, 2026-06-27, `ENCOUNTER_DESIGN.md`) and built
in reviewable phases. The run is now a **route through a graph of themed biomes**, each biome a sequence of
varied nodes capped by a Boss, playable end-to-end through the browser. **Phase 4 (acquisition channels — boss
catch + themed draft)** stays live in `TODO.md` as the bridge into the *Item Acquisition · Bag Persistence ·
Catch* cluster; everything below is the implemented §1–§3 of `ENCOUNTER_DESIGN.md`.

**Locked design (`/plan`, §1–§7).** A run is a **graph of type-themed biomes** under a regional origin (Kanto
first). The biome **type theme** is the cascade root: it drives the type-filtered encounter pool, which *is* the
**fought-only** acquisition guardrail. Enemy strength is an **`IEnemyArchetype`** seam (Weak/Medium/Strong/Boss)
composing existing levers (moveset quality, DV quality, BST band, level); biome **depth** sets the baseline band
and tier modulates it. Two **gated** acquisition channels (deferred to Phase 4): boss catch + themed draft. All
node kinds are `IRunEvent`s sequenced by the single `chooseNextEvent` (`BattleRunner` → `RunDirector`, per
`GAME_LOOP.md §3`).

**Phase 1 — Biome model + type-filtered pool (2026-06-27).** `creaturegame/Creatures/Biome.cs`: `Region` enum
(the multi-gen axis) + `BiomeDefinition` (Types + Neighbours, `Contains` = either-type match) + static `Biomes`
registry (verified 18-biome Kanto roster, all 15 Gen 1 types homed in 2–3 biomes each; `For`/`Playable` — empty
biomes never generate). `EncounterSelector.PickByBst` gained an optional biome filter with **in-theme
nearest-BST widening** (theme never broken). `CreateEnemyAsync` restricts to **wild-available** species (`"Wild"`
`GameAvailability`, full-dex fallback) + an optional biome param. Pins: `BiomeTests` (coverage/spread/graph
symmetry+connectivity/membership/`Playable`), `EncounterSelectorTests` biome cases, wild-only pin. Seam review
PASS; 1094/1094. (`ENCOUNTER_DESIGN.md §2`.)

**Phase 2 — `IEnemyArchetype` tiers + depth-scaled bands (2026-06-28).** Built 2a–2d:
- **2a — real TM/HM learnability:** `LearnMethod{LevelUp,Machine}` + `Method` on `PokemonLearnset`; migration
  `AddLearnsetMethod`; `LearnsetMapper` keeps machine rows; full re-import → `pokemon.db` carries 2,860 Machine
  rows across 145 species + 989 level-up rows. All level-up paths filter `Method == LevelUp`.
- **2b — `DvQuality{Poor,Average,High,Perfect}` seam** on `IStatCalculator.RandomiseDvs` (no-arg overload
  dropped, always explicit); `Gen1StatCalculator` maps the intents (Perfect=15 fixed, High 8–15, Poor 0–7,
  Average 0–15). Both callers pass `Average` (behaviour-preserving).
- **2c — depth-scaled bands:** `ScaleTargetBst(playerBst, depth)=playerBst+depth×10` + `ScaleWildLevel` depth
  lift ([50,80]%→~[90,120]%); `CreateEnemyAsync(depth)`; supplier `Func<Creature,int,Task<Creature>>` threaded
  `battlesWon`. Behaviour-preserving at depth 0.
- **2d — `IEnemyArchetype`/`EnemyTierSpec`** + Weak/Medium/Strong/Boss singletons (Default=Medium), each
  shifting the depth baseline; `LearnsetMoveSelector` gained `TmEnhanced` (best species-legal incl. TM/HM) +
  `Optimal` (best of any move) — deterministic top-N by a shared `MoveScore`, no level gate — and a `maxMoves`
  cap. All-tiers seed reproducibility pinned. Seam reviews PASS; 1111/1111. (`ENCOUNTER_DESIGN.md §3`.)
- **Deferred (§3.6):** Stat-Exp lever; the Boss out-class-the-player ceiling.

**Phase 3 — Biome graph + `RunDirector` + node bones (2026-06-28).** Built 3a–3c:
- **3a — event model / `RunDirector` graduation:** `RunLoop.cs` (`RunState`/`RunContext`/`Outcome`/`IRunEvent`) +
  `RunDirector` (renamed from `BattleRunner`) holding the single `chooseNextEvent`, with battle + Poké Center
  recovery as first-class events. Behaviour-preserving (endless chain identical). Seam review CLEAN.
- **3b — biome graph + map screen:** 3b-1 backend — `BiomeChoiceEvent` + `IBattleInput.ChooseBiomeAsync` seam;
  the run charts a route (region → choose biome → themed events capped by a Poké Center → choose a neighbour →
  repeat), threading the current biome into `CreateEnemyAsync`; `BiomeChoiceOffered`/`BiomeEntered` wire events.
  3b-2 activated it live — `CreatePlayerSetupAsync` computes `Biomes.Playable(Kanto, wildPool)` →
  `RunSetup.PlayableBiomes` → session → director; `SignalRInput.ChooseBiomeAsync` + `BattleHub.ChooseBiome` +
  the React `BiomeChoiceModal` (biome cards with type badges). Verified live.
- **3c — node-kind bones + tuned curve:** 3c-1 — a biome's route is a seeded `RunState.BiomeNodePlan` dispatched
  by `RunDirector.EventForNode`; six `RunNodeKind`s (wild/elite/boss battles + shop/treasure/mystery bones),
  each biome Boss-capped (§4). **Layering:** `IEnemyArchetype` is web-layer, so the core passes a
  generation-agnostic `EncounterTier {Normal,Elite,Boss}` through the supplier seam and the web maps it
  (`EnemyArchetypes.For` → Medium/Strong/Boss) — the same intent/mapping split as `DvQuality`.
  `InteractionStubEvent` bones emit a `RunNodeEntered` banner + advance the biome (`NodeVisitedOutcome`),
  behaviour later. `EventForNode`'s default arm throws (no silent mis-route). 3c-2 — tuned interior weights
  (Wild 70 / Elite 18 / Treasure 6 / Shop 4 / Mystery 2, independent per slot) + **biome-position depth**
  (`RunState.RunDepth` = nodes traversed, replacing `battlesWon` as the scaling axis; legacy chain unchanged).
  Pins: `RunDirectorBiomeTests`, `RunDirectorNodeTests`, `RunSetupBiomeTests`, `EnemyArchetypes.For`, Vitest
  banner/map arms. Seam reviews PASS. Verified live (map → themed encounter; shop + boss banners; boss is a
  tough optimal-moveset foe). 1132/1132 + Vitest 80/80. (`ENCOUNTER_DESIGN.md §5`/§3.2.)

**Run model (confirmed with the user):** region (Kanto) → player chooses a biome → ~3 themed nodes capped by a
Poké Center → choose the next biome (its neighbours; dead-end → any playable) → repeat until death.

---

## Item System ✅ DONE (2026-06-19 → 2026-06-20)

The full Gen 1 **Item System** — data import + use-in-battle — designed with the user (`/plan`, 2026-06-19)
and built in reviewable stages. Item use is playable end-to-end through the browser. **Still deferred** (moved to the *Item Acquisition ·
Bag Persistence · Catch* cluster in `TODO.md`): the Poké Ball / catch effect, real item acquisition, bag
persistence, and Revive/Max Revive (blocked on a party system).

**Locked design (`/plan`):** `items.db` + `ItemsDbContext` parallel to `moves.db`/`pokemon.db`; scope =
"anything usable *in battle*"; Gen 1 roster is a **hand-curated allowlist** (`ItemMapper.Gen1BattleItemNames`)
because PokeAPI has no Gen 1 item signal (no `/generation/1`, `game_indices` only reach Gen 3). `ItemAction`
priority **above any move** (Gen 1 items resolve first); `IItemEffect`/`ItemEffects` registry keyed by
`ItemCategory` (the item analogue of `IMoveEffect`); transient player-only `Bag`; additive
`IBattleInput.ChooseTurnActionAsync` seam (default delegates to `ChooseMoveAsync`, so AI/auto inputs untouched).

**Data import.** `Item` model + `ItemCategory` enum (Gen 1 gameplay numbers — heal amount, cured status,
revive %, PP restore, X-item boost — as **data on the row**, not a seam; ball catch-rate deliberately NOT
modelled, capture is a battle formula). `ItemsDbContext` + migration `DB/Migrations/Items`; `PokeApiItem` DTO
+ `ItemImport` + `ItemMapper` (pure mapping + roster, mirroring the `EvolutionImport`/`EvolutionMapper` split);
idempotent upsert; `Program.cs` step + `-- items` stage. `ItemService` read API. **Live import: 29 items**,
categories + numbers verified. `ItemSpriteDownloader` → `wwwroot/sprites/items/{id}.png` (idempotent, gitignored
like creature sprites); bag menu shows each sprite. Tests: `ItemImportTests` + `ItemsDbServiceTests`.

**Phase 1 — core engine.** `Bag` (`Items/Bag.cs`, ConcurrentDictionary id→qty), `ItemAction`
(`Combat/ItemAction.cs`), `IItemEffect`/`ItemEffects` + Heal/StatusCure/PpRestore/X-item
(`Combat/ItemEffects.cs`). Turn-loop integration in `Battle` (player builds move-or-item; `CanAct`/dead-target
guards scoped to `AttackAction` only — item use is legal while asleep); `ChooseTurnActionAsync` + `TurnChoice`
seam; `StatStages.Raise/Of` helper. Events `ItemUsed`/`PpRestored`/`ItemUseFailed` + SignalR projection +
timeline arms (`WebEventContractTests` forced the wire pre-UI). `Item.RestoresPpAllMoves` added (Ether=one
move, Elixir=all). Tests: `ItemEffectTests` + `ItemActionBattleTests`.

**Phase 2 — web wire.** `BattleHub.UseItem` → `GameSessionManager.SetItemChoice` → `SignalRInput` (refactored
to a single per-turn handshake — one TCS resolves to a move **or** an item; `ChooseMoveAsync` is a thin
move-only wrapper). Bag threaded end-to-end: `EncounterFactory` seeds a per-run `Bag` from `items.db` (generous
test loadout, every item ×20) + item catalog → `GameController` → `GameSessionManager` → `BattleRunner` → every
`Battle`. `GET /{gameId}/bag` endpoint (`BagItemView`). Tests: `SignalRInputTests` + a bag-seed assertion.

**Phase 3 — frontend.** BAG button + grouped item list (`BattleScreen` `'bag'` view → `BagMenu`); fetches the
bag fresh on open, filters to battle-usable pockets (Ball & Revive hidden so a guaranteed no-op can't waste a
turn), groups by pocket. PP-restore move-slot pick: single-move restores open a `PpTargetPicker` (reuses the
move-grid), whole-moveset restores use directly — distinguished via the `BagItemView.RestoresPpAllMoves` field
(no client name-sniffing). Pure `bag.ts` helpers unit-tested (`bag.test.ts`, 10 cases) + `item-use.spec.ts`.

**Phase 4 — Dire Hit + Guard Spec.** The last two in-scope effects, both `BattleStatBoost` boosters reusing the
matching Gen 1 move mechanics: **Dire Hit** → `BattleState.HasFocusEnergy` + `FocusEnergyApplied` (incl. Gen 1's
bugged ÷4 crit in `Gen1BattleRules.GetCritChance`); **Guard Spec.** → `HasMist` + `MistApplied`. Zero web work
(those events already had projections + arms). `Item.BoostsCrit`/`SetsMist` fields + migration
`AddItemCritAndMistBoosts`; `XItemEffect` → `BattleBoostItemEffect` (one category-keyed effect dispatching
X-item / Dire Hit / Guard Spec by item data). Tests across `ItemEffectTests`, `ItemImportTests`,
`ItemsDbServiceTests`, `ItemActionBattleTests` + a Guard Spec Playwright case.

**`dev.ps1`:** added `-NoBrowser` flag (skip auto-open) and fixed the backend opening a stray `:5100` tab.

---

## Evolution System ✅ DONE (2026-06-18 → 2026-06-19)

Full **level-up evolution end-to-end** — data + seam, core + run-loop, Phaser sprite-morph, plus a Gen 1
B-cancel prompt. Designed with the user (`/plan`); built and committed in reviewable stages, each through the
`/audit` gate.

**Design calls (with the user):** trade evolutions have no trading in a single-player roguelite, so the 4 Gen 1
trade lines (Kadabra→Alakazam, Machoke→Machamp, Graveler→Golem, Haunter→Gengar) evolve at **level 37** (flat);
**stone** evolutions are **deferred with the Catch/bag work** (the `Stone` trigger + `IEvolutionRules.StoneUsed`
are built but dormant — no caller emits a stone-use until a bag exists). The data stays **faithful** (a trade
evo is a `Trade` row); the level-37 conversion lives on the seam, not in the data.

**Stage 1 — data + seam (commit `2259a33`).** `PokemonEvolution` table in `pokemon.db` (`FromSpeciesId`,
`ToSpeciesId`, `Trigger`, `LevelThreshold?`, `StoneItemId?`, `Generation`) + migration `AddPokemonEvolution`.
Importer: `EvolutionMapper` (pure chain→Gen-1-edge filter — rejects happiness/time/held-item level-ups,
held-item trade, and >151 species) + `EvolutionImport` (idempotent, dedup-by-chain) + DTOs + an `-- evolutions`
re-run arg. **Live import: 72 Gen 1 edges (52 Level / 16 Stone / 4 Trade)** — matches canon. New generation
seam **`IEvolutionRules`** + `Gen1EvolutionRules.Instance` (`creaturegame/Evolution/`); `EvolutionContext` is a
closed record hierarchy (`LeveledTo` / `StoneUsed` / `Traded`). Tests (20): `EvolutionImportTests`,
`Gen1EvolutionRulesTests` (asserts the level-37 quirk + stone dormant-on-levelup/ready-on-stoneuse),
`PokemonEvolutionDataContractTests` (live-db pin: 72/52/16/4, trade lines, Eevee's 3 branches, dex-bounds).

**Stage 2 — core + run-loop + event (commit `7eea368`).** `Creature.EvolveTo(PokemonSpecies)` adopts the new
species and recomputes via the existing `IStatCalculator` path (no new stat math); the individual half
(DVs/Stat Exp/Level/XP/PP/moveset) carries over and current HP rises by exactly the max-HP delta — authentic
Gen 1. `MoveLearning.LearnMovesForLevelAsync` extracted from `Battle` (behaviour-preserving) and reused for the
evolved form's moves. `BattleRunner` takes an injected `checkEvolution` resolver (mirrors `enemySupplier` —
core stays data/gen-agnostic; null = plain chain); the web resolver is
`EncounterFactory.ResolvePlayerEvolutionAsync` (edges → `Gen1EvolutionRules` → evolved species + learnset).
`CreatureEvolved` event + SignalR projection + field-level contract guard (the recurring web event
field-projection gap) + console line. Tests (+9): `EvolveToTests`, `BattleRunnerEvolutionTests`,
`EncounterEvolutionTests`.

**Stage 3 — Phaser morph (commit `4f78d1d`).** `playEvolutionAnimation` bridge command + `BattleScene`
handler: the classic Gen 1 **white-silhouette flicker** (`setTintFill(0xffffff)` alternating old/new shapes,
settling on the evolved back sprite), loaded on demand, emitting `animationComplete` so the `timeline.ts`
`awaitAnim` contract holds. Correctness fix: evolution updates `playerTrueSpeciesId` (+ `initialPlayerSpeciesId`)
so the post-win `resetPlayerSprite` reverts to the *evolved* form. Vitest pins the arm order.

**Cry-mismatch fix (commit `d863ca9`).** Surfaced while building the morph: the OGG cry keys were bound once in
`preload()` to the *initial* species, so with cries present (production) every chained enemy played the first
enemy's cry and an evolved/transformed player cried as its pre-form. Re-keyed cries by species id (`cry-{id}`),
loaded on demand wherever the sprite changes.

**Cancel/abort evolution + level-up gate (2026-06-19).** Gen 1 B-cancel: evolution is offered via a blocking
`EvolutionOffered` event + an Allow/Cancel modal (mirrors the Poké Center recovery prompt end-to-end —
`IBattleInput.ConfirmEvolutionAsync` default-allow, `SignalRInput` TCS handshake, hub `RespondEvolution`, React
`EvolutionPromptModal`); on cancel → `EvolutionCancelled`, creature untouched. Made the check **Gen 1-canonical**:
evolution is attempted only on an actual **level-up** that battle, so a declined evo re-offers at the *next*
level-up (not every win). Tests (+5): runner allow/cancel/no-level-up, `EvolutionOffered` field guard, timeline
offer/cancel arms.

**Accepted limitation:** a multi-threshold level jump in a single battle evolves only one stage that win (the
next stage fires on the next win). **Deferred:** an in-run Playwright E2E (a real evolution is hard to force
deterministically without a test-only "evolve now" hook; the bridge ordering contract is covered at the
timeline layer). **Still open:** stone evolutions (need the bag — Catch Mechanic).

---

## Web UI Polish — Run-Over Screen, Overview, Sprite-Shake ✅ DONE (2026-06-18)

Three more battle-UI polish items, all frontend — the engine already emitted the driving events
(`RunEnded`, `DamageDealt`) and exposed the live creature; the work was rendering/animation + one new REST
snapshot.

**Run-scoped game-over screen (`BattleEndedOverlay`).** Built into the Endless Battle Chain's terminal
`RunEnded` event (→ `phase: 'ended'`), **not** a per-`BattleEnded` overlay — a win is just a mid-chain
intermission, so a game-over screen only fits at the run's true end. Full-field `alertdialog` over a
hard-dimmed field: "GAME OVER", a greyed faint sprite, a run summary (BATTLES WON / FINAL LEVEL), and
**PLAY AGAIN** (→ `/select`, fresh starter pick) / **QUIT** (→ title). Replaced the old one-line "Game over"
action-prompt; the in-battle FIGHT/CHECK menu is hidden when ended. Tests: `endless-chain.spec.ts` "a run
ends…" asserts the overlay + PLAY AGAIN → `/select` (the timeline's `RUN_ENDED` dispatch was already
unit-covered), live-verified.

**Pokémon overview screen (CHECK POKEMON).** Tabbed INFO / STATS / MOVES overview replacing the old
base-stats `CheckPanel`, opened by the in-battle CHECK POKEMON action. Shows actual stats + per-stat DV
(0–15) + Stat-Exp, types/status/HP/XP/BST + front sprite (INFO), and per-move type/category/power/accuracy/
PP/description (MOVES). Data via a new on-demand REST snapshot `GET /api/game/{gameId}/player`
(`PlayerOverviewDto.From(Creature)` reading the live in-session creature from `GameSessionManager`) — kept
off the per-turn event stream. Gen-1 model (single Special; physical/special by move type). Tests:
`PlayerOverviewDtoTests` (stat + category mapping), `e2e/overview.spec.ts` (tab structure), live-verified.
*(Between-battles/party entry stays with the deferred Game-Loop layer.)*

**Sprite shake tween on damage received.** A quick directional horizontal jolt on the struck sprite, emitted
from the `DamageDealt` timeline step (`playDamageShake` bridge command → `BattleScene.shakeSprite`).
Fire-and-forget (overlaps the hit sound + HP drain, not awaited), touches only x so it coexists with the idle
y-bob, jolts away from the attacker, and snaps back to rest x. No shake on an immune no-hit (the `eff=0`
early-return path emits nothing). Tests: `timeline.test.ts` (emit present + correct side; absent on immunity),
battle E2E lunge→hit ordering still green, live-verified.

---

## Web UI Polish + Per-Run Web Seed ✅ DONE (2026-06-17)

A polish pass over the battle UI plus the final RNG-seam closure. Three of the move-menu/log cues share one
pattern — the engine computes the fact and ships it on `TurnStarted`/`DamageDealt`; the client only renders —
and one recurring trap surfaced (SignalR projection silently dropping new fields).

**Per-run web seed (Architecture Review #3 / Tech Debt #3).** The core was already seedable (`IRandomSource`
threaded through engine + rules; `BattleScenario.Seed`); the leak was the web composition root building runs
unseeded. `GameController.Start` now picks one seed per run (client may supply `StartGameRequest.Seed`, else a
random int — logged + returned as `{ gameId, seed }`) and threads a single `SeededRandomSource` through the
whole run: player + every enemy's construction (`EncounterFactory` seeds `Gen1StatCalculator` for DVs and
passes the source to `LearnsetMoveSelector`/`PickByBst`/`ScaleWildLevel`), the battle (`BattleRunner`), and the
AI (`Gen1TrainerAi`). One shared instance is safe — the run is single-threaded. Proven by
`RunSeedReproducibilityTests` (same seed → identical player + enemy: species, level, DVs, moveset). Unblocks
the deferred recovery/replace-move modal E2Es (pass a fixed seed). Docs: `ARCHITECTURE.md §2.10`,
`GAME_LOOP.md §4`. (Earlier rules-RNG seeding, 2026-06-12, already closed the `Roll*` flakiness.)

**Move-menu STAB indicator.** `MoveInfo.Stab` on `TurnStarted` = damaging move whose type matches the user's
*current* type (computed engine-side, so it's correct under Conversion/Transform; mirrors the
`DamageCalculator` STAB condition). Renders as a gold left-edge accent + a `STAB` corner pill. Tests:
`MoveInfoStabTests` (single + dual type; status/off-type excluded), `e2e/battle-ui-cues.spec.ts` (deterministic
on Charizard's L50 set).

**Move-menu effectiveness pill.** `MoveInfo.Effectiveness` = the move's type multiplier vs the *current* enemy
(product over the enemy's types via the active `ITypeChart`; damaging moves only — fixed-damage/status report
neutral 1.0). Renders bottom-right (opposite STAB) as a colour-graded ×N pill: ×4/×2 green, ×0.5/×0.25
amber/orange, ×0 red; neutral 1× hidden. Decimal labels (×0.5) chosen over vulgar-fraction glyphs (½) — the
pixel font renders the fractions too small. Tests: `MoveInfoEffectivenessTests` (incl. dual-type ×4/×0.25 and
0× immunity).

**Colour-coded battle log.** `DamageDealt` tags its log line with a `LogTone` (`super`/`weak`/`immune`),
carried via the `LOG` action → `LogEntry` → a `log-line--{tone}` class (green / muted / red); neutral hits keep
the default colour. Test: `timeline.test.ts` tone arm.

**Recurring trap — SignalR projection drops new fields.** `SignalRBattleEventEmitter.MapEvent` hand-maps each
event (and nested `MoveInfo`) into an anonymous object; the STAB flag passed its engine test but never rendered
because the projection didn't forward it. Fix: forward the field + a field-level guard
(`WebEventContractTests.TurnStarted_MoveProjection_Carries{Stab,Effectiveness}`) — the reflection contract test
can't catch this (it builds events with empty move lists). Captured in the `web-event-field-projection-gap`
memory.

**Friendlier connection error.** `StarterSelection.tsx` maps a failed fetch (the `TypeError: NetworkError…`
case) to "Couldn't reach the game server. Make sure the backend is running, then reload." (HTTP-status errors
get their own message), and the start-game fetch gained the `try/catch` it was missing.

**Earlier move-menu polish (2026-06-10):** level-up stat-gain panel on `LeveledUp` (per-stat `StatGains`,
fanfare, stays until next input).

---

## EV Gain (Stat Experience) ✅ DONE (2026-06-17)

Gen 1 "EVs" = **Stat Experience**: a win adds the defeated foe's base stats to the player's `Exp*` (the
fields existed but were never written). Realized into actual stats **only on the next level-up** (Gen 1 never
applies Stat Exp mid-level), so it pays off through the chain's regular level-ups.

- `IStatCalculator.AwardStatExp(victor, defeated)` seam — gain rule + per-stat 65535 cap live on
  `Gen1StatCalculator` (both are gen-variable: Gen 3 uses EV yields + 252/510 caps), **not** inline in
  `Battle`. Thin `Creature.GainStatExp(defeated)` delegates to its `StatCalculator`.
- Hooked in `Battle`'s win branch after `AddExperience`, **before** the level-up loop, so a level gained
  that battle already reflects the new training. No immediate `CalculateStats` (the level-up's recompute is
  the realization point — authentic).
- No new battle event (Gen 1 is silent about Stat Exp).
- **DV→IV doc hooks** (requested groundwork): `IStatCalculator` XML documents the per-generation evolution
  (DV 0–15 / 4-stored+derived-HP → IV 0–31 / 6-independent; Special DV stays shared in Gen 1–2, splits to
  Sp.Atk/Sp.Def IVs in Gen 3; Stat Exp → EV yields/caps). `Creature`'s `Dv*`/`Exp*` regions documented as the
  IV/EV precursors. A `Gen3StatCalculator` is a drop-in implementation of that seam.
- Tests: `Unit/StatExpGainTests` (per-stat gain, accumulation, 65535 cap, realize-only-on-level-up +
  no-mid-level-change, end-to-end win awards the foe's base stats).
- Audit: `/audit` PASS-WITH-ADVISORIES (0 blockers); fixed the Special-row doc (Gen 2 splits the *stat*, the
  DV→IV change is Gen 3) + noted the single-participant scope at the call site. Deferred: the `65535` test
  literal (promoting the private `StatExpMax` just for tests would over-expose it).
- **Single-participant scope:** one player creature, no switching, so the finisher is the only participant.
  Gen 1 splits Stat Exp among all participants — revisit `AwardStatExp`'s call site when a party/switching exists.

---

## AI Move Selection ✅ DONE (2026-06-17)

**Prerequisite:** Learnset System (so AI evaluates moves the Pokémon can actually learn) — done.

Shipped an intelligent-but-fallible Gen 1 enemy brain behind a new generation/game-specific seam, **live**
on the chained enemy (`GameSessionManager` now uses `new AiBattleInput(new Gen1TrainerAi())` instead of the
old uniform-random `RandomMoveInput`). Per the brief: smarter than random, but keeps Gen 1 quirks /
bad decision-making rather than being a perfect optimiser.

**Architecture (two seams + an adapter):**
- **`IBattleAi`** (new) — the gen/game-specific *brain*: `ChooseMove(candidates, TurnContext)`. Split from
  `IBattleInput` (the I/O plumbing) so brains (wild/trainer/gym/future-gen) and plumbing vary independently.
- **`AiBattleInput : IBattleInput`** — thin adapter hosting any brain; owns the candidate filter (PP>0, not
  Disabled). `RandomMoveInput` stays available as the trivial wild-tier brain.
- **`IMoveEvaluator`** building blocks (gen-agnostic, score one dimension each), combined by
  `CompositeEvaluator` (weighted sum = "personality"): `DamageEvaluator` (expected-damage fraction of target
  HP, KO scores >1, accuracy-discounted, handles every `DamageCategory`, uses the new deterministic
  `DamageCalculator.EstimateDamage`); `TypeEffectivenessEvaluator` (log-scale SE bonus / NVE penalty / hard 0×
  penalty — the authentic Gen 1 lean); `StatStageMoveEvaluator` (self-buff/foe-debuff by headroom, penalise a
  maxed stat); `StatusMoveEvaluator` (value fresh status by severity; redundant if already statused or
  type-immune — immunity routed through the authoritative `IBattleRules.CanReceiveStatus`, not an inline
  guess). Default mix: damage 1.0, type 0.6, stat-stage 0.5, status 0.6.
- **`Gen1TrainerAi : IBattleAi`** — scores candidates then picks *probabilistically* via a softmax (not
  argmax). An `intelligence` knob (0 = near-random, 1 = near-greedy, default 0.7) maps to the softmax
  temperature, so trainer tiers are one number. The fallible pick **is** the "keep some Gen 1 bad
  decision-making"; the quirks live in the evaluators. Replaces the planned separate `GreedyAIInput`/
  `WeightedAIInput` — both are just temperature settings on one brain.

**Engine support:** `DamageCalculator` refactored to extract a private no-RNG `ComputeDamage` core shared by
the live `CalculateDamage` (rolls crit+variance) and the new `EstimateDamage` (deterministic, AI-only) — a
behavior-preserving extraction, pinned by `Estimate_MatchesLiveCalcWithNoCritAndNoVariance`.

**Tests:** `Unit/MoveEvaluatorTests` + `Unit/BattleAiTests`.

**Audit:** seam-reviewer BLOCK (wrong inline Gen-1 Ice→Freeze status immunity) fixed by routing through
`IBattleRules.CanReceiveStatus` + a guarding `Status_FreezeIsValuedAgainstAnIceFoe` test. Deferred advisory:
`DamageEvaluator` normalises accuracy as `Accuracy/100.0` rather than the engine's 0–255 `GetHitThreshold`
model — a pure ranking heuristic (stages cancel in relative ranking), not a seam break. **New failure mode
(seam-reviewer log):** even a read-only AI *heuristic* that re-derives a gen-variable rule inline is a §5.0.1
leak — consult the rules seam, never reimplement the fact.

**Future tiers (not needed yet):** distinct trainer-class weight vectors / `intelligence` values
(wild < trainer < gym); revert the *wild* enemy to `RandomMoveInput` once explicit trainer battles exist.

---

## Roguelite Run Layer — Recovery & Encounter Scaling ✅ DONE (2026-06-11)

Two run-layer features on top of the Endless Battle Chain. Both are **run/game-loop concerns, not battle
mechanics**, so they stay in the run orchestrator (`BattleRunner`) / web encounter builder (`EncounterFactory`)
and are *not* behind an `IBattleRules` seam — `/audit` §5.0 clears them (no new engine magic numbers, no gen
checks, full heal + level band are generation-invariant choices).

- **Poké Center recovery every 3rd win — an interactive game-loop step.** After every 3rd chained win the
  player is *offered* a full restore before the next encounter; it's its own blocking node in the loop, not a
  silent auto-heal. `Creature.FullHeal()` does the restore (HP→max, all PP→max, major status cleared, Toxic
  counter reset) — matches the Gen 1 Poké Center exactly (HP + PP + status, unconditional/free), identical in
  every generation, so it's ordinary engine logic, not a seam. Interval is `BattleRunner.healEveryNBattles`
  (default 3, 0 disables).
  - **Blocking choice** reuses the move-replacement plumbing: `IBattleInput.ConfirmRecoveryAsync` (default
    accepts, so AI/headless never block) ↔ hub `RespondRecovery` ↔ `SignalRInput` TCS. `BattleRunner` emits
    `RecoveryOffered(name, speciesId, battlesWon)` then awaits the choice; on accept → `FullHeal` +
    `PlayerRecovered`, on skip → `RecoveryDeclined` (status still carries). All three events mapped in both
    emitters + `timeline.ts`.
  - **UI:** in-page `RecoveryModal` (BattleScreen) shows the player's creature sprite with a CSS heal-glow and a
    single **HEAL / SKIP** press that both decides and advances the chain. Verified live (Puppeteer): offer →
    modal blocks → HEAL → "was fully healed!" → next battle; and the SKIP path → "decided to keep going!".
  - Tests: `BattleRunnerTests` (heals once after win 3 restoring HP/PP/status; **declining** leaves the player
    wounded/poisoned), `CoreMechanicsTests.FullHeal_*`, auto-covering `WebEventContractTests`, `timeline.test.ts`
    (offer/heal/decline). **Deferred:** a recovery-modal **E2E** spec (needs the seeded-battle entry point to
    reach 3 wins deterministically — same reason the replace-move modal E2E is deferred).
- **Wild level band 50–80% of player level.** `EncounterFactory.ScaleWildLevel` replaces the old
  `playerLevel ± 3` with a uniform pick in `[floor(0.5·L), floor(0.8·L)]`, floored at 2 — wild foes sit a step
  below the player so the chain stays winnable while still scaling. Tests: `EncounterLevelBandTests` (band
  bounds across levels, both ends reachable, never < 2).

---

## Learnset System — Level-up move learning ✅ DONE (2026-06-11)

Closes the learnset loop: on a win, when the player levels into a move on its species learnset, Battle now
teaches it. Only the player ever learns (enemies are settled at build time). Built as a **full vertical
slice** (engine + SignalR + React modal + tests), audit-gated.

**Engine (`creaturegame`):**
- `LearnsetMove(int Level, Attack Move)` record; `Creature.Learnset` (permanent half, untouched by
  `ResetBattleState`, so learns persist across the chain) + `MovesLearnedAtLevel(level)` (filters
  already-known) + `ReplaceMove(slot, move)`.
- Events: `MoveLearned`, `MoveReplacementRequired` (blocking), `MoveForgotten`, `MoveLearnDeclined` — so
  **all four log lines are engine-driven**, not frontend-local (consistent with the event→timeline pattern;
  covered by the web event-contract test).
- `IBattleInput.ChooseMoveToForgetAsync(MoveReplacementContext)` — **default interface method ⇒ decline**, so
  AI / auto / scripted inputs never block; only `SignalRInput` overrides it.
- `Battle` faint loop: after each `LeveledUp`, `LearnMovesForLevelAsync` auto-adds (free slot → `MoveLearned`)
  or emits `MoveReplacementRequired` and **awaits `_playerInput`** — a slot → `MoveForgotten` + `MoveLearned`,
  null → `MoveLearnDeclined`. Drives moves/levels one at a time (canonical order).
- **Transform/Mimic interaction (seam-reviewer catch):** the win branch now reverts the player's copied
  identity (`RestoreMimickedMove` / `RestoreOriginalIdentity`) **before** the learn loop, so a move learned
  while Transformed lands on (and persists to) the real moveset instead of being discarded by the
  end-of-battle restore. Guarded by `Learnset_LevelUp_AfterTransform_PersistsLearnedMoveOntoOriginalMoveset`.

**Web (`creaturegame.Web`):** `SignalRInput` second TCS + `SetForgetChoice`; `BattleHub.ForgetMove(int?)`
(null = SkipNewMove); `GameSessionManager.SetForgetChoice`; `SignalRBattleEventEmitter` + `ConsoleBattleEventEmitter`
cases for the four events; `EncounterFactory.CreatePlayerSetupAsync` resolves + attaches `player.Learnset`
(enemies get none). The XP bar already filled live (`TurnStarted` carries `XpThisLevel`/`XpToNextLevel` from
the XP & Level-Up work) — verified, no change needed.

**Frontend:** `timeline.ts` cases for the four events; `useBattleHub` `moveReplacement` state + `forgetMove`;
**two-step replace-move modal** in `BattleScreen.tsx` — choose a slot (or "Don't learn"), then a **Yes/No
confirm** so no move is deleted on a single misclick. The modal supersedes the level-up stat panel (Gen 1
order). Canonical text: "is trying to learn X!" / "forgot Y!" / "learned X!" / "did not learn X."

**Gen-seam call (ran §5.0):** the mechanic is **gen-invariant** — no new `IBattleRules` member. The only
gen-variable input is the learnset *data* (`PokemonLearnset.Generation`, filtered by `ActiveGeneration`); the
4-slot cap reuses `AddAttack`/`MaxMoves`.

**Tests:** `LearnsetLevelUpTests` (free-slot auto-learn; full-slots prompt + decline; forget-a-slot replace;
Transform-persistence) + `ScriptedInput.ForgetsSlot` / `BattleScenario.PlayerForgetsSlot` harness; Vitest
timeline cases; **Playwright `learnset.spec.ts`** — a low-level **Mew** (BST 500 ⇒ BST-matched-strong foes ⇒
fast XP) started at L9 auto-learns Transform on reaching L10, via the `reachLog` restart-on-loss pattern
(`startBattle` now matches the card by EXACT name so `MEW` doesn't also grab `MEWTWO`). **Deferred:** the replace-MODAL E2E (needs four full slots AT a learn-level —
not reliably reachable without the seeded-battle entry point, Tech Debt #3; covered at the .NET/Vitest layer);
a console `IBattleInput` that can answer the prompt (none exists yet — the default-decline is the placeholder).

---

## Completed ✅

<details>
<summary>Type Chart, PP, Status, Crits, Move Effects, Damage Categories, Bad Poison, XP/Levelling, Enemy Encounters</summary>

**Type Chart** — `ITypeChart` + `Gen1TypeChart` (15-type Gen 1 matrix, Ghost/Psychic bug, Poison→Bug quirk). Wired into `DamageCalculator` and `AttackAction`.

**PP Tracking** — `PokemonAttack` wrapper; decrements on use; Struggle when all PP = 0.

**Move Priority** — `AttackAction` reads `move.Priority` (was hardcoded 0).

**Status Conditions** — Applied after damage; `EffectChance` roll; sleep turn counter; status blocked if target already statused.

**Status Effects in Battle Loop** — Sleep/Freeze/Paralysis pre-turn; Burn/Poison end-of-turn 1/16; Confusion; Paralysis quarters Speed in sort order.

**Critical Hits & Stat Stages** — Gen 1 Speed-based crit formula; high-crit moves; stat stage multipliers on `IBattleRules`; crits ignore stages and Burn.

**Move Effects** — `MoveEffect` enum; stat-stage moves (Swords Dance, Growl); Haze; Flinch; Recharge; LeechSeed; Binding; TwoTurn.

**Damage Categories** — Fixed (Dragon Rage), LevelBased (Seismic Toss), OHKO, SelfDestruct (halves target Defense), SuperFang, Drain.

**Bad Poison (Toxic)** — `StatusCondition.BadPoison`; `ToxicCounter` escalates damage each turn; `IBattleRules.BadPoisonDamageFraction`.

**Experience, Levelling & Level Picker** — Gen 1 wild XP formula; `LeveledUp` event; level slider in UI (5–100); `GainExperience → LevelUp` path. *(Core mechanic only — XP is awarded and the player levels up at the moment of victory, recalculating stats. The on-screen XP bar is still cosmetic and there's no level-up move learning; see "XP & Level-Up — finish the in-battle loop" in `TODO.md`.)*

**Enemy Encounter System** — BST-matched random selection (±15%, widens to ±50%/all); enemy level = player level ±3; player's own species excluded. `EncounterSelector` in core library.

</details>

---

## Post-coverage sequencing — DONE (2026-06-06 → 09)

The ordered pass that followed the move-coverage completion. All six items done; only the deferred
`GameController` run-seed (Tech Debt #3, needs the Game Loop) remained open in `TODO.md`.

1. **Type/identity-mutation batch** (Transform + Conversion) — completed the 165-move coverage.
2. **jump-kick / hi-jump-kick Ghost-immunity crash edge** — Gen 1 also crashes the user on Fighting→Ghost 0×.
3. **Counter for fixed / level-based damage** — Sonic Boom / Seismic Toss / Super Fang are now counterable;
   only Bide's unleash opts out. The Normal/Fighting last-damage-type gate lives on `IBattleRules`.
4. **`AttackAction` lock-in abstraction (`ILockInMechanic`, Architecture Review #6a).** The four lock-in
   mechanics (two-turn / rampage / rage / bide) live behind `ILockInMechanic`
   (`creaturegame/Combat/LockInMechanics.cs`): a registry Battle iterates for the forced move, and three
   per-turn hooks (`OnCommit` charge/store, `OnRelease` unleash/counter-setup, `OnTurnEnd` rampage
   self-confuse) that `AttackAction.ExecuteAsync` drives. Behaviour-preserving (821/821 unchanged;
   seam-reviewer verified emission order, PP-once, RNG order, OnTurnEnd parity 1:1). Gen-variable numbers
   still come from `IBattleRules` via the context; the mechanics encode only Gen-1 lock-in *structure*.
5. **The full integration-test pass.** `BattleScenario` full-battle harness; interaction probes for
   Substitute, lock-in/forced-selection, status-stacking, crit, Counter, Rage, Hyper Beam recharge, Bide,
   paralysis turn-order flip, Wrap trap-lock, and poison+Leech-Seed end-of-turn stacking; the engine→web
   `MapEvent` contract test (`Integration/Web/`); and end-to-end flow tests (well-formed lifecycle event
   stream + win→XP→`LeveledUp` chain) over real DB moves (`Integration/Flow/`). No engine bugs surfaced —
   the probes pin Gen 1 quirks against regression.
6. **`BattleState` facade migration (Architecture Review #2).** Deleted the ~33 delegating properties on
   `Creature` and migrated ~222 call sites across the engine + test suite to `creature.Battle.X` in a
   single compiler-driven pass (full suite green before and after: 840 passed / 0 failed). New per-battle
   fields can now *only* be added to `BattleState` — a forgotten reset is structurally impossible.
   `STATE_MODEL.md` updated to match (facade documented as removed).

## Tech-Debt cleanups — DONE

- **`EncounterFactory`'s learnset query was copy-pasted five times → one `LoadLearnsetsAsync` home
  (2026-07-31).** Filed the same day from a review pass over the Generation Profile Stage 1a–2b commits: five
  sites (player setup, draft, boss catch, enemy, evolved form) each opened with
  `int gen = (int)profile.Generation;` and ran the same generation-filtered learnset query, differing only in
  the species id and (on the enemy path) a learn-methods array. Pre-existing duplication — it looked the same
  under the deleted `ActiveGeneration` const — that Stage 1b had grown by the per-site `gen` local. Collapsed
  into a private `LoadLearnsetsAsync(PokemonDbContext, GenerationProfile, int speciesId, params LearnMethod[])`
  helper (the single-method sites pass `LearnMethod.LevelUp`; the enemy path passes its array — EF translates
  `methods.Contains(l.Method)` to SQL `IN`, the shape the enemy query already used). The only remaining `gen`
  local sits in `ResolvePlayerEvolutionAsync`, which still needs it for the *evolutions-edge* query — a
  different table, not a learnset read. Shipped as a rider on Generation Profile Stage 3, per the item's own
  "when Stage 3 touches the file anyway" scheduling; behaviour-preserving, suite green before and after.

- **`GenerationProfiles.Registered` allocated a fresh array per call (2026-07-31).** Same review pass, filed as
  *(micro)*: the property did `ByGeneration.Keys.ToArray()` on every read, and it sits on the request path —
  `ParseGeneration` calls `.Contains` on it for every `/start`, and (since Stage 3) for every starter-picker dex
  read too. Materialised once into a `static readonly` field, declared **below** `ByGeneration` because static
  field initializers run in textual order — the same order trap `Gen1Profile.Gen1Types` documents. Shipped as a
  Stage 3 rider.

- **csproj boilerplate → `Directory.Build.props` (2026-07-17).** All four projects copy-pasted the same three
  properties — `<TargetFramework>net9.0`, `<ImplicitUsings>enable`, `<Nullable>enable` — and there was no
  solution-wide place for build policy, so keeping them in sync was manual and nothing enforced the repo's
  0-warning state. Added a root `Directory.Build.props` (MSBuild auto-imports it into every project) carrying
  those three plus **`<TreatWarningsAsErrors>true`**, and deleted the now-redundant `<PropertyGroup>` lines from
  each csproj (leaving only each project's genuinely local settings — `OutputType`, `IsPackable`, the
  `InternalsVisibleTo`/package/project item groups). `TreatWarningsAsErrors` was safe to switch on precisely
  because the build was verified clean first: a full `--no-incremental` rebuild stayed at **0 warnings / 0 errors**
  after the change, and a new warning now fails the build instead of accumulating silently. The clean rebuild is
  also the proof the import works — all four csprojs now build with no `TargetFramework` of their own.
  **Strictly a build-config change** — no product code touched; the full .NET suite stayed at 1317 green.
  *Deliberately not included:* raising `AnalysisMode`/`AnalysisLevel` beyond the SDK default (that surfaces a
  large batch of new style/design warnings — a real cleanup, not the "cheap insurance to keep the build clean"
  this item was), and a `Directory.Packages.props` central-package-versions pass (only EF Core `9.0.6` is shared
  across projects today — not yet worth the indirection).

- **`BattleScreen.tsx` was 1317 lines with 13 hand-rolled modal overlays → a shared `<Modal>` + `components/modals/`
  (2026-07-17).** The page held ~25 components, among them 8 blocking run prompts (`Recovery`, `EvolutionPrompt`,
  `RewardChoice`, `Shop`, `Acquisition`, `LeadChoice`, `SwitchIn`, `MoveReplacement`), each hand-rolling its own
  `<div className="modal-overlay">` + card + ARIA. Escape-to-close was the visible symptom: the map overlay had it,
  the prompts didn't, and nothing recorded whether that was a decision or an oversight.
  **It was a decision, and the refactor made it sayable.** Every prompt parks a server-side await (the run loop sits
  on a TCS until the player answers), so a prompt has no "close" to perform — dismissing one would strand the run
  with nothing to send back. Their negative buttons (DECLINE / CANCEL / SKIP / Leave) are *answers*, not dismissals.
  So the new `Modal` takes an explicit **`dismiss`** prop — `'blocking'` (no Escape, no backdrop close) vs
  `{ onEscape }` — and all 9 lifted overlays declare `'blocking'`. The escape rule itself lives in one place, the
  `useEscapeKey` hook, which `Modal` consumes and the map calls directly: the pinned map **is** the full-screen
  surface (a flex column whose children are its flex items), not an overlay-plus-card, so it can't share the
  wrapper's DOM without breaking `.encounter-map--pinned`'s layout — but it shares the rule.
  Lifted the 8 prompts + `BattleEndedOverlay` into `components/modals/`, `PartyStrip` into `components/`, and the
  duplicated HP-bar maths into `utils/hp.ts` (`hpState`/`hpPercent`). `LeadChoiceModal` and `SwitchInModal` rendered
  near-identical roster markup → one shared `PartyCard`. `RouteChoiceMap` also moved onto the wrapper (it needed
  `cardRef`, to keep its focus-the-first-offered-waypoint query scoped to its own card). **`BattleScreen.tsx` is now
  842 lines with zero hand-rolled overlays**; the map layer and control menus deliberately stay (the modals were the
  filed debt — the rest is a further split nobody has asked for).
  **Strictly behaviour-preserving, with one deliberate exception:** `aria-modal="true"` is now uniform across all
  modals, where before it was on 4 of them and absent from the rest — the same "accident of each component" the item
  was filed about, so it was normalised rather than faithfully copied. The CSS was left entirely alone (per the
  user's call), so every class/DOM shape the stylesheet and E2E selectors depend on is unchanged. Verified by
  typecheck + 1489 tests + a live drive of the app (route-choice renders and ignores Escape; the map closes on it).
  *Not done, and deliberately:* Escape→fires-the-decline (a Gen-1 B-cancel for the four prompts that have a negative
  action) is a genuine behaviour change and was ruled out of a refactor commit — **still open as an idea**, see
  `TODO.md`.

- **`Creature/` and `Creatures/` merged into one directory (2026-07-17).** Two sibling directories both declared
  `namespace creaturegame.Creatures` — `Creature/` held Creature, Attributes, BattleState, Party, StatStages and
  the stat calc; `Creatures/` held Biome, EncounterSelector, LearnsetMove(Selector). The split carried no meaning
  (nothing distinguished the two sets — they were not a singular/plural or entity/service boundary, just an
  accident of when each file was added) and it quietly violated the folder=namespace convention the test project
  follows perfectly. Fixed by `git mv`-ing all 9 files into `Creatures/` and deleting `Creature/`.
  **Zero code churn** — because both directories already shared the namespace, not one `using`, namespace
  declaration, or type reference changed anywhere in the solution; the csprojs are SDK-style and glob their
  sources, so no project file referenced the path either. Build stayed at 0 warnings and the suite at 1317 green,
  which is the whole proof: a move that changed nothing but where the files sit. Five stale doc anchors were
  retargeted (`ARCHITECTURE.md` ×2, `GAME_LOOP.md`, `STATE_MODEL.md`, `TODO.md`, plus the `pr-review` agent's
  engine-file glob). Historical anchors inside this archive were deliberately left as-written — the archive
  records what was true at the time.

- **`RunDirector.cs` was 1058 lines holding 9 types → one type per file (2026-07-17).** The file carried the
  director itself plus 6 `IRunEvent` classes (`BattleRunEvent`, `RecoveryRunEvent`, `LeadChoiceEvent`,
  `BiomeChoiceEvent`, `ShopRunEvent`, `RewardRunEvent`) and 2 static resolution helpers (`RewardResolution`,
  `AcquisitionResolution`) — every node kind the run can sequence, in the same file as the sequencer. Split the
  8 non-director types out one-per-file under `creaturegame/Combat/RunEvents/`, following the **`Combat/Ai/`
  precedent**: the subfolder keeps `namespace creaturegame.Combat`, so this is a pure file move with zero
  namespace or `using` churn at any call site. `RunDirector.cs` is now 332 lines and holds only the director
  (`ChooseNextEvent` / `Apply` / the node-plan roll) — the sequencing brain, which is what the file's name and
  its `GAME_LOOP.md §3` docs always claimed it was.
  The item's live duplication went with it: `BiomeChoiceEvent.PlayerAttackTypes` and
  `AcquisitionResolution.CreatureTypes` both walked `Type1`/`Type2` in different shapes (one an
  `IEnumerable<DamageType>` for a type-chart sweep, one an `IReadOnlyList<DamageType>` for the wire) — collapsed
  into a single `Creature.Types` property (slot order, nulls dropped) that both now read. The remaining
  `Type1`/`Type2` sites repo-wide are *per-slot tests* (`Type1 == moveType`) or live on `PokemonSpecies`, not
  the iterate-the-typing shape, so they were deliberately left alone.
  **Strictly behaviour-preserving** — no logic touched, only file boundaries; 1461 tests green, 0 build warnings.
  *Note for a future reviewer:* `RunLoop.cs` also holds ~28 types and is **fine** — a cohesive vocabulary file
  of small records. Don't let a type-count metric drive a split there.

- **Frontend linting: decided against (2026-07-17).** Filed as debt on the grounds that `ClientApp/` has no
  ESLint and no Prettier while the C# side has CSharpier pinned + hook-enforced. **The user ruled the frontend
  stays deliberately un-linted and un-formatted** — the asymmetry is intentional, so this is *closed by
  decision*, not deferred, and **must not be re-filed** because a future review notices the inconsistency.
  The frontend's one gate remains the typecheck (`tsc --noEmit` in the pre-commit hook + the `TypeScript` row
  in `test.ps1`). Codified in `DEV_STANDARDS.md` → *Coding Conventions* so it reads as a rule rather than an
  omission.

- **`RunDirector`'s 25-parameter constructor → a parameter object (2026-07-16).** The signature had grown to
  6 required args + 19 optional ones (mostly `Func<>` policy suppliers: `rewardSupplier`, `shopSupplier`,
  `draftSupplier`, `bossCatchSupplier`, `nodePlanFactory`, `checkEvolution`), and the web call site in
  `GameSessionManager` ran 45+ lines. The **injection pattern itself was never the problem** — web-layer policy
  into a policy-free core is what `GAME_LOOP.md` / `ENCOUNTER_DESIGN.md` argue for — only the delivery
  mechanism had hit its limit. Fixed by a new `creaturegame/Combat/RunDirectorOptions.cs` record carrying the
  whole optional/supplier surface (each property documenting what its absence implies); the constructor now
  takes the 6 genuinely required args positionally (player, enemySupplier, typeChart, both inputs, movePool)
  plus an optional `RunDirectorOptions?`, so omitting it *is* the legacy endless chain. A new node kind or
  acquisition channel now adds a property instead of a positional parameter + another default.
  **Strictly behaviour-preserving** — every option maps 1:1 to the parameter it replaced with identical
  defaults (verified parameter-by-parameter, and across all 36 call sites, by `pr-review`). The web
  composition root + 35 test call sites were updated (the tests scripted, then swept by hand for
  comment-attribution damage). Verified by the full suite (1314 .NET / 147 Vitest / tsc clean) **plus a live
  Playwright run — 26/26** against the real composition root, which is what a mis-mapped option (a wrong
  wiring still compiles) would actually have caught.

- **Flaky full-`Battle` tests (2026-06-07).** Swept and deterministically fixed the three intermittent
  flakes (all unseeded `Battle` RNG + un-pinned rolls): `RestContractTests` (random crit one-shot the
  player before the forced-sleep turn → `NoVarianceNoCritHitRules` + seed), `TransformRevertsWhenTheBattleEnds`
  (un-pinned Defense let the +1-priority enemy randomly OHKO before Transform; plus a false premise — Normal
  move vs Ghost was 0× → switched enemy to Water + pinned Defense + seed), and
  `BattleIntegrationTests.PicksSpecificMoveByIndex` (seeded + `AlwaysHitRules`). Verified by a 60× full-suite
  confidence sweep: **0 failures / ~49k test executions.**

- **`AttackAction` god-object → `IMoveEffect` registry (Architecture Review #7, highest-leverage item;
  2026-06-13).** The ~320-line `switch (attack.Effect)` in `AttackAction.TryApplyMoveEffect` was extracted
  into `creaturegame/Combat/MoveEffects.cs`: an `IMoveEffect` interface + `MoveEffectContext` + one sealed
  class per post-damage effect (the 20 cases — Haze, Flinch, LeechSeed, Binding, PayDay, Recoil, Disable,
  Counter, Mist, Reflect, LightScreen, FocusEnergy, Heal, Mimic, Transform, Conversion, Rest, Substitute,
  Splash, Confuse), routed by `MoveEffects.For(effect)` **derived from the `All` list** — exactly mirroring
  the proven `ILockInMechanic` / `LockInMechanics.For(effect)` pattern (Review #6a). `TryApplyMoveEffect` is
  now a 3-line lookup. Counter (the only damage-dealing effect) reaches the centralized `DealDamageToTarget`
  through a `MoveEffectContext.DealDamage` delegate, so the Substitute-soak / Bide-accumulation /
  Counter-recording stay in one place. Also renamed the file `IBattleAction.cs` → `AttackAction.cs` (its
  primary type) and split the small `IBattleAction` interface into its own `IBattleAction.cs` (part of the
  Review #7 "filename ≠ type" item; `GameDbContext.cs` → `MovesDbContext.cs` + `PokemonDbContext.cs` followed
  on 2026-06-14). Pure structural refactor, no
  behaviour change — seam-reviewer **CLEAN** (0 blockers / 0 advisories; diffed all 20 arms 1:1), csharpier
  clean, **867/867 .NET tests green**. `ARCHITECTURE.md §2.4/§2.11/§3` updated to match.

- **`bag.ts` re-encoded the engine's effect registry (2026-07-04).** The frontend `USABLE_CATEGORIES` set
  (which hardcoded which `ItemCategory`s are usable in battle) is gone; the backend now projects a
  server-computed `UsableInBattle` boolean onto `BagItemView` (from `ItemEffects.For(category)`), and the
  client filters the bag menu on that flag. Single source of truth — when Ball/Revive get effects, only the
  registry changes and the menu follows. Mirrors the `RestoresPpAllMoves` field-projection precedent.

- **Event wire contract was guarded by name but not by field (2026-07-16).** Every `BattleEvent` crosses three
  layers by hand (record in `BattleEvents.cs` → hand-listed anonymous object in
  `SignalRBattleEventEmitter.MapEvent` → `case` arm in `timeline.ts`). The *name* leg was already generically
  guarded (`EveryBattleEventMapsToItsOwnNamedClientEvent` + `EveryBattleEventHasATimelineArm`), but the *field*
  leg was ~21 bespoke `*_Projection_Carries*` tests — so **adding a field to an existing event record passed
  every gate while the field silently never reached the client** (the recurring `MoveInfo` trap; see the
  `web_event_field_projection_gap` history). Closed by
  `WebEventContractTests.EveryBattleEventProjectsAllOfItsFields`: reflects over every concrete event, asserts
  each record property appears on the projected payload, and **recurses into nested payload records** (all six
  reachable from an event: `MoveInfo`, `PartyMemberInfo`, `BiomeOption`, `RegionMapBiome`, `ShopOfferItem`,
  `StatBlock`) **and into every variant of a union family** (`RewardOption` → Item/Gold/Heal — each variant is
  hand-mapped in its own `ProjectRewardOption` arm, so each is its own place for a field to go missing). The
  probe instantiator fills collections from `ProbeElementTypes` — *the single source the checker also reads*,
  so the two can't disagree about what's in the list — which is what makes those inline
  `Select(… => new { … })` arms actually get exercised rather than skipped over an empty list. A projection
  that filters or reorders a probed list is rejected outright (the probe fills all-or-nothing, so a non-empty
  short array is *provably* a filter, never a depth-cap artifact). Deliberate renames/omissions register in
  `ProjectionExceptions` with a reason (only one: `TurnStarted.PlayerMoves` → `Moves`); a registered omission
  is asserted *absent*, so the list can't rot into a blanket mute. Verified by mutation at each level — an
  unprojected field on `BattleEnded`, on nested `MoveInfo`, and on the union's `ItemRewardOption` each fail
  with the exact path (e.g. `RewardChoiceOffered.Options[ItemRewardOption].UnionProbeField`). Found no live
  drops: the projection was already complete. The per-event one-off tests were **kept** — they pin
  *values / semantics* (string-cast enums, the PascalCase rarity the TS union needs, HP-0-means-fainted),
  which the generic test does not check; their doc comments were corrected, since several justified themselves
  on the empty-list gap this closed. **Process note:** the first `pr-review` caught the abstract/union family
  going unprobed — the claim had outrun the coverage — which is why the guard now probes union variants at all.
  *Still manual:* the TS leg (client type + `timeline.ts` mapping) is not machine-checked; only its *presence*
  is (`EveryBattleEventHasATimelineArm`).

- **TypeScript was never typechecked by any gate (2026-07-16).** `tsc` ran only in `npm run build`, which no
  gate invokes; Vitest transpiles via esbuild, which **strips types without checking them**; and the
  pre-commit hook gated C# only. So `tsconfig`'s `strict` + `noUnusedLocals`/`noUnusedParameters` were
  configured but unenforced, and a TS type error passed every gate and landed. Closed by the **TS mirror of
  the `.cs` → tests rule**: `.githooks/pre-commit` runs `npm run typecheck` (`tsc --noEmit`, ~6s) when
  `.ts`/`.tsx` is staged and **blocks on failure** (with an explicit block + `npm install` hint when
  `node_modules` is missing, so the gate can never silently no-op); `test.ps1` reports it as its own
  `TypeScript` row ahead of Vitest, so a type break reads as itself rather than as a confusing test failure.
  Verified by mutation at both levels: a real type error fails `npm run typecheck` (exit 2) and the hook
  blocks (exit 1). **Scope widened during the work (user-approved):** `tsconfig` covered only `src`, leaving
  `e2e/` — including the 240-line `helpers.ts` — completely unchecked, with **3 latent errors** found the
  moment it was included: an unused local (`cadence.spec.ts`), a type re-exported without `export type`
  (illegal under `isolatedModules`, `helpers.ts:240`), and `.at()` used against an ES2020 `lib`
  (`helpers.ts:125`). All three fixed; `include` is now `["src", "e2e"]` and `lib` raised to `ES2022`
  (additive — it can only add known-good types, never invalidate existing `src` code). **Keep `e2e` in
  `include`** — dropping it silently un-guards the test infrastructure. Full suite green at close: .NET 1314,
  Vitest 147, Playwright 26, `npm run build` clean.

---

## XP, Level-Up & the Endless Battle Chain — DONE (2026-06-09 → 10)

The "XP & progression" milestone: a live, honest in-battle XP loop, a Gen 1 stat-gain panel on
level-up, and a minimal endless run loop (one persistent creature, endless wild encounters). All
E2E specs landed in the 2026-06-10 pass.

### XP & Level-Up — finish the in-battle loop ✅
Engine emits `ExperienceGained(CreatureName, Amount)` before any `LeveledUp`; `LeveledUp` carries the
level-relative XP pair (`XpThisLevel`/`XpToNextLevel`) + post-level `StatBlock`; `TurnStarted` carries
`PlayerXpThisLevel`/`PlayerXpToNextLevel` (the hardcoded `100` is gone). `Battle` drives level-ups one
at a time (`AddExperience` + `while (TryLevelUp())`) — the seam the deferred move-learning reuses.
`Creature` exposes `XpThisLevel`/`XpToNextLevel` (full-bar at cap) + `StatSnapshot()`.
- **Frontend:** honest fill — `XP_GAIN` fills toward the level boundary (capped at the max); each
  `LeveledUp` resets + refills the leftover via `XP_SET`; the slam-to-full removed. `useBattleHub.ts`
  dispatches the new XP fields into `playerXp`/`playerXpToNext`.
- **Level-up stat panel (Web-UI Polish item, done here):** Gen 1 stat-gain box (HP/ATTACK/DEFENSE/
  SPECIAL/SPEED with +gains and new totals) on `LeveledUp`; engine sends per-stat `StatGains`
  (before/after `TryLevelUp` delta). Plays the level-up fanfare (`playLevelUpSound` → `Audio.playLevelUp`);
  the panel sits bottom-right above the battle menu and stays until the player's next input
  (`useBattleHub.dismissLevelUp`) — no auto-hide.
- **Tests:** backend — `TurnStarted` carries correct level-relative XP; a multi-level award emits
  `ExperienceGained` then the right `LeveledUp` sequence (intermediates overshoot, client caps, final is
  partial); the `LeveledUp` stat block matches `CalculateStats` at the new level. E2E — `level-up.spec.ts`:
  a low-level win fills XP, shows "grew to level N!" + the stat panel + the fanfare, panel persists until input.
- Scope decided 2026-06-09: live XP display + honest multi-level animation + stat-growth surfacing.
  Out of scope (own sections): EV gain, level-up move learning. Enemies wild ⇒ XP `a`-multiplier = 1.
  `/audit` PASS-WITH-ADVISORIES (all resolved). No new data/schema; did not need the Game Loop.

### Endless Battle Chain (minimal run loop) ✅
One persistent player runs battle after battle (fresh wild enemy each time) until it faints; HP/PP/XP/Level
carry, transient resets, `RunEnded` drives a game-over summary. A deliberate **minimal slice** of the
deferred *Game Loop & Progression* — no catch/party/save/evolution/version-filtering.
- **Persistence (free — the permanent/transient split):** reusing one player `Creature` across consecutive
  `Battle` instances carries HP, PP (`PowerPointsCurrent`), Experience, Level; status / stat-stages /
  confusion reset per `Battle.StartFightAsync`. No between-battle heal. Locked by
  `ConsecutiveBattles_OnOnePlayer_PersistHpPpXpLevel_AndResetTransientState`. See `STATE_MODEL.md §2`.
- **Cross-encounter status carry (2026-06-10):** major status now carries across encounters — `BattleRunner`
  snapshots the player's status after each win and re-applies it into the next `Battle` (`playerEntryStatus`),
  with `IBattleRules.CarryStatusOutOfBattle` deciding the out-of-battle transform (Gen 1: Toxic→Poison).
  Volatiles still reset per battle. Sleep carries its counter; Freeze persists.
- **Encounter factory (web):** `EncounterFactory` (`CreatePlayerSetupAsync` + `CreateEnemyAsync`) —
  `BuildCreature` moved out of `GameController`; builds every enemy. Enemy level = player's **current**
  level ± 3, BST-matched; `CreateEnemyAsync` takes an optional seedable `IRandomSource` (defaults to system RNG).
- **Run loop:** `BattleRunner` (core) drives the chain; `GameSessionManager` runs it instead of one `Battle`.
  New terminal `RunEnded(BattlesWon, FinalLevel, FinalCreatureName)` event (mapped in both emitters). Abandon
  path (client disconnect) throws out of the loop **before** `RunEnded` — pinned by a test.
- **Frontend:** `BattleEnded` (win) → non-terminal intermission ("A new challenger approaches!"), bars persist,
  next `BattleStarted` resumes; `BattleEnded` (loss) → no-op (`RunEnded` owns the end). `RunEnded` → game-over
  screen with run summary (wins, final level); `state.winner` → `state.battlesWon`.
- **Tests:** `BattleRunnerTests` (chain → `RunEnded`; abandon emits none); `RunEnded` auto-covered by
  `WebEventContractTests`; E2E `endless-chain.spec.ts` (win → "A new challenger approaches!" + fresh enemy +
  carried XP; QUIT → title; play-to-faint → "Run over"/game-over). Matched coin-flip battles handled by the
  `reachLog` restart-on-loss helper, not a seed. `battle.spec.ts`/`helpers.ts` updated off the removed `"wins!"` line.
- `/audit` PASS-WITH-ADVISORIES (resolved).
- **Two items intentionally left open (tracked in `TODO.md`, not done here):** (1) full per-run seed through
  `GameSessionManager` → `BattleRunner`/`EncounterFactory` — Tech Debt #3 (needs a run-seed concept); the
  factory already accepts a seedable source, so it's just wiring. (2) a deterministic test that a double-faint
  (mutual end-of-turn DoT) counts as a loss (`break` before the win-count) — behavior is correct, no
  deterministic test yet (Known Gaps).
  **Update (2026-06-12):** (2) is DONE — `BattleRunnerTests.Runner_DoubleFaintFromEndOfTurnPoison_CountsAsLoss_NotAWin`.
  The rules-RNG half of (1) is also closed — `BattleScenario.Seed(...)` is now fully deterministic
  (`SeededRulesTests`); only the *production* web run-seed wiring remains open (`TODO.md` Tech Debt #3).

---

## Generation Abstraction — Stat Selection ✅ DONE

- [x] `IBattleRules.GetOffensiveStat(Creature, AttackType)` and `GetDefensiveStat(Creature, AttackType)` added
- [x] `Gen1BattleRules`: Physical → Attack/Defense; Special → Special (combined Gen 1 stat)
- [x] `DamageCalculator`: duplicated crit/non-crit stat selection block collapsed; stat reads delegated to rules
- [x] `AlwaysHitRules` and `AlwaysCritRules` test helpers updated to implement new methods
- [x] 2 new tests — `DamageCalculator_UsesOffensiveStatFromRules`, `DamageCalculator_UsesDefensiveStatFromRules` (124 total passing)

---

## Learnset System — Initial moveset from learnsets ✅ DONE (2026-06-02)

Generation separation: learnsets are **data**, not a battle rule, so no new seam. The Gen 1
decision (filter to `red-blue` level-up moves) is isolated in the importer & commented (like
`Gen1TypeSlots`); rows are tagged with a `Generation` column; runtime filters by a single
`GameController.ActiveGeneration` constant — no generation branching in logic.

- [x] `PokemonLearnset` model (`Id`, `SpeciesId` FK→`PokemonSpecies`, `MoveId` logical
  cross-DB ref to `moves.db`, `LearnLevel`, `Generation`) + index `(SpeciesId, Generation,
  LearnLevel)`; `AddPokemonLearnset` migration on `PokemonDbContext`. Lives in `pokemon.db`.
- [x] Import: `LearnsetMapper.ExtractGen1Learnset` (pure, testable) filters the already-fetched
  `/pokemon/{id}` moves array to `red-blue` + `level-up`, parses MoveId from the URL, keeps
  `MoveId <= 165`, lowest level on repeats; `PokemonImport.ImportLearnset` persists idempotently
  (clear-then-insert). Re-imported → **989 rows across all 151 species** (verified via MCP).
- [x] `LearnsetMoveSelector.Select(strategy, …)` (core, gen-agnostic, `IRandomSource`-seamed):
  - **`CanonicalLatest`** (player) — deterministic, the 4 highest-level moves ≤ level.
  - **`WeightedSmart`** (enemy) — semi-random, semi-intelligent: weight = power (or flat 60 for
    Fixed/OHKO/etc., 35 for status) × 1.5 STAB × recency nudge; **always force-picks the top
    damaging move** (never all-status), fills the rest by weighted draw without replacement so
    same-species/level enemies vary. Deliberate precursor to the planned `IMoveEvaluator`.
- [x] Wired into `GameController.BuildCreature` (player = Canonical, enemy = WeightedSmart),
  replacing the random-4 block; graceful fallback to random if a species has no learnset rows.
- [x] Tests (18 new, 156 total): `LearnsetImportTests` (filter/range/dedup/order, ×5),
  `LearnsetMoveSelectorTests` (canonical, level-gating, ≤4 returns-all, always-damaging, seeded
  determinism, statistical STAB/power bias, ×7), `MigrationTests` learnset schema + round-trip (×2),
  `LearnsetIntegrationTests` (DB round-trip → EF query → selection: canonical legality, low-level
  gating, **generation filter isolates gens**, WeightedSmart legal + always-attack, ×4).
- [x] E2E: committed Playwright spec `e2e/learnset.spec.ts` — Bulbasaur@50 move menu equals the
  canonical 4 (RAZOR LEAF/GROWTH/SLEEP POWDER/SOLAR BEAM); also verified live via Puppeteer
  (enemy Paras used SCRATCH — legal, attacking — battle resolves).

---

## Gen 1 Attack Behavior Coverage — Batches 1–17 ✅ COMPLETE (2026-06-07)

Proved **every Gen 1 attack does what it sets out to do** when given to a Pokémon and used in
battle, in **batches of 10 moves**, via parametrized "effect contract" tests (`[Theory]` +
`[InlineData]`). Real move rows come from the live `moves.db` (`MovesFixture`); the
`MoveScenario` harness gives the move to a creature and runs one `AttackAction`. Moves 1–165 are
all covered (including the deferred Transform/Conversion mutation batch). Final suite: **813 .NET
+ 37 Vitest**.

### Test layout: capability classes, not batch files
Tests are organised by **what the move does**, not the batch it arrived in:
`tests/.../Integration/Gen1Attacks/` — `DamageContractTests`, `StabAndTypeEffectivenessContractTests`,
`CriticalHitContractTests`, `MultiHitContractTests`, `SecondaryStatusContractTests`,
`PhysicalSpecialSplitContractTests`, `OneHitKoContractTests`, `TwoTurnMoveContractTests`,
`StatStageMoveContractTests`, `BindingContractTests`, `UniqueMoveEffectContractTests`, over a shared
`Gen1MoveContract` base. **Covering a new batch means adding `InlineData` rows to the matching
class** and creating a new class only when a move introduces a genuinely new mechanic.

### Batch 1 (moves 1–10) ✅ DONE (2026-06-03)
pound, karate-chop, double-slap, comet-punch, mega-punch, pay-day, fire-punch, ice-punch,
thunder-punch, scratch. **+49 test cases (228 total).**
- Harness built once for all batches: `TestSupport/MovesFixture` (live DB loader),
  `MoveScenario`/`TestCreatures`, shared `RecordingEmitter` (deduped the 3 copies), and the
  deterministic rules doubles (`NeverHitRules`, `ForceSecondaryRules`, `NoVarianceNoCritHitRules`,
  `FixedMultiHitRules`) on `DelegatingBattleRules`.
- Contracts: damage, PP decrement, accuracy/miss, secondary status (burn/freeze/paralysis incl.
  miss + already-statused), Gen-1 special-by-type (the punches are Special), high-crit rate,
  STAB ~1.5×, type-effectiveness scaling, multi-hit count, Pay Day coins.
- **Two engine features implemented** (both behind the gen seam per `GENERATION_SEAMS.md §5.0`):
  - **Multi-hit (2–5)** — `MoveEffect.MultiHit`, `IBattleRules.RollMultiHitCount` (Gen 1 weighted
    2/3 = 3/8, 4/5 = 1/8), per-hit crit/variance, stop-on-faint, `MultiHitCompleted` event +
    "Hit N times!" line. Maps double-slap/comet-punch/fury-attack/pin-missile/barrage/
    fury-swipes/spike-cannon. Verified live (Clefairy Double Slap → "Hit 2 times!").
  - **Pay Day** — `MoveEffect.PayDay`, `IBattleRules.PayDayCoinMultiplier` (Gen 1 = 2× level),
    `CoinsScattered` event ("Coins scattered everywhere!"). No economy yet — the mechanic is the event.

### Batch 2 (moves 11–20) ✅ DONE (2026-06-03)
vice-grip, guillotine, razor-wind, swords-dance, cut, gust, wing-attack, whirlwind, fly, bind.
**248 total.** All mechanics below were already implemented in the engine — this batch is
**coverage only, no new engine code** — and each test drives the real `AttackAction` path (the only
substitutions are RNG-gated rolls through the `IBattleRules` seam doubles).
- Reused contracts (rows added to existing classes): damage, PP decrement, accuracy/miss, STAB
  (added a Flying mover), type-effectiveness scaling (Flying super-effective vs Grass/Fighting),
  physical/special-by-type (generalised to a category theory over Normal/Fighting/Flying/Fire/Ice/Electric).
- New capability classes for first-seen mechanics:
  - **One-hit KO** (guillotine) — deals full-HP damage & fells; **fails** (not misses) when user
    level < target level (Gen 1 rule); misses on accuracy fail.
  - **Two-turn charge** (razor-wind, fly) — turn 1 emits `ChargingUp` with no damage / no
    `MoveUsed`; turn 2 lands & deals damage; PP spent once; misses on the release turn. Razor Wind's
    high-crit verified on the release turn vs Fly. **Plus a full-`Battle` test** proving the release
    turn is auto-driven from `ChargingMove` without re-asking input (`CountingInput.CallCount == 1`).
  - **Self-targeting stat-stage** (swords-dance) — +2 user Attack, no damage, `StatStageChanged`
    targets the user.
  - **Binding** (bind) — damages + traps 2–5 turns (`BindingStarted`).
  - **No-op status move** (whirlwind) — announced but no combat effect yet (switch/flee has no
    home until the Game Loop); Gen 1 −6 priority pinned, so the gap is documented not silent.
- Harness: added `MoveScenario.UseRepeated(move, turns)` — runs consecutive real `AttackAction`s on
  one reused `PokemonAttack` wrapper (exactly what `Battle` feeds on a two-turn release), so PP +
  two-turn state carry across turns like a real battle.

### Batch 3 (moves 21–30) ✅ DONE (2026-06-03)
slam, vine-whip, stomp, double-kick, mega-kick, jump-kick, rolling-kick, sand-attack, headbutt,
horn-attack. Two genuine engine features this batch (both behind the gen
seam per `GENERATION_SEAMS.md §5.0`); everything else coverage-only over real `AttackAction` paths.
- Reused contracts (rows added): damage/PP/miss; STAB (first **Special-type** mover, vine-whip;
  + Fighting jump-kick); type-effectiveness (Grass→Water, Fighting→Normal); physical/special split
  (vine-whip→Special, the Fighting kicks + Normal movers→Physical, sand-attack→Undefined).
- New capability classes (engine already supported these — coverage only):
  - **Flinch** (`FlinchContractTests`: stomp, rolling-kick, headbutt) — sets the flag on hit, never
    on miss, **plus a full-`Battle` test** where a faster flincher locks the target out
    (`FlinchBlocked`, target never emits `MoveUsed`).
  - **Foe stat-drop** (sand-attack) — −1 foe Accuracy, folded into `StatStageMoveContractTests`
    alongside swords-dance's self-buff.
- **Two new engine features:**
  - **Fixed-count multi-hit** — `int? Attack.MultiHitCount` column (+`AddMoveMultiHitCount`
    migration); `AttackAction` uses `MultiHitCount ?? RollMultiHitCount()`. The fixed count is move
    data; the variable 2–5 distribution stays the gen rule. double-kick mapped (Effect=MultiHit,
    count 2). Twineedle/bonemerang reuse the mechanism in their batches.
  - **Jump Kick crash damage** — `MoveEffect.Crash` + `IBattleRules.CalculateCrashDamage`
    (Gen 1 = flat 1 HP) + `CrashDamage` event (console + SignalR emitters + `timeline.ts`
    "kept going and crashed!"). Applied on the accuracy-miss branch. jump-kick mapped. *Deferred
    edge:* Gen 1 also crashes on a Ghost immunity (Fighting→Ghost 0×) — documented, not handled.
- Data: full `PokeApiConnector` re-run (authoritative path) applied the migration + new mappings;
  verified double-kick MultiHitCount=2 / jump-kick Effect=Crash via MCP.

### Batch 4 (moves 31–40) ✅ DONE (2026-06-03)
fury-attack, horn-drill, tackle, body-slam, wrap, take-down, thrash, double-edge, tail-whip,
poison-sting. Two genuine engine features (both behind the gen seam);
everything else coverage-only over real `AttackAction` paths.
- Reused contracts (rows added): damage/PP/miss; OHKO parametrized (guillotine **+ horn-drill**);
  binding parametrized (bind **+ wrap**); secondary status (body-slam Paralysis, poison-sting Poison);
  variable multi-hit (fury-attack); foe stat-drop (tail-whip −1 Defense, with sand-attack);
  physical/special split (Normal movers + poison-sting→Poison Physical, tail-whip→Undefined);
  STAB/effectiveness (first **Poison** mover poison-sting; Poison→Grass 2×).
- **Two new engine features:**
  - **Recoil** (take-down, double-edge) — `MoveEffect.Recoil` + `IBattleRules.CalculateRecoilDamage`
    (Gen 1 = ¼ damage dealt, min 1); `AttackAction` reuses the existing `RecoilDamage` event (already
    wired through console/SignalR/`timeline.ts`). Recoil applies even on a KO. → `RecoilContractTests`.
  - **Rampage** (thrash) — multi-turn lock + self-confusion, mirroring the two-turn pattern:
    `BattleState.RampageTurnsRemaining`/`RampageMove` (+`Creature` props), `MoveEffect.Rampage`,
    `IBattleRules.RollRampageTurns` (Gen 1 = 2–3). `Battle` force-selects the locked move (no input
    consulted); when the lock expires the user confuses itself (reuses `ConfusedTurns` +
    `ConfusionStarted`). Lock decrements even on a miss. → `RampageContractTests` incl. a full-`Battle`
    test (turn 2 not consulted; player ends up confused). petal-dance reuses this in its batch.
- Data: full `PokeApiConnector` re-run; verified take-down/double-edge→Recoil, thrash→Rampage,
  wrap→Binding, horn-drill→OHKO, body-slam→Paralysis, poison-sting→Poison via MCP. No schema change.

### Batch 5 (moves 41–50) ✅ DONE (2026-06-04)
twineedle, pin-missile, leer, bite, growl, roar, sing, supersonic, sonic-boom, disable.
 One major new engine feature (Disable) and a cross-cutting Gen 1
**move-type correction pass**; everything else coverage-only over real `AttackAction`/`Battle` paths.
- Reused contracts (rows added): damage/PP/miss (pin-missile, bite, twineedle); variable multi-hit
  (pin-missile) + fixed-2 multi-hit (twineedle); foe stat-drop (leer −1 Defense, growl −1 Attack);
  flinch (bite); secondary status (twineedle 20% Poison); no-op switch move (roar, folded with
  whirlwind, −6 priority pinned); physical/special split (bite now Normal/Physical, +comment fixes).
- New capability classes: **`StatusMoveContractTests`** (sing → Sleep, supersonic → Confuse; pure
  status moves that afflict without damage, nothing on a miss) and **`FixedDamageContractTests`**
  (sonic-boom deals exactly 20 regardless of stats/type, incl. immunities; can miss).
- **Two genuine engine features this batch:**
  - **Disable** (`MoveEffect.Disable`) — full mechanic: `BattleState.DisabledMove` +
    `DisableTurnsRemaining` (+ `Creature` delegating props + `CanSelectAnyMove`),
    `IBattleRules.RollDisableTurns` (Gen 1 = 1–7), `AttackAction` picks a random PP-bearing foe
    move and locks it (fails if one's already disabled). Enforced at **move-selection time**:
    `TurnContext.DisabledMove`, `RandomMoveInput`/`AutoSelectInput`/`SignalRInput` skip it, and
    `Battle` Struggles when it's the only move; the counter ticks down in `StatusResolver` and
    re-enables. New `MoveDisabled`/`MoveReEnabled` events wired through console + SignalR +
    `timeline.ts` (+ Vitest). UI greys the locked move. Covered by `DisableContractTests` incl. a
    **full-`Battle`** lock→Struggle→re-enable test.
  - **Twineedle** — mapped to the existing fixed-2 multi-hit mechanism + its 20% poison secondary.
- **Gen 1 move-type correction pass** — PokeAPI returns each move's *modern* type, but four Gen 1
  moves were retyped later: **karate-chop** (→Fighting), **gust** (→Flying), **sand-attack**
  (→Ground), **bite** (→Dark). The importer now restores their RBY type (all Normal) right after the
  type parse. *(Superseded in batch 6 by the `past_values` resolver — the hardcodes were removed.)*

### Batch 6 (moves 51–60) ✅ DONE (2026-06-04)
acid, ember, flamethrower, mist, water-gun, hydro-pump, surf, ice-beam, blizzard, psybeam.
 First special-attack-heavy batch; introduced a **data-driven Gen 1
move-data resolver**, the **Mist** mechanic, and a gen-seam cleanup.
- New capability classes: **`SecondaryEffectContractTests`** (damaging moves whose secondary is a
  stat drop (acid → −1 foe Defense) or confusion (psybeam)) and **`MistContractTests`**.
- **`past_values` resolver (the big one)** — PokeAPI returns each move's *modern* stats; Gen 1 often
  differed. The importer now reads PokeAPI's `past_values` array and applies the **earliest**
  recorded power/accuracy/pp/effect_chance/**type** as the Gen 1 value — one data-driven source, no
  per-move hardcoding. **Supersedes batch 5's hardcoded type switch** and fixed special-move powers
  (Flamethrower/Surf/Ice Beam 95, Hydro Pump/Blizzard 120), Blizzard acc 90, double-edge → 100. One
  documented exception: **acid** (Gen 1 lowers Defense at 33%) is a manual override (empty `past_values`).
- **Mist** (`MoveEffect.Mist`) — `BattleState.HasMist`; `AttackAction` sets it + emits `MistApplied`;
  `TryApplyStatEffect` blocks foe-induced stat drops on the holder (emits `StatDropBlocked`).
- **Gen-seam cleanup (§5.0):** acid's chance-based stat drop on a damaging move now routes through
  `IBattleRules.GetSecondaryEffectChance` (new `SecondaryEffectKind.StatStage`).

### Batch 7 (moves 61–70) ✅ DONE (2026-06-04)
bubble-beam, aurora-beam, hyper-beam, peck, drill-peck, submission, low-kick, counter, seismic-toss,
strength. One new mechanic (Counter), two new coverage contracts (Recharge,
LevelBased), a **full Gen 1 secondary-chance override sweep**, and submission→Recoil.
- New capability classes: **`RechargeContractTests`** (hyper-beam), **`LevelBasedDamageContractTests`**
  (seismic-toss = user level), **`CounterContractTests`** (2× last Normal/Fighting damage; full-`Battle`).
- **Counter** (`MoveEffect.Counter`) — `BattleState.LastDamageTaken` + `LastDamageType`; `AttackAction`
  returns 2× when the last hit was Normal/Fighting; −5 priority resolves it after the opponent's hit.
  Fixed/level-based/self damage isn't recorded ⇒ not counterable (documented simplification).
- **Full Gen 1 secondary-chance override sweep** (layer 2, per `DATA_IMPORT.md` §5.5) — verified Gen 1
  values set in one commented importer block: **acid** 33% Def, **aurora-beam** 33% Atk, **bubble-beam**
  33% Spe, **bite** 10% flinch, **low-kick** 30% flinch, **poison-sting** 20% poison. Rest audited unchanged.

### Batch 8 (moves 71–80) ✅ DONE (2026-06-04)
absorb, mega-drain, leech-seed, growth, razor-leaf, solar-beam, poison-powder, stun-spore,
sleep-powder, petal-dance. Almost entirely a coverage batch, plus the
**Gen 1 type-immunity seam** and one new event.
- New capability classes: **`DrainContractTests`**, **`LeechSeedContractTests`**, **`ImmunityContractTests`**.
- **Type-immunity seam** — new `IBattleRules.CanReceiveStatus` (Poison-type can't be poisoned, Fire-type
  can't be burned, Normal-move can't paralyze a Normal-type = the Body Slam quirk) and `CanBeLeechSeeded`
  (Grass immune). Moves that bypass the damage calc (fixed/level-based/OHKO/Super Fang) and Counter now
  respect 0× type immunity via `ITypeChart`. New `MoveHadNoEffect` event. Closes the deferred body-slam +
  Ghost edges.
- **Data fix:** Gen 1 **growth** raises Special (not Attack) — importer layer-2 override.
- Latent fidelity bug fixed: `SonicBoomIgnoresTheTypeMatchup(Ghost)` corrected to "ignores effectiveness
  *scaling*" + a Ghost-immunity test.

### Batch 9 (moves 81–90) ✅ DONE (2026-06-04)
string-shot, dragon-rage, fire-spin, thunder-shock, thunderbolt, thunder-wave, thunder, rock-throw,
earthquake, fissure. Pure coverage batch + one small engine extension and
two data fixes — no new mechanics.
- **Engine extension:** the batch-8 type-immunity guard now also covers **pure-status moves** — a status
  move whose type is 0× against the target has no effect (Thunder Wave is Electric ⇒ Ground immune).
- **Data fixes (layer-2):** string-shot Speed −2 → −1; thunder paralysis 30% → 10%.
- **Self-audit fixes:** removed a dead Counter Ghost-immunity branch; added `SecondaryChanceDataContractTests`
  to pin the importer's layer-2 secondary-chance overrides (previously a re-import could silently regress).

### Batch 10 (moves 91–100) ✅ DONE (2026-06-05)
dig, toxic, confusion, psychic, hypnosis, meditate, agility, quick-attack, rage, teleport.
 One genuine new mechanic (Rage) + one Gen 1 data fix (Toxic → BadPoison).
- New capability classes: **`RageContractTests`** and **`PriorityMoveContractTests`** (quick-attack).
- **Rage (new mechanic, behind the gen seam):** `MoveEffect.Rage`; lock-in mirrors rampage/two-turn. On
  hit, a raging creature gains Attack by `IBattleRules.RageAttackStagesPerHit` (Gen 1 = 1) once per
  connecting attack. Full-`Battle` test asserts the **quirk** (Attack rises once per *hit received*).
- **Data fix:** Gen 1 **toxic** → importer layer-2 `BadPoison` override; pinned. Verified via MCP.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers); all 3 advisories fixed before commit.

### Infra cleanup (2026-06-05, between batches 10 and 11)
Extracted `Battle.SelectMoveAsync` from the duplicated 4-level player/enemy move-selection ternary
(two-turn → rampage → rage → struggle/input) so lock-precedence lives in one place; removed the
unreachable `"bad-poison"` importer arm. Behavior-preserving.

### Batch 11 (moves 101–110) ✅ DONE (2026-06-05)
night-shade, mimic, screech, double-team, recover, harden, minimize, smokescreen, confuse-ray,
withdraw. Two new mechanics (Recover, Mimic) + a correctness fix to the
type-immunity guard. Two new events (`Healed`, `MimicLearned`).
- New capability classes: **`HealContractTests`** (Recover ½ max HP) and **`MimicContractTests`**.
- **Recover (`MoveEffect.Heal`):** heals `MaxHP × IBattleRules.RecoverHealFraction` (Gen 1 = ½); emits
  `Healed` with the *actual* amount.
- **Mimic (`MoveEffect.Mimic`):** copies a random foe move by swapping `PokemonAttack.Base`; revert lives
  in **`Creature.ResetBattleState`** (so Haze's mid-battle reset can't orphan it) — the transient swap
  never leaks into the permanent `MoveSet`.
- **Correctness fix (the immunity seam):** the batch-9 pure-status type-immunity guard now only fires for
  **foe-directed** moves, so a Normal-type self-buff/Recover is no longer wrongly blocked against a Ghost.
  Counter (BaseDamage 0 but foe-directed) stays inside the guard — a failing test caught that.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers, 4 advisories); all fixed, incl. a **real bug**
  (Haze+Mimic permanent-MoveSet leak).

### Batch 12 (moves 111–120) ✅ DONE (2026-06-05)
defense-curl, barrier, light-screen, haze, reflect, focus-energy, bide, metronome, mirror-move,
self-destruct. Mechanic-heavy: **five** new mechanics (Reflect, Light Screen,
Focus Energy, Bide, Mirror Move). Three new events (`ScreenApplied`, `FocusEnergyApplied`, `BideStoring`).
- New capability classes: **`ScreenContractTests`**, **`FocusEnergyContractTests`**, **`BideContractTests`**,
  **`MirrorMoveContractTests`**.
- **Reflect / Light Screen:** double the holder's Defense / Special vs the matching damage via a new
  `DamageCalculator` `screenDefenseMultiplier` param (crits bypass screens, Gen 1). Factor on
  `IBattleRules.ScreenDefenseMultiplier` (Gen 1 = 2).
- **Focus Energy:** the Gen 1 *bug* (quarters crit instead of ×4) lives in `Gen1BattleRules.GetCritChance`;
  test pins the ÷4 quirk.
- **Bide:** lock-in; release deals `accumulated × IBattleRules.BideDamageMultiplier` (Gen 1 = 2),
  typeless/never-miss. **Accumulation runs in every damage-category branch** (a seam-review BLOCK caught
  the original Standard-only gap).
- **Mirror Move:** re-executes the foe's last move via an inner action; fails if the foe hasn't moved.
- Seam-review gate: BLOCK → 2 doc blockers (per-gen XML docs for Bide seam members) + 4 advisories, all
  fixed; the Bide all-category accumulation gap was the substantive one.

### Batch 13 (moves 121–130) ✅ DONE (2026-06-05)
egg-bomb, lick, smog, sludge, bone-club, fire-blast, waterfall, clamp, swift, skull-bash.
 Pure **coverage + data-fidelity** batch — no new engine code, events,
schema, or seam. Only production change: three Gen 1 importer data fixes.
- New capability class: **`NeverMissContractTests`** (swift).
- **lick is Ghost-type** — 0× vs Normal *and* (the Gen 1 bug) 0× vs Psychic; folds the immunity into the
  calc (emits `DamageDealt` at 0, not `MoveHadNoEffect`).
- **Three importer data fixes (layer-2 + name-match), pinned:** **skull-bash → TwoTurn** (Gen 1 plain
  charge); **fire-blast** burn 30%; **waterfall** no secondary (the 20% flinch was Gen 4).
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers). Surfaced the pre-existing flaky OHKO test (fixed batch 16).

### Batch 14 (moves 131–140) ✅ DONE (2026-06-05)
spike-cannon, constrict, amnesia, kinesis, soft-boiled, high-jump-kick, glare, dream-eater, poison-gas,
barrage. One new mechanic (Dream Eater) + two importer mappings. No layer-2
override needed.
- New capability class: **`DreamEaterContractTests`**.
- **Dream Eater (`MoveEffect.DreamEater`):** fails on a non-sleeping target (reuses `MoveMissed`, the
  state-precondition path). The sleep requirement is **gen-invariant**, so inline, not on the seam. The
  50% drain heal rides on `DamageCategory.Drain`.
- **Two importer mappings:** high-jump-kick → Crash; dream-eater → DreamEater.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers).

### Batch 15 (moves 141–150) ✅ DONE (2026-06-06)
leech-life, lovely-kiss, sky-attack, bubble, dizzy-punch, spore, flash, psywave, splash (**9 of 10** —
Transform deferred). Two new engine bits, rest coverage-only.
- **Psywave (`DamageCategory.Psywave`):** variable damage = random 1..floor(1.5 × user level), ignoring
  Attack/Defense, type, STAB, crits. Magnitude on the seam (`IBattleRules.RollPsywaveDamage`).
  **`PsywaveContractTests`** exercises the *quirk*, not just the import mapping.
- **Splash (`MoveEffect.Splash`):** Gen 1 no-op — new `ButNothingHappened` event. Inline (gen-invariant).
- **Layer-2 importer data overrides, pinned:** **bubble & constrict → 33% Speed drop** (also corrects
  batch-14 constrict); **dizzy-punch → no secondary**; **sky-attack → flinch chance cleared**.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers).

### Batch 16 (moves 151–160) ✅ DONE (2026-06-06)
acid-armor, crabhammer, explosion, fury-swipes, bonemerang, rest, rock-slide, hyper-fang, sharpen
(**9 of 10** — Conversion deferred). **779 .NET.** One new mechanic (Rest) + bonemerang mapping; rest
coverage + one data fix.
- **Rest (`MoveEffect.Rest`):** self-targeting heal+sleep. Fully restores HP, overwrites status with
  `Sleep`, forces sleep for a fixed `IBattleRules.RestSleepTurns` (Gen 1 = 2; on the seam). Fails at full
  HP via `MoveMissed`. **`RestContractTests`** + a full-`Battle` forced-skip test (asserts the foe is never slept).
- **Bonemerang:** importer → `MoveEffect.MultiHit` + `MultiHitCount=2` (reuses double-kick/twineedle).
- **Layer-2 data fix, pinned:** **rock-slide → flinch cleared** (Gen 1 had no flinch; Gen 2 added 30%).
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers). Reviewer flagged a potential self-vs-foe status
  leak on Rest; verified the row's `StatusEffect` is None, then guarded + pinned it.

### Batch 17 (moves 161–165) ✅ DONE (2026-06-07) — FINAL COVERAGE BATCH
tri-attack, super-fang, slash, substitute, struggle. One big mechanic
(Substitute) + a data fix; rest reuse.
- **Substitute (`MoveEffect.Substitute`):** costs floor(maxHP/4) HP, raises a decoy with floor(maxHP/4)+1
  HP; fails if one's up or HP ≤ cost. **Cross-cutting:** added one shared `DealDamageToTarget` helper that
  absorbs into the decoy and routed **every** damage path through it (Standard/Drain, Fixed, LevelBased,
  OHKO, SelfDestruct, SuperFang, Psywave, Counter, **and Bide unleash**) — closing the "hook on only the
  Standard path" leak class. While up, the decoy shields status/stat-drop/confusion — snapshotted at impact
  so the shield still blocks on the **breaking** hit. 3 new events. `SubstituteContractTests` covers
  create/cost, absorb, break+overflow, fail cases, shields, breaking-hit shield, full-`Battle` persistence.
- Reused/coverage: super-fang (`SuperFangContractTests` + data pin); slash (single-turn high-crit); struggle.
- **Layer-2 data fix, pinned:** tri-attack → no secondary in Gen 1.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers). Advisory fixed: secondary-shield snapshotted at impact.

### Type/identity-mutation batch — Transform (144) + Conversion (160) ✅ DONE (2026-06-07)
 The two deferred identity/type-mutation moves — covered together so the
snapshot/restore machinery (wider than Mimic's) is built once. No schema change, no new seam, no
layer-2 override (only the `Effect` name-mapping was added).
- **Shared identity-snapshot machinery:** new `BattleState.OriginalIdentity` (an `IdentitySnapshot` of
  pre-mutation types, the four non-HP battle stats, SpeciesId, original moveset wrappers) +
  `Creature.SnapshotIdentityForMutation()` (captures **once**) and `RestoreOriginalIdentity()`.
  `ResetBattleState()` restores before the `Battle = new()` swap, and `Battle`'s end cleanup calls it
  alongside `RestoreMimickedMove()` — same leak-proofing as Mimic. Added `StatStages.Copy()`.
- **Transform (`MoveEffect.Transform`):** copies the target's types, Atk/Def/Spec/Speed, stat stages,
  SpeciesId, full moveset (each move at `min(5, max)` PP); HP/MaxHP/level stay the user's. Self-affecting.
  New `TransformedInto` event.
- **Conversion (`MoveEffect.Conversion`):** copies the foe's Type1/Type2 onto the user (the Gen 1 mechanic
  — Gen 2+ matches one of the user's own moves instead, kept inline + documented). New `ConvertedType` event.
- Tests: `TransformContractTests` + `ConversionContractTests` (incl. the shared-machinery proof:
  Conversion-after-Transform still restores the true pre-Transform original). Both pinned.
- Seam-review gate: PASS-WITH-ADVISORIES (0 blockers, 2 advisories fixed): pinned `StatusEffect == None`
  on both moves + asserted the foe's status stays None; named both in the `targetsFoe` immunity-guard comment.

### Resolved coverage-era tech debt ✅
- **Flaky OHKO tests** (fixed batch 16): both `OHKOMove_*` tests relied on level implying speed, but
  randomised DVs flipped order. Rewrote to set Speed explicitly + renamed to the speed framing
  (`OHKOMove_FailsIfTargetFasterThanSource` / `OHKOMove_FaintsTargetIfSourceAtLeastAsFast`) — Gen 1 OHKO
  is a Speed compare (`IBattleRules.OneHitKoSucceeds`), not the level check Gen 2 added.
- **Fixed-2 multi-hit mover**: bonemerang — done in batch 16.
- **Rampage reuse**: petal-dance — done in batch 8.
- **Gen 1 type immunities** (batch 8): Poison→poison, Fire→burn, Body Slam→Normal-paralysis, Grass→Leech
  Seed, Ghost (0×) for fixed/level-based/OHKO/Super Fang/Counter — all on the seam. Remaining edge: Counter
  still only answers standard-path damage (documented simplification — see `TODO.md`).
- **Seam audit (2026-06-04):** fixed two move-specific damage quirks that leaked out of the seams: (1)
  **OHKO success** was using the Gen 2+ level rule → now `IBattleRules.OneHitKoSucceeds` (Gen 1 Speed
  compare); (2) **Self-Destruct/Explosion Defense-halving** was an inline `/2` mutating `Target.Attributes`
  → now `IBattleRules.SelfDestructDefenseDivisor` passed into `DamageCalculator`.
- **Gen 1 move-data fidelity** is data-driven via the `past_values` resolver; **secondary chances/targets**
  that `past_values` can't express are a short, verified override block in the importer (see batch 7).

---

## Web UI — Phaser Canvas & Animations ✅ DONE

### Phaser Canvas ✅ DONE
- [x] `phaser` + `mitt` npm dependencies added to `ClientApp`
- [x] `BattleCanvas.tsx` — mounts Phaser `Game` lazily (dynamic import, separate chunk); destroys on unmount
- [x] `BattleScene.ts` — loads front/back sprites, diagonal layout, entry slide-in animation with Web Audio cries
- [x] `PhaserBridge.ts` — typed mitt emitter; React dispatches `playMoveAnimation` / `playFaintAnimation`; Phaser emits `animationComplete` back
- [x] `AudioEngine.ts` — Web Audio API synth: `playCry`, `playFaintCry`, `playHit`, `playTick`
- [x] CSS sprite `<img>` placeholders replaced by the Phaser canvas; React retains HP/status/nameplate overlay layer

### Animations ✅ DONE
- [x] Entry: sprites slide in from edges with species cries; idle bob tween starts after entry
- [x] `MoveUsed` → attacker lunges; target white-flash + `playHit()`
- [x] `DamageDealt` → `UPDATE_HP` fires immediately (CSS transition); log message after 650ms
- [x] `CreatureFainted` → sprite slides down + fades with `playFaintCry()`; log after
- [x] `LeveledUp` → XP bar fills to 100% then resets; log after
- [x] All events enqueued — log text always appears **after** the relevant animation (Gen 1 feel)
- [x] Move menu re-enabled only after animation queue drains (`animationComplete` bridge event)
- [x] `useBattleHub` state gains `animating: boolean`; FIGHT + move buttons check `phase === 'choosing' && !animating`
- [x] **Transform (Ditto/Mew) morphs the sprite (2026-06-12).** `TransformedInto` now carries `IntoSpeciesId`;
  a `transformSprite` bridge command morphs the transforming side's sprite in place (player → back sprite,
  enemy → front sprite) with a scale-pulse cue, and `resetPlayerSprite` reverts the player on a win (Transform
  is undone at battle end; the enemy self-corrects via the next `spawnEnemy`). The Transform *mechanic* was
  already fully Gen-1-faithful (verified vs Bulbapedia: copies species/types/stats/stages/moveset@5PP, keeps
  own HP/level/status, reverts at battle end) — this was the only missing visual.

---

## Tech Debt / Cleanup — Done ✅

- Remove dead scaffolding (`Body`, `Brain`, `BodyPart`, `CreatureType`, etc.)
- `.gitignore`, `.gitattributes`, `.editorconfig`, `global.json` (SDK pin)
- EF Core migrations; `EnsureDatabaseCreated()` calls `Database.Migrate()`
- `StatStages` struct→class (silent mutation fix)
- `AsNoTracking()` on all read-only DB service methods
- Pending-session TTL in `GameSessionManager` (2-min eviction)
- `AlwaysHitRules` test helper (eliminates 1/256-miss flakiness)

### Architecture Review (2026-06-01) — resolved items

#### 1. Web battle lifecycle — disconnect leak + broken reconnect + swallowed errors ✅ DONE
`SignalRInput.ChooseMoveAsync` awaited a TCS with no cancellation path and `BattleHub` had no
`OnDisconnectedAsync`, so every abandoned battle leaked the input + both `Creature`s + the loop task.
- [x] `SignalRInput`: `_cancelled` flag + `Cancel()` that calls `_tcs?.TrySetCanceled()`; `ChooseMoveAsync`
  throws `OperationCanceledException` on entry if cancelled.
- [x] `BattleHub.OnDisconnectedAsync` → `manager.AbandonBattle(connectionId)` → `Cancel()`.
- [x] `GameSessionManager`: wrap the `Task.Run` body in try/catch — swallow/log `OperationCanceledException`
  at debug, other exceptions at error.
- [x] **Reconnect** — active battles keyed by `gameId`; `SignalRBattleEventEmitter` resolves the current
  connection per-emit; `OnConnectedAsync` with the same `gameId` rebinds (`AttachConnection`). Disconnect
  arms a 40 s grace timer (`DetachConnection`) that abandons only if no reconnect arrives. Verified e2e.

#### 2. Pull `BattleState` extraction forward ✅ DONE
`Creature` conflated persistent identity, transient battle state, and behaviour; `ResetBattleState()` was a
hand-maintained reset list (the `StatStages` struct→class bug was exactly this fault).
- [x] Extracted transient fields into `BattleState` (`Creature/BattleState.cs`), held as `Creature.Battle`.
- [x] `ResetBattleState()` is now `=> Battle = new BattleState()` — whole-object swap. Locked in by
  `ResetBattleState_ReplacesWholeBattleState_ClearingEveryTransientField`.
- [x] **Delegating properties** on `Creature` so the ~120 call sites stay unchanged. Save split is ready:
  persist Creature minus `Battle`. *(Optional future cleanup — migrate call sites to `creature.Battle.X`
  and drop the facade — deferred; see `TODO.md` tech debt.)*

#### 4. Speed tie-break uses RNG as a sort key ✅ DONE
`Battle.cs` called `.ThenBy(_ => Random.Shared.Next())` inside the `OrderBy` comparator (ill-defined key).
- [x] Now draws the tie-break once (`int tieBreak = _rng.Next(2)`) via the injected `IRandomSource`.

#### 5. DbContext via `new()` instead of DI ✅ DONE
`GameController` / `SpeciesController` did `new PokemonDbContext()` / `new MovesDbContext()` (lost pooling).
- [x] Registered `AddDbContextFactory<…>()` in `Program.cs`; both controllers inject `IDbContextFactory<T>`
  and use `CreateDbContextAsync()`. Verified at runtime.

#### 6. Frontend battle-log queue was structurally racy ✅ DONE
The imperative enqueue/waitForBridge/delay choreography in `useBattleHub` (two bugs: permanent freeze +
listener leak).
- [x] Split into a **pure** `expandEvent(...) → { now, steps }` (`battle/timeline.ts`) + a small **driver**
  (`useBattleTimeline`) that plays steps one at a time; `useBattleHub` slimmed to connection + reducer.
- [x] Sequencing/timing/text unit-tested without a browser (`timeline.test.ts`, 15 Vitest cases).
- [x] Playwright E2E landed (9 specs via the `?e2e=1` seam).
- [x] Full-flow parity verified live (Puppeteer + Playwright faint→winner play-through).

#### 6a. Code-review cleanups (batches 11–13, 2026-06-05) ✅ (one item deferred — see `TODO.md`)
- [x] **Importer name-dispatch consolidated** — the ~20-arm `else if (Name == …)` chain replaced by a
  `static readonly Dictionary<string, MoveEffect> Gen1MoveEffects`.
- [x] **`AttackAction.ExecuteInner(Attack)` helper** — Metronome and Mirror Move share one helper.
- [x] **Bide "typeless" contradiction resolved** — release no longer records `LastDamageTaken`, so Bide is
  non-counterable like the other non-standard categories. Pinned by `BideDamageIsTypelessAndNotCounterable`.
- [x] **Mirror Move filter/comment made consistent** — dropped the dead `last.Effect != MirrorMove` check.
- [x] **`Creature.cs` delegating-prop alignment** normalised.
- [x] **PP-skip predicate named** — `isLockedInContinuation` local.

---

## Known Gaps — resolved ✅
- ~~`GameController.BuildCreature` uses random moves~~ — **fixed** by the Learnset System (initial moveset
  now learnset-driven).

---

## Fixed ✅ (battle/UI bugs)
- **Gen 1 binding (Wrap/Bind/Clamp/Fire Spin) was a Gen 1 / Gen 2 hybrid (2026-06-12).** The trapped foe lost
  its turn (Gen 1) but the attacker was free to use other moves and the victim took a separate 1/16-HP
  end-of-turn "hurt by the bind" residual (both Gen 2). Fixed to true Gen 1 (Bulbapedia-confirmed): the BINDER
  is now locked into re-using the move every turn — new `BindingMechanic : ILockInMechanic` whose `ForcedMove`
  re-forces the move while the victim's counter is alive (`BattleState.BindingMove`/`BindingTarget`); the victim
  still can't act; the 1/16 residual is gone (the re-hit IS the damage). Removed the now-dead `BindingDamage`
  event and `IBattleRules.BindingDamageDenominator` (they return with the Gen 2 residual). Proven by
  `BindingInteractionTests` (binder locked into Wrap, ignores its scripted Tackle; foe never gets a move off).
  `/audit` PASS-WITH-ADVISORIES (0 blockers; the per-re-hit-vs-locked-first-hit-damage nuance is deferred +
  documented inline). Level-up stat-panel column-spacing CSS bug + its E2E guard landed the same day.
- Post-feature gen-seam + smell cleanup (2026-06-02): closed three seam leaks surfaced by the
  Learnset/confusion work — confusion self-hit chance (`ConfusionSelfHitPercent`), STAB (`StabMultiplier`),
  and the EffectChance read (`GetSecondaryEffectChance` + `SecondaryEffectKind`) are now all on
  `IBattleRules`; `CalculateConfusionDamage` reads stats via `GetOffensiveStat`/`GetDefensiveStat`. Killed
  the 5× duplicated `IBattleRules` test doubles with a `TestSupport/DelegatingBattleRules` base. Centralised
  move-selection policy in `LearnsetMoveSelector.SelectWithFallback`. Added the generation-agnostic checklist
  + definition-of-done in `GENERATION_SEAMS.md §5.0`. 179 tests green.
- Enemy "only ever uses one status move": the enemy ran on `AutoSelectInput`, which always returns slot 0;
  `WeightedSmart`/`CanonicalLatest` order ascending by learn level, so a level-1 status move landed in slot 0.
  Fixed by adding `RandomMoveInput` (uniform pick among PP-available moves, `IRandomSource`-seamed) and
  wiring it as the enemy input. Covered by `ConfusionAndInputTests` + verified live.
- Confusion-inflicting moves did nothing: confusion is a per-battle counter (`ConfusedTurns`), not a
  `StatusCondition`, and nothing set it. Fixed end-to-end: `MoveEffect.Confuse` + `IBattleRules.RollConfusionTurns`
  (Gen 1: 2–5 counter), an `AttackAction` `Confuse` case, a `ConfusionStarted` event, and the importer maps
  ailment `"confusion"` → `Confuse`. Covered by `ConfusionAndInputTests` + verified live.
- Attack cadence (Gen 1 feel): the lunge + flash played **before** the "X used MOVE!" line, and the HP bar
  snapped to its end-of-turn value when a move was chosen. Fixed by announcing the move first then animating,
  and routing `TurnStarted` **through the timeline**. Locked by Vitest + `cadence.spec.ts`.
- Gen 1 physical/special split miscategorised 18 of 110 damaging moves: the importer copied PokeAPI's
  `damage_class` (the Gen 4+ split), but Gen 1 decides physical/special by the move's **type**. Fixed in
  `MoveImport.MapToAttack` (derives `AttackType` from `DamageType` via `Gen1DamageCategory`); existing rows
  corrected in place (0 mismatches). See `DATA_IMPORT.md` §4.1/§6.
- Battle log froze on faint: `BattleScene.destroy()` was dead code, so `bridge.on` listeners leaked across
  canvas remounts and a stale scene's `playFaintAnimation` threw — now removed via `SHUTDOWN`/`DESTROY` scene
  events. Hardened the queue (`drainQueue` try/catch-continues; `waitForBridge` 3 s timeout).
- Battle-log text polish: move names display formatted (`fury-attack` → `FURY ATTACK`); Gen 1 per-move
  two-turn charge lines replace the generic "is charging up X!"; immunity reads "It doesn't affect X...".
- Metronome (`MoveEffect.Metronome`): picks a random eligible Gen 1 move and executes it in full; pool
  threaded from `GameController` → `GameSessionManager` → `Battle` → `AttackAction`.
</content>
</invoke>
