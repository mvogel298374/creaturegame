import { test, expect, type Locator, type Page } from '@playwright/test';
import { answerRunPromptIfPresent, fightButton, startBattle } from './helpers';

const focusIsInside = (page: Page, selector: string) =>
  page.evaluate(sel => document.activeElement?.closest(sel) != null, selector);

const otherOverlay = (page: Page) =>
  page.locator('.modal-overlay').filter({ hasNot: page.locator('.settings-modal') });

// Plays turns with Settings open until `target` renders beneath it; other prompts are answered and play goes on.
async function playWithSettingsOpenUntil(page: Page, target: Locator, maxTurns = 120): Promise<boolean> {
  for (let i = 0; i < maxTurns; i++) {
    if (await answerRunPromptIfPresent(page, { drafts: 'decline' })) continue;
    if (!(await fightButton(page).isEnabled().catch(() => false))) {
      await page.waitForTimeout(80);
      continue;
    }
    await fightButton(page).click();
    await expect(page.locator('.move-btn:not([disabled])').first()).toBeEnabled();
    // One tick, so Settings is guaranteed open before anything the move causes can arrive.
    await page.evaluate(() => {
      document.querySelector<HTMLElement>('.move-btn:not([disabled])')?.click();
      document.querySelector<HTMLElement>('.settings-gear-btn')?.click();
    });
    await expect(page.locator('.settings-modal')).toBeVisible();
    await expect(async () => {
      const prompted = (await otherOverlay(page).count()) > 0;
      expect(prompted || (await fightButton(page).isEnabled())).toBe(true);
    }).toPass({ timeout: 20_000 });
    if ((await target.count()) > 0) return true;
    await page.keyboard.press('Escape');
    await expect(page.locator('.settings-modal')).toHaveCount(0);
  }
  return false;
}

async function expectSettingsWinsThenHandsOver(page: Page, card: string) {
  await expect(page.locator('.modal-overlay').last().locator('.settings-modal')).toBeVisible();
  await expect(page.locator('.modal-overlay').last()).not.toHaveAttribute('inert', '');
  await expect(otherOverlay(page)).toHaveAttribute('inert', '');
  expect(await focusIsInside(page, '.settings-modal')).toBe(true);

  await page.keyboard.press('Tab');
  expect(await focusIsInside(page, '.settings-modal')).toBe(true);

  await page.keyboard.press('Escape');
  await expect(page.locator('.settings-modal')).toHaveCount(0);
  await expect(otherOverlay(page)).not.toHaveAttribute('inert', '');
  await expect.poll(() => focusIsInside(page, card)).toBe(true);
}

test.describe('Modal accessibility', () => {
  test('Tab cycles inside an open modal, wraps instead of reaching QUIT, and focus returns on close', async ({ page }) => {
    await startBattle(page);
    const gear = page.locator('.settings-gear-btn');
    await gear.focus();
    await gear.press('Enter');

    const firstControl = page.locator('.settings-modal input').first();
    const lastControl = page.locator('.settings-modal-close-btn');
    await expect(firstControl).toBeFocused();
    await expect(page.locator('.battle-panel')).toHaveAttribute('inert', '');
    await expect(page.locator('.battle-field')).toHaveAttribute('inert', '');

    await page.keyboard.press('Shift+Tab');
    await expect(lastControl).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(firstControl).toBeFocused();

    for (let i = 0; i < 8; i++) {
      await page.keyboard.press('Tab');
      expect(await focusIsInside(page, '.settings-modal')).toBe(true);
      await expect(page.locator('.action-back')).not.toBeFocused();
    }

    await page.keyboard.press('Escape');
    await expect(page.locator('.settings-modal')).toHaveCount(0);
    await expect(gear).toBeFocused();
    await expect(page.locator('.battle-panel')).not.toHaveAttribute('inert', '');
    await expect(page.locator('.battle-field')).not.toHaveAttribute('inert', '');
  });

  test('a modal with its own initial focus keeps it, and Tab wraps back to it', async ({ page }) => {
    await page.goto('/select?e2e=1&seed=1');
    await page.locator('.species-card').first().click();
    await page.getByRole('button', { name: /CONFIRM/i }).click();

    const input = page.locator('.nickname-input');
    await expect(input).toBeFocused();
    await page.locator('.nickname-modal').getByRole('button', { name: 'CANCEL', exact: true }).focus();
    await page.keyboard.press('Tab');
    await expect(input).toBeFocused();
  });

  test('the battle log announces new lines politely', async ({ page }) => {
    await startBattle(page);
    const log = page.locator('.battle-log');
    await expect(log).toHaveAttribute('role', 'log');
    await expect(log).toHaveAttribute('aria-live', 'polite');
  });

  test('Settings stays on top of a run prompt that arrives after it, then hands focus to the prompt', async ({ page }) => {
    test.setTimeout(120_000);
    await startBattle(page, 'MEWTWO', 100, 1);
    expect(await playWithSettingsOpenUntil(page, otherOverlay(page))).toBe(true);
    await expectSettingsWinsThenHandsOver(page, '.modal-overlay');
  });

  test('Settings stays on top of the game-over overlay', async ({ page }) => {
    test.setTimeout(180_000);
    await startBattle(page, 'CATERPIE', 5, 1);
    expect(await playWithSettingsOpenUntil(page, page.locator('.battle-end-modal'))).toBe(true);
    await expectSettingsWinsThenHandsOver(page, '.battle-end-modal');
  });
});
