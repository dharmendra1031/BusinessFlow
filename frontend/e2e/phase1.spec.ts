import { expect, test } from '@playwright/test';

test('Organization: admin creates department and team and sees safe disable errors', async ({ page }) => {
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Organization', exact: true }).click({ timeout: 5000 });
  const department = 'Engineering ' + Date.now();
  const teamName = 'Platform ' + Date.now();
  await page.getByLabel('Department name', { exact: true }).fill(department);
  await page.getByRole('button', { name: 'Save department', exact: true }).click();
  const departments = page.getByRole('region', { name: 'Departments', exact: true });
  await expect(departments.getByRole('cell', { name: department, exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Team department', exact: true }).selectOption({ label: department });
  await page.getByLabel('Team name', { exact: true }).fill(teamName);
  await page.getByRole('button', { name: 'Save team', exact: true }).click();
  await expect(page.getByRole('cell', { name: teamName, exact: true })).toBeVisible();
  await departments.getByRole('row').filter({ has: page.getByRole('cell', { name: department, exact: true }) })
    .getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Transfer or deactivate' })).toBeVisible();
});

test('Custom roles: admin creates a role and configures an action with its data scope', async ({ page }) => {
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Custom roles', exact: true }).click({ timeout: 5000 });
  const role = 'Quality ' + Date.now();
  await page.getByRole('textbox', { name: 'Role name', exact: true }).fill(role);
  await page.getByRole('button', { name: 'Create role', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Custom role', exact: true })).toHaveValue(/\d+/);
  await page.getByRole('combobox', { name: 'Action', exact: true }).selectOption('leads.assign');
  await page.getByRole('combobox', { name: 'Data scope', exact: true }).selectOption('Department');
  await page.getByRole('button', { name: 'Save grant', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'leads.assign', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Department', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Revoke leads.assign', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'leads.assign', exact: true })).toHaveCount(0);
});
