import { expect, test } from '@playwright/test';

test('user uploads a private document and sees validation complete', async ({ page }) => {
  await page.goto('/e2e-login?user=user-a');
  await page.getByRole('button', { name: 'New Scan' }).click();
  await page.getByLabel('Document title').fill('Application form');
  await page.getByLabel('Choose file').setInputFiles('e2e/fixtures/clean-form.pdf');
  await page.getByRole('button', { name: 'Upload securely' }).click();

  await expect(page.getByRole('heading', { name: 'Ready' })).toBeVisible({ timeout: 30_000 });

  await page.goto('/e2e-login?user=user-b');
  await expect(page.getByText('Application form')).not.toBeVisible();
});
