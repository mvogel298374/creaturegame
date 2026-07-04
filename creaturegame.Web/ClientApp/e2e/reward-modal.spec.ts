import { test, expect } from '@playwright/test';
import { fightButton } from './helpers';

// The Run Economy reward modal (Treasure/Mystery node), driven deterministically by a fixed seed. This closes
// the known live-verification gap: the reward modal + gold HUD credit had unit/integration coverage but were
// never observed in a browser (a DOM auto-player couldn't reliably win a battle to trigger a drop). Seeding the
// run sidesteps that — seed 31 with CHARIZARD @ L50 lays the FIRST biome node as a Treasure, so the modal fires
// right after the opening route pick, no battle required.
//
// If the reward-tuning (RewardCalculator) or node-weighting (RunDirector.PickInteriorNode) changes, this seed
// may no longer land a Treasure first — re-discover a reward-first seed and update the constant below.
const REWARD_FIRST_SEED = 31;

async function startSeededRun(page: import('@playwright/test').Page, seed: number, species = 'CHARIZARD') {
  // Land directly on /select with the seed on the URL (react-router drops the query on nav from the title, so
  // going through the title screen would lose it). ?e2e=1 keeps the app in test mode (collapsed delays).
  await page.goto(`/select?e2e=1&seed=${seed}`);
  await page.locator('.species-card').first().waitFor({ state: 'visible', timeout: 10_000 });
  await page.locator('.select-search').fill(species);
  await page.locator('.species-card', { has: page.locator('.card-name', { hasText: new RegExp(`^${species}$`, 'i') }) }).click();
  await page.getByRole('button', { name: /CONFIRM/i }).click();
  // Opening route choice — pick the first offered biome; its first node is the seeded Treasure.
  await page.locator('.biome-card').first().click({ timeout: 15_000 });
}

test.describe('Run Economy reward modal', () => {
  test('a Treasure node shows the reward modal + credits the gold HUD; OK continues the run', async ({ page }) => {
    test.setTimeout(60_000);
    await startSeededRun(page, REWARD_FIRST_SEED);

    // The Treasure node blocks on the modal (the backend awaits the ack).
    const modal = page.locator('.reward-modal');
    await expect(modal).toBeVisible();
    await expect(page.locator('.reward-title')).toHaveText('You found treasure!');

    // A guaranteed Treasure shows what it held: a gold line (+N₽) and at least one item line.
    await expect(page.locator('.reward-line--gold')).toHaveText(/^\+\d+₽$/);
    await expect(page.locator('.reward-line--item')).toHaveCount(1);

    // The gold was credited server-side and the HUD reflects the running total (was ₽0 at run start).
    const hud = page.locator('.gold-hud-amount');
    await expect(hud).not.toHaveText('0');
    const goldTotal = Number((await hud.textContent())?.trim());
    expect(goldTotal).toBeGreaterThan(0);

    // OK acknowledges the reward (unblocking the backend) and dismisses the modal — the run then continues into
    // the next node (a battle: its action menu enables), and the credited gold persists across the transition.
    await page.locator('.reward-modal .reward-buttons button', { hasText: /^OK$/ }).click();
    await expect(modal).toBeHidden();
    await expect(fightButton(page)).toBeEnabled({ timeout: 20_000 });
    await expect(hud).toHaveText(String(goldTotal));
  });
});
