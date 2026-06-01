import { useEffect, useRef, useReducer, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { MoveInfo } from '../types/BattleEvents';
import { bridge } from '../battle/PhaserBridge';
import { formatMoveName } from '../utils/format';

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

type Action =
  | { type: 'BATTLE_STARTED'; playerName: string; enemyName: string; enemySpeciesId: number; enemyLevel: number }
  | { type: 'TURN_STARTED'; turnNumber: number; playerHp: number; playerMaxHp: number; playerStatus: string; enemyHp: number; enemyMaxHp: number; enemyStatus: string; moves: MoveInfo[] }
  | { type: 'TURN_ENDED' }
  | { type: 'PLAYER_CHOSE' }
  | { type: 'BATTLE_ENDED'; winner: string }
  | { type: 'LOG'; message: string }
  | { type: 'UPDATE_HP'; name: string; hp: number }
  | { type: 'UPDATE_STATUS'; name: string; status: string }
  | { type: 'CLEAR_STATUS'; name: string }
  | { type: 'LEVELED_UP'; newLevel: number }
  | { type: 'ANIMATING_START' }
  | { type: 'ANIMATING_DONE' }
  | { type: 'XP_FILL' }
  | { type: 'XP_RESET' };

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

type Payload = Record<string, unknown>;

const delay = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, ms));

function statusAppliedMsg(name: string, status: string): string {
  switch (status) {
    case 'Sleep':     return `${name} fell asleep!`;
    case 'Freeze':    return `${name} was frozen solid!`;
    case 'Paralysis': return `${name} was paralyzed!`;
    case 'Burn':      return `${name} was burned!`;
    case 'Poison':    return `${name} was poisoned!`;
    case 'BadPoison': return `${name} was badly poisoned!`;
    default:          return `${name} was affected by ${status}!`;
  }
}

function statusClearedMsg(name: string, wasStatus: string): string {
  switch (wasStatus) {
    case 'Sleep':     return `${name} woke up!`;
    case 'Freeze':    return `${name} thawed out!`;
    case 'Paralysis': return `${name} was cured of paralysis!`;
    case 'Burn':      return `${name} healed its burn!`;
    case 'Poison':
    case 'BadPoison': return `${name} was cured of poison!`;
    default:          return `${name} recovered from ${wasStatus}!`;
  }
}

function actionBlockedMsg(name: string, reason: string): string {
  switch (reason) {
    case 'Sleep':     return `${name} is fast asleep!`;
    case 'Freeze':    return `${name} is frozen solid!`;
    case 'Paralysis': return `${name} is fully paralyzed!`;
    default:          return `${name} can't move! (${reason})`;
  }
}

// Gen 1 two-turn moves each have their own charge-up line.
function chargingMsg(name: string, slug: string): string {
  switch (slug) {
    case 'fly':        return `${name} flew up high!`;
    case 'dig':        return `${name} dug a hole!`;
    case 'solar-beam': return `${name} took in sunlight!`;
    case 'razor-wind': return `${name} made a whirlwind!`;
    case 'skull-bash': return `${name} lowered its head!`;
    case 'sky-attack': return `${name} is glowing!`;
    default:           return `${name} began charging up!`;
  }
}

// Resolve when Phaser signals the animation is done — but never hang the battle
// log if that signal is lost (e.g. a stale scene listener after an HMR remount).
function waitForBridge(timeoutMs = 3000): Promise<void> {
  return new Promise<void>(resolve => {
    let settled = false;
    let timer: ReturnType<typeof setTimeout>;
    const finish = () => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      bridge.off('animationComplete', handler);
      resolve();
    };
    const handler = () => finish();
    timer = setTimeout(finish, timeoutMs);
    bridge.on('animationComplete', handler);
  });
}

export function useBattleHub(gameId: string | null, initialLevel = 50) {
  const [state, dispatch] = useReducer(reducer, { ...initialState, playerLevel: initialLevel });
  const connRef = useRef<signalR.HubConnection | null>(null);

  // Stable refs to avoid stale closures inside queued async tasks
  const playerNameRef = useRef('');
  const enemyNameRef  = useRef('');

  // Animation queue
  const queueRef      = useRef<Array<() => Promise<void>>>([]);
  const processingRef = useRef(false);

  const drainQueue = useCallback(async () => {
    processingRef.current = true;
    dispatch({ type: 'ANIMATING_START' });
    try {
      while (queueRef.current.length > 0) {
        const task = queueRef.current.shift()!;
        // A failing animation task must never freeze the battle log — log it
        // and move on so faint/end events still render.
        try {
          await task();
        } catch (err) {
          console.error('[battle queue] task failed, continuing:', err);
        }
      }
    } finally {
      processingRef.current = false;
      dispatch({ type: 'ANIMATING_DONE' });
    }
  }, []);

  const enqueue = useCallback((task: () => Promise<void>) => {
    queueRef.current.push(task);
    if (!processingRef.current) drainQueue();
  }, [drainQueue]);

  useEffect(() => {
    if (!gameId) return;

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/battle?gameId=${gameId}`)
      .withAutomaticReconnect()
      .build();

    conn.on('OnBattleEvent', (eventType: string, payload: Payload) => {
      switch (eventType) {

        // ── Non-turn events: dispatch immediately ──────────────────────────

        case 'BattleStarted': {
          const pName = payload.playerName as string;
          const eName = payload.enemyName as string;
          playerNameRef.current = pName;
          enemyNameRef.current  = eName;
          dispatch({ type: 'BATTLE_STARTED', playerName: pName, enemyName: eName, enemySpeciesId: payload.enemySpeciesId as number, enemyLevel: payload.enemyLevel as number });
          dispatch({ type: 'LOG', message: `${pName} VS ${eName}` });
          break;
        }

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
          enqueue(async () => {
            await delay(200);
            dispatch({ type: 'LOG', message: `${payload.winnerName} wins!` });
          });
          break;

        // ── Turn events: enqueue for sequential animated playback ──────────

        case 'MoveUsed': {
          const attackerName = payload.attackerName as string;
          const moveName     = payload.moveName as string;
          enqueue(async () => {
            const attackerSide = attackerName === playerNameRef.current ? 'player' : 'enemy';
            const targetSide: 'player' | 'enemy' = attackerSide === 'player' ? 'enemy' : 'player';
            bridge.emit('playMoveAnimation', { attackerSide, targetSide });
            await waitForBridge();
            await delay(200);
            dispatch({ type: 'LOG', message: `${attackerName} used ${formatMoveName(moveName)}!` });
          });
          break;
        }

        case 'MoveMissed': {
          const aName = payload.attackerName as string;
          const mName = payload.moveName as string;
          enqueue(async () => {
            await delay(200);
            dispatch({ type: 'LOG', message: `${aName}'s ${formatMoveName(mName)} missed!` });
          });
          break;
        }

        case 'DamageDealt': {
          const targetName = payload.targetName as string;
          const hpAfter    = payload.hpAfter as number;
          const isCrit     = payload.isCrit as boolean;
          const eff        = payload.typeEffectiveness as number;
          const damage     = payload.damage as number;
          enqueue(async () => {
            // Immunity: no hit, no damage number — just the Gen 1 line.
            if (eff === 0) {
              await delay(650);
              dispatch({ type: 'LOG', message: `It doesn't affect ${targetName}...` });
              await delay(800);
              return;
            }
            bridge.emit('playHitSound', { isCrit });
            dispatch({ type: 'UPDATE_HP', name: targetName, hp: hpAfter });
            await delay(650);
            let msg = `${targetName} took ${damage} damage!`;
            if (isCrit) msg += ' A critical hit!';
            if (eff > 1)           msg += " It's super effective!";
            else if (eff < 1)      msg += " It's not very effective...";
            dispatch({ type: 'LOG', message: msg });
            // Breathing room between the two attackers' sequences
            await delay(800);
          });
          break;
        }

        case 'RecoilDamage': {
          const srcName = payload.sourceName as string;
          const hpAfter = payload.hpAfter as number;
          enqueue(async () => {
            dispatch({ type: 'UPDATE_HP', name: srcName, hp: hpAfter });
            await delay(400);
            dispatch({ type: 'LOG', message: `${srcName} is hit by recoil!` });
          });
          break;
        }

        case 'CreatureFainted': {
          const faintedName = payload.name as string;
          enqueue(async () => {
            const side = faintedName === playerNameRef.current ? 'player' : 'enemy';
            bridge.emit('playFaintAnimation', { side });
            await waitForBridge();
            await delay(200);
            dispatch({ type: 'LOG', message: `${faintedName} fainted!` });
          });
          break;
        }

        case 'LeveledUp': {
          const newLevel = payload.newLevel as number;
          const cName    = payload.creatureName as string;
          enqueue(async () => {
            dispatch({ type: 'XP_FILL' });
            await delay(900);
            dispatch({ type: 'XP_RESET' });
            dispatch({ type: 'LEVELED_UP', newLevel });
            dispatch({ type: 'LOG', message: `${cName} grew to level ${newLevel}!` });
          });
          break;
        }

        case 'StatusApplied': {
          const tName  = payload.targetName as string;
          const status = payload.status as string;
          enqueue(async () => {
            dispatch({ type: 'UPDATE_STATUS', name: tName, status });
            bridge.emit('playStatusSound', undefined);
            await delay(300);
            dispatch({ type: 'LOG', message: statusAppliedMsg(tName, status) });
          });
          break;
        }

        case 'StatusDamage': {
          const tName  = payload.targetName as string;
          const hpAftr = payload.hpAfter as number;
          const src    = payload.source === 'BadPoison' ? 'toxic poison' : payload.source as string;
          enqueue(async () => {
            dispatch({ type: 'UPDATE_HP', name: tName, hp: hpAftr });
            await delay(400);
            dispatch({ type: 'LOG', message: `${tName} is hurt by ${src}!` });
          });
          break;
        }

        case 'StatusCleared': {
          const cName     = payload.creatureName as string;
          const wasStatus = payload.wasStatus as string;
          enqueue(async () => {
            dispatch({ type: 'CLEAR_STATUS', name: cName });
            await delay(120);
            dispatch({ type: 'LOG', message: statusClearedMsg(cName, wasStatus) });
          });
          break;
        }

        case 'ActionBlocked': {
          const cName  = payload.creatureName as string;
          const reason = payload.reason as string;
          enqueue(async () => {
            await delay(300);
            dispatch({ type: 'LOG', message: actionBlockedMsg(cName, reason) });
            await delay(800);
          });
          break;
        }

        case 'ConfusionMessage': {
          const cName = payload.creatureName as string;
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: `${cName} is confused!` });
          });
          break;
        }

        case 'ConfusionDamage': {
          const cName  = payload.creatureName as string;
          const hpAftr = payload.hpAfter as number;
          enqueue(async () => {
            dispatch({ type: 'UPDATE_HP', name: cName, hp: hpAftr });
            await delay(400);
            dispatch({ type: 'LOG', message: `${cName} hurt itself in confusion!` });
          });
          break;
        }

        case 'ConfusionCleared': {
          const cName = payload.creatureName as string;
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: `${cName} snapped out of confusion!` });
          });
          break;
        }

        case 'StatStageChanged': {
          const cName = payload.creatureName as string;
          const delta = payload.delta as number;
          const stat  = payload.stat as string;
          enqueue(async () => {
            await delay(120);
            const dir   = delta > 0 ? 'rose' : 'fell';
            const sharp = Math.abs(delta) >= 2 ? ' sharply' : '';
            dispatch({ type: 'LOG', message: `${cName}'s ${stat}${sharp} ${dir}!` });
          });
          break;
        }

        case 'HazeClearedStages':
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: 'All stat changes were erased!' });
          });
          break;

        case 'DrainHealed': {
          const srcName    = payload.sourceName as string;
          const hpAftr     = payload.hpAfter as number;
          const healAmount = payload.healAmount as number;
          enqueue(async () => {
            dispatch({ type: 'UPDATE_HP', name: srcName, hp: hpAftr });
            await delay(300);
            dispatch({ type: 'LOG', message: `${srcName} restored ${healAmount} HP!` });
          });
          break;
        }

        case 'LeechSeedApplied': {
          const tName = payload.targetName as string;
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: `${tName} was seeded!` });
          });
          break;
        }

        case 'LeechSeedDamage': {
          const dName  = payload.drainedName as string;
          const hpAftr = payload.hpAfter as number;
          enqueue(async () => {
            dispatch({ type: 'UPDATE_HP', name: dName, hp: hpAftr });
            await delay(400);
            dispatch({ type: 'LOG', message: `${dName}'s health was sapped by Leech Seed!` });
          });
          break;
        }

        case 'LeechSeedHealed': {
          const hName  = payload.healedName as string;
          const hpAftr = payload.hpAfter as number;
          enqueue(async () => {
            dispatch({ type: 'UPDATE_HP', name: hName, hp: hpAftr });
          });
          break;
        }

        case 'Recharging': {
          const cName = payload.creatureName as string;
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: `${cName} must recharge!` });
          });
          break;
        }

        case 'BindingStarted': {
          const tName = payload.targetName as string;
          const mName = payload.moveName as string;
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: `${tName} was squeezed by ${formatMoveName(mName)}!` });
          });
          break;
        }

        case 'BindingBlocked': {
          const cName = payload.creatureName as string;
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: `${cName} is bound and can't move!` });
          });
          break;
        }

        case 'BindingDamage': {
          const tName  = payload.targetName as string;
          const hpAftr = payload.hpAfter as number;
          enqueue(async () => {
            dispatch({ type: 'UPDATE_HP', name: tName, hp: hpAftr });
            await delay(400);
            dispatch({ type: 'LOG', message: `${tName} is hurt by the bind!` });
          });
          break;
        }

        case 'FlinchBlocked': {
          const cName = payload.creatureName as string;
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: `${cName} flinched and couldn't move!` });
          });
          break;
        }

        case 'ChargingUp': {
          const cName = payload.creatureName as string;
          const mName = payload.moveName as string;
          enqueue(async () => {
            await delay(120);
            dispatch({ type: 'LOG', message: chargingMsg(cName, mName) });
          });
          break;
        }
      }
    });

    conn.start().catch(err => console.error('[SignalR] Connection failed:', err));
    connRef.current = conn;

    return () => {
      conn.stop();
      connRef.current = null;
    };
  }, [gameId, enqueue]);

  const chooseMove = useCallback((index: number) => {
    dispatch({ type: 'PLAYER_CHOSE' });
    connRef.current?.invoke('ChooseMove', index).catch(err =>
      console.error('[SignalR] ChooseMove failed:', err));
  }, []);

  return { state, chooseMove };
}
