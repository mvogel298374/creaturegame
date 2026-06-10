import { test, expect } from '@playwright/test';
import { fightButton, bridgeEvents, reachLog } from './helpers';

// XP & Level-Up, end-to-end. Determinism is client-only: starting at the minimum level (5) makes a win or
// two level the player quickly, so we play until the "grew to level N!" line appears, then assert the XP
// award, the level-up fanfare, and the stat-gain panel — including that it now stays up until the player's
// next input (no auto-hide).
test.describe('Level-up', () => {
  test('a low-level win fills XP, levels up with the fanfare + stat panel, and the panel waits for input', async ({ page }) => {
    test.setTimeout(120_000);   // min level → a win levels up fast; reachLog restarts if a run is lost first
    const log = await reachLog(page, /grew to level \d+!/);
    expect(log.some(l => /gained \d+ EXP\. Points!/.test(l))).toBe(true);
    expect(log.some(l => /grew to level \d+!/.test(l))).toBe(true);

    // The level-up fanfare fired over the bridge.
    expect((await bridgeEvents(page)).some(e => e.name === 'playLevelUpSound')).toBe(true);

    // The Gen 1 stat-gain panel is shown.
    const panel = page.locator('.levelup-panel');
    await expect(panel).toBeVisible();
    await expect(panel).toContainText('LEVEL UP!');

    // Persist-until-input: it does NOT auto-hide — still visible after a wait...
    await page.waitForTimeout(1200);
    await expect(panel).toBeVisible();

    // ...and is dismissed by any player input (opening the FIGHT menu).
    await fightButton(page).click();
    await expect(panel).toHaveCount(0);
  });
});
