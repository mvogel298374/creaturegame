// Shared vector glyphs for the run map: per-type icons (biome territory watermark + the type chips'
// "icon frame", so the type reads at a glance like the mainline games), and per-node-kind icons for the
// encounter ladder (replacing the old text glyphs). One hidden <svg> sprite of <symbol>s is mounted once by
// <MapGlyphSprite/>; everything else references them via <use href="#id"/>.
import { typeColor } from '../components/TypeBadge';

// A biome's primary type drives its territory watermark; the chip below uses each listed type's own icon.
export function typeIconId(type: string): string {
  return TYPE_ICON[type] ? `t-${type}` : 't-Normal';
}
export function nodeIconId(kind: string): string {
  return NODE_ICON[kind] ? `k-${NODE_ICON[kind]}` : 'k-question';
}

// Which types have a bespoke symbol (all 15 Gen 1 types). Value is unused — presence is the lookup.
const TYPE_ICON: Record<string, true> = {
  Normal: true, Fire: true, Water: true, Electric: true, Grass: true, Ice: true, Fighting: true,
  Poison: true, Ground: true, Flying: true, Psychic: true, Bug: true, Rock: true, Ghost: true, Dragon: true,
};

const NODE_ICON: Record<string, string> = {
  WildBattle: 'sword', EliteBattle: 'star', BossBattle: 'skull', Shop: 'coin',
  Treasure: 'gem', Mystery: 'question', Rest: 'heart',
};

// A type chip: a rounded pill whose left is a small colour-filled frame carrying the type's icon, right is
// the type name — clearly a *type*, not incidental text. Colour is the live game type colour.
export function TypeChip({ type }: { type: string }) {
  return (
    <span className="type-chip" style={{ ['--cc' as string]: typeColor(type) }}>
      <span className="type-chip-frame">
        <svg viewBox="0 0 24 24" aria-hidden="true"><use href={`#${typeIconId(type)}`} /></svg>
      </span>
      <span className="type-chip-name">{type.toUpperCase()}</span>
    </span>
  );
}

// The hidden symbol sprite. Mounted once near the battle-screen root so every <use> resolves.
export function MapGlyphSprite() {
  return (
    <svg width="0" height="0" style={{ position: 'absolute' }} aria-hidden="true" focusable="false">
      <defs>
        {/* ── Type icons (24×24, currentColor) ── */}
        <symbol id="t-Normal" viewBox="0 0 24 24"><circle cx="12" cy="12" r="7" fill="none" stroke="currentColor" strokeWidth="2.4" /><circle cx="12" cy="12" r="2.4" fill="currentColor" /></symbol>
        <symbol id="t-Fire" viewBox="0 0 24 24"><path fill="currentColor" d="M13 2c1 4 5 6 5 11a6 6 0 01-12 0c0-3 2-5 3-6 0 2 1 3 2 3-1-4 1-6 2-8z" /></symbol>
        <symbol id="t-Water" viewBox="0 0 24 24"><path fill="currentColor" d="M12 2c4 6 6 8 6 12a6 6 0 01-12 0c0-4 2-6 6-12z" /></symbol>
        <symbol id="t-Electric" viewBox="0 0 24 24"><path fill="currentColor" d="M13 2L4 14h6l-2 8 10-13h-7z" /></symbol>
        <symbol id="t-Grass" viewBox="0 0 24 24"><path fill="currentColor" d="M20 3C11 4 5 9 5 17c0 1 0 2 1 3 1-6 5-9 10-11-4 3-7 6-8 12 8 0 12-6 12-13 0-2 0-4 0-5z" /></symbol>
        <symbol id="t-Ice" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" d="M12 2v20M3.3 7l17.4 10M20.7 7L3.3 17M8 3l4 3 4-3M8 21l4-3 4 3" /></symbol>
        <symbol id="t-Fighting" viewBox="0 0 24 24"><path fill="currentColor" d="M7 10V7a2 2 0 014 0V6a2 2 0 014 0v1a2 2 0 013 1.7V15a5 5 0 01-5 5h-3a5 5 0 01-5-5v-3a2 2 0 013-1.7z" /></symbol>
        <symbol id="t-Poison" viewBox="0 0 24 24"><path fill="currentColor" d="M12 2c4 6 6 8 6 12a6 6 0 01-12 0c0-4 2-6 6-12z" /><circle cx="10" cy="13" r="1.4" fill="var(--ink-0,#05050c)" /><circle cx="14" cy="15" r="1.1" fill="var(--ink-0,#05050c)" /></symbol>
        <symbol id="t-Ground" viewBox="0 0 24 24"><path fill="currentColor" d="M2 20L9 6l4 7 3-4 6 11z" /></symbol>
        <symbol id="t-Flying" viewBox="0 0 24 24"><path fill="currentColor" d="M20 4C11 4 4 11 4 20c1 0 2 0 3-1 5 0 13-6 13-15z" /></symbol>
        <symbol id="t-Psychic" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" d="M12 6a6 6 0 106 6 4 4 0 10-4-4" /><circle cx="12" cy="12" r="1.6" fill="currentColor" /></symbol>
        <symbol id="t-Bug" viewBox="0 0 24 24"><ellipse cx="12" cy="14" rx="4.5" ry="6" fill="currentColor" /><path fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" d="M12 8V4M9 5L6 3M15 5l3-2M7 13H3M17 13h4M7 17H3M17 17h4" /></symbol>
        <symbol id="t-Rock" viewBox="0 0 24 24"><path fill="currentColor" d="M6 20l-2-7 5-6 7 1 3 6-4 6z" /></symbol>
        <symbol id="t-Ghost" viewBox="0 0 24 24"><path fill="currentColor" d="M5 21V11a7 7 0 0114 0v10l-2.3-2-2.3 2-2.4-2-2.3 2z" /><circle cx="9.3" cy="10.5" r="1.3" fill="var(--ink-0,#05050c)" /><circle cx="14.7" cy="10.5" r="1.3" fill="var(--ink-0,#05050c)" /></symbol>
        <symbol id="t-Dragon" viewBox="0 0 24 24"><path fill="currentColor" d="M3 5c5 0 8 2 10 6 2-2 5-2 7 0-1 1-3 2-3 4 2 1 3 3 3 5-4 0-7-1-9-4-2 2-5 3-8 3 1-2 3-3 3-5-3-1-5-3-6-6z" /></symbol>

        {/* ── Node-kind icons (24×24, currentColor) ── */}
        <symbol id="k-sword" viewBox="0 0 24 24"><path fill="currentColor" d="M20 3l1 1-9 9-2-2 9-9zM4 15l4-1 4 4-1 4-2-2-2 3-1-1 3-2z" /></symbol>
        <symbol id="k-star" viewBox="0 0 24 24"><path fill="currentColor" d="M12 2l2.6 6.3 6.8.5-5.2 4.4 1.7 6.6L12 16.7 6.1 20.4l1.7-6.6L2.6 8.8l6.8-.5z" /></symbol>
        <symbol id="k-skull" viewBox="0 0 24 24"><path fill="currentColor" d="M12 2a8 8 0 00-8 8c0 3 1.5 4.6 3 5.6V19a1 1 0 001 1h1.5v-2h1v2h1v-2h1v2H15a1 1 0 001-1v-3.4c1.5-1 3-2.6 3-5.6a8 8 0 00-7-8zM8.7 11.6a2 2 0 110-4 2 2 0 010 4zm6.6 0a2 2 0 110-4 2 2 0 010 4z" /></symbol>
        <symbol id="k-coin" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" strokeWidth="2" /><path fill="currentColor" d="M12.9 6h-1.5v1.3c-1.5.2-2.5 1.1-2.5 2.5 0 1.6 1.3 2.2 2.8 2.6 1 .3 1.5.6 1.5 1.2 0 .5-.5.9-1.3.9-1 0-1.6-.5-1.7-1.3H8.6c.1 1.5 1.1 2.4 2.8 2.6V18h1.5v-1.3c1.6-.2 2.6-1.2 2.6-2.6 0-1.6-1.3-2.3-2.9-2.7-1-.3-1.4-.5-1.4-1.1 0-.5.4-.8 1.1-.8.9 0 1.4.4 1.5 1.1h1.6c-.1-1.4-1-2.3-2.5-2.5z" /></symbol>
        <symbol id="k-gem" viewBox="0 0 24 24"><path fill="currentColor" d="M6 3h12l3.5 6L12 22 2.5 9z" /><path fill="rgba(255,255,255,.35)" d="M6 3l2 6h8l2-6-3 6-3-6-3 6z" /></symbol>
        <symbol id="k-question" viewBox="0 0 24 24"><path fill="currentColor" d="M12 3a5 5 0 00-5 5h2.5a2.5 2.5 0 115 0c0 1.5-1 2-2 2.7-.9.6-1.5 1.4-1.5 2.8V15h2.4v-.4c0-.9.5-1.3 1.4-1.9C16.5 11.9 17 10.8 17 8.9A5 5 0 0012 3zM10.6 17.5h2.8v2.8h-2.8z" /></symbol>
        <symbol id="k-heart" viewBox="0 0 24 24"><path fill="currentColor" d="M12 21C4.5 14.5 3 11 5.5 7.5 7.3 5 11 5.3 12 8c1-2.7 4.7-3 6.5-.5C21 11 19.5 14.5 12 21z" /></symbol>
      </defs>
    </svg>
  );
}
