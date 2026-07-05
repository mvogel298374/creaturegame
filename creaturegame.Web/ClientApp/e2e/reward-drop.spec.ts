import { test, expect } from '@playwright/test';
import { fightButton } from './helpers';

// The Run Economy reward flow at a Treasure/Mystery node, driven deterministically by a fixed seed. This closes
// the known live-verification gap: the reward + wallet credit had unit/integration coverage but were never
// observed in a browser (a DOM auto-player couldn't reliably win a battle to trigger a drop). Seeding sidesteps
// that — seed 31 with CHARIZARD @ L50 lays the FIRST biome node as a Treasure, so the reward fires right after
// the opening route pick, no battle required.
//
// Node rewards now use the SAME vanishing loot hover as a battle drop (no blocking OK modal) — the client
// auto-acknowledges them so the run flows straight on.
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

test.describe('Run Economy reward drop (Treasure node)', () => {
  test('a Treasure node pops the vanishing loot hover (no OK modal), auto-continues, and credits the BAG', async ({ page }) => {
    test.setTimeout(60_000);
    await startSeededRun(page, REWARD_FIRST_SEED);

    // No blocking OK modal anymore — the node uses the same vanishing loot hover as a battle drop.
    await expect(page.locator('.reward-modal')).toHaveCount(0);

    // The yellow loot line is logged and the transient drop hover pops (with its gold chip). Caught while the
    // hover is up (it self-dismisses after ~2.8s) — assert it before waiting on the next node.
    const lootLine = page.locator('.log-line--loot').filter({ hasText: /treasure held/i });
    await expect(lootLine).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.drop-hover .drop-chip--gold')).toBeVisible();

    // The credited amount, read from the (persistent) log line: "The treasure held 40G, …".
    const granted = Number((await lootLine.textContent())?.match(/(\d+)G/)?.[1]);
    expect(granted).toBeGreaterThan(0);

    // The run flows straight on (auto-acked — no button to press) into the next node, a battle whose menu enables.
    await expect(fightButton(page)).toBeEnabled({ timeout: 20_000 });

    // The credited gold lives in the BAG money box — proving the reward was credited server-side and shown
    // where the player looks for money.
    await page.getByRole('button', { name: /^BAG$/i }).click();
    await expect(page.locator('.bag-gold-amount')).toHaveText(String(granted));
    await page.getByRole('button', { name: /BACK/i }).click();
  });
});
