// The lightweight resume feature (ARCHITECTURE.md §2.7 — Session Resume): persists just enough of a run's nav state
// to `localStorage` for BattleScreen to fall back to when react-router state is gone (a hard refresh, a closed/
// reopened tab, or a bookmarked /battle URL) and for TitleScreen to offer a Continue button. Deliberately the
// *same* shape StarterSelection already passes as nav state, so a load from here needs no special-casing on the
// reading end. No `save.db` / player-account concept yet — same browser-local scope as `utils/settings.ts`.

import type { Species } from '../types/Species';

export interface ActiveGame {
  gameId: string;
  species: Species;
  level: number;
  generation: string;
  savedAt: number; // epoch ms — informational only; the connection attempt itself is the staleness check
}

const STORAGE_KEY = 'creaturegame.activeGame';

export function saveActiveGame(game: Omit<ActiveGame, 'savedAt'>): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...game, savedAt: Date.now() }));
  } catch {
    // Storage unavailable (private browsing / quota) — resume just won't survive a reload this session.
  }
}

export function loadActiveGame(): ActiveGame | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (
      typeof parsed !== 'object' || parsed === null
      || typeof parsed.gameId !== 'string' || !parsed.gameId
      || typeof parsed.species !== 'object' || parsed.species === null
      || typeof parsed.level !== 'number'
      || typeof parsed.generation !== 'string'
    ) return null;
    return parsed as ActiveGame;
  } catch {
    return null;
  }
}

export function clearActiveGame(): void {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Nothing to do — worst case a stale entry lingers until the next failed resume clears it.
  }
}
