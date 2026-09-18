// Bag-menu domain logic, kept pure so it's unit-testable away from React (like timeline.ts).
// A BagItem mirrors the backend BagItemView (GET /api/game/{gameId}/bag); `category` is the
// ItemCategory enum name as a string.

import type { PartyMember } from './timeline';
import { overviewSlotUrl } from './overviewPicker';

export interface BagItem {
  id: number;
  name: string;
  category: string;
  quantity: number;
  description: string;
  // True for a whole-moveset PP restore (Elixir / Max Elixir). Single-move restores (Ether / Max Ether)
  // are false and need a move-slot pick before use.
  restoresPpAllMoves: boolean;
  // Whether using the item in battle now would do anything — the server's verdict, computed from the
  // engine's ItemEffects registry (BagItemView.UsableInBattle). Ball (catch — gated on Encounter Logic)
  // and Revive (needs a party) have no effect yet, so they arrive false and the menu hides them rather
  // than letting the player burn a turn on a guaranteed no-op. Read this flag; don't re-derive usability
  // from `category` here — that mapping lives in the backend and only the backend.
  usableInBattle: boolean;
}

export function isUsableInBattle(item: Pick<BagItem, 'usableInBattle'>): boolean {
  return item.usableInBattle;
}

// Only a single-move PP restore (Ether / Max Ether) needs the player to choose which move slot to refill.
// Whole-moveset restores (Elixir / Max Elixir) and every other category target nothing extra.
export function needsMoveTarget(item: Pick<BagItem, 'category' | 'restoresPpAllMoves'>): boolean {
  return item.category === 'PpRestore' && !item.restoresPpAllMoves;
}

// Whether picking this item asks which party member to use it on — the real games' "Use item on which
// POKÉMON?" screen. True for every category except BattleStatBoost (X-items/Guard Spec/Dire Hit always apply
// to the active creature — Gen 1 has no per-party-member stat-stage storage). Ball/Other never reach the bag
// menu (usableInBattle is false). Why the split → GENERATION_SEAMS.md §5.0.2.
export function needsPartyTarget(item: Pick<BagItem, 'category'>): boolean {
  return item.category !== 'BattleStatBoost';
}

// Which party members are valid picks when needsPartyTarget is true: Revive/Max Revive need a FAINTED one;
// every other category needs a LIVING one (the inverse).
export function partyTargetMode(item: Pick<BagItem, 'category'>): 'fainted' | 'living' {
  return item.category === 'Revive' ? 'fainted' : 'living';
}

// For a single-move PP restore (Ether/Max Ether) once a party member is picked: where to read THEIR moveset
// from. The bag menu only ever loads the active creature's moves up front, so picking the active creature can
// reuse that; picking anyone else needs a fresh fetch of their own moveset. Pure so Vitest can pin the
// branching without a DOM harness (the same reason overviewSlotUrl in overviewPicker.ts is pure).
export type MoveSource = { kind: 'inline' } | { kind: 'fetch'; url: string };

export function moveSourceForPartyPick(
  gameId: string,
  party: PartyMember[],
  partySlot: number
): MoveSource {
  const activeSlot = party.findIndex(m => m.isLead);
  return partySlot === activeSlot
    ? { kind: 'inline' }
    : { kind: 'fetch', url: overviewSlotUrl(gameId, party, partySlot) };
}

// Item names arrive as lowercase, hyphenated slugs ("super-potion", "x-attack"); the UI is uppercase
// throughout, so display them "SUPER POTION", "X ATTACK" — the item analogue of formatMoveName.
export function formatItemName(name: string): string {
  if (!name) return name;
  return name.replace(/-/g, ' ').toUpperCase();
}

// The bag menu groups items under these category headers, in this order. A flat list of 20+ item types
// (the generous test loadout) is hard to scan; grouping by what the item does mirrors the Gen 1 bag pockets.
export const CATEGORY_LABELS: ReadonlyArray<{ category: string; label: string }> = [
  { category: 'Healing', label: 'HEALING' },
  { category: 'StatusCure', label: 'STATUS' },
  { category: 'Revive', label: 'REVIVE' },
  { category: 'PpRestore', label: 'PP RESTORE' },
  { category: 'BattleStatBoost', label: 'BATTLE' },
];

// Drop the items the in-battle menu can't act on, then split the rest into the labelled groups above
// (preserving each group's incoming order). Empty groups are omitted so the menu only shows pockets that
// have something usable.
export function groupBagItems(
  items: BagItem[]
): ReadonlyArray<{ label: string; items: BagItem[] }> {
  const usable = items.filter(i => isUsableInBattle(i) && i.quantity > 0);
  return CATEGORY_LABELS.map(({ category, label }) => ({
    label,
    items: usable.filter(i => i.category === category),
  })).filter(g => g.items.length > 0);
}
