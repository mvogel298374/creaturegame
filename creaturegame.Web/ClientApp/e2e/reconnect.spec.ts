import { test, expect, type Page } from '@playwright/test';
import { startBattle, attackUntilShowing, answerRunPromptIfPresent, fightButton } from './helpers';

// R1d-a (docs/RECONNECT_RESILIENCE.md); a win's drop is a roll, so walk seeds as reward-drop.spec does.
const SEEDS = [1, 2, 3, 4, 5, 6];

async function reachTheNextTurn(page: Page) {
  await expect(async () => {
    await answerRunPromptIfPresent(page, { drafts: 'decline' });
    expect(await fightButton(page).isEnabled().catch(() => false)).toBe(true);
  }).toPass({ timeout: 30_000 });
}

test.describe('Reconnect replays an open prompt (R1d-a)', () => {
  test('a refresh with the reward choice open brings it back; once picked, a refresh does not', async ({ page }) => {
    test.setTimeout(150_000);
    const modal = page.locator('.reward-modal');
    let open = false;
    for (const seed of SEEDS) {
      await startBattle(page, 'CHARIZARD', 50, seed);
      if ((open = await attackUntilShowing(page, modal))) break;
    }
    expect(open, 'no seeded first battle popped the reward modal').toBe(true);

    await page.reload();
    await expect(modal).toBeVisible({ timeout: 15_000 });
    await modal.locator('.reward-card--gold').click();
    await expect(modal).toHaveCount(0);
    await reachTheNextTurn(page);

    // .modal-overlay covers every run prompt, not just the reward.
    await page.reload();
    await expect(fightButton(page)).toBeEnabled({ timeout: 20_000 });
    await expect(page.locator('.modal-overlay')).toHaveCount(0);
  });
});
