import type { EvolutionPrompt } from '../../hooks/useBattleHub';
import { Modal } from './Modal';

// Evolution offer: a between-encounter Allow / Cancel step (Gen 1 B-cancel). Shows the current creature with
// an evolution glow; one press both answers the prompt and continues the run (the backend is blocked awaiting
// it). Cancel keeps the current form — it will be offered again at the next level-up.
export function EvolutionPromptModal({ prompt, onRespond }: {
  prompt: EvolutionPrompt;
  onRespond: (allow: boolean) => void;
}) {
  return (
    <Modal label="Evolution" dismiss="blocking" card="recovery-modal">
      <p className="recovery-title">Evolution</p>
      <p className="recovery-sub">{prompt.fromName} is evolving into {prompt.toName}!</p>
      <div className="recovery-sprite-wrap">
        <span className="recovery-glow" aria-hidden="true" />
        <img
          className="recovery-sprite"
          src={`/sprites/front/${prompt.fromSpeciesId}.png`}
          alt={prompt.fromName}
          draggable={false}
        />
      </div>
      <div className="recovery-buttons">
        <button className="action-btn action-btn--fight" onClick={() => onRespond(true)}>
          ALLOW
        </button>
        <button className="action-btn" onClick={() => onRespond(false)}>
          CANCEL
        </button>
      </div>
    </Modal>
  );
}
