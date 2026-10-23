import { expect, test } from './fixtures';

test('Source Summary uses the selected SQL report filters and renders typed metrics', async ({ page }) => {
  const calls: URL[] = [];
  await page.route('**/api/reports?**', async route => {
    const url = new URL(route.request().url());
    if (url.searchParams.get('type') !== 'source-wise') {
      await route.continue();
      return;
    }
    calls.push(url);
    await route.fulfill({
      status: 200, contentType: 'application/json',
      body: JSON.stringify({
        type: 'source-wise', total: 1, page: 1, pageSize: 50,
        rows: [{ sourceId: 2, sourceName: 'Website', leadCount: 4,
          qualifiedCount: 2, wonCount: 1, conversionRate: 25,
          pipelineAmount: 1500.75, wonAmount: 800.5 }],
      }),
    });
  });
  await page.addInitScript(session =>
    sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!,
    user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Reports', exact: true }).click();
  await page.getByLabel('Report').selectOption('source-wise');
  await page.getByLabel('Source', { exact: true }).selectOption('2');
  await page.getByLabel('From', { exact: true }).fill('2026-01-01');
  await page.getByLabel('To', { exact: true }).fill('2026-01-31');
  await page.getByRole('button', { name: 'Run report' }).click();

  await expect(page.getByRole('heading', { name: '1 records' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Website', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: '1500.75', exact: true })).toBeVisible();
  expect(calls).toHaveLength(1);
  expect(calls[0].searchParams.get('type')).toBe('source-wise');
  expect(calls[0].searchParams.get('sourceId')).toBe('2');
  expect(calls[0].searchParams.get('from')).toBe('2026-01-01');
  expect(calls[0].searchParams.get('to')).toBe('2026-01-31');
});
