import { useEffect, useRef, useReducer, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import * as signalR from '@microsoft/signalr';
import { type Payload, expandEvent, useBattleTimeline } from '../battle/timeline';
import { battleReducer, initialState } from './battleReducer';
import { bossTrainerName } from '../battle/bossTrainer';
import { nextPlayerId } from '../battle/playerIdentity';
import { clearActiveGame } from '../utils/activeGame';
import { submitPromptAnswer } from './answerPrompt';
import type { Action } from '../battle/timeline';
import type { PromptKey } from './battleReducer';

// The view-state shape + modal-prompt types live with the reducer now; re-export them so existing
// consumers (BattleScreen) keep importing them from the hook.
export type {
  BattleState,
  LevelUpPanel,
  MoveReplacementPrompt,
  RecoveryPrompt,
  EvolutionPrompt,
  BiomeChoicePrompt,
  RewardChoicePrompt,
  MoveTeachTargetPrompt,
  ShopPrompt,
  AcquisitionPrompt,
  SwitchInPrompt,
  DropToast,
} from './battleReducer';
export type { PartyMember } from '../battle/timeline';

export function useBattleHub(gameId: string | null, initialLevel = 50) {
  const [state, dispatch] = useReducer(battleReducer, { ...initialState, playerLevel: initialLevel });
  const connRef = useRef<signalR.HubConnection | null>(null);
  const nav = useNavigate();

  // The player creature's id drives the player/enemy side split inside expandEvent.
  const playerIdRef = useRef(0);
  // Counts BattleStarted events so expandEvent can tell the first encounter (scene entry animation) from a
  // chained one (slide a new enemy sprite into the running scene).
  const encounterIndexRef = useRef(0);

  // Fresh mirror of the reducer state for the event handler (registered once), so it can read the current
  // biome + node plan at event time to name the gate-boss trainer — the same derivation the map ladder uses,
  // so the ladder label and the battle framing always agree.
  const stateRef = useRef(state);
  stateRef.current = state;
  // True while the just-entered route node is the Boss, so only the boss fight gets the trainer framing.
  // Set on RunNodeEntered (BossBattle → true, any other node → false), read by its following BattleStarted.
  const bossNodeActiveRef = useRef(false);

  // The animation timeline: backend events expand into steps it plays in order.
  const enqueueSteps = useBattleTimeline(dispatch);

  useEffect(() => {
    if (!gameId) return;
    // Guards the connect-failure handler below against React.StrictMode's dev-only double-invoke: it mounts
    // this effect, tears it down, then mounts it again, all before the throwaway first connection's start()
    // has resolved — so that connection's stop()-induced rejection must not fire the resume-failed bounce (it
    // raced the real, second connection that goes on to succeed). Nothing to guard before this diff, since the
    // old handler only logged; only the new nav()-on-failure side effect below needs it.
    let torndown = false;

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/battle?gameId=${gameId}`)
      .withAutomaticReconnect()
      .build();

    conn.on('OnBattleEvent', (eventType: string, payload: Payload) => {
      // Server-side run crash (GameSessionManager's catch-all): the session is gone, so leave like a failed resume.
      if (eventType === 'RunFaulted') {
        clearActiveGame();
        nav('/', { state: { notice: 'The run hit an unexpected server error and ended.' } });
        return;
      }
      // Retarget the player/enemy side split BEFORE the event expands, so the newly-named creature's own
      // moves/damage are sided correctly in the very event that renamed it. The rule itself (which events change
      // "who the player is") lives in the pure helper.
      playerIdRef.current = nextPlayerId(eventType, payload, playerIdRef.current);
      if (eventType === 'BattleStarted') {
        encounterIndexRef.current += 1;
      }
      // Track whether the active node is the Boss (its BattleStarted follows), so the trainer framing is
      // scoped to the boss fight only.
      if (eventType === 'RunNodeEntered') {
        bossNodeActiveRef.current = (payload.kind as string) === 'BossBattle';
      }

      // The current biome's gate-boss trainer name (themed by biome type; stable per visit) — read from the
      // live reducer mirror so it matches the ladder label exactly.
      const s = stateRef.current;
      const primaryType = s.regionBiomes.find(b => b.id === s.currentBiomeId)?.types[0];
      const bossName = bossTrainerName(s.currentBiomeId, primaryType, s.mapNodePlan);

      const { now, steps } = expandEvent(eventType, payload, {
        playerId: playerIdRef.current,
        encounterIndex: encounterIndexRef.current,
        bossTrainerName: bossName,
        isBossBattle: bossNodeActiveRef.current,
        biomeName: s.mapBiomeName,
      });
      now?.forEach(action => dispatch(action));   // control plane — immediate
      if (steps) enqueueSteps(steps);              // animated — sequenced
      // Every rolled reward now blocks server-side on a pick-one-of-N (RewardChoiceOffered → the choice modal
      // → ChooseReward). There is no auto-ack: the player's pick is what releases the run loop.
    });

    // Pull the wallet balance for the gold HUD. Needed on first load and after a reconnect — events don't
    // replay across a disconnect gap (the emitter drops them while offline), so the HUD must re-hydrate from
    // the endpoint. A failure just leaves the HUD at its last value; it's not fatal to the run.
    const hydrateGold = () => {
      fetch(`/api/game/${gameId}/gold`)
        .then(r => (r.ok ? r.json() : null))
        .then((d: { gold: number } | null) => {
          if (d && typeof d.gold === 'number') dispatch({ type: 'SET_GOLD', gold: d.gold });
        })
        .catch(() => { /* keep the current HUD value */ });
    };

    // Pull the party roster for the roster panel. Like the gold HUD, events (PartyUpdated) don't replay across a
    // disconnect gap, so hydrate on first load and after a reconnect. A failure leaves the panel at its last value.
    const hydrateParty = () => {
      fetch(`/api/game/${gameId}/party`)
        .then(r => (r.ok ? r.json() : null))
        .then((members: unknown) => {
          if (Array.isArray(members)) dispatch({ type: 'PARTY_SET', members: members as never });
        })
        .catch(() => { /* keep the current panel value */ });
    };

    // Shared by both failure paths below — see each call site for which case it covers. Guarded by `torndown`
    // (see that flag's own comment) so an intentional teardown (QUIT, battle end, unmount, React StrictMode's
    // dev-only double-invoke) never bounces the player who's already left on purpose.
    const bounceForFailedConnection = (err: unknown) => {
      console.error('[SignalR] Connection failed:', err);
      if (torndown) return;
      // BattleHub now rejects the connection outright when the server no longer knows this gameId (expired
      // resume attempt, or any other connect failure) instead of leaving it silently attached to nothing —
      // see GameSessionManager.AttachConnection / ARCHITECTURE.md §2.7. Bounce to Title with a notice rather
      // than leaving the UI stuck on "Connecting…" forever.
      clearActiveGame();
      nav('/', { state: { notice: "Couldn't connect to the run — it may have expired." } });
    };

    conn.onreconnected(() => { hydrateGold(); hydrateParty(); });
    // A HubException thrown from BattleHub.OnConnectedAsync (the unknown/expired-gameId rejection) closes the
    // connection AFTER the transport handshake completes — so conn.start() below RESOLVES, not rejects, and
    // only onclose ever sees the failure. The .catch() still matters for a failure before any transport
    // connects at all (e.g. the server unreachable); onclose also catches automatic-reconnect finally giving
    // up on a connection that HAD been live. Both funnel through the same guarded bounce — real-world gap
    // found and fixed during this feature's manual verification — ARCHITECTURE.md §2.7.
    conn.onclose(err => bounceForFailedConnection(err));
    conn.start()
      .then(() => { hydrateGold(); hydrateParty(); })
      .catch(bounceForFailedConnection);
    connRef.current = conn;

    return () => {
      torndown = true;
      conn.stop();
      connRef.current = null;
    };
  }, [gameId, enqueueSteps]);

  const chooseMove = useCallback((index: number) => {
    dispatch({ type: 'PLAYER_CHOSE' });
    connRef.current?.invoke('ChooseMove', index).catch(err =>
      console.error('[SignalR] ChooseMove failed:', err));
  }, []);

  // Below: the whole-turn choices (switching/items are turn actions too, like chooseMove) dispatch PLAYER_CHOSE
  // immediately (the turn is locked), then invoke the hub method; a lost turn answer needs no restore because the
  // reconnect replay re-sends the cached TurnStarted. Every blocking-modal answer instead goes through
  // `answerPrompt` below (hide at once, restore on a rejected send). The resulting server events drive the log
  // + sprite + panel refresh. Deviations (shop's iterative non-hide, purely-local dismissals) are called out
  // per callback below.

  const chooseSwitch = useCallback((index: number) => {
    dispatch({ type: 'PLAYER_CHOSE' });
    connRef.current?.invoke('ChooseSwitch', index).catch(err =>
      console.error('[SignalR] ChooseSwitch failed:', err));
  }, []);

  // targetMoveSlot: the move slot (0–3) a single-move PP restore refills. targetPartySlot: the party member
  // Healing/StatusCure/PpRestore/Revive act on (any living member, or a fainted one for Revive). Both null to
  // act on the active creature — always true for a BattleStatBoost item, which has no party-target scope.
  const useItem = useCallback(
    (itemId: number, targetMoveSlot: number | null, targetPartySlot: number | null = null) => {
      dispatch({ type: 'PLAYER_CHOSE' });
      connRef.current?.invoke('UseItem', itemId, targetMoveSlot, targetPartySlot).catch(err =>
        console.error('[SignalR] UseItem failed:', err));
    },
    [],
  );

  // The level-up stat panel stays up until the player does anything; BattleScreen calls this on any
  // action (open FIGHT / CHECK, pick a move, QUIT) to dismiss it.
  const dismissLevelUp = useCallback(() => dispatch({ type: 'HIDE_LEVEL_UP' }), []);

  // Every blocking-prompt answer below goes through this: hide the modal at once, send, and RESTORE the modal if
  // the send is rejected (the reconnect window) — see answerPrompt.ts. `key` names the prompt slot to restore.
  const answerPrompt = useCallback(
    (key: PromptKey, hide: Action, method: string, ...args: unknown[]) =>
      submitPromptAnswer({
        key,
        prompt: stateRef.current[key],
        hide,
        method,
        dispatch,
        invoke: () => connRef.current?.invoke(method, ...args),
      }),
    [],
  );

  const forgetMove = useCallback((slot: number | null) => {
    answerPrompt('moveReplacement', { type: 'HIDE_MOVE_REPLACEMENT' }, 'ForgetMove', slot);
  }, [answerPrompt]);

  const respondEvolution = useCallback((allow: boolean) => {
    answerPrompt('evolution', { type: 'HIDE_EVOLUTION_PROMPT' }, 'RespondEvolution', allow);
  }, [answerPrompt]);

  const respondRecovery = useCallback((accept: boolean) => {
    answerPrompt('recovery', { type: 'HIDE_RECOVERY' }, 'RespondRecovery', accept);
  }, [answerPrompt]);

  const chooseBiome = useCallback((biomeId: string) => {
    answerPrompt('biomeChoice', { type: 'HIDE_BIOME_CHOICE' }, 'ChooseBiome', biomeId);
  }, [answerPrompt]);

  const chooseReward = useCallback((index: number) => {
    answerPrompt('rewardChoice', { type: 'HIDE_REWARD_CHOICE' }, 'ChooseReward', index);
  }, [answerPrompt]);

  const respondMoveTeachTarget = useCallback((slot: number | null) => {
    answerPrompt('moveTeachTarget', { type: 'HIDE_MOVE_TEACH_TARGET' }, 'RespondMoveTeachTarget', slot);
  }, [answerPrompt]);

  // Deviates from the shape above: the shop is iterative, so the modal stays open (do NOT hide it) across buys.
  const buyShopItem = useCallback((index: number) => {
    connRef.current?.invoke('BuyShopItem', index).catch(err =>
      console.error('[SignalR] BuyShopItem failed:', err));
  }, []);

  const leaveShop = useCallback(() => {
    answerPrompt('shop', { type: 'HIDE_SHOP' }, 'LeaveShop');
  }, [answerPrompt]);

  // replaceSlot: the member slot to swap out when accepting with a full party; null otherwise. nickname: the
  // raw text from the acquisition's nickname step (Creature Naming Stage B); null on a decline or a
  // skipped/cancelled step — the server normalizes it, same as the starter path.
  const respondAcquisition = useCallback((accept: boolean, replaceSlot: number | null, nickname: string | null = null) => {
    answerPrompt('acquisition', { type: 'HIDE_ACQUISITION' }, 'RespondAcquisition', accept, replaceSlot, nickname);
  }, [answerPrompt]);

  const chooseLead = useCallback((index: number) => {
    answerPrompt('leadChoice', { type: 'HIDE_LEAD_CHOICE' }, 'ChooseLead', index);
  }, [answerPrompt]);

  const respondSwitchIn = useCallback((index: number) => {
    answerPrompt('switchIn', { type: 'HIDE_SWITCH_IN' }, 'RespondSwitchIn', index);
  }, [answerPrompt]);

  // Purely local (nothing server-side blocks on it) — the view runs a timer and calls this to auto-dismiss the
  // toast after its on-screen beat.
  const dismissDrop = useCallback(() => dispatch({ type: 'HIDE_DROP' }), []);

  return { state, chooseMove, chooseSwitch, useItem, dismissLevelUp, forgetMove, respondRecovery, respondEvolution, chooseBiome, chooseReward, respondMoveTeachTarget, buyShopItem, leaveShop, respondAcquisition, chooseLead, respondSwitchIn, dismissDrop };
}
