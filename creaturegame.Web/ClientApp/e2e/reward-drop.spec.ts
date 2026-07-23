import { test, expect } from '@playwright/test';
import { fightButton, chooseMove } from './helpers';

// The Run Economy reward flow, driven deterministically by a fixed seed. This closes the known
// live-verification gap: the reward + wallet credit had unit/integration coverage but were never observed in a
// browser (a DOM auto-player couldn't reliably win a battle to trigger a drop).
//
// Originally this drove a Treasure node directly (no battle needed) — seed 31 used to lay one as the very
// first biome node. That premise stopped being reachable once `RunDirector.DefaultNodePlan` was given its
// "soft opening" rule: a biome's first node is now unconditionally `WildBattle` (never Treasure/Elite/an
// interaction node), so the player is never greeted with a difficulty spike or a non-combat slot on entry
// (see `RunDirector.cs` `DefaultNodePlan`). No seed can land a Treasure first anymore — that wasn't RNG
// drift, it's a deliberate design rule. A battle win funnels through the SAME reward-choice modal as a
// Treasure/Mystery node (`RewardGranted`, one drop hover for every source — see `battleReducer.ts`), so this
// spec now wins the first (deterministic, seeded) battle instead: seed 1 with CHARIZARD @ L50 beats its first
// wild encounter (STARMIE) in a handful of turns using only the default first-available-move pick.
//
// Every rolled reward presents a BLOCKING pick-one-of-N choice modal (two rarity items or a gold bag); the
// player's pick is what releases the run loop. This spec picks the gold bag (a deterministic credit) and
// verifies it lands in the BAG money box, the drop hover, and the loot log.
//
// If combat balance changes (levels, base stats, move power) such that CHARIZARD @ L50 no longer wins its
// first battle within MAX_TURNS using the default move pick, re-discover a seed that does and update the
// constant below.
const WIN_FIRST_BATTLE_SEED = 1;
const MAX_TURNS = 15;

async function startSeededRun(page: import('@playwright/test').Page, seed: number, species = 'CHARIZARD') {
  // Land directly on /select with the seed on the URL (react-router drops the query on nav from the title, so
  // going through the title screen would lose it). ?e2e=1 keeps the app in test mode (collapsed delays).
  await page.goto(`/select?e2e=1&seed=${seed}`);
  await page.locator('.species-card').first().waitFor({ state: 'visible', timeout: 10_000 });
  await page.locator('.select-search').fill(species);
  await page.locator('.species-card', { has: page.locator('.card-name', { hasText: new RegExp(`^${species}$`, 'i') }) }).click();
  await page.getByRole('button', { name: /CONFIRM/i }).click();
  // Opening route choice (map-based) — click the first offered biome waypoint; its first node is always a
  // plain wild battle (RunDirector's soft-opening rule).
  await page.locator('.region-node--offered').first().click({ timeout: 15_000 });
}

/** Attacks with the default (first-available) move each turn until the reward-choice modal appears — i.e.
 * until the deterministic seeded battle is won. Deliberately does NOT use the generic play-loop helpers,
 * which auto-dismiss the reward modal; this spec needs it left standing to assert against. */
async function winFirstBattle(page: import('@playwright/test').Page): Promise<void> {
  const modal = page.locator('.reward-modal');
  for (let turn = 0; turn < MAX_TURNS; turn++) {
    if (await modal.isVisible().catch(() => false)) return;
    if (await fightButton(page).isEnabled().catch(() => false)) {
      await chooseMove(page).catch(() => {});
    }
    await page.waitForTimeout(150);
  }
  await expect(modal).toBeVisible({ timeout: 5_000 });
}

test.describe('Run Economy reward choice (battle-win drop)', () => {
  test('winning a battle pops the pick-one-of-N choice modal; taking the gold bag credits the BAG and continues', async ({ page }) => {
    test.setTimeout(60_000);
    await startSeededRun(page, WIN_FIRST_BATTLE_SEED);
    await winFirstBattle(page);

    // The reward-choice modal blocks the run — it offers cards including the always-present gold bag.
    const modal = page.locator('.reward-modal');
    await expect(modal).toBeVisible({ timeout: 15_000 });
    await expect(modal.locator('.reward-card')).not.toHaveCount(0);
    const goldCard = modal.locator('.reward-card--gold');
    await expect(goldCard).toBeVisible();

    // Read the gold-bag amount off the card ("30₽") so we can assert it's exactly what gets credited.
    const goldAmount = Number((await goldCard.locator('.reward-card-name').textContent())?.match(/(\d+)/)?.[1]);
    expect(goldAmount).toBeGreaterThan(0);

    // Pick the gold bag → ChooseReward releases the run loop; the modal closes and the reward is applied.
    await goldCard.click();
    await expect(modal).toHaveCount(0);

    // The chosen reward is announced: the yellow loot line ("Found NG!" — battle-win drops read differently
    // from a Treasure/Mystery node's "The treasure/mystery held..." — see `rewardGrantedMsg` in timeline.ts)
    // + the transient drop hover with its gold chip.
    await expect(page.locator('.log-line--loot').filter({ hasText: new RegExp(`Found ${goldAmount}G!`, 'i') }))
      .toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.drop-hover .drop-chip--gold')).toBeVisible();

    // The run flows on into the next encounter — a battle whose menu enables.
    await expect(fightButton(page)).toBeEnabled({ timeout: 20_000 });

    // The credited gold lives in the BAG money box — proving the pick was applied server-side and shown where
    // the player looks for money.
    await page.getByRole('button', { name: /^BAG$/i }).click();
    await expect(page.locator('.bag-gold-amount')).toHaveText(String(goldAmount));
    await page.getByRole('button', { name: /BACK/i }).click();
  });
});
