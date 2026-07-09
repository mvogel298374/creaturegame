import { expect, type Page, type Locator } from '@playwright/test';

/**
 * Page-object helpers for the battle flow. Centralises selectors and the multi-step
 * navigation so specs read as intent ("start a battle", "choose a move") rather than
 * a pile of clicks. Selectors lean on stable semantic classes already in the app
 * (.btn-new-game, .species-card, .move-btn, .log-line, .bar-fill, .nameplate--*).
 */

export type BridgeEvent = { name: string; t: number };

/**
 * Title → starter select → confirm → battle, returning once it's the player's turn.
 *
 * Pass a `seed` to pin a **fully deterministic** run: the backend threads one seed through every
 * nondeterministic step (enemy species/DVs/moves, the biome offer, every battle roll, the AI's choices), so a
 * seeded run replays identically. This is the lever a spec uses to stop depending on coin-flip battle outcomes.
 * react-router drops the query string on nav from the title, so a seeded run lands **directly on /select** (the
 * `reward-drop.spec` pattern) — the level slider lives on that screen, so a custom `level` still works there.
 */
export async function startBattle(
  page: Page,
  species = 'CHARIZARD',
  level?: number,
  seed?: number
): Promise<void> {
  // ?e2e=1 puts the app in test mode (bridge recording + collapsed animation delays).
  if (seed !== undefined) {
    await page.goto(`/select?e2e=1&seed=${seed}`);
    await page.locator('.species-card').first().waitFor({ state: 'visible', timeout: 10_000 });
  } else {
    await page.goto('/?e2e=1');
    await page.locator('.btn-new-game').click();
  }

  // The level slider is on the select screen, which both entry paths reach.
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

  // Biome mode (Phase 3b-2): the run opens on the route-choice modal — pick the first offered biome — before
  // the first battle. It arrives a beat after CONFIRM (connect + emit), so wait for it. Then the entry
  // animation plays and the action menu enables for the first turn — unless the first node is a reward node
  // (Treasure/Mystery), whose choice modal blocks first, so clear that before waiting on the fight menu.
  await page.locator('.biome-card').first().click({ timeout: 15_000 });
  await expect(async () => {
    await leaveShopIfPresent(page);
    await dismissRewardChoiceIfPresent(page);
    expect(await fightButton(page).isEnabled().catch(() => false)).toBe(true);
  }).toPass({ timeout: 20_000 });
}

/** Answers a route-choice modal if one is up (picks the first offered biome). Returns whether it acted.
 * The run opens on one, and one follows each Poké Center, so the play loop calls this too. */
export async function chooseBiomeIfPresent(page: Page): Promise<boolean> {
  const firstCard = page.locator('.biome-card').first();
  if (await firstCard.isVisible().catch(() => false)) {
    await firstCard.click();
    return true;
  }
  return false;
}

/** Leaves a Shop node's buy modal if one is up (a shop is a blocking between-node modal, like the reward/biome
 * choice). A shop-first node opens with an empty wallet, so there's nothing to buy — the play loop and
 * startBattle just leave to keep the run flowing (shop *purchasing* is covered by shop.spec + unit tests).
 * Returns whether it acted. */
export async function leaveShopIfPresent(page: Page): Promise<boolean> {
  const leave = page.locator('.shop-leave-btn');
  if (await leave.isVisible().catch(() => false)) {
    await leave.click();
    return true;
  }
  return false;
}

/** Answers a reward-choice modal if one is up by taking the gold bag (always offered, so a deterministic
 * pick). Every rolled reward — a battle win, a Treasure/Mystery node — now blocks on this pick-one-of-N until
 * answered, so the play loop and startBattle both clear it to keep the run flowing. Returns whether it acted. */
export async function dismissRewardChoiceIfPresent(page: Page): Promise<boolean> {
  const goldCard = page.locator('.reward-card--gold').first();
  if (await goldCard.isVisible().catch(() => false)) {
    await goldCard.click();
    return true;
  }
  return false;
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
    // Between-node blocking modals must be cleared to keep the chain flowing: leave a Shop (nothing to buy in
    // an auto-played run) and take the gold bag at a reward node (battle win / Treasure / Mystery).
    await leaveShopIfPresent(page);
    await dismissRewardChoiceIfPresent(page);
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
 * Attack each turn until the "grew to level N!" line appears, then stop — for the level-up specs, run on a
 * fixed seed via `startBattle(…, seed)` so a low-level start reliably wins into a level-up. Unlike the generic
 * play loop this does NOT dismiss the post-win reward-choice modal: the level-up specs assert the level-up
 * panel + reward-modal interaction themselves, so we leave the modal standing where they expect it.
 */
export async function playToLevelUp(page: Page, maxTurns = 60): Promise<string[]> {
  const grew = (log: string[]) => log.some(l => /grew to level \d+!/.test(l));
  for (let i = 0; i < maxTurns; i++) {
    if (grew(await logLines(page))) break;
    // Leave a Shop node if one blocks before the level-up win (nothing to buy in an auto-played run). The
    // post-win reward modal is deliberately NOT dismissed here — the level-up specs assert it themselves.
    await leaveShopIfPresent(page);
    if (await fightButton(page).isEnabled().catch(() => false)) {
      // A lethal hit ends the turn mid-choice and disables the button; swallow and let the loop re-check.
      await chooseMove(page).catch(() => {});
    }
    await page.waitForTimeout(150);
  }
  return logLines(page);
}

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
