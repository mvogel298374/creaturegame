import { test, expect } from '@playwright/test';
import { startBattle, fightButton } from './helpers';

// The Run Economy Shop node, end-to-end. The shop is **affordability-gated**: a biome only keeps a Shop node if
// the wallet can afford the cheapest possible item when the route is fixed (at biome entry). The run's wallet
// starts at 0₽, so the OPENING biome never contains a shop — a broke player never gets a dead, all-unaffordable
// shop. This spec pins that gate deterministically (seed 1, CHARIZARD @ L50): the run opens on a battle, with no
// shop modal and no shop banner, and the gold HUD confirms the empty wallet.
//
// The shop's *purchase* mechanics (spend the wallet, fill the bag, the 99-per-slot cap, the ShopOffered /
// ShopItemPurchased wire events, the modal reducer/timeline) are covered exhaustively at the unit/integration
// layer (RunDirectorNodeTests, ShopCalculatorTests, BagTests, WebEventContractTests, and the timeline/reducer
// Vitest suites) — a shop only surfaces mid-run once gold is in hand, which no fixed seed reaches without a long
// coin-flip-survival playthrough, so an in-browser purchase stays out of the deterministic E2E lane.
test.describe('Run Economy shop (affordability gate)', () => {
  test('a broke opening run gets no Shop node — the first node is a battle, wallet 0₽', async ({ page }) => {
    test.setTimeout(60_000);
    await startBattle(page, 'CHARIZARD', 50, 1);

    // The opening biome's route was fixed while the wallet was 0₽, so any Shop slot was dropped: no shop modal
    // and no "you happened upon a shop" banner before the first fight — the run opens straight into a battle.
    await expect(page.locator('.shop-modal')).toHaveCount(0);
    await expect(page.locator('.log-line').filter({ hasText: /happened upon a shop/i })).toHaveCount(0);
    await expect(fightButton(page)).toBeEnabled();

    // The premise the gate reads: the wallet is empty at the opening (shown in the BAG money box).
    await page.getByRole('button', { name: /^BAG$/i }).click();
    await expect(page.locator('.bag-gold-amount')).toHaveText('0');
    await page.getByRole('button', { name: /BACK/i }).click();
  });
});
