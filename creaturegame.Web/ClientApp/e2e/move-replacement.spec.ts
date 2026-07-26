import { test, expect, type Page } from '@playwright/test';
import {
  chooseBiomeIfPresent,
  dismissRewardChoiceIfPresent,
  fightButton,
  isShowing,
  leaveShopIfPresent,
  logLines,
  waitForLog,
  walkSeedsUntil,
} from './helpers';

/**
 * Level-up move replacement, end-to-end: four slots are full and a fifth move is learned, so the run blocks on a
 * "forget which one?" prompt with a two-step confirm — or a decline, which is also an answer.
 *
 * `learnset.spec.ts` covers the *auto*-learn path (free slot, no prompt) and its comment recorded this modal as
 * "not reliably reachable without the seed, so it stays covered at the .NET/Vitest layer until then". The seed
 * plumbing landed, so this closes it.
 *
 * VICTREEBEL is the reach: its learnset gives four moves at level 1 and a fifth at level 13, so starting at 12
 * puts the prompt one level-up away. (It's one of only four species in the Gen 1 data whose fifth level-up move
 * lands below level 16 — see PokemonLearnset.) Its BST 490 means foes are matched strong, so a win pays well and
 * the 12→13 crossing needs few of them.
 */
const replaceModal = (page: Page) => page.locator('.move-replace-modal');

type Offer = { newMove: string; currentMoves: string[] };

/** Plays seeded runs until the move-replacement prompt blocks one. */
async function reachReplacementOffer(page: Page): Promise<Offer> {
  // drafts: 'decline' keeps the party at one. Every creature that levels is eligible for this prompt, so a
  // drafted second creature could raise it instead — and then the VICTREEBEL identity assertions below would
  // fail on a modal that is itself perfectly correct.
  const seed = await walkSeedsUntil(page, p => isShowing(replaceModal(p)), {
    species: 'VICTREEBEL',
    level: 12,
    drafts: 'decline',
  });
  expect(seed, 'no seeded run reached the move-replacement prompt').not.toBeNull();

  const modal = replaceModal(page);
  await expect(modal).toBeVisible();

  const title = (await modal.locator('.move-replace-title').textContent())?.trim() ?? '';
  expect(title).toMatch(/^VICTREEBEL wants to learn .+!$/i);
  const newMove = title.replace(/^VICTREEBEL wants to learn /i, '').replace(/!$/, '').trim();

  // The prompt only exists because all four slots are full.
  const slots = modal.locator('.move-replace-grid .move-btn');
  await expect(slots).toHaveCount(4);
  const currentMoves = (await slots.locator('.move-name').allTextContents()).map(s => s.trim());

  return { newMove, currentMoves };
}

/**
 * The prompt is raised by a level-up, and a level-up is paid out **on a win** — so what follows the answer is
 * not another turn, it is the between-encounter flow (reward choice → next node). Asserting FIGHT re-enables
 * straight away is therefore wrong, and failed exactly that way: the reward modal ("Choose your reward") was up
 * and FIGHT was correctly `action-btn--waiting`. Clearing the intermission and arriving at the next battle is
 * what actually proves the run didn't stall on the await the modal was parked on.
 */
async function expectRunFlowsOn(page: Page): Promise<void> {
  await expect
    .poll(
      async () => {
        await leaveShopIfPresent(page);
        await dismissRewardChoiceIfPresent(page);
        await chooseBiomeIfPresent(page);
        return fightButton(page).isEnabled().catch(() => false);
      },
      { timeout: 90_000 }
    )
    .toBe(true);
  expect((await logLines(page)).some(l => /Run over/.test(l))).toBe(false);
}

/** The server's own view of the moveset (GET /api/game/{id}/player), read through the CHECK POKEMON panel —
 * so the assertion is against persisted state, not the modal we just clicked. Called once the run is back in a
 * battle, both because the panel is a battle-screen control and because reading it a whole encounter later is
 * a stronger claim: the moveset persisted, it wasn't just rendered. */
async function movesFromOverview(page: Page): Promise<string[]> {
  await page.getByRole('button', { name: /CHECK POKEMON/i }).click();
  await page.locator('.overview-tab', { hasText: 'MOVES' }).click();
  await expect(page.locator('.overview-move').first()).toBeVisible();
  const names = (await page.locator('.overview-move-name').allTextContents()).map(s => s.trim());
  await page.getByRole('button', { name: /BACK/i }).first().click();
  return names;
}

test.describe('Level-up move replacement', () => {
  test('declining keeps the original four moves', async ({ page }) => {
    test.setTimeout(5 * 60_000);
    const { newMove, currentMoves } = await reachReplacementOffer(page);

    // Two steps on purpose: the "Don't learn X" press only raises the confirm — nothing is decided yet.
    await replaceModal(page).locator('.action-back').click();
    await expect(replaceModal(page).locator('.move-replace-question')).toHaveText(
      new RegExp(`Stop learning ${newMove}\\?`, 'i')
    );
    await replaceModal(page).getByRole('button', { name: 'YES', exact: true }).click();

    await expect(replaceModal(page)).toBeHidden();
    await waitForLog(page, new RegExp(`VICTREEBEL did not learn ${newMove}\\.`, 'i'));
    await expectRunFlowsOn(page);

    const after = await movesFromOverview(page);
    expect(after.sort()).toEqual([...currentMoves].sort());
    expect(after.some(m => m.toUpperCase() === newMove.toUpperCase())).toBe(false);
  });

  test('forgetting a move swaps it for the new one', async ({ page }) => {
    test.setTimeout(5 * 60_000);
    const { newMove, currentMoves } = await reachReplacementOffer(page);
    const forgotten = currentMoves[0];

    await replaceModal(page).locator('.move-replace-grid .move-btn').first().click();
    await expect(replaceModal(page).locator('.move-replace-question')).toHaveText(
      new RegExp(`Forget ${forgotten} and learn ${newMove}\\?`, 'i')
    );
    await replaceModal(page).getByRole('button', { name: 'YES', exact: true }).click();

    await expect(replaceModal(page)).toBeHidden();
    // MoveForgotten is emitted just before its paired MoveLearned — assert both, in that order.
    await waitForLog(page, new RegExp(`VICTREEBEL forgot ${forgotten}!`, 'i'));
    await waitForLog(page, new RegExp(`VICTREEBEL learned ${newMove}!`, 'i'));
    await expectRunFlowsOn(page);

    const after = await movesFromOverview(page);
    expect(after).toHaveLength(4);
    expect(after.some(m => m.toUpperCase() === newMove.toUpperCase())).toBe(true);
    expect(after.some(m => m.toUpperCase() === forgotten.toUpperCase())).toBe(false);
  });
});
