import { test, expect } from '@playwright/test';

// Smallest possible end-to-end check: the app boots and the title screen renders.
// Confirms the Playwright harness + Vite/backend proxy wiring all work.
test('title screen loads', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('GEN 1 BATTLE SIMULATOR')).toBeVisible();
  await expect(page.getByRole('button', { name: /NEW GAME/i })).toBeVisible();
});
