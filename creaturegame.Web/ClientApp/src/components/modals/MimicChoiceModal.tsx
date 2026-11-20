import type { MimicChoicePrompt } from '../../hooks/useBattleHub';
import { formatMoveName } from '../../utils/format';
import { Modal } from './Modal';
import './MoveReplacementModal.css';

// The player's Mimic (Gen 1 core.asm .mimicmenu): the foe's move names only, and no cancel.
export function MimicChoiceModal({ prompt, onChoose }: {
  prompt: MimicChoicePrompt;
  onChoose: (index: number) => void;
}) {
  return (
    <Modal label="Choose a move to copy" dismiss="blocking" card="move-replace-modal" overlay="modal-overlay--corner">
      <p className="move-replace-title">Which technique?</p>
      <div className="move-replace-grid">
        {prompt.moves.map((move, i) => (
          <button key={i} className="move-btn" onClick={() => onChoose(i)}>
            <span className="move-name">{formatMoveName(move)}</span>
          </button>
        ))}
      </div>
    </Modal>
  );
}
