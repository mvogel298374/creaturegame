// Client-only user preferences, persisted to localStorage. There's no save.db / player-account concept yet
// (docs/TODO.md — Game Loop), so this is deliberately a browser-local preference, not run/account state.

export interface Settings {
  masterVolume: number; // 0–1
  // Per-viewer opt-in to Dev Mode. Only a request — the server flag (GET /api/dev/status) is the authority and
  // every dev endpoint re-checks it (docs/TODO.md — Dev Mode).
  devMode: boolean;
}

// Fired after every save so mounted views (the HUD's DEV badge) can follow a toggle made in the Settings modal.
export const SETTINGS_CHANGED_EVENT = 'creaturegame:settings-changed';

const STORAGE_KEY = 'creaturegame.settings';
const DEFAULT_SETTINGS: Settings = { masterVolume: 1, devMode: false };

export function loadSettings(): Settings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { ...DEFAULT_SETTINGS };
    const parsed = JSON.parse(raw);
    const masterVolume = typeof parsed.masterVolume === 'number'
      ? Math.min(1, Math.max(0, parsed.masterVolume))
      : DEFAULT_SETTINGS.masterVolume;
    const devMode = parsed.devMode === true;
    return { masterVolume, devMode };
  } catch {
    return { ...DEFAULT_SETTINGS };
  }
}

export function saveSettings(settings: Settings): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  } catch {
    // Storage unavailable (private browsing / quota) — the setting just won't outlive this session.
  }
  if (typeof window !== 'undefined') window.dispatchEvent(new Event(SETTINGS_CHANGED_EVENT));
}
