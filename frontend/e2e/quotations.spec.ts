import { expect, test } from './fixtures';

const apiUrl = 'http://localhost:5081';

test('Admin creates a quotation through lead search and preserves revisions', async ({ page, request }) => {
  const token = process.env.E2E_ADMIN_TOKEN!;
  const headers = { Authorization: 'Bearer ' + token };
  const leadName = 'Quotation lead ' + Date.now();
  const lead = await request.post(apiUrl + '/api/leads', {
    headers, data: { name: leadName, phone: '1234567890', sourceId: 1 },
  });
  expect(lead.ok()).toBeTruthy();

  await page.addInitScript(session => {
    sessionStorage.setItem('crmSession', JSON.stringify(session));
  }, { token, user: JSON.parse(process.env.E2E_ADMIN_USER!) });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Sales quotations' }).click();

  const editor = page.getByRole('region', { name: 'Quotation editor' });
  await editor.getByRole('textbox', { name: 'Find an existing lead' }).fill(leadName);
  await editor.getByRole('button', { name: 'Find leads' }).click();
  await editor.getByRole('button', { name: 'Choose lead' }).click();
  await editor.getByRole('textbox', { name: 'Quotation title' }).fill('Initial proposal');
  await editor.getByRole('textbox', { name: 'Description' }).fill('Website build');
  await editor.getByRole('spinbutton', { name: 'Unit price' }).fill('2500.500');
  await editor.getByRole('button', { name: 'Create quotation' }).click();
  await expect(editor.getByRole('heading', { name: /Quotation #/ })).toBeVisible();
  await expect(editor.getByRole('heading', { name: 'Revision history (1)' })).toBeVisible();

  await editor.getByRole('textbox', { name: 'Quotation title' }).fill('Updated proposal');
  await editor.getByRole('button', { name: 'Save new revision' }).click();
  await expect(editor.getByRole('heading', { name: 'Revision history (2)' })).toBeVisible();
  await expect(editor.getByText('Revision 1 · Initial proposal')).toBeVisible();
  await expect(editor.getByText('Revision 2 · Updated proposal')).toBeVisible();

  await editor.getByRole('button', { name: 'Mark as sent' }).click();
  await page.getByRole('alertdialog', { name: 'Mark quotation as sent?' })
    .getByRole('button', { name: 'Mark sent' }).click();
  await expect(editor.getByRole('button', { name: 'Record accepted' })).toBeVisible();
});
