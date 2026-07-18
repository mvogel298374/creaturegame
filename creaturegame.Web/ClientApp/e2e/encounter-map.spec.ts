import { test, expect } from '@playwright/test';
import { startBattle, playToNextEncounter } from './helpers';

// The encounter-map ladder (Phase 2): once a biome is entered, its seeded node plan is drawn as a vertical
// Slay-the-Spire-style ladder — one node per RunNodeKind (Boss the apex), capped by a client-synthesized Poké
// Center 'Rest'. The pin marks the node in progress and advances as the run walks the plan. Presentation only —
// the route is fixed and logic-driven (Phase 1 reveal plumbing).
//
// Seed 1 / BULBASAUR opens on a biome whose first node is a plain wild battle (verified), so the MAP button and
// ladder are reachable right after the route pick. If node-weighting (RunDirector.PickInteriorNode) changes so
// this seed's opening node becomes an interaction node, pick another wild-first seed.
const MAP_SEED = 1;

// Reach the opening route choice (the map) without picking, so a test can assert its pre-pick state.
async function gotoOpeningRouteChoice(page: import('@playwright/test').Page, seed: number, species = 'BULBASAUR') {
  await page.goto(`/select?e2e=1&seed=${seed}`);
  await page.locator('.species-card').first().waitFor({ state: 'visible', timeout: 10_000 });
  await page.locator('.select-search').fill(species);
  await page.locator('.species-card', { has: page.locator('.card-name', { hasText: new RegExp(`^${species}$`, 'i') }) }).click();
  await page.getByRole('button', { name: /CONFIRM/i }).click();
}

test.describe('Encounter map (region graph + route choice)', () => {
  test('the run opens on a map-based route choice — a region graph with clickable offered waypoints (no card modal)', async ({ page }) => {
    test.setTimeout(60_000);
    await gotoOpeningRouteChoice(page, MAP_SEED);

    // The route choice is the region map, not the retired biome-card modal.
    const choice = page.locator('.route-choice-modal');
    await expect(choice).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.biome-card')).toHaveCount(0); // the old card modal is gone

    // A graph of waypoints wired by edges, with at least one glowing, clickable offered biome.
    await expect(choice.locator('.region-node')).not.toHaveCount(0);
    await expect(choice.locator('.region-edge')).not.toHaveCount(0);
    const offered = choice.locator('.region-node--offered');
    await expect(offered.first()).toBeVisible();
    // A11y: the choice focuses the first offered waypoint on open, so a keyboard user lands on an actionable pick.
    await expect(offered.first()).toBeFocused();

    // Clicking an offered waypoint charts the route: the choice closes, the biome is entered, and the region map
    // (in the pinned overlay) marks it current — the route is traced.
    await offered.first().click();
    await expect(choice).toHaveCount(0);
    await page.locator('.map-toggle-btn').click();
    await expect(page.locator('.encounter-map .region-node--current')).toHaveCount(1);
  });
});

test.describe('Encounter map (route ladder)', () => {
  test('entering a biome reveals a node ladder — Boss apex, synthesized Rest cap, one current pin — toggled by MAP', async ({ page }) => {
    test.setTimeout(60_000);
    await startBattle(page, 'BULBASAUR', undefined, MAP_SEED);

    // Biome mode → the MAP toggle appears (the legacy chain has no node plan and no button).
    const mapBtn = page.locator('.map-toggle-btn');
    await expect(mapBtn).toBeVisible({ timeout: 15_000 });

    // Pin the ladder open (its own auto-peek may have already faded).
    await mapBtn.click();
    const ladder = page.locator('.encounter-map');
    await expect(ladder).toBeVisible();

    // The biome is titled, and the ladder has the structural invariants of every biome route:
    await expect(ladder.locator('.encounter-map-biome')).not.toBeEmpty();
    await expect(ladder.locator('.ladder-node')).not.toHaveCount(0);
    await expect(ladder.locator('.ladder-node--bossbattle')).toHaveCount(1); // the single Boss apex
    // The Boss apex names a themed gate-boss trainer (bossTrainer.ts), not the behind-the-curtain "Region gate".
    await expect(ladder.locator('.ladder-node--bossbattle .ladder-sub')).toHaveText(/^Trainer .+/);
    await expect(ladder.locator('.ladder-node--rest')).toHaveCount(1);        // the synthesized Poké Center cap
    await expect(ladder.locator('.ladder-node--current')).toHaveCount(1);     // exactly one "you are here" pin

    // The pin starts at the biome's first node (nothing done yet), so no node reads as done.
    await expect(ladder.locator('.ladder-node--done')).toHaveCount(0);

    // The MAP toggle closes it again (× button).
    await ladder.locator('.encounter-map-close').click();
    await expect(ladder).toHaveCount(0);
  });

  test('winning the opening node advances the pin — the cleared node reads done, one pin remains', async ({ page }) => {
    test.setTimeout(90_000);
    await startBattle(page, 'BULBASAUR', undefined, MAP_SEED);

    // Play the opening node to its win (the chain's "A new challenger approaches!" intermission), clearing the
    // post-win reward modal along the way — the run walks off node 0 onto the next node.
    await playToNextEncounter(page);

    // The ladder now shows the cleared node(s) as done and the pin advanced onto the current node.
    await page.locator('.map-toggle-btn').click();
    const ladder = page.locator('.encounter-map');
    await expect(ladder).toBeVisible();
    await expect(ladder.locator('.ladder-node--done')).not.toHaveCount(0); // ≥1 node cleared — the pin moved
    await expect(ladder.locator('.ladder-node--current')).toHaveCount(1);  // still exactly one "you are here" pin
  });
});
