import { test, expect, type Page } from '@playwright/test';
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
 * Voluntary in-battle switching (In-Combat Switching, Stage C), end-to-end: a first-class SWITCH turn-action lets
 * the player swap the active creature for a benched one mid-fight, at the cost of the turn — reusing Stage 3's
 * CreatureSwitchedIn send-in (the "Go! X!" narration + nameplate retarget) that the forced faint-switch already
 * proved.
 *
 * Reaching a *switchable* turn needs a themed draft (cadence × roll) to grow the party past one, then any live
 * turn while the lead is up — so the SWITCH action button is enabled (its server-computed `canSwitch`). Like the
 * forced-switch spec, a lone pinned seed isn't reliable under load (the client's click timing shifts the server
 * stream), so we walk a list of seeds and keep the first run that reaches an enabled SWITCH.
 */
const SEEDS = [1, 2, 3, 4, 5, 6, 7, 8];

const switchButton = (page: Page) => page.getByRole('button', { name: 'SWITCH', exact: true });

/** Plays a seeded run (accepting any draft) until the SWITCH action button is enabled, the run ends, or we run
 * out of turns. Returns whether a switchable turn was reached. */
async function playUntilCanSwitch(page: Page, seed: number): Promise<boolean> {
  // Start well above the early-node curve. The draft that grows the party is several battles deep, and a level-5
  // lone starter frequently wipes before reaching one (an Elite's Vaporeon ended every one of eight seeded runs on
  // a standalone pass), which burns the whole seed list on runs that never get to the state under test. A higher
  // lead survives the early nodes, so what the seeds actually vary is the draft cadence — the thing we're waiting
  // on — not whether the run lives. The switch itself is unaffected by the level.
  await startBattle(page, 'CHARIZARD', 30, seed);

  for (let i = 0; i < 400; i++) {
    // The moment SWITCH is enabled we're on a switchable turn (party > 1, lead alive, not trapped/locked).
    if (await switchButton(page).isEnabled().catch(() => false)) return true;

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
  return switchButton(page).isEnabled().catch(() => false);
}

test('the SWITCH action swaps the active creature mid-battle and the fight continues', async ({ page }) => {
  test.setTimeout(5 * 60_000);

  let reached = false;
  for (const seed of SEEDS) {
    if (await playUntilCanSwitch(page, seed)) {
      reached = true;
      break;
    }
    // That run wiped (or never drafted) before a switchable turn — try the next seeded run.
  }
  expect(reached, `no seeded run reached a switchable turn in ${SEEDS.length} attempts`).toBe(true);

  // Open the dismissable picker (a control-view, not a blocking modal). The active lead and any fainted member
  // are greyed & disabled; a live benched member is selectable.
  await switchButton(page).click();
  const grid = page.locator('.battle-controls .lead-grid');
  await expect(grid).toBeVisible();

  const benched = grid.locator('.lead-card:not([disabled])').first();
  await expect(benched).toBeEnabled();
  const incoming = (await benched.locator('.lead-card-name').textContent())?.trim() ?? '';
  expect(incoming).not.toEqual('');

  await benched.click();

  // The send-in is narrated (reusing Stage 3's CreatureSwitchedIn) and the player nameplate retargets onto the
  // incoming creature.
  await expect(
    page.locator('.log-line').filter({ hasText: new RegExp(`Go! ${incoming}!`, 'i') }).first()
  ).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.nameplate--player .nameplate-name')).toHaveText(
    new RegExp(`^${incoming}$`, 'i')
  );

  // Switching costs the turn, so the creature coming in EATS the enemy's move that turn (Gen 1's price for a
  // switch — Battle retargets the enemy's already-queued attack onto the switch-in on purpose). Against a boss
  // that can mean an outright KO on a low-level bench member, which correctly raises the forced send-in modal
  // instead of returning the menu. That's the fight continuing, not a failure — so answer it and carry on. (The
  // spec used to assert FIGHT straight after the switch and died exactly here: a Lv6 STARMIE switched into
  // Misty's Lv13 EXEGGUTOR ate a critical HYPER BEAM for 95.)
  const forcedSwitchIn = page.locator('.lead-modal[aria-label="Send in a creature"]');
  const controlIsBack = () => fightButton(page).isEnabled().catch(() => false);
  const mustSendIn = () => forcedSwitchIn.isVisible().catch(() => false);

  // The enemy's move resolves over a couple of seconds *after* the send-in narration, so we have to wait for
  // whichever outcome lands first rather than probing once — `isVisible()` is an instantaneous check, not a
  // waiting one, and a single call here fires long before the KO does. Loop because a send-in can chain.
  for (let i = 0; i < 3; i++) {
    await expect
      .poll(async () => (await controlIsBack()) || (await mustSendIn()), { timeout: 30_000 })
      .toBe(true);
    if (!(await mustSendIn())) break;
    await forcedSwitchIn.locator('.lead-card:not([disabled])').first().click();
  }

  // Either way the player gets control back with the run still alive.
  await expect(fightButton(page)).toBeEnabled({ timeout: 20_000 });
  expect((await logLines(page)).some(l => /Run over/.test(l))).toBe(false);
});
