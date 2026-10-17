import { describe, it, expect } from 'vitest';
import { TYPE_COLORS } from './TypeBadge';

// WCAG 2.x relative luminance and contrast ratio.
function luminance(hex: string): number {
  const h = hex.replace('#', '');
  const full = h.length === 3 ? [...h].map(c => c + c).join('') : h;
  const [r, g, b] = [0, 2, 4].map(i => {
    const c = parseInt(full.slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

describe('TYPE_COLORS', () => {
  it('gives every type badge a label contrast of at least 4.5:1 (WCAG AA)', () => {
    const failing = Object.entries(TYPE_COLORS)
      .map(([type, { bg, text }]) => ({ type, ratio: contrast(bg, text) }))
      .filter(r => r.ratio < 4.5);
    expect(failing).toEqual([]);
  });

  it('measures contrast correctly (black on white is 21:1, white on white 1:1)', () => {
    expect(contrast('#000', '#fff')).toBeCloseTo(21, 5);
    expect(contrast('#fff', '#fff')).toBeCloseTo(1, 5);
  });
});
