import { describe, it, expect, vi, afterEach } from 'vitest';
import {
  DEFAULT_GENERATION,
  applyGenerationTheme,
  missingTypeAssets,
  presentationFor,
  warnOnMissingTypeAssets,
  type GenerationPresentation,
} from './presentation';

// The client-side falsification analogue of the server's TestAltProfile (GENERATION_PROFILE.md §7.2): a
// deliberately fake registry entry proving the theme flow is registry-driven, not baked in. Test-only,
// never registered in the real registry, not a real generation.
const ALT_REGISTRY: Record<string, GenerationPresentation> = {
  One: { id: 'One', theme: 'gen1' },
  TestAlt: { id: 'TestAlt', theme: 'test-alt' },
};

// A fake document root: the applied attribute is observable without a DOM.
function fakeRoot() {
  const attrs: Record<string, string> = {};
  return { attrs, setAttribute: (name: string, value: string) => { attrs[name] = value; } };
}

// The 15 Gen 1 types, as the server's roster echo delivers them. A test fixture stating the expected
// wire value — the live source of truth is Gen1Profile.TypeRoster, pinned server-side.
const GEN1_ROSTER = [
  'Normal', 'Fighting', 'Psychic', 'Electric', 'Water', 'Flying', 'Poison', 'Ground',
  'Rock', 'Bug', 'Ghost', 'Fire', 'Grass', 'Ice', 'Dragon',
];

describe('presentationFor', () => {
  it('resolves a known generation id', () => {
    expect(presentationFor('One').theme).toBe('gen1');
  });

  it('falls back to the default for a missing or unknown id — the server parse boundary contract', () => {
    // Mirrors GameController.ParseGeneration: a missing/unrecognised generation IS a Gen 1 run.
    expect(presentationFor(null).id).toBe(DEFAULT_GENERATION);
    expect(presentationFor(undefined).id).toBe(DEFAULT_GENERATION);
    expect(presentationFor('NoSuchGen').id).toBe(DEFAULT_GENERATION);
  });

  it('is registry-driven, not baked-in: an alternate registry entry resolves through the same flow', () => {
    expect(presentationFor('TestAlt', ALT_REGISTRY).theme).toBe('test-alt');
  });
});

describe('applyGenerationTheme', () => {
  it('stamps data-generation with the resolved theme', () => {
    const root = fakeRoot();
    applyGenerationTheme('One', root);
    expect(root.attrs['data-generation']).toBe('gen1');
  });

  it('stamps the default theme for an unknown id (never leaves the attribute unset)', () => {
    const root = fakeRoot();
    applyGenerationTheme('NoSuchGen', root);
    expect(root.attrs['data-generation']).toBe('gen1');
  });

  it('applies whatever theme the registry supplies — the registry is the source, not the CSS default', () => {
    const root = fakeRoot();
    applyGenerationTheme('TestAlt', root, ALT_REGISTRY);
    expect(root.attrs['data-generation']).toBe('test-alt');
  });
});

describe('missingTypeAssets', () => {
  it('reports no gaps for the Gen 1 roster — the inventories cover all 15', () => {
    expect(missingTypeAssets(GEN1_ROSTER)).toEqual({ icons: [], bossNames: [] });
  });

  it('measures against the DELIVERED roster, not a fixed Gen 1 list: extra rostered types surface as gaps', () => {
    // The falsification leg: a 17-type roster (the server's TestAltProfile shape) names exactly the two
    // types the asset tables lack. A check hardcoding "the 15" would report no gaps here.
    const gaps = missingTypeAssets([...GEN1_ROSTER, 'Dark', 'Steel']);
    expect(gaps.icons).toEqual(['Dark', 'Steel']);
    expect(gaps.bossNames).toEqual(['Dark', 'Steel']);
  });
});

describe('warnOnMissingTypeAssets', () => {
  afterEach(() => vi.restoreAllMocks());

  it('is silent when the roster is fully covered', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    warnOnMissingTypeAssets(GEN1_ROSTER);
    expect(warn).not.toHaveBeenCalled();
  });

  it('warns once, naming the uncovered types', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const gaps = warnOnMissingTypeAssets([...GEN1_ROSTER, 'Dark']);
    expect(gaps.icons).toEqual(['Dark']);
    expect(warn).toHaveBeenCalledTimes(1);
    expect(String(warn.mock.calls[0][0])).toContain('Dark');
  });
});
