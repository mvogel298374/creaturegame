// The client-side analogue of the server's GenerationProfiles registry (Generation Profile Stage 4a —
// docs/GENERATION_PROFILE.md §7.2): maps the run's generation id (the wire string from
// RunPresentationRevealed, e.g. 'One') to its presentation. Components read this registry, never a
// hardcoded default, so a later generation's chrome is a registry entry — not an edit hunt.
//
// The generation reaches the client on two paths, and both are needed (§7.2): route state (the client
// picked it at run start) for the immediate theme, and the server echo (RunPresentationRevealed, emitted
// on every hub attach) as the authority — a reconnect re-mounts BattleScreen with no route state.

import { hasBossNamePool } from '../battle/bossTrainer';
import { hasTypeIcon } from '../pages/mapGlyphs';

export interface GenerationPresentation {
  /** The wire id, matching the server's Generation enum member name. */
  id: string;
  /** The document-root theme key: `data-generation="<theme>"`, which per-gen CSS blocks key off (4b). */
  theme: string;
}

// The registry. One real entry today; a new generation adds a line here (and its CSS block), nothing else.
const PRESENTATIONS: Record<string, GenerationPresentation> = {
  One: { id: 'One', theme: 'gen1' },
};

// The default mirrors the server's parse boundary (GameController.ParseGeneration): a missing or
// unrecognised generation IS a Gen 1 run — the same value on both sides, or the theme could disagree
// with the content the run actually serves.
export const DEFAULT_GENERATION = 'One';

/** The presentation for a generation id, falling back to the default per the boundary contract above.
 *  The registry parameter exists for tests (proving the flow is registry-driven, not baked-in); runtime
 *  callers omit it. */
export function presentationFor(
  id: string | null | undefined,
  registry: Record<string, GenerationPresentation> = PRESENTATIONS,
): GenerationPresentation {
  return (id && registry[id]) || registry[DEFAULT_GENERATION];
}

/** Stamps the generation's theme onto the document root (`data-generation`), where the per-gen CSS
 *  override blocks (4b) pick it up. The root parameter exists for tests; runtime callers omit it. */
export function applyGenerationTheme(
  id: string | null | undefined,
  root: { setAttribute(name: string, value: string): void } = document.documentElement,
  registry?: Record<string, GenerationPresentation>,
): void {
  root.setAttribute('data-generation', presentationFor(id, registry).theme);
}

/** The rostered types the client has no bespoke assets for, measured against the roster the server
 *  delivered (RunPresentationRevealed.typeRoster) — the single source of truth for "which types exist
 *  this run". The per-type tables themselves (boss-name pools, map glyphs) are asset inventories, not
 *  roster claims: each degrades gracefully for a type it lacks (generic name / the Normal glyph), and
 *  this check is what keeps "inventory covers the roster" an observed fact rather than three parallel
 *  hand-maintained copies of "the 15" (the Stage 2a handoff, GENERATION_PROFILE.md §5(a)). */
export function missingTypeAssets(typeRoster: readonly string[]): {
  icons: string[];
  bossNames: string[];
} {
  return {
    icons: typeRoster.filter(t => !hasTypeIcon(t)),
    bossNames: typeRoster.filter(t => !hasBossNamePool(t)),
  };
}

/** Logs a console warning when the delivered roster names types the asset tables don't cover. Called
 *  once per run when the presentation echo arrives; returns the gaps so the caller (and tests) can see
 *  what was reported. */
export function warnOnMissingTypeAssets(typeRoster: readonly string[]): {
  icons: string[];
  bossNames: string[];
} {
  const gaps = missingTypeAssets(typeRoster);
  if (gaps.icons.length > 0 || gaps.bossNames.length > 0) {
    console.warn(
      '[generations] The run\'s type roster names types without client assets ' +
        `(they degrade to generic fallbacks) — icons: [${gaps.icons.join(', ')}], ` +
        `boss name pools: [${gaps.bossNames.join(', ')}]`,
    );
  }
  return gaps;
}
