import { useEffect, useState } from 'react';
import { loadSettings, SETTINGS_CHANGED_EVENT } from '../utils/settings';

// Whether the server permits Dev Mode (GET /api/dev/status). Asked once per page load and shared; a failed
// request reads as "not available" — the safe answer.
let serverStatus: Promise<boolean> | null = null;
function fetchDevAvailable(): Promise<boolean> {
  serverStatus ??= fetch('/api/dev/status')
    .then(r => (r.ok ? r.json() : { enabled: false }))
    .then(d => d.enabled === true)
    .catch(() => false);
  return serverStatus;
}

// `available`: the server allows Dev Mode (gates whether Settings shows the toggle).
// `enabled`: available AND the viewer switched it on — what dev-only UI should key off.
export function useDevMode() {
  const [available, setAvailable] = useState(false);
  const [toggled, setToggled] = useState(() => loadSettings().devMode);

  useEffect(() => {
    let live = true;
    fetchDevAvailable().then(a => { if (live) setAvailable(a); });
    const sync = () => setToggled(loadSettings().devMode);
    window.addEventListener(SETTINGS_CHANGED_EVENT, sync);
    return () => { live = false; window.removeEventListener(SETTINGS_CHANGED_EVENT, sync); };
  }, []);

  return { available, enabled: available && toggled };
}
