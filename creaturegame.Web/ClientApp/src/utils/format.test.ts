import { describe, it, expect } from 'vitest';
import { formatMoveName, formatDamageRange } from './format';

describe('formatMoveName', () => {
  it('uppercases and de-hyphenates a move slug', () => {
    expect(formatMoveName('fury-attack')).toBe('FURY ATTACK');
    expect(formatMoveName('dig')).toBe('DIG');
  });

  it('passes the empty-slot placeholder and empty string through unchanged', () => {
    // The move menu renders '---' for an empty slot; it must not become a formatted label.
    expect(formatMoveName('---')).toBe('---');
    expect(formatMoveName('')).toBe('');
  });
});

describe('formatDamageRange', () => {
  it('formats low–high, collapses an exact amount, and is null without a range', () => {
    expect(formatDamageRange(32, 38)).toBe('32–38');
    expect(formatDamageRange(40, 40)).toBe('40');
    expect(formatDamageRange(null, null)).toBeNull();
    expect(formatDamageRange(undefined, 5)).toBeNull();
  });
});
