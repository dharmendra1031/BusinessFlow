import { expect, test } from '@playwright/test';

test('Lead conflict preserves unsaved input and offers an explicit reload', async ({ page, request }) => {
  const headers = { Authorization: 'Bearer ' + process.env.E2E_ADMIN_TOKEN! };
  const name = 'Conflict customer ' + Date.now();
  const created = await request.post('http://localhost:5081/api/leads', {
    headers, data: { name, phone: '1234567890', sourceId: 1 },
  });
  expect(created.status()).toBe(201);
  const { id } = await created.json();
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'All leads', exact: true }).click();
  await page.getByRole('button', { name, exact: true }).click();
  const editor = page.getByRole('dialog');
  await editor.getByLabel('Full name').fill('My unsaved edit');
  const current = await (await request.get(`http://localhost:5081/api/leads/${id}`, { headers })).json();
  const changed = await request.put(`http://localhost:5081/api/leads/${id}`, {
    headers, data: { ...current, name: 'Other employee edit' },
  });
  expect(changed.status()).toBe(204);
  await editor.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('alertdialog')).toContainText('This record changed');
  await page.getByRole('alertdialog').getByRole('button', { name: 'Close', exact: true }).click();
  await expect(editor.getByLabel('Full name')).toHaveValue('My unsaved edit');
  await editor.getByRole('button', { name: 'Reload latest lead', exact: true }).click();
  await expect(editor.getByLabel('Full name')).toHaveValue('Other employee edit');
});
