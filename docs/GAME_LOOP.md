# GAME_LOOP.md — the roguelite run structure (draft v0, 2026-06-12)

> **Status: living draft.** Captures the model *as we understand it today* so we stop encoding the run
> structure implicitly across two layers. It defines vocabulary (game loop, event, outcome) and the rule that
> governs them, then maps that onto the current code and the gaps. It is **not** a spec of code that exists —
> §2 is the honest "what's really there," §3–4 are the target the code should converge on.
>
> **Companions:** `STATE_MODEL.md` (the per-battle vs permanent split), `GENERATION_SEAMS.md` (gen-variable
> rules), `DESIGN_GUIDES.md` (Gen 1 mechanics), `AI_CONTEXT.md` (profiles/tooling). This doc is the **run /
> meta** layer; a battle is *one event inside* it.

---

## 1. The model

### 1.1 The Game Loop (the run)
The **game loop** is the spine of a run: an ordered sequence of **events** `e₁ … eₙ` played one at a time, each
to completion, until the run ends (the player creature faints today; other terminators later).

The loop has one defining property:

> **The game loop is governed entirely by game logic we define — never by the player, and it emits no UI of
> its own.** It decides *what happens next* (which event, with what parameters) purely from **run state**
> (depth, wins, the player's condition, the run RNG). The player cannot reorder, skip, trigger, or short-cut
> events. The player only acts *inside* an event; the loop reads the event's **outcome** and advances.

The loop owns *"what's next."* It never owns *"what the player pressed"* — that lives inside an event.

### 1.2 Events
An **event** is one unit the loop runs to completion before advancing. Every event, whatever its kind, obeys
the same external contract:

1. it is handed the **run context** — the player creature, run state (depth/wins), the **event emitter**, the
   **input seam**, and the **run RNG**;
2. it **emits narration / console text** through the emitter (a battle's blow-by-blow; "reached a Poké
   Center!");
3. it **may block for player input** through the input seam (choose a move; HEAL / SKIP);
4. it resolves to a **variable outcome** — a typed result the loop reads to pick the next event and/or mutate
   run state (won/lost; healed/declined; later: caught/fled, bought/skipped, evolved).

### 1.3 Event taxonomy — by *internal* complexity only
Two shapes today; both satisfy §1.2 identically from the outside. The difference is purely internal.

| Kind | Internal shape | Player decisions | Example |
|:-----|:---------------|:-----------------|:--------|
| **Loop-event** | has its own internal loop (repeated rounds, rich branching) | many, across rounds | **Battle** (a turn loop) |
| **Interaction-event** | a single offer → resolve, no internal loop | one (or none) | **Poké Center recovery** |

"Heal is too basic to be a full loop" → it is an **interaction-event**, not a loop-event. Battle is currently
our only loop-event. Both are *events*; the loop treats them the same.

---

## 2. Current reality (2026-06-12) — the honest mapping

What exists today does the right *behaviour* but does not yet model events as first-class, interchangeable
units. This section is deliberately candid so the gap is visible.

| Concept (from §1) | Where it lives today | Honest state |
|:------------------|:---------------------|:-------------|
| **Game loop** | `RunDirector.RunAsync` (core) | **§3 model, shipped (Phase 3a, 2026-06-28).** Battle and recovery are first-class `IRunEvent`s; `chooseNextEvent` is the single logic-driven sequencer. (Was the hardcoded `BattleRunner` `while` body; renamed + restructured behaviour-preservingly.) |
| **Battle (loop-event)** | `Battle.StartFightAsync` (core) | A genuinely well-formed event loop: turn loop, player input via `IBattleInput.ChooseMoveAsync`, lock-in mechanics, end-of-turn resolution, faint → XP → level-up → move-learn, variable outcome (win/lose), emits a rich `BattleEvent` stream. This is the template for "what a loop-event looks like." |
| **Recovery (interaction-event)** | `RecoveryRunEvent` (core) | **Now a first-class event** (Phase 3a): emit `RecoveryOffered` → `await ConfirmRecoveryAsync` → `FullHeal`+`PlayerRecovered` *or* `RecoveryDeclined`, returning a `RecoveryOutcome`. (Was an inline code block in the loop.) |
| **Outcome** | typed `Outcome` records (core) | **Shipped (Phase 3a):** `BattleOutcome(Won)` / `RecoveryOutcome(Healed)`; the director's `Apply` folds them back into `RunState`. (Was implicit — `player.IsAlive()` + an input `bool`.) |

### 2.1 Seams every event already shares
- **Output (one-way, fire-and-forget):** `IBattleEventEmitter.Emit(BattleEvent)` → `SignalRBattleEventEmitter`
  (web) / `ConsoleBattleEventEmitter` (console). A reflection contract test (`WebEventContractTests`) forces
  every event to map.
- **Input (blocking, awaited):** `IBattleInput` — *one* TCS handshake (`SignalRInput`) serves **all** prompts:
  `ChooseMoveAsync`, `ChooseMoveToForgetAsync`, `ConfirmRecoveryAsync`. Each prompt = emit an event + `await`
  a `TaskCompletionSource` a hub method completes; `Cancel()` faults them on disconnect.
- **RNG:** `IRandomSource` (threaded through the engine; **partial** — see §6).
- **Session / transport:** the run is keyed by **`gameId`**; reconnect rebinds the live run to the new
  connection with a 40 s grace (Architecture Review #1). This is the "continue after disconnection" id.

---

## 3. The contract an event should satisfy (target abstraction)

Shipped in Phase 3a (2026-06-28) as `creaturegame/Combat/RunLoop.cs` + `RunDirector.cs` — the loop treats
every event uniformly and new events drop in by branching `chooseNextEvent`, without touching the loop body.

```
RunContext        = { player, runState, emitter, input, rng }
IRunEvent.RunAsync(RunContext) -> Outcome      // emits text, may await input, NEVER decides what's next

Outcome           = a typed result, e.g.
    BattleOutcome   { Won : bool, … }
    RecoveryOutcome { Healed : bool }

GameLoop:                                       // the ONLY place that decides sequence
    while run is alive:
        e        = chooseNextEvent(runState)    // pure game logic: state -> next event
        outcome  = await e.RunAsync(ctx)
        runState = apply(runState, outcome)     // outcome feeds back into run state
    emit RunEnded(summary)
```

- `chooseNextEvent` is **the game logic** — the single home of "battle, then heal every 3rd win, then …". It
  reads only run state + RNG, never player input.
- An event emits and awaits through the *same* seams it does today (`IBattleEventEmitter`, `IBattleInput`);
  interaction-events just use a thin slice of them. No event reaches back into the loop.

---

## 4. The governing rule, stated precisely (player vs. logic)

- The loop is **deterministic given `(runState, rng)`** — same state + same seed ⇒ same event sequence.
- **Player input only changes an event's *outcome*.** The outcome feeds back into run state, which *then*
  influences future `chooseNextEvent` decisions. That is the *only* channel from the player to the sequence,
  and it is indirect.
- Concretely: the player can **SKIP** a heal — but the *offering* of the heal was a logic decision
  (`battlesWon % 3 == 0`), and skipping changes the **outcome** (not healed), never the **sequence**. The next
  event is still chosen by logic.
- The loop **emits nothing the player drives** — no menus, no prompts. All player-facing text/prompts originate
  *inside* events.

---

## 5. Known & near-future events

| Event | Kind | State |
|:------|:-----|:------|
| **Battle** | loop-event | ✅ implemented (`Battle.StartFightAsync`, run via `BattleRunEvent`) |
| **Poké Center recovery** | interaction-event | ✅ implemented (`RecoveryRunEvent`); offer → HEAL/SKIP → heal/decline |
| **Biome route choice** | interaction-event | ✅ implemented (Phase 3b, `BiomeChoiceEvent`) — the player charts the next leg through the biome graph; offer the candidate biomes → `ChooseBiomeAsync` → `BiomeChoiceOutcome` |
| **Elite / boss battle** | loop-event | ✅ implemented (Phase 3c-1) — a `BattleRunEvent` carrying a generation-agnostic `EncounterTier` the web maps to a tougher `IEnemyArchetype`; placed by the biome **node plan** (Boss caps each biome). Not a separate event class — a parameterised battle node |
| **Treasure / Mystery** | interaction-event | ✅ implemented (**Run Economy** → **Reward Choice**, `RewardRunEvent`) — roll a reward via an injected supplier, then offer the player a **pick-one-of-N** (two rarity-rolled items or a larger gold bag): emit `RewardChoiceOffered` → block on `ChooseRewardAsync` → credit the chosen option to the transient `Wallet` / `Bag` → emit `RewardGranted`. Treasure always rewards; Mystery is the wildcard (sometimes nothing). The player takes gold **or** one item, never both. Reward *policy* is web-layer (`RewardCalculator`), like enemy scaling — not a battle seam. **Quick Heal (2026-07-12):** a `HealRewardOption` can replace one item slot in the pick-one-of-N, offered *smart-randomly* — only when the creature has something to restore (hurt/statused/low-PP via a `PlayerCondition` snapshot on `RewardContext`), never on Boss nodes; picking it applies an on-the-spot adaptive heal (`RewardResolution.ApplyHeal`) reusing the item-effect events (`Healed`/`StatusCleared`/`PpRestored`) |
| **Shop** | interaction-event | ✅ implemented (**Run Economy** → **Shop**, `ShopRunEvent`) — roll a run-scaled per-visit stock via an injected supplier (`ShopCalculator` — rarity-derived prices, not the unaffordable Gen 1 `Item.Cost`), emit `ShopOffered`, then run a spend-gold **buy loop**: block on `ChooseShopActionAsync` (buy/leave) → `Wallet.TrySpend` + `Bag.Add` + emit `ShopItemPurchased` per affordable buy → leave to advance. Iterative (buy several, then go), unlike the one-shot reward pick. Pricing *policy* is web-layer, not a battle seam |
| Catch | interaction/loop-event | future — see `TODO.md` Catch Mechanic |
| **Evolution** | interaction-event | ✅ implemented (`BattleRunEvent.TryEvolveAsync` in `RunDirector`) — on a level-up, offer → ALLOW/CANCEL → morph/decline. Data/decision injected via `checkEvolution` (web `EncounterFactory` + `IEvolutionRules`); see archive |
| **Party acquisition** | interaction-event | ✅ implemented (Phase 4 Stage 1c) — the `Party` roster (`creaturegame/Creature/Party.cs`, up to 6, held on `RunState.Party`; `Player` = the lead) and per-biome fought-species tracking exist; the Poké Center heals the whole party; a themed-draft acquisition (`AcquisitionOffered` → `ChooseAcquisitionAsync` → `AcquisitionResolution`) adds a fought-type creature to the roster |
| **Between-biome lead swap** | interaction-event | ✅ implemented (Phase 4 Stage 1d, `LeadChoiceEvent`) — at a biome boundary (after the Poké Center, before the route choice), when `Party.Count > 1`, offer the roster → block on `ChooseLeadAsync` → reassign `Party.Lead` (⇒ `RunState.Player`); emits `LeadChoiceOffered`/`LeadChanged`. Gated one-shot via `RunState.LeadChoicePending`. Status needs no reconciliation under the per-creature carry model (`Creature.CarriedStatus`) |
| **Forced switch-on-faint** | *inside the battle loop-event* | ✅ implemented (Phase 4 Stage 3) — **not a run-loop event**: it lives inside `Battle`, which now takes the `Party` (`playerParty`). When the active creature faints and a bench member is alive, `Battle` blocks on `IBattleInput.ChooseSwitchInAsync` (a forced pick), sends the survivor in against the same enemy (`Party.SetLead` ⇒ `RunState.Player`; reset + own carried status re-applied), emits `SwitchInOffered`/`CreatureSwitchedIn`/`PartyUpdated`, and continues; the run (and the battle) end only when the whole party is down — **except** when a side fled (Roar/Whirlwind) on that same turn, where there is no foe left to send anyone in against: the faint keeps precedence, no switch is offered, and the battle ends as a loss even with a live bench. `BattleRunEvent` re-reads `s.Player` post-battle so win/loss/XP/status act on the finisher. This is the Battle-holds-party groundwork for the still-deferred **voluntary** in-battle switching (a SWITCH turn-action) |

Each future event must obey §3: handed the run context, emits text, may await input, returns a typed outcome,
**never** decides the next event. The implemented set above already spans both kinds and the loop body has not
changed since 3a — new kinds drop in by branching `chooseNextEvent` (today: `RunDirector.EventForNode` over the
biome's seeded `RunNodeKind` plan).

**Encounter-map reveal events (2026-07-10, presentation-only).** Two additive `BattleEvent`s let the client draw
the run as a Slay-the-Spire-style map without changing any sequencing (`docs/TODO.md` → *Encounter Map*):
`RegionMapRevealed` (the whole playable biome graph + neighbour edges, emitted once at run start) and
`BiomeNodePlanRevealed` (the seeded `RunNodeKind` ladder, emitted when a biome is entered). Both are gated to
biome mode. **`RunNodeEntered` semantics widened:** in biome mode it now fires for **every** node *including
`WildBattle`* (previously only Elite/Boss/interaction nodes) so the map has one uniform position signal — the
client filters `WildBattle` out of the text log, so the banner behaviour is unchanged. The legacy endless chain
emits none of the three.

---

## 6. Why formalize now — and the open questions

**The smell that motivated this doc:** the "A new challenger approaches!" line fired before an unresolved Poké
Center because **event sequencing was split** between the engine's emit order (`Battle`) and the *client's*
timeline semantics (`timeline.ts` deciding `BattleEnded` ⇒ "announce next"). Two owners of "what happens when"
⇒ ordering bugs. Modeling events as first-class units with explicit outcomes gives sequencing a single owner
(the loop) and stops the client from inferring run structure.

**Open questions (to resolve as the loop grows):**
1. **Where does the sequencer live? — RESOLVED (Phase 3a, 2026-06-28).** `BattleRunner` graduated into
   `RunDirector`, which holds `chooseNextEvent` and builds its events internally (same public constructor;
   the web layer still supplies the DB-backed enemy/evolution seams). Core stays generation- and
   data-agnostic. New node kinds branch `chooseNextEvent`; the loop body is now fixed.
2. **Interaction-event plumbing — RESOLVED (Phases 3a/3b/3c-1).** The `IRunEvent.RunAsync(RunContext) → Outcome`
   contract *is* the shared minimal plumbing: `RecoveryRunEvent`, `BiomeChoiceEvent`, `RewardRunEvent`
   (treasure/mystery), and `ShopRunEvent` all emit through `RunContext.Emitter` and await through
   `RunContext.PlayerInput` (a thin slice of the same `IBattleInput` the battle uses — `ConfirmRecoveryAsync`,
   `ChooseBiomeAsync`, `ChooseRewardAsync`, `ChooseShopActionAsync`) with **no** `Battle` apparatus. New
   interaction events need only a `BattleEvent` (+ its SignalR/timeline projection) and an `Outcome`.
3. **Event replay on reconnect — NOT A BUG; settled, do not re-open.** The recurring temptation is to flag that
   `SignalRBattleEventEmitter.Emit` drops events while no connection is bound, "so a reconnect desyncs the
   client." It does **not**, and this has been traced more than once — stop re-raising it as egregious. The
   engine emits each turn's events as a synchronous burst and then **blocks on player input**
   (`Battle.StartFightAsync` → `ChooseMoveAsync`). A real disconnect lands during the multi-second animation
   playback — i.e. while the loop is parked on input, emitting nothing — and the client already received that
   burst *before* dropping; the rebind (`GameSessionManager.AttachConnection`) then restores the input channel.
   Nothing is lost on that path. The *only* loss window is a disconnect landing inside the sub-millisecond emit
   burst or the few-ms between-battle DB fetch: a rare stall, never the "client desync" it gets mis-filed as.
   If we ever want belt-and-suspenders, the proportionate fix is a one-shot resync emit on rebind (resend the
   current `TurnStarted` + any pending prompt) — **not** an event buffer, and **not** urgent.
4. **Per-run seed — DONE end-to-end (seam 2026-06-12, web wiring 2026-06-17).** A battle is fully reproducible
   from a seed: `Gen1BattleRules` takes an `IRandomSource`, `DelegatingBattleRules`/`ScriptableRules` forward a
   seed to it, and `BattleScenario.Seed(...)` makes **every** roll deterministic — including the rules'
   previously-global `Roll*` draws (`SeededRulesTests`). Do **not** re-file "Roll* draws ignore the battle seed";
   that edge is closed. The **whole run** is now reproducible too: `GameController.Start` picks one seed per run
   (or accepts `StartGameRequest.Seed`) and threads a single `SeededRandomSource` through enemy construction
   (`EncounterFactory` — incl. creature **construction**'s random DVs via a seeded `Gen1StatCalculator`), the
   battle (`BattleRunner`), and the AI (`Gen1TrainerAi`). Guarded by `RunSeedReproducibilityTests` (`TODO.md`
   Tech Debt #3, closed).
5. **Persistence.** An event boundary is the natural save point — a resumed run re-enters at the next event.

---

## 7. Glossary

- **Game loop / run** — the logic-driven sequence of events that *is* a playthrough.
- **Event** — one unit the loop runs to completion (battle, heal, …); emits text, may await input, returns an
  outcome.
- **Loop-event** — an event with its own internal loop (Battle).
- **Interaction-event** — a single-decision event, no internal loop (recovery).
- **Outcome** — the typed result of an event the loop reads to advance.
- **`chooseNextEvent`** — the pure game-logic function `runState → next event`; the only decider of sequence.
