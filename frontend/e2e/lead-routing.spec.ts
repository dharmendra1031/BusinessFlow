import { expect, test } from './fixtures';

test('Admin configures and enables website lead routing from the master', async ({ page }) => {
  let rule: { id: number; sourceId: number; teamId: number;
    maxOpenLeads: number; isActive: boolean } | null = null;

  await page.route('**/api/admin/lead-routing**', async route => {
    const method = route.request().method();
    if (method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json',
        body: JSON.stringify(rule ? [rule] : []) });
      return;
    }
    const data = route.request().postDataJSON();
    expect(data.sourceId).toBe(2);
    expect(data.teamId).toBe(91);
    rule = { id: 811, ...data };
    await route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify(rule) });
  });
  await page.route('**/api/admin/lead-sources', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify([{ id: 2, name: 'Website', isActive: true },
        { id: 3, name: 'Facebook', isActive: true }]) });
  });
  await page.route('**/api/admin/organization', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ departments: [{ id: 19, name: 'Sales', isActive: true }],
        teams: [{ id: 91, departmentId: 19, name: 'Inbound', isActive: true }],
        memberships: [] }) });
  });

  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Lead routing', exact: true }).click();

  const editor = page.getByRole('region', { name: 'Routing rule editor' });
  await expect(editor.getByRole('option', { name: 'Facebook' })).toHaveCount(0);
  await editor.getByRole('combobox', { name: 'Lead source' }).selectOption('2');
  await editor.getByRole('combobox', { name: 'Target team' }).selectOption('91');
  await editor.getByRole('spinbutton', { name: 'Maximum open leads per employee' }).fill('15');
  await editor.getByRole('button', { name: 'Save routing rule' }).click();

  const list = page.getByRole('region', { name: 'Routing rules', exact: true });
  const row = list.getByRole('row').filter({ hasText: 'Website' });
  await expect(row).toContainText('Inbound');
  await expect(row).toContainText('Disabled');
  await row.getByRole('button', { name: 'Enable' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Enable' }).click();
  await expect(row).toContainText('Enabled');
});
