import { describe, it, expect } from 'vitest';
import { coreLandCells, dilateLand, coastSides, scatterFor, biomeCaptionStatus } from './townMapLayout';

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

  it('radius 1 grows a single cell into its full 8-neighbourhood', () => {
    const core = coreLandCells([{ x: 2, y: 2 }], []);
    const dilated = dilateLand(core, 5, 5, 1);
    // The 3x3 block centred on (2,2).
    const expected = new Set<string>();
    for (let dy = -1; dy <= 1; dy++)
      for (let dx = -1; dx <= 1; dx++)
        expected.add(`${2 + dx},${2 + dy}`);
    expect(dilated).toEqual(expected);
  });

  it('clips to the canvas bounds — a corner cell never grows outside [0,width) x [0,height)', () => {
    const core = coreLandCells([{ x: 0, y: 0 }], []);
    const dilated = dilateLand(core, 5, 5, 1);
    for (const k of dilated) {
      const [x, y] = k.split(',').map(Number);
      expect(x).toBeGreaterThanOrEqual(0);
      expect(y).toBeGreaterThanOrEqual(0);
    }
    // Only the in-bounds quadrant of the 3x3 block around (0,0) survives.
    expect([...dilated].sort()).toEqual(['0,0', '0,1', '1,0', '1,1']);
  });

  it('radius 2 grows further than radius 1 (monotonic, not a fixed-size stamp)', () => {
    const core = coreLandCells([{ x: 5, y: 5 }], []);
    const r1 = dilateLand(core, 20, 20, 1);
    const r2 = dilateLand(core, 20, 20, 2);
    expect(r2.size).toBeGreaterThan(r1.size);
    for (const k of r1) expect(r2.has(k)).toBe(true); // r1 is a subset of r2
  });

  it('fills the diagonal gap between two orthogonally-adjacent-but-not-touching corridors', () => {
    // Two land cells that only share a corner (diagonal neighbours) — an 8-directional dilation of radius 1
    // must bring them into a single connected mass (both already dilate into the shared corner cell).
    const core = coreLandCells([{ x: 1, y: 1 }, { x: 2, y: 2 }], []);
    const dilated = dilateLand(core, 5, 5, 1);
    expect(dilated.has('2,1')).toBe(true);
    expect(dilated.has('1,2')).toBe(true);
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
