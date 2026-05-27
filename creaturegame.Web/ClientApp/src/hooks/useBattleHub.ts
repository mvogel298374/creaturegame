import { useEffect, useRef, useReducer, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { MoveInfo } from '../types/BattleEvents';

export interface BattleState {
  phase: 'connecting' | 'waiting' | 'choosing' | 'battling' | 'ended';
  playerName: string;
  playerHp: number;
  playerMaxHp: number;
  playerStatus: string;
  enemyName: string;
  enemyHp: number;
  enemyMaxHp: number;
  enemyStatus: string;
  moves: MoveInfo[];
  winner: string | null;
  log: string[];
  turnNumber: number;
}

type Action =
  | { type: 'BATTLE_STARTED'; playerName: string; enemyName: string }
  | { type: 'TURN_STARTED'; turnNumber: number; playerHp: number; playerMaxHp: number; playerStatus: string; enemyHp: number; enemyMaxHp: number; enemyStatus: string; moves: MoveInfo[] }
  | { type: 'TURN_ENDED' }
  | { type: 'PLAYER_CHOSE' }
  | { type: 'BATTLE_ENDED'; winner: string }
  | { type: 'LOG'; message: string }
  | { type: 'UPDATE_HP'; name: string; hp: number }
  | { type: 'UPDATE_STATUS'; name: string; status: string }
  | { type: 'CLEAR_STATUS'; name: string };

const initialState: BattleState = {
  phase: 'connecting',
  playerName: '',
  playerHp: 0,
  playerMaxHp: 1,
  playerStatus: 'None',
  enemyName: '',
  enemyHp: 0,
  enemyMaxHp: 1,
  enemyStatus: 'None',
  moves: [],
  winner: null,
  log: [],
  turnNumber: 0,
};

function reducer(state: BattleState, action: Action): BattleState {
  switch (action.type) {
    case 'BATTLE_STARTED':
      return { ...state, phase: 'waiting', playerName: action.playerName, enemyName: action.enemyName };
    case 'TURN_STARTED':
      return {
        ...state,
        phase: 'choosing',
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
      return { ...state, phase: 'battling' };
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
    default:
      return state;
  }
}

type Payload = Record<string, unknown>;

export function useBattleHub(gameId: string | null) {
  const [state, dispatch] = useReducer(reducer, initialState);
  const connRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!gameId) return;

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/battle?gameId=${gameId}`)
      .withAutomaticReconnect()
      .build();

    conn.on('OnBattleEvent', (eventType: string, payload: Payload) => {
      switch (eventType) {
        case 'BattleStarted':
          dispatch({ type: 'BATTLE_STARTED', playerName: payload.playerName as string, enemyName: payload.enemyName as string });
          dispatch({ type: 'LOG', message: `${payload.playerName} VS ${payload.enemyName}` });
          break;

        case 'TurnStarted':
          dispatch({
            type: 'TURN_STARTED',
            turnNumber: payload.turnNumber as number,
            playerHp: payload.playerHp as number,
            playerMaxHp: payload.playerMaxHp as number,
            playerStatus: payload.playerStatus as string,
            enemyHp: payload.enemyHp as number,
            enemyMaxHp: payload.enemyMaxHp as number,
            enemyStatus: payload.enemyStatus as string,
            moves: payload.moves as MoveInfo[],
          });
          break;

        case 'TurnEnded':
          dispatch({ type: 'TURN_ENDED' });
          break;

        case 'BattleEnded':
          dispatch({ type: 'BATTLE_ENDED', winner: payload.winnerName as string });
          dispatch({ type: 'LOG', message: `${payload.winnerName} wins!` });
          break;

        case 'MoveUsed':
          dispatch({ type: 'LOG', message: `${payload.attackerName} used ${payload.moveName}!` });
          break;

        case 'MoveMissed':
          dispatch({ type: 'LOG', message: `${payload.attackerName}'s ${payload.moveName} missed!` });
          break;

        case 'DamageDealt': {
          dispatch({ type: 'UPDATE_HP', name: payload.targetName as string, hp: payload.hpAfter as number });
          let msg = `${payload.targetName} took ${payload.damage} damage!`;
          if (payload.isCrit) msg += ' A critical hit!';
          const eff = payload.typeEffectiveness as number;
          if (eff > 1)       msg += " It's super effective!";
          else if (eff > 0 && eff < 1) msg += " It's not very effective...";
          else if (eff === 0) msg += " It had no effect.";
          dispatch({ type: 'LOG', message: msg });
          break;
        }

        case 'RecoilDamage':
          dispatch({ type: 'UPDATE_HP', name: payload.sourceName as string, hp: payload.hpAfter as number });
          dispatch({ type: 'LOG', message: `${payload.sourceName} is hit by recoil!` });
          break;

        case 'StatusApplied':
          dispatch({ type: 'UPDATE_STATUS', name: payload.targetName as string, status: payload.status as string });
          dispatch({ type: 'LOG', message: `${payload.targetName} was ${(payload.status as string).toLowerCase()}ed!` });
          break;

        case 'StatusDamage':
          dispatch({ type: 'UPDATE_HP', name: payload.targetName as string, hp: payload.hpAfter as number });
          dispatch({ type: 'LOG', message: `${payload.targetName} is hurt by ${payload.source}!` });
          break;

        case 'StatusCleared':
          dispatch({ type: 'CLEAR_STATUS', name: payload.creatureName as string });
          dispatch({ type: 'LOG', message: `${payload.creatureName} recovered from ${payload.wasStatus}!` });
          break;

        case 'ActionBlocked':
          dispatch({ type: 'LOG', message: `${payload.creatureName} can't move! (${payload.reason})` });
          break;

        case 'ConfusionMessage':
          dispatch({ type: 'LOG', message: `${payload.creatureName} is confused!` });
          break;

        case 'ConfusionDamage':
          dispatch({ type: 'UPDATE_HP', name: payload.creatureName as string, hp: payload.hpAfter as number });
          dispatch({ type: 'LOG', message: `${payload.creatureName} hurt itself in confusion!` });
          break;

        case 'ConfusionCleared':
          dispatch({ type: 'LOG', message: `${payload.creatureName} snapped out of confusion!` });
          break;

        case 'StatStageChanged': {
          const dir = (payload.delta as number) > 0 ? 'rose' : 'fell';
          const sharp = Math.abs(payload.delta as number) >= 2 ? ' sharply' : '';
          dispatch({ type: 'LOG', message: `${payload.creatureName}'s ${payload.stat}${sharp} ${dir}!` });
          break;
        }

        case 'HazeClearedStages':
          dispatch({ type: 'LOG', message: 'All stat changes were erased!' });
          break;

        case 'CreatureFainted':
          dispatch({ type: 'LOG', message: `${payload.name} fainted!` });
          break;
      }
    });

    conn.start().catch(err => console.error('[SignalR] Connection failed:', err));
    connRef.current = conn;

    return () => {
      conn.stop();
      connRef.current = null;
    };
  }, [gameId]);

  const chooseMove = useCallback((index: number) => {
    dispatch({ type: 'PLAYER_CHOSE' });
    connRef.current?.invoke('ChooseMove', index).catch(err =>
      console.error('[SignalR] ChooseMove failed:', err));
  }, []);

  return { state, chooseMove };
}