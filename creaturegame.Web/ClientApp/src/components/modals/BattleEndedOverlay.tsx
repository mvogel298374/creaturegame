import { Modal } from './Modal';

// Run-scoped game-over screen for the Endless Battle Chain: shown once the player's creature faints and the
// run ends (driven by the terminal RunEnded event → phase 'ended'). Not a per-battle overlay — a win is just
// an intermission in the chain, so this only appears at the run's true end. Summarises the run (creature,
// battles won, final level) over a greyed faint sprite and offers PLAY AGAIN (a fresh starter pick) or QUIT.
export function BattleEndedOverlay({ creatureName, speciesId, battlesWon, finalLevel, onPlayAgain, onQuit }: {
  creatureName: string;
  speciesId: number;
  battlesWon: number;
  finalLevel: number;
  onPlayAgain: () => void;
  onQuit: () => void;
}) {
  return (
    <Modal label="Game over" dismiss="blocking" card="battle-end-modal" overlay="battle-end-overlay">
      <p className="battle-end-title">GAME OVER</p>
      <div className="battle-end-sprite-wrap">
        <img
          className="battle-end-sprite"
          src={`/sprites/front/${speciesId}.png`}
          alt={creatureName}
          draggable={false}
        />
      </div>
      <p className="battle-end-sub">{creatureName} fainted.</p>
      <table className="battle-end-stats">
        <tbody>
          <tr>
            <td className="battle-end-stat">BATTLES WON</td>
            <td className="battle-end-value">{battlesWon}</td>
          </tr>
          <tr>
            <td className="battle-end-stat">FINAL LEVEL</td>
            <td className="battle-end-value">Lv{finalLevel}</td>
          </tr>
        </tbody>
      </table>
      <div className="battle-end-buttons">
        <button className="action-btn action-btn--fight" onClick={onPlayAgain}>PLAY AGAIN</button>
        <button className="action-btn" onClick={onQuit}>QUIT</button>
      </div>
    </Modal>
  );
}
