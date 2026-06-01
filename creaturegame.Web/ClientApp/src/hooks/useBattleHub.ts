import { useEffect, useRef, useReducer, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { MoveInfo } from '../types/BattleEvents';
import { type Action, type Payload, expandEvent, useBattleTimeline } from '../battle/timeline';

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
  winner: string | null;
  log: string[];
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
  winner: null,
  log: [],
  turnNumber: 0,
};

function reducer(state: BattleState, action: Action): BattleState {
  switch (action.type) {
    case 'BATTLE_STARTED':
      return { ...state, phase: 'waiting', playerName: action.playerName, enemyName: action.enemyName, enemySpeciesId: action.enemySpeciesId, enemyLevel: action.enemyLevel };
    case 'TURN_STARTED':
      return {
        ...state,
        phase: 'choosing',
        animating: false,
        turnNumber: action.turnNumber,
        playerHp: action.playerHp,
        playerMaxHp: action.playerMaxHp,
        playerStatus: action.playerStatus,
        enemyHp: action.enemyHp,
        enemyMaxHp: action.enemyMaxHp,
        enemyStatus: action.enemyStatus,
        moves: action.moves,
      };
    case 'PLAYER_CHOSE':
      return { ...state, phase: 'battling', animating: true };
    case 'TURN_ENDED':
      return { ...state, phase: 'battling' };
    case 'BATTLE_ENDED':
      return { ...state, phase: 'ended', winner: action.winner };
    case 'LOG':
      return { ...state, log: [...state.log, action.message] };
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
      return { ...state, playerLevel: action.newLevel };
    case 'ANIMATING_START':
      return { ...state, animating: true };
    case 'ANIMATING_DONE':
      return { ...state, animating: false };
    case 'XP_FILL':
      return { ...state, playerXp: state.playerXpToNext };
    case 'XP_RESET':
      return { ...state, playerXp: 0 };
    default:
      return state;
  }
}

export function useBattleHub(gameId: string | null, initialLevel = 50) {
  const [state, dispatch] = useReducer(reducer, { ...initialState, playerLevel: initialLevel });
  const connRef = useRef<signalR.HubConnection | null>(null);

  // Player name drives the player/enemy side split inside expandEvent.
  const playerNameRef = useRef('');

  // The animation timeline: backend events expand into steps it plays in order.
  const enqueueSteps = useBattleTimeline(dispatch);

  useEffect(() => {
    if (!gameId) return;

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/battle?gameId=${gameId}`)
      .withAutomaticReconnect()
      .build();

    conn.on('OnBattleEvent', (eventType: string, payload: Payload) => {
      if (eventType === 'BattleStarted') playerNameRef.current = payload.playerName as string;

      const { now, steps } = expandEvent(eventType, payload, { playerName: playerNameRef.current });
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

  return { state, chooseMove };
}
