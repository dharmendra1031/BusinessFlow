import { expect, test } from '@playwright/test';

test('Feedback dialogs show loading, success and actionable server errors', async ({ page }) => {
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Organization', exact: true }).click();
  const name = 'Feedback department ' + Date.now();
  await page.getByRole('textbox', { name: 'Department name', exact: true }).fill(name);
  await page.route('**/api/admin/organization/departments', async route => {
    await new Promise(resolve => setTimeout(resolve, 900));
    await route.continue();
  });
  await page.getByRole('button', { name: 'Save department', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Processing your request' })).toBeVisible();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('Organization updated');
  await dialog.getByRole('button', { name: 'Done', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Department name', exact: true }).fill(name);
  await page.getByRole('button', { name: 'Save department', exact: true }).click();
  await expect(dialog).toContainText('A department with this name already exists.');
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Department name', exact: true })).toHaveValue(name);
});
