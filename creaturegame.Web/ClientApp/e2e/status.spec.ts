import { test, expect } from '@playwright/test';
import { startBattle, fightButton, chooseMove, bridgeEvents } from './helpers';

// §6 status conditions, end-to-end. We inflict the status with a *player* move (deterministic target) and retry
// until it STICKS — a stronger loop than stat-stage.spec.ts's retry-until-lands for Growth, because sleep can
// land and then immediately expire (see below), which a retry-until-lands loop mistakes for success.
// Bulbasaur @ L50 knows Sleep Powder (its learnset) and is bulky enough to survive the missed attempts.
// Enemy-inflicted status / type-immunity edges stay at the unit/integration layer (no enemy control in E2E).
test.describe('Status conditions', () => {
  test('Sleep Powder puts the enemy to sleep — status badge on its nameplate + log line', async ({ page }) => {
    test.setTimeout(60_000);
    // The seed keeps the run cheap and repeatable, but it does NOT make the outcome deterministic: the seed fixes
    // the server's stream while the *client's* move sequence drives it, so a whiffed/retried turn shifts every
    // later roll. The assertions below are written not to care.
    await startBattle(page, 'BULBASAUR', 50, 1);

    const asleepLine = page.locator('.log-line').filter({ hasText: /fell asleep!/ }).first();
    const badge = page.locator('.nameplate--enemy .status-badge');
    const menuReady = () =>
      expect(async () => {
        expect(await fightButton(page).isEnabled().catch(() => false)).toBe(true);
      }).toPass({ timeout: 12_000 });

    // Cast until the sleep STICKS, not merely until it lands. Two things make one cast unreliable and neither is
    // a bug: Sleep Powder can miss (Gen 1 accuracy), and Gen 1 rolls sleep at 1–7 turns with the counter
    // decrementing when the target next tries to act — so a 1-turn roll on a slower foe wakes it later in the
    // SAME round ("fell asleep!" immediately followed by "woke up!"). In that case the badge's entire lifetime is
    // one collapsed E2E step (`delay` caps waits at 12ms under E2E), so it is not reliably observable at all —
    // which is what made this flake ~1 run in 5, and why watching for the badge can't fix it either. Re-casting
    // until a roll ≥2 sticks converges fast (a landed cast is a 1-turn roll only 1/7 of the time) and tests what
    // this spec is actually for: while the foe IS asleep, its nameplate carries the badge. Reading too early is
    // self-correcting — it just costs another cast — so the loop can never pass on a badge that wasn't really up.
    // Cap at 20: this seed settles in ~3 casts, but a shifted stream can need far more — an unlucky sampled run
    // took 11 (misses plus a 1-turn roll). Bulbasaur only casts Sleep Powder, so it deals no damage and simply
    // tanks Water Gun meanwhile; it comfortably outlasts 20 casts, and the loop exits the moment the badge shows.
    let sawAsleepLine = false;
    let badgeSeen = false;
    for (let i = 0; i < 20 && !badgeSeen; i++) {
      await menuReady();
      await chooseMove(page, 'SLEEP POWDER').catch(() => {});
      await page.waitForTimeout(400); // let the round resolve past the collapsed E2E steps
      await menuReady();
      if (await asleepLine.isVisible().catch(() => false)) sawAsleepLine = true;
      badgeSeen = await badge.isVisible().catch(() => false);
    }

    expect(sawAsleepLine, 'expected a "… fell asleep!" log line').toBe(true);
    // The enemy nameplate carries the sleep badge while it sleeps, and the status sound fired.
    expect(badgeSeen, 'expected the sleep badge on the enemy nameplate while it is asleep').toBe(true);
    expect((await bridgeEvents(page)).some(e => e.name === 'playStatusSound')).toBe(true);
  });
});
