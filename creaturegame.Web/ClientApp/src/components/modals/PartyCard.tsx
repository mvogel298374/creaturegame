import type { PartyMember } from '../../hooks/useBattleHub';
import { hpPercent, hpState } from '../../utils/hp';

// One selectable party member in a roster picker — the sprite, name, level and an HP read. Shared by the
// between-biome LeadChoiceModal and the mid-battle SwitchInModal, which offer the same roster for different
// reasons: `modifier` marks the card's state (current lead / fainted), `note` tails the level line, and
// `disabled` is what makes a downed member unpickable in the switch-in.
export function PartyCard({ member, onClick, disabled = false, modifier, note, title, current }: {
  member: PartyMember;
  onClick: () => void;
  disabled?: boolean;
  modifier?: string;
  note?: string;
  title?: string;
  current?: boolean;
}) {
  return (
    <button
      className={`lead-card${modifier ? ` ${modifier}` : ''}`}
      disabled={disabled}
      onClick={onClick}
      title={title}
      aria-current={current ? 'true' : undefined}
    >
      <img
        className="lead-card-sprite"
        src={`/sprites/front/${member.speciesId}.png`}
        alt={member.name}
        draggable={false}
        onError={e => { (e.currentTarget as HTMLImageElement).style.visibility = 'hidden'; }}
      />
      <span className="lead-card-name">{member.name}</span>
      <span className="lead-card-lvl">Lv{member.level}{note ?? ''}</span>
      <div className="lead-card-hp">
        <div
          className={`lead-card-hp-fill party-chip-hp-fill--${hpState(member.hp, member.maxHp)}`}
          style={{ width: `${hpPercent(member.hp, member.maxHp)}%` }}
        />
      </div>
    </button>
  );
}
