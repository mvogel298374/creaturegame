import { test, expect, type Page } from '@playwright/test';
import {
  startBattle, attackUntilShowing, answerRunPromptIfPresent, fightButton, playCurrentRunUntil, logLines, chooseMove,
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

// useBattleHub's E2E seam (R1d-f): a server-side abort, finished reconnects, and every event type received.
type HubSeam = { __cgHub?: { reconnects: number; events: string[]; drop: () => void } };

const readSeam = (page: Page) =>
  page.evaluate(() => {
    const seam = (window as unknown as HubSeam).__cgHub!;
    return { reconnects: seam.reconnects, events: [...seam.events] };
  });

// Drops the connection server-side and waits for the reconnect replay through its last event; returns the replay.
async function blip(page: Page, lastReplayed: string): Promise<string[]> {
  const before = await readSeam(page);
  await page.evaluate(() => (window as unknown as HubSeam).__cgHub!.drop());
  let replayed: string[] = [];
  await expect(async () => {
    const now = await readSeam(page);
    expect(now.reconnects).toBeGreaterThan(before.reconnects);
    replayed = now.events.slice(before.events.length);
    expect(replayed).toContain(lastReplayed);
  }).toPass({ timeout: 15_000 });
  return replayed;
}

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

test.describe('A blip changes nothing on screen (R1d-f)', () => {
  test('a blip mid-battle re-announces nothing, keeps the pin, and the move menu stays live', async ({ page }) => {
    test.setTimeout(150_000);
    await startBattle(page, 'CHARIZARD', 50, 1);
    const reached = await playCurrentRunUntil(
      page,
      async p => (await battlesStarted(p)) >= 2 && (await fightButton(p).isEnabled().catch(() => false)),
      { drafts: 'decline' },
    );
    expect(reached, 'the run never reached a second battle').toBe(true);
    const pin = await readLadderPin(page);
    const logBefore = (await logLines(page)).length;

    const replayed = await blip(page, 'TurnStarted');
    expect(replayed, 'the server replayed the battle').toContain('BattleStarted');

    await chooseMove(page);
    await expect.poll(async () => (await logLines(page)).slice(logBefore).some(l => / used /.test(l)), {
      timeout: 15_000,
    }).toBe(true);
    await expect(fightButton(page)).toBeEnabled({ timeout: 15_000 });
    // Replayed steps queue ahead of the turn's, so anything a replay re-announced lands before the first move line.
    const sinceBlip = (await logLines(page)).slice(logBefore);
    const beforeTheMove = sinceBlip.slice(0, sinceBlip.findIndex(l => / used /.test(l)));
    expect(beforeTheMove.filter(l => / VS |^Entered /.test(l))).toEqual([]);
    // A faint would move the pin legitimately; fail loudly rather than skip the pin check.
    expect(sinceBlip.filter(l => /fainted!/.test(l)), 'the checked turn must not end the battle').toEqual([]);
    expect(await readLadderPin(page)).toBe(pin);
  });

  test('a blip with the reward choice open keeps it up and answerable', async ({ page }) => {
    test.setTimeout(150_000);
    const modal = page.locator('.reward-modal');
    let open = false;
    for (const seed of SEEDS) {
      await startBattle(page, 'CHARIZARD', 50, seed);
      if ((open = await attackUntilShowing(page, modal))) break;
    }
    expect(open, 'no seeded first battle popped the reward modal').toBe(true);

    const replayed = await blip(page, 'RewardChoiceOffered');

    expect(replayed, 'a finished battle is never replayed').not.toContain('BattleStarted');
    await expect(modal).toBeVisible();
    await modal.locator('.reward-card--gold').click();
    await expect(modal).toHaveCount(0);
    await reachTheNextTurn(page);
  });
});
