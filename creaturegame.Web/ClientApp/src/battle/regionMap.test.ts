import { describe, it, expect } from 'vitest';
import { regionEdgeKey, travelledEdgeKeys } from './regionMap';

describe('regionEdgeKey', () => {
  it('is unordered — a↔b and b↔a collapse to the same key', () => {
    expect(regionEdgeKey('a', 'b')).toBe(regionEdgeKey('b', 'a'));
  });
});

describe('travelledEdgeKeys', () => {
  it('lights only the consecutive hops actually walked', () => {
    const keys = travelledEdgeKeys(['a', 'b', 'c']); // a→b→c
    expect(keys.has(regionEdgeKey('a', 'b'))).toBe(true);
    expect(keys.has(regionEdgeKey('b', 'c'))).toBe(true);
    expect(keys.has(regionEdgeKey('a', 'c'))).toBe(false); // never a→c directly
  });

  it('does NOT light the edge between two neighbours visited on separate legs (the D1 bug)', () => {
    // b and c are both visited, and are graph-neighbours — but the player went a→b→a→c, never b→c. Their
    // connecting edge must stay untravelled (the old "both visited" logic wrongly lit it).
    const keys = travelledEdgeKeys(['a', 'b', 'a', 'c']);
    expect(keys.has(regionEdgeKey('a', 'b'))).toBe(true);
    expect(keys.has(regionEdgeKey('a', 'c'))).toBe(true);
    expect(keys.has(regionEdgeKey('b', 'c'))).toBe(false);
  });

  it('is empty for a run that has entered at most one biome (no hops yet)', () => {
    expect(travelledEdgeKeys([]).size).toBe(0);
    expect(travelledEdgeKeys(['a']).size).toBe(0);
  });
});
