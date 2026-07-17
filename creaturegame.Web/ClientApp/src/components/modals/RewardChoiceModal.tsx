import type { RewardChoicePrompt } from '../../hooks/useBattleHub';
import { healSummary } from '../../battle/timeline';
import { formatItemName } from '../../battle/bag';
import { Modal } from './Modal';

// Reward choice: a pick-one-of-N shown after a rolled reward — two rarity-coloured item cards and a gold bag.
// One click picks that option (the backend is blocked awaiting the pick) and the chosen reward is then applied
// + announced by the drop hover. A required choice: the run always offers at least the gold bag, so there is
// no empty/decline state.
export function RewardChoiceModal({ prompt, onChoose }: {
  prompt: RewardChoicePrompt;
  onChoose: (index: number) => void;
}) {
  return (
    <Modal label="Choose your reward" dismiss="blocking" card="reward-modal">
      <p className="reward-title">Choose your reward</p>
      <p className="reward-sub">Pick one — the rest are left behind.</p>
      <div className="reward-cards">
        {prompt.options.map((option, i) => (
          <button
            key={i}
            className={
              option.kind === 'gold'
                ? 'reward-card reward-card--gold'
                : option.kind === 'heal'
                  ? 'reward-card reward-card--heal'
                  : `reward-card reward-card--item reward-card--${(option.rarity ?? 'Common').toLowerCase()}`
            }
            onClick={() => onChoose(i)}
          >
            {option.kind === 'gold' ? (
              <>
                <span className="reward-card-icon" aria-hidden="true">₽</span>
                <span className="reward-card-name">{option.gold}₽</span>
                <span className="reward-card-tag">GOLD BAG</span>
              </>
            ) : option.kind === 'heal' ? (
              <>
                <span className="reward-card-icon" aria-hidden="true">✚</span>
                <span className="reward-card-name">{option.label ?? 'Quick Heal'}</span>
                <span className="reward-card-tag">{healSummary(option)}</span>
              </>
            ) : (
              <>
                <span className="reward-card-icon" aria-hidden="true">✦</span>
                <span className="reward-card-name">{formatItemName(option.itemName ?? '')}</span>
                <span className="reward-card-tag">{(option.rarity ?? 'Common').toUpperCase()}</span>
              </>
            )}
          </button>
        ))}
      </div>
    </Modal>
  );
}
