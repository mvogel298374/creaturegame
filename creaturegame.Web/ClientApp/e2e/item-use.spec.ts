import { test, expect } from '@playwright/test';
import { startBattle, fightButton, waitForLog } from './helpers';

// Item-use turn, end-to-end through the bag UI (Phase 3). We use X ATTACK because it always applies on
// turn 1 — no HP/status precondition like a Potion — so the test stays deterministic. It still exercises
// the whole loop: open the bag, pick an item, the engine resolves it (ItemUsed → the stat-stage effect),
// and because using an item is the player's WHOLE turn (item priority is above any move), the enemy still
// gets to attack afterwards.
test.describe('Item use', () => {
  test('using X ATTACK from the bag spends the turn — stat rises and the enemy still attacks', async ({ page }) => {
    test.setTimeout(60_000);
    await startBattle(page, 'CHARIZARD');

    const playerName = (await page.locator('.nameplate--player .nameplate-name').textContent())?.trim() ?? '';
    const enemyName  = (await page.locator('.nameplate--enemy .nameplate-name').textContent())?.trim() ?? '';
    expect(playerName).not.toBe('');
    expect(enemyName).not.toBe('');

    // Open the bag and pick X ATTACK (matched on the item name, not the description, to avoid stray hits).
    await page.getByRole('button', { name: /^BAG$/ }).click();
    const xAttack = page.locator('.bag-item', {
      has: page.locator('.bag-item-name', { hasText: /^X ATTACK$/ }),
    });
    await expect(xAttack).toBeVisible();
    await xAttack.click();

    // The item resolves first: it's announced and its stat boost lands.
    await waitForLog(page, new RegExp(`Used X ATTACK on ${playerName}!`));
    await waitForLog(page, new RegExp(`${playerName}'s Attack rose!`));

    // Using the item was the whole turn, so the enemy still takes its move afterwards…
    await waitForLog(page, new RegExp(`^${enemyName} used `));
    // …and the turn loop comes back round to the player.
    await expect(fightButton(page)).toBeEnabled({ timeout: 15_000 });
  });
});
