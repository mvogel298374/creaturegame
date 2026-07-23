# Battle Sim – TODO List

> **Active tasks only.** Completed work lives in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only for the
> history of a finished item. **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) ·
> `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions).

## Current state (2026-07-19)

The Gen 1 battle engine is **feature-complete** (all 165 moves, XP & level-up, learnsets, AI move selection,
EV / Stat-Exp gain, evolution, in-battle item system incl. **Revive/Max Revive**), and the roguelite run layer on
top is playable end-to-end: the **Encounter Logic** biome-graph run (biome pick → randomised 4–6 nodes → Poké
Center → next biome, per-run randomised map, depth-scaled foes), the **Run Economy** (gold + rewards), the
**Reward Choice** modal (pick-1-of-3 rarity rewards), the **level-aware XP curve + trainer bonus**, the **Innate
Party XP Share** (the living bench shares in every battle's XP/Stat-Exp and evolution alongside the active
creature), and **Revive Items** (in-battle party revive, Boss-reward + rare-shop only) are all done and archived
(→ `TODO_ARCHIVE.md`).

**Next up, in priority order:**
1. **In-Combat Switching** — the voluntary, any-turn SWITCH turn-action (its own documented core feature below),
   now unblocked: Phase 4 **Stage 3 (forced-switch-on-faint) is DONE**, so `Battle` already holds the party and the
   forced + voluntary send-in path exists, and the **Switched-in creature is the active creature** defect that used
   to gate this on the participant split is resolved (evolution fixed, XP/Stat-Exp superseded by the **Innate
   Party XP Share** — see `TODO_ARCHIVE.md`). `/plan` first; a good `opus-engineer` candidate (central `Battle` /
   `AttackAction` turn-resolution change).
2. **Item Acquisition · Bag Persistence · Catch** — the deferred cluster, unblocked by the acquisition channels.
   *(Item acquisition itself is already done via the Run Economy; bag persistence + catch remain.)*
3. **Game Loop & Progression** — save layer (`save.db`); party + between-biome lead + forced-switch are done.

*(Small residual, not urgent: **sweep other end-of-battle effects that assume the starting lead** — see
[**Switched-in creature is the active creature**](#switched-in-creature-is-the-active-creature--resolved) below.)*

*(**Phase 4 shipped in full** — the roster, both acquisition channels, between-biome lead swap, and
forced-switch-on-faint. Stage 3's end-of-battle defect (wrong requirement pins in its own plan, not the domain)
is now **resolved** (2026-07-18) — evolution fixed, XP/Stat-Exp superseded by the Innate Party XP Share; see
[**Switched-in creature is the active creature**](#switched-in-creature-is-the-active-creature--resolved) below
for the closing record.)*

*(The **Run Economy** — gold, rewards, the transient bag, and the spend-gold **Shop node** — plus the
**Encounter Map** route overlay and the **Difficulty easing** tuning pass are all done and archived
(→ `TODO_ARCHIVE.md`).)*

Lower priority / opportunistic: E2E flakiness stabilisation (`status.spec.ts` **fixed 2026-07-15** — root cause
was a spec asserting a transient badge, not an engine bug; see *Browser-Based UI Testing* for the seed-≠-determinism
lesson it taught), Web UI polish (move-specific animations), Multi-Generation groundwork, User Documentation,
**Settings Menu** (sound volume + difficulty→XP bonus both ✅ done — see its own section below; the
difficulty dial's self-referential-scaling limitation is a known, user-waived follow-up, not open work).

---

## Encounter Logic — Phase 4 ✅ COMPLETE (2026-07-15)

Phases 1–3 (biome model + type-filtered pool, `IEnemyArchetype` tiers + depth bands, `RunDirector` event model
+ live biome-graph map + tuned Boss-capped node curve) are **done and archived**, along with the four follow-on
refinements — per-run biome-map randomisation, randomised 4–6 route length, Roar/Whirlwind→`ForceFlee`, and the
opening-route favourable-matchup guarantee. Full per-phase record (design, pins, seam reviews) in
[`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) → *Encounter Logic*.

**Phase 4 — Acquisition & the Roster** (the remaining `ENCOUNTER_DESIGN.md §4` piece, and the bridge into the
*Item Acquisition · Bag Persistence · Catch* cluster below). **`/plan` done** (2026-07-12) — the full design
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
  handling through Stages 1–2 stands: the lead fainting still ends the run.)* Switching mid-fight is a **separate,
  larger** feature — see
  [**In-Combat Switching**](#in-combat-switching--voluntary-in-battle-party-switching-planned-core-feature) below.
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
  participant split. See
  [**Switched-in creature is the active creature**](#switched-in-creature-is-the-active-creature--resolved) for
  the closing record.

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
  level and the nameplate needs it.)* This is the Battle-holds-party groundwork the voluntary
  [**In-Combat Switching**](#in-combat-switching--voluntary-in-battle-party-switching-planned-core-feature) feature
  builds its SWITCH turn-action on.

  **`/plan` (2026-07-14) — the design as built:** When the **active** creature faints and a bench member is
  still alive, the run **does not end**: the player **picks** the replacement from a forced (non-dismissable)
  party-select modal — "player chooses", the faithful Gen-1 forced-switch, decided with the user 2026-07-14 —
  and it comes in against the **same (damaged) enemy**; the run ends only when the **whole party** is down.
  **This is where `Battle` first learns about the party** — it must hold the benched creatures so it can bring in
  the next one against the live enemy — and it is deliberately the *choose*-a-replacement path (not auto-send-next)
  so it front-loads the in-battle party-select modal + `ChooseSwitchInAsync` prompt that the deferred
  [**In-Combat Switching**](#in-combat-switching--voluntary-in-battle-party-switching-planned-core-feature) feature
  reuses (forced + voluntary **share the send-in path**, so that later feature shrinks to "add the voluntary SWITCH
  turn-action trigger + enemy-AI switching"). Voluntary in-battle switching + `save.db` stay beyond Phase 4.

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
    > plan. Corrected by the user 2026-07-15, **resolved 2026-07-18** → see
    > [**Switched-in creature is the active creature**](#switched-in-creature-is-the-active-creature--resolved)
    > below. Kept visible rather than silently deleted: the wrong pin is why the defect shipped.
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
(see *Switched-in creature is the active creature*, resolved 2026-07-18 — the earlier "only the lead earns XP (no
Exp Share)" pin was wrong; the eventual fix was the **Innate Party XP Share**, a deliberate deviation from the
literal Gen-1 participant split, not a re-implementation of it); major status persists on benched creatures per
the carry model.

**Out of scope this phase:** the in-battle Poké Ball throw + `BallItemEffect` + catch-rate-vs-HP formula (stays
in the Catch cluster below); voluntary in-battle switching (its own planned core feature —
[**In-Combat Switching**](#in-combat-switching--voluntary-in-battle-party-switching-planned-core-feature));
`save.db`/`PlayerDbContext` persistence + cross-run meta-progression; the **Exp. Share / Exp. All item**
(a held item that pays a *non-participant* — distinct from the innate party-wide XP share that shipped 2026-07-18,
see *Switched-in creature is the active creature* below). *(Revive, which needed a fainted-but-revivable party
member, shipped 2026-07-19 on top of this stage's `Party` — see `TODO_ARCHIVE.md` → Revive Items.)*

---

## Switched-in creature is the active creature  ⟵ RESOLVED (2026-07-18) — one small residual open

**The requirement, in the user's words:** *"A switched-in Pokémon is for all intents and purposes the active
Pokémon, therefore all effects that happen at the end of battle happen to it as well. So it can evolve, it shares
XP, EVs, everything. Just like it would work in Gen 1 / generically in Pokémon."*

**There is no special case for a switched-in creature.** It is not a second-class participant, it does not "wait
until its next clean win", and it is not excluded from any end-of-battle effect. Anything the starting lead would
receive, a creature that took the field receives on the same terms. This governs the forced faint-switch (shipped)
and the voluntary SWITCH action (planned) alike.

### Why this shipped wrong (keep this — it is the reason the gate is being tightened)
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
  **No live conflict** with the requirement above: voluntary switching isn't implemented yet, and a forced switch
  always leaves the outgoing lead fainted (excluded from any share anyway), so today's only "switched-in" case —
  the finisher — is simply the active creature, paid in full, same as before this change. Full write-up →
  `TODO_ARCHIVE.md` → *Innate Party XP Share*. *(The **Exp. Share / Exp. All item** — a held item that pays a
  non-participant — stays deferred; it's a separate feature from this innate, always-on party share.)*
- [x] The invariant is now written into `docs/STATE_MODEL.md` (the party-wide end-of-battle effects section) as a
  documented fact, not a plan claim — future `requirements-review` runs can cite it directly.
- [ ] **Residual: sweep other end-of-battle effects that assume the starting lead.** The rule is general;
  evolution and XP/Stat-Exp are now confirmed party-wide, move-learning already rides the per-member evolution
  loop, and carried status already reads `s.Player` (the finisher) — but nothing has specifically audited the
  *rest* of the post-battle path for a stray `player`/`levelBefore` reference. Small, cheap, not urgent; no known
  instance today.

---

## In-Combat Switching — voluntary in-battle party switching *(planned core feature)*

**Status: planned, not started.** Confirmed a core feature we *will* build (user, 2026-07-13) — a first-class
"SWITCH" turn action so the player can swap the active creature **mid-battle**, like the mainline games. This is
distinct from — and much larger than — Phase 4's lead management:

- **Stage 1d** (above) only picks the lead **between biomes**; no engine change.
- **Stage 3** (above) only handles a **forced** switch when the lead faints.
- **This feature** is the **voluntary, any-turn** switch: choose SWITCH instead of FIGHT/BAG/RUN, pick a benched
  creature, and it comes in at the cost of your turn.

**Why it's a central `Battle` change (the reason it's deferred, not incidental).** `Battle` is constructed with a
*single* `player` creature and a *single* `enemy`; the whole engine (turn sorting, `AttackAction`, status ticks,
the SignalR `TurnStarted`/turn events, `IBattleInput`) assumes one creature per side. Phase 4 Stage 1c deliberately
kept it that way ("`Battle`, which only knows one creature, is untouched"). Voluntary switching breaks that
assumption and is best built **on top of Stage 3's groundwork** (which is where `Battle` first holds the party).

**Scope when built:**
- **Engine:** `Battle` takes the `Party` (or the benched creatures) for the player side; a new SWITCH
  `TurnChoice` / `IBattleAction` resolved at **switch priority** — Gen 1 order: the swap happens *before* attacks,
  and the incoming creature then takes the opponent's move that turn (switching **costs** your turn). Reset the
  outgoing creature's transient `BattleState`; bring in the incoming creature's permanent half + carried major
  status.
- **Gen 1 fidelity (DoR #4):** switching **resets stat stages and volatile conditions** (confusion, Leech Seed,
  Disable, substitute, two-turn/charge lock, etc.) but **keeps major status** (sleep/poison/burn/etc.) on the
  creature; **partial-trapping moves (Wrap / Bind / Clamp / Fire Spin) trap the opponent and block its switch**
  while active. No hazards (no Spikes in Gen 1), no abilities, no Pursuit, no Baton Pass — all post-Gen-1, so
  *out* of scope by construction. Confirm against the type/rules seams (`GENERATION_SEAMS.md §5.0`) — switching
  order/trap rules are generation-variable and belong on `IBattleRules`, not hardcoded in `Battle`.
- **Events + wire:** new `CreatureSwitchedOut` / `CreatureSwitchedIn` battle events (+ `SignalRBattleEventEmitter`
  projection + field-level `WebEventContractTests` guards — the recurring web-event field-projection gap); the
  enemy AI may also switch (a later refinement — start with player-only).
- **Frontend:** a SWITCH entry in the action menu → a party-select modal (reusing the `PartyStrip` / roster
  panel), the sprite swap on the canvas, and the timeline arms for the new events.
- **Interactions to get right:** a forced faint-switch (Stage 3) and a voluntary switch share the send-in path;
  Struggle/lock-in and a trapped creature must correctly *disable* the SWITCH option; the turn is consumed even if
  the incoming creature faints to the foe's move (a valid Gen 1 outcome).

**Dependencies:** **Stage 3 (forced-switch-on-faint) is DONE (2026-07-15)** — `Battle` now holds the party
(`playerParty`), the forced send-in path exists (`TrySwitchInAsync` → `SetLead` + reset + carried-status re-apply +
`CreatureSwitchedIn`/`PartyUpdated`), and the client has a party-select send-in modal (`SwitchInModal`) + a
`swapPlayerCreature` sprite swap. So this feature now just adds the **voluntary trigger**: a SWITCH `TurnChoice` /
`IBattleAction` at switch priority (the swap resolves before attacks; the incoming creature then takes the foe's
move that turn), the partial-trapping-blocks-switch rule (`IBattleRules`), the SWITCH action-menu entry, and later
enemy-AI switching. Independent of `save.db`. **Effort:** still a central refactor of `Battle` / `AttackAction`
turn resolution; a good candidate for the `opus-engineer` subagent and a dedicated `/plan` pass before implementation.

---

## Item Acquisition · Bag Persistence · Catch  ⟵ deferred cluster, gated on Encounter Logic

**One interlocked cluster, deliberately deferred together** — each depends on the previous and on the
Encounter Logic gate:
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
  replacing the fixed loadout. *(A between-encounter **Shop** — spending gold — is the remaining follow-up.)*

### 2 — Bag persistence · once acquisition defines what a bag holds
- [ ] Persist the `Bag` to `save.db` / `PlayerDbContext` (rides on the broader save layer — see **Game Loop**).
- [ ] Decide bag scope: **per-run** (lost on death) vs. **meta-progression** (carries across runs). The
  acquisition design drives this.

### 3 — Catch / Poké Ball effect (one acquisition channel) · Gen 1 reference
- [ ] `BallItemEffect : IItemEffect` for `ItemCategory.Ball`, registered in `ItemEffects.All`; extend `Battle`
  with a "catching" state/outcome.
- [ ] Gen 1 formula: `floor((MaxHP × 3 − HP × 2) × CatchRate / (MaxHP × 3))` vs a 0–255 roll (per-ball modifier
  lives in the formula, not the `Item` row).
- [ ] `CaptureAttempted(string TargetName, bool Caught)` event; `BattleEnded` variant `reason: "Caught"`.
- [ ] Caught creature → party (needs party / switching — see **Game Loop**); closes the acquisition loop.
- [ ] Unlocks the dormant **stone evolutions** (`Stone` trigger + `IEvolutionRules.StoneUsed` are built and
  waiting on a bag).
- [ ] Phaser throw / shake / catch animation.

---

## Game Loop & Progression

**Prerequisites:** Catch Mechanic, `PlayerDbContext` / `save.db`. Intentionally deferred until combat fidelity
is fully ironed out (the battle sim is the foundation). The **Endless Battle Chain** (done) is the first minimal
slice; the items below are what it deliberately leaves out.

- [ ] Catch → Pokémon added to party (up to 6). **The roster half is done** — the `Party` container, both
  post-battle acquisition channels, the between-biome lead choice and the forced faint-switch all shipped in
  **Encounter Logic Phase 4** (Stages 1a–3 ✅, complete 2026-07-15). What remains here is only the **in-battle
  ball throw** as a third acquisition channel — see the Catch cluster above; it deposits into the existing `Party`.
- [ ] **Voluntary in-battle switching** — a SWITCH turn action to swap the active creature mid-fight. Its own
  documented core feature (planned, user-confirmed 2026-07-13): see
  [**In-Combat Switching**](#in-combat-switching--voluntary-in-battle-party-switching-planned-core-feature).
  **Now unblocked:** Phase 4 Stage 3 wired the party into `Battle` and built the shared send-in path.
- [ ] Progressive difficulty beyond the current `targetBst = lead BST + depth × 10`; trainer encounters at
  milestones.
- [ ] `PlayerSave` / `SavedCreature` models in `save.db`; auto-save after each battle; party-management UI.
- [ ] **Stone evolutions** — the only remaining evolution piece, gated on the bag (Catch). The `Stone` trigger
  + `IEvolutionRules.StoneUsed` are built and dormant.
- [x] **Cross-encounter status persistence** — DONE (2026-06-10); major status carries across chain encounters,
  volatiles reset per battle. See `STATE_MODEL.md §2` and `TODO_ARCHIVE.md`.

---

## Settings Menu — sound volume + difficulty (XP bonus) controls

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
  > **Flagged to flesh out later:** either rename to something honest ("Leveling Pace") or add a real
  > difficulty-shaping axis independent of the self-referential scaling (e.g. a flat enemy level/BST offset
  > that doesn't re-normalize to the player) — not scheduled, no target date.

---

## Web UI — Polish

Stack: React 18 + TypeScript + SignalR + Phaser 3. (Canvas & core animations done — see archive.)

- [ ] **Move-specific attack animations (grouped, not per-move).** Today every move plays the one generic lunge
  + type-neutral white tint + `playDamageShake`. Map each move to one of ≈5–7 **animation families** keyed off
  data we already have (`DamageType`, `AttackType`) + a few special cases — believable variety without 165
  bespoke clips.
  - **Families:** *physical contact* (current lunge, keep) · *projectile/ranged special* (sprite travels
    attacker→target, no lunge) · *status/self-buff* (glow/pulse on user, no lunge) · *two-turn/charge* (charge
    glow turn 1, release burst turn 2) · *multi-hit/flurry* (repeat a jab in step with `MultiHitCompleted`).
    Cheap layered win: tint the flash/shake by the move's **type colour** (reuse the `TypeBadge` palette).
  - **Plumbing (the real work, mind the seam):** `MoveUsed` carries only `(AttackerName, MoveName)` — the client
    can't see the *enemy's* move type/category. Project `DamageType` + `AttackType` onto `MoveUsed` + its
    `SignalRBattleEventEmitter` mapping with the field-level guard (the recurring **web event field-projection
    gap** — see the memory + `WebEventContractTests`). Then a pure `moveAnimationFamily(type, category, slug)`
    map (unit-testable like `timeline.ts`), new per-family `BridgeCommand`s + `BattleScene` handlers, each still
    emitting `animationComplete` so the timeline's `awaitAnim` contract holds.
- [ ] *(small)* **Escape = B-cancel on the prompts that have a negative action.** Surfaced by the `<Modal>` refactor
  (2026-07-17) and deliberately left out of it — a refactor commit shouldn't carry a behaviour change. Today Escape
  does nothing on every run prompt, which is right for the four that are *required* choices (`RouteChoice`,
  `RewardChoice`, `LeadChoice`, `SwitchIn` — there is no answer a dismissal could send). But four others do have a
  negative answer, and Gen 1's B-cancel is exactly that: evolution→CANCEL, acquisition→DECLINE, shop→LEAVE,
  move-replacement→don't-learn. *Fix:* give those four `dismiss={{ onEscape: () => <their decline> }}` — the wrapper
  already supports it; the escapable branch of `ModalDismiss` currently has no caller. Needs Vitest coverage and a
  `requirements-review` pass on the B-cancel claim (per the *plan-asserted domain facts are claims* lesson).
- [ ] `ConsoleInput : IBattleInput` — numbered move menu for terminal play (low priority).

---

## Browser-Based UI Testing (Playwright)

Suite lives in `ClientApp/e2e/` (`npm run test:e2e`). Playwright drives the React DOM; the Phaser canvas is
tested through the `mitt` bridge (assert **event ordering**, never wall-clock durations — the #1 flake source).

**Done (2026-07-05):**
- [x] **Seed plumbing** — `StarterSelection` forwards an optional `?seed=<int>` URL param into the `/start`
  request (backend already accepted `Seed`), so an E2E can pin a repeatable run. `?e2e=1` still sets
  test mode. react-router drops the query on nav from the title, so seeded specs land directly on `/select?seed=`.
  > **A seed is not by itself determinism** (learned the hard way, 2026-07-15 — it flaked two specs). The seed
  > fixes the *server's* RNG stream, but the **client's move sequence is what draws from it**: a spec that races
  > (polling loops, `waitFor` timeouts, a swallowed click) submits moves on different turns under load, which
  > shifts every later roll and plays out a different run. A seeded spec is only deterministic if the driving loop
  > is **paced** — settle each turn (wait for the action menu to come back) before the next input. Otherwise write
  > the assertions not to care (retry/walk seeds). See `status.spec.ts` and `forced-switch.spec.ts`.
- [x] **Run Economy reward-modal E2E** (`reward-modal.spec.ts`) — seed 31 / CHARIZARD @ L50 lays the first
  biome node as a **Treasure**, so the modal fires right after the opening route pick (no battle to win). Asserts
  the modal + title, a gold line (`+N₽`) + item line, the **gold HUD credit** (was `₽0`), and OK →
  `acknowledgeReward` → modal closes + run continues into the next node. **Closes the known live-verification
  gap** — the reward modal + gold credit are now observed in a browser, not just unit/integration.
- [x] **E2E harness recovered from spec-rot** — the suite was fully red: **biome mode (Phase 3b-2)** added an
  opening route-choice modal that blocked before every battle (the `startBattle` helper didn't answer it), and
  the **Run Economy** starting bag stopped seeding `BattleStatBoost` items. Fixed `startBattle` to pick the
  opening biome (`chooseBiomeIfPresent`); fixed `battle.spec` (the first log line is now the biome banner, not
  the VS line); removed the two `item-use` specs (X ATTACK / GUARD SPEC aren't battle-1 obtainable anymore — the
  item-effect logic stays covered by `ItemEffectTests`, bag grouping by `bag.test.ts`).

**Remaining (in priority order):**
- [ ] **`reward-drop.spec.ts` is red — seed-31 drift** (found 2026-07-19, pre-existing): the spec pins seed 31
  laying a **Treasure** as the first biome node, but the `.reward-modal` no longer appears — the node layout under
  that seed has drifted (some earlier commit added/moved an RNG draw before node planning). Verified NOT caused by
  the 2026-07-19 immunity-gate fix: it fails identically against a pre-fix backend, and the spec's failure point
  (run start → first node) precedes any battle. *Fix:* walk seeds for a new Treasure-first seed (the documented
  "seed ≠ determinism" remedy), or make the spec robust to node order (play until the first Treasure).
- [x] **Stabilise inter-test E2E flakiness (a seed-determinism pass)** — DONE (2026-07-08). `startBattle` gained
  an optional `seed` param: when given it lands directly on `/select?e2e=1&seed=…` (the `reward-drop.spec`
  pattern; the level slider lives on that screen so a custom `level` still works), pinning the whole run — enemy,
  DVs, moves, biome offer, every battle roll, AI choice. Converted the flaky coin-flip specs to seed 1:
  `battle-ui-cues` + `stat-stage` (seeded `startBattle`), `status` (was also flaky — same treatment), and
  `level-up` (both tests: replaced the `reachLog` restart loop with seeded `startBattle` + a new `playToLevelUp`
  helper that stops at the level-up line *without* dismissing the reward modal the test asserts). `reachLog`
  stays for `battle`/`endless-chain`/`learnset` (not flaky; their retry keeps them reliable). **Verified:** full
  `npm run test:e2e` green across 3 consecutive runs (21 passed each, `retries: 0`), and the converted specs run
  in seconds instead of coin-flip minutes.
- [ ] **Other between-encounter modal E2Es** — same seeded/blocking-modal shape as the reward modal, now
  unblocked by the seed plumbing: Poké Center recovery Heal/Skip, move-replacement forget/decline, evolution
  Allow/Cancel (Gen 1 B-cancel).
- [ ] **CI step** (or `test.ps1 -StartStack`-adjacent) that boots backend + frontend, runs headless, tears down.
  **This is the root cause of the rot going unnoticed** — E2E isn't gated in CI and `test.ps1` skips it when the
  stack is down, so a red suite stayed invisible. Wiring E2E into the gate is what prevents a repeat.
- [ ] `data-testid` attributes — **deferred**: specs lean on stable semantic classes (`.btn-new-game`,
  `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`). Add testids only where a class
  proves brittle.
- [ ] §8 visual-regression canvas snapshots — skipped (maintenance cost).

## Frontend Unit Coverage (Vitest)

Test-harness audit (2026-07-05) — the .NET engine + event-wire seam are near-exhaustively covered; the gap was
the frontend. Closed the pure-logic gaps and pinned the suite split.

**Done (2026-07-05):** extracted the pure `battleReducer` out of `useBattleHub` (`hooks/battleReducer.ts`,
type-only imports → zero runtime deps) and added `battleReducer.test.ts` — the edge transitions a live
playthrough can't deterministically force (name-mismatch HP/status no-ops, `XP_GAIN` clamp, the level-up→
move-replacement supersede, the `BATTLE_STARTED` enemy-nameplate reset, biome-choice which has no E2E spec).
Plus `format`/`fetchError` unit tests (the backend-unreachable path is invisible to E2E). 84 → 107 Vitest tests.

**The suite-split rule (so future tests land in the right place):** Vitest owns **pure decision logic**
(input → exact output, especially branches E2E can't force or that an assembled-state test hits trivially).
Playwright owns anything needing the **full stack or the DOM** (rendering, flows, modal gating, event/animation
ordering). *Do not* add a second DOM harness (`jsdom`/RTL) to re-assert what E2E already renders — the one real
component-gating gap (the Run Economy reward modal) is closed by a **seeded Playwright spec** (see Browser-Based
UI Testing above), not RTL.

**Open (opt-in, low urgency):**
- [ ] **`GameSessionManager` connection lifecycle** — reconnect rebind, abandon grace, pending-session eviction
  TTL, and the run-loop `Task.Run` are covered by *neither* suite (they're entangled with `IHubContext` +
  `Task.Run` + wall-clock timers). Regression-insurance only: the reconnect behaviour is a settled/validated
  edge, not a suspected bug. Would need an injectable clock to unit-test the timing without real delays.

---

## Multi-Generation: Data Model & Schema

Deferred to the Gen 2 sprint. (The stat-selection abstraction — the only piece to do now — is done.)

- [ ] **`Attributes` Special split:** `Special` → `SpAtk` + `SpDef` (keep `Special` as a Gen 1 computed alias);
  `Creature.BaseSpecial`/`DvSpecial`/`ExpSpecial` split in parallel.
- [ ] **`PokemonSpecies` per-generation schema:** separate timeless identity (`Id`, `Name`, `CatchRate`,
  `BaseExperience`, `PokedexEntry`, `GrowthRate`) from a new `PokemonSpeciesGenData` table (`SpeciesId`,
  `Generation`, types, base stats; Gen 3+ adds abilities). Importer stores one row per species per generation;
  engine queries by active generation. *(PokeAPI has no `past_stats` — Gen 1 stat corrections need a
  corrections table or separate source.)*
- [ ] **Move per-generation data:** a generalisation, not a rewrite — resolve a field for gen *G* as the
  earliest `past_values` entry whose version-group generation is **> G**, else the current value ("earliest =
  Gen 1" is the *G=1* case). Store one `Attack` row per `(moveId, generation)` (mirror the learnset model) or
  resolve on demand; make the layer-2 override table per-generation too. Keep mechanic/formula differences on
  the **seams**, never in per-gen move data.
- [ ] **Generation filtering:** `Attack.GenerationIntroduced` + `PokemonSpecies.GenerationIntroduced` (set on
  import); `EncounterSelector.PickByBst` / `BuildCreature` filter by `<= activeGeneration`;
  `GetSpeciesForGenerationAsync(int)` / `GetMovesForGenerationAsync(int)` replace the unfiltered `ToListAsync()`.

---

## User Documentation

Battles are fully playable now — docs won't describe a moving target.

- [ ] `/help` route or modal — starter selection, battle controls, status icons, level picker.
- [ ] Expand `README.md` — architecture decisions (two-DB model, `IBattleRules` pattern, how to add a move
  effect / a generation).
- [ ] `GEN_DIFFERENCES.md` (written) — adapt into a player-facing "what makes Gen 1 different" explainer.

---

## Tech Debt / Cleanup

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

**Still open** (filed 2026-07-16 from a repo-wide structural review — ranked by cost-of-deferring, not size):

- [ ] *(low, watch — do not refactor speculatively)* **`AttackAction` still has three large methods**:
  `ResolveDamage` (145 lines), `ExecuteAsync` (140), `ResolvePreDamageGates` (112), despite the earlier split
  archived as (B). It is central and well-tested; revisit only if a change makes it hurt.
- [ ] *(low)* **`Console.WriteLine` is the web layer's entire logging strategy** — no `ILogger<T>` anywhere
  (`GameController`, `GameSessionManager`, the importer's catches). Fine for local dev; it's the one modern
  ASP.NET convention the repo skips, and it will matter the first time something needs debugging on Fly.io
  (structured levels, category filtering, log scraping). Filed from the 2026-07-19 audit's design review; do it
  as a mechanical pass when a real debugging need first bites, not speculatively.
- [ ] *(low, watch — do not refactor speculatively)* **`Battle`'s constructor is at 12 parameters** and heading
  where `RunDirector`'s was before `RunDirectorOptions` (archived 2026-07-16). Apply the same precedent — a
  `BattleOptions` record for the optional tail (rules/emitter/rng/bag/escapable/trainer/runRules/party) — the
  next time a feature (likely **In-Combat Switching**) has to touch the signature anyway. Not worth a
  standalone churn commit: every call site is a test or the run layer, and both are stable.

*(The 2026-07-19 repo-wide PR-audit is now fully closed — all five findings resolved: four fixed & archived in
`TODO_ARCHIVE.md`, the DB-services try/catch convention decided above, and the `SignalRInput` cancel/prompt race
deliberately waived by the user (memory `project_waived_cancel_race`). Don't re-file "Repo-wide PR-audit
findings" as an open section.)*

### Known Gaps
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector exists.
- Enemy Pokémon do not evolve — wire into level-up when Game Loop is built.
- **Endless-chain double-faint** — tested (2026-06-12): a mutual end-of-turn DoT double-faint counts as a loss,
  pinned by `BattleRunnerTests.Runner_DoubleFaintFromEndOfTurnPoison_CountsAsLoss_NotAWin`.
- ~~**Phantom stat-cap message**~~ — **FIXED 2026-07-19** (see `TODO_ARCHIVE.md` → *Stat-cap message fidelity*).
- **Fly deploy must stay single-machine** — `GameSessionManager` keeps run state in-process with no shared
  store, so a 2nd machine 404s any plain REST call (e.g. CHECK POKEMON) that Fly's proxy routes to the machine
  that never saw the run's `/start` call. `flyctl deploy` defaults to `--ha=true`, which recreates a 2nd
  machine on every deploy; the workflow now pins `--ha=false` (fixed 2026-07-23, live bug). Don't remove that
  flag or bump `min_machines_running`/scale count until session state is externalized (`save.db`). Full
  write-up → `ARCHITECTURE.md` §2.7 (Web session lifecycle).

---

## Database Architecture (reference)

**Two-database model:**
- `pokemon.db` / `PokemonDbContext` — species, base stats, types, growth/catch rates, learnsets, game
  availability, evolution chains.
- `moves.db` / `MovesDbContext` — moves, damage type, accuracy, PP, stat/status effects.
- `items.db` / `ItemsDbContext` — battle-usable items (Gen 1 roster + gameplay numbers).

**Where new tables go:** Pokémon-world data (egg groups, …) → `pokemon.db`; move-world data → `moves.db`; item
data → `items.db`; player save state (party, caught Pokémon, bag) → `save.db` / `PlayerDbContext` (deferred
until Catch).
