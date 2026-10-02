import { describe, it, expect, beforeEach } from 'vitest';
import { loadSettings, saveSettings } from './settings';

// This project's Vitest config runs in the plain 'node' environment (no jsdom dependency), so there's no
// built-in Storage API — a minimal in-memory stand-in is enough to exercise the pure load/save logic without
// pulling in a new test dependency.
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

describe('settings', () => {
  it('defaults to full volume when nothing is stored', () => {
    expect(loadSettings()).toEqual({ masterVolume: 1, devMode: false });
  });

  it('round-trips a saved volume', () => {
    saveSettings({ masterVolume: 0.4, devMode: false });
    expect(loadSettings()).toEqual({ masterVolume: 0.4, devMode: false });
  });

  it('round-trips the dev-mode toggle, defaulting off', () => {
    saveSettings({ masterVolume: 1, devMode: true });
    expect(loadSettings().devMode).toBe(true);

    localStorage.setItem('creaturegame.settings', JSON.stringify({ masterVolume: 1 }));
    expect(loadSettings().devMode).toBe(false);

    localStorage.setItem('creaturegame.settings', JSON.stringify({ masterVolume: 1, devMode: 'yes' }));
    expect(loadSettings().devMode).toBe(false);
  });

  it('clamps an out-of-range stored value', () => {
    localStorage.setItem('creaturegame.settings', JSON.stringify({ masterVolume: 5 }));
    expect(loadSettings().masterVolume).toBe(1);

    localStorage.setItem('creaturegame.settings', JSON.stringify({ masterVolume: -2 }));
    expect(loadSettings().masterVolume).toBe(0);
  });

  it('falls back to the default on corrupt JSON', () => {
    localStorage.setItem('creaturegame.settings', 'not json');
    expect(loadSettings()).toEqual({ masterVolume: 1, devMode: false });
  });
});
