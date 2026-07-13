import { useEffect, useRef, useReducer, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { type Payload, expandEvent, useBattleTimeline } from '../battle/timeline';
import { battleReducer, initialState } from './battleReducer';

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
  ShopPrompt,
  AcquisitionPrompt,
  DropToast,
} from './battleReducer';
export type { PartyMember } from '../battle/timeline';

export function useBattleHub(gameId: string | null, initialLevel = 50) {
  const [state, dispatch] = useReducer(battleReducer, { ...initialState, playerLevel: initialLevel });
  const connRef = useRef<signalR.HubConnection | null>(null);

  // Player name drives the player/enemy side split inside expandEvent.
  const playerNameRef = useRef('');
  // Counts BattleStarted events so expandEvent can tell the first encounter (scene entry animation) from a
  // chained one (slide a new enemy sprite into the running scene).
  const encounterIndexRef = useRef(0);

  // The animation timeline: backend events expand into steps it plays in order.
  const enqueueSteps = useBattleTimeline(dispatch);

  useEffect(() => {
    if (!gameId) return;

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/battle?gameId=${gameId}`)
      .withAutomaticReconnect()
      .build();

    conn.on('OnBattleEvent', (eventType: string, payload: Payload) => {
      if (eventType === 'BattleStarted') {
        playerNameRef.current = payload.playerName as string;
        encounterIndexRef.current += 1;
      }

      const { now, steps } = expandEvent(eventType, payload, {
        playerName: playerNameRef.current,
        encounterIndex: encounterIndexRef.current,
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

    conn.onreconnected(() => { hydrateGold(); hydrateParty(); });
    conn.start()
      .then(() => { hydrateGold(); hydrateParty(); })
      .catch(err => console.error('[SignalR] Connection failed:', err));
    connRef.current = conn;

    return () => {
      conn.stop();
      connRef.current = null;
    };
  }, [gameId, enqueueSteps]);

  const chooseMove = useCallback((index: number) => {
    dispatch({ type: 'PLAYER_CHOSE' });
    connRef.current?.invoke('ChooseMove', index).catch(err =>
      console.error('[SignalR] ChooseMove failed:', err));
  }, []);

  // Use a bag item this turn. Like chooseMove, using an item IS the turn action, so mark the turn chosen
  // (→ battling/animating, locks the menu) and fire the hub call. targetMoveSlot is the move slot (0–3) a
  // single-move PP restore refills, null otherwise. The engine resolves a no-effect use (ItemUseFailed); the
  // resulting ItemUsed / effect events drive the log + HP/status/PP updates, so nothing is logged here.
  const useItem = useCallback((itemId: number, targetMoveSlot: number | null) => {
    dispatch({ type: 'PLAYER_CHOSE' });
    connRef.current?.invoke('UseItem', itemId, targetMoveSlot).catch(err =>
      console.error('[SignalR] UseItem failed:', err));
  }, []);

  // The level-up stat panel stays up until the player does anything; BattleScreen calls this on any
  // action (open FIGHT / CHECK, pick a move, QUIT) to dismiss it.
  const dismissLevelUp = useCallback(() => dispatch({ type: 'HIDE_LEVEL_UP' }), []);

  // Answer the level-up replace-move prompt: slot 0–3 to forget, or null to decline. Hide the modal at once
  // (the backend is blocked on this answer); the resulting MoveForgotten/MoveLearned/MoveLearnDeclined events
  // drive the log lines, so nothing is logged locally here.
  const forgetMove = useCallback((slot: number | null) => {
    dispatch({ type: 'HIDE_MOVE_REPLACEMENT' });
    connRef.current?.invoke('ForgetMove', slot).catch(err =>
      console.error('[SignalR] ForgetMove failed:', err));
  }, []);

  // Answer the evolution prompt: true to allow, false to cancel (Gen 1 B-cancel). Hide the modal at once (the
  // backend is blocked on this answer); the resulting evolution events drive the log + sprite/stat refresh.
  const respondEvolution = useCallback((allow: boolean) => {
    dispatch({ type: 'HIDE_EVOLUTION_PROMPT' });
    connRef.current?.invoke('RespondEvolution', allow).catch(err =>
      console.error('[SignalR] RespondEvolution failed:', err));
  }, []);

  const respondRecovery = useCallback((accept: boolean) => {
    dispatch({ type: 'HIDE_RECOVERY' });
    connRef.current?.invoke('RespondRecovery', accept).catch(err =>
      console.error('[SignalR] RespondRecovery failed:', err));
  }, []);

  // Answer the map-screen route choice: the chosen biome id. Hide the modal at once (the backend is blocked
  // awaiting the pick); the resulting BiomeEntered event titles the next leg in the log.
  const chooseBiome = useCallback((biomeId: string) => {
    dispatch({ type: 'HIDE_BIOME_CHOICE' });
    connRef.current?.invoke('ChooseBiome', biomeId).catch(err =>
      console.error('[SignalR] ChooseBiome failed:', err));
  }, []);

  // Answer the reward-choice modal: the chosen option index (an item or the gold bag). Hide the modal at once
  // (the backend is blocked awaiting the pick); the resulting RewardGranted event bumps the gold HUD, logs the
  // loot line, and raises the drop hover for the chosen reward.
  const chooseReward = useCallback((index: number) => {
    dispatch({ type: 'HIDE_REWARD_CHOICE' });
    connRef.current?.invoke('ChooseReward', index).catch(err =>
      console.error('[SignalR] ChooseReward failed:', err));
  }, []);

  // Buy a shop stock item by index. Unlike the one-shot prompts, the shop is iterative — the modal stays open
  // (do NOT hide it), and the resulting ShopItemPurchased event updates the balance + gold HUD and logs the buy.
  // An unaffordable/stale index is a no-op server-side, so the run just re-prompts.
  const buyShopItem = useCallback((index: number) => {
    connRef.current?.invoke('BuyShopItem', index).catch(err =>
      console.error('[SignalR] BuyShopItem failed:', err));
  }, []);

  // Leave the shop. Close the modal at once (optimistic — the backend advances to the next node, whose banner
  // confirms the move); the run loop is blocked awaiting this Leave.
  const leaveShop = useCallback(() => {
    dispatch({ type: 'HIDE_SHOP' });
    connRef.current?.invoke('LeaveShop').catch(err =>
      console.error('[SignalR] LeaveShop failed:', err));
  }, []);

  // Answer an acquisition offer: accept (optionally naming a member slot to swap out when the party is full) or
  // decline. Hide the modal at once (the backend is blocked awaiting the answer); the resulting CreatureAcquired
  // /AcquisitionDeclined + PartyUpdated events drive the log line and refresh the roster panel.
  const respondAcquisition = useCallback((accept: boolean, replaceSlot: number | null) => {
    dispatch({ type: 'HIDE_ACQUISITION' });
    connRef.current?.invoke('RespondAcquisition', accept, replaceSlot).catch(err =>
      console.error('[SignalR] RespondAcquisition failed:', err));
  }, []);

  // Clear the transient loot hover. Purely local (nothing server-side blocks on it) — the view runs a timer
  // and calls this to auto-dismiss the toast after its on-screen beat.
  const dismissDrop = useCallback(() => dispatch({ type: 'HIDE_DROP' }), []);

  return { state, chooseMove, useItem, dismissLevelUp, forgetMove, respondRecovery, respondEvolution, chooseBiome, chooseReward, buyShopItem, leaveShop, respondAcquisition, dismissDrop };
}
