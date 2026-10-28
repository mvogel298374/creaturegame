# RECONNECT_RESILIENCE.md — the remaining reconnect work, designed (2026-10-03)

> **Status: R1d-a BUILT (2026-10-26), R1d-d, R1d-e and R1d-f BUILT (2026-10-27); their sections below are the design records;
> R1d-c Ready with a simplified design (user 2026-10-28).** Written from a read of the code on 2026-10-03, not from
> the review sweep's text. The c task entry and
> acceptance conditions live in `TODO.md` → *Reliability — reconnect*; R1d-a's and R1d-d's are in `TODO_ARCHIVE.md` → *§2 R1d-a*, *§2 R1d-d*;
> this is the design behind them. Already fixed and archived: the flee stale-cache, the 60 s grace, the client replay
> dedupe (`TODO_ARCHIVE.md` → *Repo-sweep R1 — reconnect replay mishandled…*), lost modal answers
> (*…lost modal answers…*), the `RunFaulted` notice (*…five small fixes*), and the abandon-timer race (*R1d-b*).
>
> **Read it when:** touching `SignalRBattleEventEmitter`, `GameSessionManager.AttachConnection`/`DetachConnection`,
> `SignalRInput`, or the client's `useBattleHub` replay handling. See also `ARCHITECTURE.md` §2.7 (the session
> lifecycle and its accepted trade-offs) and `GAME_LOOP.md` §5 (the session-layer event category).

## How reconnect works today

`SignalRBattleEventEmitter` caches five events — `RegionMapRevealed`, `BiomeEntered`,
`BiomeNodePlanRevealed`, `BattleStarted`, `TurnStarted` — plus the one open prompt and the route facts behind the
`RunMapSnapshot` notice, and `GameSessionManager.ReEstablishClient` replays them
(after the presentation echo) on **every** (re)connect. While no connection is current, `Send` drops events by
design. The client skips a replay it already holds (`battle/replayDedupe.ts`, arrival-order refs). Reconnect also
calls `CancelAbandon()`, so a reconnected run has no timer left to rescue it; a timer that already fired is
ignored unless it is still the current one (`OnGraceElapsed`).

**Ordering.** (a) first — it was the only item that stranded a run (built 2026-10-26). (d) next
(built 2026-10-27). (c) after the policy question is answered. The cross-cutting test plan at the end covers a, c and d plus the verification gaps.

## R1d-a. A refresh — or a blip — while a blocking prompt is open strands the run

- **What is wrong (verified in code).** Ten prompts park the run on a `SignalRInput` handshake:
  `MoveReplacementRequired`, `RecoveryOffered`, `EvolutionOffered`, `BiomeChoiceOffered`, `RewardChoiceOffered`,
  `MoveTeachTargetRequired`, `ShopOffered` (iterative — `ShopItemPurchased` updates it), `AcquisitionOffered`,
  `LeadChoiceOffered`, `SwitchInOffered`. None is cached. Two ways to lose one: (1) **refresh while it is open** —
  the fresh client never learns of it; (2) **a prompt emitted during an outage** — `Send` drops it, and the replay
  has nothing to resend. (2) is the likelier one. Either way the server stays parked, reconnect cancels the abandon
  timer, and nothing ever unparks it: the player can only QUIT. Supersedes the former Known Gap *"Session Resume doesn't cover a reconnect during a between-node blocking prompt"*.
  Re-verified against the code 2026-10-26: the ten prompts, the emitter's five-event cache and `SignalRInput`'s
  handshakes are as described.
- **Acceptance.** Refresh or blip while any of the ten prompts is open → the modal reappears and is answerable and
  the run continues; an **answered** prompt never reappears; a replayed shop shows the **current** balance.
- **Design — the emitter caches the open prompt (ratified and built 2026-10-26).** Same reasoning as `TurnStarted`: the
  emitter is where each payload is already fully assembled; re-deriving it elsewhere would drift from the real
  projection.
  - `public abstract record PromptEvent : BattleEvent` as a new base for the ten records (engine event model; the
    set of blocking prompts is an event-model fact, not a web one). The emitter keeps one `volatile` `_openPrompt`
    (a run has **at most one** open prompt — the loop is sequential — so a single slot, replaced by the next).
  - **Clear when the run consumes the answer, on the run thread.** `SignalRInput` gains one `Action?
    PromptAnswered` hook; `GameSessionManager` wires it to `emitter.ClearOpenPrompt()` when it builds the battle.
    Each `Choose*Async`/`Confirm*Async` invokes it right after `await tcs.Task` returns, where it already nulls its
    TCS — **not** from the hub-thread `Set*` methods. The TCSs use `RunContinuationsAsynchronously`, so the run's
    continuation can emit the *next* prompt before a hub-thread clear runs, and that clear would wipe it: a
    reward pick goes straight to `MoveTeachTargetRequired` (`RewardResolution`), an evolution to
    `MoveReplacementRequired`. Clearing on the run thread orders the clear before the next emit. A cancelled
    handshake throws past the clear; the run is being abandoned, so nothing reads the slot again.
  - **`RunEnded` also clears it**, as a cheap backstop. `CreatureFled` does not: with the run-thread clear the slot
    is always empty before any later event, so a flee has nothing to clear.
  - **Replay order:** the existing cached events first, `_openPrompt` last (prompts overlay the field).
  - **Shop:** the cached `ShopOffered` is rewritten on each `ShopItemPurchased` (`Balance` = the event's balance),
    so a replay shows the real balance; stock does not change on a buy. Cleared on LEAVE like any answer, from
    `SignalRInput.CloseShopIfLeaving` (it marks the end of a shop and runs on the run thread on both the TCS and
    the backlog path), not on BUY.
  - **Client:** nothing new — a replayed prompt event goes through the normal `expandEvent` → `SHOW_*` path, and
    every modal renders from its own state slot in any phase, `connecting` included (`BattleScreen.tsx`).
    `replayDedupe` must keep **not** skipping prompts (it only knows BattleStarted / BiomeEntered / plan). After a
    blip the client still holds the modal; the replayed `SHOW_*` re-sets the same slot (a shop's balance with the
    current one), so it is idempotent.
  - **Accepted race, documented not closed:** a reconnect replay can read `_openPrompt` in the instant between an
    answer completing the handshake and the run's continuation clearing it → a modal for an already-answered
    prompt. Answering it hits "no handshake pending" and is dropped — the same accepted shape as
    *rejected-after-sent* (`ARCHITECTURE.md` §2.7). A prompt-id protocol would close it; still judged not worth its
    blast radius.
  - **Accepted cosmetic gap (2026-10-26):** a prompt raised mid-battle — `MoveReplacementRequired` after a KO's
    level-up, `SwitchInOffered` after a faint — is emitted before `BattleEnded`, so a refresh replays the battle's
    last `TurnStarted` behind the modal (pre-faint HP) until the answer moves the battle on. The prompt itself
    works. R1d-d closes the visible half — a fainted creature's cached HP is zeroed (see R1d-d); the rest of that
    last turn's HP changes stay accepted.
  - **Rejected alternatives.** *Re-derive from `SignalRInput`'s open context* (the TCS owner knows which prompt is
    open): the contexts are not the events (`ShopOffered` is built from the stock + a wallet read), so a second
    projection would drift. *Clear from the hub-thread `Set*` methods* (the 2026-10-03 draft): races the run's next
    emit and can wipe a follow-on prompt, re-creating the strand (above). *The `save.db`-backed resume (Tier 3):*
    the right long-term home, far too heavy for this.
- **DoR.** Gen-variable surface: **none** — event/transport layer; the event model is on the gen-invariant list
  (`GENERATION_PROFILE.md` §2.2). Gen 1 source of truth: n/a (not a game rule). Data-vs-runtime: runtime only — a
  base record in the engine's event model plus the web layer; no importer or DB change. **Quirk the tests must
  assert:** a prompt replays exactly while it is open, never after the run has consumed its answer, and a shop
  replays the *current* balance; an answer with no handshake waiting leaves the cached prompt in place (the clear
  belongs to the run thread, not the hub). Dependencies: none.
- **Tests for this item.** C# unit tests per the test plan below (the R1d-a half), on the
  `GameSessionLifecycleTests` fake-clock harness where a session is needed. Browser: one refresh scenario in a new
  `reconnect.spec.ts` (`page.reload()`, no hub method needed) — a reward choice open, then a second refresh after
  it is picked shows no prompt — run only on the user's request. **Shop scenario waived (user, 2026-10-26):** a
  shop with gold in hand is a biome deep (`shop.spec.ts`), so a browser walk to it would be long and flaky; the
  replayed balance is pinned by the emitter test instead. The blip scenario and its `DropConnection` hub method
  move to R1d-c/d.
- **Falsifiable guard (so an 11th prompt can't be forgotten).** A reflection test: every private `volatile
  TaskCompletionSource<…>` field on `SignalRInput` other than the turn handshake has a `PromptEvent` subclass, and
  the emitter's replay includes each — adding a blocking prompt without a cached event fails it.

## R1d-c. A second tab on the same `gameId` silently takes over — design simplified 2026-10-28 (below)

> **Superseded in part (user 2026-10-28):** the server half below stands; the "go to Title with a notice" client
> half and the `transportNotices.ts` extraction are replaced by a full-screen, no-action panel in the old window
> (no navigation, no `clearActiveGame()`). The ready entry is in `TODO.md` §2.

- **What is wrong (verified).** `AttachConnection`'s reconnect branch rebinds `CurrentConnectionId` to whoever
  connected last. The old tab keeps its socket, receives nothing, and its invocations are dropped (routing is by
  connection id). It shows a frozen run with no explanation.
- **Policy options.** (1) *First tab wins, refuse the second.* **Rejected** — a refresh makes a new connection while
  the old socket may not have been detected as closed yet, so this would lock a player out of their own refresh.
  (2) *Last tab wins, tell the displaced one* — **recommended.** (3) *Last wins, silently* — today's behaviour.
- **Design (2).** In `AttachConnection`'s reconnect branch, if `previous` is non-empty and differs from the new id,
  send the **previous** connection a transport-level `SessionTakenOver` notice (same bypass-the-event-model shape as
  `RunFaulted`: never cached, never replayed) before swapping. A refreshed page's ghost connection receives it
  harmlessly.
  **Client (as ruled 2026-10-28):** the old window stops its connection (marked torn down) and shows a full-screen
  "open in another window" panel with no actions (reducer flag `sessionTakenOver`). No navigation and no
  `clearActiveGame()`: the saved game record lives in `localStorage`, which the *surviving* window shares.
- **Acceptance.** Opening the run in a second window → the first shows the panel and can't be played; the second
  continues; `activeGame` in `localStorage` is untouched; a plain refresh of a single window never shows the panel.
- **DoR.** Ready (user 2026-10-28; last-window-wins confirmed). Gen-variable: none. Runtime/web + a small client
  change. Dependencies: none (independent of a/b/d).

## R1d-d. A refresh loses the route trail, the node pin, boss framing and the player's sprite — BUILT (2026-10-27)

The sections below are the design record; the as-built list is `TODO_ARCHIVE.md` → *§2 R1d-d*.

- **What is wrong (verified 2026-10-26).** After a refresh: `routePath` holds only the *current* biome (only the
  latest `BiomeEntered` is cached); the ladder pin is −1 mid-biome (`RunNodeEntered` is not cached, and the replayed
  plan resets it); a mid-boss-fight refresh loses the trainer framing — the "Trainer X wants to battle!" / "VS
  Trainer X's …" log lines, because `bossNodeActiveRef` is set from `RunNodeEntered` and read when the replayed
  `BattleStarted` expands (`useBattleHub.ts`); and the player's sprite is the **starter** (`BattleCanvas`'s mount prop
  and the game-over card read the nav-state species, `BattleScreen.tsx` — `BattleStarted` carries no player species
  id). Found in this pass, an R1d-a side effect: `RecoveryOffered` advances the pin with a relative `+1` onto the
  Poké Center cap (`timeline.ts`), so each replay of an open recovery prompt moves the pin one more step.
- **Acceptance.** Refresh at any point in a run → the region map shows the full travelled trail and the correct pin
  (Rest cap included), a boss fight keeps its trainer framing, and the on-field player sprite and the game-over card
  are the *current* lead's. A blip changes none of these. A refresh or blip with the Poké Center prompt open leaves
  the pin on the Rest cap. A refresh behind a mid-battle prompt shows a fainted creature at 0 HP.
- **Design (ratified 2026-10-26, built 2026-10-27) — a snapshot, not a history replay.** Replaying every past `BiomeEntered` would be
  *wrong*: after a *blip* `replayDedupe` accepts the older biomes (they differ from the last arrival) and corrupts
  `routePath`. Instead the emitter (which already sees every `BiomeEntered` / `RunNodeEntered` / `RecoveryOffered`,
  so the map half needs **no engine change**) keeps the route and per-biome node facts and replays one **absolute**
  transport notice, `RunMapSnapshot { routePath, nodesEntered, restReached, currentNodeKind }`. Like `RunFaulted`
  it is outside the event model: never an engine event. Sent only once a biome has been entered (the legacy chain
  emits no map events).
  - **Raw facts, not a pin.** The server sends what happened in the current biome (`nodesEntered` = its
    `RunNodeEntered` count, `restReached` = a `RecoveryOffered` was emitted in it); the client derives the pin
    (`restReached` → the Rest cap, else `nodesEntered − 1`). The server never copies the reducer's pin rule.
  - **Replay order: map, biome, plan, *snapshot*, battle, turn, prompt.** After the plan, so its step lands after
    the replayed `MAP_BIOME_ENTERED` / `MAP_PLAN_REVEALED` steps and overrides them; **before** `BattleStarted`,
    because the hook sets `bossNodeActiveRef` from `currentNodeKind` on the snapshot's *arrival*, and the replayed
    `BattleStarted` reads that ref when it expands. (The 2026-10-03 draft sent it last, which is too late for the
    boss framing.)
  - **Timing hazard.** The client queues animated events behind its timeline (`ARCHITECTURE.md` §2.8), so state lags
    arrival. The snapshot's map update travels as an **ordered step** through the same queue — *not* an immediate
    `now` action — or a still-queued replayed `MAP_BIOME_ENTERED` would append the current biome on top of it. Only
    the boss ref is set on arrival. Applying the step twice gives the same state (absolute), so refresh and blip
    are alike.
  - **Rest-cap pin made absolute.** `RecoveryOffered`'s step changes from `MAP_NODE_ENTERED` (`+1`) to setting the
    pin to the Rest cap (the plan length), so a replayed recovery prompt — refresh or blip — can't move it twice.
  - **Stale HP behind a mid-battle prompt (from R1d-a).** When the emitter sees `CreatureFainted`, it rewrites the
    cached `TurnStarted` with that creature's HP at 0 (matched by id, player or enemy side). Any other HP change in
    that last turn stays accepted.
  - **The lead after an in-battle switch (requirements-review, 2026-10-27).** `CreatureSwitchedIn` (player-only)
    rewrites the cached `BattleStarted`'s player name, id and species, so a refresh after a forced or voluntary
    switch replays the creature on the field, not the battle's first lead. The cached `TurnStarted` is left alone:
    the run emits the next one at once, so a stale old-lead turn is replayable only for that instant — accepted.
  - **Boss framing after a refresh (user, 2026-10-27).** A refresh re-enters the fight, so the replayed boss
    `BattleStarted` takes the first-encounter branch: the "VS Trainer X's …" line, not "Trainer X wants to
    battle!" — the same as a non-boss refresh skipping "A new challenger approaches!". The trainer name comes from
    the boss inputs in **arrival order** (`nextBossArrival` / `arrivalBossName`, `bossTrainer.ts`), not from reducer
    state, which lags the queue mid-replay; so the VS line always matches the ladder. The hook's per-event
    bookkeeping (dedupe, player id, encounter count, boss flag and inputs, `expandEvent`) is one pure function,
    `battle/eventIntake.ts`, which `reconnectReplay.test.ts` drives through whole replays.
  - **Sprite — a real field, not the snapshot.** `PlayerSpeciesId` becomes a **required** field on `BattleStarted`
    (engine event + the `MapEvent` projection + the TS payload type; the C# wire-drop half is auto-guarded, the TS
    leg and render layer are manual — `ClaudeDirectives/engine.md` rule 8). A reducer `playerSpeciesId` is set by
    `BATTLE_STARTED`, `SWITCHED_IN`, `LEAD_CHANGED` and `CREATURE_RENAMED` (evolution); those actions gain the species
    id their events already carry; `CREATURE_RENAMED` changes it only when the evolved creature is the on-field one
    (evolution is party-wide), while `LEAD_CHANGED` always names the new lead. Transform is **not** tracked —
    it is temporary and the scene already restores the true species. Consumers: `BattleCanvas`'s mount prop (it
    builds its scene once, on the first `BATTLE_STARTED`, so a later change can't rebuild it mid-battle; live swaps
    stay on the bridge commands) and the game-over card. The nav-state starter remains the fallback before the
    first battle.
  - **Out of scope:** the Title screen's Continue card reads a saved `Species` object from `localStorage`, so
    fixing it needs a persisted-shape change; it keeps its §5 entry. Duplicate prompt log lines after a blip
    (R1d-a's replay re-expands the recovery / move-replacement / evolution intros) were fixed as R1d-e (below).
- **DoR.** Gen-variable: none (species identity, route state — gen-invariant). Gen 1 source: n/a. Data vs runtime:
  runtime — one engine event field (`BattleStarted.PlayerSpeciesId`), the web emitter and a transport notice, and
  the client hook / timeline / reducer; no importer or DB change. **Quirk to test:** the snapshot is absolute — a
  blip leaves `routePath` and the pin unchanged (not duplicated, not truncated), a refresh restores the full trail;
  the snapshot is replayed after the plan and before `BattleStarted`; a replayed recovery prompt leaves the pin on
  the Rest cap; the cached `TurnStarted` shows a fainted creature at 0 HP. Dependencies: R1d-a (built).
- **Tests for this item.** C#: snapshot contents and replay position, the legacy chain sends none, the faint HP
  rewrite, `PlayerSpeciesId` projected. Vitest: the snapshot step is absolute and idempotent and lands after a
  replayed `MAP_BIOME_ENTERED`; the Rest-cap pin is idempotent; `playerSpeciesId` follows each of its four actions.
  Browser: one refresh scenario in `reconnect.spec.ts` mid-first-biome after two nodes, checking the ladder pin, run
  on request. The blip scenario and its `DropConnection` hub method are their own tooling entry (they serve R1d-c
  too); a second-biome trail check in E2E is a biome-deep walk and is left to the C# and Vitest tests.

## R1d-e. A blip re-logs an open prompt's intro — BUILT (2026-10-27)

- **What was wrong.** R1d-a replays the open prompt on every reconnect. After a blip the client still holds the
  modal, and `RecoveryOffered`, `MoveReplacementRequired` and `EvolutionOffered` expand into log lines before their
  idempotent `SHOW_*`, so each blip wrote the intro again. The other prompts raise their modal with no log line.
- **Design.** `replayDedupe.ts` gains an arrival-order `openPrompt` tracker, set when one of those three events is
  accepted. A replayed event of the same type while it is set is skipped. `useBattleHub`'s `answerPrompt` clears it
  at answer time, so a replay after the answer (R1d-a's accepted race) shows the modal again. When the answer's send is
  rejected and `submitPromptAnswer` restores the modal, its `onRestore` callback sets the tracker again: the prompt
  is open, and the reconnect that follows must not re-log it (the re-arm was added in implementation; the user
  ratified it 2026-10-27). Both live in `submitTrackedPromptAnswer` (`answerPrompt.ts`), which the hook calls. Tracker,
  not reducer state, for the same reason as the biome trackers: the `SHOW_*` is queued behind the intro's waits, so
  state lags arrival.
- **Invariant: at most one logged prompt is open, so the event type identifies it.** The skip compares the event type,
  not the payload. That holds because the server parks the run on each prompt (`SignalRInput`), so the next prompt of
  the same type only follows an answer, and the answer clears the tracker. A future flow that opens two same-type
  prompts at once (a batched evolution, say) breaks this and must key the tracker on a payload identity instead
  (user ruling 2026-10-27: document, don't key on payload now).
- **Rejected.** *Skip whenever the reducer slot is set:* misses a replay landing during the intro animation, before
  `SHOW_*` applies. *Clear only once the send resolves:* would skip a replay that lands before the resolve, but the
  confirmed acceptance has a post-answer replay show the prompt (the race stays as R1d-a documents it).
- **Tests.** Vitest: the three prompts are skipped only while open, cleared by their own answer and re-set by their
  own restore (`replayDedupe.test.ts`); through the real intake, reducer and the hook's answer path
  (`submitTrackedPromptAnswer` on each prompt's `PROMPT_ANSWERS` route), a blip logs each intro once with the modal
  up, a refresh shows it, a post-answer replay shows it, and a rejected answer then a replay logs it once
  (`reconnectReplay.test.ts`); `onRestore` fires only on a restore (`answerPrompt.test.ts`). The browser blip check
  is R1d-f's.

## R1d-f. Blip test tooling — BUILT (2026-10-27)

- **What was missing.** Replay-after-blip had never been checked in a browser: a refresh is `page.reload()`, but
  nothing could drop a live connection on cue, and `context.setOffline` waits out SignalR's 30 s client timeout.
- **Design.** `BattleHub.DropConnection` cuts the transport with `Context.GetHttpContext()?.Abort()`; with Dev Mode
  off it throws a `HubException` ("Method does not exist."), the hub's equivalent of `DevController`'s 404.
  `useBattleHub` exposes `window.__cgHub` only when `E2E` is true: `drop()` invokes the method (its rejection, from
  the abort itself, is swallowed), `reconnects` counts `onreconnected`, and `events` records every received event
  type. The effect's cleanup removes it, so a torn-down connection is never driven.
- **Why not `Context.Abort()`** (the confirmed draft; switched by user ruling 2026-10-27 after the first E2E run).
  Since .NET 5 a server-initiated hub abort sends a close message with reconnecting forbidden (`HubConnectionContext`
  keeps an `_allowReconnect` flag, cleared by `Abort()`), and the JS client obeys: no reconnect, `onclose` fires,
  and `useBattleHub` bounces to Title ("Couldn't connect to the run"). Both blip scenarios failed that way, with
  `reconnects` at 0. Aborting the HTTP context drops the WebSocket with no close message, which the client treats as
  a lost connection, exactly what a network blip is.
- **How the spec knows the replay landed.** A reconnect fires `onreconnected` before the server's
  `OnConnectedAsync` replay arrives, so the spec waits for the replay's last event in `events` (`TurnStarted`
  mid-battle, the open prompt otherwise; `ReplayLastKnownState` sends them in that order). Arrival is not
  application: replayed steps still play through the animation queue. So the battle scenario plays a move and
  checks only once that move's "used" line is drawn and the menu is back: every replayed step queues ahead of the
  turn's, so a re-announced battle (a VS line) or biome ("Entered …") would sit before it, and the replayed
  `RunMapSnapshot` has applied before the pin is read. A faint in that turn would move the pin legitimately, so the
  spec asserts none happened (a loud failure if seed 1 ever changes outcome, never a skipped check). Reading the pin
  right after arrival was rejected by requirements-review: it could pass before the snapshot applied (user ruling
  2026-10-28).
- **Seam scope** (ratified by the user 2026-10-28): wider than the confirmed `dropConnection`, because
  `onreconnected` fires before the replay arrives and the spec must see the replay land without sleeping. Its
  `events` list is unbounded; that is fine in an E2E session only.
- **Scenarios** (`reconnect.spec.ts`, opt-in): a blip mid-battle → `BattleStarted` *was* replayed (so the pass
  proves the client's skip, not a silent server), no new VS or "Entered" line before the next move, the move
  resolves, the pin unchanged; a blip with the reward choice open → no `BattleStarted` in the replay, the
  modal stays and is answerable, the run continues. R1d-e's "intro logged once" is pinned in Vitest only.
  *Waived (user 2026-10-28):* a client VS-line check and a pin check in the reward scenario. The client logs a VS
  line only for a replayed `BattleStarted`, which the spec already proves was not sent; the pin bullet is covered by
  the mid-battle scenario, and a sound pin read here would have no move to sync on.
- **Not covered:** the 60 s grace (an immediate retry never nears it; user ruling 2026-10-27).
- **Tests.** C#: `DevModeTests.DropConnectionCutsTheTransportOnlyUnderDevMode_NeverTheReconnectForbiddingHubAbort`
  (Dev Mode off → throws, nothing aborted; on → the transport is aborted, the hub-level `Abort()` never called).
  Browser: the two scenarios above, run on request.

## Test plan (covers a, c, d and the open verification gaps)

- **Unit (C#, `SessionResumeTests` style):** open prompt cached/replayed last and in order; replaced by the next
  prompt; cleared via `PromptAnswered` (after the run consumes the answer) and `RunEnded`; a stray answer with no
  handshake waiting leaves it cached; shop balance follows `ShopItemPurchased`; the reflection guard above; `AttachConnection` twice → `SessionTakenOver` to the previous
  connection and the replay to the new; snapshot contents after N biomes/nodes and its position (after the plan,
  before `BattleStarted`); the faint HP rewrite of the cached `TurnStarted`.
- **Unit (Vitest):** the `sessionTakenOver` reducer flag; `expandEvent` for a
  replayed prompt; the snapshot step applies **after** a replayed `BiomeEntered` (the timing hazard) and is
  idempotent; the Rest-cap pin is idempotent; `playerSpeciesId` reducer transitions.
- **Browser/E2E (new `reconnect.spec.ts`, opt-in as always):** (1) **refresh with a reward choice open** → the modal
  reappears, pick → run continues, and a later refresh shows no prompt — ships with R1d-a (the shop-with-a-purchase
  variant was waived, see R1d-a); (2)
  **blip** mid-battle (ships with R1d-f, with `DropConnection`) →
  no second "new challenger", move menu live; (3) **second tab** → first tab shows the notice, `activeGame` kept;
  (4) refresh mid-first-biome after two nodes → the ladder pin is intact (ships with R1d-d; a second-biome trail is
  left to C#/Vitest, a biome-deep walk). A deterministic blip needs a **Dev-Mode-gated hub method**
  `DropConnection` that cuts the transport (`HttpContext.Abort()`, not `Context.Abort()` — see R1d-f) so the client
  sees a dropped socket and its automatic reconnect runs — `context.setOffline` would wait out SignalR's 30 s client
  timeout. Gate it exactly like `forceDraft`: the
  client can ask, only a Dev-Mode server grants (`ARCHITECTURE.md` §2.7).
- **Closes** the two verification gaps (no browser check of replay-after-blip; the hook's receive side untested in a
  browser). It does **not** measure the 60 s grace: the cut transport is answered by SignalR's immediate (0 s) retry,
  so measuring the grace needs the reconnects blocked for ≈42 s, a separate spec (user ruling 2026-10-27, R1d-f).
