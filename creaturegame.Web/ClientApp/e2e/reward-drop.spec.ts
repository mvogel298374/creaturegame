import { test, expect } from '@playwright/test';
import { fightButton } from './helpers';

// The Run Economy reward flow at a Treasure node, driven deterministically by a fixed seed. This closes the
// known live-verification gap: the reward + wallet credit had unit/integration coverage but were never observed
// in a browser (a DOM auto-player couldn't reliably win a battle to trigger a drop). Seeding sidesteps that —
// seed 31 with CHARIZARD @ L50 lays the FIRST biome node as a Treasure, so the reward fires right after the
// opening route pick, no battle required.
//
// Every rolled reward now presents a BLOCKING pick-one-of-N choice modal (two rarity items or a gold bag); the
// player's pick is what releases the run loop. This spec picks the gold bag (a deterministic credit) and
// verifies it lands in the BAG money box, the drop hover, and the loot log.
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

test.describe('Run Economy reward choice (Treasure node)', () => {
  test('a Treasure node pops the pick-one-of-N choice modal; taking the gold bag credits the BAG and continues', async ({ page }) => {
    test.setTimeout(60_000);
    await startSeededRun(page, REWARD_FIRST_SEED);

    // The reward-choice modal blocks the run — it offers cards including the always-present gold bag.
    const modal = page.locator('.reward-modal');
    await expect(modal).toBeVisible({ timeout: 15_000 });
    await expect(modal.locator('.reward-card')).not.toHaveCount(0);
    const goldCard = modal.locator('.reward-card--gold');
    await expect(goldCard).toBeVisible();

    // Read the gold-bag amount off the card ("60₽") so we can assert it's exactly what gets credited.
    const goldAmount = Number((await goldCard.locator('.reward-card-name').textContent())?.match(/(\d+)/)?.[1]);
    expect(goldAmount).toBeGreaterThan(0);

    // Pick the gold bag → ChooseReward releases the run loop; the modal closes and the reward is applied.
    await goldCard.click();
    await expect(modal).toHaveCount(0);

    // The chosen reward is announced: the yellow loot line + the transient drop hover with its gold chip.
    await expect(page.locator('.log-line--loot').filter({ hasText: new RegExp(`treasure held ${goldAmount}G`, 'i') }))
      .toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.drop-hover .drop-chip--gold')).toBeVisible();

    // The run flows on into the next node — a battle whose menu enables.
    await expect(fightButton(page)).toBeEnabled({ timeout: 20_000 });

    // The credited gold lives in the BAG money box — proving the pick was applied server-side and shown where
    // the player looks for money.
    await page.getByRole('button', { name: /^BAG$/i }).click();
    await expect(page.locator('.bag-gold-amount')).toHaveText(String(goldAmount));
    await page.getByRole('button', { name: /BACK/i }).click();
  });
});
