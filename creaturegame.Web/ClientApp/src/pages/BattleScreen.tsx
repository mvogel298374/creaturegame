import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { TypeBadge, typeColor } from '../components/TypeBadge';
import { MapGlyphSprite, TypeChip, typeIconId, nodeIconId } from './mapGlyphs';
import { BattleCanvas } from '../battle/BattleCanvas';
import { useBattleHub, type LevelUpPanel, type DropToast } from '../hooks/useBattleHub';
import { useEscapeKey } from '../hooks/useEscapeKey';
import { type RegionBiome, type BiomeOption } from '../battle/timeline';
import { regionEdgeKey, travelledEdgeKeys } from '../battle/regionMap';
import { powerPill } from '../battle/movePower';
import { bossTrainerName } from '../battle/bossTrainer';
import type { Species } from '../types/Species';
import type { MoveInfo } from '../types/BattleEvents';
import { formatMoveName } from '../utils/format';
import { friendlyFetchError } from '../utils/fetchError';
import { type BagItem, groupBagItems, needsMoveTarget, formatItemName } from '../battle/bag';
import { PartyStrip } from '../components/PartyStrip';
import { Modal } from '../components/modals/Modal';
import { BattleEndedOverlay } from '../components/modals/BattleEndedOverlay';
import { RecoveryModal } from '../components/modals/RecoveryModal';
import { EvolutionPromptModal } from '../components/modals/EvolutionPromptModal';
import { RewardChoiceModal } from '../components/modals/RewardChoiceModal';
import { ShopModal } from '../components/modals/ShopModal';
import { AcquisitionModal } from '../components/modals/AcquisitionModal';
import { LeadChoiceModal } from '../components/modals/LeadChoiceModal';
import { SwitchInModal } from '../components/modals/SwitchInModal';
import { MoveReplacementModal } from '../components/modals/MoveReplacementModal';
import { CreatureOverview } from './CreatureOverview';
import './BattleScreen.css';

// Gen 1 HP estimate at level 50, no DVs/EVs — used until first TurnStarted arrives
function estimateHp(baseHp: number): number {
  return Math.floor((baseHp * 2 * 50) / 100) + 60;
}

type ControlView = 'menu' | 'fight' | 'bag' | 'check';

// How long a battle-drop hover stays on the field before auto-dismissing (kept in sync with the CSS
// drop-toast animation duration). ~2.8s: long enough to read the loot, short enough not to stall the run.
const DROP_TOAST_MS = 2800;

// How long the encounter map auto-peeks after a ladder change (a node advancing / a new biome plan) before it
// fades back — unless the player has pinned it open. Long enough to register the move, short enough not to nag.
const MAP_PEEK_MS = 2600;

export function BattleScreen() {
  const location = useLocation();
  const nav = useNavigate();
  const playerSpecies: Species | null = location.state?.species ?? null;
  const gameId: string | null = location.state?.gameId ?? null;
  const startLevel: number = location.state?.level ?? 50;

  const { state, chooseMove, useItem, dismissLevelUp, forgetMove, respondRecovery, respondEvolution, chooseBiome, chooseReward, buyShopItem, leaveShop, respondAcquisition, chooseLead, respondSwitchIn, dismissDrop } = useBattleHub(gameId, startLevel);
  const [controlView, setControlView] = useState<ControlView>('menu');
  // Encounter-map overlay: pinned open by the MAP button, and briefly auto-peeked at each ladder change.
  const [mapPinned, setMapPinned] = useState(false);
  const [mapPeek, setMapPeek] = useState(false);
  const logRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (logRef.current)
      logRef.current.scrollTop = logRef.current.scrollHeight;
  }, [state.log]);

  // Auto-dismiss the battle-drop hover after its on-screen beat. A fresh drop replaces the state object, so
  // this re-runs and restarts the timer; clearing on unmount/replace prevents a stale timer hiding a new toast.
  useEffect(() => {
    if (!state.dropToast) return;
    const t = window.setTimeout(dismissDrop, DROP_TOAST_MS);
    return () => window.clearTimeout(t);
  }, [state.dropToast, dismissDrop]);

  // Auto-peek the encounter map whenever the ladder changes (a node advances or a new biome plan arrives) so the
  // player sees the run move, then fade it back — unless it's pinned open. A fresh pin/plan re-runs and restarts
  // the timer; clearing on change prevents a stale timer hiding a later peek. Skipped in the legacy chain (no plan).
  useEffect(() => {
    if (state.mapNodePlan.length === 0) return;
    setMapPeek(true);
    const t = window.setTimeout(() => setMapPeek(false), MAP_PEEK_MS);
    return () => window.clearTimeout(t);
  }, [state.mapPin, state.mapNodePlan]);

  // The level-up panel stays up until the player does anything — clear it on the first interaction.
  const onAnyInput = () => { if (state.levelUp) dismissLevelUp(); };

  const handleChooseMove = (index: number) => {
    onAnyInput();
    chooseMove(index);
    setControlView('menu');
  };

  const handleUseItem = (itemId: number, targetMoveSlot: number | null) => {
    onAnyInput();
    useItem(itemId, targetMoveSlot);
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
      <MapGlyphSprite />
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

        {/* Bottom-right corner stack: the party roster sits directly ABOVE the player's nameplate/HP bar, so the
            two never overlap at any window scale (a flex column auto-adjusts to the nameplate's height). */}
        <div className="player-corner">
          {/* Party roster strip — the run's owned creatures (shown once the party grows past the lone starter via a
              themed draft). The lead is flagged; benched members show a compact HP read. Single non-wrapping row so
              its footprint stays constant at a max of 6 chips. */}
          {state.party.length > 1 && <PartyStrip members={state.party} />}
          <div className="nameplate nameplate--player">
            <div className="nameplate-row">
              <span className="nameplate-name">{playerName}</span>
              <span className="nameplate-level">Lv{state.playerLevel}</span>
            </div>
            <HpBar hp={playerHp} maxHp={playerMaxHp} showNumbers />
            <StatusBadge status={state.playerStatus} />
            <XpBar xp={state.playerXp} xpToNext={state.playerXpToNext} />
          </div>
        </div>

        {state.levelUp && <LevelUpStatPanel panel={state.levelUp} />}

        {state.dropToast && <DropHover drop={state.dropToast} />}

        {/* Encounter map (biome mode only — the legacy chain has no region graph). The MAP button pins it open; it
            also auto-peeks at each ladder change. Presentation over the run — the route is fixed and logic-driven. */}
        {state.regionBiomes.length > 0 && (
          <>
            <button
              className={`map-toggle-btn${mapPinned ? ' map-toggle-btn--on' : ''}`}
              onClick={() => setMapPinned(o => !o)}
              aria-pressed={mapPinned}
              aria-label="Toggle route map"
            >
              MAP
            </button>
            {(mapPinned || mapPeek) && (
              <RunMapPanel
                biomes={state.regionBiomes}
                routePath={state.routePath}
                currentId={state.currentBiomeId}
                biomeName={state.mapBiomeName}
                nodePlan={state.mapNodePlan}
                pin={state.mapPin}
                pinned={mapPinned}
                onClose={() => setMapPinned(false)}
              />
            )}
          </>
        )}
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
              canAct={state.phase === 'choosing' && !state.animating}
              onFight={() => { onAnyInput(); setControlView('fight'); }}
              onBag={() => { onAnyInput(); setControlView('bag'); }}
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
          {controlView === 'bag' && (
            <BagMenu
              gameId={gameId}
              gold={state.gold}
              moves={state.moves}
              onUse={handleUseItem}
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

      {state.biomeChoice && (
        <RouteChoiceMap
          biomes={state.regionBiomes}
          routePath={state.routePath}
          currentId={state.currentBiomeId}
          options={state.biomeChoice.options}
          onChoose={chooseBiome}
        />
      )}

      {state.rewardChoice && (
        <RewardChoiceModal prompt={state.rewardChoice} onChoose={chooseReward} />
      )}

      {state.shop && (
        <ShopModal prompt={state.shop} onBuy={buyShopItem} onLeave={leaveShop} />
      )}

      {state.acquisition && (
        <AcquisitionModal prompt={state.acquisition} onRespond={respondAcquisition} />
      )}

      {state.leadChoice && (
        <LeadChoiceModal party={state.leadChoice} onChoose={chooseLead} />
      )}

      {state.switchIn && (
        <SwitchInModal prompt={state.switchIn} onChoose={respondSwitchIn} />
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

// Stable empty set for the read-only region map (no offered/choosable waypoints), so RunMapPanel doesn't mint a
// new Set each render.
const EMPTY_ID_SET: ReadonlySet<string> = new Set<string>();

// Per-node label + sub-label for the encounter-map ladder. Keys are the backend RunNodeKind names plus the
// client-synthesized 'Rest' cap; the drawn icon comes from nodeIconId (a shared vector glyph, no longer text).
const LADDER_NODE_META: Record<string, { label: string; sub: string }> = {
  WildBattle:  { label: 'Wild Battle',  sub: 'Encounter' },
  EliteBattle: { label: 'Elite Battle', sub: 'Tough foe' },
  BossBattle:  { label: 'Boss',         sub: 'Gate boss' },
  Shop:        { label: 'Shop',         sub: 'Spend gold' },
  Treasure:    { label: 'Treasure',     sub: 'Free reward' },
  Mystery:     { label: 'Mystery',      sub: '???' },
  Rest:        { label: 'Poké Center',  sub: 'Rest & heal' },
};

// The encounter-map ladder (Phase 2): the current biome's route drawn as a vertical Slay-the-Spire-style path —
// one node per revealed RunNodeKind (the Boss its apex), capped by a synthesized Poké Center 'Rest' (which isn't
// a plan node — see ENCOUNTER_DESIGN.md §5). The pin marks the node in progress; earlier nodes read as done,
// later as upcoming. CSS column-reverse puts node 0 at the bottom so the player climbs upward to the apex.
// The current biome's route drawn as a vertical Slay-the-Spire-style ladder — one node per revealed RunNodeKind
// (the Boss its apex), capped by a synthesized Poké Center 'Rest' (not a plan node — see ENCOUNTER_DESIGN.md §5).
// The pin marks the node in progress; earlier nodes read done, later upcoming. CSS column-reverse puts node 0 at
// the bottom so the player climbs upward to the apex. Presentation only — the route is fixed and logic-driven.
function NodeLadder({ nodePlan, pin, bossSub }: { nodePlan: string[]; pin: number; bossSub?: string }) {
  const nodes = [...nodePlan, 'Rest'];
  return (
    <ol className="ladder">
      {nodes.map((kind, i) => {
        const base = LADDER_NODE_META[kind] ?? { label: kind, sub: '' };
        // The Boss node names a themed gate-boss trainer (from bossTrainer.ts) instead of the static
        // "Region gate" — everything else uses its fixed meta.
        const meta = kind === 'BossBattle' && bossSub ? { ...base, sub: bossSub } : base;
        const nodeState = i < pin ? 'done' : i === pin ? 'current' : 'upcoming';
        return (
          <li key={i} className={`ladder-node ladder-node--${kind.toLowerCase()} ladder-node--${nodeState}`}>
            <span className="ladder-icon" aria-hidden="true">
              <svg viewBox="0 0 24 24"><use href={`#${nodeIconId(kind)}`} /></svg>
            </span>
            <span className="ladder-text">
              <span className="ladder-label">{meta.label}</span>
              {meta.sub && <span className="ladder-sub">{meta.sub}</span>}
            </span>
            {nodeState === 'current' && <span className="ladder-here" aria-label="you are here">◄</span>}
          </li>
        );
      })}
    </ol>
  );
}

// The region-map graph: the run's playable biomes as waypoints at their authored 2-D coords, wired by their
// (playable-subset) neighbour edges, with the travelled route highlighted and the current biome marked. When
// `onChoose` is supplied, the biomes in `offeredIds` become clickable route picks (the map-based route choice);
// otherwise it's a read-only overview. A presentation view — the offered set is decided server-side.
function RegionMap({ biomes, routePath, currentId, offeredIds, onChoose }: {
  biomes: RegionBiome[];
  routePath: string[];
  currentId: string;
  offeredIds: ReadonlySet<string>;
  onChoose?: (id: string) => void;
}) {
  const byId = new Map(biomes.map(b => [b.id, b]));
  const visited = new Set(routePath); // node-visited membership
  const travelled = travelledEdgeKeys(routePath); // edges actually walked (consecutive hops, not "both visited")
  // Undirected edges, de-duped by drawing each pair once (id order) — both endpoints must be in the sent subset.
  const edges: Array<{ a: RegionBiome; b: RegionBiome }> = [];
  for (const b of biomes)
    for (const nid of b.neighbours) {
      const n = byId.get(nid);
      if (n && b.id < n.id) edges.push({ a: b, b: n });
    }
  const primary = (b: RegionBiome) => b.types[0] ?? 'Normal';
  return (
    <div className="region-map">
      {/* Territory layer: each biome glows in its type colour (background imagery), watermarked with its
          primary-type icon. screen-blended so neighbours bleed into one painterly overworld. */}
      <div className="region-terr" aria-hidden="true">
        {biomes.map(b => (
          <span
            key={b.id}
            className="region-territory"
            style={{ left: `${b.x}%`, top: `${b.y}%`, '--c': typeColor(primary(b)) } as CSSProperties}
          >
            <svg className="region-territory-motif" viewBox="0 0 24 24"><use href={`#${typeIconId(primary(b))}`} /></svg>
          </span>
        ))}
      </div>
      {/* Edge layer: a path per neighbour link, its gradient blending the two biomes' type colours. */}
      <svg className="region-map-edges" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
        <defs>
          {edges.map(({ a, b }, i) => (
            <linearGradient key={i} id={`edge-grad-${i}`} gradientUnits="userSpaceOnUse" x1={a.x} y1={a.y} x2={b.x} y2={b.y}>
              <stop offset="0" stopColor={typeColor(primary(a))} />
              <stop offset="1" stopColor={typeColor(primary(b))} />
            </linearGradient>
          ))}
        </defs>
        {edges.map(({ a, b }, i) => {
          const isTravelled = travelled.has(regionEdgeKey(a.id, b.id));
          // Gentle perpendicular bow so links read as drawn paths, not a stiff mesh.
          const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
          const dx = b.x - a.x, dy = b.y - a.y, len = Math.hypot(dx, dy) || 1;
          const cx = mx + (-dy / len) * 5, cy = my + (dx / len) * 5;
          return (
            <path
              key={i}
              d={`M${a.x} ${a.y} Q${cx} ${cy} ${b.x} ${b.y}`}
              stroke={isTravelled ? undefined : `url(#edge-grad-${i})`}
              className={`region-edge${isTravelled ? ' region-edge--travelled' : ''}`}
            />
          );
        })}
      </svg>
      {biomes.map(b => {
        const isCurrent = b.id === currentId;
        const isOffered = offeredIds.has(b.id);
        const choosable = isOffered && !!onChoose;
        const cls = [
          'region-node',
          isCurrent ? 'region-node--current' : '',
          visited.has(b.id) && !isCurrent ? 'region-node--visited' : '',
          isOffered ? 'region-node--offered' : '',
        ].filter(Boolean).join(' ');
        return (
          <button
            key={b.id}
            type="button"
            className={cls}
            style={{ left: `${b.x}%`, top: `${b.y}%`, '--node-clr': typeColor(primary(b)) } as CSSProperties}
            disabled={!choosable}
            onClick={choosable ? () => onChoose!(b.id) : undefined}
            aria-current={isCurrent ? 'location' : undefined}
            aria-label={`${b.name}${isCurrent ? ' (current)' : ''}${choosable ? ' — choose this route' : ''}`}
          >
            {isCurrent && <span className="region-node-flag" aria-hidden="true">You are here</span>}
            {choosable && <span className="region-node-flag region-node-flag--pick" aria-hidden="true">Choose</span>}
            <span className="region-node-disc" aria-hidden="true">
              <svg viewBox="0 0 24 24"><use href={`#${typeIconId(primary(b))}`} /></svg>
            </span>
            <span className="region-node-label">{b.name}</span>
            <span className="region-node-chips" aria-hidden="true">
              {b.types.map(t => <TypeChip key={t} type={t} />)}
            </span>
          </button>
        );
      })}
    </div>
  );
}

// The pinned/peeked route-map overlay: the whole-run region graph plus, when a biome is active, its node ladder.
// Toggled by the MAP button and auto-peeked at each ladder change. Read-only (no route pick here — that happens
// in the RouteChoiceMap when the run offers a choice).
function RunMapPanel({ biomes, routePath, currentId, biomeName, nodePlan, pin, pinned, onClose }: {
  biomes: RegionBiome[];
  routePath: string[];
  currentId: string;
  biomeName: string;
  nodePlan: string[];
  pin: number;
  pinned: boolean;
  onClose: () => void;
}) {
  // The map is the one overlay Escape may close: it only draws run state the client already has, so leaving it
  // costs nothing. Every other overlay is a prompt the run is blocked on — see Modal's `dismiss`. It can't use
  // that wrapper (the pinned panel *is* the full-screen surface, not an overlay + card), but it shares the rule.
  useEscapeKey(pinned ? onClose : null);

  // The biome boss's themed trainer name (stable per biome visit — see bossTrainer.ts), used for the Boss
  // node's ladder sub-label in place of the behind-the-curtain "Region gate".
  const primaryType = biomes.find(b => b.id === currentId)?.types[0];
  const bossSub = `Trainer ${bossTrainerName(currentId, primaryType, nodePlan)}`;

  // Compact corner peek (auto-shown at each ladder change): the current biome's ladder only — the full graph
  // belongs to the pinned full-screen view, so the peek stays small and unobtrusive.
  if (!pinned) {
    return (
      <div className="encounter-map" role="complementary" aria-label="Route map">
        <div className="encounter-map-head">
          <span className="encounter-map-biome">{biomeName || 'Region map'}</span>
        </div>
        {nodePlan.length > 0 && <NodeLadder nodePlan={nodePlan} pin={pin} bossSub={bossSub} />}
      </div>
    );
  }

  // Pinned: the full-screen overworld. Backdrop click (outside the stage) closes it.
  return (
    <div
      className="encounter-map encounter-map--pinned"
      role="dialog"
      aria-modal="true"
      aria-label="Route map"
      onClick={e => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="map-topbar">
        <div className="map-brand">
          <h2 className="map-title">Run Map</h2>
          <span className="encounter-map-biome">{biomeName || 'Region map'}</span>
        </div>
        <button className="encounter-map-close" onClick={onClose} aria-label="Close map">×</button>
      </div>
      <div className="map-stage">
        <div className="map-overworld">
          <RegionMap biomes={biomes} routePath={routePath} currentId={currentId} offeredIds={EMPTY_ID_SET} />
        </div>
        {nodePlan.length > 0 && (
          <aside className="map-ladder-panel">
            <h3 className="map-ladder-head">Encounter Path</h3>
            <div className="map-ladder-biome">{biomeName}</div>
            <NodeLadder nodePlan={nodePlan} pin={pin} bossSub={bossSub} />
          </aside>
        )}
      </div>
      <div className="map-legend" aria-hidden="true">
        <span className="map-legend-key"><span className="map-legend-swatch map-legend-swatch--here" />You are here</span>
        <span className="map-legend-key"><span className="map-legend-swatch map-legend-swatch--travelled" />Travelled</span>
        <span className="map-legend-key map-legend-note">Territory colour = biome type</span>
      </div>
    </div>
  );
}

// The map-based route choice (replaces the old biome-card modal): a blocking, prominent region map where the
// offered biomes glow as clickable waypoints. Clicking one charts the route (the backend is blocked awaiting it).
// The run always offers at least one option, so there is no empty/decline state — it's a required choice.
function RouteChoiceMap({ biomes, routePath, currentId, options, onChoose }: {
  biomes: RegionBiome[];
  routePath: string[];
  currentId: string;
  options: BiomeOption[];
  onChoose: (biomeId: string) => void;
}) {
  const offeredIds = new Set(options.map(o => o.id));
  // Move keyboard focus onto the first offered waypoint when the choice opens, so a keyboard/screen-reader user
  // lands on an actionable route pick (not stranded on the backdrop) and can Tab between the offered biomes.
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    ref.current?.querySelector<HTMLButtonElement>('.region-node--offered')?.focus();
  }, []);
  return (
    <Modal label="Choose your route" dismiss="blocking" card="route-choice-modal" cardRef={ref}>
      <p className="biome-title">Choose your route</p>
      <p className="biome-sub">Click a highlighted biome to chart your path.</p>
      <RegionMap biomes={biomes} routePath={routePath} currentId={currentId} offeredIds={offeredIds} onChoose={onChoose} />
      <div className="route-choice-legend">
        {options.map(o => (
          <span key={o.id} className="route-choice-legend-item">
            <span className="route-choice-legend-name">{o.name}</span>
            {o.types.map(t => <TypeChip key={t} type={t} />)}
          </span>
        ))}
      </div>
    </Modal>
  );
}

// Loot drop hover: a transient floating loot popup over the field (gold + any items the reward dropped),
// like the item-pickup toasts in loot games. Purely cosmetic and non-blocking — the reducer sets it, a timer
// in BattleScreen clears it after DROP_TOAST_MS, and the CSS floats + fades it over that same beat. It never
// intercepts input (pointer-events: none) so it can't block the menu underneath.
function DropHover({ drop }: { drop: DropToast }) {
  return (
    <div className="drop-hover" role="status" aria-label="Drop">
      <span className="drop-hover-title">Found!</span>
      <ul className="drop-hover-list">
        {drop.itemNames.map((name, i) => (
          <li key={i} className="drop-chip drop-chip--item">
            <span className="drop-chip-icon" aria-hidden="true">✦</span>{name}
          </li>
        ))}
        {drop.gold > 0 && (
          <li className="drop-chip drop-chip--gold">
            <span className="drop-chip-icon" aria-hidden="true">₽</span>+{drop.gold}
          </li>
        )}
      </ul>
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
    <div className="levelup-panel" role="status" aria-label={`${panel.creatureName} reached level ${panel.level}`}>
      <div className="levelup-title">{panel.creatureName} · LEVEL UP! · Lv {panel.level}</div>
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

function ActionMenu({ playerName, canAct, onFight, onBag, onCheck, onBack }: {
  playerName: string;
  canAct: boolean;
  onFight: () => void;
  onBag: () => void;
  onCheck: () => void;
  onBack: () => void;
}) {
  // FIGHT and BAG both spend the turn, so they're gated on it being the player's turn (canAct). CHECK
  // POKEMON is read-only, so it's always available.
  return (
    <div className="action-menu">
      <p className="action-prompt">What will {playerName} do?</p>
      <div className="action-buttons">
        <button
          className={`action-btn action-btn--fight ${!canAct ? 'action-btn--waiting' : ''}`}
          onClick={onFight}
          disabled={!canAct}
        >
          FIGHT
        </button>
        <button
          className={`action-btn ${!canAct ? 'action-btn--waiting' : ''}`}
          onClick={onBag}
          disabled={!canAct}
        >
          BAG
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
          const pow = isEmpty ? null : powerPill(move.power); // raw base-power strength cue
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
              {!isEmpty && (
                <span className="move-meta">
                  <TypeBadge type={move.type} size="sm" />
                  {pow && (
                    <span className={`move-pow ${pow.cls}`} aria-label={`power ${pow.label}`}>
                      {pow.label}
                    </span>
                  )}
                </span>
              )}
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

// The in-battle bag: fetched fresh each time it opens (quantities change as items are consumed), grouped by
// pocket, with only battle-usable categories shown (see bag.ts). Picking an item uses it as the turn —
// except a single-move PP restore (Ether), which first asks which move slot to refill via PpTargetPicker.
function BagMenu({ gameId, gold, moves, onUse, onBack }: {
  gameId: string | null;
  gold: number;
  moves: MoveInfo[];
  onUse: (itemId: number, targetMoveSlot: number | null) => void;
  onBack: () => void;
}) {
  const [items, setItems] = useState<BagItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  // When set, we're picking the move slot for this single-move PP-restore item instead of the item list.
  const [ppTarget, setPpTarget] = useState<BagItem | null>(null);

  useEffect(() => {
    if (!gameId) { setError('No active game.'); return; }
    let live = true;
    fetch(`/api/game/${gameId}/bag`)
      .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); })
      .then((d: BagItem[]) => { if (live) setItems(d); })
      .catch(e => { if (live) setError(friendlyFetchError(e)); });
    return () => { live = false; };
  }, [gameId]);

  const pick = (item: BagItem) => {
    if (needsMoveTarget(item)) setPpTarget(item);
    else onUse(item.id, null);
  };

  if (ppTarget) {
    return (
      <PpTargetPicker
        item={ppTarget}
        moves={moves}
        onPick={slot => onUse(ppTarget.id, slot)}
        onBack={() => setPpTarget(null)}
      />
    );
  }

  const groups = items ? groupBagItems(items) : [];

  return (
    <div className="bag-menu">
      <div className="bag-gold" aria-label={`${gold} gold`}>
        <span className="bag-gold-label">MONEY</span>
        <span className="bag-gold-value">
          <span className="bag-gold-coin" aria-hidden="true">₽</span>
          <span className="bag-gold-amount">{gold}</span>
        </span>
      </div>
      <div className="bag-list">
        {error && <p className="bag-error">{error}</p>}
        {!error && !items && <p className="bag-empty">Loading…</p>}
        {items && groups.length === 0 && <p className="bag-empty">No usable items.</p>}
        {groups.map(group => (
          <div className="bag-group" key={group.label}>
            <p className="bag-group-label">{group.label}</p>
            {group.items.map(item => (
              <button key={item.id} className="bag-item" onClick={() => pick(item)}>
                <img
                  className="bag-item-sprite"
                  src={`/sprites/items/${item.id}.png`}
                  alt=""
                  aria-hidden="true"
                  draggable={false}
                  onError={e => { (e.currentTarget as HTMLImageElement).style.visibility = 'hidden'; }}
                />
                <span className="bag-item-name">{formatItemName(item.name)}</span>
                <span className="bag-item-qty">×{item.quantity}</span>
                {item.description && <span className="bag-item-desc">{item.description}</span>}
              </button>
            ))}
          </div>
        ))}
      </div>
      <button className="btn-ghost action-back" onClick={onBack}>← BACK</button>
    </div>
  );
}

// Move-slot pick for a single-move PP restore (Ether / Max Ether). Reuses the move-grid look; a slot already
// at full PP is disabled (restoring it would have no effect → the engine refuses the use).
function PpTargetPicker({ item, moves, onPick, onBack }: {
  item: BagItem;
  moves: MoveInfo[];
  onPick: (slot: number) => void;
  onBack: () => void;
}) {
  const slots = [...moves];
  while (slots.length < 4) slots.push({ name: '---', type: 'Normal', ppCurrent: 0, ppMax: 0 });

  return (
    <div className="move-menu">
      <p className="bag-pp-prompt">Restore PP to which move? ({formatItemName(item.name)})</p>
      <div className="move-grid">
        {slots.map((move, i) => {
          const isEmpty = move.name === '---';
          const full = !isEmpty && move.ppCurrent >= move.ppMax;
          const disabled = isEmpty || full;
          return (
            <button
              key={i}
              className={`move-btn ${disabled ? 'move-btn--disabled' : ''}`}
              disabled={disabled}
              onClick={() => onPick(i)}
            >
              <span className="move-name">{formatMoveName(move.name)}</span>
              {!isEmpty && (
                <span className={`move-pp ${full ? '' : 'move-pp--low'}`}>
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

