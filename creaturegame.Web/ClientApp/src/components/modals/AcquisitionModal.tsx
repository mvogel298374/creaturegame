import { useState } from 'react';
import type { AcquisitionPrompt } from '../../hooks/useBattleHub';
import { TypeBadge } from '../TypeBadge';
import { Modal } from './Modal';

// Acquisition offer (themed draft / boss catch): a blocking modal to add the offered creature to the party.
// With room, it's a simple ACCEPT / DECLINE. When the party is full, ACCEPT opens a "release which member?"
// step over the benched members (the lead is excluded — a mid-biome lead change is Stage 1d) with a two-step
// confirm so no creature is released on a single misclick. That one flow answers RespondAcquisition, which the
// run loop is blocked on.
export function AcquisitionModal({ prompt, onRespond }: {
  prompt: AcquisitionPrompt;
  onRespond: (accept: boolean, replaceSlot: number | null) => void;
}) {
  // null → the offer; 'swap' → picking a member to release (full party); { slot } → confirming that release.
  const [phase, setPhase] = useState<'offer' | 'swap' | { slot: number }>('offer');
  const label = prompt.source === 'BossCatch' ? 'Catch!' : 'A creature wants to join!';

  // Full-party release confirm.
  if (typeof phase === 'object') {
    const releasing = prompt.party[phase.slot];
    return (
      <Modal label="Confirm release" dismiss="blocking" card="acquire-modal">
        <p className="acquire-question">Release {releasing.name} to make room for {prompt.name}?</p>
        <div className="acquire-buttons">
          <button className="action-btn action-btn--fight" onClick={() => onRespond(true, phase.slot)}>YES</button>
          <button className="action-btn" onClick={() => setPhase('swap')}>NO</button>
        </div>
      </Modal>
    );
  }

  // Full-party swap picker: choose a benched member to release (the lead can't be swapped out here).
  if (phase === 'swap') {
    return (
      <Modal label="Choose a member to release" dismiss="blocking" card="acquire-modal">
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
    <Modal label="Creature offer" dismiss="blocking" card="acquire-modal">
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
          onClick={() => (prompt.partyFull ? setPhase('swap') : onRespond(true, null))}
        >
          {prompt.partyFull ? 'ADD (SWAP)' : 'ADD'}
        </button>
        <button className="action-btn" onClick={() => onRespond(false, null)}>DECLINE</button>
      </div>
    </Modal>
  );
}
