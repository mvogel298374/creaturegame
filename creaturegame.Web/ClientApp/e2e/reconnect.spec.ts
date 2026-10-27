import { test, expect, type Page } from '@playwright/test';
import {
  startBattle, attackUntilShowing, answerRunPromptIfPresent, fightButton, playCurrentRunUntil, logLines,
} from './helpers';

// R1d-a (docs/RECONNECT_RESILIENCE.md); a win's drop is a roll, so walk seeds as reward-drop.spec does.
const SEEDS = [1, 2, 3, 4, 5, 6];

async function reachTheNextTurn(page: Page) {
  await expect(async () => {
    await answerRunPromptIfPresent(page, { drafts: 'decline' });
    expect(await fightButton(page).isEnabled().catch(() => false)).toBe(true);
  }).toPass({ timeout: 30_000 });
}

// The pinned map covers the controls, so it is opened only to read the pin and closed by its own button.
async function readLadderPin(page: Page): Promise<number> {
  const ladder = page.locator('.ladder-node');
  await expect(ladder).toHaveCount(0, { timeout: 10_000 }); // outlasts an auto-peek
  await page.getByRole('button', { name: 'Toggle route map' }).click();
  await expect(ladder.first()).toBeVisible();
  const pin = await ladder.evaluateAll(nodes => nodes.findIndex(n => n.classList.contains('ladder-node--current')));
  await page.getByRole('button', { name: 'Close map' }).click();
  await expect(ladder).toHaveCount(0, { timeout: 10_000 });
  return pin;
}

const battlesStarted = async (page: Page) => (await logLines(page)).filter(l => / VS /.test(l)).length;

test.describe('Reconnect restores the route map (R1d-d)', () => {
  test('a refresh mid-biome keeps the ladder pin on the node in progress', async ({ page }) => {
    test.setTimeout(150_000);
    await startBattle(page, 'CHARIZARD', 50, 1);
    const reached = await playCurrentRunUntil(
      page,
      async p => (await battlesStarted(p)) >= 2 && (await fightButton(p).isEnabled().catch(() => false)),
      { drafts: 'decline' },
    );
    expect(reached, 'the run never reached a second battle').toBe(true);
    const before = await readLadderPin(page);
    expect(before).toBeGreaterThanOrEqual(1);

    await page.reload();
    await expect(fightButton(page)).toBeEnabled({ timeout: 20_000 });
    expect(await readLadderPin(page)).toBe(before);
  });
});

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
