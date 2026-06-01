// Move names arrive from the API as lowercase, hyphenated slugs (e.g. "fury-attack").
// The UI is uppercase throughout (creature names, menus), so display move names the
// same way: "FURY ATTACK", "ROLLING KICK", "DIG".
export function formatMoveName(slug: string): string {
  if (!slug || slug === '---') return slug;
  return slug.replace(/-/g, ' ').toUpperCase();
}
