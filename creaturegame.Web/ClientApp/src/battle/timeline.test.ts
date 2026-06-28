import { describe, it, expect } from 'vitest';
import { expandEvent, type Step, type Action } from './timeline';

const CTX = { playerName: 'MEWTWO' };

// ── helpers to read a timeline without caring about the exact wait/emit shape ──
const logLines = (steps: Step[] = []): string[] =>
  steps.filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
       .map(s => s.action)
       .filter((a): a is Extract<Action, { type: 'LOG' }> => a.type === 'LOG')
       .map(a => a.message);

const logTones = (steps: Step[] = []): (string | undefined)[] =>
  steps.filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
       .map(s => s.action)
       .filter((a): a is Extract<Action, { type: 'LOG' }> => a.type === 'LOG')
       .map(a => a.tone);

const kinds = (steps: Step[] = []): string[] => steps.map(s => s.kind);

const emits = (steps: Step[] = []) =>
  steps.filter((s): s is Extract<Step, { kind: 'emit' }> => s.kind === 'emit').map(s => s.command);

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

  it('narrates the Substitute lifecycle (put up, soak a hit, fade)', () => {
    expect(logLines(expandEvent('SubstitutePutUp', { creatureName: 'CHANSEY', substituteHp: 90 }, CTX).steps))
      .toEqual(['CHANSEY put up a substitute!']);
    expect(logLines(expandEvent('SubstituteAbsorbedHit', { creatureName: 'CHANSEY', substituteHpAfter: 40 }, CTX).steps))
      .toEqual(['The substitute took damage for CHANSEY!']);
    expect(logLines(expandEvent('SubstituteFaded', { creatureName: 'CHANSEY' }, CTX).steps))
      .toEqual(["CHANSEY's substitute faded!"]);
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

  it('tags damage lines with a colour tone by effectiveness (super/weak/immune, none for neutral)', () => {
    const at = (eff: number) => logTones(expandEvent('DamageDealt',
      { targetName: 'ARTICUNO', damage: 10, typeEffectiveness: eff, hpAfter: 90, isCrit: false }, CTX).steps);
    expect(at(2)).toEqual(['super']);
    expect(at(0.5)).toEqual(['weak']);
    expect(at(0)).toEqual(['immune']);
    expect(at(1)).toEqual([undefined]); // neutral hit — default line colour
  });

  it('emits the hit sound + damage shake and updates HP before the log line', () => {
    const { steps } = expandEvent('DamageDealt',
      { targetName: 'ARTICUNO', damage: 20, typeEffectiveness: 1, hpAfter: 80, isCrit: false }, CTX);
    expect(kinds(steps)).toEqual(['emit', 'emit', 'dispatch', 'wait', 'dispatch', 'wait']);
  });

  it('shakes the struck sprite (enemy side here), and not on an immune no-hit', () => {
    const hit = expandEvent('DamageDealt',
      { targetName: 'ARTICUNO', damage: 20, typeEffectiveness: 1, hpAfter: 80, isCrit: false }, CTX);
    expect(emits(hit.steps)).toContainEqual({ type: 'playDamageShake', side: 'enemy' });

    const immune = expandEvent('DamageDealt',
      { targetName: 'ARTICUNO', damage: 0, typeEffectiveness: 0, hpAfter: 100, isCrit: false }, CTX);
    expect(emits(immune.steps).some(c => c.type === 'playDamageShake')).toBe(false);
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
  it('the first BattleStarted is queued (not immediate) with the VS log — the scene plays its own entry', () => {
    const { now, steps } = expandEvent('BattleStarted',
      { playerName: 'MEWTWO', enemyName: 'ARTICUNO', enemySpeciesId: 144, enemyLevel: 50 },
      { playerName: 'MEWTWO', encounterIndex: 1 });
    // Queued so it never jumps ahead of a draining animation queue; no spawnEnemy on the first encounter.
    expect(now).toBeUndefined();
    const dispatched = (steps ?? [])
      .filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
      .map(s => s.action.type);
    expect(dispatched).toEqual(['BATTLE_STARTED', 'LOG']);
    expect((steps ?? []).some(s => s.kind === 'emit')).toBe(false);
  });

  it('a chained BattleStarted (2nd+ encounter) announces the new challenger and emits a spawnEnemy command', () => {
    const { now, steps } = expandEvent('BattleStarted',
      { playerName: 'MEWTWO', enemyName: 'ARBOK', enemySpeciesId: 24, enemyLevel: 52 },
      { playerName: 'MEWTWO', encounterIndex: 2 });
    expect(now).toBeUndefined();
    // The challenger line lives HERE now (not on the previous BattleEnded), so it can't precede an
    // unresolved between-battle instance like the Poké Center recovery. It comes before the VS line.
    expect(logLines(steps)).toEqual(['A new challenger approaches!', 'MEWTWO VS ARBOK']);
    const spawn = (steps ?? []).find(
      (s): s is Extract<Step, { kind: 'emit' }> => s.kind === 'emit' && s.command.type === 'spawnEnemy');
    expect(spawn).toBeDefined();
    expect((spawn!.command as { enemySpeciesId: number }).enemySpeciesId).toBe(24);
  });

  it('TurnStarted flows through the timeline (queued), not immediately — so HP syncs after damage animates', () => {
    const { now, steps } = expandEvent('TurnStarted',
      { turnNumber: 2, playerHp: 100, playerMaxHp: 150, playerStatus: 'None', enemyHp: 80, enemyMaxHp: 120, enemyStatus: 'None', moves: [] }, CTX);
    expect(now).toBeUndefined();
    expect(steps).toHaveLength(1);
    expect(steps![0]).toMatchObject({ kind: 'dispatch', action: { type: 'TURN_STARTED', enemyHp: 80 } });
  });

  it('BattleEnded with the player winning is a silent intermission beat that reverts the player sprite', () => {
    // Endless chain: a player win is not the end — the next BattleStarted resumes play. It logs NOTHING here
    // (so a between-battle instance like the Poké Center recovery is never preceded by "a new challenger"),
    // but it does revert the player sprite in case it Transformed this battle.
    const { now, steps } = expandEvent('BattleEnded', { winnerName: 'MEWTWO' }, CTX);
    expect(now).toBeUndefined();
    expect(logLines(steps)).toEqual([]);
    expect(emits(steps)).toContainEqual({ type: 'resetPlayerSprite' });
  });

  it('BattleEnded with the player losing does nothing (RunEnded drives the game over)', () => {
    expect(expandEvent('BattleEnded', { winnerName: 'GENGAR' }, CTX)).toEqual({});
  });

  it('RunEnded logs the run summary and queues the game-over phase flip', () => {
    const { now, steps } = expandEvent(
      'RunEnded', { battlesWon: 3, finalLevel: 52, finalCreatureName: 'MEWTWO' }, CTX);
    expect(now).toBeUndefined();
    expect(logLines(steps)).toEqual(['MEWTWO fainted! Run over — 3 wins, reached level 52.']);
    const dispatched = (steps ?? [])
      .filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
      .map(s => s.action.type);
    expect(dispatched).toContain('RUN_ENDED');
  });

  it('RunEnded uses the singular "win" for a single victory', () => {
    const { steps } = expandEvent(
      'RunEnded', { battlesWon: 1, finalLevel: 6, finalCreatureName: 'PIDGEY' }, CTX);
    expect(logLines(steps)).toEqual(['PIDGEY fainted! Run over — 1 win, reached level 6.']);
  });

  it('RecoveryOffered announces the Poké Center and raises the heal modal (queued, blocking)', () => {
    const { now, steps } = expandEvent(
      'RecoveryOffered', { creatureName: 'MEWTWO', speciesId: 150, battlesWon: 3 }, CTX);
    expect(now).toBeUndefined();
    expect(logLines(steps)).toEqual(['MEWTWO reached a Poké Center!']);
    const show = (steps ?? []).find(
      (s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch' && s.action.type === 'SHOW_RECOVERY');
    expect(show).toBeDefined();
    const action = show!.action as Extract<Action, { type: 'SHOW_RECOVERY' }>;
    expect(action.speciesId).toBe(150);
    expect(action.battlesWon).toBe(3);
  });

  it('PlayerRecovered logs the Poké Center heal, refills HP to full, and clears the status badge', () => {
    const { now, steps } = expandEvent(
      'PlayerRecovered', { creatureName: 'MEWTWO', hpAfter: 150 }, CTX);
    expect(now).toBeUndefined();
    expect(logLines(steps)).toEqual(['MEWTWO was fully healed!']);
    const dispatched = (steps ?? [])
      .filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
      .map(s => s.action);
    expect(dispatched).toContainEqual({ type: 'UPDATE_HP', name: 'MEWTWO', hp: 150 });
    expect(dispatched).toContainEqual({ type: 'CLEAR_STATUS', name: 'MEWTWO' });
  });

  it('RecoveryDeclined logs the keep-going line', () => {
    const { steps } = expandEvent('RecoveryDeclined', { creatureName: 'MEWTWO' }, CTX);
    expect(logLines(steps)).toEqual(['MEWTWO decided to keep going!']);
  });

  it('BiomeChoiceOffered raises the map modal with the offered biomes (queued, blocking)', () => {
    const { now, steps } = expandEvent('BiomeChoiceOffered', {
      options: [
        { id: 'phantom-marsh', name: 'Phantom Marsh', types: ['Ghost', 'Poison'] },
        { id: 'tranquil-lake', name: 'Tranquil Lake', types: ['Water', 'Psychic'] },
      ],
    }, CTX);
    expect(now).toBeUndefined(); // queued through the timeline, not dispatched immediately
    const show = (steps ?? []).find(
      (s): s is Extract<Step, { kind: 'dispatch' }> =>
        s.kind === 'dispatch' && s.action.type === 'SHOW_BIOME_CHOICE');
    expect(show).toBeDefined();
    const action = show!.action as Extract<Action, { type: 'SHOW_BIOME_CHOICE' }>;
    expect(action.options.map(o => o.id)).toEqual(['phantom-marsh', 'tranquil-lake']);
    expect(action.options[0].types).toEqual(['Ghost', 'Poison']);
  });

  it('BiomeChoiceOffered tolerates a missing options list', () => {
    const { steps } = expandEvent('BiomeChoiceOffered', {}, CTX);
    const show = (steps ?? []).find(
      (s): s is Extract<Step, { kind: 'dispatch' }> =>
        s.kind === 'dispatch' && s.action.type === 'SHOW_BIOME_CHOICE');
    expect((show!.action as Extract<Action, { type: 'SHOW_BIOME_CHOICE' }>).options).toEqual([]);
  });

  it('BiomeEntered titles the next leg of the route', () => {
    const { steps } = expandEvent('BiomeEntered', {
      biomeId: 'phantom-marsh', biomeName: 'Phantom Marsh', types: ['Ghost', 'Poison'],
    }, CTX);
    expect(logLines(steps)).toEqual(['Entered Phantom Marsh!']);
  });

  it.each([
    ['EliteBattle', 'An Elite trainer blocks the path!'],
    ['BossBattle',  'The biome boss looms ahead!'],
    ['Shop',        'You happened upon a shop.'],
    ['Treasure',    'You found a treasure cache!'],
    ['Mystery',     'Something mysterious stirs…'],
  ])('RunNodeEntered banners the %s node', (kind, message) => {
    const { steps } = expandEvent('RunNodeEntered', { kind }, CTX);
    expect(logLines(steps)).toEqual([message]);
  });

  it('EvolutionOffered announces it and raises the Allow/Cancel modal (queued, blocking)', () => {
    const { steps } = expandEvent('EvolutionOffered', {
      fromName: 'MACHOKE', toName: 'MACHAMP', fromSpeciesId: 67, toSpeciesId: 68,
    }, CTX);
    expect(logLines(steps)).toEqual(['What? MACHOKE is evolving!']);
    const dispatched = (steps ?? [])
      .filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
      .map(s => s.action);
    expect(dispatched).toContainEqual({
      type: 'SHOW_EVOLUTION_PROMPT', fromName: 'MACHOKE', toName: 'MACHAMP', fromSpeciesId: 67, toSpeciesId: 68,
    });
  });

  it('EvolutionCancelled logs the stopped-evolving line', () => {
    const { steps } = expandEvent('EvolutionCancelled', { creatureName: 'MACHOKE' }, CTX);
    expect(logLines(steps)).toEqual(['MACHOKE stopped evolving.']);
  });

  it('CreatureEvolved plays the morph and waits for it before the confirm line (no duplicate "evolving" line)', () => {
    const { steps } = expandEvent('CreatureEvolved', {
      fromName: 'CHARMANDER', toName: 'CHARMELEON', fromSpeciesId: 4, toSpeciesId: 5,
    }, CTX);

    // The "is evolving!" line plays in the offer, not here — this arm only confirms.
    expect(logLines(steps)).toEqual(['CHARMANDER evolved into CHARMELEON!']);

    // The morph command carries the evolved species id...
    expect(emits(steps)).toContainEqual({ type: 'playEvolutionAnimation', toSpeciesId: 5 });

    // ...and the timeline awaits it BEFORE the "evolved into" confirm line lands on the new sprite.
    const animIdx = (steps ?? []).findIndex(s => s.kind === 'awaitAnim');
    const confirmIdx = (steps ?? []).findIndex(
      s => s.kind === 'dispatch' && s.action.type === 'LOG'
        && s.action.message === 'CHARMANDER evolved into CHARMELEON!',
    );
    expect(animIdx).toBeGreaterThanOrEqual(0);
    expect(animIdx).toBeLessThan(confirmIdx);
  });

  it('LeveledUp announces the level, plays the fanfare, and shows the stat-gain panel (no auto-hide)', () => {
    const { steps } = expandEvent('LeveledUp', {
      creatureName: 'MEWTWO', newLevel: 12, xpThisLevel: 40, xpToNextLevel: 100,
      stats: { maxHp: 130, attack: 78, defense: 65, special: 70, speed: 90 },
      statGains: { maxHp: 3, attack: 2, defense: 2, special: 1, speed: 2 },
    }, CTX);
    expect(logLines(steps)).toEqual(['MEWTWO grew to level 12!']);
    const dispatched = (steps ?? [])
      .filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
      .map(s => s.action.type);
    expect(dispatched).toContain('SHOW_LEVEL_UP');
    // The panel now persists until the player's next input — the timeline must NOT auto-hide it.
    expect(dispatched).not.toContain('HIDE_LEVEL_UP');

    // The quick level-up fanfare fires.
    expect((steps ?? []).some(s => s.kind === 'emit' && s.command.type === 'playLevelUpSound')).toBe(true);

    const show = (steps ?? []).find(
      (s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch' && s.action.type === 'SHOW_LEVEL_UP');
    const action = show!.action as Extract<Action, { type: 'SHOW_LEVEL_UP' }>;
    expect(action.level).toBe(12);
    expect(action.gains).toEqual({ maxHp: 3, attack: 2, defense: 2, special: 1, speed: 2 });
    expect(action.totals.maxHp).toBe(130);
  });
});

describe('expandEvent — level-up move learning', () => {
  it('MoveLearned formats the learned move name', () => {
    expect(logLines(expandEvent('MoveLearned', { creatureName: 'CHARMANDER', moveName: 'ember' }, CTX).steps))
      .toEqual(['CHARMANDER learned EMBER!']);
  });

  it('MoveReplacementRequired announces the attempt and raises the replace-move modal', () => {
    const { now, steps } = expandEvent('MoveReplacementRequired',
      { creatureName: 'CHARMANDER', newMoveName: 'ember', currentMoves: ['tackle', 'growl', 'tail-whip', 'scratch'] }, CTX);
    expect(now).toBeUndefined();
    expect(logLines(steps)).toEqual([
      'CHARMANDER is trying to learn EMBER!',
      'But CHARMANDER already knows 4 moves.',
    ]);
    const show = (steps ?? []).find(
      (s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch' && s.action.type === 'SHOW_MOVE_REPLACEMENT');
    expect(show).toBeDefined();
    const action = show!.action as Extract<Action, { type: 'SHOW_MOVE_REPLACEMENT' }>;
    expect(action.newMoveName).toBe('ember');
    expect(action.currentMoves).toEqual(['tackle', 'growl', 'tail-whip', 'scratch']);
  });

  it('MoveForgotten and MoveLearnDeclined read the canonical Gen 1 lines', () => {
    expect(logLines(expandEvent('MoveForgotten', { creatureName: 'CHARMANDER', moveName: 'growl' }, CTX).steps))
      .toEqual(['CHARMANDER forgot GROWL!']);
    expect(logLines(expandEvent('MoveLearnDeclined', { creatureName: 'CHARMANDER', moveName: 'ember' }, CTX).steps))
      .toEqual(['CHARMANDER did not learn EMBER.']);
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
  it('TransformedInto names both creatures and morphs the transforming side to the copied species', () => {
    // DITTO is the enemy here (player is MEWTWO) → the enemy front sprite morphs to species 25 (Pikachu).
    const { steps } = expandEvent('TransformedInto',
      { creatureName: 'DITTO', targetName: 'PIKACHU', intoSpeciesId: 25 }, CTX);
    expect(logLines(steps)).toEqual(['DITTO transformed into PIKACHU!']);
    expect(emits(steps)).toContainEqual({ type: 'transformSprite', side: 'enemy', speciesId: 25 });
  });
  it('TransformedInto morphs the player back sprite when the player transforms', () => {
    const { steps } = expandEvent('TransformedInto',
      { creatureName: 'MEWTWO', targetName: 'DITTO', intoSpeciesId: 132 }, CTX);
    expect(emits(steps)).toContainEqual({ type: 'transformSprite', side: 'player', speciesId: 132 });
  });
  it('ConvertedType reports the new type', () => {
    expect(logLines(expandEvent('ConvertedType', { creatureName: 'PORYGON', newType: 'Water' }, CTX).steps))
      .toEqual(['PORYGON changed its type to Water!']);
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

// The run-layer arms (Poké Center recovery, endless-chain game-over, the honest XP bar) are the newest
// features; the C# WebEventContractTests guards that each has an arm — these pin what the arm renders.
describe('expandEvent — run layer (recovery, run-end, XP)', () => {
  const actions = (steps: Step[] = []) =>
    steps.filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch').map(s => s.action);

  it('raises the Poké Center recovery modal after announcing it', () => {
    const { steps } = expandEvent('RecoveryOffered', { creatureName: 'MEWTWO', speciesId: 150, battlesWon: 3 }, CTX);
    expect(logLines(steps)).toEqual(['MEWTWO reached a Poké Center!']);
    expect(actions(steps)).toContainEqual({ type: 'SHOW_RECOVERY', creatureName: 'MEWTWO', speciesId: 150, battlesWon: 3 });
  });

  it('fills HP and clears any status when the heal is accepted (PlayerRecovered)', () => {
    const { steps } = expandEvent('PlayerRecovered', { creatureName: 'MEWTWO', hpAfter: 226 }, CTX);
    expect(logLines(steps)).toEqual(['MEWTWO was fully healed!']);
    expect(actions(steps)).toContainEqual({ type: 'UPDATE_HP', name: 'MEWTWO', hp: 226 });
    expect(actions(steps)).toContainEqual({ type: 'CLEAR_STATUS', name: 'MEWTWO' });
  });

  it('announces the run summary and flips to game-over on RunEnded (plural wins)', () => {
    const { steps } = expandEvent('RunEnded', { battlesWon: 5, finalLevel: 22, finalCreatureName: 'MEWTWO' }, CTX);
    expect(logLines(steps)).toEqual(['MEWTWO fainted! Run over — 5 wins, reached level 22.']);
    expect(actions(steps)).toContainEqual({ type: 'RUN_ENDED', battlesWon: 5, finalLevel: 22 });
  });

  it('uses the singular "win" in the run summary after exactly one win', () => {
    const { steps } = expandEvent('RunEnded', { battlesWon: 1, finalLevel: 8, finalCreatureName: 'PIKACHU' }, CTX);
    expect(logLines(steps)).toEqual(['PIKACHU fainted! Run over — 1 win, reached level 8.']);
  });

  it('fills the XP bar by the awarded amount on ExperienceGained', () => {
    const { steps } = expandEvent('ExperienceGained', { creatureName: 'MEWTWO', amount: 137 }, CTX);
    expect(logLines(steps)).toEqual(['MEWTWO gained 137 EXP. Points!']);
    expect(actions(steps)).toContainEqual({ type: 'XP_GAIN', amount: 137 });
  });
});

describe('expandEvent — items', () => {
  it('narrates an item use, uppercasing the slug', () => {
    const { steps } = expandEvent('ItemUsed', { itemName: 'super-potion', targetName: 'MEWTWO' }, CTX);
    expect(logLines(steps)).toEqual(['Used SUPER POTION on MEWTWO!']);
  });

  it('narrates a PP restore with the formatted move name', () => {
    const { steps } = expandEvent('PpRestored', { creatureName: 'MEWTWO', moveName: 'ice-beam', ppAfter: 24 }, CTX);
    expect(logLines(steps)).toEqual(["MEWTWO's ICE BEAM PP was restored!"]);
  });

  it('reads the Gen 1 "won\'t have any effect" line on a failed item use', () => {
    const { steps } = expandEvent('ItemUseFailed', { itemName: 'potion' }, CTX);
    expect(logLines(steps)).toEqual(["It won't have any effect!"]);
  });
});
