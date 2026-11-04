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

test('Admin reviews a source customer and merges it into the selected customer', async ({ page, request }) => {
  const token = process.env.E2E_ADMIN_TOKEN!;
  const suffix = Date.now();
  const targetName = 'Merge target ' + suffix;
  const sourceName = 'Merge source ' + suffix;
  const headers = { Authorization: 'Bearer ' + token };
  const targetResponse = await request.post(apiUrl + '/api/admin/customers', {
    headers, data: { name: targetName },
  });
  const sourceResponse = await request.post(apiUrl + '/api/admin/customers', {
    headers, data: { name: sourceName },
  });
  expect(targetResponse.ok()).toBeTruthy();
  expect(sourceResponse.ok()).toBeTruthy();
  const targetId = (await targetResponse.json()).id as number;
  const sourceId = (await sourceResponse.json()).id as number;

  await page.addInitScript(session => {
    sessionStorage.setItem('crmSession', JSON.stringify(session));
  }, { token, user: JSON.parse(process.env.E2E_ADMIN_USER!) });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Customer master' }).click();

  const directory = page.getByRole('region', { name: 'Customer directory' });
  await directory.getByRole('textbox', { name: 'Search customers' }).fill(targetName);
  await directory.getByRole('button', { name: 'Search' }).click();
  await directory.getByRole('row').filter({ hasText: targetName })
    .getByRole('button', { name: 'View customer' }).click();

  const target = page.getByRole('dialog', { name: 'Customer ' + targetName });
  const merge = target.getByRole('region', { name: 'Merge customers' });
  await merge.getByRole('spinbutton', { name: 'Source customer ID' }).fill(String(sourceId));
  await merge.getByRole('button', { name: 'Review source' }).click();
  await expect(merge.getByText(sourceName, { exact: false })).toBeVisible();
  await merge.getByRole('textbox', { name: 'Reason for merge' })
    .fill('Verified duplicate customer by administrator');
  await merge.getByRole('button', { name: 'Merge into this customer' }).click();

  const confirmation = page.getByRole('alertdialog', { name: 'Merge customer records?' });
  await expect(confirmation).toContainText(sourceName);
  await expect(confirmation).toContainText(targetName);
  await confirmation.getByRole('button', { name: 'Merge customers' }).click();
  const success = page.getByRole('alertdialog', { name: 'Customers merged' });
  await expect(success).toBeVisible();
  await success.getByRole('button', { name: 'Done' }).click();

  const sourceResponseAfterMerge = await request.get(
    apiUrl + '/api/admin/customers/' + sourceId, { headers });
  expect(sourceResponseAfterMerge.ok()).toBeTruthy();
  const retired = (await sourceResponseAfterMerge.json()).customer;
  expect(retired.isActive).toBe(false);
  expect(retired.mergedIntoCustomerId).toBe(targetId);

  await target.getByRole('button', { name: 'Close customer' }).click();
  await directory.getByRole('textbox', { name: 'Search customers' }).fill(sourceName);
  await directory.getByRole('button', { name: 'Search' }).click();
  const row = directory.getByRole('row').filter({ hasText: sourceName });
  await expect(row).toContainText('Merged');
  await row.getByRole('button', { name: 'View customer' }).click();
  const source = page.getByRole('dialog', { name: 'Customer ' + sourceName });
  await expect(source.getByText('This record is read-only.', { exact: false })).toBeVisible();
  await expect(source.getByText('Edit customer', { exact: true })).toHaveCount(0);
  await source.getByRole('button', { name: 'Open current customer' }).click();
  await expect(page.getByRole('dialog', { name: 'Customer ' + targetName })).toBeVisible();
});
