import { describe, it, expect } from 'vitest';
import { bossTrainerName } from './bossTrainer';

describe('bossTrainerName — themed gate-boss name', () => {
  it('is deterministic for the same biome + node plan (so the ladder does not flicker)', () => {
    const a = bossTrainerName('scorched-caldera', 'Fire', ['WildBattle', 'Shop', 'BossBattle']);
    const b = bossTrainerName('scorched-caldera', 'Fire', ['WildBattle', 'Shop', 'BossBattle']);
    expect(a).toBe(b);
  });

  it('draws the name from the biome primary type pool', () => {
    const firePool = ['Blaine', 'Cinder', 'Ember', 'Ignatia', 'Pyra', 'Ashlin'];
    const name = bossTrainerName('any-fire-biome', 'Fire', ['BossBattle']);
    expect(firePool).toContain(name);
  });

  it('varies with the node plan (per-run randomised) even for the same biome', () => {
    // Different revealed plans should be able to produce different names — pin that the seed includes the
    // plan, not just the biome id, by finding two plans that diverge (guaranteed across the small pool).
    const names = new Set(
      [
        ['BossBattle'],
        ['WildBattle', 'BossBattle'],
        ['Shop', 'WildBattle', 'BossBattle'],
        ['EliteBattle', 'Treasure', 'BossBattle'],
        ['Mystery', 'WildBattle', 'Shop', 'BossBattle'],
      ].map(plan => bossTrainerName('same-biome', 'Water', plan)),
    );
    expect(names.size).toBeGreaterThan(1);
  });

  it('falls back to the generic pool for an unknown / missing type', () => {
    const generic = ['Blue', 'Trace', 'Silver', 'Vale', 'Rex', 'Nova'];
    expect(generic).toContain(bossTrainerName('mystery-zone', undefined, ['BossBattle']));
    expect(generic).toContain(bossTrainerName('mystery-zone', 'Steel', ['BossBattle'])); // no Gen-1 Steel pool
  });
});
