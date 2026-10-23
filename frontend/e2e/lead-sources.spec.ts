import { expect, test } from './fixtures';

test('Admin manages lead sources without exposing inactive sources in new-lead form', async ({ page }) => {
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Lead sources', exact: true }).click();
  const name = 'Referral Source ' + Date.now();
  const renamed = name + ' Updated';
  const list = page.getByRole('region', { name: 'Existing lead sources' });

  await page.getByRole('textbox', { name: 'Source name' }).fill(name);
  await page.getByRole('button', { name: 'Add source' }).click();
  await expect(list.getByRole('row').filter({ hasText: name })).toBeVisible();

  await list.getByRole('row').filter({ hasText: name }).getByRole('button', { name: 'Edit' }).click();
  await page.getByRole('textbox', { name: 'Source name' }).fill(renamed);
  await page.getByRole('button', { name: 'Save source' }).click();
  const row = list.getByRole('row').filter({ hasText: renamed });
  await expect(row).toContainText('Active');

  await row.getByRole('button', { name: 'Deactivate' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Deactivate' }).click();
  await expect(row).toContainText('Inactive');

  await page.getByRole('button', { name: 'All leads', exact: true }).click();
  await page.getByRole('button', { name: 'New lead', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Lead source' }).getByRole('option', { name: renamed })).toHaveCount(0);
});
