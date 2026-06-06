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

  it('reads the Gen 1 immunity line for a no-effect move', () => {
    const { steps } = expandEvent('MoveHadNoEffect', { targetName: 'GENGAR', moveName: 'seismic-toss' }, CTX);
    expect(logLines(steps)).toEqual(["It doesn't affect GENGAR..."]);
  });

  it('reads the Gen 1 "But nothing happened!" line for Splash', () => {
    const { steps } = expandEvent('ButNothingHappened', { creatureName: 'GYARADOS' }, CTX);
    expect(logLines(steps)).toEqual(['But nothing happened!']);
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

  it('TurnStarted flows through the timeline (queued), not immediately — so HP syncs after damage animates', () => {
    const { now, steps } = expandEvent('TurnStarted',
      { turnNumber: 2, playerHp: 100, playerMaxHp: 150, playerStatus: 'None', enemyHp: 80, enemyMaxHp: 120, enemyStatus: 'None', moves: [] }, CTX);
    expect(now).toBeUndefined();
    expect(steps).toHaveLength(1);
    expect(steps![0]).toMatchObject({ kind: 'dispatch', action: { type: 'TURN_STARTED', enemyHp: 80 } });
  });

  it('BattleEnded flips phase immediately but logs the winner via the timeline', () => {
    const { now, steps } = expandEvent('BattleEnded', { winnerName: 'MEWTWO' }, CTX);
    expect(now).toEqual([{ type: 'BATTLE_ENDED', winner: 'MEWTWO' }]);
    expect(logLines(steps)).toEqual(['MEWTWO wins!']);
  });
});

describe('expandEvent — crash / flinch / multi-hit', () => {
  it('CrashDamage updates the user HP before logging the Gen 1 crash line', () => {
    const { steps } = expandEvent('CrashDamage', { sourceName: 'HITMONLEE', damage: 1, hpAfter: 119 }, CTX);
    expect(kinds(steps)).toEqual(['dispatch', 'wait', 'dispatch']);
    expect(steps![0]).toMatchObject({ kind: 'dispatch', action: { type: 'UPDATE_HP', name: 'HITMONLEE', hp: 119 } });
    expect(logLines(steps)).toEqual(['HITMONLEE kept going and crashed!']);
  });

  it('FlinchBlocked logs the flinch line', () => {
    expect(logLines(expandEvent('FlinchBlocked', { creatureName: 'ARTICUNO' }, CTX).steps))
      .toEqual(["ARTICUNO flinched and couldn't move!"]);
  });

  it('MultiHitCompleted pluralises the hit count', () => {
    expect(logLines(expandEvent('MultiHitCompleted', { hits: 2 }, CTX).steps)).toEqual(['Hit 2 times!']);
    expect(logLines(expandEvent('MultiHitCompleted', { hits: 1 }, CTX).steps)).toEqual(['Hit 1 time!']);
  });
});

describe('expandEvent — Disable', () => {
  it('MoveDisabled formats the locked move name', () => {
    expect(logLines(expandEvent('MoveDisabled', { targetName: 'ARTICUNO', moveName: 'ice-beam' }, CTX).steps))
      .toEqual(["ARTICUNO's ICE BEAM was disabled!"]);
  });
  it('MoveReEnabled announces the move is usable again', () => {
    expect(logLines(expandEvent('MoveReEnabled', { creatureName: 'ARTICUNO', moveName: 'ice-beam' }, CTX).steps))
      .toEqual(["ARTICUNO's ICE BEAM is no longer disabled!"]);
  });
});

describe('expandEvent — Mist', () => {
  it('MistApplied plays the status sound and logs the line', () => {
    const { steps } = expandEvent('MistApplied', { creatureName: 'ARTICUNO' }, CTX);
    expect(steps!.some(s => s.kind === 'emit')).toBe(true);   // playStatusSound
    expect(logLines(steps)).toEqual(['ARTICUNO became shrouded in mist!']);
  });
  it('StatDropBlocked logs the Mist protection line', () => {
    expect(logLines(expandEvent('StatDropBlocked', { creatureName: 'ARTICUNO' }, CTX).steps))
      .toEqual(['ARTICUNO is protected by the mist!']);
  });
});

describe('expandEvent — Heal & Mimic', () => {
  it('Healed updates HP before the regained-health line', () => {
    const { steps } = expandEvent('Healed', { creatureName: 'CHANSEY', healAmount: 100, hpAfter: 150 }, CTX);
    expect(steps![0]).toMatchObject({ kind: 'dispatch', action: { type: 'UPDATE_HP', name: 'CHANSEY', hp: 150 } });
    expect(logLines(steps)).toEqual(['CHANSEY regained health!']);
  });
  it('MimicLearned formats the copied move name', () => {
    expect(logLines(expandEvent('MimicLearned', { creatureName: 'DITTO', moveName: 'ice-beam' }, CTX).steps))
      .toEqual(['DITTO learned ICE BEAM!']);
  });
});

describe('expandEvent — Screens, Focus Energy & Bide', () => {
  it('ScreenApplied plays the status sound and logs the screen name', () => {
    const { steps } = expandEvent('ScreenApplied', { creatureName: 'MEW', screenName: 'Reflect' }, CTX);
    expect(steps!.some(s => s.kind === 'emit')).toBe(true);   // playStatusSound
    expect(logLines(steps)).toEqual(['MEW was protected by Reflect!']);
  });
  it('FocusEnergyApplied logs the pumped line', () => {
    expect(logLines(expandEvent('FocusEnergyApplied', { creatureName: 'MEW' }, CTX).steps))
      .toEqual(['MEW is getting pumped!']);
  });
  it('BideStoring logs the storing line', () => {
    expect(logLines(expandEvent('BideStoring', { creatureName: 'MEW' }, CTX).steps))
      .toEqual(['MEW is storing energy!']);
  });
});

describe('expandEvent — confusion & coins', () => {
  it('ConfusionStarted plays the status sound and logs the line', () => {
    const { steps } = expandEvent('ConfusionStarted', { targetName: 'ARTICUNO' }, CTX);
    expect(steps!.some(s => s.kind === 'emit')).toBe(true);   // playStatusSound
    expect(logLines(steps)).toEqual(['ARTICUNO became confused!']);
  });
  it('ConfusionMessage logs the per-turn confused line', () => {
    expect(logLines(expandEvent('ConfusionMessage', { creatureName: 'MEWTWO' }, CTX).steps))
      .toEqual(['MEWTWO is confused!']);
  });
  it('ConfusionDamage updates HP before the self-hit line', () => {
    const { steps } = expandEvent('ConfusionDamage', { creatureName: 'MEWTWO', damage: 12, hpAfter: 88 }, CTX);
    expect(steps![0]).toMatchObject({ kind: 'dispatch', action: { type: 'UPDATE_HP', name: 'MEWTWO', hp: 88 } });
    expect(logLines(steps)).toEqual(['MEWTWO hurt itself in confusion!']);
  });
  it('ConfusionCleared announces snapping out', () => {
    expect(logLines(expandEvent('ConfusionCleared', { creatureName: 'MEWTWO' }, CTX).steps))
      .toEqual(['MEWTWO snapped out of confusion!']);
  });
  it('CoinsScattered logs the Gen 1 Pay Day line', () => {
    expect(logLines(expandEvent('CoinsScattered', { sourceName: 'MEOWTH', amount: 100 }, CTX).steps))
      .toEqual(['Coins scattered everywhere!']);
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
