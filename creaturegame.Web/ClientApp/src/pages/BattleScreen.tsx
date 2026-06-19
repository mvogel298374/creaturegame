import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { TypeBadge } from '../components/TypeBadge';
import { BattleCanvas } from '../battle/BattleCanvas';
import { useBattleHub, type LevelUpPanel, type MoveReplacementPrompt, type RecoveryPrompt, type EvolutionPrompt } from '../hooks/useBattleHub';
import type { Species } from '../types/Species';
import type { MoveInfo } from '../types/BattleEvents';
import { formatMoveName } from '../utils/format';
import { CreatureOverview } from './CreatureOverview';
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

  const { state, chooseMove, dismissLevelUp, forgetMove, respondRecovery, respondEvolution } = useBattleHub(gameId, startLevel);
  const [controlView, setControlView] = useState<ControlView>('menu');
  const logRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (logRef.current)
      logRef.current.scrollTop = logRef.current.scrollHeight;
  }, [state.log]);

  // The level-up panel stays up until the player does anything — clear it on the first interaction.
  const onAnyInput = () => { if (state.levelUp) dismissLevelUp(); };

  const handleChooseMove = (index: number) => {
    onAnyInput();
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
          {state.log.map((entry, i) => (
            <p key={i} className={`log-line${entry.tone ? ` log-line--${entry.tone}` : ''}`}>{entry.message}</p>
          ))}
          {state.phase === 'connecting' && (
            <p className="log-line log-line--muted">Connecting…</p>
          )}
        </div>

        <div className="battle-controls">
          {controlView === 'menu' && state.phase !== 'ended' && (
            <ActionMenu
              playerName={playerName}
              canFight={state.phase === 'choosing' && !state.animating}
              onFight={() => { onAnyInput(); setControlView('fight'); }}
              onCheck={() => { onAnyInput(); setControlView('check'); }}
              onBack={() => { onAnyInput(); nav('/'); }}
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
            <CreatureOverview gameId={gameId} onBack={() => setControlView('menu')} />
          )}
        </div>
      </div>

      {state.moveReplacement && (
        <MoveReplacementModal prompt={state.moveReplacement} onForget={forgetMove} />
      )}

      {state.recovery && (
        <RecoveryModal prompt={state.recovery} onRespond={respondRecovery} />
      )}

      {state.evolution && (
        <EvolutionPromptModal prompt={state.evolution} onRespond={respondEvolution} />
      )}

      {state.phase === 'ended' && (
        <BattleEndedOverlay
          creatureName={playerName}
          speciesId={playerSpecies?.id ?? 1}
          battlesWon={state.battlesWon}
          finalLevel={state.playerLevel}
          onPlayAgain={() => nav('/select')}
          onQuit={() => nav('/')}
        />
      )}
    </div>
  );
}

// Run-scoped game-over screen for the Endless Battle Chain: shown once the player's creature faints and the
// run ends (driven by the terminal RunEnded event → phase 'ended'). Not a per-battle overlay — a win is just
// an intermission in the chain, so this only appears at the run's true end. Summarises the run (creature,
// battles won, final level) over a greyed faint sprite and offers PLAY AGAIN (a fresh starter pick) or QUIT.
function BattleEndedOverlay({ creatureName, speciesId, battlesWon, finalLevel, onPlayAgain, onQuit }: {
  creatureName: string;
  speciesId: number;
  battlesWon: number;
  finalLevel: number;
  onPlayAgain: () => void;
  onQuit: () => void;
}) {
  return (
    <div className="modal-overlay battle-end-overlay">
      <div className="battle-end-modal" role="alertdialog" aria-label="Game over">
        <p className="battle-end-title">GAME OVER</p>
        <div className="battle-end-sprite-wrap">
          <img
            className="battle-end-sprite"
            src={`/sprites/front/${speciesId}.png`}
            alt={creatureName}
            draggable={false}
          />
        </div>
        <p className="battle-end-sub">{creatureName} fainted.</p>
        <table className="battle-end-stats">
          <tbody>
            <tr>
              <td className="battle-end-stat">BATTLES WON</td>
              <td className="battle-end-value">{battlesWon}</td>
            </tr>
            <tr>
              <td className="battle-end-stat">FINAL LEVEL</td>
              <td className="battle-end-value">Lv{finalLevel}</td>
            </tr>
          </tbody>
        </table>
        <div className="battle-end-buttons">
          <button className="action-btn action-btn--fight" onClick={onPlayAgain}>PLAY AGAIN</button>
          <button className="action-btn" onClick={onQuit}>QUIT</button>
        </div>
      </div>
    </div>
  );
}

// Roguelite Poké Center: a between-encounter heal step. Shows the player's creature with a heal glow and
// offers a single Heal / Skip press — that one input both decides the heal and continues the chain (the
// backend is blocked awaiting it). Skipping leaves the creature as it was.
function RecoveryModal({ prompt, onRespond }: {
  prompt: RecoveryPrompt;
  onRespond: (accept: boolean) => void;
}) {
  return (
    <div className="modal-overlay">
      <div className="recovery-modal" role="alertdialog" aria-label="Poké Center recovery">
        <p className="recovery-title">Poké Center</p>
        <p className="recovery-sub">{prompt.creatureName} can be fully healed.</p>
        <div className="recovery-sprite-wrap">
          <span className="recovery-glow" aria-hidden="true" />
          <img
            className="recovery-sprite"
            src={`/sprites/front/${prompt.speciesId}.png`}
            alt={prompt.creatureName}
            draggable={false}
          />
        </div>
        <div className="recovery-buttons">
          <button className="action-btn action-btn--fight" onClick={() => onRespond(true)}>
            HEAL
          </button>
          <button className="action-btn" onClick={() => onRespond(false)}>
            SKIP
          </button>
        </div>
      </div>
    </div>
  );
}

// Evolution offer: a between-encounter Allow / Cancel step (Gen 1 B-cancel). Shows the current creature with
// an evolution glow; one press both answers the prompt and continues the run (the backend is blocked awaiting
// it). Cancel keeps the current form — it will be offered again at the next level-up.
function EvolutionPromptModal({ prompt, onRespond }: {
  prompt: EvolutionPrompt;
  onRespond: (allow: boolean) => void;
}) {
  return (
    <div className="modal-overlay">
      <div className="recovery-modal" role="alertdialog" aria-label="Evolution">
        <p className="recovery-title">Evolution</p>
        <p className="recovery-sub">{prompt.fromName} is evolving into {prompt.toName}!</p>
        <div className="recovery-sprite-wrap">
          <span className="recovery-glow" aria-hidden="true" />
          <img
            className="recovery-sprite"
            src={`/sprites/front/${prompt.fromSpeciesId}.png`}
            alt={prompt.fromName}
            draggable={false}
          />
        </div>
        <div className="recovery-buttons">
          <button className="action-btn action-btn--fight" onClick={() => onRespond(true)}>
            ALLOW
          </button>
          <button className="action-btn" onClick={() => onRespond(false)}>
            CANCEL
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────────

// Level-up move learning: the four slots are full, so the player chooses one to forget for the new move —
// or declines. Two steps with a confirmation so a move is never deleted on a single misclick.
function MoveReplacementModal({ prompt, onForget }: {
  prompt: MoveReplacementPrompt;
  onForget: (slot: number | null) => void;
}) {
  // null → choosing; { slot } → confirming that choice (slot null = confirming a decline).
  const [pending, setPending] = useState<{ slot: number | null } | null>(null);
  const newMove = formatMoveName(prompt.newMoveName);

  if (pending) {
    const declining = pending.slot === null;
    const question = declining
      ? `Stop learning ${newMove}?`
      : `Forget ${formatMoveName(prompt.currentMoves[pending.slot!])} and learn ${newMove}?`;
    return (
      <div className="modal-overlay modal-overlay--corner">
        <div className="move-replace-modal" role="alertdialog" aria-label="Confirm move change">
          <p className="move-replace-question">{question}</p>
          <div className="move-replace-confirm">
            <button className="action-btn action-btn--fight" onClick={() => onForget(pending.slot)}>YES</button>
            <button className="action-btn" onClick={() => setPending(null)}>NO</button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="modal-overlay modal-overlay--corner">
      <div className="move-replace-modal" role="alertdialog" aria-label="Choose a move to forget">
        <p className="move-replace-title">{prompt.creatureName} wants to learn {newMove}!</p>
        <p className="move-replace-sub">But it already knows 4 moves. Forget one?</p>
        <div className="move-replace-grid">
          {prompt.currentMoves.map((move, i) => (
            <button key={i} className="move-btn" onClick={() => setPending({ slot: i })}>
              <span className="move-name">{formatMoveName(move)}</span>
            </button>
          ))}
        </div>
        <button className="btn-ghost action-back" onClick={() => setPending({ slot: null })}>
          Don't learn {newMove}
        </button>
      </div>
    </div>
  );
}

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

function ActionMenu({ playerName, canFight, onFight, onCheck, onBack }: {
  playerName: string;
  canFight: boolean;
  onFight: () => void;
  onCheck: () => void;
  onBack: () => void;
}) {
  return (
    <div className="action-menu">
      <p className="action-prompt">What will {playerName} do?</p>
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
      <button className="btn-ghost action-back" onClick={onBack}>← QUIT</button>
    </div>
  );
}

// The ×N type-effectiveness pill for a move vs the current enemy. Neutral (1×) and non-damaging moves
// (effectiveness 1) show nothing — the engine sends 1.0 for both. Gen 1 multipliers are exact in IEEE-754
// (0, ¼, ½, 1, 2, 4), so direct equality is safe.
function effectivenessPill(eff: number | undefined): { label: string; cls: string } | null {
  switch (eff) {
    case 4:    return { label: '×4', cls: 'move-eff--x4' };
    case 2:    return { label: '×2', cls: 'move-eff--x2' };
    case 0.5:  return { label: '×0.5', cls: 'move-eff--half' };
    case 0.25: return { label: '×0.25', cls: 'move-eff--quarter' };
    case 0:    return { label: '×0', cls: 'move-eff--zero' };
    default:   return null; // 1× neutral, non-damaging, or undefined → no pill
  }
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
          const isStab = !isEmpty && !!move.stab;            // same-type damaging move → STAB bonus
          const eff = isEmpty ? null : effectivenessPill(move.effectiveness); // type matchup vs enemy
          return (
            <button
              key={i}
              className={`move-btn ${disabled ? 'move-btn--disabled' : ''} ${isStab ? 'move-btn--stab' : ''}`}
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
              {isStab && <span className="move-stab" aria-label="STAB">STAB</span>}
              {eff && (
                <span className={`move-eff ${eff.cls}`} aria-label={`effectiveness ${eff.label}`}>
                  {eff.label}
                </span>
              )}
            </button>
          );
        })}
      </div>
      <button className="btn-ghost action-back" onClick={onBack}>← BACK</button>
    </div>
  );
}

