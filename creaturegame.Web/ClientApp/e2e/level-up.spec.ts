import { test, expect } from '@playwright/test';
import { fightButton, bridgeEvents, reachLog, dismissRewardChoiceIfPresent } from './helpers';

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

    // Persist-until-input: it does NOT auto-hide — still visible after a wait (the win's reward-choice modal
    // may pop over it in the meantime; that doesn't dismiss the panel).
    await page.waitForTimeout(1200);
    await expect(panel).toBeVisible();

    // A win now also offers a reward choice that covers the field — take the gold bag so the FIGHT menu
    // underneath is reachable (the stat panel persists behind the modal), then confirm an input dismisses it.
    await dismissRewardChoiceIfPresent(page);
    await fightButton(page).click();
    await expect(panel).toHaveCount(0);
  });

  // Regression guard for the stat-panel column spacing. The bug: `.levelup-table td { padding: 2px 0 }`
  // out-specified the columns' `padding-right`, collapsing it to 0, so the gain (+2) and total (20) rendered
  // touching as "+220" — unreadable. Asserted via real rendered-text geometry (a Range box per cell), so it
  // fails if the numbers ever collide again. DOM geometry is deterministic — no timing, no canvas.
  test('the gain and total numbers stay visibly separated (column spacing)', async ({ page }) => {
    test.setTimeout(120_000);
    await reachLog(page, /grew to level \d+!/);

    const panel = page.locator('.levelup-panel');
    await expect(panel).toBeVisible();

    const m = await page.evaluate(() => {
      const row = document.querySelector('.levelup-table tr');
      if (!row) return null;
      const stat = row.querySelector('.levelup-stat') as HTMLElement;
      const gain = row.querySelector('.levelup-gain') as HTMLElement;
      const total = row.querySelector('.levelup-total') as HTMLElement;
      if (!stat || !gain || !total) return null;
      // Measure the actual rendered TEXT box (not the cell box) so we see what the player sees.
      const textRect = (el: HTMLElement) => {
        const range = document.createRange();
        range.selectNodeContents(el);
        return range.getBoundingClientRect();
      };
      return {
        gap: textRect(total).left - textRect(gain).right, // horizontal space between +gain and total
        statPadRight: parseFloat(getComputedStyle(stat).paddingRight),
        gainPadRight: parseFloat(getComputedStyle(gain).paddingRight),
        gainText: (gain.textContent ?? '').trim(),
        totalText: (total.textContent ?? '').trim(),
      };
    });

    expect(m).not.toBeNull();
    // Both a gain (+N) and a separate total (N) are actually rendered.
    expect(m!.gainText).toMatch(/^\+\d+$/);
    expect(m!.totalText).toMatch(/^\d+$/);
    // The two numbers are clearly apart, not glued into one (the regression rendered them touching, gap ~0).
    expect(m!.gap).toBeGreaterThan(10);
    // ...and the per-column right padding actually resolves (the specificity bug forced these to 0).
    expect(m!.statPadRight).toBeGreaterThanOrEqual(8);
    expect(m!.gainPadRight).toBeGreaterThanOrEqual(8);
  });
});
