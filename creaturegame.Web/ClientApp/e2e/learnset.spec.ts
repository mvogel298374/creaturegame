import { test, expect } from '@playwright/test';
import { startBattle, fightButton, reachLog } from './helpers';

// Regression lock for the Learnset System: the player's starting moves must come from the
// species' learnset (canonical most-recent-4 at the chosen level), NOT the old
// random-from-the-full-pool behaviour (which let e.g. a Bulbasaur roll Hydro Pump).
test.describe('Learnset', () => {
  test('starter knows exactly its canonical learnset moves (Bulbasaur @ L50)', async ({ page }) => {
    // Default level is 50; the helper confirms without touching the slider.
    await startBattle(page, 'BULBASAUR');
    await fightButton(page).click();

    const moves = page.locator('.move-btn');
    await expect(moves).toHaveCount(4);

    const names = (await moves.locator('.move-name').allTextContents())
      .map(s => s.trim().toUpperCase())
      .sort();

    // The 4 highest-level moves Bulbasaur can learn by L50: Razor Leaf(27), Growth(34),
    // Sleep Powder(41), Solar Beam(48). Deterministic for the canonical (player) strategy.
    expect(names).toEqual(['GROWTH', 'RAZOR LEAF', 'SLEEP POWDER', 'SOLAR BEAM'].sort());
  });

  // Level-up move learning, end-to-end. Mew (started at level 9) knows only Pound — three free slots — so
  // the move it learns on reaching level 10, Transform, is AUTO-added with no replace prompt. Determinism
  // is client-only (no per-run seed yet, Tech Debt #3): Mew is chosen because its BST 500 means wild foes
  // are BST-matched-strong → a win awards lots of XP, so a single 9→10 crossing needs few wins; reachLog
  // reloads a lost run. The replace-move MODAL path needs four full slots AT a learn-level, which isn't
  // reliably reachable without the seed, so it stays covered at the .NET/Vitest layer until then.
  test('a low-level Mew auto-learns Transform on reaching level 10', async ({ page }) => {
    test.setTimeout(240_000); // a 9→10 crossing spans a few chained wins; reachLog restarts a lost run
    // Anchor the wait on the PLAYER's name so an enemy's Mimic ("FOO learned …!") can't satisfy it early —
    // the player's own species is excluded from the wild pool, so "MEW learned" is uniquely the player.
    const log = await reachLog(page, /MEW learned .+!/, { species: 'MEW', level: 9, attempts: 14 });
    expect(log.some(l => /MEW learned TRANSFORM!/.test(l))).toBe(true);
  });
});
