import { test, expect, type Page } from '@playwright/test';
import { fightButton, isShowing, logLines, walkSeedsUntil } from './helpers';

/**
 * The acquisition offer (themed draft), end-to-end. Both switching specs *click through* this modal to grow the
 * party — it's the only way a party gets past one — but nothing asserted it, so the offer that gates the whole
 * roster layer was covered only at the .NET/Vitest layer.
 *
 * Two answers, two runs: ADD deposits into the party (the strip appears — it only renders above one member),
 * DECLINE is a sequencing no-op that leaves the party alone and the run flowing. The offer is gated on the draft
 * cadence × a web-policy roll, hence the seed walk.
 */
const acquireModal = (page: Page) => page.locator('.acquire-modal');
const partyChips = (page: Page) => page.locator('.party-strip .party-chip');

/** Plays seeded runs until a themed-draft offer blocks one. Returns the offered creature's name. */
async function reachDraftOffer(page: Page): Promise<string> {
  // drafts: 'leave' — this modal is the target, so the driver must not answer it for us.
  const seed = await walkSeedsUntil(page, p => isShowing(acquireModal(p)), { drafts: 'leave' });
  expect(seed, 'no seeded run reached an acquisition offer').not.toBeNull();

  const modal = acquireModal(page);
  await expect(modal).toBeVisible();
  await expect(modal.locator('.acquire-title')).toHaveText('A creature wants to join!');

  const sub = (await modal.locator('.acquire-sub').textContent())?.trim() ?? '';
  expect(sub).toMatch(/^.+ \(Lv\d+\) wants to join your party!$/);

  // The party strip only renders above one member, so before the first accepted draft there is none.
  await expect(page.locator('.party-strip')).toBeHidden();

  return sub.split(' (Lv')[0];
}

test.describe('Acquisition offer (themed draft)', () => {
  test('ADD deposits the creature into the party', async ({ page }) => {
    test.setTimeout(5 * 60_000);
    const offered = await reachDraftOffer(page);

    await acquireModal(page).getByRole('button', { name: 'ADD', exact: true }).click();

    await expect(acquireModal(page)).toBeHidden();
    // The roster is now two, so the strip appears — with the offered creature on it.
    await expect(partyChips(page)).toHaveCount(2, { timeout: 15_000 });
    // A chip carries its species name in the sprite's alt text and its own tooltip, never as rendered text —
    // visibly it is just a sprite, a level and a LEAD tag. (Asserting on text here read back "Lv31LEADLv21".)
    await expect(page.locator(`.party-strip .party-chip img[alt="${offered}"]`)).toHaveCount(1);
    await expect(
      partyChips(page).filter({ hasText: /Lv\d+/ }).first()
    ).toHaveAttribute('title', /· Lv\d+ · \d+\/\d+ HP$/);

    await expect(fightButton(page)).toBeEnabled({ timeout: 30_000 });
    expect((await logLines(page)).some(l => /Run over/.test(l))).toBe(false);
  });

  test('DECLINE leaves the party alone and the run flows on', async ({ page }) => {
    test.setTimeout(5 * 60_000);
    await reachDraftOffer(page);

    await acquireModal(page).getByRole('button', { name: 'DECLINE', exact: true }).click();

    await expect(acquireModal(page)).toBeHidden();
    // Declining is a sequencing no-op: no deposit, and — the part that matters — the run does not stall on the
    // await the modal was parked on.
    await expect(fightButton(page)).toBeEnabled({ timeout: 30_000 });
    await expect(page.locator('.party-strip')).toBeHidden();
    expect((await logLines(page)).some(l => /Run over/.test(l))).toBe(false);
  });
});
