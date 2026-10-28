import { Modal } from './Modal';
import './BattleEndedOverlay.css';

// No actions on purpose: a Continue from here would take the run back from the other window (R1d-c).
export function SessionTakenOverPanel() {
  return (
    <Modal label="Run open elsewhere" dismiss="blocking" card="battle-end-modal" overlay="battle-end-overlay">
      <p className="battle-end-title">RUN OPEN ELSEWHERE</p>
      <p className="battle-end-sub">This run is now open in another window.</p>
    </Modal>
  );
}
