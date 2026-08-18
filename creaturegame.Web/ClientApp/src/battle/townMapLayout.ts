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

// Extra cells of *guaranteed-visible* exterior water beyond the server's own canvas margin, applied purely at
// render time (TownMapGrid pads its whole canvas by this on every side — the wire's Width/Height/positions
// never change). Tuned per user feedback (2026-08-19): the server's own margin (IslandLayoutGenerator.Margin,
// 1 cell) is measured against the *sparse core*, but this module's own dilation above already grows that core
// outward by TOWN_MAP_DILATION cells — so the rendered land can reach right up to the canvas edge with no
// water margin visibly left over at all. This adds breathing room independent of that interaction, rather
// than trying to re-derive "how much margin survives dilation" analytically.
export const TOWN_MAP_RENDER_PADDING = 1;

// How much the outer *dilation ring* (never the sparse core itself — see dilateLand) gets deterministically
// thinned, to read as a slightly irregular coastline rather than one uniform rounded-rectangle outline. 1-in-N
// candidate ring cells are skipped; higher N = subtler. Tuned per user feedback (2026-08-19: "a tiny bit more
// jagged, not much") — low enough that it reads as texture, not a different island shape. Never applied to the
// core, so it can never carve an unwanted interior water pocket (the user's other 2026-08-19 request — no
// interior water) — only ever removes cells from the ring that surrounds already-solid land.
const JAGGED_EDGE_SKIP_ODDS = 6;

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

// A small stable hash of a grid cell (FNV-1a-ish — decorative, not cryptographic). Deterministic placement
// purely from a cell's own coordinates means "same layout ⇒ same result" for free, without threading the
// run's RNG seed to the client at all: the server-generated coordinates are already the only input that
// varies per run. Shared by dilateLand's jagged-edge thinning and scatterFor's prop placement below.
function hashCell(x: number, y: number): number {
  let h = 2166136261;
  for (const n of [x, y]) {
    h ^= n;
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

/** Grows `core` outward by `radius` cells (8-directional, so diagonal gaps between two nearby corridors fill
 * too), clipped to the [0,width) × [0,height) canvas — synthesizes the fuller landmass the ratified design
 * calls for from the sparse core the server actually computed. `radius` 0 returns the core unchanged.
 * The outer ring (any cell added by this dilation, never a `core` cell) is deterministically thinned by
 * JAGGED_EDGE_SKIP_ODDS for a slightly irregular coastline — see that constant's own doc for why the core
 * itself is never touched. */
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
          if (land.has(nk)) continue;
          if (hashCell(nx, ny) % JAGGED_EDGE_SKIP_ODDS === 0) continue; // deterministic coastline jaggedness
          next.add(nk);
          land.add(nk);
        }
      }
    }
    frontier = next;
  }
  return land;
}

const ORTHOGONAL_STEPS: readonly [number, number][] = [
  [0, -1],
  [0, 1],
  [-1, 0],
  [1, 0],
];

/** Fills any water cell that dilation left fully enclosed by land — the user's explicit requirement (no
 * interior water, 2026-08-19). This is a real, distinct failure mode from jagged-edge thinning: a graph can
 * have two nearby-but-unconnected path segments separated by a gap *wider* than TOWN_MAP_DILATION (the
 * server's own node spacing, IslandLayoutGenerator's PlacementSpacing, is 2 cells — dilating 1 cell in from
 * each side leaves exactly a 1-cell gap neither side's ring reaches), which then gets surrounded by land on
 * every side once dilation finishes — a small lake in the middle of the island. Flood-fills 4-directionally
 * (orthogonal only, matching coastSides' own convention — decision 11's grid-aligned rule) from every water
 * cell on the canvas border; whatever water is never reached is an enclosed pocket and gets converted to land.
 * Uses the *un-padded* [0,width) × [0,height) canvas — the same coordinate space dilateLand operates in; the
 * render-time padding (TOWN_MAP_RENDER_PADDING) is applied later, purely at render, and is never itself land,
 * so it never needs to run through this pass. */
export function fillInteriorPockets(land: ReadonlySet<string>, width: number, height: number): Set<string> {
  const exteriorReachable = new Set<string>();
  const queue: GridPoint[] = [];

  const seed = (x: number, y: number) => {
    const k = cellKey(x, y);
    if (!land.has(k) && !exteriorReachable.has(k)) {
      exteriorReachable.add(k);
      queue.push({ x, y });
    }
  };
  for (let x = 0; x < width; x++) {
    seed(x, 0);
    seed(x, height - 1);
  }
  for (let y = 0; y < height; y++) {
    seed(0, y);
    seed(width - 1, y);
  }

  for (let i = 0; i < queue.length; i++) {
    const { x, y } = queue[i];
    for (const [dx, dy] of ORTHOGONAL_STEPS) {
      const nx = x + dx, ny = y + dy;
      if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
      const nk = cellKey(nx, ny);
      if (land.has(nk) || exteriorReachable.has(nk)) continue;
      exteriorReachable.add(nk);
      queue.push({ x: nx, y: ny });
    }
  }

  const filled = new Set(land);
  for (let x = 0; x < width; x++)
    for (let y = 0; y < height; y++) {
      const k = cellKey(x, y);
      if (!land.has(k) && !exteriorReachable.has(k)) filled.add(k);
    }
  return filled;
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

/** Whether a synthesized (dilated, non-core) land cell gets a scatter prop, and which — sparse (about 1 in 5),
 * matching the ratified sketch's "small terrain features... scattered in the gaps between nodes," never
 * called for a core cell (a biome tile or a route corridor must stay visually clear). */
export function scatterFor(x: number, y: number): ScatterKind | null {
  const h = hashCell(x, y);
  if (h % 5 !== 0) return null;
  return SCATTER_KINDS[Math.floor(h / 5) % SCATTER_KINDS.length];
}

// Two-part island names built from real short Japanese words common in actual Japanese place-name compounds
// (Fuji+yama = Mt. Fuji, Yoko+hama = Yokohama, Kuro+kawa = a real river/town name, …) rather than invented
// nonsense syllables — genuinely Japanese-sounding because the material and the compounding pattern both are,
// romanized (English script), per the user's ask (2026-08-19). No fidelity claim beyond "sounds right" — this
// doesn't title itself as a real Japanese place, the same spirit as the run's already-invented trainer names.
// PART_A leads (mostly nature/colour words); PART_B trails (the geography-suffix half real compounds use).
const ISLAND_NAME_PART_A: readonly string[] = [
  'Aka', 'Ao', 'Shiro', 'Kuro', 'Fuji', 'Haru', 'Aki', 'Natsu', 'Fuyu', 'Kaze',
  'Yama', 'Kawa', 'Umi', 'Mori', 'Sora', 'Hoshi', 'Tsuki', 'Hikari', 'Yoru', 'Asa',
  'Kumo', 'Yuki', 'Take', 'Iwa', 'Sakura', 'Ryu', 'Ken', 'Nami', 'Mizu', 'Hana',
  'Tori', 'Kin', 'Gin', 'Taka', 'Shio',
];
const ISLAND_NAME_PART_B: readonly string[] = [
  'yama', 'kawa', 'gawa', 'shima', 'jima', 'umi', 'mori', 'saki', 'zaki', 'hama',
  'tani', 'dani', 'ike', 'no', 'sawa', 'sato', 'hara', 'bara', 'matsu', 'mine',
];

// A small stable hash of a string (FNV-1a — decorative, not cryptographic; the same algorithm as hashCell,
// just over characters instead of two grid coordinates).
function hashString(s: string): number {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

/** A random-reading but deterministic island name — same island (same set of biome ids) always names itself
 * the same way, with no server seed threaded to the client (the biome id set is already the only input that
 * varies per run, exactly like scatterFor's own reasoning). `biomeIds` need not be pre-sorted — this sorts
 * them itself so name and biome-entry order can never accidentally disagree. */
export function islandName(biomeIds: readonly string[]): string {
  const h = hashString([...biomeIds].sort().join('|'));
  const a = ISLAND_NAME_PART_A[h % ISLAND_NAME_PART_A.length];
  const b = ISLAND_NAME_PART_B[Math.floor(h / ISLAND_NAME_PART_A.length) % ISLAND_NAME_PART_B.length];
  return `${a}${b} Island`;
}
