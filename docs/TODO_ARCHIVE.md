# Battle Sim – Done / Archive

Historical record of completed work, split out of `TODO.md` to keep the live task list (and the
per-session read) small. Nothing here is pending. The per-batch logs are kept verbatim because they
double as a fidelity record and the `seam-reviewer` references these patterns.

> **Live tasks:** `TODO.md` · **See also:** `CLAUDE.md`, `AI_CONTEXT.md`, `DESIGN_GUIDES.md`, `DEV_STANDARDS.md`

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
*Encounter Logic — Phase 4* (still active in `TODO.md`; its Stages 1a–3 are archived further below under
*Encounter Logic (roguelite run layer)*).

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
creature** open defect (TODO.md, user ruling 2026-07-15) — not by implementing Gen 1's participant split, but by
**superseding** it with a deliberate roguelite deviation the user chose instead. The defect's *evolution* half was
fixed in the same change. See `TODO.md` → *Switched-in creature is the active creature* for the closing record of
that defect (evolution fixed, XP/Stat-Exp superseded, one small residual sweep still open there).

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
  provably unaffected). Bench XP itself is silent (no per-member `ExperienceGained`) until it produces a level-up.
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
was ever sent out — a wider, more generous, always-on Exp-All-style grant. **Today** there is no observable
conflict with the "switched-in creature is the active creature" requirement: voluntary switching isn't
implemented yet, and a forced switch always leaves the outgoing lead fainted (excluded from any share anyway), so
the only "switched-in" case that exists — the finisher — is simply the active creature, paid in full, exactly as
before this change. **The divergence is real for a *future* case, by design:** once voluntary in-combat switching
ships, a creature that fought part of a battle and was then swapped out *while still alive* will earn only the
flat `BenchXpShare` — the same as a bench member that never entered — rather than a participation-weighted share.
That is intended (the whole point of choosing the roguelite share over the Gen-1 participant split), but the
[**In-Combat Switching**](TODO.md) work should treat it as a decision already made here, not re-open it as a bug.

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
