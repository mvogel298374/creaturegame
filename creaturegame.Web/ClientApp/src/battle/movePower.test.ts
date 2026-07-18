import { describe, it, expect } from 'vitest';
import { powerPill } from './movePower';

describe('powerPill — base-power strength cue', () => {
  it('shows no pill for a status / fixed-damage move (power 0 or undefined)', () => {
    expect(powerPill(0)).toBeNull();
    expect(powerPill(undefined)).toBeNull();
  });

  it('labels the pill with the raw power number', () => {
    expect(powerPill(40)?.label).toBe('40');
    expect(powerPill(150)?.label).toBe('150');
  });

  it('buckets power onto the cool→hot tier classes', () => {
    // Boundaries: <50 weak, 50–79 mid, 80–109 strong, ≥110 max.
    expect(powerPill(35)?.cls).toBe('move-pow--weak');   // Tackle-class
    expect(powerPill(49)?.cls).toBe('move-pow--weak');
    expect(powerPill(50)?.cls).toBe('move-pow--mid');    // tier edge
    expect(powerPill(79)?.cls).toBe('move-pow--mid');
    expect(powerPill(80)?.cls).toBe('move-pow--strong'); // tier edge
    expect(powerPill(109)?.cls).toBe('move-pow--strong');
    expect(powerPill(110)?.cls).toBe('move-pow--max');   // tier edge
    expect(powerPill(150)?.cls).toBe('move-pow--max');   // Hyper Beam-class
  });
});
