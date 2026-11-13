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
  things that open up other items first, fewest blockers first. §1 is that ranking, tiered by the step that stands
  between an item and a greenlight, and lists **every** open entry; §2–§8 are the same items grouped by area, with
  the detail.
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

Ranked within each tier; the tiers run from the shortest path to a greenlight to the longest. ✅ Ready · ⚠️ every
gap has drafted text, awaiting your confirmation · ✗ gaps remain.

**Ready — each awaits your greenlight**

| Item | Cost | Ready? | Next step / what it unblocks |
|:-----|:----:|:-------|:-----------------------------|
| **Binding continuation turns follow Gen 1** (§3.1) | M | ✅ | Central `AttackAction` and `BindingMechanic`; two `IBattleRules` members and one new event. |

**A. Confirm the drafted lines, then Ready**

None open.

**B. A design pass first** (central engine code: both gates; each pass is shown to you)

| Item | Cost | Ready? | Next step / what it unblocks |
|:-----|:----:|:-------|:-----------------------------|
| **A trapped Pokémon can switch out in Gen 1** (§3.1) | S–M | ✗ 2, 4, 6; the rest drafted | Found 2026-11-13. `Battle.CanSwitchTo`; best after the continuation item. |
| **The player chooses what Mimic copies** (§3.1) | M | ✗ 2, 3; the rest drafted | Found 2026-11-03. Needs an input prompt, so a design pass. |

**C. Needs a `/plan` or a verify slice**

| Item | Cost | Ready? | Next step / what it unblocks |
|:-----|:----:|:-------|:-----------------------------|
| **End-of-turn residual phase** (§4.2) | L | ✗ 2; 1, 5, 6 drafted | Unblocks Leech Seed × Toxic (§3.1). |
| **Damage/accuracy core** (§3.1) | L | ✗ 1, 2, 3, 4, 6 | A verify slice first (a pokered cite per claim); touches every damage number. |
| **Audit the inline battle rules behind seams** (§3.1) | M | ✗ 1, 2 drafted | Your 2026-10-31 ruling: battle logic always on a seam. A verify slice lists every inline rule first. |
| **CI E2E step** (§6) + **Run the fast suites before deploying** (§7.1) | M + M | ✗ 1, 2, 6, 7 | Design together; the only automatic E2E coverage. |
| **Catch / Poké Ball effect** (§4.1) | L | ✗ 1, 2, 3, 4, 6 | Pair with the unsold-item rule (§3.2), which the first 0-priced Ball triggers. |
| **Stone evolutions** (§4.1) | M | ✗ 1, 2, 4, 5, 6, 7 | Waits on the acquisition design (how stones enter a run). |
| **Progressive difficulty** (§4.2) | L | ✗ 1, 2, 4, 6, 7 | |
| **The unsold-item rule keys on price, not category** (§3.2) | S | ✗ 1, 2 | A design pass picks the rule; pair with Catch, which first puts a 0-priced Ball in the pool. |
| **Leech Seed × Toxic counter** (§3.1) | S | ✗ 1, 2, 3, 5, 6, 7 (drafted) | After the residual phase. |
| **Dev Mode — next features** (§4.6) | M | ✗ 1–7 | "Force the first enemy" would speed the two slowest specs. |
| **Agent profiles → project skills** (§4.6) | M | ✗ 2 | |
| **Generation Profile 4d+ surface catalog** (§4.3) | L | ✗ 1, 2 per surface | Each surface its own mini-plan with you. |
| **Move-specific attack animations** (§4.5) | M | ✗ 1, 2, 6, 7 | Folds in Sprite FX §3.4. |
| **Text feel — typewriter + menu blips** (§4.5) | M | ✗ 1, 2, 4, 6, 7 | |
| **A Fly or Dig user's sprite stays on screen** (§4.5) | S–M | ✗ 1, 2, 4, 6 | Filed 2026-11-09. Needs a wire signal and a `BattleScene` handler. |
| **Sprite presentation FX** (§4.5) | S–M | ✗ 1, 2, 6 | A joint sketch session; the §3.1 `pixelArt` check could go first as its own S item. |
| **Pre-commit hook: staged snapshot; dotnet path** (§7.1) | M + S | ✗ 1, 2, 6 each | |
| **User documentation** — `/help`, README, Gen 1 explainer (§4.7) | M | ✗ 1, 2 | |
| **Multi-generation data model** (§4.4) | L | ✗ 1, 2, 6, 7 each | Waits on a Gen 2 sprint `/plan`. |

**D. Investigate or verify first**

| Item | Cost | Ready? | Next step |
|:-----|:----:|:-------|:----------|
| **Disable matches move instances, not move ids** (§3.1) | S | ✗ 1, 2, 4, 6 | Filed 2026-11-09. Read `DisabledMoveStops` and the Metronome and Mirror Move wrappers first. |
| **Same-type secondary status immunity** (§3.1) | S–M | ✗ 2, 4, 6; the rest drafted | Take the pokered cite; Ice Beam freezes Ice-types today. |
| **Other `ResidualEffects1` moves may roll accuracy** (§3.1) | S–M | ✗ 1, 2, 4, 6 | Filed 2026-11-06. A verify slice reads each effect for its own hit test. |
| **Residual-effect failure lines: Leech Seed "evaded", confusion-move miss** (§3.1) | S–M | ✗ 2, 3, 6 | Filed 2026-11-06. Pair with the `ResidualEffects1` slice. |
| **Counter keys on the foe's selected move** (§3.1) | S–M | ✗ 1, 2, 4, 6 | Trace `HandleCounterMove`, list the diverging cases. |
| **A re-import marks `pokemon.db` changed with identical data** (§3.2) | S | ✗ 2; the rest drafted | Filed 2026-11-06. Read the three delete-and-insert sites, pick upsert or stable IDs. |
| **Escape = B-cancel on the negative-answer prompts** (§4.5) | S | ✗ 4; the rest drafted | One pokered cite. |
| **Dependencies — `npm audit`** (§7.2) | S | ✗ 1, 7 | Re-run `npm audit`; the bumps need your E2E run. |
| **The client `RunFaulted` handler is unpinned** (§6) | S | ✗ 1, 2, 7 | Choose: extract the handler logic, or test it in place. |
| **E2E play helper hangs on an open move grid** (§6) | S | ✗ 2 | Confirm the trigger from the trace. |
| **Level-up panel follow-ups** (§4.3) | S | ✗ 4 | Verify pokered's level-up stats box. |
| **Transform's scale pulse may revert a resize** (§5) | S | ✗ 1, 3, 5, 6, 7 | Reproduce first. |
| **`voluntary-switch.spec.ts` is still slow** (§6) | S | ✗ 1, 7 | Investigate; likely waits on Dev Mode "force the first enemy". |
| **The Continue card shows the starter's species** (§5) | S–M | ✗ 1, 2, 6 | Needs a persisted-shape change. |

**E. Waiting on you, parked or blocked**

| Item | Cost | Ready? | What it waits on |
|:-----|:----:|:-------|:-----------------|
| **Enemy AI ignores a foe that is out of reach** (§3.1) | S–M | ✗ 1, 2, 6 | Your product call: stay faithful to Gen 1's AI or add a guard. |
| **`evolution.spec.ts` nameplate assertion** (§6) | S | ✗ 1 drafted | Your E2E go-ahead. |
| **Tighten `encounter-map` and `cadence` specs** (§6) | S | ✗ | Your E2E go-ahead; a cadence seed. |
| **E2E half of "Deduplicate the test helpers"** (§6) | M | ✗ | Your E2E go-ahead. |
| **`-StartStack` backend leak** (§7.1 A) | S | ✗ parked | Your reproduction. |
| **Abuse protection** (§7.1) | M | ✗ shelved | Your numbers and the idle-notice UX call. |
| **Struggle skips the accuracy roll** (§3.2) | S | ✗ | Left as filed (ruling 2026-10-11). |
| **Enemy encounter pool ignores game version** (§4.2) | S | ✗ 1, 2, 3, 5, 6, 7 | Gap 7 blocks the rest: no version selector exists. |
| **Enemy Pokémon do not evolve** (§4.2) | S | ✗ 1, 2, 4, 6, 7 | Your product call on when the loop wants it. |
| **`ConsoleInput`** (§4.6) | S | ✗ 1, 2, 7 | Which host runs it. |
| **`save.db`** (§4.2) | L | declined | Revisit by your ruling only. |

---

## 2. Reliability — reconnect (designed in [`RECONNECT_RESILIENCE.md`](RECONNECT_RESILIENCE.md))

Every reconnect item is fixed and archived (`TODO_ARCHIVE.md` → the *§2 R1d-* records). Replaying on every reconnect
is an accepted design (§9).

- **Open verification gap:** the 60 s grace rests on SignalR's documented 0/2/10/30 s schedule, not a measured run;
  measuring it needs the reconnects blocked for ≈42 s, a separate spec, not filed. The design is in
  [`RECONNECT_RESILIENCE.md`](RECONNECT_RESILIENCE.md); the server-side harness is `GameSessionLifecycleTests`
  (`FakeTimeProvider`, `ActiveBattleFor`, `ActiveBattle.RunTask`).

---

## 3. Correctness and Gen 1 fidelity

All items below are **unverified** (a reviewer's reading) unless marked; the Gen 1 value in each must be checked
against pret/pokered before the engine or data changes (`GEN_DIFFERENCES.md` is a secondary source). Fixing a
data/engine item usually also means updating the test that pins the wrong value.

### 3.1 Engine

- **Damage/accuracy core (L):** `DamageCalculator.cs:246-247` skips the `2L/5` and `/50` floors `GEN_DIFFERENCES.md`
  documents (~24% high at L12); `Gen1BattleRules.cs:215` uses a 3-based accuracy/evasion table (Gen 1 uses the
  standard table); crit multiplier should be ≈(2L+5)/(L+5), not a flat 2× (`Gen1BattleRules.cs` crit block, ~`:287`); recoil/drain/Struggle use full calculated damage, not
  HP actually lost (`AttackAction.ExecuteAsync` Struggle recoil, `ResolveDamage` drain); Ice can freeze Ice /
  Electric can paralyse Electric (`Gen1BattleRules.CanReceiveStatus`); `DamageCalculator` applies no stat cap after the stage multiplier (Gen 1 reportedly clamps a staged
  stat to 1–999) and no ÷4 scaling of both stats when either exceeds 255 (found by `requirements-review`
  2026-10-14, during the stat-stage seam item; unverified against pokered).
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 3, 4, 6** (line cites refreshed 2026-10-17). *Drafted:* first a verify
  slice that gives each claim a pokered cite, then one entry per confirmed claim. Each entry gets: acceptance, a
  test pinning pokered's number; its seam member (most are already `IBattleRules`/`DamageCalculator` inputs);
  both gates, since `DamageCalculator`/`AttackAction` are central. Data vs runtime: runtime.
  Dependencies: none.
- **Leech Seed × Toxic counter (from §7.6 V10) — S, ✗ not Ready.** *Intent:* the Leech Seed drain is multiplied by the
  Toxic counter (`core.asm:546-591`). *Gaps:* overlaps the end-of-turn residual `/plan` (§4.2); sequence after it.
  *Drafted:* an `IBattleRules` member, runtime only; quirk: the drain grows with the Toxic counter.
- **Other `ResidualEffects1` moves may roll accuracy they skip in Gen 1 (found 2026-11-06, Roar design pass;
  unverified) — S–M, ✗ not Ready.** pokered skips the accuracy test for every effect in `ResidualEffects1`
  (`data/battle/residual_effects_1.asm`, dispatched at `core.asm:3124-3127`) unless the effect calls `MoveHitTest`
  itself. Roar and Whirlwind are done (`TODO_ARCHIVE.md` → "Roar/Whirlwind"); the rest (Conversion, Haze, Mist, Focus Energy,
  Confusion, Heal, Transform, the screens, Poison, Paralyze, Substitute, Mimic, Leech Seed, Splash) are unchecked.
  **DoR:** ✗ — gaps **1, 2, 4, 6**. *Drafted:* a verify slice reads each effect for its own `MoveHitTest` call and
  lists which of ours roll accuracy that pokered skips; then one entry or one seam member (likely generalising
  `ForceFleeSkipsAccuracy`, shipped 2026-11-06). 3: `IBattleRules`. 5: runtime only. 7: none now that Roar has shipped.
  Noted 2026-11-09 (`pr-review`, Fly and Dig): `Gen1BattleRules.RunsHitTest` already lists which moves run the hit
  test, for the out-of-reach rule; this item should make the accuracy roll read the same member so the two can't drift.
- **Same-type secondary status immunity (found 2026-10-29 by `requirements-review`; read-confirmed by the reviewer,
  not yet by us) — S–M, ✗ not Ready.** pokered `FreezeBurnParalyzeEffect` (`effects.asm:194-212`) skips a secondary
  freeze, burn or paralysis whenever the move's type equals either target type ("an ice move can't freeze an
  ice-type, body slam can't paralyze a normal-type"). `Gen1BattleRules.CanReceiveStatus` models only Poison, burn vs
  Fire and Normal→Normal paralysis, so Ice Beam can freeze an Ice-type and Thunderbolt/Thunder can paralyze an
  Electric-type. `GENERATION_SEAMS.md` §2 "Status by type" records the gap.
  **DoR:** ✗ — gaps **4** (take the pokered cite ourselves, and check whether primary status moves and Tri Attack-style
  cases share the rule), **2** (does the seam signature change: the rule keys on the move's type vs the target's, which
  `CanReceiveStatus(target, status, moveType)` already receives), **6** (drafted: a full battle where a forced-hit
  Ice Beam on an Ice-type never freezes, and Body Slam on a Normal-type never paralyzes). 1 drafted: the engine applies
  pokered's same-type rule to secondary freeze/burn/paralysis. 3: `CanReceiveStatus` is the seam. 5: runtime only. 7:
  none.
- **Counter keys on the foe's selected move (found 2026-10-29 by `requirements-review`; read-confirmed by the
  reviewer) — S–M, ✗ not Ready.** pokered `HandleCounterMove` (`core.asm:4547-4607`) qualifies on the foe's last
  *selected* move: it misses when that move is Counter, has 0 power, or isn't Normal/Fighting type, and doubles the
  shared `wDamage`. Ours keys on the type of the last damage the user *took* (`LastDamageType`). Cases that differ
  need tracing (a foe that selected a Normal move but missed; damage from the user's own move in `wDamage`).
  **DoR:** ✗ — gaps **4** (trace `HandleCounterMove` ourselves and list the diverging cases), **2** (design pass:
  touches `AttackAction`/`CounterEffect` and `CounterQualifies`' signature), **6**, **1**. 5: runtime only. 7: none.
- **Audit the inline battle rules and put each behind a seam (user ruling 2026-10-31) — M, ✗ not Ready.** Battle
  logic gets full generation separation: a rule goes on a seam even when the generations we checked agree, and
  only out-of-battle or ancillary behaviour may stay inline. `GENERATION_SEAMS.md` still documents deliberate inline
  rules, among them the freeze thaw and Explosion/Self-Destruct fainting on a miss (row "Freeze", "inline in
  `AttackAction`"), Disable's target move and Conversion ("not yet a seam"), and Confuse's never-re-rolled counter
  ("gen-agnostic and inline in `ConfuseEffect`") (read-confirmed 2026-10-31). Also `SubstituteEffect`'s can't-pay
  check: Gen 1 fails only below the cost, Gen 2 at exactly the cost too (`GENERATION_SEAMS.md` §2, Substitute lines).
  **DoR:** ✗ not Ready — gaps **1, 2**; the rest follow from the slice. *Drafted:*
  - **Acceptance:** a verify slice lists every inline battle rule in the engine (the `GENERATION_SEAMS.md` rows plus
    a code sweep of `Combat/`), each with its pokered cite; each then gets its own seam member and a test that a
    `TestAltProfile` override changes the outcome; no `GENERATION_SEAMS.md` row says "inline" for battle logic. The same pass rewords the carve-outs that rule 4 now contradicts: `GENERATION_SEAMS.md` §5.0 ("leave it inline … don't over-abstract", the `ItemPriority` note), `pr-review.md` and `DEFINITION_OF_DONE.md` ("gen-invariant constants inline are fine"), `ARCHITECTURE.md:109`.
  - **Design:** the slice first (read-only, shown to you), then one design pass per cluster of members.
  - **Data vs runtime:** runtime only. **Dependencies:** none; can split into one item per cluster after the slice.
  - **Also in scope (user ruling 2026-10-31, waived from "Failure announcements"):** battle message *wording* lives in
    the client (`timeline.ts`) and the console emitter, keyed on event fields (e.g. `MoveUnaffected.Cause`), not on a
    seam. Decide whether battle text moves to the presentation catalog (§4.3) or a seam; the same question covers
    the Counter-failure and Dream Eater `MoveMissed` lines.
- **Residual-effect failure lines (found 2026-11-06, "It didn't affect" design pass; source-confirmed) — S–M, ✗ not
  Ready.** Two more pokered lines we don't print. Leech Seed on a Grass type, on an already-seeded foe, or on a miss
  prints "<TARGET> evaded the attack!" (`leech_seed.asm` `.moveMissed`, `EvadedAttackText`); ours prints "doesn't
  affect" for Grass (`MoveEffects.cs` ~118), nothing when already seeded, and "missed" on a miss. A missed Supersonic
  or Confuse Ray prints "But it failed!" (`effects.asm:1120-1126`, `ConfusionEffectFailed` :1158-1163); ours prints
  "missed".
  **DoR:** ✗ — gaps **2, 3, 6**. *Drafted:* **Acceptance:** each case prints its pokered line in a full battle.
  **Data vs runtime:** runtime plus a new "evaded" event wired in all four places. **Dependencies:** pair with the
  `ResidualEffects1` accuracy slice, which decides where these moves roll accuracy.
- **Binding continuation turns follow Gen 1 (found 2026-11-13, missed-Wrap design pass; source-confirmed) — M, ✅
  Ready.** On every turn after the first, pokered prints "<user>'s attack continues!" and skips damage calculation,
  `MoveHitTest` and PP (`core.asm:3554-3566`, enemy `:5929-5938`): the turn always lands and deals the last damage
  again. Ours re-runs the whole pipeline: it announces "used Wrap", rolls accuracy (so it can miss) and re-rolls crit
  and damage. Separately, a 0× matchup marks the move missed without clearing the lock (`core.asm:5171-5176`), so
  Wrap or Bind on a Ghost whose accuracy roll passes still locks both sides in, dealing 0; ours prints "no effect"
  and starts no trap (`AttackAction.cs` ~467).
  **DoR:** ✅ (audited 2026-11-13).
  1. **Intent / acceptance:**
     - A continuation turn prints "<user>'s attack continues!" in place of "used <move>", with no accuracy, crit,
       damage or type roll and no PP spent. It deals exactly the first strike's damage (a crit's included) through
       `DealDamageToTarget`, so a Substitute soaks it, and after the decoy breaks the next turn hits the foe for the
       same amount.
     - An immune first strike (Wrap or Bind on a Ghost, or Fire Spin damage that rounds to 0) prints "It doesn't
       affect <foe>!". If its accuracy roll passed, the lock starts with stored damage 0: the user is locked for the
       rolled turns, the foe can't move, and each continuation deals 0. If the roll failed, it is a plain miss.
  2. **Design:**
     - The binder gets a transient `BindingDamage`, cleared in `ResetBattleState` and listed in `STATE_MODEL.md`.
       `BindingEffect` stores the computed damage when the lock starts, not capped by a Substitute's HP (check what
       `ResolveDamage` returns).
     - `BindingMechanic.OnCommit` on a continuation emits a new `BindingContinues` and returns `ProceedUnannounced`;
       `OnRelease` returns the stored damage through the unleash path Bide uses, so the turn skips `Connects` and
       the damage roll.
     - `Connects`' immune branch calls the same lock-start helper as `BindingEffect` when the move is a binding move,
       the rule allows it and the accuracy roll passed; under the Gen 1 roll order that branch must now read the
       roll's result.
     - `BindingContinues` gets the four-place wiring (`ClaudeDirectives/engine.md` rule 7).
  3. **Gen-variable surface:** two `IBattleRules` members, both true for Gen 1:
     `BindingContinuationReplaysDamage` and `BindingLockSurvivesTypeImmunity`. Gen 2 differs on both.
  4. **Gen 1 source:** `core.asm:3554-3566`, `:5929-5938`, `:5171-5176`, `:5328-5345`, `:4879-4880` (a broken
     Substitute leaves the stored damage whole), `effects.asm:1081-1104`, `PrintMoveFailureText` ("It doesn't
     affect"), `data/text/text_2.asm:1061-1069`.
  5. **Data vs runtime:** runtime only.
  6. **Quirks the tests assert (full battle):** with an always-miss double installed after the first hit the
     continuations still land; each deals exactly the first hit's damage and repeats a crit's; a continuation emits
     `BindingContinues`, not `MoveUsed`, and PP is spent once; Wrap on a Ghost with the roll passing prints the
     no-effect line, the foe is blocked every turn, the binder is locked and continuations deal 0; with the roll
     failing, no lock; a Substitute takes the stored damage and after it breaks the next hit lands on the foe for
     the same amount. Plus `WebEventContractTests` and a client timeline test for the new event.
  7. **Dependencies:** none. The same commit fixes `GEN_DIFFERENCES.md:168` (Gen 1 binding has no 1/16 chip) and adds
     the continuation rules there, adds `GENERATION_SEAMS.md` §2 rows for both members, and deletes the stale
     "recalculated per re-hit" comment (`MoveEffects.cs` ~144) and the `BindingMechanic` summary comment.
  **Estimate:** ~150–250k tokens through the full review chain (`pr-review` ~80–100k of it).
- **A trapped Pokémon can switch out in Gen 1 (found 2026-11-13, missed-Wrap design pass; read-confirmed, not run) —
  S–M, ✗ not Ready.** pokered shows the battle menu to a trapped Pokémon and skips only move selection
  (`core.asm:305-322`); the switch and run paths have no trap check. Sending out any Pokémon frees the opponent's
  binder (`core.asm:1755-1756` player side, `:1308-1309` enemy side), so the binder's next turn is a fresh use of the
  move against the switch-in. Ours forbids the switch (`Battle.CanSwitchTo`, `Battle.cs` ~579), and its comment says
  Gen 1 does too.
  **DoR:** ✗ gaps **2** (what the binder does against the switch-in: a fresh use that spends PP, next to the known
  Gen 1 PP glitch), **4** (trace the binder's own switch: `RetreatMon` only prints a line, so its lock may survive
  it; and whether running from a wild battle while trapped is allowed), **6**. *Drafted:* 3: an `IBattleRules`
  member for whether a trapped Pokémon may switch. 5: runtime only. 7: best done after "Binding continuation turns
  follow Gen 1".
- **Disable matches move instances, not move ids (found 2026-11-09 by `opus-engineer`, Fly and Dig item;
  read-confirmed, unverified beyond that) — S, ✗ not Ready.** `StatusResolver.DisabledMoveStops` compares the selected
  `PokemonAttack` with `Battle.DisabledMove` by reference; pokered compares move numbers
  (`wPlayerDisabledMoveNumber`, `core.asm:3437`+). A disabled move called through Metronome or Mirror Move is a
  different instance here, so it is not stopped, and the "is disabled!" route into the Fly and Dig glitch cannot
  occur. Whether anything else leans on the instance comparison (Mimic, Transform's copied moveset) is unchecked.
  **DoR:** ✗ gaps **1, 2, 4, 6**. *Drafted:* a verify slice reads `DisabledMoveStops`, `DisableEffect` and the
  Metronome and Mirror Move wrappers against pokered, then lists the diverging cases. 3: likely none (a matching bug,
  not a rule), decided by the slice. 5: runtime only. 7: none.
- **Enemy AI ignores a foe that is out of reach (found 2026-11-09, Fly and Dig item) — S–M, ✗ not Ready, your
  call.** `Ai/MoveEvaluators.cs:26` scores hit chance from `NeverMisses` and `Accuracy` and doesn't ask
  `HitOnSemiInvulnerable`, so the enemy attacks a flying or underground Pokémon as if it could hit. pokered's AI
  doesn't consider `INVULNERABLE` either, so today's behaviour is faithful; a guard would be a roguelite extension.
  **DoR:** ✗ gaps **1** (the product decision: stay faithful or add a guard), **2**, **6**. *Drafted if a guard is
  wanted:* acceptance, the enemy picks a move that lands (or its best move when none does) against a foe out of
  reach; the evaluator asks the rules member instead of re-deriving it (`ClaudeDirectives/engine.md` rule 2). 3: the
  existing member. 4: pokered's AI has no such check. 5: runtime only. 7: none.
- **The player chooses what Mimic copies (found 2026-11-03, move description audit) — M, ✗ not Ready.** pokered lets the
  player pick any of the target's moves, and only the enemy picks at random (`MimicEffect`, `effects.asm:1203-1250`). Ours
  always picks at random (`MoveEffects.cs:345-352`).
  **DoR:** ✗ gaps **2** (a new input prompt across the engine, hub and client) and **3** (the seam for who chooses).
  *Drafted:*
  - **Acceptance:** the player's Mimic prompts for one of the target's moves; an opponent's stays random.
  - **Gen 1 source:** the cite above. **Data vs runtime:** runtime and client. **Quirk:** the target's moves show in full.
  - **Dependencies:** none.

### 3.2 Data and importer

- **Found 2026-10-11 during V7 / the item seam (unverified beyond the citation; each needs its own DoR):**
  - *Struggle skips the accuracy roll* (found during "Gen 1 roll order", archived): `AttackAction.Connects`
    treats Struggle as never-miss; in pokered it goes through `MoveHitTest` like any move (so the 1/256 miss applies).
    Unverified beyond that reading; needs its own DoR. **Fixing it must flip
    `RollOrderContractTests.StruggleRollsCritAndDamageWithNoAccuracyDrawMirroringTheKnownGap`.**
  **DoR (V16), Struggle:** ✗ not Ready — gaps **1, 2, 3, 4, 5, 7**, left as filed per the ruling below. When
  reopened, the draft is:
  - **Acceptance:** Struggle rolls accuracy, so a forced 255 roll misses.
  - **Design:** central `AttackAction`, so both gates.
  - **Seam:** `IBattleRules` (Gen 2's Struggle is unchecked).
  - **Gen 1 source:** cite `MoveHitTest` (`core.asm:5228`).
  - **Data vs runtime:** runtime only.
  - **Dependencies:** none.

  User ruling 2026-10-11: leave this one as filed.
- **The unsold-item rule keys on price, not category (S; from `requirements-review` 2026-10-25, finding 2).**
  `RarityOf`/`IsLootOnly` treat any `Cost == 0` item as loot only with a PP-shaped fixed rarity. Today only the
  four PP items reach it. A future 0-priced item in another category (Master Ball, once Catch adds Balls to the
  pool) would silently become loot only. **User ruling 2026-10-25:** accepted as shipped; revisit in a design pass.
  **DoR:** ✗ not Ready — gaps **1, 2** (the design pass decides between "unsold ⇒ loot only, rarity by category"
  and keying the rule on `PpRestore`). Gen surface none (run tuning); runtime only; best paired with the Catch
  `/plan` (§4.1), which first puts a 0-priced Ball in the pool.
- **A re-import marks `pokemon.db` changed with identical data (S; found 2026-11-06 in the process pass,
  read-confirmed).** `PokemonImport.cs:140`, `EvolutionImport.cs:71` and `GameAvailabilitySeeder.cs:138` delete
  and re-insert their rows, so every run regenerates auto-increment IDs and the binary differs. It left the working
  copy dirty after three sessions (2026-11-02, 11-03, 11-05), and `git restore` then fails while the sqlite MCP
  server holds the file open.
  **DoR:** ✗ not Ready — gap **2**; the rest drafted:
  - **Acceptance:** two importer runs in a row leave `pokemon.db` byte-identical to the committed file
    (`git status` clean), with the same row content as today.
  - **Design:** ✗ open — upsert by natural key (species/evolution/game-version) instead of delete-and-insert, or
    keep the delete but insert in a stable order with explicit IDs. Read the three call sites first.
  - **Gen surface:** none (importer persistence, no rule or value changes).
  - **Gen 1 source:** n/a.
  - **Data vs runtime:** importer only; the re-run is the data change (`MoDirectives/data.md` 1).
  - **Quirk to assert:** an importer test that runs the evolution and availability writes twice on one database
    and asserts the row IDs are unchanged.
  - **Dependencies:** none. Not logic-heavy (persistence wiring), so the format and test steps, then `docs-cleanup`.

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
  `GEN_DIFFERENCES.md` → Status Quirks and `GENERATION_SEAMS.md` §2. Needs a `/plan` and both gates.
  **User ruling 2026-10-17:** its own item, behind a generation seam; "slightly modernized but functionally the
  same" is acceptable — the Gen 1 order of effects and its outcomes are the requirement.
  **DoR:** ✗ not Ready — gap **2** (the `/plan`); 1, 5 and 6 are drafted below for your confirmation, 7 is
  resolved. 3 (seam beside `FaintEndsTurnImmediately`) and 4 (`MainInBattleLoop`, `core.asm:413-462`) are
  answered. *Drafted:*
  - **Acceptance:** (a)–(c) each flip in a full-battle test, and the `RunDirectorTests` double-faint test is
    re-pinned to a path Gen 1 can produce.
  - **Data vs runtime:** runtime only.
  - **Quirk:** the faster creature's own poison tick lands before the slower one acts.
  - **Dependencies:** none. §3.1 "Leech Seed × Toxic counter" waits on this.
  - **Also covers (ruling 2026-11-06):** when the slower side's Roar ends a Gen 1 battle, the faster side's own
    residual has already ticked (`core.asm:426`, `:449`); with the flee ending the turn at once, ours never ticks it.
    HP carries between battles, so the per-creature residual order fixes this too. Gen 1 consequences: the faster
    side's poison can faint it before the slower side ever Roars, and Leech Seed heals the slower side first. When
    this lands, re-pin `ForceFleeTests.Gen1SlowerRoarEndsTheTurnAtOnce_TheRoarersPoisonNeverTicks`, which asserts
    only the Roar user's tick; the faster side's tick should then run.
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
each). Their seam and data-vs-runtime lines are already written in the text.

- [ ] **`Attributes` Special split:** `Special` → `SpAtk` + `SpDef` (keep `Special` as a Gen 1 computed alias);
  `Creature.BaseSpecial`/`DvSpecial`/`ExpSpecial` split in parallel, and `StatStages`/`StageStat` gain SpAtk/SpDef
  stages (`IBattleRules.OffensiveStage`/`DefensiveStage` already route the damage stage, so a Gen 2 rules set
  returns them with no `DamageCalculator` change).
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
  - *Items:* `ItemImport.ImportGen1BattleItemsAsync`, `ItemMapper.Gen1BattleItemNames`, `ItemMapper.Gen1Prices`, `ItemMapper.Gen1Descriptions`,
    `ApplyGen1Gameplay`.
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
  `SPRITE_PRESENTATION.md` §3.3–3.5. **Gen 1 quirk to carry (ruled 2026-11-07):** a Petal Dance repeat turn plays
  Thrash's animation, since pokered sets `wPlayerMoveNum = THRASH` (`core.asm:3534`) and the hit animation reads it
  (`:3174-3175`); `RampageContinues` (and its `MoveName`) will need the animation move projected.
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
- [ ] **A Fly or Dig user's sprite stays on screen (found 2026-11-09 by `pr-review`, Fly and Dig item) — S–M, ✗ not
  Ready.** The engine puts a Fly or Dig user out of reach on its charge turn, but the client keeps drawing it. In
  Gen 1 the sprite leaves the screen (Fly plays Teleport's animation, Dig a slide down; pokered `effects.asm`
  `ChargeEffect`, `:1014-1022`) and returns on the release turn. `ChargingUp` carries no out-of-reach field, and the
  glitch (a stranded user) has no event at all.
  **DoR:** ✗ gaps **1, 2, 4, 6**. *Drafted:* acceptance, the sprite hides when its owner goes out of reach and shows
  again when it comes back, including after a reconnect replay. Design open: which event carries the state (a field
  on `ChargingUp` plus a cleared signal, wired by hand per `ClaudeDirectives/engine.md` rule 8) and whether a stranded
  user stays hidden as in Gen 1. 3: the animation is theme. 5: wire (C# and TS) plus client. 7: none.
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
  merged; whether `/test` overlaps the G3 test step; whether `/sync` survives. *Gen surface:* none. *Gen 1 source:*
  n/a (tooling). *Data vs runtime:* neither. *Quirk to assert:* invoking a skill loads its knowledge base, checked
  by invoking each once. *Dependencies:* none.
  **DoR:** ✗ not Ready — gap **2**: a full `/plan` answers the three design questions. Starting recommendation:
  one skill each for `/plan` and `/dev`; fold `/test` into the test step's description; retire `/sync` unless a schema pass
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
  starter's name/level. The on-field sprite and the game-over card were fixed by R1d-d (`TODO_ARCHIVE.md` → *§2 R1d-d*, 2026-10-27);
  **the Continue card stays open here**: it reads a saved `Species` object from `localStorage` (`activeGame.ts`), so
  it needs a persisted-shape change. *DoR: ✗ not Ready — gaps 1, 2, 6 (acceptance, the saved shape, the quirk);
  its R1d-d dependency is met.*

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
  so the G3 test step runs `.\test.ps1 -Dotnet -Web`, reports that E2E did not run, and may only *recommend*
  the narrowest `.\e2e.ps1 -Spec <file>`.

**Open:**
- [ ] *(small)* **The client `RunFaulted` handler is unpinned.** The server half is covered (`GameSessionLifecycleTests`);
  nothing tests the client side (`clearActiveGame()` and the bounce to Title with the notice). Stands on its own;
  The R1d-c takeover panel did not close it.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 7**. *Drafted:* acceptance, a Vitest test pins that `RunFaulted` clears the
  saved game and returns to Title with a notice; the design (extract the handler logic or test it in place) is open.
  Gen surface none; runtime client.
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
- [ ] **E2E play helper hangs when the move grid is left open** (S, found 2026-10-28 during R1d-f; filed by user
  ruling). If `chooseBestMove` / `chooseMove` leaves `.move-grid` open without picking a move (seen with BULBASAUR
  deep in a run, likely around SOLAR BEAM's charge turn; unconfirmed from the trace), `playCurrentRunUntil`'s next
  pass clicks the hidden FIGHT button and waits out the whole test timeout instead of failing.
  **DoR:** ✗ not Ready — gap **2**: the design waits on confirming the trigger.
  - **Acceptance:** a left-open grid is closed (BACK) or its move picked on the next pass; a spec never waits on a
    hidden FIGHT button; a BULBASAUR run plays through a charge turn.
  - **Design (pending):** first confirm the trigger from the trace (`test-results/reconnect-…-chromium/trace.zip`
    of the 2026-10-28 BULBASAUR run, if still on disk); then the helper checks for an open grid before clicking FIGHT.
  - **Gen surface:** none. **Gen 1 source:** n/a (the client's handling of a two-turn move, not the mechanic).
    **Data vs runtime:** E2E helpers only. **Quirk:** "a charge turn never strands the play loop".
    **Dependencies:** none.
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
- [ ] *(small)* **Deduplicate the test helpers — E2E half** (M, from §7.6 V14, ✗ not Ready). The seeded-start
  preamble in three E2E files (`helpers.startBattle`, `encounter-map.spec.ts` `gotoOpeningRouteChoice`,
  `reward-drop.spec.ts` `startSeededRun`) (read-confirmed). The C# `Fighter` half shipped (see the archive).
  *Drafted mini-DoR:* acceptance — one seeded-start helper in `e2e/helpers.ts`; no behaviour change. Gen-variable
  surface: none. Test: the user's run of the three affected specs. **Gap:** the user's go-ahead on the E2E runs.
  **DoR (V16):** ✗ not Ready — confirmed as above.

---

## 7. Infrastructure, tech debt and hygiene

### 7.1 Infra, CI/deploy and scripts (unverified)

- **Run the fast suites before deploying (M, low priority — user ruling 2026-10-11).** *Verified (§7.6 V2):*
  `fly-deploy.yml` goes checkout → setup-flyctl → `flyctl deploy`, and it is the repo's only workflow, so no suite
  ever runs on GitHub; an RC tag on a red commit deploys anyway, and the local pre-commit hook is the only net.
  **Not Ready:** needs a design pass for which suites (the G3 test step's set: .NET + `tsc` + Vitest), the CI
  .NET (SDK 9.0.200 pin) and Node setup, `needs:` ordering ahead of the deploy job, and how it shares setup with
  §6 "CI E2E step". Acceptance and test approach to be written then; the result is only observable on an RC
  deploy or a non-deploying trigger added for the purpose.
  **DoR (V16):** ✗ not Ready — gaps **1, 2, 6, 7**, as the entry says. *Drafted:* acceptance, a red commit's RC tag
  fails before `flyctl deploy` runs (proven on a throwaway tag or a `workflow_dispatch` dry run). Design this
  together with §6 "CI E2E step". Gen surface none; CI only.
- **Dev-script and pre-commit-hook gaps — only Part A remains (parked).** B and C are done (see
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

Nothing open. The INFO tab's later-generation rows were deleted 2026-10-31 (`TODO_ARCHIVE.md` → "§7.4 The INFO tab's
later-generation rows").

### 7.5 Speculative, unverified — check before filing as real

Nothing open. The list was verified and its findings fixed or filed in §7.6 V15 (`TODO_ARCHIVE.md` → "§7.6 V15 —
speculative list (§7.5)").

### 7.6 Verification pass over the unverified findings — complete

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
  and a rule for evolve-only forms change nothing in play. Only Lickitung was worth fixing (shipped 2026-10-18; `TODO_ARCHIVE.md` → "§3.2 Lickitung wild in Yellow").
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
- **WAIVED (user 2026-10-18): no test for a generation response with no list at all** (e.g. `{}`; the `?.moves` /
  `?.pokemon_species` null path in the importer). The empty-list and no-usable-URL shapes are pinned; this third one
  is a tiny risk and the test was declined as not worth the cost. Don't re-file it.
- **Max Revive** is out of `items.db` and the roster; the scaffolding a future generation needs is the schema, not a
  stray row (§4.4).
- **Repo docs and comments can be wrong about Gen 1/Gen 2:** `GEN_DIFFERENCES.md` and three code comments misstated the
  Gen 2 type-chart changes (corrected 2026-10-03 against pret/pokecrystal). Verify against the primary source.
- **The 2026-07-19 repo-wide PR-audit is fully closed** — don't re-file "Repo-wide PR-audit findings" as an open
  section.
