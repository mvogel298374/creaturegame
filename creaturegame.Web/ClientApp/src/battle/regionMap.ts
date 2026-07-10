// Pure helpers for the region-map overlay's travelled-route rendering, kept out of the component so the
// edge-highlight logic is unit-testable without a DOM.

// A stable *unordered* key for the edge between two biome ids, so a↔b and b↔a collapse to one key.
export function regionEdgeKey(a: string, b: string): string {
  return a < b ? `${a}|${b}` : `${b}|${a}`;
}

// The edges the player actually WALKED, derived from the ordered route (`routePath`, biome ids in entry order,
// with repeats on a re-visit) as its consecutive hops. This is deliberately NOT "any edge between two visited
// biomes" — that would light the connecting edge of two neighbours visited on separate, unrelated legs of the
// run, which the player never traversed. A hop to the same id (defensive) is ignored.
export function travelledEdgeKeys(routePath: readonly string[]): Set<string> {
  const keys = new Set<string>();
  for (let i = 1; i < routePath.length; i++) {
    const a = routePath[i - 1];
    const b = routePath[i];
    if (a !== b) keys.add(regionEdgeKey(a, b));
  }
  return keys;
}
