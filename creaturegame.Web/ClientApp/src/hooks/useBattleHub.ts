import { useEffect, useRef, useReducer, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { MoveInfo } from '../types/BattleEvents';
import { type Action, type Payload, type StatBlock, type LogEntry, type BiomeOption, expandEvent, useBattleTimeline } from '../battle/timeline';

export interface LevelUpPanel {
  level: number;
  gains: StatBlock;
  totals: StatBlock;
}

export interface MoveReplacementPrompt {
  creatureName: string;
  newMoveName: string;
  currentMoves: string[];
}

export interface RecoveryPrompt {
  creatureName: string;
  speciesId: number;
  battlesWon: number;
}

export interface EvolutionPrompt {
  fromName: string;
  toName: string;
  fromSpeciesId: number;
  toSpeciesId: number;
}

export interface BiomeChoicePrompt {
  options: BiomeOption[];
}

export interface BattleState {
  phase: 'connecting' | 'waiting' | 'choosing' | 'battling' | 'ended';
  animating: boolean;
  playerName: string;
  playerHp: number;
  playerMaxHp: number;
  playerStatus: string;
  playerLevel: number;
  playerXp: number;
  playerXpToNext: number;
  enemyName: string;
  enemyHp: number;
  enemyMaxHp: number;
  enemyStatus: string;
  enemySpeciesId: number;
  enemyLevel: number;
  moves: MoveInfo[];
  battlesWon: number;
  levelUp: LevelUpPanel | null;
  moveReplacement: MoveReplacementPrompt | null;
  recovery: RecoveryPrompt | null;
  evolution: EvolutionPrompt | null;
  biomeChoice: BiomeChoicePrompt | null;
  log: LogEntry[];
  turnNumber: number;
}

const initialState: BattleState = {
  phase: 'connecting',
  animating: false,
  playerName: '',
  playerHp: 0,
  playerMaxHp: 1,
  playerStatus: 'None',
  playerLevel: 50,
  playerXp: 0,
  playerXpToNext: 100,
  enemyName: '',
  enemyHp: 0,
  enemyMaxHp: 1,
  enemyStatus: 'None',
  enemySpeciesId: 0,
  enemyLevel: 0,
  moves: [],
  battlesWon: 0,
  levelUp: null,
  moveReplacement: null,
  recovery: null,
  evolution: null,
  biomeChoice: null,
  log: [],
  turnNumber: 0,
};

function reducer(state: BattleState, action: Action): BattleState {
  switch (action.type) {
    case 'BATTLE_STARTED':
      // Reset the enemy nameplate for the incoming foe (the previous one fainted at 0 HP) — the next
      // TurnStarted fills in its real HP. enemyMaxHp: 1 makes BattleScreen show the estimate-full bar
      // during the slide-in rather than the old enemy's empty bar.
      return {
        ...state,
        phase: 'waiting',
        playerName: action.playerName,
        enemyName: action.enemyName,
        enemySpeciesId: action.enemySpeciesId,
        enemyLevel: action.enemyLevel,
        enemyHp: 1,
        enemyMaxHp: 1,
        enemyStatus: 'None',
      };
    case 'TURN_STARTED':
      return {
        ...state,
        phase: 'choosing',
        animating: false,
        turnNumber: action.turnNumber,
        playerHp: action.playerHp,
        playerMaxHp: action.playerMaxHp,
        playerStatus: action.playerStatus,
        playerXp: action.playerXpThisLevel,
        playerXpToNext: action.playerXpToNextLevel,
        enemyHp: action.enemyHp,
        enemyMaxHp: action.enemyMaxHp,
        enemyStatus: action.enemyStatus,
        moves: action.moves,
      };
    case 'PLAYER_CHOSE':
      return { ...state, phase: 'battling', animating: true };
    case 'TURN_ENDED':
      return { ...state, phase: 'battling' };
    case 'RUN_ENDED':
      // Terminal — the player fainted. Show the game-over screen with the run summary.
      return { ...state, phase: 'ended', battlesWon: action.battlesWon, playerLevel: action.finalLevel };
    case 'LOG':
      return { ...state, log: [...state.log, { message: action.message, tone: action.tone }] };
    case 'UPDATE_HP':
      if (action.name === state.playerName) return { ...state, playerHp: action.hp };
      if (action.name === state.enemyName)  return { ...state, enemyHp: action.hp };
      return state;
    case 'UPDATE_STATUS':
      if (action.name === state.playerName) return { ...state, playerStatus: action.status };
      if (action.name === state.enemyName)  return { ...state, enemyStatus: action.status };
      return state;
    case 'CLEAR_STATUS':
      if (action.name === state.playerName) return { ...state, playerStatus: 'None' };
      if (action.name === state.enemyName)  return { ...state, enemyStatus: 'None' };
      return state;
    case 'LEVELED_UP':
      // Tick the level and reset the bar onto the new level's scale (refilled by a following XP_SET).
      return { ...state, playerLevel: action.newLevel, playerXpToNext: action.xpToNextLevel, playerXp: 0 };
    case 'SHOW_LEVEL_UP':
      return { ...state, levelUp: { level: action.level, gains: action.gains, totals: action.totals } };
    case 'HIDE_LEVEL_UP':
      return { ...state, levelUp: null };
    case 'SHOW_MOVE_REPLACEMENT':
      // The replace prompt supersedes the level-up panel (Gen 1 shows the stat panel, then the prompt).
      return {
        ...state,
        levelUp: null,
        moveReplacement: { creatureName: action.creatureName, newMoveName: action.newMoveName, currentMoves: action.currentMoves },
      };
    case 'HIDE_MOVE_REPLACEMENT':
      return { ...state, moveReplacement: null };
    case 'SHOW_RECOVERY':
      return {
        ...state,
        recovery: { creatureName: action.creatureName, speciesId: action.speciesId, battlesWon: action.battlesWon },
      };
    case 'HIDE_RECOVERY':
      return { ...state, recovery: null };
    case 'SHOW_EVOLUTION_PROMPT':
      return {
        ...state,
        evolution: {
          fromName: action.fromName,
          toName: action.toName,
          fromSpeciesId: action.fromSpeciesId,
          toSpeciesId: action.toSpeciesId,
        },
      };
    case 'HIDE_EVOLUTION_PROMPT':
      return { ...state, evolution: null };
    case 'SHOW_BIOME_CHOICE':
      return { ...state, biomeChoice: { options: action.options } };
    case 'HIDE_BIOME_CHOICE':
      return { ...state, biomeChoice: null };
    case 'ANIMATING_START':
      return { ...state, animating: true };
    case 'ANIMATING_DONE':
      return { ...state, animating: false };
    case 'XP_GAIN':
      return { ...state, playerXp: Math.min(state.playerXp + action.amount, state.playerXpToNext) };
    case 'XP_SET':
      return { ...state, playerXp: action.value };
    default:
      return state;
  }
}

export function useBattleHub(gameId: string | null, initialLevel = 50) {
  const [state, dispatch] = useReducer(reducer, { ...initialState, playerLevel: initialLevel });
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
    });

    conn.start().catch(err => console.error('[SignalR] Connection failed:', err));
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

  return { state, chooseMove, useItem, dismissLevelUp, forgetMove, respondRecovery, respondEvolution, chooseBiome };
}
