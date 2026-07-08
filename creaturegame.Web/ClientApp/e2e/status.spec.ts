import { test, expect } from '@playwright/test';
import { startBattle, fightButton, chooseMove, bridgeEvents } from './helpers';

// §6 status conditions, end-to-end. We inflict the status with a *player* move (deterministic target) and
// use the retry-until-lands loop (Gen 1 accuracy can whiff) that stat-stage.spec.ts uses for Growth.
// Bulbasaur @ L50 knows Sleep Powder (its learnset) and is bulky enough to survive the missed attempts.
// Enemy-inflicted status / type-immunity edges stay at the unit/integration layer (no enemy control in E2E).
test.describe('Status conditions', () => {
  test('Sleep Powder puts the enemy to sleep — status badge on its nameplate + log line', async ({ page }) => {
    test.setTimeout(60_000);
    // Fixed seed → a reproducible enemy/run so this can't race on a shared in-suite backend (the badge assertion
    // had flaked when a coin-flip enemy state shifted between the log line and the nameplate check).
    await startBattle(page, 'BULBASAUR', 50, 1);

    const asleepLine = page.locator('.log-line').filter({ hasText: /fell asleep!/ }).first();

    let asleep = false;
    for (let i = 0; i < 6 && !asleep; i++) {
      if (!(await fightButton(page).isEnabled().catch(() => false))) break;
      await chooseMove(page, 'SLEEP POWDER').catch(() => {});
      asleep = await asleepLine.waitFor({ state: 'visible', timeout: 8_000 })
        .then(() => true).catch(() => false);
    }

    expect(asleep, 'expected a "… fell asleep!" log line').toBe(true);
    // The enemy nameplate shows the sleep badge, and the status sound fired.
    await expect(page.locator('.nameplate--enemy .status-badge')).toBeVisible();
    expect((await bridgeEvents(page)).some(e => e.name === 'playStatusSound')).toBe(true);
  });
});
