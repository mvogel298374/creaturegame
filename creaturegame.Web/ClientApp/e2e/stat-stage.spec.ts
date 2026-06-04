import { test, expect } from '@playwright/test';
import { startBattle, fightButton, chooseMove } from './helpers';

// End-to-end proof for a self-targeting stat move. Bulbasaur @ L50 learns Growth (batch 8), which in
// Gen 1 raises the user's (combined) Special — NOT Attack as modern data reports. This drives the
// move-data fix and the StatStageChanged → "rose" render all the way to the battle log in a browser.
test.describe('Stat-stage moves', () => {
  test("Growth raises Bulbasaur's Special and logs it", async ({ page }) => {
    test.setTimeout(60_000);
    await startBattle(page, 'BULBASAUR');

    // Growth can whiff on the Gen 1 1/256 accuracy bug; retry across a few turns until it lands
    // (Bulbasaur is bulky enough at L50 to survive the attempts). It almost always lands turn 1.
    const roseLine = page.locator('.log-line').filter({ hasText: /BULBASAUR's Special rose!/ }).first();

    let rose = false;
    for (let i = 0; i < 5 && !rose; i++) {
      if (!(await fightButton(page).isEnabled().catch(() => false))) break;
      await chooseMove(page, 'GROWTH').catch(() => {});
      rose = await roseLine.waitFor({ state: 'visible', timeout: 8_000 })
        .then(() => true).catch(() => false);
    }

    expect(rose, "expected a \"BULBASAUR's Special rose!\" log line").toBe(true);
  });
});
