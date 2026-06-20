// Bag-menu domain logic, kept pure so it's unit-testable away from React (like timeline.ts).
// A BagItem mirrors the backend BagItemView (GET /api/game/{gameId}/bag); `category` is the
// ItemCategory enum name as a string.

export interface BagItem {
  id: number;
  name: string;
  category: string;
  quantity: number;
  description: string;
  // True for a whole-moveset PP restore (Elixir / Max Elixir). Single-move restores (Ether / Max Ether)
  // are false and need a move-slot pick before use.
  restoresPpAllMoves: boolean;
}

// The categories that actually do something when used in battle this scope — the same set the engine's
// ItemEffects registry implements (Healing, StatusCure, PpRestore, BattleStatBoost). Ball (catch — gated on
// Encounter Logic) and Revive (needs a party) resolve to ItemUseFailed and would just waste the turn, so the
// menu hides them rather than letting the player burn a turn on a guaranteed no-op.
const USABLE_CATEGORIES = new Set(['Healing', 'StatusCure', 'PpRestore', 'BattleStatBoost']);

export function isUsableInBattle(category: string): boolean {
  return USABLE_CATEGORIES.has(category);
}

// Only a single-move PP restore (Ether / Max Ether) needs the player to choose which move slot to refill.
// Whole-moveset restores (Elixir / Max Elixir) and every other category target nothing extra.
export function needsMoveTarget(item: Pick<BagItem, 'category' | 'restoresPpAllMoves'>): boolean {
  return item.category === 'PpRestore' && !item.restoresPpAllMoves;
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
  { category: 'PpRestore', label: 'PP RESTORE' },
  { category: 'BattleStatBoost', label: 'BATTLE' },
];

// Drop the items the in-battle menu can't act on, then split the rest into the labelled groups above
// (preserving each group's incoming order). Empty groups are omitted so the menu only shows pockets that
// have something usable.
export function groupBagItems(
  items: BagItem[]
): ReadonlyArray<{ label: string; items: BagItem[] }> {
  const usable = items.filter(i => isUsableInBattle(i.category) && i.quantity > 0);
  return CATEGORY_LABELS.map(({ category, label }) => ({
    label,
    items: usable.filter(i => i.category === category),
  })).filter(g => g.items.length > 0);
}
