import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import { loadSettings } from './utils/settings';
import { setMasterVolume } from './battle/AudioEngine';
import './index.css';

// Applies the persisted volume before any sound plays. setMasterVolume() only records the value until the
// AudioContext is actually created by the first sound, so this never trips the browser's autoplay-policy
// warning on load.
setMasterVolume(loadSettings().masterVolume);

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </React.StrictMode>
);
