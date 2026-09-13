import { useEffect, useRef, useReducer, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { type Payload, expandEvent, useBattleTimeline } from '../battle/timeline';
import { battleReducer, initialState } from './battleReducer';
import { bossTrainerName } from '../battle/bossTrainer';
import { nextPlayerName } from '../battle/playerIdentity';

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
  SwitchInPrompt,
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

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/battle?gameId=${gameId}`)
      .withAutomaticReconnect()
      .build();

    conn.on('OnBattleEvent', (eventType: string, payload: Payload) => {
      // Retarget the player/enemy side split BEFORE the event expands, so the newly-named creature's own
      // moves/damage are sided correctly in the very event that renamed it. The rule itself (which events change
      // "who the player is", and the party-wide-evolution guard) lives in the pure helper.
      playerNameRef.current = nextPlayerName(eventType, payload, playerNameRef.current);
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
        playerName: playerNameRef.current,
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

  // Below: the whole-turn choices (switching/items are turn actions too, like chooseMove) and every modal
  // answer follow one shape — dispatch the local HIDE/PLAYER_CHOSE action immediately (the backend is blocked
  // awaiting it, or the turn is locked), then invoke the hub method; the resulting server events drive the log
  // + sprite + panel refresh. Deviations (shop's iterative non-hide, purely-local dismissals) are called out
  // per callback below.

  const chooseSwitch = useCallback((index: number) => {
    dispatch({ type: 'PLAYER_CHOSE' });
    connRef.current?.invoke('ChooseSwitch', index).catch(err =>
      console.error('[SignalR] ChooseSwitch failed:', err));
  }, []);

  // targetMoveSlot: the move slot (0–3) a single-move PP restore refills. targetPartySlot: the party-member
  // index a Revive targets (a fainted benched member). Both null otherwise.
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

  const forgetMove = useCallback((slot: number | null) => {
    dispatch({ type: 'HIDE_MOVE_REPLACEMENT' });
    connRef.current?.invoke('ForgetMove', slot).catch(err =>
      console.error('[SignalR] ForgetMove failed:', err));
  }, []);

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

  const chooseBiome = useCallback((biomeId: string) => {
    dispatch({ type: 'HIDE_BIOME_CHOICE' });
    connRef.current?.invoke('ChooseBiome', biomeId).catch(err =>
      console.error('[SignalR] ChooseBiome failed:', err));
  }, []);

  const chooseReward = useCallback((index: number) => {
    dispatch({ type: 'HIDE_REWARD_CHOICE' });
    connRef.current?.invoke('ChooseReward', index).catch(err =>
      console.error('[SignalR] ChooseReward failed:', err));
  }, []);

  // Deviates from the shape above: the shop is iterative, so the modal stays open (do NOT hide it) across buys.
  const buyShopItem = useCallback((index: number) => {
    connRef.current?.invoke('BuyShopItem', index).catch(err =>
      console.error('[SignalR] BuyShopItem failed:', err));
  }, []);

  const leaveShop = useCallback(() => {
    dispatch({ type: 'HIDE_SHOP' });
    connRef.current?.invoke('LeaveShop').catch(err =>
      console.error('[SignalR] LeaveShop failed:', err));
  }, []);

  // replaceSlot: the member slot to swap out when accepting with a full party; null otherwise.
  const respondAcquisition = useCallback((accept: boolean, replaceSlot: number | null) => {
    dispatch({ type: 'HIDE_ACQUISITION' });
    connRef.current?.invoke('RespondAcquisition', accept, replaceSlot).catch(err =>
      console.error('[SignalR] RespondAcquisition failed:', err));
  }, []);

  const chooseLead = useCallback((index: number) => {
    dispatch({ type: 'HIDE_LEAD_CHOICE' });
    connRef.current?.invoke('ChooseLead', index).catch(err =>
      console.error('[SignalR] ChooseLead failed:', err));
  }, []);

  const respondSwitchIn = useCallback((index: number) => {
    dispatch({ type: 'HIDE_SWITCH_IN' });
    connRef.current?.invoke('RespondSwitchIn', index).catch(err =>
      console.error('[SignalR] RespondSwitchIn failed:', err));
  }, []);

  // Purely local (nothing server-side blocks on it) — the view runs a timer and calls this to auto-dismiss the
  // toast after its on-screen beat.
  const dismissDrop = useCallback(() => dispatch({ type: 'HIDE_DROP' }), []);

  return { state, chooseMove, chooseSwitch, useItem, dismissLevelUp, forgetMove, respondRecovery, respondEvolution, chooseBiome, chooseReward, buyShopItem, leaveShop, respondAcquisition, chooseLead, respondSwitchIn, dismissDrop };
}
