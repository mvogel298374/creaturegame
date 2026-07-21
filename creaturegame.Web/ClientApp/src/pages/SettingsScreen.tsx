import { useNavigate } from 'react-router-dom';
import { SettingsPanel } from '../components/SettingsPanel';
import './SettingsScreen.css';

export function SettingsScreen() {
  const nav = useNavigate();

  return (
    <div className="screen screen-grid settings-screen">
      <header className="settings-header">
        <button className="btn-ghost" onClick={() => nav(-1)}>← BACK</button>
        <h1 className="screen-title">SETTINGS</h1>
        <span className="settings-header-spacer" />
      </header>

      <div className="settings-body">
        <SettingsPanel />
      </div>
    </div>
  );
}
