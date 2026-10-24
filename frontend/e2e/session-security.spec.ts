import { expect, test } from './fixtures';

test('Expired authenticated session clears the workspace and returns to login', async ({ page }) => {
  await page.route('**/api/auth/permissions', route =>
    route.fulfill({
      status: 401, contentType: 'application/json',
      body: JSON.stringify({ message: 'Session expired.' }),
    }));
  await page.addInitScript(session => {
    sessionStorage.setItem('crmSession', JSON.stringify(session));
  }, {
    token: process.env.E2E_ADMIN_TOKEN!,
    user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Sign in to your account' })).toBeVisible();
  await expect(page.getByText('Please sign in again.')).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('crmSession'))).toBeNull();
});

test('Incorrect login remains a credential error, not a session expiry', async ({ page }) => {
  await page.route('**/api/auth/login', route =>
    route.fulfill({
      status: 401, contentType: 'application/json',
      body: JSON.stringify({ message: 'Invalid login.' }),
    }));
  await page.goto('/');
  await page.getByLabel('Email address').fill('invalid@example.test');
  await page.locator('input[autocomplete="current-password"]').fill('WrongPassword123!');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page.getByRole('alert')).toContainText('Invalid login.');
  await expect(page.getByText('Please sign in again.')).toHaveCount(0);
});
