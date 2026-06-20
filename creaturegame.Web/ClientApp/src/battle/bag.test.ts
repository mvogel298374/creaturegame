import { describe, it, expect } from 'vitest';
import {
  type BagItem,
  isUsableInBattle,
  needsMoveTarget,
  formatItemName,
  groupBagItems,
} from './bag';

const item = (over: Partial<BagItem> = {}): BagItem => ({
  id: 1,
  name: 'potion',
  category: 'Healing',
  quantity: 5,
  description: '',
  restoresPpAllMoves: false,
  ...over,
});

describe('isUsableInBattle', () => {
  it('accepts the categories the engine has an in-battle effect for', () => {
    for (const c of ['Healing', 'StatusCure', 'PpRestore', 'BattleStatBoost'])
      expect(isUsableInBattle(c)).toBe(true);
  });

  it('rejects categories that would just waste the turn (Ball, Revive, Other)', () => {
    for (const c of ['Ball', 'Revive', 'Other', 'unknown'])
      expect(isUsableInBattle(c)).toBe(false);
  });
});

describe('needsMoveTarget', () => {
  it('is true only for a single-move PP restore (Ether / Max Ether)', () => {
    expect(needsMoveTarget({ category: 'PpRestore', restoresPpAllMoves: false })).toBe(true);
  });

  it('is false for a whole-moveset PP restore (Elixir / Max Elixir)', () => {
    expect(needsMoveTarget({ category: 'PpRestore', restoresPpAllMoves: true })).toBe(false);
  });

  it('is false for non-PP items', () => {
    expect(needsMoveTarget({ category: 'Healing', restoresPpAllMoves: false })).toBe(false);
    expect(needsMoveTarget({ category: 'BattleStatBoost', restoresPpAllMoves: false })).toBe(false);
  });
});

describe('formatItemName', () => {
  it('uppercases and de-hyphenates the slug', () => {
    expect(formatItemName('super-potion')).toBe('SUPER POTION');
    expect(formatItemName('x-attack')).toBe('X ATTACK');
    expect(formatItemName('ether')).toBe('ETHER');
  });

  it('passes empty through unchanged', () => {
    expect(formatItemName('')).toBe('');
  });
});

describe('groupBagItems', () => {
  it('drops non-usable categories and groups the rest by pocket, in order', () => {
    const items: BagItem[] = [
      item({ id: 1, name: 'potion', category: 'Healing' }),
      item({ id: 2, name: 'poke-ball', category: 'Ball' }),
      item({ id: 3, name: 'antidote', category: 'StatusCure' }),
      item({ id: 4, name: 'revive', category: 'Revive' }),
      item({ id: 5, name: 'ether', category: 'PpRestore' }),
      item({ id: 6, name: 'x-attack', category: 'BattleStatBoost' }),
    ];
    const groups = groupBagItems(items);
    expect(groups.map(g => g.label)).toEqual(['HEALING', 'STATUS', 'PP RESTORE', 'BATTLE']);
    expect(groups.flatMap(g => g.items.map(i => i.name)))
      .toEqual(['potion', 'antidote', 'ether', 'x-attack']);
  });

  it('omits empty pockets and zero-quantity items', () => {
    const items: BagItem[] = [
      item({ id: 1, name: 'potion', category: 'Healing', quantity: 0 }),
      item({ id: 2, name: 'antidote', category: 'StatusCure', quantity: 2 }),
    ];
    const groups = groupBagItems(items);
    expect(groups.map(g => g.label)).toEqual(['STATUS']);
    expect(groups[0].items.map(i => i.name)).toEqual(['antidote']);
  });

  it('returns no groups when nothing is usable', () => {
    expect(groupBagItems([item({ category: 'Ball' }), item({ category: 'Revive' })])).toEqual([]);
  });
});
