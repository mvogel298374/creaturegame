import { describe, it, expect } from 'vitest';
import { formatMoveName } from './format';

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
