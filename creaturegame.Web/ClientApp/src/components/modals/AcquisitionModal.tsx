import { useState } from 'react';
import type { AcquisitionPrompt } from '../../hooks/useBattleHub';
import { TypeBadge } from '../TypeBadge';
import { Modal } from './Modal';
import './AcquisitionModal.css';
import { NicknameModal } from './NicknameModal';

type Phase = 'offer' | 'swap' | { slot: number } | 'naming';

// B steps back one phase; on the offer it declines (null). The naming step escapes through NicknameModal.
export function acquisitionOnB(phase: Exclude<Phase, 'naming'>): Phase | null {
  if (phase === 'offer') return null;
  return phase === 'swap' ? 'offer' : 'swap';
}

// Acquisition offer (themed draft / boss catch): a modal to add the offered creature to the party.
// With room, it's a simple ACCEPT / DECLINE. When the party is full, ACCEPT opens a "release which member?"
// step over the benched members (the lead is excluded — a mid-biome lead change is Stage 1d) with a two-step
// confirm so no creature is released on a single misclick. Either accept path then opens the nickname step
// (Creature Naming Stage B) before finally answering RespondAcquisition, which the run loop is blocked on —
// same "nickname is its own cancelable step after the decision" shape as the starter path (StarterSelection).
export function AcquisitionModal({ prompt, onRespond }: {
  prompt: AcquisitionPrompt;
  onRespond: (accept: boolean, replaceSlot: number | null, nickname: string | null) => void;
}) {
  // null → the offer; 'swap' → picking a member to release (full party); { slot } → confirming that release;
  // 'naming' → the nickname step, the final one before onRespond fires.
  const [phase, setPhase] = useState<Phase>('offer');
  // The slot to swap (full-party accept) or null (open-slot accept) — captured when entering 'naming' so it
  // survives to the final onRespond call.
  const [pendingSlot, setPendingSlot] = useState<number | null>(null);
  const label = prompt.source === 'BossCatch' ? 'Catch!' : 'A creature wants to join!';

  if (phase === 'naming') {
    return (
      <NicknameModal
        speciesName={prompt.name}
        onDone={nickname => onRespond(true, pendingSlot, nickname)}
      />
    );
  }

  const step = phase;
  const onB = () => {
    const next = acquisitionOnB(step);
    if (next === null) onRespond(false, null, null);
    else setPhase(next);
  };

  // Full-party release confirm.
  if (typeof phase === 'object') {
    const releasing = prompt.party[phase.slot];
    return (
      <Modal label="Confirm release" dismiss={{ onBKey: onB }} card="acquire-modal">
        <p className="acquire-question">Release {releasing.name} to make room for {prompt.name}?</p>
        <div className="acquire-buttons">
          <button
            className="action-btn action-btn--fight"
            onClick={() => { setPendingSlot(phase.slot); setPhase('naming'); }}
          >YES</button>
          <button className="action-btn" onClick={() => setPhase('swap')}>NO</button>
        </div>
      </Modal>
    );
  }

  // Full-party swap picker: choose a benched member to release (the lead can't be swapped out here).
  if (phase === 'swap') {
    return (
      <Modal label="Choose a member to release" dismiss={{ onBKey: onB }} card="acquire-modal">
        <p className="acquire-title">Party is full!</p>
        <p className="acquire-sub">Release which creature for {prompt.name}?</p>
        <div className="acquire-swap-grid">
          {prompt.party.map((m, i) => (
            <button
              key={i}
              className="acquire-swap-btn"
              disabled={m.isLead}
              onClick={() => setPhase({ slot: i })}
              title={m.isLead ? 'The lead cannot be released here' : undefined}
            >
              <img
                className="acquire-swap-sprite"
                src={`/sprites/front/${m.speciesId}.png`}
                alt={m.name}
                draggable={false}
                onError={e => { (e.currentTarget as HTMLImageElement).style.visibility = 'hidden'; }}
              />
              <span className="acquire-swap-name">{m.name}</span>
              <span className="acquire-swap-lvl">Lv{m.level}{m.isLead ? ' · LEAD' : ''}</span>
            </button>
          ))}
        </div>
        <button className="btn-ghost action-back" onClick={() => setPhase('offer')}>← BACK</button>
      </Modal>
    );
  }

  // The offer itself.
  return (
    <Modal label="Creature offer" dismiss={{ onBKey: onB }} card="acquire-modal">
      <p className="acquire-title">{label}</p>
      <p className="acquire-sub">{prompt.name} (Lv{prompt.level}) wants to join your party!</p>
      <div className="acquire-sprite-wrap">
        <span className="acquire-glow" aria-hidden="true" />
        <img
          className="acquire-sprite"
          src={`/sprites/front/${prompt.speciesId}.png`}
          alt={prompt.name}
          draggable={false}
        />
      </div>
      <div className="acquire-types" aria-hidden="true">
        {prompt.types.map(t => <TypeBadge key={t} type={t} size="sm" />)}
      </div>
      <div className="acquire-buttons">
        <button
          className="action-btn action-btn--fight"
          onClick={() => {
            if (prompt.partyFull) { setPhase('swap'); return; }
            setPendingSlot(null);
            setPhase('naming');
          }}
        >
          {prompt.partyFull ? 'ADD (SWAP)' : 'ADD'}
        </button>
        <button className="action-btn" onClick={() => onRespond(false, null, null)}>DECLINE</button>
      </div>
    </Modal>
  );
}
