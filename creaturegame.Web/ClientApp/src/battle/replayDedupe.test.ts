import { describe, it, expect } from 'vitest';
import { isReplayOfKnownState, afterAcceptedEvent, type ReplayView } from './replayDedupe';

const fresh: ReplayView = { enemyId: 0, arrivedBiomeId: null, planBiomeId: null };
const live: ReplayView = { enemyId: 7, arrivedBiomeId: 'phantom-marsh', planBiomeId: 'phantom-marsh' };

describe('isReplayOfKnownState', () => {
  it('applies every replayed event to a freshly-mounted client (the refresh case)', () => {
    expect(isReplayOfKnownState('BattleStarted', { enemyId: 7 }, fresh)).toBe(false);
    expect(isReplayOfKnownState('BiomeEntered', { biomeId: 'phantom-marsh' }, fresh)).toBe(false);
    expect(isReplayOfKnownState('BiomeNodePlanRevealed', {}, fresh)).toBe(false);
  });

  it('skips a BattleStarted for the enemy already on screen, but not a new encounter', () => {
    expect(isReplayOfKnownState('BattleStarted', { enemyId: 7 }, live)).toBe(true);
    expect(isReplayOfKnownState('BattleStarted', { enemyId: 8 }, live)).toBe(false);
  });

  it('skips a BiomeEntered for the biome just entered, but not a hop to another (or back to a previous one)', () => {
    expect(isReplayOfKnownState('BiomeEntered', { biomeId: 'phantom-marsh' }, live)).toBe(true);
    expect(isReplayOfKnownState('BiomeEntered', { biomeId: 'meadow-trail' }, live)).toBe(false);
  });

  it('skips a node plan only when this biome already has one accepted', () => {
    expect(isReplayOfKnownState('BiomeNodePlanRevealed', {}, live)).toBe(true);
    expect(isReplayOfKnownState('BiomeNodePlanRevealed', {}, { ...live, planBiomeId: null })).toBe(false);
  });

  it('never skips TurnStarted — re-applying it is the replay self-correction — or unrelated events', () => {
    expect(isReplayOfKnownState('TurnStarted', { turnNumber: 3 }, live)).toBe(false);
    expect(isReplayOfKnownState('DamageDealt', {}, live)).toBe(false);
  });
});

describe('afterAcceptedEvent — the full replay sequence', () => {
  it('lets a genuine hop + plan through even though the previous biome had a plan, then skips their replay', () => {
    // Previous biome A fully set up; a genuine hop to B arrives, then B's plan (state would still show A's plan).
    let v: ReplayView = { enemyId: 7, arrivedBiomeId: 'a', planBiomeId: 'a' };
    expect(isReplayOfKnownState('BiomeEntered', { biomeId: 'b' }, v)).toBe(false);
    v = afterAcceptedEvent('BiomeEntered', { biomeId: 'b' }, v);
    expect(isReplayOfKnownState('BiomeNodePlanRevealed', {}, v)).toBe(false);
    v = afterAcceptedEvent('BiomeNodePlanRevealed', {}, v);
    // A reconnect now replays both — each is skipped.
    expect(isReplayOfKnownState('BiomeEntered', { biomeId: 'b' }, v)).toBe(true);
    expect(isReplayOfKnownState('BiomeNodePlanRevealed', {}, v)).toBe(true);
  });

  it('accepts a return to a previously-visited biome (A -> B -> A)', () => {
    let v: ReplayView = { enemyId: 7, arrivedBiomeId: 'b', planBiomeId: 'b' };
    expect(isReplayOfKnownState('BiomeEntered', { biomeId: 'a' }, v)).toBe(false);
    v = afterAcceptedEvent('BiomeEntered', { biomeId: 'a' }, v);
    expect(v).toEqual({ enemyId: 7, arrivedBiomeId: 'a', planBiomeId: null });
  });

  it('leaves the trackers alone for other events', () => {
    expect(afterAcceptedEvent('TurnStarted', {}, live)).toBe(live);
  });
});
