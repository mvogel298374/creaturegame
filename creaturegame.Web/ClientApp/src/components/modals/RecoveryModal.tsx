import type { RecoveryPrompt } from '../../hooks/useBattleHub';
import { Modal } from './Modal';

// Roguelite Poké Center: a between-encounter heal step. Shows the player's creature with a heal glow and
// offers a single Heal / Skip press — that one input both decides the heal and continues the chain (the
// backend is blocked awaiting it). Skipping leaves the creature as it was.
export function RecoveryModal({ prompt, onRespond }: {
  prompt: RecoveryPrompt;
  onRespond: (accept: boolean) => void;
}) {
  return (
    <Modal label="Poké Center recovery" dismiss="blocking" card="recovery-modal">
      <p className="recovery-title">Poké Center</p>
      <p className="recovery-sub">Your whole party can be fully healed.</p>
      <div className="recovery-sprite-wrap">
        <span className="recovery-glow" aria-hidden="true" />
        <img
          className="recovery-sprite"
          src={`/sprites/front/${prompt.speciesId}.png`}
          alt={prompt.creatureName}
          draggable={false}
        />
      </div>
      <div className="recovery-buttons">
        <button className="action-btn action-btn--fight" onClick={() => onRespond(true)}>
          HEAL
        </button>
        <button className="action-btn" onClick={() => onRespond(false)}>
          SKIP
        </button>
      </div>
    </Modal>
  );
}
