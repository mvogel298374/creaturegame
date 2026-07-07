import { describe, it, expect } from 'vitest';
import { battleReducer, initialState, type BattleState } from './battleReducer';
import type { Action } from '../battle/timeline';

// A mid-battle state with both sides named, so the name-routed actions (UPDATE_HP/STATUS/CLEAR_STATUS)
// have a player and an enemy to resolve against.
const ready = (over: Partial<BattleState> = {}): BattleState => ({
  ...initialState,
  playerName: 'PIKACHU',
  enemyName: 'RATTATA',
  playerHp: 100,
  playerMaxHp: 100,
  enemyHp: 80,
  enemyMaxHp: 80,
  ...over,
});

describe('battleReducer — name-routed updates', () => {
  it('routes UPDATE_HP to the matching side by name', () => {
    const s = ready();
    expect(battleReducer(s, { type: 'UPDATE_HP', name: 'PIKACHU', hp: 42 }).playerHp).toBe(42);
    expect(battleReducer(s, { type: 'UPDATE_HP', name: 'RATTATA', hp: 7 }).enemyHp).toBe(7);
  });

  it('is a no-op (same reference) when the HP target matches neither side', () => {
    // The endless chain reuses one reducer across encounters; a late event from a *previous* foe (a name
    // that's now neither side) must not bleed onto the current enemy's bar. E2E can't force this race.
    const s = ready();
    const next = battleReducer(s, { type: 'UPDATE_HP', name: 'PIDGEY', hp: 0 });
    expect(next).toBe(s);
  });

  it('routes and clears status by name, and no-ops on an unknown name', () => {
    const s = ready({ playerStatus: 'None', enemyStatus: 'Sleep' });
    expect(battleReducer(s, { type: 'UPDATE_STATUS', name: 'PIKACHU', status: 'Burn' }).playerStatus).toBe('Burn');
    expect(battleReducer(s, { type: 'CLEAR_STATUS', name: 'RATTATA' }).enemyStatus).toBe('None');
    expect(battleReducer(s, { type: 'UPDATE_STATUS', name: 'GHOST', status: 'Burn' })).toBe(s);
  });
});

describe('battleReducer — XP bar math', () => {
  it('XP_GAIN adds onto the current fill', () => {
    const s = ready({ playerXp: 10, playerXpToNext: 100 });
    expect(battleReducer(s, { type: 'XP_GAIN', amount: 30 }).playerXp).toBe(40);
  });

  it('XP_GAIN clamps at the current level max (a level-up handles the overflow)', () => {
    const s = ready({ playerXp: 90, playerXpToNext: 100 });
    expect(battleReducer(s, { type: 'XP_GAIN', amount: 50 }).playerXp).toBe(100);
  });

  it('XP_SET sets the absolute fill (used to refill leftover after a level reset)', () => {
    const s = ready({ playerXp: 100, playerXpToNext: 250 });
    expect(battleReducer(s, { type: 'XP_SET', value: 30 }).playerXp).toBe(30);
  });

  it('LEVELED_UP ticks the level, rescales the bar, and zeroes the fill', () => {
    const s = ready({ playerLevel: 12, playerXp: 100, playerXpToNext: 100 });
    const next = battleReducer(s, { type: 'LEVELED_UP', newLevel: 13, xpToNextLevel: 250 });
    expect(next.playerLevel).toBe(13);
    expect(next.playerXpToNext).toBe(250);
    expect(next.playerXp).toBe(0);
  });
});

describe('battleReducer — modal gating', () => {
  it('SHOW_MOVE_REPLACEMENT supersedes the level-up panel (Gen 1 order)', () => {
    const s = ready({ levelUp: { level: 13, gains: {} as never, totals: {} as never } });
    const next = battleReducer(s, {
      type: 'SHOW_MOVE_REPLACEMENT',
      creatureName: 'PIKACHU',
      newMoveName: 'thunderbolt',
      currentMoves: ['a', 'b', 'c', 'd'],
    });
    expect(next.levelUp).toBeNull();
    expect(next.moveReplacement?.newMoveName).toBe('thunderbolt');
  });

  it('each show/hide pair sets then clears exactly its own slice', () => {
    // The generic loot hover — the single reward popup for every source (battle drop + Treasure/Mystery).
    const drop = battleReducer(ready(), { type: 'SHOW_DROP', gold: 8, itemNames: ['Potion'] });
    expect(drop.dropToast).toEqual({ gold: 8, itemNames: ['Potion'] });
    expect(battleReducer(drop, { type: 'HIDE_DROP' }).dropToast).toBeNull();

    // Biome choice has no E2E coverage (no map spec), so its transitions are only pinned here.
    const opts = [{ id: 'marsh', name: 'Marsh', types: ['Ghost'] }];
    const biome = battleReducer(ready(), { type: 'SHOW_BIOME_CHOICE', options: opts });
    expect(biome.biomeChoice?.options).toEqual(opts);
    expect(battleReducer(biome, { type: 'HIDE_BIOME_CHOICE' }).biomeChoice).toBeNull();

    // Reward choice: the pick-one-of-N modal — set by SHOW_REWARD_CHOICE, cleared when the player picks.
    const rewardOpts = [
      { kind: 'item' as const, itemId: 25, itemName: 'hyper-potion', rarity: 'Rare' as const, gold: 0 },
      { kind: 'gold' as const, itemId: 0, itemName: null, rarity: null, gold: 60 },
    ];
    const reward = battleReducer(ready(), { type: 'SHOW_REWARD_CHOICE', source: 'Battle', options: rewardOpts });
    expect(reward.rewardChoice).toEqual({ source: 'Battle', options: rewardOpts });
    expect(battleReducer(reward, { type: 'HIDE_REWARD_CHOICE' }).rewardChoice).toBeNull();
  });
});

describe('battleReducer — phase transitions', () => {
  it('BATTLE_STARTED resets the enemy nameplate for the incoming foe', () => {
    // The previous foe fainted at 0 HP; the new one must show a full estimate bar during slide-in
    // (enemyHp/enemyMaxHp = 1), not the old empty bar, until the next TURN_STARTED fills real values.
    const s = ready({ enemyHp: 0, enemyStatus: 'Poison' });
    const next = battleReducer(s, {
      type: 'BATTLE_STARTED', playerName: 'PIKACHU', enemyName: 'PIDGEY', enemySpeciesId: 16, enemyLevel: 8,
    });
    expect(next.phase).toBe('waiting');
    expect(next.enemyName).toBe('PIDGEY');
    expect(next.enemyHp).toBe(1);
    expect(next.enemyMaxHp).toBe(1);
    expect(next.enemyStatus).toBe('None');
  });

  it('TURN_STARTED moves to the choosing phase and stops animating', () => {
    const s = ready({ phase: 'battling', animating: true });
    const next = battleReducer(s, {
      type: 'TURN_STARTED', turnNumber: 3,
      playerHp: 55, playerMaxHp: 100, playerStatus: 'None', playerXpThisLevel: 20, playerXpToNextLevel: 100,
      enemyHp: 33, enemyMaxHp: 80, enemyStatus: 'Paralysis', moves: [],
    });
    expect(next.phase).toBe('choosing');
    expect(next.animating).toBe(false);
    expect(next.turnNumber).toBe(3);
    expect(next.enemyStatus).toBe('Paralysis');
  });

  it('PLAYER_CHOSE locks into the battling/animating phase', () => {
    const next = battleReducer(ready({ phase: 'choosing' }), { type: 'PLAYER_CHOSE' });
    expect(next.phase).toBe('battling');
    expect(next.animating).toBe(true);
  });

  it('RUN_ENDED is terminal and carries the run summary', () => {
    const next = battleReducer(ready({ phase: 'battling' }), { type: 'RUN_ENDED', battlesWon: 5, finalLevel: 23 });
    expect(next.phase).toBe('ended');
    expect(next.battlesWon).toBe(5);
    expect(next.playerLevel).toBe(23);
  });
});

describe('battleReducer — misc', () => {
  it('LOG appends to the log preserving order and tone', () => {
    const s = ready({ log: [{ message: 'first' }] });
    const next = battleReducer(s, { type: 'LOG', message: 'super!', tone: 'super' });
    expect(next.log).toEqual([{ message: 'first' }, { message: 'super!', tone: 'super' }]);
  });

  it('SET_GOLD sets the HUD total', () => {
    expect(battleReducer(ready(), { type: 'SET_GOLD', gold: 250 }).gold).toBe(250);
  });

  it('returns the same state reference for an unknown action', () => {
    const s = ready();
    expect(battleReducer(s, { type: 'NOT_A_REAL_ACTION' } as unknown as Action)).toBe(s);
  });

  it('does not mutate the input state', () => {
    const s = ready({ playerHp: 100 });
    const snapshot = JSON.stringify(s);
    battleReducer(s, { type: 'UPDATE_HP', name: 'PIKACHU', hp: 1 });
    expect(JSON.stringify(s)).toBe(snapshot);
  });
});
