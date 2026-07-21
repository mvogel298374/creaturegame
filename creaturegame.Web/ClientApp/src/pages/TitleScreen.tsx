import { useNavigate } from 'react-router-dom';
import './TitleScreen.css';

export function TitleScreen() {
  const nav = useNavigate();
  return (
    <div className="title-screen">
      <button
        className="settings-gear-btn"
        onClick={() => nav('/settings')}
        aria-label="Settings"
      >
        ⚙
      </button>
      <div className="title-content">
        <div className="title-logo">CREATURE<span className="title-logo-accent">GAME</span></div>
        <div className="title-subtitle">GEN 1 BATTLE SIMULATOR</div>
        <button className="btn-new-game" onClick={() => nav('/select')}>
          ▶ NEW GAME
        </button>
      </div>
      <div className="title-footer">Press NEW GAME to begin your adventure</div>
    </div>
  );
}
