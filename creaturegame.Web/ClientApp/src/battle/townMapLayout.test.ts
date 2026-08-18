import { describe, it, expect } from 'vitest';
import { coreLandCells, dilateLand, fillInteriorPockets, coastSides, scatterFor, biomeCaptionStatus, islandName } from './townMapLayout';

describe('coreLandCells', () => {
  it('is the union of every biome cell and every route cell', () => {
    const land = coreLandCells(
      [{ x: 1, y: 1 }, { x: 3, y: 1 }],
      [{ cells: [{ x: 1, y: 1 }, { x: 2, y: 1 }, { x: 3, y: 1 }] }],
    );
    expect([...land].sort()).toEqual(['1,1', '2,1', '3,1']);
  });

  it('is empty for no biomes and no routes', () => {
    expect(coreLandCells([], []).size).toBe(0);
  });
});

describe('dilateLand', () => {
  it('radius 0 returns the core unchanged', () => {
    const core = coreLandCells([{ x: 2, y: 2 }], []);
    const dilated = dilateLand(core, 5, 5, 0);
    expect([...dilated]).toEqual([...core]);
  });

  it('radius 1 grows a single cell into (most of) its 8-neighbourhood, always keeping the core cell', () => {
    const core = coreLandCells([{ x: 2, y: 2 }], []);
    const dilated = dilateLand(core, 5, 5, 1);
    expect(dilated.has('2,2')).toBe(true); // the core cell itself is never thinned (JAGGED_EDGE_SKIP_ODDS
    // only ever applies to cells dilation is *adding*, never to a core cell already present)
    // Every dilated cell is a genuine neighbour of the core (within Chebyshev distance 1) — the deterministic
    // jagged-edge thinning means not every one of the 8 candidates necessarily survives, but nothing outside
    // that neighbourhood can appear either.
    for (const k of dilated) {
      const [x, y] = k.split(',').map(Number);
      expect(Math.max(Math.abs(x - 2), Math.abs(y - 2))).toBeLessThanOrEqual(1);
    }
    expect(dilated.size).toBeGreaterThan(1); // it did grow — not every candidate was thinned away
  });

  it('never thins the core itself, even when dilation has no in-bounds candidate to add at all', () => {
    // A degenerate 1x1 canvas: every one of (0,0)'s 8 neighbourhood candidates is out of bounds, so dilation
    // adds nothing — the core must still survive intact regardless.
    const core = coreLandCells([{ x: 0, y: 0 }], []);
    const dilated = dilateLand(core, 1, 1, 1);
    expect(dilated).toEqual(new Set(['0,0']));
  });

  it('clips to the canvas bounds — a corner cell never grows outside [0,width) x [0,height)', () => {
    const core = coreLandCells([{ x: 0, y: 0 }], []);
    const dilated = dilateLand(core, 5, 5, 1);
    expect(dilated.has('0,0')).toBe(true);
    for (const k of dilated) {
      const [x, y] = k.split(',').map(Number);
      expect(x).toBeGreaterThanOrEqual(0);
      expect(y).toBeGreaterThanOrEqual(0);
    }
    // Only the in-bounds quadrant of the 3x3 block around (0,0) is even a *candidate* — jagged-edge thinning
    // may remove some of those four, but nothing from the out-of-bounds three-quarters can ever appear.
    for (const k of dilated) expect(['0,0', '0,1', '1,0', '1,1']).toContain(k);
  });

  it('radius 2 grows further than radius 1 (monotonic, not a fixed-size stamp)', () => {
    const core = coreLandCells([{ x: 5, y: 5 }], []);
    const r1 = dilateLand(core, 20, 20, 1);
    const r2 = dilateLand(core, 20, 20, 2);
    expect(r2.size).toBeGreaterThan(r1.size);
    for (const k of r1) expect(r2.has(k)).toBe(true); // r1 is a subset of r2
  });

  it('closes the diagonal gap between two orthogonally-adjacent-but-not-touching corridors, across many sample positions', () => {
    // Two land cells that only share a corner (diagonal neighbours) become 4-connected as soon as *either* of
    // the two cells bridging them (the two other corners of the shared 2x2 block) survives dilation — a single
    // hardcoded pair isn't a reliable pin any more, since deterministic jagged-edge thinning may skip any one
    // specific bridge cell, so this samples many independent, non-overlapping diagonal pairs and asserts the
    // 8-directional mechanism closes the gap in the large majority of cases, not that it's a coincidence of one
    // cherry-picked pick.
    let closed = 0;
    const samples = 60;
    for (let i = 0; i < samples; i++) {
      const ox = i * 4; // spread pairs out so their dilation rings never overlap each other
      const core = coreLandCells([{ x: ox, y: 0 }, { x: ox + 1, y: 1 }], []);
      const dilated = dilateLand(core, ox + 5, 5, 1);
      if (dilated.has(`${ox + 1},0`) || dilated.has(`${ox},1`)) closed++;
    }
    expect(closed).toBeGreaterThan(samples * 0.9); // the overwhelming majority (either bridge cell suffices)
  });
});

describe('dilateLand — jagged-edge thinning', () => {
  it('is deterministic — the same core/canvas/radius always thins the same cells', () => {
    const core = coreLandCells([{ x: 5, y: 5 }], []);
    const a = dilateLand(core, 20, 20, 2);
    const b = dilateLand(core, 20, 20, 2);
    expect(a).toEqual(b);
  });

  it('thins some but not all of an interior cell\'s ring — texture, not a different shape', () => {
    const core = coreLandCells([{ x: 10, y: 10 }], []);
    const dilated = dilateLand(core, 20, 20, 1); // all 8 candidates are in-bounds — only jaggedness thins them
    expect(dilated.size).toBeGreaterThan(1); // not everything skipped
    expect(dilated.size).toBeLessThan(9); // not everything survived either — some texture, per the user's ask
  });

  it('never thins a core cell, across many sample positions — no interior water pocket can ever appear', () => {
    // The user's explicit requirement (2026-08-19): interior water is never wanted. Thinning only ever removes
    // a cell dilation was about to *add* — this samples many core cells across a big dense cluster (the case
    // most likely to expose an accidental core removal, if the guard were ever wrong) and asserts every one
    // survives.
    const biomes = [];
    for (let x = 0; x < 15; x++) for (let y = 0; y < 15; y++) biomes.push({ x, y });
    const core = coreLandCells(biomes, []);
    const dilated = dilateLand(core, 20, 20, 1);
    for (const k of core) expect(dilated.has(k)).toBe(true);
  });
});

describe('fillInteriorPockets', () => {
  it('fills a single water cell fully enclosed by a ring of land', () => {
    // A 3x3 ring of land around (1,1) with (1,1) itself left as water — a 1-cell lake, the exact shape a
    // 2-cell-wide gap between two dilation rings leaves (the real-world case this exists for).
    const land = new Set(['0,0', '1,0', '2,0', '0,1', '2,1', '0,2', '1,2', '2,2']);
    const filled = fillInteriorPockets(land, 3, 3);
    expect(filled.has('1,1')).toBe(true);
  });

  it('never fills water that has a path to the canvas border', () => {
    // Same ring, but with a gap in one side — (1,1) can now reach the border via (1,0)'s missing neighbour.
    const land = new Set(['0,0', '2,0', '0,1', '2,1', '0,2', '1,2', '2,2']); // (1,0) left as water too
    const filled = fillInteriorPockets(land, 3, 3);
    expect(filled.has('1,1')).toBe(false);
    expect(filled.has('1,0')).toBe(false);
  });

  it('is a no-op when there is no enclosed water at all', () => {
    const land = new Set(['0,0', '1,0', '0,1']); // an open corner shape, plenty of unenclosed water around it
    const filled = fillInteriorPockets(land, 5, 5);
    expect(filled).toEqual(land);
  });

  it('never removes an existing land cell — the result is always a superset of the input', () => {
    const land = new Set(['1,1']);
    const filled = fillInteriorPockets(land, 5, 5);
    for (const k of land) expect(filled.has(k)).toBe(true);
  });

  it('fills every enclosed cell of a larger pocket, not just a single-cell lake', () => {
    // A 2x2 lake (1,1)-(2,2) inside a 4x4 ring of land.
    const land = new Set<string>();
    for (let x = 0; x <= 3; x++) for (let y = 0; y <= 3; y++)
      if (x === 0 || x === 3 || y === 0 || y === 3) land.add(`${x},${y}`);
    const filled = fillInteriorPockets(land, 4, 4);
    expect(filled.has('1,1')).toBe(true);
    expect(filled.has('1,2')).toBe(true);
    expect(filled.has('2,1')).toBe(true);
    expect(filled.has('2,2')).toBe(true);
  });

  it('composes with dilateLand on the real-world gap shape without dropping any biome cell', () => {
    // Four biomes in a cross around a shared centre, each 2 cells out — the same PlacementSpacing-driven gap
    // shape a real run can produce (each side's dilation only reaches 1 cell in, meeting the centre exactly —
    // manual testing found this exact shape leaving a 1-cell lake, 2026-08-19). A smoke/integration check that
    // chaining the two real functions on a realistic shape doesn't regress the one invariant fillInteriorPockets
    // must never violate (never drops land) — full correctness of the fill itself is pinned directly above,
    // against hand-constructed shapes with a known ground truth.
    const biomes = [{ x: 3, y: 1 }, { x: 3, y: 5 }, { x: 1, y: 3 }, { x: 5, y: 3 }];
    const core = coreLandCells(biomes, []);
    const dilated = dilateLand(core, 7, 7, 1);
    const filled = fillInteriorPockets(dilated, 7, 7);
    for (const b of biomes) expect(filled.has(`${b.x},${b.y}`)).toBe(true);
    for (const k of dilated) expect(filled.has(k)).toBe(true);
  });
});

describe('coastSides', () => {
  it('a fully-interior cell (land on all 4 orthogonal sides) has no exposed coast', () => {
    const land = new Set(['1,1', '0,1', '2,1', '1,0', '1,2']);
    expect(coastSides(land, 1, 1)).toEqual([]);
  });

  it('an isolated single land cell exposes all 4 sides', () => {
    const land = new Set(['1,1']);
    expect(coastSides(land, 1, 1).sort()).toEqual(['bottom', 'left', 'right', 'top']);
  });

  it('reports exactly the sides bordering non-land, nothing else', () => {
    // Land to the right and below only — top and left are water.
    const land = new Set(['1,1', '2,1', '1,2']);
    expect(coastSides(land, 1, 1).sort()).toEqual(['left', 'top']);
  });

  it('a diagonal-only water corner is not reported as a side (orthogonal-only, decision 11)', () => {
    // (1,1) has land on all 4 orthogonal neighbours; only the diagonal (0,0) is water — not an exposed side.
    const land = new Set(['1,1', '0,1', '2,1', '1,0', '1,2']);
    expect(coastSides(land, 1, 1)).toEqual([]);
  });
});

describe('scatterFor', () => {
  it('is a pure function of its coordinates — same cell always yields the same result', () => {
    for (let x = 0; x < 20; x++)
      for (let y = 0; y < 20; y++)
        expect(scatterFor(x, y)).toBe(scatterFor(x, y));
  });

  it('is sparse — most cells in a sample get no prop', () => {
    let withProp = 0;
    const total = 30 * 30;
    for (let x = 0; x < 30; x++)
      for (let y = 0; y < 30; y++)
        if (scatterFor(x, y) !== null) withProp++;
    expect(withProp).toBeGreaterThan(0);
    expect(withProp).toBeLessThan(total * 0.4); // sparse, not the majority of cells
  });

  it('reaches every scatter kind across a large enough sample', () => {
    const seen = new Set<string>();
    for (let x = 0; x < 60; x++)
      for (let y = 0; y < 60; y++) {
        const kind = scatterFor(x, y);
        if (kind) seen.add(kind);
      }
    expect(seen).toEqual(new Set(['tree-a', 'tree-b', 'tree-c', 'rock', 'boulder', 'signpost']));
  });
});

describe('biomeCaptionStatus', () => {
  it('is "you are here" for the current biome, regardless of visited/offered', () => {
    expect(biomeCaptionStatus(true, false, false)).toBe('you are here');
    expect(biomeCaptionStatus(true, true, true)).toBe('you are here');
  });

  it('is "visited" for an already-visited biome even when it is also offered again', () => {
    // The real-world case that motivated this pin: BiomeChoiceEvent.PickOptions offers every playable
    // neighbour with no visited filter, so the biome you just left stays offered at the next route choice.
    expect(biomeCaptionStatus(false, true, true)).toBe('visited');
  });

  it('is "visited" for an already-visited biome that is not currently offered', () => {
    expect(biomeCaptionStatus(false, true, false)).toBe('visited');
  });

  it('is "offered, unvisited" for a fresh offer never visited before', () => {
    expect(biomeCaptionStatus(false, false, true)).toBe('offered, unvisited');
  });

  it('is plain "unvisited" for a biome that is neither current, visited, nor offered', () => {
    expect(biomeCaptionStatus(false, false, false)).toBe('unvisited');
  });
});

describe('islandName', () => {
  it('always ends with "Island"', () => {
    expect(islandName(['a', 'b', 'c'])).toMatch(/ Island$/);
    expect(islandName(['meadow-trail'])).toMatch(/ Island$/);
  });

  it('is the same island id set always gets the same name (deterministic, no seed threaded to the client)', () => {
    const a = islandName(['sparkwire-ruins', 'crystal-cavern', 'granite-cliffs']);
    const b = islandName(['sparkwire-ruins', 'crystal-cavern', 'granite-cliffs']);
    expect(a).toBe(b);
  });

  it('does not depend on the order biome ids are passed in — same set, same name', () => {
    const a = islandName(['sparkwire-ruins', 'crystal-cavern', 'granite-cliffs']);
    const b = islandName(['granite-cliffs', 'sparkwire-ruins', 'crystal-cavern']);
    expect(a).toBe(b);
  });

  it('different islands (different biome sets) mostly get different names', () => {
    // Not a hard guarantee for any two arbitrary inputs (a hash can collide), but across a real spread of
    // distinct biome-id combinations the names should mostly differ, not collapse onto one or two values.
    const names = new Set<string>();
    for (let i = 0; i < 40; i++) names.add(islandName([`biome-${i}`, `biome-${i + 1}`]));
    expect(names.size).toBeGreaterThan(20);
  });
});
