import { test, expect } from '@playwright/test';
import { startBattle, fightButton } from './helpers';

// Regression guard for the "HP dropped instantly when I chose an attack" bug.
// The fix routes TurnStarted through the timeline so HP syncs only AFTER the
// turn's damage animates. End-to-end proof: there is a window where a move has
// been announced but the enemy HP bar is still full — HP drains after the
// announce/animation, it never snaps to the end-of-turn value at choose-time.
test('enemy HP does not snap to its end-of-turn value when a move is chosen', async ({ page }) => {
  await startBattle(page, 'CHARIZARD');
  await fightButton(page).click();

  const timeline = await page.evaluate(async () => {
    const btn = [...document.querySelectorAll('.move-btn')]
      .find(b => !(b as HTMLButtonElement).disabled) as HTMLButtonElement;
    const enemyHp = () =>
      (document.querySelector('.nameplate--enemy .bar-fill') as HTMLElement | null)?.style.width || '?';
    const lastLog = () => {
      const ls = [...document.querySelectorAll('.log-line')];
      return ls.length ? ls[ls.length - 1].textContent!.trim() : '';
    };
    const samples: { t: number; hp: string; last: string }[] = [];
    const t0 = performance.now();
    btn.click();
    await new Promise<void>(res => {
      const iv = setInterval(() => {
        samples.push({ t: Math.round(performance.now() - t0), hp: enemyHp(), last: lastLog() });
        if (performance.now() - t0 > 2500) { clearInterval(iv); res(); }
      }, 30);
    });
    return samples;
  });

  const usedWhileFull = timeline.some(s => /used .+!/.test(s.last) && s.hp === '100%');
  const compressed = timeline.filter((s, i) =>
    i === 0 || s.hp !== timeline[i - 1].hp || s.last !== timeline[i - 1].last);
  expect(usedWhileFull, `no "used" frame with full enemy HP — HP snapped early.\n${JSON.stringify(compressed, null, 2)}`)
    .toBe(true);
});
