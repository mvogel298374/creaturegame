import { test, expect } from '@playwright/test';
import { startBattle, fightButton } from './helpers';

// The move-menu STAB highlight exercises the client RENDER layer (the JSX class wiring) — the layer the pure
// timeline.test.ts unit tests can't reach, and exactly where the STAB flag silently failed to show until the
// SignalR projection was fixed. The engine/projection logic is pinned by MoveInfoStabTests +
// WebEventContractTests; here we only assert the cue reaches the DOM in a real battle.
//
// NOTE: the parallel effectiveness-colour cue (log-line--super/weak/immune) is intentionally NOT E2E-tested.
// The enemy type is RNG, so reaching a non-neutral hit deterministically isn't possible without a seeded run
// the UI can't request, making the test slow + flaky. Its tone logic is unit-tested (timeline.test.ts), and
// it renders via the identical state→className mechanism this STAB spec proves reaches the DOM.

test.describe('Move-menu STAB highlight', () => {
  test('flags the player’s same-type damaging move, not off-type moves', async ({ page }) => {
    // Charizard @ L50 has a deterministic CanonicalLatest moveset: SCRATCH/RAGE/SLASH (Normal) + FLAMETHROWER
    // (Fire). Charizard is Fire/Flying, so only FLAMETHROWER earns STAB. The STAB flag itself has no RNG, but
    // in-suite the shared backend + biome mode's extra async step widened the setup timing; a fixed seed pins
    // the whole run (enemy, biome offer) so the start is reproducible and the spec no longer flakes in-suite.
    await startBattle(page, 'CHARIZARD', 50, 1);
    await fightButton(page).click();

    const flamethrower = page.locator('.move-btn', { hasText: 'FLAMETHROWER' });
    await expect(flamethrower).toHaveClass(/move-btn--stab/);
    await expect(flamethrower.locator('.move-stab')).toBeVisible();

    const scratch = page.locator('.move-btn', { hasText: 'SCRATCH' });
    await expect(scratch).not.toHaveClass(/move-btn--stab/);
    await expect(scratch.locator('.move-stab')).toHaveCount(0);
  });
});
