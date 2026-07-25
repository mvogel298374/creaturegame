import { describe, it, expect } from 'vitest';
import { hasUsableMove } from './moveMenu';
import type { MoveInfo } from '../types/BattleEvents';

const move = (over: Partial<MoveInfo> = {}): MoveInfo => ({
  name: 'tackle', type: 'Normal', ppCurrent: 10, ppMax: 35, ...over,
});

// hasUsableMove decides whether FIGHT opens the move list or spends the turn as Struggle, so it has to agree
// with the engine's Creature.CanSelectAnyMove exactly — a disagreement either hides a usable menu or opens one
// whose every click silently Struggles. These cases mirror that server predicate.
describe('hasUsableMove — the client mirror of CanSelectAnyMove', () => {
  it('is true while any move still has PP', () => {
    expect(hasUsableMove([move({ ppCurrent: 0 }), move({ ppCurrent: 1 })])).toBe(true);
  });

  it('is false once every move is out of PP — FIGHT then Struggles instead of opening the list', () => {
    expect(hasUsableMove([move({ ppCurrent: 0 }), move({ ppCurrent: 0 })])).toBe(false);
  });

  it('ignores a Disabled move even though it still has PP', () => {
    // Gen 1 Disable locks the move out entirely; PP left on it does not make it selectable.
    expect(hasUsableMove([move({ ppCurrent: 5, disabled: true })])).toBe(false);
  });

  it('is true when a Disabled move sits alongside one that is still usable', () => {
    expect(hasUsableMove([move({ ppCurrent: 5, disabled: true }), move({ ppCurrent: 3 })])).toBe(true);
  });

  it('treats an empty move list as usable, so a pre-TurnStarted render never auto-Struggles', () => {
    // Not a real battle state (every creature has a move, and each TurnStarted carries the full list) — it only
    // appears before the first TurnStarted lands. Opening a padded, unclickable menu beats spending the turn.
    expect(hasUsableMove([])).toBe(true);
  });
});
