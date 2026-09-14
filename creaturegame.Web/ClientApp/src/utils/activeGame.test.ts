import { describe, it, expect, beforeEach } from 'vitest';
import { saveActiveGame, loadActiveGame, clearActiveGame } from './activeGame';
import type { Species } from '../types/Species';

// Same in-memory Storage stand-in as settings.test.ts — this project's Vitest config runs in the plain 'node'
// environment (no jsdom), so there's no built-in Storage API.
function fakeLocalStorage(): Storage {
  const store = new Map<string, string>();
  return {
    getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
    setItem: (k: string, v: string) => { store.set(k, v); },
    removeItem: (k: string) => { store.delete(k); },
    clear: () => store.clear(),
    key: () => null,
    length: 0,
  } as Storage;
}

beforeEach(() => {
  (globalThis as unknown as { localStorage: Storage }).localStorage = fakeLocalStorage();
});

const species: Species = {
  id: 1, name: 'Bulbasaur', type1: 'Grass', type2: 'Poison',
  baseHp: 45, baseAttack: 49, baseDefense: 49, baseSpecial: 65, baseSpeed: 45, baseStatTotal: 253,
};

describe('activeGame', () => {
  it('returns null when nothing is stored', () => {
    expect(loadActiveGame()).toBeNull();
  });

  it('round-trips a saved game, stamping savedAt', () => {
    saveActiveGame({ gameId: 'abc123', species, level: 50, generation: 'One' });

    const loaded = loadActiveGame();
    expect(loaded).not.toBeNull();
    expect(loaded).toMatchObject({ gameId: 'abc123', species, level: 50, generation: 'One' });
    expect(typeof loaded!.savedAt).toBe('number');
  });

  it('clears a saved game', () => {
    saveActiveGame({ gameId: 'abc123', species, level: 50, generation: 'One' });
    clearActiveGame();

    expect(loadActiveGame()).toBeNull();
  });

  it('falls back to null on corrupt JSON', () => {
    localStorage.setItem('creaturegame.activeGame', 'not json');
    expect(loadActiveGame()).toBeNull();
  });

  it('falls back to null on a stored value missing a required field', () => {
    localStorage.setItem('creaturegame.activeGame', JSON.stringify({ gameId: 'abc123', level: 50 }));
    expect(loadActiveGame()).toBeNull();
  });
});
