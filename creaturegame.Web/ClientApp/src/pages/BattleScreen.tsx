import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { TypeBadge } from '../components/TypeBadge';
import { BattleCanvas } from '../battle/BattleCanvas';
import { useBattleHub, type LevelUpPanel } from '../hooks/useBattleHub';
import type { Species } from '../types/Species';
import type { MoveInfo } from '../types/BattleEvents';
import { formatMoveName } from '../utils/format';
import './BattleScreen.css';

// Gen 1 HP estimate at level 50, no DVs/EVs — used until first TurnStarted arrives
function estimateHp(baseHp: number): number {
  return Math.floor((baseHp * 2 * 50) / 100) + 60;
}

type ControlView = 'menu' | 'fight' | 'check';

export function BattleScreen() {
  const location = useLocation();
  const nav = useNavigate();
  const playerSpecies: Species | null = location.state?.species ?? null;
  const gameId: string | null = location.state?.gameId ?? null;
  const startLevel: number = location.state?.level ?? 50;

  const { state, chooseMove } = useBattleHub(gameId, startLevel);
  const [controlView, setControlView] = useState<ControlView>('menu');
  const logRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (logRef.current)
      logRef.current.scrollTop = logRef.current.scrollHeight;
  }, [state.log]);

  const handleChooseMove = (index: number) => {
    chooseMove(index);
    setControlView('menu');
  };

  // Fall back to estimated values until the first TurnStarted arrives
  const playerMaxHp = state.playerMaxHp > 1 ? state.playerMaxHp
    : (playerSpecies ? estimateHp(playerSpecies.baseHp) : 100);
  const playerHp    = state.playerMaxHp > 1 ? state.playerHp : playerMaxHp;
  const enemyMaxHp  = state.enemyMaxHp  > 1 ? state.enemyMaxHp  : estimateHp(39);
  const enemyHp     = state.enemyMaxHp  > 1 ? state.enemyHp     : enemyMaxHp;

  const playerName  = state.playerName || (playerSpecies?.name.toUpperCase() ?? 'PLAYER');
  const enemyName   = state.enemyName  || '???';
  const enemyLevel  = state.enemyLevel || '?';

  return (
    <div className="battle-screen">
      <div className="battle-field">
        <div className="nameplate nameplate--enemy">
          <div className="nameplate-row">
            <span className="nameplate-name">{enemyName}</span>
            <span className="nameplate-level">Lv{enemyLevel}</span>
          </div>
          <HpBar hp={enemyHp} maxHp={enemyMaxHp} />
          <StatusBadge status={state.enemyStatus} />
        </div>

        {state.enemySpeciesId > 0 && (
          <BattleCanvas
            playerSpeciesId={playerSpecies?.id ?? 1}
            enemySpeciesId={state.enemySpeciesId}
          />
        )}

        <div className="nameplate nameplate--player">
          <div className="nameplate-row">
            <span className="nameplate-name">{playerName}</span>
            <span className="nameplate-level">Lv{state.playerLevel}</span>
          </div>
          <HpBar hp={playerHp} maxHp={playerMaxHp} showNumbers />
          <StatusBadge status={state.playerStatus} />
          <XpBar xp={state.playerXp} xpToNext={state.playerXpToNext} />
        </div>

        {state.levelUp && <LevelUpStatPanel panel={state.levelUp} />}
      </div>

      <div className="battle-panel">
        <div className="battle-log" ref={logRef}>
          {state.log.map((msg, i) => (
            <p key={i} className="log-line">{msg}</p>
          ))}
          {state.phase === 'connecting' && (
            <p className="log-line log-line--muted">Connecting…</p>
          )}
        </div>

        <div className="battle-controls">
          {controlView === 'menu' && (
            <ActionMenu
              playerName={playerName}
              canFight={state.phase === 'choosing' && !state.animating}
              battleEnded={state.phase === 'ended'}
              battlesWon={state.battlesWon}
              finalLevel={state.playerLevel}
              onFight={() => setControlView('fight')}
              onCheck={() => setControlView('check')}
              onBack={() => nav('/')}
            />
          )}
          {controlView === 'fight' && (
            <MoveMenu
              moves={state.moves}
              canChoose={state.phase === 'choosing' && !state.animating}
              onChoose={handleChooseMove}
              onBack={() => setControlView('menu')}
            />
          )}
          {controlView === 'check' && (
            <CheckPanel
              playerName={playerName}
              playerHp={playerHp}
              playerMaxHp={playerMaxHp}
              playerLevel={state.playerLevel}
              playerSpecies={playerSpecies}
              onBack={() => setControlView('menu')}
            />
          )}
        </div>
      </div>
    </div>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────────

function HpBar({ hp, maxHp, showNumbers = false }: {
  hp: number; maxHp: number; showNumbers?: boolean;
}) {
  const pct   = maxHp > 0 ? Math.max(0, Math.min(100, (hp / maxHp) * 100)) : 0;
  const state = pct > 50 ? 'high' : pct > 25 ? 'mid' : 'low';
  return (
    <div className="hp-row">
      <span className="bar-label">HP</span>
      <div className="bar-track">
        <div className={`bar-fill bar-fill--${state}`} style={{ width: `${pct}%` }} />
      </div>
      {showNumbers && (
        <span className="hp-numbers">{hp}<span className="hp-sep">/</span>{maxHp}</span>
      )}
    </div>
  );
}

function XpBar({ xp, xpToNext }: { xp: number; xpToNext: number }) {
  const pct = xpToNext > 0 ? Math.max(0, Math.min(100, (xp / xpToNext) * 100)) : 0;
  return (
    <div className="xp-row">
      <span className="bar-label">XP</span>
      <div className="bar-track">
        <div className="bar-fill bar-fill--xp" style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

// Gen 1 level-up box: the stat names with the gain (+N) from this level and the new total.
function LevelUpStatPanel({ panel }: { panel: LevelUpPanel }) {
  const { gains, totals } = panel;
  const rows: [string, number, number][] = [
    ['HP', gains.maxHp, totals.maxHp],
    ['ATTACK', gains.attack, totals.attack],
    ['DEFENSE', gains.defense, totals.defense],
    ['SPECIAL', gains.special, totals.special],
    ['SPEED', gains.speed, totals.speed],
  ];
  return (
    <div className="levelup-panel" role="status" aria-label={`Level ${panel.level}`}>
      <div className="levelup-title">LEVEL UP! · Lv {panel.level}</div>
      <table className="levelup-table">
        <tbody>
          {rows.map(([label, gain, total]) => (
            <tr key={label}>
              <td className="levelup-stat">{label}</td>
              <td className="levelup-gain">+{gain}</td>
              <td className="levelup-total">{total}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  if (!status || status === 'None') return null;
  const label =
    status === 'Paralysis'  ? 'PAR'    :
    status === 'BadPoison'  ? 'BADPSN' :
    status.slice(0, 3).toUpperCase();
  return (
    <span className={`status-badge status-badge--${status.toLowerCase()}`}>
      {label}
    </span>
  );
}

function ActionMenu({ playerName, canFight, battleEnded, battlesWon, finalLevel, onFight, onCheck, onBack }: {
  playerName: string;
  canFight: boolean;
  battleEnded: boolean;
  battlesWon: number;
  finalLevel: number;
  onFight: () => void;
  onCheck: () => void;
  onBack: () => void;
}) {
  return (
    <div className="action-menu">
      <p className="action-prompt">
        {battleEnded
          ? `Game over — ${battlesWon} win${battlesWon === 1 ? '' : 's'}, reached Lv${finalLevel}.`
          : `What will ${playerName} do?`}
      </p>
      {!battleEnded && (
        <div className="action-buttons">
          <button
            className={`action-btn action-btn--fight ${!canFight ? 'action-btn--waiting' : ''}`}
            onClick={onFight}
            disabled={!canFight}
          >
            FIGHT
          </button>
          <button className="action-btn" onClick={onCheck}>
            CHECK POKEMON
          </button>
        </div>
      )}
      <button className="btn-ghost action-back" onClick={onBack}>← QUIT</button>
    </div>
  );
}

function MoveMenu({ moves, canChoose, onChoose, onBack }: {
  moves: MoveInfo[];
  canChoose: boolean;
  onChoose: (index: number) => void;
  onBack: () => void;
}) {
  const slots = [...moves];
  while (slots.length < 4) slots.push({ name: '---', type: 'Normal', ppCurrent: 0, ppMax: 0 });

  return (
    <div className="move-menu">
      <div className="move-grid">
        {slots.map((move, i) => {
          const isEmpty = move.name === '---';
          const outOfPp = !isEmpty && move.ppCurrent <= 0;
          const isDisabled = !isEmpty && !!move.disabled;   // locked out by Disable
          const disabled = !canChoose || isEmpty || outOfPp || isDisabled;
          return (
            <button
              key={i}
              className={`move-btn ${disabled ? 'move-btn--disabled' : ''}`}
              disabled={disabled}
              onClick={() => onChoose(i)}
            >
              <span className="move-name">{formatMoveName(move.name)}</span>
              {!isEmpty && (
                <span className={`move-pp ${outOfPp ? 'move-pp--low' : ''}`}>
                  {move.ppCurrent}/{move.ppMax}
                </span>
              )}
              {!isEmpty && <TypeBadge type={move.type} size="sm" />}
            </button>
          );
        })}
      </div>
      <button className="btn-ghost action-back" onClick={onBack}>← BACK</button>
    </div>
  );
}

const STAT_ROWS: Array<{ label: string; key: keyof Species }> = [
  { label: 'ATK', key: 'baseAttack' },
  { label: 'DEF', key: 'baseDefense' },
  { label: 'SPC', key: 'baseSpecial' },
  { label: 'SPD', key: 'baseSpeed' },
];

function CheckPanel({ playerName, playerHp, playerMaxHp, playerLevel, playerSpecies, onBack }: {
  playerName: string;
  playerHp: number;
  playerMaxHp: number;
  playerLevel: number;
  playerSpecies: Species | null;
  onBack: () => void;
}) {
  return (
    <div className="check-panel">
      <div className="check-header">
        <button className="btn-ghost" onClick={onBack}>← BACK</button>
        <span className="check-title">{playerName}</span>
        <span className="check-level">Lv{playerLevel}</span>
      </div>
      <div className="check-body">
        <div className="check-hp-row">
          <span className="check-stat-label">HP</span>
          <span className="check-hp-value">{playerHp} / {playerMaxHp}</span>
        </div>
        {playerSpecies && (
          <div className="check-base-stats">
            {STAT_ROWS.map(({ label, key }) => {
              const val = playerSpecies[key] as number;
              return (
                <div className="check-stat-row" key={label}>
                  <span className="check-stat-label">{label}</span>
                  <div className="check-stat-track">
                    <div className="check-stat-bar" style={{ width: `${Math.min(100, (val / 155) * 100)}%` }} />
                  </div>
                  <span className="check-stat-value">{val}</span>
                </div>
              );
            })}
          </div>
        )}
        {playerSpecies && (
          <div className="check-types">
            <TypeBadge type={playerSpecies.type1} size="md" />
            {playerSpecies.type2 && <TypeBadge type={playerSpecies.type2} size="md" />}
          </div>
        )}
      </div>
    </div>
  );
}