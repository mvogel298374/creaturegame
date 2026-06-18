import { test, expect } from '@playwright/test';
import { startBattle, playToRunEnd, bridgeEvents, reachLog } from './helpers';

// The Endless Battle Chain: one persistent player runs battle-after-battle (a fresh wild enemy each time)
// until it faints, then a game-over summary. Determinism is client-only — we can't script the enemy, so we
// play through with a strong starter and assert the chain's own log/bridge signals (see e2e/README.md).
test.describe('Endless chain', () => {
  test('winning continues the run: intermission line, a fresh enemy slides in, progression carried', async ({ page }) => {
    test.setTimeout(120_000);   // matched coin-flip battles — reachLog restarts the run until a win
    const log = await reachLog(page, /A new challenger approaches!/);

    // A win is an intermission, not a terminal — the next encounter is announced.
    expect(log.some(l => /A new challenger approaches!/.test(l))).toBe(true);
    // XP carried/advanced on the win (the chain keeps the same creature).
    expect(log.some(l => /gained \d+ EXP\. Points!/.test(l))).toBe(true);
    // A second enemy is spawned into the running scene (chained encounter, not a fresh scene entry).
    expect((await bridgeEvents(page)).some(e => e.name === 'spawnEnemy')).toBe(true);
  });

  test('QUIT returns to the title screen', async ({ page }) => {
    await startBattle(page, 'CHARIZARD');

    await page.getByRole('button', { name: /QUIT/i }).click();

    await expect(page.getByText('GEN 1 BATTLE SIMULATOR')).toBeVisible();
    await expect(page.getByRole('button', { name: /NEW GAME/i })).toBeVisible();
  });

  test('a run ends when the player faints: Run over summary + game-over screen', async ({ page }) => {
    // A natural run-to-loss can span several battles (no between-battle heal); allow generous time.
    // Low level keeps each battle short so the run reaches its end quickly.
    test.setTimeout(180_000);
    await startBattle(page, 'CHARIZARD', 5);

    const log = await playToRunEnd(page);
    expect(log.some(l => /Run over/.test(l))).toBe(true);

    // Game-over phase: the run-scoped game-over overlay takes over. It shows the run summary and offers
    // PLAY AGAIN / QUIT; the in-battle FIGHT/CHECK action menu is gone.
    const overlay = page.getByRole('alertdialog', { name: /Game over/i });
    await expect(overlay).toBeVisible();
    await expect(overlay.getByText('GAME OVER')).toBeVisible();
    await expect(overlay.getByText(/BATTLES WON/i)).toBeVisible();
    await expect(overlay.getByRole('button', { name: /PLAY AGAIN/i })).toBeVisible();
    await expect(overlay.getByRole('button', { name: /QUIT/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /^FIGHT/i })).toHaveCount(0);

    // PLAY AGAIN starts a fresh run from the starter-selection screen.
    await overlay.getByRole('button', { name: /PLAY AGAIN/i }).click();
    await expect(page).toHaveURL(/\/select$/);
  });
});
