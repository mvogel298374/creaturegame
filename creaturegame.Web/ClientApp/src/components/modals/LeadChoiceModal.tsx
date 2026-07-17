import type { PartyMember } from '../../hooks/useBattleHub';
import { Modal } from './Modal';
import { PartyCard } from './PartyCard';

// Between-biome lead choice (Stage 1d): a blocking pick of which party member leads into the next biome. Shown
// at the biome boundary (after the Poké Center) when the party has more than one creature. Clicking a member
// makes it the lead (clicking the current lead keeps it — a no-op); the run is blocked server-side awaiting this.
// This is NOT in-battle switching — it only sets the lead for the next biome.
export function LeadChoiceModal({ party, onChoose }: {
  party: PartyMember[];
  onChoose: (index: number) => void;
}) {
  return (
    <Modal label="Choose your lead" dismiss="blocking" card="lead-modal">
      <p className="lead-title">Choose your lead</p>
      <p className="lead-sub">Who leads into the next biome?</p>
      <div className="lead-grid">
        {party.map((m, i) => (
          <PartyCard
            key={i}
            member={m}
            onClick={() => onChoose(i)}
            modifier={m.isLead ? 'lead-card--current' : undefined}
            note={m.isLead ? ' · current' : undefined}
            current={m.isLead}
          />
        ))}
      </div>
    </Modal>
  );
}
