import { test, expect } from '@playwright/test';
import { startBattle, fightButton, chooseMove, bridgeEvents, reachLog } from './helpers';

// Mirrors §3–§7 of the UI checklist — battle entry, menus, sequencing, end.
test.describe('Battle', () => {
  test('entry: nameplates, VS log, and an enabled action menu', async ({ page }) => {
    await startBattle(page, 'CHARIZARD');

    await expect(page.locator('.nameplate--player')).toContainText('CHARIZARD');
    await expect(page.locator('.nameplate--enemy')).toBeVisible();
    await expect(page.locator('.log-line').first()).toContainText('CHARIZARD VS');
    await expect(fightButton(page)).toBeEnabled();
    await expect(page.getByRole('button', { name: /CHECK POKEMON/i })).toBeEnabled();
  });

  test('move menu: 2×2 grid of moves with PP, and BACK returns', async ({ page }) => {
    await startBattle(page, 'CHARIZARD');
    await fightButton(page).click();

    const moves = page.locator('.move-btn');
    await expect(moves).toHaveCount(4);
    await expect(moves.first().locator('.move-pp')).toContainText('/');   // "n/n" PP

    await page.getByRole('button', { name: /BACK/i }).click();
    await expect(fightButton(page)).toBeVisible();   // back on the action menu
  });

  test('a chosen move is announced, then resolves; bridge fires the lunge before the hit', async ({ page }) => {
    await startBattle(page, 'CHARIZARD');
    const move = await chooseMove(page);

    // The attacker's "used MOVE!" line appears.
    await expect(page.locator('.log-line').filter({ hasText: new RegExp(`used ${move}`, 'i') }))
      .toBeVisible();

    // Bridge ordering: a move animation is emitted, and any hit sound follows it
    // (animation → impact), never the reverse.
    await expect.poll(async () => (await bridgeEvents(page)).some(e => e.name === 'playMoveAnimation')).toBe(true);
    const events = await bridgeEvents(page);
    const firstLunge = events.findIndex(e => e.name === 'playMoveAnimation');
    const firstHit   = events.findIndex(e => e.name === 'playHitSound');
    if (firstHit !== -1) expect(firstHit).toBeGreaterThan(firstLunge);
  });

  test('a won battle is an intermission (faint → new challenger), not a terminal "wins!"', async ({ page }) => {
    test.setTimeout(120_000);   // matched coin-flip battles — reachLog restarts the run until a win
    const log = await reachLog(page, /A new challenger approaches!/);

    // The endless chain replaced the terminal "X wins!" with an intermission line.
    const challengerIdx = log.findIndex(l => /A new challenger approaches!/.test(l));
    const faintIdx = log.findIndex(l => /fainted!$/.test(l));
    expect(challengerIdx).toBeGreaterThan(-1);
    expect(faintIdx).toBeGreaterThan(-1);
    expect(challengerIdx).toBeGreaterThan(faintIdx);   // a faint precedes the intermission
    expect(log.some(l => /wins!$/.test(l))).toBe(false);

    // Every "took N damage" line is preceded somewhere earlier by a "used" line.
    const firstUsed = log.findIndex(l => /\bused\b/.test(l));
    const firstDamage = log.findIndex(l => /took \d+ damage/.test(l));
    if (firstDamage !== -1) expect(firstDamage).toBeGreaterThan(firstUsed);
  });
});
