import { expect, type Page, type Locator } from '@playwright/test';

/**
 * Page-object helpers for the battle flow. Centralises selectors and the multi-step
 * navigation so specs read as intent ("start a battle", "choose a move") rather than
 * a pile of clicks. Selectors lean on stable semantic classes already in the app
 * (.btn-new-game, .species-card, .move-btn, .log-line, .bar-fill, .nameplate--*).
 */

export type BridgeEvent = { name: string; t: number };

/** Title → starter select → confirm → battle, returning once it's the player's turn. */
export async function startBattle(page: Page, species = 'CHARIZARD'): Promise<void> {
  // ?e2e=1 puts the app in test mode (bridge recording + collapsed animation delays).
  await page.goto('/?e2e=1');
  await page.locator('.btn-new-game').click();

  const card = page.locator('.species-card', { hasText: species });
  await expect(card).toBeVisible();
  await card.click();
  await page.getByRole('button', { name: /CONFIRM/i }).click();

  // Entry animation plays, then the action menu enables for the first turn.
  await expect(fightButton(page)).toBeEnabled({ timeout: 15_000 });
}

export const fightButton = (page: Page): Locator =>
  page.getByRole('button', { name: /^FIGHT/i });

/** Opens the FIGHT grid and picks a move (by name, or the first available). */
export async function chooseMove(page: Page, moveName?: string): Promise<string> {
  await fightButton(page).click();
  const move = moveName
    ? page.locator('.move-btn', { hasText: moveName })
    : page.locator('.move-btn:not([disabled])').first();
  await expect(move).toBeEnabled();
  const label = (await move.locator('.move-name').textContent())?.trim() ?? '';
  await move.click();
  return label;
}

export const logLines = (page: Page): Promise<string[]> =>
  page.locator('.log-line').allTextContents().then(xs => xs.map(s => s.trim()));

export const lastLog = async (page: Page): Promise<string> =>
  (await logLines(page)).at(-1) ?? '';

/** HP-bar fill width (e.g. "84.375%") for the named side. */
export const hpWidth = (page: Page, side: 'player' | 'enemy'): Promise<string> =>
  page.locator(`.nameplate--${side} .bar-fill`).evaluate(el => (el as HTMLElement).style.width);

/** Recorded Phaser bridge events (name + ms timestamp), in emit order. */
export const bridgeEvents = (page: Page): Promise<BridgeEvent[]> =>
  page.evaluate(() => (window as unknown as { __cgEvents?: BridgeEvent[] }).__cgEvents ?? []);

/** Waits until a log line matching the pattern appears. */
export async function waitForLog(page: Page, re: RegExp, timeout = 15_000): Promise<void> {
  await expect(page.locator('.log-line').filter({ hasText: re }).first()).toBeVisible({ timeout });
}

type BridgeEventWindow = { __cgEvents?: BridgeEvent[] };

/** Plays the battle to its end by attacking each turn; returns the final log. */
export async function playToEnd(page: Page, maxTurns = 50): Promise<string[]> {
  for (let i = 0; i < maxTurns; i++) {
    if ((await logLines(page)).some(l => /wins!$/.test(l))) break;
    if (await fightButton(page).isEnabled().catch(() => false)) {
      // The battle can end mid-choice (the move we just picked is lethal); a click
      // on the now-disabled FIGHT/move then fails — swallow it and let the next
      // iteration see the "wins!" line and break.
      await chooseMove(page).catch(() => {});
    }
    await page.waitForTimeout(150);
  }
  return logLines(page);
}

export { BridgeEventWindow };
