import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { loadActiveGame, type ActiveGame } from '../utils/activeGame';
import './TitleScreen.css';

export function TitleScreen() {
  const nav = useNavigate();
  const location = useLocation();
  // Session Resume (ARCHITECTURE.md §2.7): offers a way back into a run that survives closing the tab entirely, not
  // just a refresh of /battle — read fresh on every mount, since a run started/ended elsewhere in the SPA
  // (StarterSelection, or BattleScreen clearing it on QUIT/end) can change this between visits to Title.
  const [continueGame, setContinueGame] = useState<ActiveGame | null>(null);
  useEffect(() => { setContinueGame(loadActiveGame()); }, []);
  // A failed-resume bounce (useBattleHub's conn.start() rejection) lands here with a one-shot notice —
  // dismissed on the first interaction with the page, same lifetime rule as the battle level-up panel.
  const [notice, setNotice] = useState<string | null>((location.state as { notice?: string } | null)?.notice ?? null);

  return (
    <div className="title-screen" onClick={() => setNotice(null)}>
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
        {notice && <p className="title-notice" role="status">{notice}</p>}
        {continueGame && (
          <button
            className="btn-continue"
            onClick={() => nav('/battle', { state: continueGame })}
          >
            ▶ CONTINUE — {continueGame.species.name.toUpperCase()} (Lv {continueGame.level})
          </button>
        )}
        <button className="btn-new-game" onClick={() => nav('/select')}>
          ▶ NEW GAME
        </button>
      </div>
      <div className="title-footer">Press NEW GAME to begin your adventure</div>
    </div>
  );
}
