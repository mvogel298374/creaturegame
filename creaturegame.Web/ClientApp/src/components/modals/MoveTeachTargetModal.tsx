import { useState } from 'react';
import type { MoveTeachTargetPrompt } from '../../hooks/useBattleHub';
import { formatMoveName } from '../../utils/format';
import { TypeBadge } from '../TypeBadge';
import { Modal } from './Modal';
import { PartyCard } from './PartyCard';
import './RosterPicker.css';

// TM/HM — Move-Teach Rewards: a "teach {move} to a Pokémon?" screen raised after the player picks a moveTeach
// reward card. A deliberate roguelite QoL improvement over the source games, NOT a literal Gen 1 reproduction —
// real Gen 1 lets you pick any party member and only tells you afterward if it can't learn the move; this
// flags ABLE/NOT ABLE up front instead. Every current party member is shown; only an ABLE one (a real
// Machine-learnset match that isn't already known) is selectable. Declining asks "Don't teach X?" first: the
// reward was already spent on pick, so a decline forfeits the teach (FRONTEND_PLAN.md → Dismiss).
export function MoveTeachTargetModal({ prompt, onChoose }: {
  prompt: MoveTeachTargetPrompt;
  onChoose: (index: number | null) => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const move = formatMoveName(prompt.moveName);
  const onB = () => setConfirming(!confirming);

  if (confirming) {
    return (
      <Modal label="Confirm skipping the teach" dismiss={{ onBKey: onB }} card="lead-modal">
        <p className="lead-title">Don't teach {move}?</p>
        <div className="lead-confirm">
          <button className="action-btn action-btn--fight" onClick={() => onChoose(null)}>YES</button>
          <button className="action-btn" onClick={() => setConfirming(false)}>NO</button>
        </div>
      </Modal>
    );
  }

  return (
    <Modal label="Teach a move" dismiss={{ onBKey: onB }} card="lead-modal">
      <p className="lead-title">Teach {move}?</p>
      <p className="lead-sub">
        <TypeBadge type={prompt.damageType} />
        {' '}
        {prompt.power > 0 ? `PWR ${prompt.power} · ` : ''}ACC {prompt.accuracy} · PP {prompt.pp}
      </p>
      <div className="lead-grid">
        {prompt.candidates.map((c, i) => {
          // Gen 1 allows teaching a TM/HM to a fainted Pokémon, so a fainted-but-able member stays selectable
          // (unlike SwitchInModal, which disables fainted members for a different reason — sending one INTO
          // battle). Flagged with its own note/title rather than SwitchInModal's disabled "fainted" styling, so
          // a clickable fainted card never reads as broken/disabled.
          const fainted = c.hp <= 0;
          const note = !c.able ? ' · Unable' : fainted ? ' · FNT' : undefined;
          const title = !c.able
            ? `${c.name} can't learn this move`
            : fainted
              ? `${c.name} has fainted — it can still learn a move`
              : undefined;
          return (
            <PartyCard
              key={i}
              member={{
                speciesId: c.speciesId,
                id: c.id,
                name: c.name,
                level: c.level,
                hp: c.hp,
                maxHp: c.maxHp,
                status: c.status,
                isLead: false,
              }}
              onClick={() => onChoose(i)}
              disabled={!c.able}
              note={note}
              title={title}
            />
          );
        })}
      </div>
      <button className="btn-ghost action-back" onClick={() => setConfirming(true)}>
        Don't teach {move}
      </button>
    </Modal>
  );
}
