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
  await expect(dialog.locator('.feedback-kicker')).toHaveText('Success');
  await dialog.getByRole('button', { name: 'Done', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Department name', exact: true }).fill(name);
  await page.getByRole('button', { name: 'Save department', exact: true }).click();
  await expect(dialog).toContainText('A department with this name already exists.');
  await expect(dialog.locator('.feedback-kicker')).toHaveText('Action failed');
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Department name', exact: true })).toHaveValue(name);
});

test('Background reads do not cover the workspace with a blocking loading dialog', async ({ page }) => {
  let signalRequest!: () => void;
  const requestStarted = new Promise<void>(resolve => { signalRequest = resolve; });
  await page.route('**/api/auth/permissions', async route => {
    signalRequest();
    await new Promise(resolve => setTimeout(resolve, 1100));
    await route.continue();
  });
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await requestStarted;
  await page.waitForTimeout(600);
  await expect(page.locator('.request-loading')).toHaveCount(0);
});

test('Invalid fields are highlighted and recover when edited', async ({ page }) => {
  await page.goto('/');
  const email = page.getByLabel('Email address');
  await email.fill('invalid-email');
  await page.locator('input[autocomplete="current-password"]').fill('ValidPassword123!');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(email).toHaveAttribute('aria-invalid', 'true');
  await email.fill('valid@example.test');
  await expect(email).not.toHaveAttribute('aria-invalid', 'true');
});
