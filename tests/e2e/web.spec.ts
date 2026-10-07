import { expect, test } from '@playwright/test';

test('Web demo renders the design system and responds to theme changes', async ({ page }) => {
  await page.goto('http://127.0.0.1:5173');

  await expect(page.getByRole('heading', { name: 'Engineered for the eight-hour day.' })).toBeVisible();
  await expect(page.locator('sl-button').first()).toBeVisible();
  await page.getByRole('button', { name: 'Dark', exact: true }).click();
  await expect(page.locator('#provider')).toHaveAttribute('data-sl-theme', 'dark');
});
