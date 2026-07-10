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
1. **Encounter Logic — Phase 4: Acquisition channels** (boss catch + themed draft, fought-only). The last open
   piece of Encounter Logic and the bridge into the deferred Catch cluster. `/plan` first.
2. **Item Acquisition · Bag Persistence · Catch** — the deferred cluster, unblocked by (1). *(Item acquisition
   itself is already done via the Run Economy; bag persistence + catch remain.)*
3. **Game Loop & Progression** — party, switching, save layer (`save.db`).

*(The **Shop node** — the last Run Economy follow-up — is now done: `ShopRunEvent` + `ShopCalculator`, a
spend-gold buy modal. See below.)*

Lower priority / opportunistic: E2E flakiness stabilisation, Web UI polish (move-specific animations),
Multi-Generation groundwork, User Documentation.

---

## Encounter Logic — Phase 4 (the only open piece)

Phases 1–3 (biome model + type-filtered pool, `IEnemyArchetype` tiers + depth bands, `RunDirector` event model
+ live biome-graph map + tuned Boss-capped node curve) are **done and archived**, along with the four follow-on
refinements — per-run biome-map randomisation, randomised 4–6 route length, Roar/Whirlwind→`ForceFlee`, and the
opening-route favourable-matchup guarantee. Full per-phase record (design, pins, seam reviews) in
[`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) → *Encounter Logic*.

- [ ] **Phase 4 — Acquisition channels** (boss catch + themed draft, fought-only) — the remaining
  `ENCOUNTER_DESIGN.md §4` piece, and the bridge into the *Item Acquisition · Bag Persistence · Catch* cluster
  below. Now unblocked (the §1–§3 layer is done): a biome's **Boss** node is the catch hook, the **fought-only
  themed pool** is the draft source. `/plan` first; *n%* rates tuned here.

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

- [ ] Catch → Pokémon added to party (up to 6); choose lead between battles.
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
4. **Polish (optional):** transition animation/easing, icon art, accessibility pass, coord/edge-crossing tuning.
   *(Authored coords landed in Phase 3.)*

---

## Browser-Based UI Testing (Playwright)

Suite lives in `ClientApp/e2e/` (`npm run test:e2e`). Playwright drives the React DOM; the Phaser canvas is
tested through the `mitt` bridge (assert **event ordering**, never wall-clock durations — the #1 flake source).

**Done (2026-07-05):**
- [x] **Seed plumbing** — `StarterSelection` forwards an optional `?seed=<int>` URL param into the `/start`
  request (backend already accepted `Seed`), so an E2E can pin a fully deterministic run. `?e2e=1` still sets
  test mode. react-router drops the query on nav from the title, so seeded specs land directly on `/select?seed=`.
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

**Done & archived** (2026-06-20 → 22 code-review + Architecture Review #7 pass — full write-ups in
[`TODO_ARCHIVE.md`](TODO_ARCHIVE.md)): (A) `MoveSet` cross-thread mutation → lock-free copy-on-write;
(B) `AttackAction.ExecuteAsync` split into `ResolveDamage` + `ResolvePreDamageGates`; (C) repo-wide
comment-density pass; (D) minor comment/dead-field batch; the **RNG seam** (CLOSED — do not re-file the
`AlwaysHit`/`AlwaysCrit` shim idea, the unseeded-web-composition-root, or "Roll\* ignores the battle seed");
and Architecture Review #7 (`SecondaryHits` seam dedup, `MoveImport.MapToAttack` split + `MoveMappingTests`).

**Still open:**

*(none — the `bag.ts` effect-registry drift seam below is now closed.)*

**Done & archived:**
- [x] **`bag.ts` re-encodes the engine's effect registry** — CLOSED (2026-07-04). The frontend
  `USABLE_CATEGORIES` set (which hardcoded which `ItemCategory`s are usable in battle) is gone; the backend now
  projects a server-computed `UsableInBattle` boolean onto `BagItemView` (from `ItemEffects.For(category)`), and
  the client filters the bag menu on that flag. Single source of truth — when Ball/Revive get effects, only the
  registry changes and the menu follows. Mirrors the `RestoresPpAllMoves` field-projection precedent.

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
