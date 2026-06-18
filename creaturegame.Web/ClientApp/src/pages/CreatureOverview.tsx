import { useEffect, useState } from 'react';
import { TypeBadge } from '../components/TypeBadge';
import { formatMoveName } from '../utils/format';
import { friendlyFetchError } from '../utils/fetchError';
import type { PlayerOverview, StatRow, MoveRow } from '../types/PlayerOverview';
import './CreatureOverview.css';

type Tab = 'info' | 'stats' | 'moves';

/** The in-battle CHECK POKEMON overview: tabbed INFO / STATS / MOVES, fed by GET /api/game/{gameId}/player. */
export function CreatureOverview({ gameId, onBack }: { gameId: string | null; onBack: () => void }) {
  const [data, setData]   = useState<PlayerOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab]     = useState<Tab>('stats');

  useEffect(() => {
    if (!gameId) { setError('No active game.'); return; }
    let live = true;
    fetch(`/api/game/${gameId}/player`)
      .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); })
      .then((d: PlayerOverview) => { if (live) setData(d); })
      .catch(e => { if (live) setError(friendlyFetchError(e)); });
    return () => { live = false; };
  }, [gameId]);

  return (
    <div className="overview-panel">
      <div className="overview-header">
        <button className="btn-ghost" onClick={onBack}>← BACK</button>
        {data && <span className="overview-title">{data.name}</span>}
        {data && <span className="overview-sub">Lv{data.level} · #{String(data.speciesId).padStart(3, '0')}</span>}
      </div>

      {error && <p className="overview-error">{error}</p>}
      {!error && !data && <p className="overview-loading">Loading…</p>}

      {data && (
        <>
          <div className="overview-tabs">
            {(['info', 'stats', 'moves'] as Tab[]).map(t => (
              <button
                key={t}
                className={`overview-tab ${tab === t ? 'overview-tab--active' : ''}`}
                onClick={() => setTab(t)}
              >
                {t.toUpperCase()}
              </button>
            ))}
          </div>
          <div className="overview-body">
            {tab === 'info' && <InfoTab d={data} />}
            {tab === 'stats' && <StatsTab stats={data.stats} hp={data.hp} maxHp={data.maxHp} />}
            {tab === 'moves' && <MovesTab moves={data.moves} />}
          </div>
        </>
      )}
    </div>
  );
}

// Later-generation INFO fields, listed once with the generation that introduces them. Gated invisible until
// the active generation reaches them (so nothing shows in Gen 1) — the layout is ready, no empty rows today.
const GEN_FIELDS: Array<{ label: string; minGen: number; get: (d: PlayerOverview) => string | null }> = [
  { label: 'Held Item', minGen: 2, get: d => d.heldItem },
  { label: 'Ability', minGen: 3, get: d => d.ability },
  { label: 'Nature', minGen: 3, get: d => d.nature },
  { label: 'Tera Type', minGen: 9, get: d => d.teraType },
];

function InfoTab({ d }: { d: PlayerOverview }) {
  const xpPct = Math.min(100, (d.xpThisLevel / Math.max(1, d.xpToNextLevel)) * 100);
  const genFields = GEN_FIELDS.filter(f => d.generation >= f.minGen);
  return (
    <div className="overview-info">
      <div className="overview-identity">
        <img className="overview-sprite" src={`/sprites/front/${d.speciesId}.png`} alt={d.name} />
        <div className="overview-types">
          <TypeBadge type={d.type1} size="md" />
          {d.type2 && <TypeBadge type={d.type2} size="md" />}
        </div>
      </div>
      <div className="overview-fields">
        <div className="overview-field"><span>Status</span><span>{d.status === 'None' ? 'OK' : d.status}</span></div>
        <div className="overview-field"><span>HP</span><span>{d.hp} / {d.maxHp}</span></div>
        <div className="overview-field"><span>Exp. Points</span><span>{d.xpThisLevel} / {d.xpToNextLevel}</span></div>
        <div className="overview-xp-track"><div className="overview-xp-bar" style={{ width: `${xpPct}%` }} /></div>
        <div className="overview-field"><span>Base Stat Total</span><span>{d.baseStatTotal}</span></div>
        {genFields.map(f => (
          <div className="overview-field" key={f.label}><span>{f.label}</span><span>{f.get(d) ?? '—'}</span></div>
        ))}
      </div>
    </div>
  );
}

// Full stat names read nicer than the wire abbreviations. Gen 1 keeps a single Special (no Sp.Atk/Sp.Def).
const STAT_NAMES: Record<string, string> = {
  HP: 'HP',
  ATK: 'Attack',
  DEF: 'Defense',
  SPC: 'Special',
  SPD: 'Speed',
};

function StatsTab({ stats, hp, maxHp }: { stats: StatRow[]; hp: number; maxHp: number }) {
  // No magnitude bars: the stats don't share a scale, so a value/cap bar wouldn't compare anything. Each row
  // is the stat name, the actual value (HP as current/max), and its DV + gained EV as small chips.
  return (
    <div className="overview-stats">
      {stats.map(s => {
        const isHp = s.label === 'HP';
        return (
          <div className="overview-stat-row" key={s.label}>
            <span className="overview-stat-name">{STAT_NAMES[s.label] ?? s.label}</span>
            <span className="overview-stat-value">{isHp ? `${hp} / ${maxHp}` : s.value}</span>
            <span className="overview-stat-chips">
              <span className="overview-chip overview-chip--dv">DV {s.dv}</span>
              <span className="overview-chip overview-chip--ev">EV {s.statExp.toLocaleString()}</span>
            </span>
          </div>
        );
      })}
    </div>
  );
}

function MovesTab({ moves }: { moves: MoveRow[] }) {
  if (moves.length === 0) return <p className="overview-loading">No moves.</p>;
  return (
    <div className="overview-moves">
      {moves.map((m, i) => (
        <div className="overview-move" key={i}>
          <div className="overview-move-top">
            <span className="overview-move-name">{formatMoveName(m.name)}</span>
            <TypeBadge type={m.type} size="sm" />
          </div>
          <div className="overview-move-meta">
            <span>{m.category}</span>
            <span>PWR {m.power > 0 ? m.power : '—'}</span>
            <span>ACC {m.accuracy > 0 ? m.accuracy : '—'}</span>
            <span>PP {m.ppCurrent}/{m.ppMax}</span>
          </div>
          {m.description && <p className="overview-move-desc">{m.description}</p>}
        </div>
      ))}
    </div>
  );
}
