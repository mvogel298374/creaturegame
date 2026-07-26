import { test, expect, type Page } from '@playwright/test';
import { hpWidth, isShowing, logLines, walkSeedsUntil, waitForLog } from './helpers';

/**
 * The Poké Center recovery step, end-to-end: the blocking Heal / Skip prompt that caps every biome. Both answers
 * are *answers*, not dismissals — the run loop parks a server-side await on this one press — so the thing under
 * test is as much "the run flows on either way" as it is the heal itself.
 *
 * This is the most expensive reach in the suite: the Poké Center sits after a whole biome (a randomised 4–6
 * nodes ending in the Boss), so a run has to win *every* node to get there.
 *
 * MEWTWO is the lead for a specific reason, not for flavour. Enemy strength is self-referential —
 * `EncounterFactory.ScaleTargetBst` is `playerBst + depth×10` — so raising the starting *level* buys nothing;
 * the foe re-scales to match. Raising the starting *BST* does buy something, because the scaling saturates:
 * at MEWTWO's 680 the target runs off the top of the Gen 1 roster and `PickByBst` can only return the closest
 * species it has, which is weaker. A CHARIZARD @ L40 died on node 4 of 6 to a GYARADOS (Fire/Flying into a
 * water-themed biome), and needing ~5 wins in a row turns a per-battle coin flip into a ~3% reach.
 */
const recoveryModal = (page: Page) => page.locator('.recovery-modal[aria-label="Poké Center recovery"]');

/** Plays seeded runs until the Poké Center prompt blocks one. Returns the healing creature's name. */
async function reachPokeCenter(page: Page): Promise<string> {
  const seed = await walkSeedsUntil(page, p => isShowing(recoveryModal(p)), {
    species: 'MEWTWO',
    level: 50,
  });
  expect(seed, 'no seeded run cleared a biome to reach the Poké Center').not.toBeNull();

  const modal = recoveryModal(page);
  await expect(modal).toBeVisible();
  await expect(modal.locator('.recovery-title')).toHaveText('Poké Center');
  await expect(modal.locator('.recovery-sub')).toHaveText(/whole party can be fully healed/i);

  // The offer announces itself in the log, and names the creature both answers will report on.
  await waitForLog(page, /reached a Poké Center!/i);
  const line = (await logLines(page)).find(l => /reached a Poké Center!/i.test(l)) ?? '';
  return line.replace(/ reached a Poké Center!.*/i, '').trim();
}

/** After either answer the biome is over, so the run's next blocking step is the route choice for the next
 * biome — with a lead choice first when the party grew past one. Clearing it proves the run didn't stall on the
 * await the modal was parked on. */
async function expectRunFlowsOn(page: Page): Promise<void> {
  const leadChoice = page.locator('.lead-modal[aria-label="Choose your lead"] .lead-card--current');
  const nextRoute = page.locator('.region-node--offered').first();

  await expect
    .poll(async () => (await isShowing(leadChoice)) || (await isShowing(nextRoute)), { timeout: 45_000 })
    .toBe(true);

  if (await isShowing(leadChoice)) await leadChoice.click();
  await expect(nextRoute).toBeVisible({ timeout: 30_000 });
  expect((await logLines(page)).some(l => /Run over/.test(l))).toBe(false);
}

test.describe('Poké Center recovery', () => {
  test('HEAL fully restores the creature and the run continues into the next biome', async ({ page }) => {
    test.setTimeout(6 * 60_000);
    const healed = await reachPokeCenter(page);

    await recoveryModal(page).getByRole('button', { name: 'HEAL', exact: true }).click();

    await expect(recoveryModal(page)).toBeHidden();
    await waitForLog(page, new RegExp(`${healed} was fully healed!`, 'i'));
    // The heal is a full restore, so the bar reads full whatever the Boss left it at.
    await expect.poll(() => hpWidth(page, 'player'), { timeout: 20_000 }).toBe('100%');

    await expectRunFlowsOn(page);
  });

  test('SKIP keeps the creature as it was and the run still continues', async ({ page }) => {
    test.setTimeout(6 * 60_000);
    const kept = await reachPokeCenter(page);
    const hpBefore = await hpWidth(page, 'player');

    await recoveryModal(page).getByRole('button', { name: 'SKIP', exact: true }).click();

    await expect(recoveryModal(page)).toBeHidden();
    await waitForLog(page, new RegExp(`${kept} decided to keep going!`, 'i'));
    // Skipping heals nothing — no PlayerRecovered, so no bar change and no "fully healed" line.
    expect(await hpWidth(page, 'player')).toBe(hpBefore);
    expect((await logLines(page)).some(l => /was fully healed!/i.test(l))).toBe(false);

    await expectRunFlowsOn(page);
  });
});
