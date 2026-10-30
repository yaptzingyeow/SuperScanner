import { expect, test } from '@playwright/test';

test('user uploads a private document that other users cannot see', async ({ page }) => {
  // Unique per run: the local database keeps documents from earlier runs.
  const title = `Application form ${Date.now()}`;
  await page.goto('/e2e-login?user=user-a');
  await page.getByLabel(/Document title/).fill(title);
  // Choosing a file starts the upload; the app then opens the import review for the new document.
  await page.locator('#document-file').setInputFiles('e2e/fixtures/clean-form.pdf');
  await expect(page.getByRole('heading', { name: 'Import pages' })).toBeVisible({ timeout: 30_000 });

  await page.getByRole('link', { name: 'My documents' }).click();
  await expect(page.getByText(title)).toBeVisible({ timeout: 30_000 });

  await page.goto('/e2e-login?user=user-b');
  await page.getByRole('link', { name: 'My documents' }).click();
  await expect(page.getByRole('heading', { name: 'Your documents' })).toBeVisible();
  await expect(page.getByText(title)).not.toBeVisible();
});
