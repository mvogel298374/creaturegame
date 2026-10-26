# RECONNECT_RESILIENCE.md — the remaining reconnect work, designed (2026-10-03)

> **Status: R1d-a BUILT (2026-10-26; its section below is the design record); R1d-c and R1d-d
> provisional-pending-`/plan`.** Written from a read of the code on 2026-10-03, not from the review sweep's text.
> The frontend halves of c and d are provisional until their `/plan` pass (DoR #2). The c and d task entries and
> acceptance conditions live in `TODO.md` → *Reliability — reconnect*; R1d-a's are in `TODO_ARCHIVE.md` → *§2 R1d-a*;
> this is the design behind them. Already fixed and archived: the flee stale-cache, the 60 s grace, the client replay
> dedupe (`TODO_ARCHIVE.md` → *Repo-sweep R1 — reconnect replay mishandled…*), lost modal answers
> (*…lost modal answers…*), the `RunFaulted` notice (*…five small fixes*), and the abandon-timer race (*R1d-b*).
>
> **Read it when:** touching `SignalRBattleEventEmitter`, `GameSessionManager.AttachConnection`/`DetachConnection`,
> `SignalRInput`, or the client's `useBattleHub` replay handling. See also `ARCHITECTURE.md` §2.7 (the session
> lifecycle and its accepted trade-offs) and `GAME_LOOP.md` §5 (the session-layer event category).

## How reconnect works today

`SignalRBattleEventEmitter` caches exactly five events — `RegionMapRevealed`, `BiomeEntered`,
`BiomeNodePlanRevealed`, `BattleStarted`, `TurnStarted` — and `GameSessionManager.ReEstablishClient` replays them
(after the presentation echo) on **every** (re)connect. While no connection is current, `Send` drops events by
design. The client skips a replay it already holds (`battle/replayDedupe.ts`, arrival-order refs). Reconnect also
calls `CancelAbandon()`, so a reconnected run has no timer left to rescue it; a timer that already fired is
ignored unless it is still the current one (`OnGraceElapsed`).

**Ordering.** (a) first — it was the only item that stranded a run (built 2026-10-26). (d) next
(it shares (a)'s replay touch-points, and the sprite fix shares a `BattleStarted` change). (c) after the policy
question is answered. The cross-cutting test plan at the end covers a, c and d plus the verification gaps.

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
    works. The real fix is R1d-d's absolute state snapshot, not this item.
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

## R1d-c. A second tab on the same `gameId` silently takes over — **needs a policy decision**

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
  **Client (provisional-pending-`/plan`):** go to Title with a notice ("This run was opened in another tab") and
  — **unlike `RunFaulted` — do NOT `clearActiveGame()`**: the saved game record lives in `localStorage`, which the
  *surviving* tab shares, so clearing it would delete that tab's resume point. Extract the notice handling into a
  pure `battle/transportNotices.ts` (`'RunFaulted' → { clearActiveGame: true, … }`, `'SessionTakenOver' → {
  clearActiveGame: false, … }`) so the distinction is unit-pinned; this also closes the `RunFaulted` handler test
  gap in `TODO.md`.
- **Acceptance.** Opening the run in a second tab → the first tab shows the notice and returns to Title; the second
  continues; `activeGame` in `localStorage` is untouched; a plain refresh of a single tab never shows the notice.
- **DoR.** Gen-variable: none. Runtime/web + a small client change. **Open question for the user:** is last-tab-wins
  the intended policy? Dependencies: none (independent of a/b/d).

## R1d-d. A refresh loses the route trail, the node pin, boss framing and the player's sprite

- **What is wrong (verified).** After a refresh: `routePath` holds only the *current* biome (only the latest
  `BiomeEntered` is cached); the node pin is −1 mid-biome (`RunNodeEntered` is not cached, and the replayed plan
  resets it); a mid-boss-fight refresh loses the trainer framing (`bossNodeActiveRef` is derived from
  `RunNodeEntered`); and the player's sprite is the **starter** (`BattleScreen.tsx:77,237,400` read the nav-state
  species — `playerSpecies?.id ?? 1` — because `BattleStarted` carries no player species id). The same species
  source is why the game-over card / Continue show the starter after an evolution or lead change (also in R2).
- **Acceptance.** Refresh at any point in a run → the region map shows the full travelled trail and the correct pin,
  the boss fight keeps its framing, and the on-field player sprite/nameplate are the *current* lead's.
- **Design — a snapshot, not a history replay.** Replaying every past `BiomeEntered` would be *wrong*: after a
  *blip* `replayDedupe` accepts the older biomes (they differ from the last arrival) and corrupts `routePath`.
  Instead the emitter (which already sees every `BiomeEntered` / `RunNodeEntered`, so **no engine change**) keeps
  `_routePath` and a per-biome node counter, and `ReplayLastKnownState` finishes with one **absolute** transport
  notice `RunMapSnapshot { routePath, nodesEntered, currentNodeKind }`. Idempotent for refresh and blip alike.
  - **Timing hazard (the reason this is a design item, not a one-liner).** The client queues animated events behind
    its timeline (`ARCHITECTURE.md` §2.8), so state lags arrival. The snapshot must travel as an **ordered step**
    through the same queue — *not* as an immediate `now` action — or the still-queued replayed `MAP_BIOME_ENTERED`
    would append the current biome on top of it afterwards. It is sent **last**, so it overrides whatever the
    replayed events produced. The hook also sets `bossNodeActiveRef` from `currentNodeKind`.
  - **Sprite — a real field, not the snapshot.** Add `PlayerSpeciesId` to `BattleStarted` (engine event + the
    `MapEvent` projection + the TS payload type; the C# wire-drop half is auto-guarded, the TS leg and render layer
    are manual — memory *web event field projection gap*), and keep a reducer `playerSpeciesId` set by
    `BATTLE_STARTED`, `SWITCHED_IN`, `LEAD_CHANGED` and evolution (those events already carry a species id). The
    view reads `state.playerSpeciesId`, falling back to the nav-state starter only before the first battle. This
    fixes the refresh case **and** the live post-evolution / post-lead-change cases in one move, and also covers
    the R2 "Player species always taken from the starter" item.
- **DoR.** Gen-variable: none (species identity, route state — gen-invariant). Runtime/web + a client change.
  **Quirk to test:** a blip after three biomes leaves `routePath` unchanged (not duplicated, not truncated); a
  refresh restores all three; the pin matches; a refresh mid-boss keeps the framing. Dependencies: none, but it is
  sequenced after (a) because both extend `ReplayLastKnownState` and its tests.

## Test plan (covers a, c, d and the open verification gaps)

- **Unit (C#, `SessionResumeTests` style):** open prompt cached/replayed last and in order; replaced by the next
  prompt; cleared via `PromptAnswered` (after the run consumes the answer) and `RunEnded`; a stray answer with no
  handshake waiting leaves it cached; shop balance follows `ShopItemPurchased`; the reflection guard above; `AttachConnection` twice → `SessionTakenOver` to the previous
  connection and the replay to the new; snapshot contents after N biomes/nodes.
- **Unit (Vitest):** `transportNotices` mapping (incl. the `clearActiveGame: false` case); `expandEvent` for a
  replayed prompt; the snapshot step applies **after** a replayed `BiomeEntered` (the timing hazard) and is
  idempotent; `playerSpeciesId` reducer transitions.
- **Browser/E2E (new `reconnect.spec.ts`, opt-in as always):** (1) **refresh with a reward choice open** → the modal
  reappears, pick → run continues, and a later refresh shows no prompt — ships with R1d-a (the shop-with-a-purchase
  variant was waived, see R1d-a); (2)
  **blip** mid-battle (ships with R1d-c/d, with `DropConnection`) →
  no second "new challenger", move menu live; (3) **second tab** → first tab shows the notice, `activeGame` kept;
  (4) refresh after two biomes → trail + pin intact. A deterministic blip needs a **Dev-Mode-gated hub method**
  `DropConnection` that calls `Context.Abort()` (the client sees an abnormal close and its automatic reconnect runs
  — `context.setOffline` would wait out SignalR's 30 s client timeout). Gate it exactly like `forceDraft`: the
  client can ask, only a Dev-Mode server grants (`ARCHITECTURE.md` §2.7).
- **Closes** the two verification gaps (no browser check of replay-after-blip; `useBattleHub` wiring untested) and the 60 s-grace measurement (the `DropConnection` blip can be timed against the real schedule).
