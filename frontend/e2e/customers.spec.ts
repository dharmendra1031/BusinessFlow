import { expect, test } from './fixtures';

const apiUrl = 'http://localhost:5081';

test('Admin creates customer, contact and links an existing CRM lead', async ({ page, request }) => {
  const token = process.env.E2E_ADMIN_TOKEN!;
  const name = 'Client account ' + Date.now();
  const leadName = 'Linked lead ' + Date.now();
  const created = await request.post(apiUrl + '/api/leads', {
    headers: { Authorization: 'Bearer ' + token },
    data: { name: leadName, phone: '1234567890', sourceId: 1 },
  });
  expect(created.ok()).toBeTruthy();

  await page.addInitScript(session => {
    sessionStorage.setItem('crmSession', JSON.stringify(session));
  }, { token, user: JSON.parse(process.env.E2E_ADMIN_USER!) });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Customer master' }).click();

  const form = page.getByRole('region', { name: 'Add customer' });
  await form.getByRole('textbox', { name: 'Customer / company name' }).fill(name);
  await form.getByRole('textbox', { name: 'Legal / billing name' }).fill('Sample Technology Private Limited');
  await form.getByRole('textbox', { name: 'GSTIN (India)' }).fill('09ABCDE1234F1Z5');
  await form.getByRole('textbox', { name: 'Billing city' }).fill('Kanpur');
  await form.getByRole('button', { name: 'Add customer' }).click();

  const detail = page.getByRole('dialog', { name: 'Customer ' + name });
  await expect(detail).toBeVisible();
  await detail.getByText('Edit customer', { exact: true }).click();
  await expect(detail.getByRole('textbox', { name: 'GSTIN (India)' })).toHaveValue('09ABCDE1234F1Z5');
  await expect(detail.getByRole('textbox', { name: 'Billing city' })).toHaveValue('Kanpur');
  const contact = detail.getByRole('region', { name: 'Customer contacts' });
  await contact.getByRole('textbox', { name: 'Contact name' }).fill('Primary contact');
  await contact.getByRole('checkbox', { name: 'Primary contact' }).check();
  await contact.getByRole('button', { name: 'Save contact' }).click();
  await expect(contact.getByText('Primary contact', { exact: true })).toBeVisible();

  const leads = detail.getByRole('region', { name: 'Linked leads' });
  await leads.getByRole('textbox', { name: 'Find existing unlinked lead' }).fill(leadName);
  await leads.getByRole('button', { name: 'Find leads' }).click();
  await expect(leads.getByText(leadName, { exact: true })).toBeVisible();
  await leads.getByRole('button', { name: 'Link to customer' }).click();
  await expect(leads.getByText(leadName, { exact: true })).toBeVisible();
  await expect(leads.getByRole('button', { name: 'Unlink' })).toBeVisible();
  await expect(detail.getByRole('region', { name: 'Customer activity history' })
    .getByText('Link · Leads')).toBeVisible();

  await detail.getByRole('button', { name: 'Close customer' }).click();
  const directory = page.getByRole('region', { name: 'Customer directory' });
  await expect(directory.getByRole('row').filter({ hasText: name })).toContainText('1');
});
