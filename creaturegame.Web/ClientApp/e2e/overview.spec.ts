import { test, expect } from '@playwright/test';
import { startBattle } from './helpers';

// The CHECK POKEMON creature overview (INFO / STATS / MOVES tabs), fed by GET /api/game/{gameId}/player.
// Structure-level assertions only — the DV/stat/Stat-Exp values are RNG per run, so we assert shape (five
// stat rows each carrying a DV + Stat-Exp readout, move cards, the info sprite), not exact numbers.
test.describe('Creature overview (CHECK POKEMON)', () => {
  test('opens to STATS (5 stats with DV + Stat-Exp), and the MOVES / INFO tabs render', async ({ page }) => {
    test.setTimeout(60_000);
    await startBattle(page, 'CHARIZARD');

    await page.getByRole('button', { name: /CHECK POKEMON/i }).click();

    // STATS is the default tab: the five Gen 1 stats, each with a DV chip and an EV (Stat-Exp) chip.
    const rows = page.locator('.overview-stat-row');
    await expect(rows).toHaveCount(5);
    await expect(rows.first().locator('.overview-chip--dv')).toBeVisible();
    await expect(rows.first().locator('.overview-chip--ev')).toBeVisible();

    // MOVES tab — at least one move card.
    await page.locator('.overview-tab', { hasText: 'MOVES' }).click();
    await expect(page.locator('.overview-move').first()).toBeVisible();

    // INFO tab — the front sprite renders.
    await page.locator('.overview-tab', { hasText: 'INFO' }).click();
    await expect(page.locator('.overview-sprite')).toBeVisible();
  });
});
