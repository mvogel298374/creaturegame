import { describe, it, expect } from 'vitest';
import { expandEvent, healSummary, type Step, type Action } from './timeline';

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

// The dispatched actions (control-plane), for asserting reducer actions like SET_GOLD / SHOW_DROP.
const dispatched = (steps: Step[] = []): Action[] =>
  steps.filter((s): s is Extract<Step, { kind: 'dispatch' }> => s.kind === 'dispatch')
       .map(s => s.action);

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

  it('CreatureFled (foe scared off) logs the wild flee and resets the player sprite', () => {
    // Roar/Whirlwind ended the wild battle. We skip BattleEnded on a flee, so this is where the player
    // sprite reverts (in case it Transformed). The foe-fled branch words it as the wild Pokémon fleeing.
    const { now, steps } = expandEvent('CreatureFled', { name: 'PIDGEY', isPlayer: false }, CTX);
    expect(now).toBeUndefined();
    expect(logLines(steps)).toEqual(['PIDGEY fled!']);
    expect(emits(steps)).toContainEqual({ type: 'resetPlayerSprite' });
  });

  it('CreatureFled (player blown away) words it from the player side', () => {
    const { steps } = expandEvent('CreatureFled', { name: 'MEWTWO', isPlayer: true }, CTX);
    expect(logLines(steps)).toEqual(['MEWTWO was blown away!']);
    expect(emits(steps)).toContainEqual({ type: 'resetPlayerSprite' });
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

  it('BiomeEntered titles the next leg of the route and traces it on the map (by id + name)', () => {
    const { steps } = expandEvent('BiomeEntered', {
      biomeId: 'phantom-marsh', biomeName: 'Phantom Marsh', types: ['Ghost', 'Poison'],
    }, CTX);
    expect(logLines(steps)).toEqual(['Entered Phantom Marsh!']);
    expect(dispatched(steps)).toContainEqual({ type: 'MAP_BIOME_ENTERED', biomeId: 'phantom-marsh', biomeName: 'Phantom Marsh' });
  });

  it.each([
    ['EliteBattle', 'An Elite trainer blocks the path!'],
    ['BossBattle',  'The biome boss looms ahead!'],
    ['Shop',        'You happened upon a shop.'],
    ['Treasure',    'You found a treasure cache!'],
    ['Mystery',     'Something mysterious stirs…'],
  ])('RunNodeEntered banners the %s node in the event tone and advances the map pin', (kind, message) => {
    const { steps } = expandEvent('RunNodeEntered', { kind }, CTX);
    expect(logLines(steps)).toEqual([message]);
    // Nonstandard encounters (Elite/Boss) and non-battle nodes get the special-event colour.
    expect(logTones(steps)).toEqual(['event']);
    // …and every node advances the encounter-map pin one step.
    expect(dispatched(steps)).toContainEqual({ type: 'MAP_NODE_ENTERED' });
  });

  it('a WildBattle node advances the map pin but shows no banner (reads as a plain wild encounter)', () => {
    const { steps } = expandEvent('RunNodeEntered', { kind: 'WildBattle' }, CTX);
    expect(dispatched(steps)).toEqual([{ type: 'MAP_NODE_ENTERED' }]);
    expect(logLines(steps)).toEqual([]);
  });

  it('BiomeNodePlanRevealed feeds the ladder its seeded node plan (no battle-log line)', () => {
    const { steps } = expandEvent(
      'BiomeNodePlanRevealed', { nodeKinds: ['WildBattle', 'Shop', 'BossBattle'] }, CTX);
    expect(dispatched(steps)).toEqual([
      { type: 'MAP_PLAN_REVEALED', nodeKinds: ['WildBattle', 'Shop', 'BossBattle'] },
    ]);
    expect(logLines(steps)).toEqual([]);
  });

  it('RegionMapRevealed feeds the region-map overlay the playable biome graph (id/types/edges/coords)', () => {
    const { steps } = expandEvent('RegionMapRevealed', {
      biomes: [
        { id: 'a', name: 'Alpha', types: ['Fire'], neighbours: ['b'], mapX: 10, mapY: 20 },
        { id: 'b', name: 'Beta', types: ['Water'], neighbours: ['a'], mapX: 30, mapY: 40 },
      ],
    }, CTX);
    expect(dispatched(steps)).toEqual([
      { type: 'REGION_MAP_REVEALED', biomes: [
        { id: 'a', name: 'Alpha', types: ['Fire'], neighbours: ['b'], x: 10, y: 20 },
        { id: 'b', name: 'Beta', types: ['Water'], neighbours: ['a'], x: 30, y: 40 },
      ] },
    ]);
    expect(logLines(steps)).toEqual([]);
  });

  it('RecoveryOffered advances the map pin onto the synthesized Poké Center (Rest) cap', () => {
    // The Poké Center caps every biome after the Boss but is NOT a plan node (ENCOUNTER_DESIGN.md §5), so the
    // ladder synthesizes a terminal Rest step — and RecoveryOffered walks the pin onto it. Pins that dispatch so
    // a future edit can't silently drop it (the ladder would then stall on the Boss).
    const { steps } = expandEvent(
      'RecoveryOffered', { creatureName: 'MEWTWO', speciesId: 150, battlesWon: 3 }, CTX);
    expect(dispatched(steps)).toContainEqual({ type: 'MAP_NODE_ENTERED' });
  });

  it('RewardGranted (battle drop) bumps the gold total, logs a loot line, and raises the drop hover', () => {
    const { steps } = expandEvent('RewardGranted',
      { source: 'Battle', gold: 25, goldTotal: 25, itemNames: ['Potion'] }, CTX);
    expect(logLines(steps)).toEqual(['Found 25G, Potion!']);
    expect(logTones(steps)).toEqual(['loot']); // reward lines are yellow
    const actions = dispatched(steps);
    expect(actions).toContainEqual({ type: 'SET_GOLD', gold: 25 });
    expect(actions).toContainEqual({ type: 'SHOW_DROP', gold: 25, itemNames: ['Potion'] });
  });

  it('RewardGranted (empty drop) still logs but raises no drop hover', () => {
    const { steps } = expandEvent('RewardGranted',
      { source: 'Battle', gold: 0, goldTotal: 12, itemNames: [] }, CTX);
    expect(logLines(steps)).toEqual(['Found nothing this time!']);
    const actions = dispatched(steps);
    expect(actions).toContainEqual({ type: 'SET_GOLD', gold: 12 });
    // Nothing actually dropped → no hover (it would be an empty popup).
    expect(actions.map(a => a.type)).not.toContain('SHOW_DROP');
  });

  it('RewardGranted (Treasure node) uses the SAME drop hover as a battle drop — no blocking modal', () => {
    const { steps } = expandEvent('RewardGranted',
      { source: 'Treasure', gold: 40, goldTotal: 65, itemNames: ['Full Restore'] }, CTX);
    expect(logLines(steps)).toEqual(['The treasure held 40G, Full Restore!']);
    expect(logTones(steps)).toEqual(['loot']);
    const actions = dispatched(steps);
    expect(actions).toContainEqual({ type: 'SET_GOLD', gold: 65 });
    // The generic vanishing hover — the same one a battle drop raises (the OK modal is gone).
    expect(actions).toContainEqual({ type: 'SHOW_DROP', gold: 40, itemNames: ['Full Restore'] });
    expect(actions.map(a => a.type)).not.toContain('SHOW_REWARD');
  });

  it('RewardChoiceOffered parses the options off the wire and raises the choice modal', () => {
    const { steps } = expandEvent('RewardChoiceOffered', {
      source: 'Battle',
      options: [
        { kind: 'item', itemId: 25, itemName: 'hyper-potion', rarity: 'Rare', gold: 0 },
        { kind: 'item', itemId: 42, itemName: 'antidote', rarity: 'Common', gold: 0 },
        { kind: 'gold', itemId: 0, itemName: null, rarity: null, gold: 60 },
      ],
    }, CTX);
    const actions = dispatched(steps);
    expect(actions).toContainEqual({
      type: 'SHOW_REWARD_CHOICE',
      source: 'Battle',
      options: [
        { kind: 'item', itemId: 25, itemName: 'hyper-potion', rarity: 'Rare', gold: 0, hpRestore: 0, cureStatus: false, restoreLowPp: false, label: null },
        { kind: 'item', itemId: 42, itemName: 'antidote', rarity: 'Common', gold: 0, hpRestore: 0, cureStatus: false, restoreLowPp: false, label: null },
        { kind: 'gold', itemId: 0, itemName: null, rarity: null, gold: 60, hpRestore: 0, cureStatus: false, restoreLowPp: false, label: null },
      ],
    });
  });

  it('RewardChoiceOffered parses a Quick Heal option (kind "heal") with its restore fields off the wire', () => {
    const { steps } = expandEvent('RewardChoiceOffered', {
      source: 'Battle',
      options: [
        { kind: 'heal', itemId: 0, itemName: null, rarity: null, gold: 0, hpRestore: 24, cureStatus: true, restoreLowPp: false, label: 'Quick Heal' },
        { kind: 'gold', itemId: 0, itemName: null, rarity: null, gold: 60 },
      ],
    }, CTX);
    const actions = dispatched(steps);
    expect(actions).toContainEqual({
      type: 'SHOW_REWARD_CHOICE',
      source: 'Battle',
      options: [
        { kind: 'heal', itemId: 0, itemName: null, rarity: null, gold: 0, hpRestore: 24, cureStatus: true, restoreLowPp: false, label: 'Quick Heal' },
        { kind: 'gold', itemId: 0, itemName: null, rarity: null, gold: 60, hpRestore: 0, cureStatus: false, restoreLowPp: false, label: null },
      ],
    });
  });

  it('ShopOffered parses the stock off the wire and raises the shop modal with the balance', () => {
    const { now, steps } = expandEvent('ShopOffered', {
      balance: 142,
      items: [
        { itemId: 17, itemName: 'potion', price: 8, rarity: 'Common' },
        { itemId: 20, itemName: 'elixir', price: 90, rarity: 'Epic' },
      ],
    }, CTX);
    expect(now).toBeUndefined(); // queued through the timeline, not dispatched immediately
    const actions = dispatched(steps);
    expect(actions).toContainEqual({
      type: 'SHOW_SHOP',
      balance: 142,
      items: [
        { itemId: 17, itemName: 'potion', price: 8, rarity: 'Common' },
        { itemId: 20, itemName: 'elixir', price: 90, rarity: 'Epic' },
      ],
    });
  });

  it('ShopOffered tolerates a missing items list', () => {
    const { steps } = expandEvent('ShopOffered', { balance: 50 }, CTX);
    const actions = dispatched(steps);
    expect(actions).toContainEqual({ type: 'SHOW_SHOP', balance: 50, items: [] });
  });

  it('ShopItemPurchased updates the balance/gold and logs the buy — modal stays open (no HIDE_SHOP)', () => {
    const { steps } = expandEvent('ShopItemPurchased',
      { itemName: 'super-potion', price: 20, balance: 122 }, CTX);
    expect(logLines(steps)).toEqual(['Bought super-potion for 20₽!']);
    expect(logTones(steps)).toEqual(['loot']);
    const actions = dispatched(steps);
    expect(actions).toContainEqual({ type: 'SHOP_PURCHASED', itemName: 'super-potion', price: 20, balance: 122 });
    // The modal stays open across buys — closing is the player's Leave, not a purchase.
    expect(actions.map(a => a.type)).not.toContain('HIDE_SHOP');
  });

  it('RewardGranted (Mystery, nothing rolled) still bumps gold and logs, but raises no hover', () => {
    const { steps } = expandEvent('RewardGranted',
      { source: 'Mystery', gold: 0, goldTotal: 10, itemNames: [] }, CTX);
    expect(logLines(steps)).toEqual(['The mystery held nothing this time!']);
    const actions = dispatched(steps);
    expect(actions).toContainEqual({ type: 'SET_GOLD', gold: 10 });
    // An empty node reward: no hover (nothing to show), and no modal (it's gone).
    expect(actions.map(a => a.type)).not.toContain('SHOW_DROP');
    expect(actions.map(a => a.type)).not.toContain('SHOW_REWARD');
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

describe('healSummary — Quick Heal reward card tag', () => {
  it('lists only the components the option carries, in HP · CURE · PP order', () => {
    expect(healSummary({ hpRestore: 24, cureStatus: true, restoreLowPp: true })).toBe('+24 HP · CURE · PP');
  });

  it('omits the HP part when nothing is restored', () => {
    expect(healSummary({ hpRestore: 0, cureStatus: true, restoreLowPp: false })).toBe('CURE');
    expect(healSummary({ hpRestore: 0, cureStatus: false, restoreLowPp: true })).toBe('PP');
  });

  it('shows only HP when that is the sole component', () => {
    expect(healSummary({ hpRestore: 12, cureStatus: false, restoreLowPp: false })).toBe('+12 HP');
  });

  it('falls back to RESTORE when (defensively) nothing is set', () => {
    expect(healSummary({ hpRestore: 0, cureStatus: false, restoreLowPp: false })).toBe('RESTORE');
  });
});

describe('expandEvent — party & acquisition (Phase 4 Stage 1c)', () => {
  const dispatchedOf = (steps: Step[] = [], type: string): Action[] =>
    dispatched(steps).filter(a => a.type === type);

  it('AcquisitionOffered parses the offer and raises the blocking modal', () => {
    const { steps } = expandEvent('AcquisitionOffered', {
      source: 'ThemedDraft', speciesId: 25, name: 'PIKACHU', level: 12, types: ['Electric'],
      maxHp: 34, partyFull: true,
      party: [{ speciesId: 4, name: 'CHARMANDER', level: 14, hp: 20, maxHp: 40, status: 'Burn', isLead: true }],
    }, CTX);
    const show = dispatchedOf(steps, 'SHOW_ACQUISITION')[0] as Extract<Action, { type: 'SHOW_ACQUISITION' }>;
    expect(show.offer.name).toBe('PIKACHU');
    expect(show.offer.partyFull).toBe(true);
    expect(show.offer.types).toEqual(['Electric']);
    expect(show.offer.party[0].name).toBe('CHARMANDER');
    expect(show.offer.party[0].isLead).toBe(true);
  });

  it('PartyUpdated dispatches PARTY_SET with the parsed members (no log line)', () => {
    const { steps } = expandEvent('PartyUpdated', {
      members: [
        { speciesId: 6, name: 'CHARIZARD', level: 36, hp: 100, maxHp: 120, status: 'None', isLead: true },
        { speciesId: 9, name: 'BLASTOISE', level: 34, hp: 0, maxHp: 110, status: 'None', isLead: false },
      ],
    }, CTX);
    const set = dispatchedOf(steps, 'PARTY_SET')[0] as Extract<Action, { type: 'PARTY_SET' }>;
    expect(set.members).toHaveLength(2);
    expect(set.members[1].name).toBe('BLASTOISE');
    expect(set.members[1].hp).toBe(0);
    expect(logLines(steps)).toEqual([]); // roster refresh carries no battle-log line
  });

  it('CreatureAcquired logs "joined the party" — with the released member on a full-party swap', () => {
    expect(logLines(expandEvent('CreatureAcquired',
      { name: 'PIKACHU', speciesId: 25, replaced: false, replacedName: null }, CTX).steps))
      .toEqual(['PIKACHU joined the party!']);
    expect(logLines(expandEvent('CreatureAcquired',
      { name: 'PIKACHU', speciesId: 25, replaced: true, replacedName: 'RATTATA' }, CTX).steps))
      .toEqual(['PIKACHU joined the party! (RATTATA was released.)']);
  });

  it('AcquisitionDeclined logs the left-in-the-wild line', () => {
    expect(logLines(expandEvent('AcquisitionDeclined', { name: 'PIKACHU' }, CTX).steps))
      .toEqual(['Left PIKACHU in the wild.']);
  });
});

describe('expandEvent — between-biome lead choice (Stage 1d)', () => {
  const dispatchedOf = (steps: Step[] = [], type: string): Action[] =>
    dispatched(steps).filter(a => a.type === type);

  it('LeadChoiceOffered raises the blocking lead picker with the parsed roster', () => {
    const { steps } = expandEvent('LeadChoiceOffered', {
      party: [
        { speciesId: 6, name: 'CHARIZARD', level: 36, hp: 100, maxHp: 120, status: 'None', isLead: true },
        { speciesId: 9, name: 'BLASTOISE', level: 34, hp: 80, maxHp: 110, status: 'Poison', isLead: false },
      ],
    }, CTX);
    const show = dispatchedOf(steps, 'SHOW_LEAD_CHOICE')[0] as Extract<Action, { type: 'SHOW_LEAD_CHOICE' }>;
    expect(show.party).toHaveLength(2);
    expect(show.party[0].isLead).toBe(true);
    expect(show.party[1].name).toBe('BLASTOISE');
    expect(show.party[1].status).toBe('Poison');
  });

  it('LeadChanged logs the "is now your lead" line', () => {
    expect(logLines(expandEvent('LeadChanged', { name: 'BLASTOISE', speciesId: 9 }, CTX).steps))
      .toEqual(['BLASTOISE is now your lead!']);
  });
});

describe('expandEvent — forced faint-switch (Stage 3)', () => {
  const dispatchedOf = (steps: Step[] = [], type: string): Action[] =>
    dispatched(steps).filter(a => a.type === type);

  it('SwitchInOffered raises the blocking forced picker with the parsed roster + fainted name', () => {
    const { steps } = expandEvent('SwitchInOffered', {
      faintedName: 'CHARIZARD',
      party: [
        { speciesId: 6, name: 'CHARIZARD', level: 36, hp: 0, maxHp: 120, status: 'None', isLead: true },
        { speciesId: 9, name: 'BLASTOISE', level: 34, hp: 90, maxHp: 110, status: 'Poison', isLead: false },
      ],
    }, CTX);
    const show = dispatchedOf(steps, 'SHOW_SWITCH_IN')[0] as Extract<Action, { type: 'SHOW_SWITCH_IN' }>;
    expect(show.faintedName).toBe('CHARIZARD');
    expect(show.party).toHaveLength(2);
    expect(show.party[0].hp).toBe(0); // the fainted lead — the modal disables it
    expect(show.party[1].name).toBe('BLASTOISE');
  });

  it('CreatureSwitchedIn swaps the player sprite, retargets the nameplate, and logs the send-in', () => {
    const { steps } = expandEvent('CreatureSwitchedIn',
      { name: 'BLASTOISE', speciesId: 9, level: 34, hp: 90, maxHp: 110, status: 'Poison' }, CTX);
    const swap = emits(steps).find(c => c.type === 'swapPlayerCreature') as Extract<
      import('./timeline').BridgeCommand, { type: 'swapPlayerCreature' }
    >;
    expect(swap.speciesId).toBe(9);
    const set = dispatchedOf(steps, 'SWITCHED_IN')[0] as Extract<Action, { type: 'SWITCHED_IN' }>;
    expect(set).toMatchObject({ name: 'BLASTOISE', level: 34, hp: 90, maxHp: 110, status: 'Poison' });
    expect(logLines(steps)).toEqual(['Go! BLASTOISE!']);
  });
});
