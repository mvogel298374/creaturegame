# Battle Sim – TODO

> **Active work only.** Finished work lives in [`TODO_ARCHIVE.md`](TODO_ARCHIVE.md); design lives in the design
> docs (`ARCHITECTURE.md` §5 indexes them). **See also:** `CLAUDE.md` (setup/commands) · `AI_CONTEXT.md`
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
- **Gen 1 facts** are checked against the primary source (pret/pokered for Gen 1, pret/pokecrystal for Gen 2), not
  against the repo's own docs or comments — both have been wrong (see §9).
- **Comment budget:** new code carries no comments beyond one short line; logic documentation goes in markdown
  (`CLAUDE.md` → *Design Rationale Placement*).
- **Gate 1 — nothing is implemented until its entry is Ready** (`CLAUDE.md` → *Process gates*,
  `DEFINITION_OF_READY.md`). The **Ready?** column in §1 is the standing audit: ✅ every DoR item is answered; ⚠️
  designed but provisional or awaiting your decision; ✗ missing items (named). **A greenlight is only valid on ✅.**
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
| 1 | **Dev-script and pre-commit-hook gaps** (§7.1) | S–M | ✗ no per-script acceptance conditions (the `dev.ps1`/`stop-dev.ps1` part is done) | Remaining: `-StartStack` backend leak, `test.ps1 -E2E -StartStack` exit 0, and the hook's blind spots (`.csproj`, `*.db`, deletes). Protects every later commit and test run. |
| 2 | **Verification pass over the unverified findings** (§3, §5, §7) | S–M | ✗ no scope or output format (batch size; what "confirmed" means) | Cheap read-only agents in batches; turns ~45 "reviewer's reading" items into confirmed work or discards. Opens up everything below it. |
| 3 | **Server test gaps — `GameSessionManager` lifecycle + the `RunFaulted` path** (§6) | M | ⚠️ needs the injectable-clock design | Unblocks the R1d tests and closes a long-open Tier-4 item. |
| 4 | **Importer hardening** (§3.2) | M | ✗ no design (retry policy, transaction boundaries) or acceptance | Failure no longer wipes evolutions or exits 0; unblocks CI/Docker imports and the multi-generation importer work. |
| 5 | **R1d-a — replay an open blocking prompt** (§2) | M | ⚠️ designed, but provisional-pending-`/plan` and needs your three decisions (§8) | The only reconnect item that strands a run; highest player value. |
| 6 | **CI E2E step** (§6) | M | ✗ no design (runner, stack boot, caching) or acceptance | The only automatic E2E coverage; makes "agents don't run E2E" safe, and unblocks checking the BST/balance flag and the reconnect spec. |
| 7 | **R1d-d, R1d-c** — refresh-state snapshot + sprite; second-tab policy (§2) | M each | ⚠️ provisional-pending-`/plan`; (c) needs your policy call | Both extend (a)'s replay work. |
| 8 | **Gen 1 fidelity — the damage/accuracy core** (§3.1) | L | ✗ needs the Gen 1 source per claim, the seam surface, and the quirks to pin | Verify-first; large blast radius (it touches every damage number). |
| 9 | **End-of-turn residual phase** (§4.2) | L | ✗ needs a `/plan` | A `/plan`, `opus-engineer`, and both review gates; rewrites `Battle`'s turn loop. |
| 10 | **Features** — Catch, `save.db`, progressive difficulty, Generation Profile 4d+, … (§4) | L | ✗ each needs a `/plan` | Catch and stone evolutions are unblocked by the bag-scope ruling (`ARCHITECTURE.md` §2.12); `save.db` is declined for now. |

---

## 2. Reliability — reconnect (designed in [`RECONNECT_RESILIENCE.md`](RECONNECT_RESILIENCE.md), provisional-pending-`/plan`)

Verified in code 2026-10-03. Fixed and archived already: the flee stale-cache, the 60 s grace, the client replay
dedupe, lost modal answers, the `RunFaulted` notice, and the abandon-timer race (R1d-b). The server replaying on every reconnect is an **accepted
design** (it cannot tell a refresh from a blip), not a bug.

- **R1d-a — an open blocking prompt is not replayed (strands the run).** Ten prompts park the run on a
  `SignalRInput` handshake and none is cached, so after a refresh *or a blip* the modal never reappears, the server
  stays parked, and reconnect cancels the abandon timer — the player can only QUIT. **Acceptance:** refresh or blip
  with any of the ten prompts open → the modal reappears and is answerable and the run continues; an answered
  prompt never reappears; a replayed shop shows the current balance. **Design:** the emitter caches the open
  prompt and replays it last; cleared by one `PromptAnswered` hook. **Needs-decision:** design points in the doc.
- **R1d-c — a second tab on the same `gameId` silently takes over.** **Needs-decision:** the recommended policy is
  last tab wins, with the displaced tab told via a `SessionTakenOver` notice that must **not** clear `activeGame`
  (it is shared `localStorage`). **Acceptance:** second tab → first tab shows the notice and returns to Title; the
  second continues; a plain refresh never shows the notice.
- **R1d-d — a refresh loses the route trail, node pin, boss framing and the player's sprite.** **Acceptance:** a
  refresh at any point restores the full trail and pin, keeps boss framing, and shows the current lead's sprite.
  **Design:** an absolute `RunMapSnapshot` sent last and applied as an ordered step (not an immediate action —
  the client's queue lags arrival), plus `PlayerSpeciesId` on `BattleStarted` and a reducer-held
  `playerSpeciesId` (also fixes the post-evolution sprite, §5).
- **Test plan** (in the doc): C# unit tests, Vitest, and a new opt-in `reconnect.spec.ts` driven by a Dev-Mode-gated
  `DropConnection` hub method. It also closes three open verification gaps: replay-after-blip has never been
  checked in a browser; the `useBattleHub` wiring of `replayDedupe` is untested (only the pure helper is); the 60 s
  grace rests on SignalR's documented 0/2/10/30 s schedule, not a measured run.
- **`RunFaulted` server-half test gap** — nothing pins `SendRunFaulted` or the `GameSessionManager` catch-all call
  (the client handler's mapping is covered by R1d-c's `transportNotices` extraction).

---

## 3. Correctness and Gen 1 fidelity

All items below are **unverified** (a reviewer's reading) unless marked; the Gen 1 value in each must be checked
against pret/pokered before the engine or data changes (`GEN_DIFFERENCES.md` is a secondary source). Fixing a
data/engine item usually also means updating the test that pins the wrong value.

### 3.1 Engine

- **Damage/accuracy core (L):** `DamageCalculator.cs:219-223` skips the `2L/5` and `/50` floors `GEN_DIFFERENCES.md`
  documents (~24% high at L12); `Gen1BattleRules.cs:213-219` uses a 3-based accuracy/evasion table (Gen 1 uses the
  standard table); crit multiplier should be ≈(2L+5)/(L+5), not a flat 2× (`:258`); multi-hit re-rolls crit/variance
  per hit and continues past a broken Substitute (`AttackAction.cs:379-404`); recoil/drain/Struggle use full
  calculated damage, not HP actually lost (`AttackAction.cs:402,430-437`); Ice can freeze Ice / Electric can
  paralyse Electric (`Gen1BattleRules.cs:161`); Fly/Dig are not untargetable on the charge turn (not listed as a
  known gap).
- **Turn-order/status edges:** a trap persists after the trapper faints (`StatusResolver.cs:46-50`); the recharge
  turn can be lost twice (`Battle.cs:195` runs `CanAct` before `AttackAction.cs:69`'s recharge check) and the
  `CanAct` order differs from Gen 1 (sleep → freeze → trapped → flinch → recharge → confusion → paralysis); full
  paralysis and confusion self-hits don't cancel Thrash/Bide/charge/trap.
- **Smaller Gen 1 gaps:** confusion self-damage applies variance and ignores stages/Burn (`DamageCalculator.cs:264`);
  binding duration is uniform 2–5 vs 3/8-3/8-1/8-1/8; Stat Exp `/4` unfloored + sqrt floored not ceiled
  (`Gen1StatCalculator.cs:20`); Psywave max one too high (`Gen1BattleRules.cs:150`); Mist message before the chance
  roll and Mist shouldn't block secondary drops (`AttackAction.cs:657`); Leech Seed ignores the Toxic counter
  (`Battle.cs:674`); a Fire move thaws via a Substitute hit (`AttackAction.cs:270`); trainer XP 1.5× rounding
  (`:267`); post-Transform crit speed; Substitute at exactly the HP cost should succeed (`MoveEffects.cs:497`).
- **Open question (documented, but Gen 1 differs):** after Roar/Whirlwind the scared-off foe still acts
  (`Battle.cs:311-313`), which can end the run via a faint with a healthy bench (`GAME_LOOP.md:135`); Gen 1 ends
  the battle immediately. **Needs-decision.**
- **Seam leak / AI / validation:** `DamageCalculator.cs:189-198` uses Special stages directly, bypassing
  `GetOffensiveStat`/`GetDefensiveStat` (a Gen 2 Special split would need a change here); AI evaluators overvalue
  10% secondary status and Thunder Wave vs Ground (`MoveEvaluators.cs:153-177`); `Battle.cs:529-531` trusts a
  `MoveTurnChoice` (no PP/Disable check; only `SignalRInput.ResolveMove` validates); latent `Biome.cs:301` crash if
  a future profile's playable set is disconnected.

### 3.2 Data and importer

(Aside: `Creature.FullHeal` and `ReviveItemEffect` duplicate
`ClearStatus`'s reset instead of routing through it.)

- **Gen 1 gate-order and Dire Hit/Guard Spec gaps (found during X Accuracy; unverified beyond that review).**
  `MoveHitTest` fails Dream Eater on an awake target and runs the OHKO speed check *after* the roll draw; ours do
  both before the roll, so the rng draw order differs when X Accuracy is not in use. pokered's `ItemUseDireHit` does
  not refuse a second use either (Guard Spec unchecked), while ours does. Verify at pokered before changing.
  Dependency: `XAccuracyContractTests.XAccuracyMakesAFasterOhkoUserLandThroughMaxedEvasion` holds only because our
  OHKO speed check runs before the roll — keep it as the regression guard if the gate order is changed. Also:
  item descriptions are PokeAPI's modern text as-is (only X Accuracy's is overridden), so Dire Hit / Guard Spec and
  the X-items still describe the modern effects — unverified, check before a descriptions pass.
- **Importer hardening (M):** a failed/offline run wipes evolutions and exits 0 (`EvolutionImport.cs:24` deletes
  before fetching and swallows errors); no retry/429/timeout anywhere; `Program.cs:111` always prints "Import
  Complete!"; a failed `SaveChangesAsync` poisons the shared DbContext for the rest of the run (`MoveImport.cs:29/82`,
  `ItemImport.cs`); learnset delete+insert isn't transactional. (The species importer now fails loudly and exits
  non-zero — archived.)
- **Importer/data lows:** `MoveImport.cs:175` overwrites the Gen-1-resolved `EffectChance` with today's ailment
  chance; the dev server can read stale data (Web csproj copies `*.db` only, never `-wal`; `DbPathHelper.cs:18`
  prefers the bin copy); item `Cost` is modern and now drives reward rarity and the starting bag (five cures tie at
  200, the starter pair is picked by row order; `DATA_IMPORT.md` §4.5's "no shop" rationale is stale); the Yellow
  exclusion list wrongly drops the Sandshrew/Vulpix/Oddish/Mankey/Growlithe/Bellsprout lines (no gameplay effect).
- **Balance flag — every species' BST changed (2026-10-03).** BST feeds `EncounterSelector.Bst`/`PickByBst`, the
  strength tiers (×0.85/1.10/1.20) and `ScaleTargetBst`, so enemy pool bands shift and the starter picker's BST
  display changed (e.g. Venusaur 425); base experience also dropped sharply for some species (Chansey 395→255,
  Mew 270→64). The tier offsets were tuned against modern numbers. Seeded E2E specs may land on different
  encounters. **Decided 2026-10-06 (user): keep the current tier bands, no retune** — the full E2E run that day was
  36/38 with no failure attributable to the bands; reopen only if playtests show difficulty spikes or lulls (§9).

### 3.3 Backend session edges (unverified)

A half-started run leaves a broken registered session (`GameSessionManager.cs:226`); `Party.Members` is enumerated
off-thread (`:501` `UsableInBattle`; `Party` also hands out its live `List`) and can 500 `GET /bag`; an unknown
`/api/*` returns `index.html` 200 (`Program.cs:57`).

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
- [ ] **Stone evolutions** — the `Stone` trigger and `IEvolutionRules.StoneUsed` are built and dormant, waiting only
  on stones becoming obtainable as items (per-run bag; no persistence needed). (A flat `StoneEvolutionLevel` of 30 floors stone-evolved species in wild/draft selection.)

### 4.2 Run loop and progression

- [ ] **`PlayerSave`/`SavedCreature` in `save.db`** — **DECLINED for the development phase (user, 2026-10-03):
  no server-restart-surviving saves** (`ARCHITECTURE.md` §2.12); kept here only as the parked design. Auto-save after
  each battle, party-management UI; the heavier persistence beyond the lightweight Session Resume (survives a
  server restart/redeploy, not just a client refresh). When revisited it is two tiers — a mid-run snapshot and a
  cross-run profile for the roguelite meta unlocks (unplanned). **Fly deploys must stay single-machine until session state is externalised here:**
  `GameSessionManager` is in-process, so a second machine 404s any REST call routed to the machine that never saw
  `/start`; the workflow pins `flyctl deploy --ha=false` — don't remove it or raise `min_machines_running`
  (`ARCHITECTURE.md` §2.7).
- [ ] **Progressive difficulty** beyond `targetBst = lead BST + depth × 10`; trainer encounters at milestones. A
  good pairing point for the per-area encounter question below.
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
- [ ] **Wild/draft selection has no per-area or natural-minimum-level awareness** *(needs-plan)*. The evolution-floor
  half is done (`EvolutionMinLevel.Compute`, `ENCOUNTER_DESIGN.md` §3.8). Open: `PickByBst`/`ScaleWildLevel` select
  purely by BST band (deliberate per `ENCOUNTER_DESIGN.md`), so a high-BST species can surface far below where the
  original games place it. Is a per-species minimum encounter level worth folding in as a soft floor, or is "BST is
  the only lever" an accepted tradeoff? No `/plan` yet.
- [ ] **Enemy encounter pool ignores game version** — filter by `PokemonGameAvailability` once a version selector
  exists.
- [ ] **Enemy Pokémon do not evolve** — wire into level-up when the loop wants it.

### 4.3 Generation Profile — presentation catalog (Stage 4d+) and the falsification rule

Design: [`GENERATION_PROFILE.md`](GENERATION_PROFILE.md). Stages 1–3 and 4a/4b/4c (generation channel, the Kanto
Sage skin, the grid Town Map) are done and archived. The standing 2026-08-04 commitment keeps this ahead of Tiers
2–3 in the user's own ordering; cost-to-benefit places the cheap items above it in §1.

- [ ] **4d+ — the surface catalog, jointly iterated** (each surface its own greenlit mini-plan): move select, battle
  HUD, CHECK POKEMON (the main overview), BAG (a full skin beyond the 2026-08-23 legibility fix), the `PartyStrip`
  chips (`.party-chip*`), Title/StarterSelection (including the generation picker). The battle command menu is
  settled (2×2 grid, verbs fixed). Already skinned: the encounter overlay, every run-prompt modal, the SWITCH menu
  cards, CHECK POKEMON's party picker, and the level-up panel.
  - **Open wart:** `Modal.tsx` still imports `pages/BattleScreen.css` for shared button chrome (`.action-btn`,
    `.move-btn`); inverting that means extracting the chrome into its own sheet.
  - **Level-up panel follow-ups.** *Flagged, not decided — a domain claim for `requirements-review`:* whether Gen 1's
    real level-up box lists HP at all (recollection: four rows, ATTACK/DEFENSE/SPEED/SPECIAL) and whether it shows
    gains first, then totals on a keypress; today's panel shows five rows with gain + total together. Also:
    `.battle-screen` and `.battle-log` still carry their own copies of the double-frame recipe that
    `components/modals/ModalFrame.css` now shares. `level-up.spec.ts` is opt-in — recommend `.\e2e.ps1 -Spec level-up`.
- [ ] **Stage 5 — the falsification harness** is a standing requirement, not a final stage: each stage ships its
  leg of `TestAltProfile`, because upward compatibility is unfalsifiable with one profile.
- Phaser/canvas and per-gen sprite/cry assets stay deferred (`GENERATION_PROFILE.md` §7.6).

### 4.4 Multi-Generation data model and schema (deferred to a Gen 2 sprint)

The stat-selection abstraction is done. Open:

- [ ] **`Attributes` Special split:** `Special` → `SpAtk` + `SpDef` (keep `Special` as a Gen 1 computed alias);
  `Creature.BaseSpecial`/`DvSpecial`/`ExpSpecial` split in parallel.
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
- [ ] **Text feel — typewriter log scroll + menu blips.** The battle log renders ~15–20 ms/char; a cursor-move blip
  and a confirm blip on menu navigation. Pure client polish, no wire change; pairs well with the "Kanto Sage"
  pass as one "this is Gen 1 now" moment.
- [ ] **Sprite presentation FX — joint mini-plan not yet scheduled.** `SPRITE_PRESENTATION.md` §3 sketches eight
  unratified ideas. Recommendation: §3.1 (confirm `pixelArt: true`) is a one-line check, do it standalone; §3.4
  (type-coloured particles) folds into the animations item above (same wire dependency); §3.2 (a grounding shadow)
  is the strongest candidate for a joint sketch → ratify session.
- [ ] *(small)* **Escape = B-cancel on the prompts that have a negative answer** — evolution→CANCEL,
  acquisition→DECLINE, shop→LEAVE, move-replacement→don't-learn. The four required choices (`RouteChoice`,
  `RewardChoice`, `LeadChoice`, `SwitchIn`) stay blocking. Give those four `dismiss={{ onEscape: … }}`
  (the wrapper already supports it). Needs Vitest coverage and a `requirements-review` pass on the B-cancel claim.

### 4.6 Dev and test tooling features

- [ ] `ConsoleInput : IBattleInput` — a numbered move menu for terminal play (low priority).
- [ ] *(small)* **Stat stages in the dev enemy overview** — `PlayerOverviewDto` carries none; add them to the DTO (or
  a dev-only extension) and render in `CreatureOverview` `enemy` mode.
- [ ] **Dev Mode — next features (shell, not ready: needs a `/plan` and a full DoR entry).** More dev actions behind
  the same `DevController` gate (404 when the flag is off; `forceDraft` is the existing test-support precedent).
  Candidates, unranked: force the first enemy's species/encounter (removes the seed walks behind the slow
  `voluntary-switch`/`poke-center` specs); set level or force an evolution; force a battle-win drop; grant gold/items;
  skip to node. **Acceptance / design / gen surface / Gen 1 source / data-vs-runtime / quirk / dependencies:** to be
  filled in. Current behaviour → `PRODUCT_SPEC.md` → *Dev Mode*.
- [ ] `data-testid` attributes — deferred; specs lean on stable semantic classes. Add only where a class proves
  brittle.
- [ ] §8 visual-regression canvas snapshots — skipped for maintenance cost.

### 4.7 User documentation

- [ ] `/help` route or modal — starter selection, battle controls, status icons, level picker.
- [ ] Expand `README.md` — architecture decisions (two-DB model, `IBattleRules` pattern, how to add a move effect or a
  generation).
- [ ] Adapt `GEN_DIFFERENCES.md` into a player-facing "what makes Gen 1 different" explainer.

---

## 5. UX, accessibility and robustness (all unverified unless noted)

- **Modal accessibility:** `Modal.tsx:35-44` has `aria-modal` but no focus trap, initial focus or inert background —
  Tab still reaches QUIT (abandons the run, no confirmation); the battle log has no `role="log"`/`aria-live`.
  **Verified in a browser 2026-10-03** (route-choice modal over the pinned map): Tab leaves the modal after the
  offered towns — focus goes to `<body>`, then the MAP toggle, the settings gear, then the pinned map *behind* the
  modal (its close button, then its towns). Not checked: what Enter does on those towns (probably inert).
- **Double-submit creates two runs** (`StarterSelection.tsx:45-72`, `NicknameModal.tsx:32,38`); **`/battle` with no
  saved game shows "Connecting…" forever** (`BattleScreen.tsx:78`, `useBattleHub.ts:52`).
- **Battle log unbounded** (`battleReducer.ts:262`, `BattleScreen.tsx:286`) and the whole `BattleScreen` re-renders on
  every dispatch.
- **Phaser:** a resize mid-attack leaves sprites at the old x (`BattleScene.ts:122-137` vs lunge/shake never
  clearing `*Rested`); `entryComplete` is emitted but unheard, so FIGHT unlocks ≈2.6 s before the sprites arrive.
- **Player species is taken from the starter** (`BattleScreen.tsx:77,237,400`, `activeGame.ts`) — game-over shows the
  starter's sprite with the final creature's name after an evolution or lead change; Continue also shows the
  starter's name/level. Designed in R1d-d (§2).
- **Level-up panel covering the nameplate** is fixed; the underlying pattern (independent absolute positions) is the
  one the UI no-crowding rule warns about.
- **Frontend nits:** `/party` JSON dispatched `as never` (`useBattleHub.ts:116`); an `"unknown"` reward kind renders
  a blank card (`RewardChoiceModal.tsx:32`); `SpeciesCard` is Enter-only (no Space); `TypeBadge` ≈2:1 contrast on
  light types; `SettingsScreen` `nav(-1)`; the title-screen notice reappears on reload; dead code (`PhaserBridge`
  `enterBattle`/`entryComplete`, `AudioEngine.playTick`/`playFaintCry`, two `eslint-disable`s).

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
- [ ] **`GameSessionManager` connection lifecycle** (M) — abandon grace, pending-session eviction TTL and the
  run-loop `Task.Run` are covered by neither suite (entangled with `IHubContext` + `Task.Run` + wall-clock timers;
  needs an injectable clock). Also untested: `SetItemChoice`'s unknown-id fallback (`:457-464`), `DetachConnection`'s
  stale-connection guard (`:679-680`) — reachable without SignalR, and the paths behind §2.
  (`ActiveBattle.ScheduleAbandon`/`OnGraceElapsed`/`CancelAbandon`, `:765-796`, are now covered by `AbandonTimerTests`.)
- [ ] *(small)* **`voluntary-switch.spec.ts` is still slow (~3 min standalone).** Its seed walk forces a themed draft
  via `forceDraft` yet still burns several seeds before reaching a switchable turn; investigate why seeds 1–3 fail
  even with the draft forced (e.g. battle-one losses) or give the walk a faster-fail path.
- [ ] *(low)* **`evolution.spec.ts` doesn't pin the nameplate-follows-evolution fix** — its ALLOW case reads the
  nameplate only after the *next* `BattleStarted`, so it would pass without the fix. Add an assertion right after the
  morph. Not verified by a Playwright run (E2E is user-only).
- **Flake watch:** `endless-chain.spec.ts` *"a run ends when the player faints"* failed once in a full 2026-07-26 run
  (no `Run over` line after 1m10s) but passes in 7.3 s alone — consistent with the documented "a long run
  accumulates abandoned server-side runs" degradation, not a code defect.
- **Test hygiene (unverified):** `shop.spec.ts:22` zero-count assertion can't fail (`startBattle` closes the shop
  first); `encounter-map.spec.ts:94` ignores `playToNextEncounter`'s result; `DevModeTests.cs:124` never exercises
  `MinDamage`; `cadence.spec.ts` can pass trivially; the older `SignalRInputTests` handshake tests have no timeout
  (the newer shop-backlog tests use a 5 s `WaitAsync`); `WebEventContractTests.cs:77` greps all of `timeline.ts` for
  `case 'Name'` (can be fooled by node-kind labels); ~33 `new Battle(...)` calls with no `rng:`; 10 duplicated
  `Fighter(...)` helpers plus a duplicated seeded-start preamble and `fakeLocalStorage`; `MovesFixture.cs:48`'s
  "fresh copy" is a shared cached instance; `TestAltProfile` belongs in `TestSupport/`; two no-assertion "must not
  throw" tests.

---

## 7. Infrastructure, tech debt and hygiene

### 7.1 Infra, CI/deploy and scripts (unverified)

- **CI/deploy hardening:** `fly-deploy.yml` runs no tests before `flyctl deploy`; `setup-flyctl@master` is an
  unpinned moving branch and there is no `permissions:` block; tags can point at any commit. **The container runs as
  root** (`Dockerfile:44-52`; use `USER $APP_UID` after checking SQLite write needs).
- **Dev scripts:** `-StartStack` leaves the backend running (`test.ps1:119/148`, `e2e.ps1:180/357` kill only the
  `dotnet run` parent); `test.ps1 -E2E -StartStack` exits 0 when the backend never starts.
- **Pre-commit hook gaps** (`.githooks/pre-commit`): hardcoded dotnet path (`:11`); checks the working tree, not the
  staged snapshot (`:14,22`); skips deletes/renames and `.csproj`/`Directory.Build.props`/`package.json`/
  `tsconfig.json`/`*.db`-only commits (`:20,33`).
- **Public endpoint exhaustion:** `POST /api/game/start` is unauthenticated with no rate limit, no cap on concurrent
  runs/connections, no idle timeout, no `fly.toml` health check or concurrency block (single 1 GB VM). Unmeasured;
  also no security headers/`UseForwardedHeaders` (low impact: no auth/cookies).
- **Script/Docker polish:** `dev.ps1:53-54` apostrophe-in-path (the `-Command` strings single-quote `$root`/`$dotnet`); `build-release.ps1:200` leaves cwd changed; `Dockerfile:25-28` `COPY . .` re-downloads every sprite on any
  source change and base images are tag- not digest-pinned; `GameController.cs:66` logs only `ex.Message`;
  `SignalRBattleEventEmitter.cs:86` discards the send task; the client-chosen run `Seed` (a product decision, not a
  bug).

### 7.2 Dependencies

`npm audit` 15 (1 critical `vitest` 2.1.0 — dev-only, not exposed by `vitest run`; 6 high incl. `vite`/`esbuild`/
`@playwright/test`; the only one shipped to players is `react-router-dom` 6.30.3, moderate) — `npm audit fix` clears
most. `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 High (GHSA-2m69-gcr7-jv3q) via EF Core Sqlite 9.0.6: low practical risk,
invisible in builds.

### 7.3 Watch items — do not refactor speculatively

- [ ] `AttackAction` still has three large methods (`ResolveDamage` 145 lines, `ExecuteAsync` 140,
  `ResolvePreDamageGates` 112); central and well-tested — revisit only if a change makes it hurt.
- [ ] `Console.WriteLine` is the web layer's whole logging strategy (no `ILogger<T>`); do it as a mechanical pass the
  first time a real Fly.io debugging need bites.
- [ ] `Battle`'s constructor is at 12 parameters; apply the `RunDirectorOptions` precedent (a `BattleOptions` record
  for the optional tail) the next time a feature has to touch the signature anyway.
- [ ] `EncounterFactory`'s profile threading is growing the same way (`BuildCreature` is at 8 parameters;
  `profile` + `allMoves` + `rng` travel together). The next time a feature widens these signatures, consider a small
  run-scoped parameter object that keeps the threading explicit and required.
- [ ] `BiomeDefinition.MapX`/`MapY` are now vestigial (the procedural layout replaced them); removing them means
  updating `BiomeTests.cs`'s pinning test. Low priority.
- [ ] `IslandLayoutGenerator` is fuzz-validated for 2–12 biomes (matched to `RunBiomeMapSize` = 10); it needs
  hardening first if a future roster grows past ~12 or `RunBiomeMapSize` is raised.
- [ ] Dead code: `AttackService.GetRandomAttackAsync`/`GiveDefaultMoveAsync`/`GiveRandomMoveAsync` have no callers
  (and `GetRandomAttackAsync` uses `Skip` without `OrderBy`).

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

### 7.5 Speculative, unverified — check before filing as real

`waitForBridge` resolving early on a slow evolution-sprite load; the timeline queue not cleared on unmount (leftover
steps into a new run); the corner map preview covering the player corner on long node paths; Sludge's poison chance
possibly 40%; Substitute vs sleep/paralysis status moves and Disable's 1–8 turns; untested types `RunLoop`,
`RewardRunEvent`, `AcquisitionResolution`, `CompositeEvaluator`, `MoveEvaluators` (likely covered indirectly — run
coverage).

---

## 8. Open decisions (each needs the user; recommendation first)

| Decision | Recommendation | Unblocks |
|:---------|:---------------|:---------|
| **Second-tab policy** (R1d-c) | Last tab wins + notify the displaced tab; never clear `activeGame` | R1d-c |
| **Transport notices outside the event model** (`SessionTakenOver`, `RunMapSnapshot`, like `RunFaulted`) | Accept | R1d-c, R1d-d |
| **`PlayerSpeciesId` on `BattleStarted`** (a wire-contract change; the C# wire-drop half is auto-guarded, the TS half is manual) | Accept | R1d-d, the post-evolution sprite |
| **Roar/Whirlwind** — end the battle immediately like Gen 1? | Verify at pokered, then fix | §3.1 |
| **When to `/plan` the end-of-turn residual phase** | The cheap fidelity fixes have landed (archived) — ready to `/plan` once greenlit | §4.2 |
| **Per-area encounter floor** — soft floor or accepted tradeoff? | Accept the tradeoff unless playtests show a problem | §4.2 |

---

## 9. Settled — do not re-raise

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
  operation at its boundary (`CLAUDE.md` → *Coding Conventions*).
- **Every run prompt is `'blocking'` by construction** — each parks a server-side await, so dismissing one would
  strand the run; don't re-file "the modals should close on Escape". (The Escape-as-B-cancel item in §4.5 covers only
  the four prompts that have a negative answer.)
- **BST tier bands stay as they are** (user ruling 2026-10-06): the tier multipliers and `K` were tuned on modern
  base stats and are not retuned after the real-Gen-1 import — no measured problem. Reopen only on a playtest report.
- **`RunLoop.cs`'s ~28 types are fine** — a cohesive vocabulary file; don't split it on a type-count metric.
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
