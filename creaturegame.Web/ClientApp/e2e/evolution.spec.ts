import { test, expect, type Page } from '@playwright/test';
import { fightButton, isShowing, logLines, playCurrentRunUntil, waitForLog, walkSeedsUntil } from './helpers';

/**
 * The evolution offer, end-to-end: a blocking between-encounter Allow / Cancel prompt (Gen 1's B-cancel is a
 * real answer here, not a dismissal). One of the "other between-encounter modal E2Es" the seed plumbing
 * unblocked — until now the whole path was covered only at the .NET/Vitest layer.
 *
 * **Choosing the reach took measurement, and both obvious heuristics are wrong.**
 *  - *"Fewest levels to climb"* — CATERPIE @ L5 (evolves at 7; with WEEDLE the earliest in Gen 1) has 21 max HP
 *    at level 6, and every seeded run died on the biome's Elite before the seventh level.
 *  - *"Highest BST survives"* (the lever `poke-center.spec.ts` correctly uses for a whole-biome reach) —
 *    DRAGONAIR @ L54, the sturdiest level-up evolver in the game, went 4 wins deep and **gained no level at
 *    all** before a boss killed it: `Run over — 4 wins, reached level 54`. XP required per level grows
 *    cubically with level while XP *earned* only grows linearly with the (level-matched) enemy, so a
 *    high-level lead effectively never levels — and no level-up means no evolution check.
 *
 * The reach has to satisfy *both* at once, which puts it low: **CHARMANDER @ L15**, evolving at 16. One level
 * away, at a level cheap enough to actually cross, on the best BST (259) available that early.
 */
const FROM = 'CHARMANDER';
const TO = 'CHARMELEON';

const evolutionModal = (page: Page) => page.locator('.recovery-modal[aria-label="Evolution"]');

const offerIsUp = (page: Page) => isShowing(evolutionModal(page));

/**
 * Walks seeds until one run is blocked on the evolution offer.
 *
 * `seeds` is passed in **disjoint per test** on purpose. These two tests need one reach each and cannot share
 * one (answering consumes the offer), so they are the two-identical-walks shape that has already misfired in
 * this suite — the second walk exhausting every seed the first had just succeeded on. Handing them different
 * seeds means the second test is not re-running the first's runs under a backend that has since accumulated
 * abandoned ones; it gets its own fresh set.
 *
 * (Merging both answers into one run *was* tried, since Gen 1's B-cancel re-offers at the next level-up and
 * that would have made the re-offer itself assertable. It is not reachable in practice: it needs two level-ups
 * in a single run, the second while carrying the form you just declined to upgrade. Across 24 seeded runs — 12
 * on CHARMANDER, 12 on DRAGONAIR — not one got there; the cancelled run kept dying to the biome Boss that came
 * next, which is the cost of cancelling working as designed, not a defect. Re-offer stays covered at the
 * .NET layer.)
 */
async function reachOffer(page: Page, seeds: number[]): Promise<void> {
  // drafts: 'decline' keeps the party at one. Every creature that levels is eligible to evolve, so a drafted
  // second creature could raise this modal instead — and the identity assertions would then fail on a prompt
  // that is itself perfectly correct.
  const seed = await walkSeedsUntil(page, offerIsUp, {
    species: FROM,
    level: 15,
    drafts: 'decline',
    seeds,
  });
  expect(seed, 'no seeded run reached the evolution offer').not.toBeNull();

  await expect(evolutionModal(page)).toBeVisible();
  await expect(evolutionModal(page).locator('.recovery-sub')).toHaveText(
    new RegExp(`${FROM} is evolving into ${TO}!`, 'i')
  );
}

/**
 * The offer is answered between encounters, right after a win — so what follows is the intermission (reward
 * choice → next node), not another turn. Reaching the *next interactive state* is what proves the run didn't
 * stall on the server-side await the modal was parked on.
 *
 * "Next interactive state" is deliberately either a playable battle **or** the run ending, because those are
 * both the run continuing. Requiring a playable battle alone asserts that the player goes on to *win*, which
 * is not this spec's business and is not in the spec's control: it failed exactly there, having answered ALLOW,
 * evolved, and played on into the biome Boss — `CHARMELEON VS Trainer Weevil's SCYTHER` — where a critical SKY
 * ATTACK ended the run. A whole further battle is proof of flow; losing it is the roguelite working.
 */
async function expectRunFlowsOn(page: Page): Promise<void> {
  const progressed = await playCurrentRunUntil(
    page,
    async p =>
      (await fightButton(p).isEnabled().catch(() => false)) ||
      (await logLines(p)).some(l => /Run over/.test(l)),
    { drafts: 'decline' }
  );
  expect(progressed, 'the run stalled after the evolution prompt was answered').toBe(true);
}

test.describe('Evolution offer', () => {
  test('CANCEL keeps the current form and the run continues', async ({ page }) => {
    test.setTimeout(6 * 60_000);
    await reachOffer(page, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

    // CANCEL is the non-primary button — the primary (.action-btn--fight) is ALLOW.
    await evolutionModal(page).getByRole('button', { name: 'CANCEL', exact: true }).click();

    await expect(evolutionModal(page)).toBeHidden();
    await waitForLog(page, new RegExp(`${FROM} stopped evolving\\.`, 'i'));
    expect((await logLines(page)).some(l => /evolved into/i.test(l))).toBe(false);
    // Declining is not a dismissal — the creature keeps its current form and stays the active creature.
    await expect(page.locator('.nameplate--player .nameplate-name')).toHaveText(
      new RegExp(`^${FROM}$`, 'i')
    );

    await expectRunFlowsOn(page);
  });

  test('ALLOW morphs the creature and the run continues', async ({ page }) => {
    test.setTimeout(6 * 60_000);
    await reachOffer(page, [11, 12, 13, 14, 15, 16, 17, 18, 19, 20]);

    await evolutionModal(page).getByRole('button', { name: 'ALLOW', exact: true }).click();

    await expect(evolutionModal(page)).toBeHidden();
    await waitForLog(page, new RegExp(`${FROM} evolved into ${TO}!`, 'i'));

    // Let the intermission finish BEFORE reading the nameplate. The morph is narrated as soon as it resolves,
    // but the nameplate and the "What will X do?" prompt only catch up on the next state push — and another
    // between-encounter prompt can land in between (an acquisition offer for the creature just beaten), which
    // holds that push back. Asserting the rename right after the log line therefore races a blocking modal:
    // it failed with the log already reading "CHARMANDER evolved into CHARMELEON!" while a BEEDRILL draft sat
    // on top and the menu still said CHARMANDER. Reading it once the run is playable again asserts the same
    // thing — the evolved creature IS the active creature — without the race.
    await expectRunFlowsOn(page);
    await expect(page.locator('.nameplate--player .nameplate-name')).toHaveText(
      new RegExp(`^${TO}$`, 'i')
    );
  });
});
