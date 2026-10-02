import { useEffect, useState } from 'react';
import { useDevMode } from './useDevMode';
import { damageRangesUrl } from '../battle/overviewPicker';

export type DamageRange = { min: number; max: number } | null;

// Dev Mode: per-move low–high damage vs the current foe, parallel to the creature's moveset (null entry = a move
// with no damage to show). Fetched when the consumer mounts — the fight menu and CHECK POKEMON both mount fresh
// each time they open, which is exactly when stat stages etc. may have changed. Stays null unless Dev Mode is
// on and the server answers (a 404 — dev off, or no enemy yet — just means "show nothing").
export function useDamageRanges(gameId: string | null, side: 'player' | 'enemy', slot?: number): DamageRange[] | null {
  const { enabled } = useDevMode();
  const [ranges, setRanges] = useState<DamageRange[] | null>(null);

  useEffect(() => {
    setRanges(null);
    if (!enabled || !gameId) return;
    let live = true;
    fetch(damageRangesUrl(gameId, side, slot))
      .then(r => (r.ok ? r.json() : null))
      .then(d => { if (live && d && Array.isArray(d.ranges)) setRanges(d.ranges); })
      .catch(() => { /* dev-only extra; failing quietly is the right behaviour */ });
    return () => { live = false; };
  }, [enabled, gameId, side, slot]);

  return ranges;
}
