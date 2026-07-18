import { useState } from 'react';
import type { MoveReplacementPrompt } from '../../hooks/useBattleHub';
import { formatMoveName } from '../../utils/format';
import { Modal } from './Modal';

// Level-up move learning: the four slots are full, so the player chooses one to forget for the new move —
// or declines. Two steps with a confirmation so a move is never deleted on a single misclick.
export function MoveReplacementModal({ prompt, onForget }: {
  prompt: MoveReplacementPrompt;
  onForget: (slot: number | null) => void;
}) {
  // null → choosing; { slot } → confirming that choice (slot null = confirming a decline).
  const [pending, setPending] = useState<{ slot: number | null } | null>(null);
  const newMove = formatMoveName(prompt.newMoveName);

  if (pending) {
    const declining = pending.slot === null;
    const question = declining
      ? `Stop learning ${newMove}?`
      : `Forget ${formatMoveName(prompt.currentMoves[pending.slot!])} and learn ${newMove}?`;
    return (
      <Modal label="Confirm move change" dismiss="blocking" card="move-replace-modal" overlay="modal-overlay--corner">
        <p className="move-replace-question">{question}</p>
        <div className="move-replace-confirm">
          <button className="action-btn action-btn--fight" onClick={() => onForget(pending.slot)}>YES</button>
          <button className="action-btn" onClick={() => setPending(null)}>NO</button>
        </div>
      </Modal>
    );
  }

  return (
    <Modal label="Choose a move to forget" dismiss="blocking" card="move-replace-modal" overlay="modal-overlay--corner">
      <p className="move-replace-title">{prompt.creatureName} wants to learn {newMove}!</p>
      <p className="move-replace-sub">But {prompt.creatureName} already knows 4 moves. Forget one?</p>
      <div className="move-replace-grid">
        {prompt.currentMoves.map((move, i) => (
          <button key={i} className="move-btn" onClick={() => setPending({ slot: i })}>
            <span className="move-name">{formatMoveName(move)}</span>
          </button>
        ))}
      </div>
      <button className="btn-ghost action-back" onClick={() => setPending({ slot: null })}>
        Don't learn {newMove}
      </button>
    </Modal>
  );
}
