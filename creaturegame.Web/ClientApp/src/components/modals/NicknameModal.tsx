import { useState } from 'react';
import { Modal } from './Modal';
import './NicknameModal.css';

// Nickname max length mirrors the backend's NicknameRules.MaxLength (creaturegame/Creatures/NicknameRules.cs)
// — Gen 1's own nickname-entry cap (Red/Blue).
const NICKNAME_MAX_LENGTH = 10;

// The "Give a nickname?" step after every accept; Cancel, Escape (B) and blank OK all mean no nickname.
export function NicknameModal({ speciesName, onDone }: {
  speciesName: string;
  onDone: (nickname: string | null) => void;
}) {
  const [nickname, setNickname] = useState('');
  const cancel = () => onDone(null);
  const confirm = () => onDone(nickname.trim() || null);

  return (
    <Modal label="Nickname" dismiss={{ onBKey: cancel }} card="nickname-modal">
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
