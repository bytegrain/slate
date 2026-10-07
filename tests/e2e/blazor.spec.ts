import { expect, test } from '@playwright/test';

test('Blazor data-grid demo renders its interactive grid', async ({ page }) => {
  await page.goto('http://127.0.0.1:5091/data-grid');

  await expect(page.getByRole('heading', { name: 'Data grid' })).toBeVisible();
  const grid = page.locator('.sl-data-grid').first();
  await expect(grid).toBeVisible();
  await expect(grid.locator('.sl-data-grid__row').first()).toBeVisible({ timeout: 30_000 });
  const viewport = grid.locator('.sl-data-grid__viewport');
  await viewport.evaluate((element) => {
    element.scrollLeft = element.scrollWidth;
    element.dispatchEvent(new Event('scroll'));
  });
  await expect.poll(() => viewport.evaluate((element) => element.scrollLeft)).toBeGreaterThan(0);
  const pinnedCell = grid.locator('.sl-data-grid__cell.is-pinned-start').first();
  await expect(pinnedCell).toBeVisible();
  await expect.poll(() => pinnedCell.evaluate((cell) => getComputedStyle(cell).backgroundColor))
    .not.toBe('rgba(0, 0, 0, 0)');
});
