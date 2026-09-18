import { describe, it, expect } from 'vitest';
import {
  type BagItem,
  isUsableInBattle,
  needsMoveTarget,
  needsPartyTarget,
  partyTargetMode,
  moveSourceForPartyPick,
  formatItemName,
  groupBagItems,
} from './bag';
import type { PartyMember } from './timeline';

function member(overrides: Partial<PartyMember> = {}): PartyMember {
  return {
    speciesId: 1,
    name: 'MON',
    level: 5,
    hp: 10,
    maxHp: 10,
    status: 'None',
    isLead: false,
    ...overrides,
  };
}

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

describe('needsPartyTarget', () => {
  it('is true for every category except BattleStatBoost', () => {
    expect(needsPartyTarget({ category: 'Healing' })).toBe(true);
    expect(needsPartyTarget({ category: 'StatusCure' })).toBe(true);
    expect(needsPartyTarget({ category: 'PpRestore' })).toBe(true);
    expect(needsPartyTarget({ category: 'Revive' })).toBe(true);
  });

  it('is false for BattleStatBoost (Gen 1 has no per-member stat-stage slot)', () => {
    expect(needsPartyTarget({ category: 'BattleStatBoost' })).toBe(false);
  });
});

describe('partyTargetMode', () => {
  it('is "fainted" only for Revive', () => {
    expect(partyTargetMode({ category: 'Revive' })).toBe('fainted');
  });

  it('is "living" for every other category', () => {
    expect(partyTargetMode({ category: 'Healing' })).toBe('living');
    expect(partyTargetMode({ category: 'StatusCure' })).toBe('living');
    expect(partyTargetMode({ category: 'PpRestore' })).toBe('living');
  });
});

describe('moveSourceForPartyPick', () => {
  it('reuses the already-loaded moveset when the pick IS the active creature', () => {
    const party = [member({ name: 'BENCH' }), member({ name: 'LEAD', isLead: true })];
    expect(moveSourceForPartyPick('game-1', party, 1)).toEqual({ kind: 'inline' });
  });

  it('fetches that member\'s own overview when the pick is a bench member', () => {
    const party = [member({ name: 'BENCH' }), member({ name: 'LEAD', isLead: true })];
    expect(moveSourceForPartyPick('game-1', party, 0)).toEqual({
      kind: 'fetch',
      url: '/api/game/game-1/player/0',
    });
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

  it('surfaces a usable Revive under its own REVIVE pocket, in order', () => {
    // When a fainted member exists the server marks the Revive usableInBattle, so the menu shows it — between
    // the STATUS and PP RESTORE pockets (CATEGORY_LABELS order).
    const items: BagItem[] = [
      item({ id: 1, name: 'potion', category: 'Healing' }),
      item({ id: 2, name: 'revive', category: 'Revive', usableInBattle: true }),
      item({ id: 3, name: 'ether', category: 'PpRestore' }),
    ];
    const groups = groupBagItems(items);
    expect(groups.map(g => g.label)).toEqual(['HEALING', 'REVIVE', 'PP RESTORE']);
    expect(groups.find(g => g.label === 'REVIVE')?.items.map(i => i.name)).toEqual(['revive']);
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
