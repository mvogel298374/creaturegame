import { test, expect } from '@playwright/test';

// Mirrors §2 of the UI checklist — starter selection.
test.describe('Starter selection', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.locator('.btn-new-game').click();
  });

  test('renders all 151 species with sprites, type badges and BST', async ({ page }) => {
    const cards = page.locator('.species-card');
    await expect(cards).toHaveCount(151);

    const charizard = cards.filter({ hasText: 'CHARIZARD' });
    await expect(charizard.locator('img')).toBeVisible();
    await expect(charizard).toContainText('FIRE');
    await expect(charizard).toContainText('FLYING');   // Gen 1 typing via past_types
    await expect(charizard).toContainText('449');      // BST
  });

  test('level slider defaults to 50 and is adjustable', async ({ page }) => {
    const slider = page.locator('input[type="range"]');
    await expect(slider).toHaveValue('50');
    await expect(slider).toHaveAttribute('min', '5');
    await expect(slider).toHaveAttribute('max', '100');
  });

  test('selecting a starter shows the confirm footer and CONFIRM enters battle', async ({ page }) => {
    await page.locator('.species-card', { hasText: 'CHARIZARD' }).click();

    const confirm = page.getByRole('button', { name: /CONFIRM/i });
    await expect(confirm).toBeVisible();

    await confirm.click();
    // Battle screen: the action menu appears once entry finishes.
    await expect(page.getByRole('button', { name: /^FIGHT/i })).toBeVisible({ timeout: 15_000 });
  });
});
