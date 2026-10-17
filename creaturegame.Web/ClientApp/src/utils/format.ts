// Move names arrive from the API as lowercase, hyphenated slugs (e.g. "fury-attack").
// The UI is uppercase throughout (creature names, menus), so display move names the
// same way: "FURY ATTACK", "ROLLING KICK", "DIG".
// Dev Mode damage range (docs/TODO.md — Dev Mode damage ranges): "32–38", or the single number when the move
// deals a fixed amount (low == high). Null when there's no range to show.
export function formatDamageRange(min: number | null | undefined, max: number | null | undefined): string | null {
  if (min == null || max == null) return null;
  return min === max ? `${min}` : `${min}–${max}`;
}

// A battle stat stage with its sign: "+2", "−1" (a true minus sign, like the damage-range dash).
export function formatStage(stage: number): string {
  return stage > 0 ? `+${stage}` : stage < 0 ? `−${-stage}` : '0';
}

export function formatMoveName(slug: string): string {
  if (!slug || slug === '---') return slug;
  return slug.replace(/-/g, ' ').toUpperCase();
}
