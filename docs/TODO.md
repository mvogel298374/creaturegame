# Battle Sim – TODO List

> **Active tasks only.** Completed work lives in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only for the
> history of a finished item. **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) ·
> `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions).

## Current state (2026-07-08)

The Gen 1 battle engine is **feature-complete** (all 165 moves, XP & level-up, learnsets, AI move selection,
EV / Stat-Exp gain, evolution, in-battle item system), and the roguelite run layer on top is playable end-to-end:
the **Encounter Logic** biome-graph run (biome pick → randomised 4–6 nodes → Poké Center → next biome, per-run
randomised map, depth-scaled foes), the **Run Economy** (gold + rewards), the **Reward Choice** modal (pick-1-of-3
rarity rewards), and the **level-aware XP curve + trainer bonus** are all done and archived (→ `TODO_ARCHIVE.md`).

**Next up, in priority order:**
1. **[Switched-in creature is the active creature](#switched-in-creature-is-the-active-creature--open-defect)** —
   an **open defect in shipped Stage 3 code** (user ruling 2026-07-15): a switched-in creature must take every
   end-of-battle effect exactly as the starting lead would — evolve, earn/share XP + Stat-Exp, everything. Today
   evolution is gated out for it and XP ignores Gen 1 participation. Do this **before** In-Combat Switching, which
   makes the participant split load-bearing. `/plan` first.
2. **In-Combat Switching** — the voluntary, any-turn SWITCH turn-action (its own documented core feature below),
   now unblocked: Phase 4 **Stage 3 (forced-switch-on-faint) is DONE**, so `Battle` already holds the party and the
   forced + voluntary send-in path exists. `/plan` first; a good `opus-engineer` candidate (central `Battle` /
   `AttackAction` turn-resolution change).
3. **Item Acquisition · Bag Persistence · Catch** — the deferred cluster, unblocked by the acquisition channels.
   *(Item acquisition itself is already done via the Run Economy; bag persistence + catch remain.)*
4. **Game Loop & Progression** — save layer (`save.db`); party + between-biome lead + forced-switch are done.

*(**Phase 4 shipped in full** — the roster, both acquisition channels, between-biome lead swap, and
forced-switch-on-faint — **but not cleanly**: Stage 3 carries the open end-of-battle defect above, which came from
wrong requirement pins in its own plan rather than from the domain.)*

*(The **Shop node** — the last Run Economy follow-up — is now done: `ShopRunEvent` + `ShopCalculator`, a
spend-gold buy modal. See below.)*

Lower priority / opportunistic: E2E flakiness stabilisation (`status.spec.ts` **fixed 2026-07-15** — root cause
was a spec asserting a transient badge, not an engine bug; see *Browser-Based UI Testing* for the seed-≠-determinism
lesson it taught), Web UI polish (move-specific animations), Multi-Generation groundwork, User Documentation.

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
  (`creaturegame/Creature/Party.cs` — `MaxSize` 6, `Lead`/`Add`/`IsFull`/`Replace`/`SetLead`), `RunState.Party`
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
  **⚠️ Shipped with a known defect** — evolution is gated to the no-switch case, so a switched-in finisher that
  levels up does **not** evolve, and XP/Stat-Exp go to the finisher alone rather than being shared per Gen 1. Both
  came from wrong pins in this plan, not from the domain; see
  [**Switched-in creature is the active creature**](#switched-in-creature-is-the-active-creature--open-defect).

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
    > plan. Corrected by the user 2026-07-15 → see
    > [**Switched-in creature is the active creature**](#switched-in-creature-is-the-active-creature--open-defect)
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
party size 6; **every creature sent out shares in the battle's end-of-battle effects per Gen 1** (see the open
defect below — the earlier "only the lead earns XP (no Exp Share)" pin was wrong); major status persists on
benched creatures per the carry model.

**Out of scope this phase:** the in-battle Poké Ball throw + `BallItemEffect` + catch-rate-vs-HP formula (stays
in the Catch cluster below); voluntary in-battle switching (its own planned core feature —
[**In-Combat Switching**](#in-combat-switching--voluntary-in-battle-party-switching-planned-core-feature));
`save.db`/`PlayerDbContext` persistence + cross-run meta-progression; the **Exp. Share / Exp. All item**
(distinct from participant sharing — see the open defect below); Revive (needs a fainted-but-revivable party
member — possible after Stage 3, not built here).

---

## Switched-in creature is the active creature  ⟵ OPEN DEFECT (user ruling 2026-07-15)

**The requirement, in the user's words:** *"A switched-in Pokémon is for all intents and purposes the active
Pokémon, therefore all effects that happen at the end of battle happen to it as well. So it can evolve, it shares
XP, EVs, everything. Just like it would work in Gen 1 / generically in Pokémon."*

**There is no special case for a switched-in creature.** It is not a second-class participant, it does not "wait
until its next clean win", and it is not excluded from any end-of-battle effect. Anything the starting lead would
receive, a creature that took the field receives on the same terms. This governs the forced faint-switch (shipped)
and the voluntary SWITCH action (planned) alike, and it **overrides** the two pins the Stage 3 plan invented.

### Why this shipped wrong (keep this — it is the reason the gate is being tightened)
Neither rule came from Gen 1 or from any design doc. Both were written *by the plan*, then implemented faithfully,
and `requirements-review` returned **MET** because the code matched the plan. The plan even pre-argued the point
(*"i.e. **not** a deviation, and the participant-split Exp remains the documented deferral"*), which suppressed the
domain check instead of inviting it. Two specific traps to recognise again:
- **An implementation convenience written up as design.** The evolution gate exists only because one `levelBefore`
  local belongs to the creature that *started* the battle, so a switched-in finisher "can't be compared against
  it". That is a five-line fix, not a design position.
- **A rule that is right by coincidence.** "Finisher earns the XP" happens to match Gen 1 *today* only because the
  outgoing lead has fainted and a fainted participant earns nothing anyway — so it was never tested against the
  real rule, and it would silently diverge the moment voluntary switching lands with both creatures alive.

→ `requirements-review` now escalates by default and treats plan-asserted domain facts as claims to verify
(`.claude/agents/requirements-review.md`, "Escalate by default" + the recurring-discrepancy log).

### Open points
- [ ] **Evolution must apply to any creature that levelled up this battle**, switched-in or not. Track the
  pre-battle level **per creature that takes the field** (capture on send-in) instead of the single
  `levelBefore` local for the starting lead. Anchor: `RunDirector.cs` `BattleRunEvent` — the
  `ReferenceEquals(active, player) && active.Level > levelBefore` gate, and its comment, both go.
- [ ] **XP / Stat-Exp follow Gen 1 participation, not "the finisher".** Gen 1 divides Exp (and the Stat-Exp
  award) among every party member that was **sent out** during the battle and has **not fainted**; a participant
  that fainted earns nothing. So a forced switch = the survivor takes the full award (matching today's behaviour
  by coincidence), while a voluntary switch with both alive = a genuine split. Anchors: `Battle.cs` — the XP /
  `GainStatExp` award site (single-recipient today, and its comment); needs a per-battle participant set.
  *(The **Exp. Share / Exp. All item** stays deferred — that is an item that pays a *non*-participant, a separate
  feature from participant sharing.)*
- [ ] **Sweep for other end-of-battle effects that assume the starting lead** rather than only fixing the two
  found. The rule is general, so audit the whole post-battle path (carried status ✅ already reads `s.Player`;
  move-learning on level-up; anything else keyed off `player` or `levelBefore`).
- [ ] Once fixed, write the rule into `docs/STATE_MODEL.md` / `docs/GAME_LOOP.md` so it is a documented invariant
  (source #2) rather than a plan claim — future `requirements-review` runs can then cite it.

**Sequencing:** worth doing **before** [In-Combat Switching](#in-combat-switching--voluntary-in-battle-party-switching-planned-core-feature),
which makes the participant split load-bearing (both creatures alive at the end) and multiplies the blast radius.
`/plan` first — the participant set touches `Battle`'s award site, a central method.

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

## Difficulty easing — weak wild encounters + Quick Heal reward  ✅ DONE (2026-07-12)

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

## Run Economy — Gold, Item Rewards, Transient Bag & Treasure/Mystery Nodes  ✅ DONE (2026-07-02)

Phases **A** (core, generation-agnostic) + **B** (web-layer reward policy) + **C** (frontend gold HUD + reward
modal) are done — currency, battle-win + Treasure/Mystery reward rolls, an earned transient bag, playable
end-to-end. Commits `ea41531` (A/B, audited **PASS-WITH-ADVISORIES**) + `7d9afc5` (C). 1267 tests green.
**Full record → [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) → *Run Economy*.**

**Follow-up — the Shop node** ✅ DONE (2026-07-09): a between-encounter shop that **spends** the transient
`Wallet`. `ShopRunEvent` (replacing the old no-op `InteractionStubEvent`) rolls a per-visit, run-scaled stock via
the web-layer `ShopCalculator` (rarity-derived prices — *not* the unaffordable Gen 1 `Item.Cost`), emits a
blocking `ShopOffered`, then runs an iterative buy loop (`ChooseShopActionAsync` → buy/leave) charging the
`Wallet` and filling the `Bag`. Full stack: core event + `IBattleInput`/`SignalRInput` handshake +
`BattleHub.BuyShopItem`/`LeaveShop` + `SignalRBattleEventEmitter` projection + a React shop modal
(`BattleScreen`). Buy-only MVP — selling / restock / persistence remain out of scope (persistence rides the
deferred `save.db` layer). Two refinements from review: the shop is **affordability-gated** (a biome keeps a Shop
node only when the wallet clears `ShopCalculator.MinItemPrice` at biome entry — no dead 0₽ shop, so the opening
node is never a shop), and purchases respect the Gen 1 **99-per-slot** `Bag` ceiling (a buy that would overfill
is refused before charging). Covered by `RunDirectorNodeTests` (buy/leave/no-op/headless/gate/99-cap),
`ShopCalculatorTests` (pricing shape + seed), `BagTests` (99-cap), `WebEventContractTests` (wire projection),
Vitest (`timeline` + `battleReducer`), and a Playwright `shop.spec` (earn gold → buy at a shop).

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
  null ⇒ `ItemUseFailed`. The frontend hides Ball & Revive via `bag.ts isUsableInBattle`. `CatchRate` is
  already imported on `PokemonSpecies` ✓.

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

> **Revive / Max Revive** (the only remaining in-scope item effect) is also blocked here — it needs a
> fainted-but-revivable party member, which the single-creature chain doesn't have. `ItemEffects.For(Revive)`
> stays null until Game Loop adds a party.

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
- [ ] `ConsoleInput : IBattleInput` — numbered move menu for terminal play (low priority).

---

## Encounter Map — Slay-the-Spire-style route overlay  *(design pass 2026-07-10 — `/plan`; sub-decisions ratified by the user 2026-07-10)*

Make the run **visible**: a striking secondary overlay that shows the region as a node map — biomes as
waypoints wired by their `Neighbours`, the route you've charted traced through them, and the **current biome's
node ladder revealed inline** (wild / elite / treasure / shop / mystery … → **Boss** apex). Replaces the flavour
gap where the biome plan is walked *invisibly* today. The backend already holds every fact this draws — this is
overwhelmingly a **presentation** feature over existing run state plus a few **additive** events.

**Decided in the `/plan` pass (three forks):**
1. **Reveal the path, don't branch it.** The overlay *shows* the biome's seeded node sequence and your position
   on it; it grants **no** new in-biome choice. This honours the settled `GAME_LOOP.md §4` governing rule (logic
   owns the sequence; the player only changes an event's outcome). The only real choice stays the **between-biome
   route pick**, which the map surfaces as clickable waypoints. *(Full STS branching was considered and rejected —
   it would overturn §4 and rework `RunDirector`/`chooseNextEvent`.)*
2. **Whole region graph.** One map of the run's playable biome subset (the seeded 10-of-18) wired by neighbours,
   the traversed route highlighted, the **current biome expanded** to its node ladder. (Not just the current
   biome; not the deferred multi-panel nesting.)
3. **Both reveal triggers.** The overlay **auto-peeks** at each node transition (pin advances one step, then it
   fades back to the scene) **and** a persistent **Map** toggle reopens it on demand.

**Acceptance condition (DoR #1):** From the battle screen the player can (a) see, at any time via a Map button, a
region node-map with biomes-as-waypoints, their neighbour edges, the route so far, and the current biome's node
ladder with per-node icons + done/current/upcoming state and the Boss at the apex; (b) watch the pin auto-advance
one node at each transition; and (c) chart the next biome by clicking a highlighted neighbour waypoint on the map
(folding in the old `BiomeChoiceModal`). Same seed ⇒ same map. Adding the reveal changes **no** run sequencing.

**Gen-variable surface (DoR #3): none.** This touches no `IBattleRules` / `ITypeChart` / `IStatCalculator`. Node
kinds (`RunNodeKind`) and biomes are already generation-agnostic *run structure* (biomes are the multi-gen axis —
Johto ships its own set), and biome type colours reuse the chart-agnostic `TypeBadge` palette. Litmus "does this
change for Gen 2?" → no; the map renders whatever playable set/plan it is handed. (Satisfies the
`GENERATION_SEAMS.md §5.0` checklist trivially — no seam is added or read.)

**Gen-1 source of truth (DoR #4): N/A — no Gen 1 mechanic.** Behavioural truth is the existing run model
(`ENCOUNTER_DESIGN.md §1/§5`, `GAME_LOOP.md §3-5`); Slay the Spire is the **UX** reference only.

**Data vs runtime boundary (DoR #5):** web + frontend, plus a few **additive core events**. **No importer change,
no DB migration.** The one optional *data* choice is authored 2-D coords for map layout (below).

**Backend surface — additive, small (the reveal plumbing):**
- **Region graph payload at run start.** The client needs the full playable subset + neighbour edges (today only
  the 3 *offered* biomes reach the client via `BiomeChoiceOffered`; `RunSetup.PlayableBiomes` is server-only).
  Expose it once — either embedded in the game-start/setup response or a new `RegionMapRevealed(biomes[], edges)`
  event. Edges = each playable biome's `Neighbours` filtered to the playable id set.
- **`BiomeNodePlanRevealed(nodeKinds[])`** — emit the seeded `BiomeNodePlan` when it's rolled (in
  `RunDirector.Apply` on `BiomeChoiceOutcome`, right after `GateShopsByBudget`, via `_emitter` — same
  director-emits precedent as `RunEnded`). This is the ladder the overlay draws ahead of time. Revealing it is a
  **sequencing no-op** (the plan is already deterministic once entered).
- **`RunNodeEntered` for *every* node.** Today wild battles emit nothing (`RunDirector` only banners
  Elite/Boss/interaction nodes); emit it for wild too so the map has one uniform pin-advance signal. Keep the
  *text banner* filtered to Elite/Boss in the frontend timeline (don't add wild-battle banner noise).
- **Field-level wire guards.** Every new event/field needs its `SignalRBattleEventEmitter` projection **and** a
  field-level `WebEventContractTests` guard — the recurring *web event field-projection gap* (see memory +
  existing `BiomeChoiceOffered`/`BiomeEntered` projection guards).

**Frontend surface (the bulk of the work):**
- **`EncounterMap` overlay component.** Region = a node-link **overworld** map (waypoints + neighbour edges,
  visited route traced, current biome highlighted, offered neighbours flagged choosable). Current biome = a
  vertical **ladder** to the Boss apex, one icon per revealed plan node (wild ⚔ / elite ★ / boss 💀 / shop $ /
  treasure ▧ / mystery ?), each in done / current / upcoming state, plus a "you are here" pin. Type-
  coloured waypoints reuse the `TypeBadge` palette; theme-aware (light/dark).
  - **Note — the Poké Center cap is not a plan node.** The mandatory recovery after each biome's Boss is a
    separate `RunDirector` branch (`EventsInCurrentBiome >= BiomeNodePlan.Count`), **not** a `RunNodeKind` and so
    **not** in `BiomeNodePlan` / `BiomeNodePlanRevealed`. Phase 2's ladder must **synthesize** a terminal rest ♥
    step after the Boss client-side (it's implied by the model, not carried by the reveal event) — or Phase 1.5
    emits it explicitly if that proves cleaner.
- **Reducer/hub wiring** (`battleReducer` + `useBattleHub`): accumulate region graph (start) → route trace
  (`BiomeEntered`) → node ladder (`BiomeNodePlanRevealed`) → pin index (`RunNodeEntered`) → choosable set
  (`BiomeChoiceOffered`). Pure-logic accumulation → Vitest (per the suite-split rule).
- **Overlay behaviour:** auto-peek + fade at each transition; Map toggle to reopen; the between-biome route pick
  happens **on the map** (click a choosable waypoint → existing `chooseBiome`), replacing `BiomeChoiceModal`.

**What tests assert (DoR #6 — the invariants, since there's no gen-quirk):**
- **Reveal is a no-op to sequencing** — a `RunDirector` test proving the emitted battle/event *order* is
  unchanged with the new reveal event present (the map must not alter the run).
- `BiomeNodePlanRevealed` carries the exact seeded plan **and** projects over SignalR (field-level guard).
- `RunNodeEntered` now fires once per node incl. wild (count == plan length per biome).
- Region payload carries the full playable set + correct filtered edges.
- Reducer accumulates map state correctly across the event stream (Vitest).
- E2E (seeded, Playwright): open the Map overlay, see the ladder + pin, advance a node, and chart the next biome
  by clicking a waypoint.

**Dependencies (DoR #7): none blocking.** Everything the map visualizes already exists (biome graph, seeded node
plan, `BiomeEntered`/`RunNodeEntered`); the work is exposing it + drawing it. Independent of Phase 4 acquisition.

**Sub-decisions — ratified by the user (2026-07-10):**
- **Map-based route choice replaces `BiomeChoiceModal`** *(chosen)* — the choice happens by clicking a
  waypoint, so "user choices" become the visible verb. (Alt considered: keep the card modal *and* add a passive
  map — rejected as redundant surface.)
- **Layout coords: authored per-biome 2-D coords in the `Biomes` registry** *(chosen — cheap, geographic,
  seed-stable)* over a client-side computed/force-directed layout (no data, but wobblier + less "designed").
- **Fog of war** *(chosen)*: region topology fully visible from start, biome interiors revealed on entry
  (tunable later).

**Phased build (shippable slices):**
1. ✅ **DONE (2026-07-10) — Reveal plumbing (backend):** `RegionMapRevealed` (playable subset + neighbour edges,
   filtered to the subset) emitted once at run start; `BiomeNodePlanRevealed` (the seeded ladder) emitted on
   biome entry; `RunNodeEntered` now fires for *every* node incl. `WildBattle` in biome mode (the frontend
   filters `WildBattle` out of the log). SignalR projections + field-level `WebEventContractTests` guards for
   both new events; explicit no-op timeline arms (every-event contract). Reveal proven a **sequencing no-op**
   (`BiomeMode_RevealsNodePlan_…WithoutChangingSequence`). No visible change; full suite + E2E green. *(Sub-
   decisions confirmed: map replaces `BiomeChoiceModal`; authored biome coords; region topology visible from
   start.)*
2. ✅ **DONE (2026-07-10) — Current-biome ladder overlay (frontend):** the reveal events are consumed into
   reducer state (`mapBiomeName` / `mapNodePlan` / `mapPin`) via the timeline (`MAP_BIOME_ENTERED` /
   `MAP_PLAN_REVEALED` / `MAP_NODE_ENTERED`); `RunNodeEntered` now advances the pin per node (incl. the
   banner-less `WildBattle`), and the Poké Center cap advances it onto a client-synthesized terminal `Rest`. New
   `EncounterLadder` overlay in `BattleScreen` draws the vertical ladder (icons per kind, done/current/upcoming,
   Boss apex + Rest cap, "you are here" pin), auto-peeks at each ladder change and toggles via a `MAP` button.
   Covered by Vitest (reducer + timeline, incl. the `RecoveryOffered`→Rest-cap pin dispatch) + a seeded
   Playwright `encounter-map.spec` (opening ladder structure **and** a win advancing the pin so the cleared node
   reads `done`); also live-verified end-to-end. The STS "path" feel within a biome is now visible.
3. ✅ **DONE (2026-07-10) — Region graph + map-based route choice:** authored 2-D coords on the 18 Kanto biomes
   (`BiomeDefinition.MapX/MapY`, guarded by `BiomeTests`) projected onto `RegionMapRevealed` (wire + field guard);
   the frontend consumes the graph (`REGION_MAP_REVEALED`) + traces the route by id (`MAP_BIOME_ENTERED` →
   `visitedBiomeIds`/`currentBiomeId`). New `RegionMap` component draws the overworld node-link graph (type-
   coloured waypoints at their coords, neighbour edges, travelled-route + current-biome highlight); the MAP
   overlay now shows the region graph **above** the current biome's node ladder (`RunMapPanel`). The route pick
   is **on the map** — `RouteChoiceMap` (a prominent blocking region map with glowing clickable offered
   waypoints) **replaces `BiomeChoiceModal`** (retired). E2E helpers repointed to `.region-node--offered`.
   Covered by Vitest (reducer + timeline) + Playwright (region-choice + trace); full suite + live-verified.
4. ✅ **DONE (2026-07-10) — Polish:** **a11y** — the route choice focuses the first offered waypoint on open
   (E2E-asserted), `aria-current` on the current biome, `aria-modal`, a keyboard focus-visible ring on choosable
   waypoints; **easing** — CSS transitions on the pin/edge/waypoint state changes + a route-choice rise, all
   reduced-motion-guarded (decorative, no E2E waits on them); **glyphs** — ♥ heal-pink Rest cap, ₽ Shop; **coords**
   — pulled the two edge-most waypoints inward so labels don't clip. *(Finer edge-crossing tuning deferred as
   low-ROI — each run shows a random 10-of-18 subset at fixed positions, so crossings vary by subset.)*

5. ✅ **DONE (2026-07-11) — Visual overhaul (full-screen dark-fantasy overworld):** the corner map was too small
   and flat. The MAP button now opens a **full-screen** night-overworld (`.encounter-map--pinned`): biomes render
   as **type-coloured bioluminescent territories** (`.region-territory`, screen-blended, watermarked with the
   primary-type icon = procedural "background imagery"), **paths are gradient-stroked to blend the two biomes'
   type colours** (per-edge `<linearGradient>`, gently bowed `<path>`), and the active biome's encounter ladder
   runs up a right-hand panel with **real SVG node-kind icons** (sword/star/skull/coin/gem/?/heart) replacing the
   old text glyphs. Biome waypoints gained an emblem disc + name + **type chips with an icon-in-frame** (new
   shared `mapGlyphs.tsx`: 15 per-type icons + 7 node icons + `TypeChip` + `MapGlyphSprite`; `RouteChoiceMap`
   legend uses it too). The auto-peek stays a small corner ladder; Escape/backdrop/× close the full-screen view.
   All required test selectors preserved (`.encounter-map`, `.region-node/-edge`, `.ladder-node--*`, etc.);
   full suite green + live Puppeteer-verified. Committed dark single-theme by design (a night chart).

**Feature complete** — the Encounter Map ships all four phases + the visual overhaul. Any future work is net-new
(e.g. the map as the persistent run screen, or wiring Phase 4 acquisition's boss-catch/themed-draft offers).

**Follow-up (deferred, user-approved 2026-07-11 — "procedural now, real art later"):** replace the procedural
type-colour+motif territories with **real painted per-biome scenery** (forest/cave/shore illustrations). Needs an
image-asset pipeline (source/generate + store under `ClientApp/public`); the current `.region-territory` layer is
the drop-in seam. Low priority — the procedural look reads clearly on its own.

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

**Still open** (filed 2026-07-16 from a repo-wide structural review — ranked by cost-of-deferring, not size):

- [ ] **No ESLint/Prettier config in `ClientApp/` at all.** The C# side has CSharpier pinned + hook-enforced;
  the frontend has no linter and no formatter, so style/quality drift is unpoliced. The *typecheck* half of
  this gap is closed (`tsc` in the hook — archived above); this is the remaining lint/format half. Lower value
  than the debt above; worth a call on whether to adopt ESLint flat config + Prettier, or to keep the frontend
  deliberately un-linted.
- [ ] **`RunDirector.cs` is 1058 lines holding 9 types** — the director, 6 `IRunEvent` classes
  (`BattleRunEvent`, `RecoveryRunEvent`, `LeadChoiceEvent`, `BiomeChoiceEvent`, `ShopRunEvent`,
  `RewardRunEvent`) and 2 static resolution helpers (`RewardResolution`, `AcquisitionResolution`). Split the
  events out per-file under `Combat/RunEvents/`. It also carries a small live duplication: `PlayerAttackTypes`
  and `CreatureTypes` both walk `Type1`/`Type2` in different shapes — collapse to one helper.
  *Note:* `RunLoop.cs` also has ~28 types but is **fine** — a cohesive vocabulary file of small records. Don't
  let a type-count metric drive a split there.
- [ ] **`BattleScreen.tsx` — 1317 lines, ~25 components.** 8 modals (`Recovery`, `EvolutionPrompt`,
  `RewardChoice`, `Shop`, `Acquisition`, `LeadChoice`, `SwitchIn`, `MoveReplacement`) + 11 hand-rolled
  `<div className="modal-overlay">` blocks. Escape-to-close is ad hoc: the map overlay has it (:468), the
  blocking modals don't — plausibly deliberate for blocking prompts, but currently an accident of each
  component rather than a stated rule. *Fix:* a shared `<Modal>` wrapper that makes the escapable/blocking
  choice explicit; lift the modals into `components/modals/`.
- [ ] **`Creature/` and `Creatures/` are two directories that both declare `namespace creaturegame.Creatures`.**
  (`Creature/` holds Creature, Attributes, BattleState, Party, StatStages, stat calc; `Creatures/` holds Biome,
  EncounterSelector, LearnsetMove(Selector).) The split carries no meaning, and it quietly violates the
  folder=namespace convention the test project follows perfectly (verified: zero mismatches under `tests/`).
  *Fix:* merge into `Creatures/` — a pure file move, no namespace/using churn since both already share it.
- [ ] *(low)* **No `Directory.Build.props`** — `TargetFramework`/`ImplicitUsings`/`Nullable` are copy-pasted
  across all four csprojs, and there are no analyzers or `TreatWarningsAsErrors`. Build is clean (0 warnings)
  today, so this is cheap insurance to keep it that way, not a fix for a live problem.
- [ ] *(low, watch — do not refactor speculatively)* **`AttackAction` still has three large methods**:
  `ResolveDamage` (145 lines), `ExecuteAsync` (140), `ResolvePreDamageGates` (112), despite the earlier split
  archived as (B). It is central and well-tested; revisit only if a change makes it hurt.

### Known Gaps
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector exists.
- Enemy Pokémon do not evolve — wire into level-up when Game Loop is built.
- **Endless-chain double-faint** — tested (2026-06-12): a mutual end-of-turn DoT double-faint counts as a loss,
  pinned by `BattleRunnerTests.Runner_DoubleFaintFromEndOfTurnPoison_CountsAsLoss_NotAWin`.

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
