import { expect, test } from './fixtures';

const apiUrl = 'http://localhost:5081';

test('Admin can add rename deactivate and reactivate lead sources', async ({ page, request }) => {
  const name = 'Campaign ' + Date.now();
  const renamed = 'Referral ' + Date.now();
  const token = process.env.E2E_ADMIN_TOKEN!;
  await page.addInitScript(session => {
    sessionStorage.setItem('crmSession', JSON.stringify(session));
  }, { token, user: JSON.parse(process.env.E2E_ADMIN_USER!) });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Lead sources' }).click();

  const form = page.getByRole('region', { name: 'Manage lead sources' });
  await form.getByRole('textbox', { name: 'Source name' }).fill(name);
  await form.getByRole('button', { name: 'Add source' }).click();
  let row = page.getByRole('row').filter({ hasText: name });
  await expect(row).toBeVisible();

  await row.getByRole('button', { name: 'Edit' }).click();
  await form.getByRole('textbox', { name: 'Source name' }).fill(renamed);
  await form.getByRole('button', { name: 'Save source' }).click();
  row = page.getByRole('row').filter({ hasText: renamed });
  await expect(row).toContainText('Active');

  await row.getByRole('button', { name: 'Deactivate' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Deactivate' }).click();
  await expect(row).toContainText('Inactive');

  const sources = await request.get(apiUrl + '/api/masters/lead-sources', {
    headers: { Authorization: 'Bearer ' + token },
  });
  expect(sources.ok()).toBeTruthy();
  expect((await sources.json() as { name: string }[])
    .some(item => item.name === renamed)).toBe(false);

  await row.getByRole('button', { name: 'Activate' }).click();
  await expect(row).toContainText('Active');
  const restored = await request.get(apiUrl + '/api/masters/lead-sources', {
    headers: { Authorization: 'Bearer ' + token },
  });
  expect((await restored.json() as { name: string }[])
    .some(item => item.name === renamed)).toBe(true);
});
