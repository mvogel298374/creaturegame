import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import { loadSettings } from './utils/settings';
import { setMasterVolume } from './battle/AudioEngine';
import { applyGenerationTheme } from './generations/presentation';
import './index.css';

// Applies the persisted volume before any sound plays. setMasterVolume() only records the value until the
// AudioContext is actually created by the first sound, so this never trips the browser's autoplay-policy
// warning on load.
setMasterVolume(loadSettings().masterVolume);

// Boot in the default generation's theme (Generation Profile Stage 4a): pre-run screens (Title, starter
// picker) have no run to read a generation from, so the document starts on the default; BattleScreen
// re-stamps it from the run's route state / server echo, and the future picker live-previews over it.
applyGenerationTheme(null);

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </React.StrictMode>
);
