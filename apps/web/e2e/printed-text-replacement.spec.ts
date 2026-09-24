import { expect, test } from '@playwright/test';
import { execFile } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { promisify } from 'node:util';

const run = promisify(execFile);

async function imageDistance(page: import('@playwright/test').Page, left: string, right: string): Promise<number> {
  return page.evaluate(async ([first, second]) => {
    const decode = async (source: string) => {
      const image = new Image();
      image.src = source;
      await image.decode();
      const canvas = document.createElement('canvas');
      canvas.width = 400;
      canvas.height = 560;
      const context = canvas.getContext('2d')!;
      context.drawImage(image, 0, 0, canvas.width, canvas.height);
      return context.getImageData(0, 0, canvas.width, canvas.height).data;
    };
    const a = await decode(first);
    const b = await decode(second);
    let sum = 0;
    for (let index = 0; index < a.length; index += 4) {
      sum += Math.abs(a[index] - b[index]) + Math.abs(a[index + 1] - b[index + 1]) + Math.abs(a[index + 2] - b[index + 2]);
    }
    return sum / (a.length / 4);
  }, [left, right] as const);
}

async function pagePixels(page: import('@playwright/test').Page): Promise<string> {
  return page.locator('app-page-card img').first().evaluate(async (image: HTMLImageElement) => {
    await image.decode();
    const canvas = document.createElement('canvas');
    canvas.width = image.naturalWidth;
    canvas.height = image.naturalHeight;
    canvas.getContext('2d')!.drawImage(image, 0, 0);
    return canvas.toDataURL('image/png');
  });
}

async function reopenDocument(page: import('@playwright/test').Page, documentPath: string): Promise<void> {
  await page.reload();
  await page.goto('/e2e-login?user=user-a');
  await page.getByRole('link', { name: 'My documents' }).click();
  await page.locator(`a[href="${documentPath}"]`).click();
}

test('printed text replacement survives reload, export, and undo', async ({ page }) => {
  test.setTimeout(600_000);
  page.setDefaultTimeout(15_000);
  test.skip(process.env['E2E_TEXT_EDIT_READY'] !== '1',
    'Requires the local PostgreSQL, private object storage, worker, and printed fake OCR fixture.');

  await page.goto('/e2e-login?user=user-a');
  const dataUrl = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 1000;
    canvas.height = 1400;
    const context = canvas.getContext('2d')!;
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, 1000, 1400);
    context.fillStyle = '#171717';
    context.font = '48px serif';
    context.fillText('Yap Tzing Yeow', 100, 195);
    return canvas.toDataURL('image/jpeg', 0.94);
  });
  await page.getByLabel('Document title').fill('Printed replacement acceptance');
  await page.getByLabel('Choose file').setInputFiles({
    name: 'printed-form.jpg', mimeType: 'image/jpeg',
    buffer: Buffer.from(dataUrl.split(',')[1], 'base64'),
  });
  await page.getByRole('button', { name: 'Upload securely' }).click();
  await page.getByRole('link', { name: 'Open document preview' }).click();
  const documentPath = new URL(page.url()).pathname;

  const card = page.locator('app-page-card').first();
  await expect(card.getByRole('link', { name: 'Crop & filters' })).toBeVisible({ timeout: 90_000 });
  await card.getByRole('link', { name: 'Crop & filters' }).click();
  const save = page.getByRole('button', { name: 'Save scan' });
  await expect(save).toBeEnabled({ timeout: 90_000 });
  await save.click();
  await expect(page.getByRole('button', { name: 'Edit selected text' })).toHaveCount(0);
  await expect(card.getByRole('img', { name: 'Thumbnail of page 1' })).toBeVisible({ timeout: 90_000 });
  await expect(page.getByText(/text elements recognized/)).toBeVisible({ timeout: 90_000 });
  const originalPixels = await pagePixels(page);

  const words = card.locator('polygon[role="option"]');
  await expect(words).toHaveCount(3);
  await card.locator('svg[role="listbox"]').focus();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('Enter');
  await page.keyboard.press('Shift+ArrowRight');
  await page.keyboard.press('Shift+ArrowRight');
  await card.getByRole('button', { name: 'Edit selected text' }).click();
  await expect(page.getByRole('dialog', { name: 'Replace selected text' })).toBeVisible();
  await page.getByLabel('Replacement text').fill('Tan BB');
  await page.getByRole('button', { name: 'Apply change' }).click();
  await expect(page.getByText('Text change applied. The latest page preview was loaded.'))
    .toBeVisible({ timeout: 120_000 });
  await reopenDocument(page, documentPath);
  await expect(card.getByRole('img', { name: 'Thumbnail of page 1' })).toBeVisible();
  const editedPixels = await pagePixels(page);
  expect(editedPixels).not.toBe(originalPixels);
  page.once('dialog', dialog => void dialog.accept());
  await page.getByRole('button', { name: 'Generate PDF' }).click();
  await expect(page.getByRole('button', { name: 'Download PDF' })).toBeEnabled({ timeout: 120_000 });
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download PDF' }).click();
  const pdf = await download;
  expect(pdf.suggestedFilename()).toBe('Printed replacement acceptance.pdf');
  expect(await pdf.failure()).toBeNull();
  const pdfBytes = await readFile(await pdf.path());
  expect(pdfBytes.toString('latin1')).not.toMatch(/\/Annots\b/);
  const renderDirectory = await mkdtemp(join(tmpdir(), 'superscanner-export-check-'));
  try {
    const renderPrefix = join(renderDirectory, 'page');
    await run('pdftoppm', ['-f', '1', '-l', '1', '-singlefile', '-scale-to', '1000', '-jpeg', await pdf.path(), renderPrefix]);
    const rendered = `data:image/jpeg;base64,${(await readFile(`${renderPrefix}.jpg`)).toString('base64')}`;
    expect(await imageDistance(page, rendered, editedPixels))
      .toBeLessThan(await imageDistance(page, rendered, originalPixels));
  } finally {
    await rm(renderDirectory, { recursive: true, force: true });
  }

  await page.getByRole('button', { name: 'Edit history for page 1' }).click();
  const undo = page.getByRole('button', { name: 'Undo text edit' });
  await expect(undo).toBeEnabled();
  await undo.click();
  await expect(page.getByText('Text change undone.')).toBeVisible();
  await reopenDocument(page, documentPath);
  await expect(card.getByRole('img', { name: 'Thumbnail of page 1' })).toBeVisible();
  expect(await pagePixels(page)).toBe(originalPixels);
  await page.getByRole('button', { name: 'Edit history for page 1' }).click();
  await expect(page.getByRole('button', { name: 'Redo text edit' })).toBeEnabled();
});
