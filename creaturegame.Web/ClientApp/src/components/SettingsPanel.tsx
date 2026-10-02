import { useState } from 'react';
import { loadSettings, saveSettings } from '../utils/settings';
import { setMasterVolume } from '../battle/AudioEngine';
import { useDevMode } from '../hooks/useDevMode';
import './SettingsPanel.css';

// The settings content itself, shared between the full-page /settings route (reached from the Title Screen)
// and the in-battle SettingsModal (reached mid-run) — the two entry points differ only in chrome, never in
// what's actually being edited.
export function SettingsPanel() {
  const [volume, setVolume] = useState(() => loadSettings().masterVolume);
  const [devMode, setDevMode] = useState(() => loadSettings().devMode);
  // The toggle only exists when the server permits Dev Mode — on the deployed app it never renders.
  const { available: devAvailable } = useDevMode();

  const onVolumeChange = (v: number) => {
    setVolume(v);
    setMasterVolume(v);
    saveSettings({ ...loadSettings(), masterVolume: v });
  };

  const onDevModeChange = (on: boolean) => {
    setDevMode(on);
    saveSettings({ ...loadSettings(), devMode: on });
  };

  return (
    <div className="settings-panel">
      <div className="settings-row">
        <span className="settings-label">SOUND VOLUME</span>
        <input
          className="settings-slider"
          type="range"
          min={0}
          max={100}
          step={1}
          value={Math.round(volume * 100)}
          onChange={e => onVolumeChange(Number(e.target.value) / 100)}
          aria-label="Sound volume"
        />
        <span className="settings-value">{Math.round(volume * 100)}%</span>
      </div>
      {devAvailable && (
        <div className="settings-row">
          <span className="settings-label">DEV MODE</span>
          <input
            type="checkbox"
            checked={devMode}
            onChange={e => onDevModeChange(e.target.checked)}
            aria-label="Dev mode"
          />
        </div>
      )}
    </div>
  );
}
