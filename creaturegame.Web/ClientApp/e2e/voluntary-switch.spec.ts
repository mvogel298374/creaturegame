import { test, expect, type Page } from '@playwright/test';
import { startBattle, fightButton, logLines, walkSeedsUntil } from './helpers';

/**
 * Voluntary in-battle switching (In-Combat Switching, Stage C), end-to-end: a first-class SWITCH turn-action lets
 * the player swap the active creature for a benched one mid-fight, at the cost of the turn — reusing Stage 3's
 * CreatureSwitchedIn send-in (the "Go! X!" narration + nameplate retarget) that the forced faint-switch already
 * proved.
 *
 * Reaching a *switchable* turn needs a themed draft (cadence × roll) to grow the party past one, then any live
 * turn while the lead is up — so the SWITCH action button is enabled (its server-computed `canSwitch`). That's
 * what `walkSeedsUntil` drives; see its doc comment for why a lone pinned seed isn't enough.
 *
 * The gating and the picker's *dismissability* are covered here too, because they're what separate this from
 * Stage 3's forced modal: SWITCH greys out when there's no legal target, and BACK costs nothing.
 */
const switchButton = (page: Page) => page.getByRole('button', { name: 'SWITCH', exact: true });

/** True the moment we're on a switchable turn — party > 1, lead alive, not trapped or locked in. */
const canSwitchNow = (page: Page) => switchButton(page).isEnabled().catch(() => false);

test.describe('In-combat switching', () => {
  test('SWITCH is offered but disabled while the starter is alone', async ({ page }) => {
    // No party growth needed, so no seed walk — the very first turn of any run is the state under test. This is
    // the server-computed TurnStarted.CanSwitch reaching the DOM, which is the whole reason the field is
    // projected: with a party of one there is nobody to switch to.
    await startBattle(page, 'CHARIZARD', 50);

    await expect(fightButton(page)).toBeEnabled();
    await expect(switchButton(page)).toBeVisible();
    await expect(switchButton(page)).toBeDisabled();
    await expect(switchButton(page)).toHaveAttribute('title', "Can't switch right now");
  });

  // Reaching a switchable turn is the expensive part of this feature (a themed draft is several battles deep,
  // gated on cadence × roll), so the picker's *dismissability* and the switch itself share one reach rather than
  // paying for two. They ran as two tests briefly and that was actively worse: the suite is `workers: 1`, so the
  // two walks were sequential and identical on paper — and the second still exhausted all eight seeds where the
  // first had found one. That is the seed-≠-determinism drift this suite already documents, and the fix is not
  // to make the second walk more reliable but to not need it. BACK is a natural prelude to the swap anyway: it
  // must leave the turn untouched, so the switch that follows is the same turn it would otherwise have been.
  test('the SWITCH picker dismisses without spending a turn, then swaps the active creature', async ({ page }) => {
    test.setTimeout(5 * 60_000);

    const seed = await walkSeedsUntil(page, canSwitchNow);
    expect(seed, 'no seeded run reached a switchable turn').not.toBeNull();

    const nameBefore = await page.locator('.nameplate--player .nameplate-name').textContent();
    const logBefore = await logLines(page);

    // Open the dismissable picker (a control-view, not a blocking modal). The active lead and any fainted member
    // are greyed & disabled; a live benched member is selectable.
    await switchButton(page).click();
    const grid = page.locator('.battle-controls .lead-grid');
    await expect(grid).toBeVisible();

    // The creature already out is rendered as its own greyed, unselectable card marked "· OUT". The forced
    // send-in modal has no such card (the outgoing creature there has fainted), so this is the render that
    // distinguishes the two pickers — and the modifier is load-bearing, not cosmetic: without it the card that's
    // already out looks selectable and still lights on hover.
    const lead = grid.locator('.lead-card--current');
    await expect(lead).toBeVisible();
    await expect(lead).toBeDisabled();
    await expect(lead).toContainText('· OUT');

    // BACK is the whole point of a dismissable control-view: no server prompt is parked on it, so leaving costs
    // nothing. A regression to the forced modal's `dismiss="blocking"` would strand the turn here.
    await page.locator('.battle-controls .action-back').click();
    await expect(grid).toBeHidden();
    await expect(fightButton(page)).toBeEnabled();
    expect(await logLines(page)).toEqual(logBefore);
    await expect(page.locator('.nameplate--player .nameplate-name')).toHaveText(nameBefore ?? '');

    // Same turn, second opening — now go through with it.
    await switchButton(page).click();
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
});
