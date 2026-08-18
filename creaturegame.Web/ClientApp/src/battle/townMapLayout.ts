// Pure helpers for the Town Map grid renderer (Stage 4c step 4, GENERATION_PROFILE.md §7.4), kept out of the
// component so the landmass/coastline/scatter derivation is unit-testable without a DOM — same rationale as
// regionMap.ts's travelled-route helpers, which this module sits alongside (not inside, to keep that file's
// single-purpose framing intact).
//
// The server (IslandLayoutGenerator) only ever produces a *sparse* graph — a grid cell per biome plus a thin
// one-cell-wide orthogonal corridor per route edge; everything else in the Width×Height canvas is empty. The
// ratified design (decision 11) calls for an actual landmass with a coastline and scattered terrain features,
// not a bare scatter of stepping-stones over open water — so this module synthesizes that fuller shape at
// render time from the sparse wire data, entirely client-side (no backend/wire change; user's call, 2026-08-18).

export interface GridPoint { x: number; y: number }

// How far (8-directional, Chebyshev) the sparse server-generated land (biome + route cells — the only cells
// IslandLayoutGenerator actually produces) grows outward to synthesize a fuller landmass to draw a coastline
// and scatter terrain against. Purely a rendering choice, tuned here alongside dilateLand (the function it
// configures). Untuned beyond "looks like an island, not a bare path over open water" (GENERATION_PROFILE.md
// §7.4 left the exact canvas/padding feel open for build/playtest tuning); revisit if a run's map reads too
// cramped or too sparse.
export const TOWN_MAP_DILATION = 1;

// Status line for the Town Map's hover/focus caption band (decision: no floating per-tile labels — a caption
// below the map is the only place a biome's name/status/type(s) show, per GENERATION_PROFILE.md §7.4's
// 2026-08-05 sketch ratification). isVisited is checked before isOffered — a biome that is both (every biome
// you've already left stays offered at each later route choice, since BiomeChoiceEvent.PickOptions offers
// every playable neighbour with no visited filter) must read "visited," not "offered, unvisited".
export function biomeCaptionStatus(isCurrent: boolean, isVisited: boolean, isOffered: boolean): string {
  if (isCurrent) return 'you are here';
  if (isVisited) return 'visited';
  if (isOffered) return 'offered, unvisited';
  return 'unvisited';
}

/** The stable "x,y" string key every land/coast/scatter Set and lookup in this module (and its consumers) uses
 * for a grid cell — exported so the renderer can build/read the same keys without duplicating the format. */
export function cellKey(x: number, y: number): string {
  return `${x},${y}`;
}

export function parseCellKey(k: string): GridPoint {
  const [x, y] = k.split(',').map(Number);
  return { x, y };
}

/** The sparse "core" land — every biome cell plus every route's cell-by-cell corridor. Exactly what the server
 * generated; nothing synthesized yet. */
export function coreLandCells(
  biomes: readonly GridPoint[],
  routes: readonly { cells: readonly GridPoint[] }[],
): Set<string> {
  const land = new Set<string>();
  for (const b of biomes) land.add(cellKey(b.x, b.y));
  for (const r of routes) for (const c of r.cells) land.add(cellKey(c.x, c.y));
  return land;
}

/** Grows `core` outward by `radius` cells (8-directional, so diagonal gaps between two nearby corridors fill
 * too), clipped to the [0,width) × [0,height) canvas — synthesizes the fuller landmass the ratified design
 * calls for from the sparse core the server actually computed. `radius` 0 returns the core unchanged. */
export function dilateLand(
  core: ReadonlySet<string>,
  width: number,
  height: number,
  radius: number,
): Set<string> {
  const land = new Set(core);
  let frontier = core;
  for (let step = 0; step < radius; step++) {
    const next = new Set<string>();
    for (const k of frontier) {
      const { x, y } = parseCellKey(k);
      for (let dy = -1; dy <= 1; dy++) {
        for (let dx = -1; dx <= 1; dx++) {
          if (dx === 0 && dy === 0) continue;
          const nx = x + dx, ny = y + dy;
          if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
          const nk = cellKey(nx, ny);
          if (!land.has(nk)) {
            next.add(nk);
            land.add(nk);
          }
        }
      }
    }
    frontier = next;
  }
  return land;
}

export type CoastSide = 'top' | 'bottom' | 'left' | 'right';

/** Which of a land cell's 4 orthogonal sides border non-land (water) — drives which coastline edge-trim
 * image(s) render on that cell. A cell with land on every side (fully interior) returns []. Diagonal-only
 * water (a corner) is deliberately not a "side" — the trim set is orthogonal, matching decision 11's
 * grid-aligned-only rule. */
export function coastSides(land: ReadonlySet<string>, x: number, y: number): CoastSide[] {
  const sides: CoastSide[] = [];
  if (!land.has(cellKey(x, y - 1))) sides.push('top');
  if (!land.has(cellKey(x, y + 1))) sides.push('bottom');
  if (!land.has(cellKey(x - 1, y))) sides.push('left');
  if (!land.has(cellKey(x + 1, y))) sides.push('right');
  return sides;
}

export type ScatterKind = 'tree-a' | 'tree-b' | 'tree-c' | 'rock' | 'boulder' | 'signpost';
const SCATTER_KINDS: readonly ScatterKind[] = ['tree-a', 'tree-b', 'tree-c', 'rock', 'boulder', 'signpost'];

// A small stable hash of a grid cell (FNV-1a-ish — decorative, not cryptographic). Deterministic scatter-prop
// placement purely from the cell's own coordinates means "same layout ⇒ same decoration" for free, without
// threading the run's RNG seed to the client at all: the server-generated coordinates are already the only
// input that varies per run.
function hashCell(x: number, y: number): number {
  let h = 2166136261;
  for (const n of [x, y]) {
    h ^= n;
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

/** Whether a synthesized (dilated, non-core) land cell gets a scatter prop, and which — sparse (about 1 in 5),
 * matching the ratified sketch's "small terrain features... scattered in the gaps between nodes," never
 * called for a core cell (a biome tile or a route corridor must stay visually clear). */
export function scatterFor(x: number, y: number): ScatterKind | null {
  const h = hashCell(x, y);
  if (h % 5 !== 0) return null;
  return SCATTER_KINDS[Math.floor(h / 5) % SCATTER_KINDS.length];
}
