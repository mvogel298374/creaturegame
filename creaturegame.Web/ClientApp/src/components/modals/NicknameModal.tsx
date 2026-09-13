import { useState } from 'react';
import { Modal } from './Modal';

// Nickname max length mirrors the backend's NicknameRules.MaxLength (creaturegame/Creatures/NicknameRules.cs)
// — Gen 1's own nickname-entry cap (Red/Blue).
const NICKNAME_MAX_LENGTH = 10;

// A cancelable "Do you want to give a nickname to X?" step, shared by every acquisition path (starter pick,
// themed draft, boss catch). Deliberately its own step *after* the accept/confirm decision and *before* the
// network call that carries it — so, like SettingsModal, nothing here parks a server-side await and it's safe
// to make escapable (Modal's `{ onEscape }` dismiss). Cancel, Escape, and OK-with-blank-text all resolve to
// "no nickname" (the caller normalizes via NicknameRules.Normalize either way) — mirroring Gen 1's own
// cancel-out-of-naming behavior, which keeps the species name rather than aborting the acquisition itself.
export function NicknameModal({ speciesName, onDone }: {
  speciesName: string;
  onDone: (nickname: string | null) => void;
}) {
  const [nickname, setNickname] = useState('');
  const cancel = () => onDone(null);
  const confirm = () => onDone(nickname.trim() || null);

  return (
    <Modal label="Nickname" dismiss={{ onEscape: cancel }} card="nickname-modal">
      <p className="nickname-title">Give a nickname?</p>
      <p className="nickname-sub">{speciesName}</p>
      <input
        className="nickname-input"
        type="text"
        value={nickname}
        onChange={e => setNickname(e.target.value)}
        onKeyDown={e => e.key === 'Enter' && confirm()}
        placeholder={speciesName}
        maxLength={NICKNAME_MAX_LENGTH}
        autoFocus
      />
      <div className="nickname-buttons">
        <button className="action-btn action-btn--fight" onClick={confirm}>OK</button>
        <button className="action-btn" onClick={cancel}>CANCEL</button>
      </div>
    </Modal>
  );
}
