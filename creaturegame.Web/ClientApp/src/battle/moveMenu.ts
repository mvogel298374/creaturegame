import type { MoveInfo } from '../types/BattleEvents';

/**
 * Whether the player has any *selectable* move this turn — one with PP left that isn't Disabled.
 *
 * This is the client mirror of the engine's `Creature.CanSelectAnyMove`, and the two must agree: the server
 * resolves a FIGHT choice with nothing selectable to Struggle (`SignalRInput`), so if the client disagreed it
 * would either hide a usable move menu or open a move list whose every click silently became Struggle.
 *
 * Gen 1 shows no move list at all when nothing is usable — choosing FIGHT prints "no moves left" and Struggles
 * immediately. So `false` here means the FIGHT button spends the turn directly rather than opening the submenu.
 * BAG and SWITCH are unaffected: Gen 1 keeps the rest of the menu open at 0 PP, and Struggle is a consequence
 * of *choosing FIGHT*, never auto-resolved before the player chooses.
 *
 * An empty move list is deliberately treated as "usable" rather than as a Struggle situation. It isn't a real
 * battle state (every creature has at least one move, and each `TurnStarted` carries the full list) — it only
 * shows up before the first `TurnStarted` lands. Returning `true` there makes the degenerate case open a
 * (padded, unclickable) move menu instead of silently spending the turn on Struggle.
 */
export function hasUsableMove(moves: MoveInfo[]): boolean {
  if (moves.length === 0) return true;
  return moves.some(m => m.ppCurrent > 0 && !m.disabled);
}
