import { test, expect, type Page, type Locator } from '@playwright/test';
import { fightButton, isShowing, logLines, walkSeedsUntil } from './helpers';

/**
 * Forced switch-on-faint (Encounter Logic Phase 4 Stage 3), end-to-end: when the active creature faints while a
 * bench member is still alive, the run does NOT end. A forced, non-dismissable modal demands a replacement; the
 * chosen creature enters against the same enemy and the battle continues.
 *
 * Reaching that state needs BOTH a themed draft (cadence × roll) to grow the party past one AND the lead to then
 * faint with that bench member alive — several battles deep into a run. That's what `walkSeedsUntil` drives; see
 * its doc comment for why a lone pinned seed isn't enough. The lead starts at level 5 here **on purpose**: this
 * spec needs it to faint, so the usual "start high enough to survive" default is exactly wrong.
 */
const switchInModal = (page: Page): Locator =>
  page.locator('.lead-modal[aria-label="Send in a creature"]');

test('a lead faint with a live bench forces a send-in and the battle continues', async ({ page }) => {
  test.setTimeout(5 * 60_000);

  const seed = await walkSeedsUntil(page, p => isShowing(switchInModal(p)), { level: 5 });
  expect(seed, 'no seeded run reached a forced switch-in').not.toBeNull();

  const modal = switchInModal(page);
  await expect(modal).toBeVisible();
  await expect(modal.locator('.lead-title')).toContainText(/fainted!/);

  // A roster pick with the fainted member greyed out and unselectable, and a live one selectable.
  const faintedCard = modal.locator('.lead-card--fainted');
  await expect(faintedCard.first()).toBeDisabled();

  const liveCard = modal.locator('.lead-card:not(.lead-card--fainted)').first();
  await expect(liveCard).toBeEnabled();
  const incoming = (await liveCard.locator('.lead-card-name').textContent())?.trim() ?? '';
  expect(incoming).not.toEqual('');

  await liveCard.click();

  // The send-in is narrated, the player nameplate retargets onto the incoming creature, and the run continues:
  // the fight menu comes back (the same enemy is still standing) and the run never ended.
  await expect(modal).toBeHidden();
  await expect(
    page.locator('.log-line').filter({ hasText: new RegExp(`Go! ${incoming}!`, 'i') }).first()
  ).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.nameplate--player .nameplate-name')).toHaveText(
    new RegExp(`^${incoming}$`, 'i')
  );
  await expect(fightButton(page)).toBeEnabled({ timeout: 20_000 });
  expect((await logLines(page)).some(l => /Run over/.test(l))).toBe(false);
});
