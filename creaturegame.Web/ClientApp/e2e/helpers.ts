import { expect, type Page, type Locator } from '@playwright/test';

/**
 * Page-object helpers for the battle flow. Centralises selectors and the multi-step
 * navigation so specs read as intent ("start a battle", "choose a move") rather than
 * a pile of clicks. Selectors lean on stable semantic classes already in the app
 * (.btn-new-game, .species-card, .move-btn, .log-line, .bar-fill, .nameplate--*).
 */

export type BridgeEvent = { name: string; t: number };

/** Title → starter select → confirm → battle, returning once it's the player's turn. */
export async function startBattle(page: Page, species = 'CHARIZARD', level?: number): Promise<void> {
  // ?e2e=1 puts the app in test mode (bridge recording + collapsed animation delays).
  await page.goto('/?e2e=1');
  await page.locator('.btn-new-game').click();

  if (level !== undefined) await setStartLevel(page, level);

  // Match the card by its EXACT name (the .card-name element) so a prefix like MEW doesn't also grab
  // MEWTWO (a strict-mode violation). The search box narrows the grid first.
  await page.locator('.select-search').fill(species);
  const card = page.locator('.species-card', {
    has: page.locator('.card-name', { hasText: new RegExp(`^${species}$`, 'i') }),
  });
  await expect(card).toBeVisible();
  await card.click();
  await page.getByRole('button', { name: /CONFIRM/i }).click();

  // Entry animation plays, then the action menu enables for the first turn.
  await expect(fightButton(page)).toBeEnabled({ timeout: 15_000 });
}

/** Set the starter level slider (defaults to 50 if left untouched). Driven by keyboard so the React
 * controlled input actually updates — Home jumps to the min (5), then ArrowRight steps up. */
async function setStartLevel(page: Page, level: number): Promise<void> {
  const slider = page.locator('input[type="range"]');
  await slider.focus();
  await slider.press('Home'); // → min (5)
  for (let i = 5; i < level; i++) await slider.press('ArrowRight');
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

/** HP-bar fill width (e.g. "84.375%") for the named side. Scoped to the HP row because the player
 * nameplate also has an XP bar that shares the .bar-fill class. */
export const hpWidth = (page: Page, side: 'player' | 'enemy'): Promise<string> =>
  page.locator(`.nameplate--${side} .hp-row .bar-fill`).evaluate(el => (el as HTMLElement).style.width);

/** Player XP-bar fill width. */
export const xpWidth = (page: Page): Promise<string> =>
  page.locator('.nameplate--player .bar-fill--xp').evaluate(el => (el as HTMLElement).style.width);

/** Recorded Phaser bridge events (name + ms timestamp), in emit order. */
export const bridgeEvents = (page: Page): Promise<BridgeEvent[]> =>
  page.evaluate(() => (window as unknown as { __cgEvents?: BridgeEvent[] }).__cgEvents ?? []);

/** Waits until a log line matching the pattern appears. */
export async function waitForLog(page: Page, re: RegExp, timeout = 15_000): Promise<void> {
  await expect(page.locator('.log-line').filter({ hasText: re }).first()).toBeVisible({ timeout });
}

type BridgeEventWindow = { __cgEvents?: BridgeEvent[] };

/**
 * Attack each turn (first available move) until `done(log)` holds or maxTurns elapses; returns the log.
 * The endless chain has no terminal "wins!" — a battle win is an intermission — so callers stop on the
 * chain's own lines ("A new challenger approaches!" / "Run over").
 */
async function attackUntil(
  page: Page,
  done: (log: string[]) => boolean,
  maxTurns: number
): Promise<string[]> {
  for (let i = 0; i < maxTurns; i++) {
    if (done(await logLines(page))) break;
    if (await fightButton(page).isEnabled().catch(() => false)) {
      // A turn/battle can end mid-choice (the move we picked is lethal); the click on the
      // now-disabled button then fails — swallow it and let the next iteration re-check.
      await chooseMove(page).catch(() => {});
    }
    await page.waitForTimeout(150);
  }
  return logLines(page);
}

/** Attack until a log line matches `re` (or maxTurns elapses). */
export const attackUntilLog = (page: Page, re: RegExp, maxTurns = 80): Promise<string[]> =>
  attackUntil(page, log => log.some(l => re.test(l)), maxTurns);

/** Play one battle to a win — the chain's "A new challenger approaches!" intermission — or to the run's
 * end if the player faints first. */
export const playToNextEncounter = (page: Page, maxTurns = 80): Promise<string[]> =>
  attackUntil(
    page,
    log => log.some(l => /A new challenger approaches!/.test(l) || /Run over/.test(l)),
    maxTurns
  );

/** Play battle after battle until the player faints and the run ends. Caller should raise the test
 * timeout — a natural run-to-loss can span several battles. */
export const playToRunEnd = (page: Page, maxTurns = 300): Promise<string[]> =>
  attackUntilLog(page, /Run over/, maxTurns);

/**
 * Start fresh runs until one reaches a log line matching `re`, returning that run's log. Wild enemies are
 * BST- and level-matched, so a single battle is roughly a coin-flip — a win-dependent target (an
 * intermission, a level-up) can be lost on the way. We just reload and try another run (`startBattle`'s
 * `page.goto` resets the SPA + the recorded bridge events, so the returned run is clean). Each run plays
 * until `re` appears OR the run ends ("Run over"); a run that ends without the target triggers a restart.
 * Throws after `attempts` runs so a genuine regression fails loudly instead of hanging.
 */
export async function reachLog(
  page: Page,
  re: RegExp,
  opts: { species?: string; level?: number; attempts?: number } = {}
): Promise<string[]> {
  const { species = 'CHARIZARD', level = 5, attempts = 8 } = opts;
  for (let i = 0; i < attempts; i++) {
    await startBattle(page, species, level);
    const log = await attackUntil(
      page,
      l => l.some(x => re.test(x)) || l.some(x => /Run over/.test(x)),
      200
    );
    if (log.some(x => re.test(x))) return log;
    // Run ended without the target (the player fainted first) — reload and try a fresh run.
  }
  throw new Error(`reachLog: never reached /${re.source}/ in ${attempts} runs`);
}

export { BridgeEventWindow };
