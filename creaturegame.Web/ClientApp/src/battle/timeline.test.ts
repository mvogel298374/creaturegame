import { describe, it, expect } from 'vitest';
import { expandEvent, type Step, type Action } from './timeline';

const CTX = { playerName: 'MEWTWO' };

// ── helpers to read a timeline without caring about the exact wait/emit shape ──
const logLines = (steps: Step[] = []): string[] =>
  steps.filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
       .map(s => s.action)
       .filter((a): a is Extract<Action, { type: 'LOG' }> => a.type === 'LOG')
       .map(a => a.message);

const kinds = (steps: Step[] = []): string[] => steps.map(s => s.kind);

describe('expandEvent — move-name formatting', () => {
  it('formats the slug in "used" lines', () => {
    const { steps } = expandEvent('MoveUsed', { attackerName: 'MEWTWO', moveName: 'fury-attack' }, CTX);
    expect(logLines(steps)).toEqual(['MEWTWO used FURY ATTACK!']);
  });

  it('announces the move first, then beats, then plays the lunge (Gen 1 order)', () => {
    const { steps } = expandEvent('MoveUsed', { attackerName: 'MEWTWO', moveName: 'tackle' }, CTX);
    // dispatch(LOG "used") → wait → emit(lunge) → awaitAnim — text must precede animation.
    expect(kinds(steps)).toEqual(['dispatch', 'wait', 'emit', 'awaitAnim']);
  });

  it('formats the slug in "missed" lines', () => {
    const { steps } = expandEvent('MoveMissed', { attackerName: 'ARTICUNO', moveName: 'rolling-kick' }, CTX);
    expect(logLines(steps)).toEqual(["ARTICUNO's ROLLING KICK missed!"]);
  });
});

describe('expandEvent — DamageDealt', () => {
  it('immunity reads the Gen 1 line, with no hit sound and no damage number', () => {
    const { steps } = expandEvent('DamageDealt',
      { targetName: 'ARTICUNO', damage: 0, typeEffectiveness: 0, hpAfter: 100, isCrit: false }, CTX);
    expect(logLines(steps)).toEqual(["It doesn't affect ARTICUNO..."]);
    expect(steps!.some(s => s.kind === 'emit')).toBe(false);          // no playHitSound
    expect(logLines(steps)[0]).not.toContain('damage');
  });

  it('appends crit + effectiveness suffixes in order', () => {
    const { steps } = expandEvent('DamageDealt',
      { targetName: 'ARTICUNO', damage: 42, typeEffectiveness: 2, hpAfter: 58, isCrit: true }, CTX);
    expect(logLines(steps)).toEqual(["ARTICUNO took 42 damage! A critical hit! It's super effective!"]);
  });

  it('uses "not very effective" for resisted hits', () => {
    const { steps } = expandEvent('DamageDealt',
      { targetName: 'ARTICUNO', damage: 10, typeEffectiveness: 0.5, hpAfter: 90, isCrit: false }, CTX);
    expect(logLines(steps)).toEqual(["ARTICUNO took 10 damage! It's not very effective..."]);
  });

  it('emits the hit sound and updates HP before the log line', () => {
    const { steps } = expandEvent('DamageDealt',
      { targetName: 'ARTICUNO', damage: 20, typeEffectiveness: 1, hpAfter: 80, isCrit: false }, CTX);
    expect(kinds(steps)).toEqual(['emit', 'dispatch', 'wait', 'dispatch', 'wait']);
  });
});

describe('expandEvent — two-turn charge lines', () => {
  it('uses move-specific Gen 1 charge text', () => {
    expect(logLines(expandEvent('ChargingUp', { creatureName: 'MEWTWO', moveName: 'dig' }, CTX).steps))
      .toEqual(['MEWTWO dug a hole!']);
    expect(logLines(expandEvent('ChargingUp', { creatureName: 'MEWTWO', moveName: 'solar-beam' }, CTX).steps))
      .toEqual(['MEWTWO took in sunlight!']);
  });
});

describe('expandEvent — stat stages', () => {
  it('says "sharply" for a two-stage change', () => {
    expect(logLines(expandEvent('StatStageChanged', { creatureName: 'MEWTWO', stat: 'Attack', delta: 2 }, CTX).steps))
      .toEqual(["MEWTWO's Attack sharply rose!"]);
  });
  it('plain "fell" for a single-stage drop', () => {
    expect(logLines(expandEvent('StatStageChanged', { creatureName: 'ARTICUNO', stat: 'Defense', delta: -1 }, CTX).steps))
      .toEqual(["ARTICUNO's Defense fell!"]);
  });
});

describe('expandEvent — control plane vs timeline', () => {
  it('BattleStarted dispatches immediately (now) with the VS log, no steps', () => {
    const { now, steps } = expandEvent('BattleStarted',
      { playerName: 'MEWTWO', enemyName: 'ARTICUNO', enemySpeciesId: 144, enemyLevel: 50 }, CTX);
    expect(now?.map(a => a.type)).toEqual(['BATTLE_STARTED', 'LOG']);
    expect(steps).toBeUndefined();
  });

  it('BattleEnded flips phase immediately but logs the winner via the timeline', () => {
    const { now, steps } = expandEvent('BattleEnded', { winnerName: 'MEWTWO' }, CTX);
    expect(now).toEqual([{ type: 'BATTLE_ENDED', winner: 'MEWTWO' }]);
    expect(logLines(steps)).toEqual(['MEWTWO wins!']);
  });
});

describe('expandEvent — faint side resolves against the player name', () => {
  it('player faint targets the player sprite', () => {
    const { steps } = expandEvent('CreatureFainted', { name: 'MEWTWO' }, CTX);
    const emit = steps!.find(s => s.kind === 'emit') as Extract<Step, { kind: 'emit' }>;
    expect(emit.command).toEqual({ type: 'playFaintAnimation', side: 'player' });
  });
  it('enemy faint targets the enemy sprite', () => {
    const { steps } = expandEvent('CreatureFainted', { name: 'ARTICUNO' }, CTX);
    const emit = steps!.find(s => s.kind === 'emit') as Extract<Step, { kind: 'emit' }>;
    expect(emit.command).toEqual({ type: 'playFaintAnimation', side: 'enemy' });
  });
});
