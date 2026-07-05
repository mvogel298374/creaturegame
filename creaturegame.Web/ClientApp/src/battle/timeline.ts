import { useCallback, useRef } from 'react';
import type { MoveInfo } from '../types/BattleEvents';
import { bridge } from './PhaserBridge';
import { formatMoveName } from '../utils/format';
import { E2E } from '../testEnv';

// ─────────────────────────────────────────────────────────────────────────────
// The battle log is driven by a stream of backend events. Each event expands —
// via the PURE `expandEvent` function below — into:
//   • `now`   : reducer actions dispatched immediately (control plane: phase,
//               turn start/end, the winner flip), and
//   • `steps` : an ordered timeline of primitive instructions (dispatch a view
//               change, fire a Phaser command, wait, or await an animation),
//               played one at a time by the driver (`useBattleTimeline`).
// Keeping the sequencing/timing/text in a pure function makes it unit-testable
// without a browser or a wall clock, and confines all timers/bridge access to
// the small driver — the part that used to be a tangle of async closures.
// ─────────────────────────────────────────────────────────────────────────────

export type Payload = Record<string, unknown>;
type Side = 'player' | 'enemy';

// Colour cue for a battle-log line. Drives a CSS class on the rendered line: super-effective (green),
// not-very-effective (muted/grey), no-effect/immune (red). Undefined tone = the default neutral line.
export type LogTone = 'super' | 'weak' | 'immune';
export interface LogEntry { message: string; tone?: LogTone }

// The stat totals carried by a level-up (matches the engine's StatBlock).
export interface StatBlock { maxHp: number; attack: number; defense: number; special: number; speed: number }

// One biome offered on the map screen: stable id, display name, and 1–3 theme types (for the card's badges).
export interface BiomeOption { id: string; name: string; types: string[] }

// View-state actions — consumed by the reducer in useBattleHub.
export type Action =
  | { type: 'BATTLE_STARTED'; playerName: string; enemyName: string; enemySpeciesId: number; enemyLevel: number }
  | { type: 'TURN_STARTED'; turnNumber: number; playerHp: number; playerMaxHp: number; playerStatus: string; playerXpThisLevel: number; playerXpToNextLevel: number; enemyHp: number; enemyMaxHp: number; enemyStatus: string; moves: MoveInfo[] }
  | { type: 'TURN_ENDED' }
  | { type: 'PLAYER_CHOSE' }
  | { type: 'RUN_ENDED'; battlesWon: number; finalLevel: number }
  | { type: 'LOG'; message: string; tone?: LogTone }
  | { type: 'UPDATE_HP'; name: string; hp: number }
  | { type: 'UPDATE_STATUS'; name: string; status: string }
  | { type: 'CLEAR_STATUS'; name: string }
  | { type: 'LEVELED_UP'; newLevel: number; xpToNextLevel: number }
  // The Gen 1 level-up stat panel: per-stat gains + the new totals. Shown then hidden by the timeline.
  | { type: 'SHOW_LEVEL_UP'; level: number; gains: StatBlock; totals: StatBlock }
  | { type: 'HIDE_LEVEL_UP' }
  | { type: 'ANIMATING_START' }
  | { type: 'ANIMATING_DONE' }
  // Bar fills by an awarded amount, capped at the current level's max (a level-up follows if it caps out).
  | { type: 'XP_GAIN'; amount: number }
  // Bar set to an absolute level-relative value (used to refill to the leftover after a level-up reset).
  | { type: 'XP_SET'; value: number }
  // Level-up move learning: prompt to replace a move (4 slots full), shown then hidden by the player's answer.
  | { type: 'SHOW_MOVE_REPLACEMENT'; creatureName: string; newMoveName: string; currentMoves: string[] }
  | { type: 'HIDE_MOVE_REPLACEMENT' }
  // Roguelite Poké Center: heal offer between encounters — shown then hidden by the player's Heal/Skip press.
  | { type: 'SHOW_RECOVERY'; creatureName: string; speciesId: number; battlesWon: number }
  | { type: 'HIDE_RECOVERY' }
  // Evolution offer between encounters — shown then hidden by the player's Allow/Cancel press.
  | { type: 'SHOW_EVOLUTION_PROMPT'; fromName: string; toName: string; fromSpeciesId: number; toSpeciesId: number }
  | { type: 'HIDE_EVOLUTION_PROMPT' }
  // Biome map / route choice — shown then hidden by the player's pick (charts the next leg of the run).
  | { type: 'SHOW_BIOME_CHOICE'; options: BiomeOption[] }
  | { type: 'HIDE_BIOME_CHOICE' }
  // Run economy: set the gold HUD to the wallet's running total (RewardGranted carries the post-credit total).
  | { type: 'SET_GOLD'; gold: number }
  // Treasure/Mystery reward — shown then hidden by the player's OK (the backend blocks on the ack). A battle
  // drop never raises this (it's inline: HUD bump + log only).
  | { type: 'SHOW_REWARD'; source: string; gold: number; goldTotal: number; itemNames: string[] }
  | { type: 'HIDE_REWARD' }
  // Battle-win drop hover: a transient floating "you found …" toast (gold + items) shown over the field for a
  // moment, then auto-dismissed by the view (non-blocking — the run keeps flowing). Battle source only;
  // Treasure/Mystery use the blocking SHOW_REWARD modal instead.
  | { type: 'SHOW_DROP'; gold: number; itemNames: string[] }
  | { type: 'HIDE_DROP' };

// Phaser commands sent over the mitt bridge.
export type BridgeCommand =
  | { type: 'playMoveAnimation'; attackerSide: Side; targetSide: Side }
  | { type: 'playFaintAnimation'; side: Side }
  | { type: 'playDamageShake'; side: Side }
  | { type: 'playHitSound'; isCrit: boolean }
  | { type: 'playStatusSound' }
  | { type: 'playLevelUpSound' }
  | { type: 'spawnEnemy'; enemySpeciesId: number }
  | { type: 'transformSprite'; side: Side; speciesId: number }
  | { type: 'resetPlayerSprite' }
  | { type: 'playEvolutionAnimation'; toSpeciesId: number };

// One primitive instruction in a timeline.
export type Step =
  | { kind: 'dispatch'; action: Action }
  | { kind: 'emit'; command: BridgeCommand }
  | { kind: 'wait'; ms: number }
  | { kind: 'awaitAnim' };

export interface Expansion {
  now?: Action[];
  steps?: Step[];
}

export interface ExpandContext {
  playerName: string;
  // 1 for the first battle of the run, 2+ for each chained encounter. Drives whether BattleStarted is the
  // scene's initial entry (handled by create()) or a mid-run enemy swap (slide in a new sprite). Optional
  // for tests; treated as the first encounter when absent.
  encounterIndex?: number;
}

// ── Step builders (keep expandEvent readable) ───────────────────────────────
const d    = (action: Action): Step => ({ kind: 'dispatch', action });
const emit = (command: BridgeCommand): Step => ({ kind: 'emit', command });
const w    = (ms: number): Step => ({ kind: 'wait', ms });
const anim = (): Step => ({ kind: 'awaitAnim' });
const log  = (message: string, tone?: LogTone): Action => ({ type: 'LOG', message, tone });

// ── Text helpers (pure) ──────────────────────────────────────────────────────
function statusAppliedMsg(name: string, status: string): string {
  switch (status) {
    case 'Sleep':     return `${name} fell asleep!`;
    case 'Freeze':    return `${name} was frozen solid!`;
    case 'Paralysis': return `${name} was paralyzed!`;
    case 'Burn':      return `${name} was burned!`;
    case 'Poison':    return `${name} was poisoned!`;
    case 'BadPoison': return `${name} was badly poisoned!`;
    default:          return `${name} was affected by ${status}!`;
  }
}

function statusClearedMsg(name: string, wasStatus: string): string {
  switch (wasStatus) {
    case 'Sleep':     return `${name} woke up!`;
    case 'Freeze':    return `${name} thawed out!`;
    case 'Paralysis': return `${name} was cured of paralysis!`;
    case 'Burn':      return `${name} healed its burn!`;
    case 'Poison':
    case 'BadPoison': return `${name} was cured of poison!`;
    default:          return `${name} recovered from ${wasStatus}!`;
  }
}

// Banner line for a route node (RunNodeEntered). Kinds are the RunNodeKind names the backend emits.
function runNodeBannerMsg(kind: string): string {
  switch (kind) {
    case 'EliteBattle': return 'An Elite trainer blocks the path!';
    case 'BossBattle':  return 'The biome boss looms ahead!';
    case 'Shop':        return 'You happened upon a shop.';
    case 'Treasure':    return 'You found a treasure cache!';
    case 'Mystery':     return 'Something mysterious stirs…';
    default:            return `Reached a ${kind} node.`;
  }
}

// Log line for a reward payout (battle drop or Treasure/Mystery node). A real gold HUD / reward modal is a
// later, separately-planned pass (Run Economy Phase C) — for now this just narrates what was granted so the
// event isn't silently dropped.
function rewardGrantedMsg(source: string, gold: number, itemNames: string[]): string {
  const parts = [gold > 0 ? `${gold}G` : null, ...itemNames].filter((p): p is string => p !== null);
  const reward = parts.length > 0 ? parts.join(', ') : 'nothing this time';
  return source === 'Battle' ? `Found ${reward}!` : `The ${source.toLowerCase()} held ${reward}!`;
}

function actionBlockedMsg(name: string, reason: string): string {
  switch (reason) {
    case 'Sleep':     return `${name} is fast asleep!`;
    case 'Freeze':    return `${name} is frozen solid!`;
    case 'Paralysis': return `${name} is fully paralyzed!`;
    default:          return `${name} can't move! (${reason})`;
  }
}

// Gen 1 two-turn moves each have their own charge-up line.
function chargingMsg(name: string, slug: string): string {
  switch (slug) {
    case 'fly':        return `${name} flew up high!`;
    case 'dig':        return `${name} dug a hole!`;
    case 'solar-beam': return `${name} took in sunlight!`;
    case 'razor-wind': return `${name} made a whirlwind!`;
    case 'skull-bash': return `${name} lowered its head!`;
    case 'sky-attack': return `${name} is glowing!`;
    default:           return `${name} began charging up!`;
  }
}

/**
 * Pure mapping from a backend battle event to its immediate actions + timeline steps.
 * No timers, no bridge, no dispatch — entirely testable in isolation.
 */
export function expandEvent(eventType: string, payload: Payload, ctx: ExpandContext): Expansion {
  const side = (name: string): Side => (name === ctx.playerName ? 'player' : 'enemy');

  switch (eventType) {
    // ── Control plane ──────────────────────────────────────────────────────────
    case 'BattleStarted': {
      const pName = payload.playerName as string;
      const eName = payload.enemyName as string;
      const enemySpeciesId = payload.enemySpeciesId as number;
      // Queued, NOT immediate. In the endless chain the next encounter's BattleStarted arrives the instant
      // the backend finishes the previous battle — if applied immediately it jumps ahead of the still-
      // draining damage/faint/XP steps (the "KINGLER VS ARBOK" line landing mid-attack). Routing it through
      // the queue syncs the new enemy only after the previous battle's animation has played. (At run start
      // the queue is empty, so the first BattleStarted still applies at once.)
      const started: Action = {
        type: 'BATTLE_STARTED',
        playerName: pName,
        enemyName: eName,
        enemySpeciesId,
        enemyLevel: payload.enemyLevel as number,
      };
      // First encounter: the scene's create() plays the entry animation. Subsequent encounters: tell the
      // scene to load + slide in the new enemy sprite (the canvas is never remounted).
      if ((ctx.encounterIndex ?? 1) <= 1) {
        return { steps: [d(started), d(log(`${pName} VS ${eName}`))] };
      }
      // A chained encounter: announce the new challenger HERE, at the start of the next battle — not on the
      // previous BattleEnded. The run loop emits this only after any interleaved instance (e.g. a Poké Center
      // recovery) is fully resolved, so "a new challenger approaches" can never jump ahead of an unfinished
      // between-battle step.
      return { steps: [
        d(started),
        d(log('A new challenger approaches!')),
        d(log(`${pName} VS ${eName}`)),
        emit({ type: 'spawnEnemy', enemySpeciesId }),
        w(450), // let the new enemy slide in before the first turn's prompt
      ] };
    }

    case 'TurnStarted':
      // Queued, NOT immediate. The next turn's TurnStarted arrives over the network
      // the instant the backend resolves the turn — if applied immediately it slams
      // the HP bars to their end-of-turn values before the damage has animated. Routing
      // it through the timeline means HP/status/moves sync only after the queued damage
      // steps have played, so the bar drains incrementally in step with the animation.
      // (At battle start the queue is empty, so the first TurnStarted still applies at once.)
      return {
        steps: [d({
          type: 'TURN_STARTED',
          turnNumber: payload.turnNumber as number,
          playerHp: payload.playerHp as number,
          playerMaxHp: payload.playerMaxHp as number,
          playerStatus: payload.playerStatus as string,
          playerXpThisLevel: payload.playerXpThisLevel as number,
          playerXpToNextLevel: payload.playerXpToNextLevel as number,
          enemyHp: payload.enemyHp as number,
          enemyMaxHp: payload.enemyMaxHp as number,
          enemyStatus: payload.enemyStatus as string,
          moves: payload.moves as MoveInfo[],
        })],
      };

    case 'TurnEnded':
      return { now: [{ type: 'TURN_ENDED' }] };

    case 'BattleEnded': {
      // Per-encounter, NOT terminal in an endless chain. A player win is just a short intermission beat — the
      // "new challenger" announcement now belongs to the NEXT encounter's BattleStarted (so it never precedes
      // an interleaved Poké Center recovery the run loop hasn't resolved yet). A loss is followed by RunEnded,
      // which drives the game-over screen, so there's nothing to do here.
      const winner = payload.winnerName as string;
      if (side(winner) === 'player') {
        // Revert the player sprite in case it Transformed this battle (Transform is undone at battle end;
        // the enemy sprite self-corrects via the next encounter's spawnEnemy, the player's does not).
        return { steps: [emit({ type: 'resetPlayerSprite' }), w(300)] };
      }
      return {};
    }

    case 'RunEnded': {
      // Terminal: the player's creature fainted. Flip to the game-over screen after the faint animation
      // (queued) has drained, and log the run summary.
      const battlesWon = payload.battlesWon as number;
      const finalLevel = payload.finalLevel as number;
      const name = payload.finalCreatureName as string;
      const wins = `${battlesWon} win${battlesWon === 1 ? '' : 's'}`;
      return { steps: [
        w(400),
        d(log(`${name} fainted! Run over — ${wins}, reached level ${finalLevel}.`)),
        d({ type: 'RUN_ENDED', battlesWon, finalLevel }),
      ] };
    }

    case 'RecoveryOffered': {
      // Roguelite Poké Center step in the game loop: announce it and raise the heal modal. The backend is now
      // blocked awaiting the player's Heal/Skip press (RespondRecovery), so the timeline idles here — exactly
      // like MoveReplacementRequired — until the modal resolves.
      const cName      = payload.creatureName as string;
      const speciesId  = payload.speciesId as number;
      const battlesWon = payload.battlesWon as number;
      return { steps: [
        w(300),
        d(log(`${cName} reached a Poké Center!`)),
        w(300),
        d({ type: 'SHOW_RECOVERY', creatureName: cName, speciesId, battlesWon }),
      ] };
    }

    case 'PlayerRecovered': {
      // The player accepted the heal: fully restored (HP/PP/status). Fill the bar back to full and clear any
      // lingering status badge, with the Gen 1 heal line. (The modal already closed on the player's press.)
      const cName   = payload.creatureName as string;
      const hpAfter = payload.hpAfter as number;
      return { steps: [
        w(300),
        d(log(`${cName} was fully healed!`)),
        d({ type: 'UPDATE_HP', name: cName, hp: hpAfter }),
        d({ type: 'CLEAR_STATUS', name: cName }),
        w(500),
      ] };
    }

    case 'RecoveryDeclined': {
      // The player skipped the heal — keep going as they were.
      const cName = payload.creatureName as string;
      return { steps: [w(150), d(log(`${cName} decided to keep going!`)), w(400)] };
    }

    // ── Biome / route map (between biomes) ─────────────────────────────────────
    // The route choice raises a blocking map modal: the backend is now blocked awaiting the player's pick
    // (ChooseBiome), so the timeline idles here — exactly like RecoveryOffered — until the modal resolves.
    // Queued (not immediate) so it follows any in-flight recovery animation cleanly.
    case 'BiomeChoiceOffered': {
      const options: BiomeOption[] = ((payload.options as Array<Record<string, unknown>>) ?? []).map(o => ({
        id: o.id as string,
        name: o.name as string,
        types: (o.types as string[]) ?? [],
      }));
      return { steps: [w(200), d({ type: 'SHOW_BIOME_CHOICE', options })] };
    }

    case 'BiomeEntered': {
      // The player entered a biome (or the run auto-picked one) — title the next leg of the route.
      const biomeName = payload.biomeName as string;
      return { steps: [w(200), d(log(`Entered ${biomeName}!`)), w(300)] };
    }

    case 'RewardGranted': {
      const rSource = payload.source as string;
      const gold = payload.gold as number;
      const goldTotal = payload.goldTotal as number;
      const itemNames = (payload.itemNames as string[]) ?? [];
      // Always bump the gold HUD to the wallet's running total and log the payout. Battle drops stop there
      // (inline, non-blocking). Treasure/Mystery also raise the reward modal — the backend blocks on its ack.
      const steps: Step[] = [
        w(200),
        d({ type: 'SET_GOLD', gold: goldTotal }),
        d(log(rewardGrantedMsg(rSource, gold, itemNames))),
        w(300),
      ];
      if (rSource === 'Battle') {
        // Inline battle drop: besides the HUD bump + log line, raise a transient drop hover so the loot is
        // visible on the field (only when something actually dropped — an empty roll stays silent). The view
        // auto-dismisses it, so no HIDE step here — it must not block the run flowing on to the next encounter.
        if (gold > 0 || itemNames.length > 0)
          steps.push(d({ type: 'SHOW_DROP', gold, itemNames }));
      } else {
        steps.push(d({ type: 'SHOW_REWARD', source: rSource, gold, goldTotal, itemNames }));
      }
      return { steps };
    }

    case 'RunNodeEntered': {
      // A route node banner (Phase 3c-1 bones): Elite/Boss precede the battle stream; Shop/Treasure/Mystery
      // are interaction bones with no behaviour yet, so the banner line is the whole node for now.
      const kind = payload.kind as string;
      return { steps: [w(200), d(log(runNodeBannerMsg(kind))), w(300)] };
    }

    // ── Turn events: sequenced through the animation timeline ──────────────────
    case 'MoveUsed': {
      const attacker = payload.attackerName as string;
      const moveName = payload.moveName as string;
      const attackerSide = side(attacker);
      const targetSide: Side = attackerSide === 'player' ? 'enemy' : 'player';
      // Gen 1 cadence: announce the move FIRST, brief beat, THEN the lunge. The
      // hit sound + incremental HP drain follow in the DamageDealt event, so the
      // bar never moves before the "used" line. (Was animating before the text.)
      return { steps: [
        d(log(`${attacker} used ${formatMoveName(moveName)}!`)),
        w(400),
        emit({ type: 'playMoveAnimation', attackerSide, targetSide }),
        anim(),
      ] };
    }

    case 'MoveMissed': {
      const aName = payload.attackerName as string;
      const mName = payload.moveName as string;
      return { steps: [w(200), d(log(`${aName}'s ${formatMoveName(mName)} missed!`))] };
    }

    case 'MoveHadNoEffect': {
      // Type immunity (e.g. Ghost vs a Normal/Fighting move, Poison vs poison-powder).
      const targetName = payload.targetName as string;
      return { steps: [w(650), d(log(`It doesn't affect ${targetName}...`)), w(800)] };
    }

    case 'ButNothingHappened': {
      // Splash and other no-op moves — the Gen 1 "But nothing happened!" line.
      return { steps: [w(650), d(log('But nothing happened!')), w(800)] };
    }

    case 'SubstitutePutUp': {
      const name = payload.creatureName as string;
      return { steps: [w(300), d(log(`${name} put up a substitute!`)), w(600)] };
    }

    case 'SubstituteAbsorbedHit': {
      const name = payload.creatureName as string;
      return { steps: [w(300), d(log(`The substitute took damage for ${name}!`)), w(600)] };
    }

    case 'SubstituteFaded': {
      const name = payload.creatureName as string;
      return { steps: [w(300), d(log(`${name}'s substitute faded!`)), w(600)] };
    }

    case 'DamageDealt': {
      const targetName = payload.targetName as string;
      const hpAfter    = payload.hpAfter as number;
      const isCrit     = payload.isCrit as boolean;
      const eff        = payload.typeEffectiveness as number;
      const damage     = payload.damage as number;

      // Immunity: no hit, no damage number — just the Gen 1 line (red).
      if (eff === 0) {
        return { steps: [w(650), d(log(`It doesn't affect ${targetName}...`, 'immune')), w(800)] };
      }

      let msg = `${targetName} took ${damage} damage!`;
      if (isCrit) msg += ' A critical hit!';
      // Tint the line by type effectiveness: super-effective green, not-very-effective grey.
      const tone: LogTone | undefined = eff > 1 ? 'super' : eff < 1 ? 'weak' : undefined;
      if (eff > 1)      msg += " It's super effective!";
      else if (eff < 1) msg += " It's not very effective...";

      return { steps: [
        emit({ type: 'playHitSound', isCrit }),
        emit({ type: 'playDamageShake', side: side(targetName) }), // hit reaction: jolt the struck sprite
        d({ type: 'UPDATE_HP', name: targetName, hp: hpAfter }),
        w(650),
        d(log(msg, tone)),
        w(800),   // breathing room between the two attackers' sequences
      ] };
    }

    case 'RecoilDamage': {
      const srcName = payload.sourceName as string;
      const hpAfter = payload.hpAfter as number;
      return { steps: [d({ type: 'UPDATE_HP', name: srcName, hp: hpAfter }), w(400), d(log(`${srcName} is hit by recoil!`))] };
    }

    case 'CrashDamage': {
      const srcName = payload.sourceName as string;
      const hpAfter = payload.hpAfter as number;
      return { steps: [d({ type: 'UPDATE_HP', name: srcName, hp: hpAfter }), w(400), d(log(`${srcName} kept going and crashed!`))] };
    }

    case 'MultiHitCompleted': {
      const hits = payload.hits as number;
      return { steps: [w(120), d(log(`Hit ${hits} time${hits === 1 ? '' : 's'}!`))] };
    }

    case 'CoinsScattered':
      return { steps: [w(120), d(log('Coins scattered everywhere!'))] };

    case 'CreatureFainted': {
      const faintedName = payload.name as string;
      return { steps: [
        emit({ type: 'playFaintAnimation', side: side(faintedName) }),
        anim(),
        w(200),
        d(log(`${faintedName} fainted!`)),
      ] };
    }

    case 'CreatureFled': {
      // Roar/Whirlwind ended the wild battle — a side was scared off (no faint). Reset the player sprite in
      // case it Transformed this battle (we skip BattleEnded on a flee, which is where that revert normally
      // fires); the foe's sprite self-corrects via the next encounter's spawnEnemy.
      const name = payload.name as string;
      const isPlayer = payload.isPlayer as boolean;
      const msg = isPlayer ? `${name} was blown away!` : `${name} fled!`;
      return { steps: [emit({ type: 'resetPlayerSprite' }), w(150), d(log(msg)), w(500)] };
    }

    case 'ExperienceGained': {
      const cName  = payload.creatureName as string;
      const amount = payload.amount as number;
      // Fill the bar by the award, capped at the current level's max. If it tops out, the level-up
      // events that follow handle the reset + refill into the next level.
      return { steps: [
        d(log(`${cName} gained ${amount} EXP. Points!`)),
        d({ type: 'XP_GAIN', amount }),
        w(800),
      ] };
    }

    case 'LeveledUp': {
      const cName         = payload.creatureName as string;
      const newLevel      = payload.newLevel as number;
      const xpThisLevel   = payload.xpThisLevel as number;
      const xpToNextLevel = payload.xpToNextLevel as number;
      const stats         = payload.stats as StatBlock;
      const gains         = payload.statGains as StatBlock;
      // The bar is full from the preceding gain (or prior level). Reset it onto the new level's scale and
      // tick the level, announce the level, play the level-up fanfare, then show the Gen 1 stat-gain panel
      // (gains + new totals) and refill the bar to the leftover XP — "full again" for an intermediate level
      // in a multi-level award, or a partial fill for the final one. The panel is NOT auto-hidden here: it
      // stays up until the player's next input (BattleScreen dispatches HIDE_LEVEL_UP on any action).
      return { steps: [
        w(300),
        d({ type: 'LEVELED_UP', newLevel, xpToNextLevel }),
        w(150),
        d(log(`${cName} grew to level ${newLevel}!`)),
        emit({ type: 'playLevelUpSound' }),
        d({ type: 'SHOW_LEVEL_UP', level: newLevel, gains, totals: stats }),
        w(300),
        d({ type: 'XP_SET', value: Math.min(xpThisLevel, xpToNextLevel) }),
        w(500),
      ] };
    }

    // ── Level-up move learning ─────────────────────────────────────────────────
    case 'MoveLearned': {
      const cName = payload.creatureName as string;
      const mName = payload.moveName as string;
      return { steps: [w(150), d(log(`${cName} learned ${formatMoveName(mName)}!`)), w(600)] };
    }

    case 'MoveReplacementRequired': {
      // Four slots full — announce, then raise the replace-move modal. The backend is now blocked awaiting
      // the player's ForgetMove answer, so the timeline naturally idles here until the modal resolves.
      const cName        = payload.creatureName as string;
      const newMoveName  = payload.newMoveName as string;
      const currentMoves = payload.currentMoves as string[];
      return { steps: [
        w(300),
        d(log(`${cName} is trying to learn ${formatMoveName(newMoveName)}!`)),
        w(500),
        d(log(`But ${cName} already knows 4 moves.`)),
        w(300),
        d({ type: 'SHOW_MOVE_REPLACEMENT', creatureName: cName, newMoveName, currentMoves }),
      ] };
    }

    case 'MoveForgotten': {
      const cName = payload.creatureName as string;
      const mName = payload.moveName as string;
      return { steps: [w(150), d(log(`${cName} forgot ${formatMoveName(mName)}!`)), w(400)] };
    }

    case 'MoveLearnDeclined': {
      const cName = payload.creatureName as string;
      const mName = payload.moveName as string;
      return { steps: [w(150), d(log(`${cName} did not learn ${formatMoveName(mName)}.`)), w(500)] };
    }

    // ── Evolution (between encounters) ─────────────────────────────────────────
    // The offer raises a blocking Allow/Cancel modal — the backend is now waiting on RespondEvolution, so
    // the timeline idles here (like RecoveryOffered) until the player answers. Allow → CreatureEvolved
    // (morph); Cancel → EvolutionCancelled (log).
    case 'EvolutionOffered': {
      const fromName      = payload.fromName as string;
      const toName        = payload.toName as string;
      const fromSpeciesId = payload.fromSpeciesId as number;
      const toSpeciesId   = payload.toSpeciesId as number;
      return { steps: [
        w(300),
        d(log(`What? ${fromName} is evolving!`)),
        w(300),
        d({ type: 'SHOW_EVOLUTION_PROMPT', fromName, toName, fromSpeciesId, toSpeciesId }),
      ] };
    }

    case 'EvolutionCancelled': {
      const cName = payload.creatureName as string;
      return { steps: [w(150), d(log(`${cName} stopped evolving.`)), w(500)] };
    }

    // Announce, play the white-silhouette morph (awaited so the log line lands on the evolved
    // sprite), then confirm. The morph swaps front+back to the evolved species in the scene.
    case 'CreatureEvolved': {
      // Reached only after the player allowed the offer, so the "is evolving!" line already played there.
      const fromName    = payload.fromName as string;
      const toName      = payload.toName as string;
      const toSpeciesId = payload.toSpeciesId as number;
      return { steps: [
        w(200),
        emit({ type: 'playEvolutionAnimation', toSpeciesId }),
        anim(),
        d(log(`${fromName} evolved into ${toName}!`)),
        w(600),
      ] };
    }

    case 'StatusApplied': {
      const tName  = payload.targetName as string;
      const status = payload.status as string;
      return { steps: [
        d({ type: 'UPDATE_STATUS', name: tName, status }),
        emit({ type: 'playStatusSound' }),
        w(300),
        d(log(statusAppliedMsg(tName, status))),
      ] };
    }

    case 'StatusDamage': {
      const tName  = payload.targetName as string;
      const hpAftr = payload.hpAfter as number;
      const src    = payload.source === 'BadPoison' ? 'toxic poison' : payload.source as string;
      return { steps: [d({ type: 'UPDATE_HP', name: tName, hp: hpAftr }), w(400), d(log(`${tName} is hurt by ${src}!`))] };
    }

    case 'StatusCleared': {
      const cName     = payload.creatureName as string;
      const wasStatus = payload.wasStatus as string;
      return { steps: [d({ type: 'CLEAR_STATUS', name: cName }), w(120), d(log(statusClearedMsg(cName, wasStatus)))] };
    }

    case 'ActionBlocked': {
      const cName  = payload.creatureName as string;
      const reason = payload.reason as string;
      return { steps: [w(300), d(log(actionBlockedMsg(cName, reason))), w(800)] };
    }

    case 'ConfusionStarted': {
      const tName = payload.targetName as string;
      return { steps: [emit({ type: 'playStatusSound' }), w(300), d(log(`${tName} became confused!`))] };
    }

    case 'ConfusionMessage': {
      const cName = payload.creatureName as string;
      return { steps: [w(120), d(log(`${cName} is confused!`))] };
    }

    case 'ConfusionDamage': {
      const cName  = payload.creatureName as string;
      const hpAftr = payload.hpAfter as number;
      return { steps: [d({ type: 'UPDATE_HP', name: cName, hp: hpAftr }), w(400), d(log(`${cName} hurt itself in confusion!`))] };
    }

    case 'ConfusionCleared': {
      const cName = payload.creatureName as string;
      return { steps: [w(120), d(log(`${cName} snapped out of confusion!`))] };
    }

    case 'StatStageChanged': {
      const cName = payload.creatureName as string;
      const delta = payload.delta as number;
      const stat  = payload.stat as string;
      const dir   = delta > 0 ? 'rose' : 'fell';
      const sharp = Math.abs(delta) >= 2 ? ' sharply' : '';
      return { steps: [w(120), d(log(`${cName}'s ${stat}${sharp} ${dir}!`))] };
    }

    case 'HazeClearedStages':
      return { steps: [w(120), d(log('All stat changes were erased!'))] };

    case 'DrainHealed': {
      const srcName    = payload.sourceName as string;
      const hpAftr     = payload.hpAfter as number;
      const healAmount = payload.healAmount as number;
      return { steps: [d({ type: 'UPDATE_HP', name: srcName, hp: hpAftr }), w(300), d(log(`${srcName} restored ${healAmount} HP!`))] };
    }

    case 'Healed': {
      const cName  = payload.creatureName as string;
      const hpAftr = payload.hpAfter as number;
      return { steps: [d({ type: 'UPDATE_HP', name: cName, hp: hpAftr }), w(300), d(log(`${cName} regained health!`))] };
    }

    // --- Items (using a bag item in battle). The HP/status/PP result rides on the effect events that
    // follow (Healed / StatusCleared / StatStageChanged / PpRestored); these arms just narrate. ---
    case 'ItemUsed': {
      const itemName = (payload.itemName as string).replace(/-/g, ' ').toUpperCase();
      const target   = payload.targetName as string;
      return { steps: [d(log(`Used ${itemName} on ${target}!`)), w(400)] };
    }

    case 'PpRestored': {
      const cName = payload.creatureName as string;
      const mName = payload.moveName as string;
      return { steps: [w(120), d(log(`${cName}'s ${formatMoveName(mName)} PP was restored!`))] };
    }

    case 'ItemUseFailed':
      return { steps: [w(120), d(log(`It won't have any effect!`))] };

    case 'MimicLearned': {
      const cName = payload.creatureName as string;
      const mName = payload.moveName as string;
      return { steps: [w(120), d(log(`${cName} learned ${formatMoveName(mName)}!`))] };
    }

    case 'TransformedInto': {
      const cName = payload.creatureName as string;
      const tName = payload.targetName as string;
      const intoSpeciesId = payload.intoSpeciesId as number;
      // Morph the transforming side's sprite to the copied species, then announce it (player → back sprite,
      // enemy → front sprite; the scene resolves the directory from the side).
      return { steps: [
        w(300),
        emit({ type: 'transformSprite', side: side(cName), speciesId: intoSpeciesId }),
        d(log(`${cName} transformed into ${tName}!`)),
        w(500),
      ] };
    }

    case 'ConvertedType': {
      const cName = payload.creatureName as string;
      const nType = payload.newType as string;
      return { steps: [w(300), d(log(`${cName} changed its type to ${nType}!`))] };
    }

    case 'ScreenApplied': {
      const cName = payload.creatureName as string;
      const sName = payload.screenName as string;
      return { steps: [emit({ type: 'playStatusSound' }), w(300), d(log(`${cName} was protected by ${sName}!`))] };
    }

    case 'FocusEnergyApplied': {
      const cName = payload.creatureName as string;
      return { steps: [w(120), d(log(`${cName} is getting pumped!`))] };
    }

    case 'BideStoring': {
      const cName = payload.creatureName as string;
      return { steps: [w(120), d(log(`${cName} is storing energy!`))] };
    }

    case 'LeechSeedApplied': {
      const tName = payload.targetName as string;
      return { steps: [w(120), d(log(`${tName} was seeded!`))] };
    }

    case 'LeechSeedDamage': {
      const dName  = payload.drainedName as string;
      const hpAftr = payload.hpAfter as number;
      return { steps: [d({ type: 'UPDATE_HP', name: dName, hp: hpAftr }), w(400), d(log(`${dName}'s health was sapped by Leech Seed!`))] };
    }

    case 'LeechSeedHealed': {
      const hName  = payload.healedName as string;
      const hpAftr = payload.hpAfter as number;
      return { steps: [d({ type: 'UPDATE_HP', name: hName, hp: hpAftr })] };
    }

    case 'Recharging': {
      const cName = payload.creatureName as string;
      return { steps: [w(120), d(log(`${cName} must recharge!`))] };
    }

    case 'BindingStarted': {
      const tName = payload.targetName as string;
      const mName = payload.moveName as string;
      return { steps: [w(120), d(log(`${tName} was squeezed by ${formatMoveName(mName)}!`))] };
    }

    case 'BindingBlocked': {
      const cName = payload.creatureName as string;
      return { steps: [w(120), d(log(`${cName} is bound and can't move!`))] };
    }

    case 'FlinchBlocked': {
      const cName = payload.creatureName as string;
      return { steps: [w(120), d(log(`${cName} flinched and couldn't move!`))] };
    }

    case 'ChargingUp': {
      const cName = payload.creatureName as string;
      const mName = payload.moveName as string;
      return { steps: [w(120), d(log(chargingMsg(cName, mName)))] };
    }

    case 'MoveDisabled': {
      const tName = payload.targetName as string;
      const mName = payload.moveName as string;
      return { steps: [w(120), d(log(`${tName}'s ${formatMoveName(mName)} was disabled!`))] };
    }

    case 'MoveReEnabled': {
      const cName = payload.creatureName as string;
      const mName = payload.moveName as string;
      return { steps: [w(120), d(log(`${cName}'s ${formatMoveName(mName)} is no longer disabled!`))] };
    }

    case 'MistApplied': {
      const cName = payload.creatureName as string;
      return { steps: [emit({ type: 'playStatusSound' }), w(300), d(log(`${cName} became shrouded in mist!`))] };
    }

    case 'StatDropBlocked': {
      const cName = payload.creatureName as string;
      return { steps: [w(120), d(log(`${cName} is protected by the mist!`))] };
    }

    default:
      return {};
  }
}

// ── Driver (the only place with timers / bridge access) ─────────────────────

// Under E2E, collapse the cadence delays so specs run fast while preserving step
// ordering (a tiny non-zero value keeps the microtask/timer sequence intact).
const delay = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, E2E ? Math.min(ms, 12) : ms));

// Resolve when Phaser signals the animation is done — but never hang the battle
// log if that signal is lost (e.g. a stale scene listener after an HMR remount).
// E2E uses a short fallback: headless WebGL often never fires animationComplete,
// so this also bounds per-move time (keeps a small gap for ordering assertions).
function waitForBridge(timeoutMs = E2E ? 100 : 3000): Promise<void> {
  return new Promise<void>(resolve => {
    let settled = false;
    let timer: ReturnType<typeof setTimeout>;
    const finish = () => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      bridge.off('animationComplete', handler);
      resolve();
    };
    const handler = () => finish();
    timer = setTimeout(finish, timeoutMs);
    bridge.on('animationComplete', handler);
  });
}

function emitCommand(c: BridgeCommand): void {
  switch (c.type) {
    case 'playMoveAnimation':  bridge.emit('playMoveAnimation', { attackerSide: c.attackerSide, targetSide: c.targetSide }); break;
    case 'playFaintAnimation': bridge.emit('playFaintAnimation', { side: c.side }); break;
    case 'playDamageShake':    bridge.emit('playDamageShake', { side: c.side }); break;
    case 'playHitSound':       bridge.emit('playHitSound', { isCrit: c.isCrit }); break;
    case 'playStatusSound':    bridge.emit('playStatusSound', undefined); break;
    case 'playLevelUpSound':   bridge.emit('playLevelUpSound', undefined); break;
    case 'spawnEnemy':         bridge.emit('spawnEnemy', { enemySpeciesId: c.enemySpeciesId }); break;
    case 'transformSprite':    bridge.emit('transformSprite', { side: c.side, speciesId: c.speciesId }); break;
    case 'resetPlayerSprite':  bridge.emit('resetPlayerSprite', undefined); break;
    case 'playEvolutionAnimation': bridge.emit('playEvolutionAnimation', { toSpeciesId: c.toSpeciesId }); break;
  }
}

type Dispatch = (action: Action) => void;

async function execStep(step: Step, dispatch: Dispatch): Promise<void> {
  switch (step.kind) {
    case 'dispatch':  dispatch(step.action); return;
    case 'emit':      emitCommand(step.command); return;
    case 'wait':      await delay(step.ms); return;
    case 'awaitAnim': await waitForBridge(); return;
  }
}

/**
 * Plays queued timeline steps one at a time. Returns an `enqueue` function that
 * appends steps and kicks off draining if idle. A failing step is logged and
 * skipped so a hiccup can never freeze the battle log.
 */
export function useBattleTimeline(dispatch: Dispatch) {
  const queueRef   = useRef<Step[]>([]);
  const runningRef = useRef(false);

  const drain = useCallback(async () => {
    runningRef.current = true;
    dispatch({ type: 'ANIMATING_START' });
    try {
      while (queueRef.current.length > 0) {
        const step = queueRef.current.shift()!;
        try {
          await execStep(step, dispatch);
        } catch (err) {
          console.error('[battle timeline] step failed, continuing:', err);
        }
      }
    } finally {
      runningRef.current = false;
      dispatch({ type: 'ANIMATING_DONE' });
    }
  }, [dispatch]);

  return useCallback((steps: Step[]) => {
    if (steps.length === 0) return;
    queueRef.current.push(...steps);
    if (!runningRef.current) drain();
  }, [drain]);
}
