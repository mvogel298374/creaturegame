# Battle Sim – TODO List

> **Active tasks only.** Completed work lives in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md) — read it only for the
> history of a finished item. **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md` (profiles) ·
> `DESIGN_GUIDES.md` (mechanics) · `DEV_STANDARDS.md` (conventions).

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
code defect. (Web UI polish, Multi-Generation groundwork, User Documentation, and test-infra items are Tiers
4–5 above, not repeated here.)

**Settings Menu** — sound volume + difficulty→XP bonus, both shipped and archived (→ `TODO_ARCHIVE.md` →
*Settings Menu — sound volume + difficulty (XP bonus) controls*); the difficulty dial's self-referential-scaling
limitation is a known, user-waived follow-up, not open work.

---

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

### 2 — Bag persistence · once acquisition defines what a bag holds
- [ ] Persist the `Bag` to `save.db` / `PlayerDbContext` (rides on the broader save layer — see **Game Loop**).
- [ ] Decide bag scope: **per-run** (lost on death) vs. **meta-progression** (carries across runs). The
  acquisition design drives this.

### 3 — Catch / Poké Ball effect (one acquisition channel) · Gen 1 reference
- [ ] `BallItemEffect : IItemEffect` for `ItemCategory.Ball`, registered in `ItemEffects.All`; extend `Battle`
  with a "catching" state/outcome.
- [ ] Gen 1 formula: `floor((MaxHP × 3 − HP × 2) × CatchRate / (MaxHP × 3))` vs a 0–255 roll (per-ball modifier
  lives in the formula, not the `Item` row).
- [ ] `CaptureAttempted(string TargetName, int TargetId, bool Caught)` event (carries the creature id like every
  other creature event — `WebEventContractTests` requires it); `BattleEnded` variant `reason: "Caught"`.
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
  **Encounter Logic Phase 4** (Stages 1a–3 ✅, complete 2026-07-15; full record archived in `TODO_ARCHIVE.md`).
  What remains here is only the **in-battle ball throw** as a third acquisition channel — see the Catch cluster
  above; it deposits into the existing `Party`.
- [x] **Voluntary in-battle switching** — a SWITCH turn action to swap the active creature mid-fight. ✅ DONE
  (2026-07-25) as **In-Combat Switching** (all three stages incl. the out-of-PP menu affordance); full record
  archived in `TODO_ARCHIVE.md`.
- [ ] Progressive difficulty beyond the current `targetBst = lead BST + depth × 10`; trainer encounters at
  milestones.
- [ ] `PlayerSave` / `SavedCreature` models in `save.db`; auto-save after each battle; party-management UI.
- [x] **Refresh/reconnect-safe session handling** — the lightweight `gameId`-persistence option. ✅ DONE
  (2026-09-14) as **Session Resume**; full record archived in `TODO_ARCHIVE.md`. The heavier `PlayerSave`/
  `save.db` option (survives a server restart/redeploy, not just a client refresh) stays deferred to Tier 3
  (this section).
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
  - **Plumbing (the real work, mind the seam):** `MoveUsed` carries only `(AttackerName, AttackerId, MoveName)` — the client
    can't see the *enemy's* move type/category. Project `DamageType` + `AttackType` onto `MoveUsed` + its
    `SignalRBattleEventEmitter` mapping with the field-level guard (the recurring **web event field-projection
    gap** — see the memory + `WebEventContractTests`). Then a pure `moveAnimationFamily(type, category, slug)`
    map (unit-testable like `timeline.ts`), new per-family `BridgeCommand`s + `BattleScene` handlers, each still
    emitting `animationComplete` so the timeline's `awaitAnim` contract holds.
  - **Raised 2026-08-03, worth doing in the same pass:** once `MoveUsed` carries `DamageType`, the hit-flash
    tint and the battle log's "It's super effective!" emphasis colour (`docs/SPRITE_PRESENTATION.md` §3.3–3.5
    covers the canvas-side FX ideas this pairs with) can read off **one shared type-colour source**
    (`TypeBadge`'s palette) instead of two independently-chosen colours drifting apart over time.
- [ ] **Text feel — typewriter log scroll + menu navigation blips.** Raised 2026-08-03 as a design-lead
  suggestion; zero engine dependency, cheapest/most-authentic-per-effort item in this section. Two pieces:
  (1) the battle log renders character-by-character (~15–20ms/char) instead of appearing all at once — the
  classic Gen 1 text-scroll feel; (2) a cursor-move blip + a confirm blip on menu navigation (command grid,
  move select, any list). Pure client polish, no wire change. Good candidate to pair with `docs/GENERATION_PROFILE.md`
  §7.3's now-ratified "Kanto Sage" pass, since both land as one felt "this is Gen 1 now" moment rather than
  two separate small changes.
- [ ] **Sprite presentation FX — joint mini-plan not yet scheduled.** `docs/SPRITE_PRESENTATION.md` §3 sketches
  8 unratified ideas on top of the existing genuine Gen 1 sprites (texture-filter audit, a grounding shadow,
  crit shine, type-coloured impact particles, status tint, a screen-wipe transition, depth-of-field, a cleaner
  evolution-flicker), ordered cheapest/safest first. Design lead's recommendation (2026-08-04): §3.1 (confirm
  `pixelArt: true` in the `Phaser.Game` config) isn't really a mini-plan candidate — it's a one-line check, do
  it standalone whenever picked up. §3.4 (type-coloured particles) should fold into the *Move-specific attack
  animations* item above rather than be scoped separately — both are blocked on the same `DamageType`-on-
  `MoveUsed` wire change. §3.2 (grounding shadow, tied to the Kanto Sage ink tone) is the strongest next
  candidate for an actual joint sketch → ratify session — cheap, zero wire dependency, ties the canvas visibly
  to the new HUD. Not started; next step is scheduling that session, not building anything yet.
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

## Dev Mode — open follow-ups

The server-gated Dev Mode switch, the enemy-creature overview and the damage-roll ranges are **done and archived** (→ `TODO_ARCHIVE.md`
→ *Dev Mode*). Open:
- [ ] *(small)* **Stat stages in the enemy overview.** `PlayerOverviewDto` carries no stat stages, so the dev
  enemy sheet omits them. Add the stages to the DTO (or a dev-only extension) and render them in
  `CreatureOverview` `enemy` mode.
- [ ] *(unscoped ideas)* More dev actions behind the same `DevController` gate (404 when the server flag is
  off): skip to node, spawn a chosen enemy, grant items/gold, set level. None are planned.

---

## Browser-Based UI Testing (Playwright)

Suite lives in `ClientApp/e2e/` (`npm run test:e2e`). Playwright drives the React DOM; the Phaser canvas is
tested through the `mitt` bridge (assert **event ordering**, never wall-clock durations — the #1 flake source).

> **A seed is not by itself determinism** (learned the hard way 2026-07-15, and again 2026-07-26). The seed
> fixes the *server's* RNG stream, but the **client's move sequence is what draws from it**: a spec that races
> (polling loops, `waitFor` timeouts, a swallowed click) submits moves on different turns under load, which
> shifts every later roll and plays out a different run. A seeded spec is only deterministic if the driving loop
> is **paced** — settle each turn (wait for the action menu to come back) before the next input. Otherwise write
> the assertions not to care (retry / walk seeds). See `status.spec.ts`, `forced-switch.spec.ts`, and
> `e2e/README.md`. This note is standing guidance, not a task — it outlives the items that taught it.

**Done and archived** (→ `TODO_ARCHIVE.md`): seed plumbing, the Run Economy reward-modal E2E, the spec-rot
recovery and the inter-test flakiness pass are all under *"Browser-Based UI Testing — seed plumbing, spec-rot
recovery & the flakiness pass"*; the between-encounter modal E2Es under *"Other between-encounter modal E2Es"*;
the In-Combat Switching UI contract in the *"In-Combat Switching"* 2026-07-26 addendum; the evolution
nameplate/action-prompt lag under *"Evolution nameplate doesn't follow until the next battle starts"* (2026-07-28);
and its sibling *"Party strip shows a stale name after an on-field evolution"* (2026-07-29) — see the one
known-still-open follow-up (the regression-insurance E2E coverage gap) below.

> **E2E is deliberately out of the AI agent's pre-finish gate (2026-07-26, user's call).** The suite is ~4 min
> for 37 browser-driven tests and is the only one with real flakes, so agents are *heavily disincentivized*:
> `test-runner` runs `.\test.ps1 -Dotnet -Web` and reports that E2E did not run, may only **recommend** the
> narrowest covering command, and never runs it on its own initiative — **only the user asks for an E2E run**.
> Iteration tool is **`.\e2e.ps1`** at the repo root (`-Spec`/`-Grep`/`-Bail`/`-LastFailed`, per-file timings,
> failure artefact paths). See `.claude/agents/test-runner.md` and `CLAUDE.md`.

**Remaining (in priority order):**
- [ ] **CI step** that boots backend + frontend, runs headless, tears down. **Now the only automatic E2E
  coverage there is** — and therefore more load-bearing than when it was written, not less: the local gate
  never runs E2E (see the note above) and `test.ps1` skips it when the stack is down, so nothing catches a red
  suite until someone asks for a run. CI is what makes "agents don't run E2E" safe rather than merely cheap.
- [ ] `data-testid` attributes — **deferred**: specs lean on stable semantic classes (`.btn-new-game`,
  `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`). Add testids only where a class
  proves brittle.
- [ ] §8 visual-regression canvas snapshots — skipped (maintenance cost).
- [ ] *(low, regression-insurance only)* **`evolution.spec.ts` doesn't pin the nameplate-follows-evolution fix**
  (see `TODO_ARCHIVE.md` → *Evolution nameplate doesn't follow until the next battle starts*, 2026-07-28). The
  ALLOW case reads the nameplate only after `expectRunFlowsOn` (i.e. after the *next* `BattleStarted`), which
  would pass whether or not the rename happens on `CreatureEvolved` itself — so it wouldn't catch a regression of
  the fix. Add an assertion reading the nameplate immediately after the morph, before the next encounter starts.
  Not done here: E2E is user-only per the repo's agent rules, and an added assertion wasn't verified by a
  Playwright run.

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
- [ ] **`GameSessionManager` connection lifecycle** — abandon grace, pending-session eviction TTL, and the
  run-loop `Task.Run` are still covered by *neither* suite (entangled with `IHubContext` + `Task.Run` +
  wall-clock timers; would need an injectable clock to unit-test the timing without real delays). **Narrower
  than it used to be:** the unknown/expired-`gameId` reconnect-rebind slice now *is* covered
  (`SessionResumeTests.cs`, shipped with **Session Resume**, `TODO_ARCHIVE.md`) — and that pass found two real
  bugs in it (the `AttachConnection` silent hang, a `HubException`-fires-too-late-to-reject-`conn.start()` bug),
  so "the reconnect behaviour is a settled/validated edge, not a suspected bug" no longer holds for that slice.
  What remains open here is specifically the wall-clock-timer slice (abandon grace, eviction TTL).

---

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
  - [ ] **4d+ — the surface catalog, jointly iterated** (each its own greenlit mini-plan): battle command menu
    (settled — the 2×2 grid, verbs fixed), move select, battle HUD, CHECK POKEMON (the main overview; its party
    picker ✅ skinned 2026-10-01), BAG, party surfaces (the `PartyStrip` chips `.party-chip*`; the SWITCH menu cards
    ✅ skinned 2026-10-01), Title/StarterSelection (incl. the generation picker). *(The encounter overlay — peek, pinned Run Map, node ladder,
    type chips — ✅ DONE 2026-10-01. All run-prompt
    modals ✅ skinned 2026-10-01: evolution, Poké Center, move-replacement, acquisition offer, reward pick, shop,
    GAME OVER, and the roster-picker family — lead choice, forced switch-in, TM-teach target.)*
    *(The level-up stat panel shipped 2026-09-22, and the nickname / evolution / Poké Center / move-replacement /
    game-over / acquisition surfaces followed — see the open follow-ups below; full record in `TODO_ARCHIVE.md` → *Generation
    Profile 4d+ · Level-up stat panel — Kanto Sage skin*.)*

    **Modals: none remain open (2026-10-01).** Every modal family is skinned and archived (Tiers 1-4, incl. the
    roster picker + SWITCH menu + CHECK POKEMON party cards). Modal CSS lives one-file-per-modal in
    `components/modals/` (see `TODO_ARCHIVE.md` → *Frontend tech debt — segment `BattleScreen.css`*); the shared
    frame rule is `ModalFrame.css`.
    - **Open wart (unfiled):** `Modal.tsx` still imports `pages/BattleScreen.css` for the shared button chrome
      (`.action-btn`, `.move-btn`); inverting that fully means extracting that chrome into its own sheet.
  - [ ] **4d+ · Level-up stat panel — open follow-ups** (the skin itself ✅ shipped 2026-09-22, archived):
    - **Flagged, not decided — a domain claim for `requirements-review`, not asserted here:** whether Gen 1's real
      level-up box lists HP at all (recollection: four rows, ATTACK/DEFENSE/SPEED/SPECIAL) and whether it shows
      gains first, then totals on a keypress. Today's panel shows five rows with gain + total together; left as-is
      because changing it is a behaviour change, not a skin — would be its own item if wanted.
    - **Follow-up, mostly done (2026-10-01):** `.levelup-panel`, `.nickname-modal`, `.recovery-modal` (evolution
      + Poké Center), `.move-replace-modal`, `.battle-end-modal` (game over), `.acquire-modal`, `.reward-modal`,
      `.shop-modal` and `.lead-modal` share ONE double-frame rule (a selector list under `[data-generation="gen1"]`, now in
      `components/modals/ModalFrame.css`). Still open: `.battle-screen` and `.battle-log` carry their own copies of
      the recipe.
    - **E2E not run:** `level-up.spec.ts` (incl. the column-spacing geometry test) is opt-in / user-run —
      recommend `.\e2e.ps1 -Spec level-up`. The panel's geometry was verified live via Puppeteer instead.
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
  Phaser/canvas + per-gen sprite & cry assets stay **deferred** (`GENERATION_PROFILE.md` §7.6).
- [ ] **Stage 5 — falsification harness.** Standing requirement, not a final stage: each stage ships its leg of
  `TestAltProfile`.

**Relationship to *Multi-Generation* below:** that section is the **content/schema** half (Special split, per-gen
species/move tables, `GenerationIntroduced` filtering), still deferred to a Gen 2 sprint. This is the
**axis/framework** half, doable now against Gen 1 alone. Neither subsumes the other.

**Sequencing against the current backlog: resolved (2026-08-04) — Generation Profile is next**, ahead of both
*Item Acquisition · Bag Persistence · Catch* and *Game Loop & Progression* (see the priority list at the top of
this file).

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
- [ ] **Generation filtering:** `Attack.GenerationIntroduced` + `PokemonSpecies.GenerationIntroduced` columns
  (set on import). **The runtime socket already exists, this item is not** — `GenerationProfile.ContentScope`
  (`IContentScope`, shipped Stage 2b, see *Generation Profile* above and `GENERATION_PROFILE.md` §5(b)) already
  routes every catalog read in `EncounterFactory` (species, moves, items) through the profile, and
  `Gen1ContentScope` is a **documented no-op stub** whose doc comment names the exact fix. So this item is just:
  add the columns, then replace `Gen1ContentScope`'s identity pass-through with
  `all.Where(x => x.GenerationIntroduced <= (int)profile.Generation)` (`<=`, not `==`) — no new call sites, no
  new plumbing, no `GetSpeciesForGenerationAsync`/`GetMovesForGenerationAsync` methods to invent.
- [ ] **Per-generation ITEM data — the third catalog, and the one with no schema at all.** The two bullets above
  cover species and moves; items have neither a `GenerationIntroduced` column nor a per-gen table, and unlike the
  other two PokeAPI gives **no** generation signal for items at all (`DATA_IMPORT.md` §4.5 — the Gen 1 roster is a
  hand-curated allowlist, `ItemMapper.Gen1BattleItemNames`). So a second generation's items need: an
  `Item.GenerationIntroduced` column set on import, a **per-generation allowlist** (the curated roster becomes one
  roster per generation, since the API still cannot answer the membership question), and the
  `Gen1ContentScope.Items` identity replaced by the same `<=` filter. Gameplay numbers that differ by generation
  follow the moves' layer-2 pattern — a per-generation override table, not a second code path. **Note the layer-2
  half is not optional:** PokeAPI supplies no revive percent / heal amount / cured status, so an item re-added to
  an allowlist *without* its `ApplyGen1Gameplay`-equivalent override imports with those fields at zero — a
  silently broken item, not a missing one. Max Revive's deleted `RevivePercent = 100` case is the concrete
  example.
  **Why this is its own item (raised by the user, 2026-07-30):** Max Revive used to be imported into `items.db` as
  forward scaffolding for exactly this milestone, and was kept from players by a name-matched hold-out in
  `RewardCalculator`. Stage 2b made content membership a real seam whose Gen 1 implementation is an *identity*, so
  that stray Gen-2 row made the seam rest on a second, unrelated mechanism. It was **removed from the roster and
  the database** rather than special-cased further, on the rule that *the scaffolding a future generation needs is
  the schema, not a stray row* — a row that cannot say which generation it belongs to is indistinguishable from
  Gen 1 content. **This item is where Max Revive comes back**, as data: `ReviveItemEffect` already reads
  `RevivePercent` generically, so no engine change is involved. Pinned meanwhile by
  `ItemImportTests.Gen1BattleItemNames_ExcludesMaxRevive_TheItemsCatalogIsOneGenerationsContent`.

---

## User Documentation

Battles are fully playable now — docs won't describe a moving target.

- [ ] `/help` route or modal — starter selection, battle controls, status icons, level picker.
- [ ] Expand `README.md` — architecture decisions (two-DB model, `IBattleRules` pattern, how to add a move
  effect / a generation).
- [ ] `GEN_DIFFERENCES.md` (written) — adapt into a player-facing "what makes Gen 1 different" explainer.

---

## Repo-Wide Code Review Sweep (2026-10-02) ⟵ OPEN, adjudicated one item at a time (fixed items → archive)

Six read-only Opus reviewers, one per slice (core engine · ASP.NET/SignalR backend · importer + data layer ·
React/Phaser frontend · tests · infra/security). **Every item still listed below is an unadjudicated finding** —
the user decides fix / waive / defer per item; nothing here is approved work until they say so (fixed items move
to `TODO_ARCHIVE.md`). Items already waived/closed elsewhere
(SignalRInput cancel race, reconnect event-replay *absence*, rules-RNG seeding, items.db pin) were excluded.
**Gen 1 claims were quoted from the reviewers' memory of pokered, not fetched — verify each against
`GEN_DIFFERENCES.md` / pokered before changing the engine or data** (see the *challenge gen fidelity* rule).
Fixing a data/engine item usually also means updating the test that currently pins the wrong value.

### R1 — players get stuck or lose control (fix first)

- **Lost answers soft-lock the run — only part (c) remains.** Parts (a) shop BUY+LEAVE dropped and (b) modal
  answer lost during the reconnect window are fixed (`TODO_ARCHIVE.md` → *Repo-sweep R1 — lost modal answers…*).
  (c) the replay doesn't re-open an open prompt after a refresh — tracked as the existing Known Gap, see *Known
  Gaps* → "Session Resume doesn't cover a reconnect during a between-node blocking prompt".
- **Reconnect replay mishandled at both ends.** Server: a flee (`CreatureFled`, no `BattleEnded`) leaves the
  cached `BattleStarted`/`TurnStarted` alive (`SignalRBattleEventEmitter.cs:55-63`), so a later reconnect revives
  a finished battle with a dead move menu; replay fires on *every* reconnect (`GameSessionManager.cs:82-94`).
  Client: a replayed `BattleStarted` bumps `encounterIndexRef` (spurious "A new challenger approaches!", enemy
  re-slides, HP flashes 1/1) and a replayed `MAP_BIOME_ENTERED` duplicates `routePath` and leaves `mapPin` wrong
  (`useBattleHub.ts:70-72`, `battleReducer.ts:440-454`, `timeline.ts:407-421`). Also: the 40 s grace is shorter
  than the client's 0/2/10/30 s retry schedule (last attempt ≈42 s), so outages >~12 s can't be recovered.
- **A faulted run task never tells the client** (`GameSessionManager.cs:281-290`) — catch-all logs + removes the
  session but sends no `RunEnded`/error and doesn't close the socket; the UI sits on its last screen forever.
- **Pinned map hides blocking prompts** — `.encounter-map--pinned` z-index 60 vs `.modal-overlay` 10
  (`BattleScreen.css:1172`, `Modal.css:4`); `RouteChoiceMap` also pulls focus to a covered town (Enter picks an
  unseen route).
- **Engine: flinch from a slower attacker carries into the next turn** (`MoveEffects.cs:96-100`,
  `StatusResolver.cs:28-33`, `Battle.cs:211-214`) — clear `IsFlinched` at end of turn; Substitute should also
  block flinch. No test covers a slower flincher.
- **Data: species base stats are modern, not Gen 1** (`PokemonImport.cs:144-152`) — `BaseSpecial` taken from
  Sp. Atk (Chansey 35 vs 105, Tentacruel 80/120, Gyarados 60/100, Articuno 95/125, Golduck 95/80, …) and the
  Gen 6/7 buffs never undone (Pikachu Def 40/30, Beedrill Atk 90/80, Dugtrio Atk 100/80, Farfetch'd Atk 90/65,
  …). Fix: a curated 151-row Gen 1 base-stat table (pokered `base_stats`) applied over the import.
  `DATA_IMPORT.md` §4.2 presents the Sp. Atk choice as correct and needs correcting with it.
- **Data: `BaseExperience` is the modern value** (`PokemonImport.cs:155`, feeds `Gen1BattleRules.cs:263`) —
  Chansey 395/255, Mew 270/64, Magikarp 40/20, Pikachu 112/82.

### R1b — follow-ups from the lost-answer fix's `pr-review` (2026-10-02, verdict PR-READY)

The fix itself (archived: `TODO_ARCHIVE.md` → *Repo-sweep R1 — lost modal answers…*) passed `pr-review` with no
blocking findings; these are the cheap, no-behaviour-change follow-ups it recommended, **in priority order**.
All are unadjudicated — the user decides fix / waive / defer per item (recommendation: 1–4 and 6 together,
about 20–30 min; 5 as a rename; leave 7–9 unless they bite).

1. **Test the branch the fix actually exists for** (`SignalRInputTests.cs`, shop section). All five shop tests
   send the second answer *before* awaiting `first`, so they only hit the "pending TCS already completed" path.
   Add `ShopActions_LeaveArrivingAfterBuyWasConsumed_IsServedByTheNextPrompt`: start a prompt, send BUY,
   `await Within(first)` (the prompt is now cleared), send LEAVE, assert the next `ChooseShopActionAsync` is
   already completed with LEAVE (~10 lines) — the "no prompt pending, shop open" branch the
   `SignalRInput.cs` comment describes only in words.
2. **Test that serving a queued LEAVE clears the backlog** (`SignalRInputTests.cs`; `CloseShopIfLeaving` at
   `SignalRInput.cs:312`). Start a prompt, send BUY, LEAVE, then a stray BUY, all before awaiting; assert `first`
   = BUY, the second prompt = LEAVE, and a third prompt is **not** completed (~12 lines).
3. **Tie `RESTORE_PROMPT`'s `key` to its `value` by type** (`timeline.ts` Action union, `answerPrompt.ts`,
   `battleReducer.ts`). Today `value` is the union of every prompt type and the reducer's computed-key spread is
   unchecked, so `{ key: 'rewardChoice', value: <a shop prompt> }` compiles. Build the action from a mapped type
   (`{ [K in PromptKey]: { type: 'RESTORE_PROMPT'; key: K; value: NonNullable<BattleState[K]> } }[PromptKey]`) and
   make `PromptAnswer`/`submitPromptAnswer` generic over `K extends PromptKey` (~10 lines, types only).
4. **Document the accepted "rejected-after-sent" trade-off** (`ARCHITECTURE.md` §2.7, the *modal answer must not be
   lost* bullet; one sentence). The rationale assumes a rejected call means the server never got the answer, but
   SignalR also rejects a call already sent when the connection closes before the reply: the server consumed it
   and the client re-opens a modal that is no longer live. Mostly harmless (a one-shot answer to it is dropped; a
   shop BUY is dropped because the shop is closed) — but it is the exact case a prompt-id protocol would have
   covered, so record that it was accepted and why.
5. **Make `Cancel_MidShop_…` pin what it actually pins** (`SignalRInputTests.cs:281-293`,
   `SignalRInput.cs` `Cancel`). It passes even if `Cancel` doesn't clear the backlog, because `_cancelled` never
   resets and `ChooseShopActionAsync` throws at its cancelled check before reaching the backlog. Rename it to
   say "cancel throws even with a non-empty backlog" and mark the `_shopOpen = false; _shopBacklog.Clear()`
   lines as housekeeping — or drop them (one-line edit).
6. **Fix the `PRODUCT_SPEC.md` wording** (the Session-resume bullet, ~line 245): "if the server rejects it" is
   inaccurate — the rejection comes from the client's own SignalR library during a reconnect. Also confirm the
   behaviour change is intended: rapid repeated shop BUY clicks are now all honoured where some were dropped.
7. *Advisory —* a restored shop uses the balance from the LEAVE click (`battleReducer.ts` `RESTORE_PROMPT`); a
   `SHOP_PURCHASED` landing between the click and the restore leaves `shop.balance` stale vs. `gold`. Cosmetic
   (the server rechecks every buy); a fix would apply `state.gold` to `balance` when restoring the shop.
8. *Advisory —* nothing tests that each of the ten `answerPrompt` callbacks in `useBattleHub.ts:193-255` pairs the
   right slot, hide action and hub method (the callbacks they replaced weren't tested either; item 3 catches a
   slot/value mismatch but not a wrong hide action).
9. *Advisory —* `SignalRInput.cs:334` ends the shop only on LEAVE (`action is not LeaveShop`), while
   `ShopRunEvent` ends on any non-BUY answer. With only BUY and LEAVE today they agree; if a third `ShopAction`
   is ever added, `action is not BuyShopItem` would keep them in step.

### R2 — correctness, fidelity and safety (next)

- **Gen 1 fidelity — damage/accuracy core** (verify first): `DamageCalculator.cs:219-223` skips the `2L/5` and
  `/50` floors `GEN_DIFFERENCES.md` documents (~24% high at L12); `Gen1BattleRules.cs:213-219` uses the
  3-based accuracy/evasion table (Gen 1 uses the standard table); crit multiplier should be ≈(2L+5)/(L+5), not
  flat 2× (`:258`); multi-hit re-rolls crit/variance per hit and continues past a broken Substitute
  (`AttackAction.cs:379-404`); recoil/drain/Struggle use full calculated damage, not HP actually lost
  (`AttackAction.cs:402,430-437`); Ice can freeze Ice / Electric can paralyse Electric (`Gen1BattleRules.cs:161`);
  Fly/Dig not untargetable on the charge turn (not listed as a known gap).
- **Engine: trap persists after the trapper faints** (`StatusResolver.cs:46-50`); **recharge turn can be lost
  twice** (`Battle.cs:195` runs `CanAct` before `AttackAction.cs:69`'s recharge check) and the `CanAct` order
  differs from Gen 1 (sleep → freeze → trapped → flinch → recharge → confusion → paralysis); full paralysis and
  confusion self-hits don't cancel Thrash/Bide/charge/trap.
- **Data: Psychic Special-drop chance is 10%, Gen 1 is 33%** — no `psychic` case in `MoveImport.cs:267-330`;
  `SecondaryEffectContractTests.cs:22` pins the wrong value. **X Accuracy imported as +1 accuracy stage**
  (`ItemMapper.cs:163`, pinned by `ItemImportTests.cs:229`); Gen 1 skips the accuracy check entirely (the
  OHKO quirk).
- **Importer: offline/failed run wipes evolutions and exits 0** (`EvolutionImport.cs:24` deletes before fetch,
  swallows errors); no retry/429/timeout anywhere; `Program.cs:111` always prints "Import Complete!". A failed
  `SaveChangesAsync` poisons the shared DbContext for the rest of the run (`MoveImport.cs:29/82`,
  `PokemonImport.cs`, `ItemImport.cs`); learnset delete+insert isn't transactional.
- **Player species always taken from the starter** (`BattleScreen.tsx:77,237,400`, `activeGame.ts`) — game-over
  shows the starter's sprite with the final creature's name after evolution/lead change; after a refresh Phaser
  draws the starter for the whole battle. Needs the player species id on the replayed `BattleStarted` (server
  change); Continue button also shows the starter's name/level.
- **CI/deploy hardening** — `fly-deploy.yml` runs no tests before `flyctl deploy`; `setup-flyctl@master` is an
  unpinned moving branch and there is no `permissions:` block; tags can point at any commit. **Container runs as
  root** (`Dockerfile:44-52`; use `USER $APP_UID` after checking SQLite write needs).
- **Dev scripts**: `-StartStack` leaves the backend running (`test.ps1:119/148`, `e2e.ps1:180/357` kill only the
  `dotnet run` parent); `stop-dev.ps1:103` kills *anything* on :5173 (another project's Vite); `e2e.ps1:218`
  `-f` binds as a second `-ForegroundColor` and crashes the failure path; `test.ps1 -E2E -StartStack` exits 0
  when the backend never starts.
- **Pre-commit hook gaps** (`.githooks/pre-commit`): hardcoded dotnet path (`:11`); checks the working tree not
  the staged snapshot (`:14,22`); skips deletes/renames and `.csproj`/`Directory.Build.props`/`package.json`/
  `tsconfig.json`/`*.db`-only commits (`:20,33`).
- **Public endpoint exhaustion** — `POST /api/game/start` unauthenticated, no rate limit, no cap on concurrent
  runs/connections, no idle timeout, no `fly.toml` health check or concurrency block (single 1 GB VM).
  Unmeasured; also no security headers/`UseForwardedHeaders` (low impact: no auth/cookies).
- **Full Restore refuses a full-HP creature that has a status** (`ItemEffects.cs:71-74`
  `HealingItemEffect.CanApply` requires HP < max) — found by the requirements-review of the `CarriedStatus` cure
  fix (2026-10-02), deferred by the user as its own item. In Gen 1, Full Restore at full HP with a major status
  cures it and consumes the item (**verify against pokered first**); fix = let `CanApply` pass when
  `CuresAllStatus` and a status is present, and skip the zero-amount `Healed` event in `Apply`. Same cure-path
  family as the archived `CarriedStatus` fix. Related aside: `Creature.FullHeal` and `ReviveItemEffect` duplicate
  `ClearStatus`'s reset instead of routing through it (consolidation optional).
- **Test gap: `GameSessionManager` connection-routed methods** — `SetItemChoice` unknown-id fallback
  (`:441-448`), `DetachConnection` stale-connection guard (`:657-667`), `ScheduleAbandon`/`CancelAbandon`
  (`:748-774`) (reachable without SignalR; overlaps the existing Tier-4 connection-lifecycle item and covers the
  paths behind R1).

### R3 — robustness, UX and accessibility

- **Modal accessibility**: `Modal.tsx:35-44` has `aria-modal` but no focus trap/initial focus/inert background —
  Tab still reaches QUIT (abandons the run, no confirmation); battle log has no `role="log"`/`aria-live`.
- **Double-submit creates two runs** (`StarterSelection.tsx:45-72`, `NicknameModal.tsx:32,38`); **`/battle` with
  no saved game shows "Connecting…" forever** (`BattleScreen.tsx:78`, `useBattleHub.ts:52`).
- **Level-up panel covers the player nameplate** — same `bottom/right` anchor as `.player-corner` at higher
  z-index (`BattleScreen.css:800` vs `:114`); the independent-absolute-position pattern the UI no-crowding rule
  warns against.
- **Battle log unbounded** (`battleReducer.ts:262`, `BattleScreen.tsx:286`) and the whole `BattleScreen`
  re-renders on every dispatch.
- **Phaser**: resize mid-attack leaves sprites at the old x (`BattleScene.ts:122-137` vs lunge/shake never
  clearing `*Rested`); `entryComplete` is emitted but unheard, so FIGHT unlocks ≈2.6 s before sprites arrive.
- **Backend session edges**: half-started run leaves a broken registered session (`GameSessionManager.cs:226`);
  `Party.Members` enumerated off-thread (`:501` `UsableInBattle`, also `Party` hands out its live `List`) can 500
  `GET /bag`; abandon timer can still cancel a just-reconnected run (`:755-763`); second tab on the same
  `gameId` silently takes over (`:195-205`); unknown `/api/*` returns `index.html` 200 (`Program.cs:57`).
- **Engine smaller Gen 1 gaps** (verify each): confusion self-damage applies variance and ignores stages/Burn
  (`DamageCalculator.cs:264`); binding duration uniform 2–5 vs 3/8-3/8-1/8-1/8; Stat Exp `/4` unfloored + sqrt
  floored not ceiled (`Gen1StatCalculator.cs:20`); Psywave max one too high (`Gen1BattleRules.cs:150`); Mist
  message before the chance roll and Mist shouldn't block secondary drops (`AttackAction.cs:657`); Leech Seed
  ignores Toxic counter (`Battle.cs:674`); Fire move thaws via a Substitute hit (`AttackAction.cs:270`); trainer
  XP 1.5× rounding (`:267`); post-Transform crit speed; Substitute at exactly the HP cost should succeed
  (`MoveEffects.cs:497`).
- **Importer/data lows**: `MoveImport.cs:175` overwrites the Gen-1-resolved `EffectChance` with today's ailment
  chance; dev server can read stale data (Web csproj copies `*.db` only, never `-wal`; `DbPathHelper.cs:18`
  prefers the bin copy); item `Cost` is modern and now drives reward rarity/starting bag (five cures tie at 200,
  starter pair picked by row order; `DATA_IMPORT.md` §4.5's "no shop" rationale is stale); Yellow exclusion list
  wrongly drops the Sandshrew/Vulpix/Oddish/Mankey/Growlithe/Bellsprout lines (no gameplay effect).
- **Seam leak / AI / validation**: `DamageCalculator.cs:189-198` uses Special stages directly, bypassing
  `GetOffensiveStat`/`GetDefensiveStat` (a Gen 2 Special split would need a change here); AI evaluators overvalue
  10% secondary status and Thunder Wave vs Ground (`MoveEvaluators.cs:153-177`); `Battle.cs:529-531` trusts a
  `MoveTurnChoice` (no PP/Disable check; only `SignalRInput.ResolveMove` validates); latent `Biome.cs:301`
  crash if a future profile's playable set is disconnected.
- **Open question (documented, but Gen 1 differs)**: after Roar/Whirlwind the scared-off foe still acts
  (`Battle.cs:311-313`), which can end the run via a faint with a healthy bench (`GAME_LOOP.md:135`); Gen 1 ends
  the battle immediately.

### R4 — hygiene, dependencies and nits

- **Dependency advisories**: `npm audit` 15 (1 critical `vitest` 2.1.0 — dev-only, not exposed by `vitest run`;
  6 high incl. `vite`/`esbuild`/`@playwright/test`; shipped to players: only `react-router-dom` 6.30.3
  moderate) — `npm audit fix` clears most; `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 High (GHSA-2m69-gcr7-jv3q) via
  EF Core Sqlite 9.0.6, low practical risk, invisible in builds.
- **Test hygiene**: `shop.spec.ts:22` zero-count assertion can't fail (`startBattle` closes the shop first);
  `encounter-map.spec.ts:94` ignores `playToNextEncounter`'s result; `DevModeTests.cs:124` never exercises
  `MinDamage`; `cadence.spec.ts` can pass trivially; the pre-existing `SignalRInputTests` handshake tests have no timeout (the newer shop-backlog tests are
  bounded with a 5 s `WaitAsync`);
  `WebEventContractTests.cs:77` greps all of `timeline.ts` for `case 'Name'` (can be fooled by node-kind labels);
  ~33 `new Battle(...)` calls with no `rng:`; 10 duplicated `Fighter(...)` helpers + duplicated seeded-start
  preamble + duplicated `fakeLocalStorage`; `MovesFixture.cs:48` "fresh copy" is a shared cached instance;
  `TestAltProfile` belongs in `TestSupport/`; two no-assertion "must not throw" tests.
- **Frontend nits**: `/party` JSON dispatched `as never` (`useBattleHub.ts:116`); `"unknown"` reward kind renders
  a blank card (`RewardChoiceModal.tsx:32`); `SpeciesCard` Enter-only (no Space); `TypeBadge` ≈2:1 contrast on
  light types; `SettingsScreen` `nav(-1)`; title-screen notice reappears on reload; dead code
  (`PhaserBridge` `enterBattle`/`entryComplete`, `AudioEngine.playTick`/`playFaintCry`, two `eslint-disable`s).
- **Stale comments/docs**: `timeline.ts:613-617` ("auto-acks"), `:330-332` (gold HUD "later pass"), `bag.ts:18-20`
  (Revive "no effect yet"), `battle-ui-cues.spec.ts:9-12`, `BattleIntegrationTests.cs:250` (`Console.ReadKey`),
  `Wallet.cs:25`, `AttackService.cs:48` (cites TODO.md, lives only in the archive), `Gen1TypeChart.cs:10,127`
  (Bug→Poison became 0.5×, not 1×), `GameSessionManager.cs:25-27` ("gives up ~30 s").
- **Script/Docker polish**: `dev.ps1:30-31` apostrophe-in-path; `dev.ps1:16` doesn't clean exited-server window
  shells; `build-release.ps1:200` leaves cwd changed; `Dockerfile:25-28` `COPY . .` re-downloads every sprite on
  any source change, base images tag- not digest-pinned; `GameController.cs:66` logs only `ex.Message`;
  `SignalRBattleEventEmitter.cs:86` discards the send task; client-chosen run `Seed` (product decision, not a
  bug).
- **Speculative, unverified** (verify before filing as real): `waitForBridge` resolving early on a slow
  evolution-sprite load; timeline queue not cleared on unmount (leftover steps into a new run); corner map
  preview covering the player corner on long node paths; boss framing lost after refresh; Sludge poison chance
  possibly 40%; `AttackService.GetRandomAttackAsync` `Skip` without `OrderBy` (no callers); Substitute vs
  sleep/paralysis status moves and Disable 1–8 turns; untested types `RunLoop`/`RewardRunEvent`/
  `AcquisitionResolution`/`CompositeEvaluator`/`MoveEvaluators` (likely covered indirectly — run coverage).

**Clean (checked, no findings):** Dev Mode gating (server flag; ranges stripped at the emitter's single send
point), client-supplied index/id bounds checks, CORS, event wire projection + REST DTOs, XSS (`dangerouslySetInnerHTML`/
`innerHTML`/`eval` absent), SignalR + Phaser listener cleanup, secrets in tracked files/history, EF schema vs
snapshots vs live dbs, runtime queries (no N+1), Gen 1 type chart and XP curves, HP DV derivation, island layout
generation.

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
  next time a feature has to touch the signature anyway (**In-Combat Switching**, shipped 2026-07-25, reused the
  existing `playerParty` param and didn't need to). Not worth a standalone churn commit: every call site is a test
  or the run layer, and both are stable.

**Filed 2026-07-31 from a review pass over the Generation Profile Stage 1a–2b commits** (`e603478`…`fa952e4`;
the threading discipline itself is sound — every seam explicit, no `?? Gen1…` default reintroduced, each stage
carries its `TestAltProfile` leg). Two of the three closed the same day as Stage 3 riders (full write-ups in
`TODO_ARCHIVE.md` → *Tech-Debt cleanups*): the **five-site learnset-query duplication** in `EncounterFactory`
(→ one `LoadLearnsetsAsync` home) and the **per-call `GenerationProfiles.Registered` allocation** (→ materialised
once). One remains:

- [ ] *(low, watch — do not refactor speculatively)* **`EncounterFactory`'s profile threading is growing the same
  way `Battle`'s constructor did.** `BuildCreature` is at 8 parameters; `profile` + `allMoves` (+ `rng`) travel
  together through every public entry point and both supplier builders. Same precedent as the `Battle` item
  above: the next time a feature has to *widen* these signatures anyway, consider a small run-scoped parameter
  object (profile + move pool + rng — the things that are fixed per run) instead of a fourth parallel parameter.
  **Stage 3 (2026-07-31) did not trip the trigger** — it added zero parameters (the region rides on the already-
  threaded profile), so the watch stands for Stage 4 or whatever next touches the shape. The threading being
  *explicit and required* is the feature's deliberate design (no defaults — see `GENERATION_PROFILE.md` §4.2);
  a parameter object preserves that, it just stops the arity creep.

*(The 2026-07-19 repo-wide PR-audit is now fully closed — all five findings resolved: four fixed & archived in
`TODO_ARCHIVE.md`, the DB-services try/catch convention decided above, and the `SignalRInput` cancel/prompt race
deliberately waived by the user (memory `project_waived_cancel_race`). Don't re-file "Repo-wide PR-audit
findings" as an open section.)*

### Known Gaps
- **Session Resume doesn't cover a reconnect during a between-node blocking prompt.** Found 2026-09-14 while
  shipping **Session Resume** (`TODO_ARCHIVE.md`): the fix for the full-remount reconnect deadlock replays only
  the last `BattleStarted`/`TurnStarted`, so a refresh reattaches cleanly mid-battle but a refresh while a
  route-choice, shop, reward-choice, recovery, acquisition, lead-choice, or switch-in prompt is open still hangs
  on "Connecting…" — unchanged from before this feature, not a regression it introduced. Deliberately
  out-of-scope for the lightweight Session Resume feature; would need each blocking-prompt event cached/replayed
  the same way, or folded into the heavier `save.db`-backed resume (Tier 3 — Game Loop & Progression).
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
- **Open, unplanned — Gen 1 has NO end-of-turn residual phase; each creature ticks its own poison/burn/Leech
  Seed right after its own action, so our turn shape is wrong.** Found 2026-09-20 while answering the question
  `requirements-review` raised 2026-09-13 ("does the faint-ends-the-turn rule cover a faint caused by the residual
  tick itself?"; history → `TODO_ARCHIVE.md` → *End-of-turn residual … fired even after a same-turn faint*).
  Verified against the Gen 1 source (pret/pokered `engine/battle/core.asm`, `MainInBattleLoop` lines 413-468,
  raw file read 2026-09-20): `HandlePoisonBurnLeechSeed` uses `hWhoseTurn`, so the order is **A acts → A's own
  residual → if A fainted, jump to A's faint handler (B never acts) → else B acts → B's own residual → if B
  fainted, B's faint handler.** `Battle.cs` (~lines 179-231) instead runs both actions, then `TickTurnCounters`,
  then both creatures' residuals at end of turn (gated by `IBattleRules.FaintEndsTurnImmediately` only when both
  are alive). Three discrepancies: **(a)** the slower creature acts even when the faster one would have died to
  its own tick first; **(b)** the faster creature's tick lands after the slower one's attack instead of before
  it; **(c)** a residual-caused double-faint is impossible in Gen 1 (the first faint jumps to the handler) but our
  engine can produce it — the second `ApplyEndOfTurnDamage`/`ApplyLeechSeedDrain` still runs after the first
  faints something. The 2026-09-13 `FaintEndsTurnImmediately` fix only covers a faint from a direct hit.
  `RunDirectorTests.Runner_DoubleFaintFromEndOfTurnPoison_EndsTheRun_ButStillCountsTheWin` pins a path Gen 1
  cannot produce (the 2026-07-28 mutual-KO ruling still stands for genuine trades like Explosion — it would need
  a different scenario, not a different ruling). This is a real generation difference (Gen 2+ moved residual to
  a true end-of-turn phase), so the fix belongs **behind an `IBattleRules` seam alongside
  `FaintEndsTurnImmediately`** — design rationale in `GEN_DIFFERENCES.md` → Status Quirks, seam row in
  `GENERATION_SEAMS.md` §2. It rewrites `Battle`'s central turn loop: needs a `/plan`, `opus-engineer`
  implementation, and both review gates. **Not yet decided when to plan it — unscheduled, deliberately not
  placed in the tier list.**
- **Wild/draft selection has no per-area or natural-minimum-level awareness — evolution floors DONE, the broader
  question open.** Raised 2026-09-12 after the user met a level-25 Exeggcutor. The evolution-stage half is fixed:
  `EvolutionMinLevel.Compute` (`ENCOUNTER_DESIGN.md` §3.8) floors every evolved species at the level its chain
  requires — a `Level` edge's threshold, the trade-evolution stand-in (37), and, since 2026-10-02, a flat
  **`StoneEvolutionLevel` (30)** for a `Stone` edge — and `EncounterFactory` filters the pool by it before
  `PickByBst` (wild/Elite/Boss and draft); full record in `TODO_ARCHIVE.md` (*Species selection respects each
  species' evolution-chain floor*; *Level-Gated Strong/Boss Movesets* for the stone floor). **What's still open:**
  `PickByBst`/`ScaleWildLevel` select purely by BST band with no per-area/per-level encounter table (`ENCOUNTER_DESIGN.md`
  confirms this is deliberate), so a high-BST species can still surface far below where the original games would place
  it (Cerulean Cave is a late/post-game area; this system has no notion of "late-game area"). Open question for a
  real design pass: is a per-species real-game minimum encounter level worth folding in as a soft floor, or is "BST is
  the only lever beyond the evolution floor" an accepted tradeoff of the roguelite's simplified encounter model. No
  `/plan` done on this remaining piece.
- Enemy encounter pool ignores game version — filter by `PokemonGameAvailability` once a version selector exists.
- Enemy Pokémon do not evolve — wire into level-up when Game Loop is built.
- ~~**Endless-chain double-faint**~~ — **RESOLVED 2026-07-28**: a mutual end-of-turn DoT double-faint now counts
  as the player's win and promotes a survivor whenever the party has a live bench member; it only remains a loss
  for a **lone** creature with nobody left to promote (`RunDirectorTests.Runner_DoubleFaintFromEndOfTurnPoison_EndsTheRun_ButStillCountsTheWin`
  — note both the class and the name, neither of which matches the formerly-cited
  `BattleRunnerTests.…_CountsAsLoss_NotAWin`: that class doesn't exist in this repo, and the test was renamed when
  the win-tally decision flipped its `BattlesWon` pin 0 → 1). See
  `TODO_ARCHIVE.md` → *Mutual KO ends the run even with a live bench*.
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
