import { test, expect } from '@playwright/test';
import { startBattle, fightButton } from './helpers';

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
});
