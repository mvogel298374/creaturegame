import type { SwitchInPrompt } from '../../hooks/useBattleHub';
import { Modal } from './Modal';
import { PartyCard } from './PartyCard';

// Forced faint-switch (Stage 3): the active creature fainted but the bench has a live member, so the player MUST
// send in a replacement — a blocking, non-dismissable modal (no decline/close) over the roster. Only live members
// are choosable; the fainted one (and any other downed member) is greyed and disabled. Clicking a live member
// answers RespondSwitchIn, which the battle is blocked on; the run ends only if the whole party is down (in which
// case this modal never opens). Distinct from the between-biome LeadChoiceModal — this is a mid-battle send-in.
export function SwitchInModal({ prompt, onChoose }: {
  prompt: SwitchInPrompt;
  onChoose: (index: number) => void;
}) {
  return (
    <Modal label="Send in a creature" dismiss="blocking" card="lead-modal">
      <p className="lead-title">{prompt.faintedName} fainted!</p>
      <p className="lead-sub">Send in your next creature.</p>
      <div className="lead-grid">
        {prompt.party.map((m, i) => {
          const fainted = m.hp <= 0;
          return (
            <PartyCard
              key={i}
              member={m}
              onClick={() => onChoose(i)}
              disabled={fainted}
              modifier={fainted ? 'lead-card--fainted' : undefined}
              note={fainted ? ' · FNT' : undefined}
              title={fainted ? `${m.name} has fainted` : undefined}
            />
          );
        })}
      </div>
    </Modal>
  );
}
