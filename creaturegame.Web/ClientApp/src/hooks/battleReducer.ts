// The battle view-state reducer — a PURE (state, action) => state function, extracted from useBattleHub so
// it's unit-testable without React, SignalR, or a DOM (this module has only type imports, no runtime deps).
// The hook owns the effects (the SignalR connection, the timeline driver); this owns the state transitions.
import type { MoveInfo } from '../types/BattleEvents';
import type { Action, StatBlock, LogEntry, BiomeOption } from '../battle/timeline';

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

// A transient loot hover (gold + items) floated over the field, then auto-dismissed by the view. The single,
// generic reward popup: every source — a battle-win drop and a Treasure/Mystery node — uses it (node rewards
// no longer raise a blocking modal; the client auto-acks them).
export interface DropToast {
  gold: number;        // gold dropped by this reward (0 if none)
  itemNames: string[]; // display names of any items dropped
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
  dropToast: DropToast | null;
  gold: number;
  log: LogEntry[];
  turnNumber: number;
}

export const initialState: BattleState = {
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
  dropToast: null,
  gold: 0,
  log: [],
  turnNumber: 0,
};

export function battleReducer(state: BattleState, action: Action): BattleState {
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
    case 'SET_GOLD':
      return { ...state, gold: action.gold };
    case 'SHOW_DROP':
      return { ...state, dropToast: { gold: action.gold, itemNames: action.itemNames } };
    case 'HIDE_DROP':
      return { ...state, dropToast: null };
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
