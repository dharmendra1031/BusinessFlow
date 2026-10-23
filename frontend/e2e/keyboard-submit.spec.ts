import { expect, test } from './fixtures';

test('Enter on the last login input submits the form', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Email address').fill(process.env.CRM_ADMIN_EMAIL!);
  const password = page.locator('input[autocomplete="current-password"]');
  await password.fill(process.env.CRM_ADMIN_PASSWORD!);
  await password.press('Enter');

  await expect(page.getByRole('heading', { name: 'Good to see you!' })).toBeVisible();
});
