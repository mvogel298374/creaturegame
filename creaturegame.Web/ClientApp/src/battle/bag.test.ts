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
  usableInBattle: true,
  ...over,
});

describe('isUsableInBattle', () => {
  // Usability is the server's verdict (BagItemView.UsableInBattle, from the engine's ItemEffects
  // registry); the client just reflects the flag rather than re-encoding the category→effect mapping.
  it('reflects the server-computed usableInBattle flag', () => {
    expect(isUsableInBattle(item({ usableInBattle: true }))).toBe(true);
    expect(isUsableInBattle(item({ usableInBattle: false }))).toBe(false);
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
  it('drops non-usable items and groups the rest by pocket, in order', () => {
    const items: BagItem[] = [
      item({ id: 1, name: 'potion', category: 'Healing' }),
      item({ id: 2, name: 'poke-ball', category: 'Ball', usableInBattle: false }),
      item({ id: 3, name: 'antidote', category: 'StatusCure' }),
      item({ id: 4, name: 'revive', category: 'Revive', usableInBattle: false }),
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
    expect(
      groupBagItems([
        item({ category: 'Ball', usableInBattle: false }),
        item({ category: 'Revive', usableInBattle: false }),
      ])
    ).toEqual([]);
  });
});
