import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { TypeBadge } from '../components/TypeBadge';
import type { Species } from '../types/Species';
import './BattleScreen.css';

interface CreatureState {
  speciesId: number;
  name: string;
  level: number;
  hp: number;
  maxHp: number;
  xp: number;
  xpToNext: number;
}

type ActionPhase = 'menu' | 'check';

// Gen 1 HP formula at level 50, no DVs/EVs — rough initial value until Phase 6 wires real data
function estimateHp(baseHp: number): number {
  return Math.floor((baseHp * 2 * 50) / 100) + 60;
}

export function BattleScreen() {
  const location = useLocation();
  const nav = useNavigate();
  const playerSpecies: Species | null = location.state?.species ?? null;

  const playerMaxHp = playerSpecies ? estimateHp(playerSpecies.baseHp) : 100;

  // Mock state — replaced by useBattleHub in Phase 6
  const player: CreatureState = {
    speciesId: playerSpecies?.id ?? 1,
    name: playerSpecies?.name.toUpperCase() ?? 'PLAYER',
    level: 50,
    hp: playerMaxHp,
    maxHp: playerMaxHp,
    xp: 0,
    xpToNext: 100,
  };

  const enemyMaxHp = estimateHp(39);
  const enemy: CreatureState = {
    speciesId: 4,
    name: 'CHARMANDER',
    level: 50,
    hp: enemyMaxHp,
    maxHp: enemyMaxHp,
    xp: 0,
    xpToNext: 100,
  };

  const [phase, setPhase] = useState<ActionPhase>('menu');

  return (
    <div className="battle-screen">
      <div className="battle-field">
        <div className="nameplate nameplate--enemy">
          <div className="nameplate-row">
            <span className="nameplate-name">{enemy.name}</span>
            <span className="nameplate-level">Lv{enemy.level}</span>
          </div>
          <HpBar hp={enemy.hp} maxHp={enemy.maxHp} />
        </div>

        <div className="sprite-slot sprite-slot--enemy">
          <img
            className="battle-sprite"
            src={`/sprites/front/${enemy.speciesId}.png`}
            alt={enemy.name}
          />
        </div>

        <div className="sprite-slot sprite-slot--player">
          <img
            className="battle-sprite battle-sprite--back"
            src={`/sprites/back/${player.speciesId}.png`}
            alt={player.name}
          />
        </div>

        <div className="nameplate nameplate--player">
          <div className="nameplate-row">
            <span className="nameplate-name">{player.name}</span>
            <span className="nameplate-level">Lv{player.level}</span>
          </div>
          <HpBar hp={player.hp} maxHp={player.maxHp} showNumbers />
          <XpBar xp={player.xp} xpToNext={player.xpToNext} />
        </div>
      </div>

      <div className="battle-panel">
        {phase === 'menu' && (
          <ActionMenu
            playerName={player.name}
            onFight={() => { /* Phase 6: show move list */ }}
            onCheck={() => setPhase('check')}
            onBack={() => nav('/')}
          />
        )}
        {phase === 'check' && (
          <CheckPanel
            player={player}
            playerSpecies={playerSpecies}
            onBack={() => setPhase('menu')}
          />
        )}
      </div>
    </div>
  );
}

function HpBar({ hp, maxHp, showNumbers = false }: {
  hp: number;
  maxHp: number;
  showNumbers?: boolean;
}) {
  const pct = maxHp > 0 ? Math.max(0, Math.min(100, (hp / maxHp) * 100)) : 0;
  const state = pct > 50 ? 'high' : pct > 25 ? 'mid' : 'low';

  return (
    <div className="hp-row">
      <span className="bar-label">HP</span>
      <div className="bar-track">
        <div className={`bar-fill bar-fill--${state}`} style={{ width: `${pct}%` }} />
      </div>
      {showNumbers && (
        <span className="hp-numbers">
          {hp}<span className="hp-sep">/</span>{maxHp}
        </span>
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

function ActionMenu({ playerName, onFight, onCheck, onBack }: {
  playerName: string;
  onFight: () => void;
  onCheck: () => void;
  onBack: () => void;
}) {
  return (
    <div className="action-menu">
      <p className="action-prompt">What will {playerName} do?</p>
      <div className="action-buttons">
        <button className="action-btn action-btn--fight" onClick={onFight}>
          FIGHT
        </button>
        <button className="action-btn" onClick={onCheck}>
          CHECK POKEMON
        </button>
      </div>
      <button className="btn-ghost action-back" onClick={onBack}>← QUIT</button>
    </div>
  );
}

const STAT_ROWS: Array<{ label: string; key: keyof Species }> = [
  { label: 'ATK', key: 'baseAttack' },
  { label: 'DEF', key: 'baseDefense' },
  { label: 'SPC', key: 'baseSpecial' },
  { label: 'SPD', key: 'baseSpeed' },
];

function CheckPanel({ player, playerSpecies, onBack }: {
  player: CreatureState;
  playerSpecies: Species | null;
  onBack: () => void;
}) {
  return (
    <div className="check-panel">
      <div className="check-header">
        <button className="btn-ghost" onClick={onBack}>← BACK</button>
        <span className="check-title">{player.name}</span>
        <span className="check-level">Lv{player.level}</span>
      </div>

      <div className="check-body">
        <div className="check-hp-row">
          <span className="check-stat-label">HP</span>
          <span className="check-hp-value">{player.hp} / {player.maxHp}</span>
        </div>

        {playerSpecies && (
          <div className="check-base-stats">
            {STAT_ROWS.map(({ label, key }) => {
              const val = playerSpecies[key] as number;
              return (
                <div className="check-stat-row" key={label}>
                  <span className="check-stat-label">{label}</span>
                  <div className="check-stat-track">
                    <div
                      className="check-stat-bar"
                      style={{ width: `${Math.min(100, (val / 155) * 100)}%` }}
                    />
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