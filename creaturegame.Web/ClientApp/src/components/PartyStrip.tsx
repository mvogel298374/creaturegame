import type { PartyMember } from '../hooks/useBattleHub';
import { hpPercent, hpState } from '../utils/hp';

// Party roster strip: the run's owned creatures as a compact row over the field. The active lead is flagged
// and pulled to the front; benched members show a small HP bar + level. Fed by PartyUpdated snapshots (and the
// /party hydrate on load). Read-only here — the between-biome lead swap (Stage 1d) is where the lead changes.
export function PartyStrip({ members }: { members: PartyMember[] }) {
  return (
    <div className="party-strip" role="group" aria-label="Party roster">
      {members.map((m, i) => (
        <div
          key={i}
          className={`party-chip${m.isLead ? ' party-chip--lead' : ''}`}
          title={`${m.name} · Lv${m.level} · ${m.hp}/${m.maxHp} HP`}
        >
          <img
            className="party-chip-sprite"
            src={`/sprites/front/${m.speciesId}.png`}
            alt={m.name}
            draggable={false}
            onError={e => { (e.currentTarget as HTMLImageElement).style.visibility = 'hidden'; }}
          />
          <span className="party-chip-lvl">Lv{m.level}</span>
          <div className="party-chip-hp">
            <div
              className={`party-chip-hp-fill party-chip-hp-fill--${hpState(m.hp, m.maxHp)}`}
              style={{ width: `${hpPercent(m.hp, m.maxHp)}%` }}
            />
          </div>
          {m.isLead && <span className="party-chip-tag" aria-label="lead">LEAD</span>}
        </div>
      ))}
    </div>
  );
}
