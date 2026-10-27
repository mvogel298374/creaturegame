import { describe, it, expect } from 'vitest';
import type { Payload } from './timeline';
import { battleReducer, initialState, type BattleState } from '../hooks/battleReducer';
import { FRESH_INTAKE, intakeEvent, type Intake } from './eventIntake';
import { bossTrainerName } from './bossTrainer';

// The hook's own intake (eventIntake.ts) with each step applied in arrival order (RECONNECT_RESILIENCE.md R1d-d).
interface Client {
  state: BattleState;
  intake: Intake;
}

const FRESH: Client = { state: initialState, intake: FRESH_INTAKE };

function receive(client: Client, [eventType, payload]: [string, Payload]): Client {
  const taken = intakeEvent(client.intake, eventType, payload, {
    enemyId: client.state.enemyId,
    biomeName: client.state.mapBiomeName,
  });
  if (!taken) return client;
  let state = client.state;
  for (const action of taken.expansion.now ?? []) state = battleReducer(state, action);
  for (const step of taken.expansion.steps ?? []) if (step.kind === 'dispatch') state = battleReducer(state, step.action);
  return { state, intake: taken.intake };
}

const receiveAll = (client: Client, events: [string, Payload][]) => events.reduce(receive, client);

const PLAN = ['WildBattle', 'BossBattle'];

// What the server replays mid-boss-fight in the second biome: map, biome, plan, snapshot, battle.
const MID_BOSS_REPLAY: [string, Payload][] = [
  ['RegionMapRevealed', {
    width: 2, height: 1, routes: [],
    biomes: [
      { id: 'woods', name: 'Woods', types: ['Grass'], neighbours: ['marsh'], x: 0, y: 0 },
      { id: 'marsh', name: 'Marsh', types: ['Poison'], neighbours: ['woods'], x: 1, y: 0 },
    ],
  }],
  ['BiomeEntered', { biomeId: 'marsh', biomeName: 'Marsh', types: ['Poison'] }],
  ['BiomeNodePlanRevealed', { nodeKinds: PLAN }],
  ['RunMapSnapshot', { routePath: ['woods', 'marsh'], nodesEntered: 2, restReached: false, currentNodeKind: 'BossBattle' }],
  ['BattleStarted', {
    playerName: 'CHARIZARD', playerId: 1, playerSpeciesId: 6,
    enemyName: 'MUK', enemyId: 9, enemySpeciesId: 89, enemyLevel: 40,
  }],
];

const vsLines = (state: BattleState) => state.log.map(l => l.message).filter(m => m.includes(' VS '));

describe('a reconnect replay rebuilds the screen', () => {
  it('a refresh mid-boss restores the trail, the pin, the lead and the trainer framing the ladder shows', () => {
    const { state } = receiveAll(FRESH, MID_BOSS_REPLAY);

    expect(state.routePath).toEqual(['woods', 'marsh']);
    expect(state.mapPin).toBe(1);
    expect(state.playerSpeciesId).toBe(6);
    const ladderName = bossTrainerName(
      state.currentBiomeId, state.regionBiomes.find(b => b.id === state.currentBiomeId)?.types[0], state.mapNodePlan);
    expect(vsLines(state)).toEqual([`CHARIZARD VS Trainer ${ladderName}'s MUK`]);
  });

  it('a blip replays the same events and changes nothing', () => {
    const refreshed = receiveAll(FRESH, MID_BOSS_REPLAY);

    const { state } = receiveAll(refreshed, MID_BOSS_REPLAY);

    expect(state.routePath).toEqual(refreshed.state.routePath);
    expect(state.mapPin).toBe(refreshed.state.mapPin);
    expect(vsLines(state)).toHaveLength(1);
  });

  it('a snapshot before any node is entered leaves the pin off the ladder', () => {
    const { state } = receiveAll(FRESH, [
      ['BiomeEntered', { biomeId: 'woods', biomeName: 'Woods', types: ['Grass'] }],
      ['BiomeNodePlanRevealed', { nodeKinds: PLAN }],
      ['RunMapSnapshot', { routePath: ['woods'], nodesEntered: 0, restReached: false, currentNodeKind: null }],
    ]);

    expect(state.mapPin).toBe(-1);
  });

  it('a replayed Poké Center prompt leaves the pin on the Rest cap', () => {
    const { state } = receiveAll(FRESH, [
      ['BiomeEntered', { biomeId: 'woods', biomeName: 'Woods', types: ['Grass'] }],
      ['BiomeNodePlanRevealed', { nodeKinds: PLAN }],
      ['RunMapSnapshot', { routePath: ['woods'], nodesEntered: 2, restReached: true, currentNodeKind: 'BossBattle' }],
      ['RecoveryOffered', { creatureName: 'CHARIZARD', creatureId: 1, speciesId: 6, battlesWon: 3 }],
    ]);

    expect(state.mapPin).toBe(PLAN.length);
  });
});
