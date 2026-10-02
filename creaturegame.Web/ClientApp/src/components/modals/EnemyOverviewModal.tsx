import { CreatureOverview } from '../../pages/CreatureOverview';
import { Modal } from './Modal';
import './EnemyOverviewModal.css';

// Dev Mode: the CHECK POKEMON sheet for the current foe (docs/TODO.md — Dev Mode). A read-only modal like
// SettingsModal — nothing here parks a server-side await, so Escape can dismiss it freely.
export function EnemyOverviewModal({ gameId, onClose }: { gameId: string | null; onClose: () => void }) {
  return (
    <Modal label="Enemy overview" dismiss={{ onEscape: onClose }} card="enemy-overview-modal">
      <CreatureOverview gameId={gameId} party={[]} onBack={onClose} enemy />
    </Modal>
  );
}
