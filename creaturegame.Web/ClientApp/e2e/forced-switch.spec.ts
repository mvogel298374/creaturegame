import { test, expect, type Page, type Locator } from '@playwright/test';
import {
  startBattle,
  fightButton,
  chooseMove,
  logLines,
  leaveShopIfPresent,
  dismissRewardChoiceIfPresent,
  chooseBiomeIfPresent,
} from './helpers';

/**
 * Forced switch-on-faint (Encounter Logic Phase 4 Stage 3), end-to-end: when the active creature faints while a
 * bench member is still alive, the run does NOT end. A forced, non-dismissable modal demands a replacement; the
 * chosen creature enters against the same enemy and the battle continues.
 *
 * Reaching that state needs BOTH a themed draft (cadence × roll) to grow the party past one AND the lead to then
 * faint with that bench member alive — several battles deep into a run. A pinned seed is NOT enough on its own:
 * the seed fixes the *server's* stream, but the client's move sequence is what drives it, and under load the
 * polling loop's clicks land on different turns — which shifts the stream and plays out a different run (a lone
 * pinned seed passed standalone and then lost its run at battle 2 inside the full suite). So we walk a list of
 * seeds and keep the first run that actually reaches the modal — the `reachLog` retry idiom, but seeded so each
 * attempt is cheap and repeatable rather than a fresh coin-flip.
 */
const SEEDS = [1, 2, 3, 4, 5, 6, 7, 8];

const switchInModal = (page: Page): Locator =>
  page.locator('.lead-modal[aria-label="Send in a creature"]');

/** Plays a seeded run (accepting any draft) until the forced switch-in modal blocks it, the run ends, or we run
 * out of turns. Returns whether the modal is up. */
async function playUntilForcedSwitch(page: Page, seed: number): Promise<boolean> {
  await startBattle(page, 'CHARIZARD', 5, seed);
  const modal = switchInModal(page);

  for (let i = 0; i < 400; i++) {
    if (await modal.isVisible().catch(() => false)) return true;

    // Accept the draft when it's offered — the only way the party grows past one.
    const add = page.locator('.acquire-modal .action-btn--fight');
    if (await add.isVisible().catch(() => false)) {
      await add.click().catch(() => {});
      continue;
    }
    // Keep the current lead at a biome boundary so the run keeps flowing.
    const keepLead = page.locator('.lead-modal[aria-label="Choose your lead"] .lead-card--current');
    if (await keepLead.isVisible().catch(() => false)) {
      await keepLead.click().catch(() => {});
      continue;
    }
    await leaveShopIfPresent(page);
    await dismissRewardChoiceIfPresent(page);
    await chooseBiomeIfPresent(page);

    if ((await logLines(page)).some(l => /Run over/.test(l))) return false;
    if (await fightButton(page).isEnabled().catch(() => false)) {
      await chooseMove(page).catch(() => {});
    }
    await page.waitForTimeout(80);
  }
  return modal.isVisible().catch(() => false);
}

test('a lead faint with a live bench forces a send-in and the battle continues', async ({ page }) => {
  test.setTimeout(5 * 60_000);

  let reached = false;
  for (const seed of SEEDS) {
    if (await playUntilForcedSwitch(page, seed)) {
      reached = true;
      break;
    }
    // That run wiped before a draft-then-faint lined up — try the next seeded run.
  }
  expect(reached, `no seeded run reached a forced switch-in in ${SEEDS.length} attempts`).toBe(true);

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
