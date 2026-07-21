import { useState } from 'react';
import { loadSettings, saveSettings } from '../utils/settings';
import { setMasterVolume } from '../battle/AudioEngine';
import './SettingsPanel.css';

// The settings content itself, shared between the full-page /settings route (reached from the Title Screen)
// and the in-battle SettingsModal (reached mid-run) — the two entry points differ only in chrome, never in
// what's actually being edited.
export function SettingsPanel() {
  const [volume, setVolume] = useState(() => loadSettings().masterVolume);

  const onVolumeChange = (v: number) => {
    setVolume(v);
    setMasterVolume(v);
    saveSettings({ masterVolume: v });
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
    </div>
  );
}
