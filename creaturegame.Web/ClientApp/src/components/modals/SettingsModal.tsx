import { SettingsPanel } from '../SettingsPanel';
import { Modal } from './Modal';
import './SettingsModal.css';

// The in-battle settings entry. Unlike every other prompt in this module, nothing here parks a server-side
// await — it only edits a client-local preference — so it's the first real caller of Modal's escapable
// dismiss (closing costs nothing; see Modal.tsx). Deliberately a modal over BattleScreen, not a page nav: a
// page nav would unmount BattleScreen and tear down its live SignalR connection mid-run (the reconnect path
// resumes the transport but doesn't replay the accumulated battle state back into a fresh component).
export function SettingsModal({ onClose }: { onClose: () => void }) {
  return (
    <Modal label="Settings" dismiss={{ onEscape: onClose }} card="settings-modal">
      <p className="settings-modal-title">Settings</p>
      <SettingsPanel />
      <button className="settings-modal-close-btn" onClick={onClose}>Close</button>
    </Modal>
  );
}
