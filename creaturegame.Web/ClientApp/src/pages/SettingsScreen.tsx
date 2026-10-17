import { useLocation, useNavigate } from 'react-router-dom';
import { SettingsPanel } from '../components/SettingsPanel';
import './SettingsScreen.css';

export function SettingsScreen() {
  const nav = useNavigate();
  // 'default' marks the entry the app was opened on: there is no in-app page to go back to.
  const back = useLocation().key === 'default' ? () => nav('/') : () => nav(-1);

  return (
    <div className="screen screen-grid settings-screen">
      <header className="settings-header">
        <button className="btn-ghost" onClick={back}>← BACK</button>
        <h1 className="screen-title">SETTINGS</h1>
        <span className="settings-header-spacer" />
      </header>

      <div className="settings-body">
        <SettingsPanel />
      </div>
    </div>
  );
}
