import { expect, test } from './fixtures';

const apiUrl = 'http://localhost:5081';

test('Admin completes quotation, contract, invoice and partial payment', async ({ page, request }) => {
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

  await editor.getByRole('button', { name: 'Submit for approval' }).click();
  const review = editor.getByRole('heading', { name: 'Internal quotation review' });
  await expect(review).toBeVisible();
  await editor.getByRole('textbox', { name: 'Review comment' })
    .fill('Approved by administrator for quotation testing');
  await editor.getByRole('button', { name: 'Approve quotation' }).click();
  await expect(editor.getByRole('button', { name: 'Mark as sent' })).toBeVisible();
  await editor.getByRole('button', { name: 'Mark as sent' }).click();
  await page.getByRole('alertdialog', { name: 'Mark quotation as sent?' })
    .getByRole('button', { name: 'Mark sent' }).click();
  await expect(editor.getByRole('button', { name: 'Record accepted' })).toBeVisible();
  await editor.getByRole('textbox', { name: 'Client contact' }).fill('Customer director');
  await editor.getByRole('textbox', { name: 'Response reference' }).fill('Approved by customer email');
  await editor.getByRole('button', { name: 'Record accepted' }).click();
  await page.getByRole('alertdialog', { name: 'Record client response?' })
    .getByRole('button', { name: 'Record accepted' }).click();
  const handoff = editor.getByRole('region', { name: 'Contract handoff' });
  await expect(handoff).toBeVisible();
  await handoff.getByRole('textbox', { name: 'Contract terms' })
    .fill('Contract covers development and delivery of the approved website');
  await handoff.getByRole('button', { name: 'Create contract draft' }).click();
  const contract = editor.getByRole('region', { name: 'Contract details' });
  await expect(contract).toContainText('Draft');
  await contract.getByRole('textbox', { name: 'Signed by' }).fill('Customer director');
  await contract.getByRole('textbox', { name: 'Signature evidence reference' })
    .fill('Document signed and verified');
  await contract.getByLabel('Signed on')
    .fill(new Date().toISOString().slice(0, 10));
  await contract.getByRole('button', { name: 'Record signature' }).click();
  await page.getByRole('alertdialog', { name: 'Record signed contract?' })
    .getByRole('button', { name: 'Record signature' }).click();
  await expect(contract).toContainText('Signed');

  await page.getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Invoices & collections' }).click();
  const editorBilling = page.getByRole('region', { name: 'New invoice' });
  await editorBilling.getByRole('textbox', { name: 'Find signed contract' }).fill(leadName);
  await editorBilling.getByRole('button', { name: 'Find contracts' }).click();
  await editorBilling.getByRole('button', { name: 'Choose contract' }).click();
  await editorBilling.getByRole('textbox', { name: 'Description' })
    .fill('Advance payment for website project');
  await editorBilling.getByRole('spinbutton', { name: 'Amount' }).fill('300');
  await editorBilling.getByRole('button', { name: 'Create invoice' }).click();
  const invoice = page.getByRole('region', { name: 'Invoice details' });
  await expect(invoice).toContainText('Outstanding: INR 300.000');

  const paymentForm = invoice.locator('form[aria-label="Record payment"]');
  await paymentForm.getByRole('spinbutton', { name: 'Amount' }).fill('200');
  await paymentForm.getByRole('textbox', { name: 'Unique payment reference' })
    .fill('AUTOTEST-' + Date.now());
  await paymentForm.getByRole('button', { name: 'Record payment' }).click();
  await expect(invoice).toContainText('Outstanding: INR 100.000');
  await invoice.getByRole('button', { name: 'View contract ledger' }).click();
  await expect(page.getByRole('region', { name: 'Contract ledger' }))
    .toContainText('Invoice outstanding: INR 100.000');

  const reports = page.getByRole('region', { name: 'Billing reports' });
  await reports.getByRole('button', { name: 'Run report' }).click();
  await expect(reports).toContainText('INR 100.000');
  await reports.getByRole('combobox', { name: 'Report type' }).selectOption('collections');
  await reports.getByRole('button', { name: 'Run report' }).click();
  await expect(reports).toContainText('INR 200.000');

  const credit = invoice.locator('form[aria-label="Issue credit note"]');
  await credit.getByRole('spinbutton', { name: 'Credit amount' }).fill('150');
  await credit.getByRole('textbox', { name: 'Reason' })
    .fill('Reduction in accepted website project delivery scope');
  await credit.getByRole('button', { name: 'Issue credit note' }).click();
  await page.getByRole('alertdialog', { name: 'Issue credit note?' })
    .getByRole('button', { name: 'Issue credit note' }).click();
  await expect(invoice).toContainText('Refund due: INR 50.000');

  const refund = invoice.locator('form[aria-label="Record refund"]');
  await refund.getByRole('combobox', { name: 'Original payment' }).selectOption({ index: 1 });
  await refund.getByRole('spinbutton', { name: 'Refund amount' }).fill('50');
  await refund.getByRole('textbox', { name: 'Unique refund reference' })
    .fill('REFUND-' + Date.now());
  await refund.getByRole('textbox', { name: 'Reason' })
    .fill('Refund completed for reduced website project scope');
  await refund.getByRole('button', { name: 'Record completed refund' }).click();
  await page.getByRole('alertdialog', { name: 'Record completed refund?' })
    .getByRole('button', { name: 'Record refund' }).click();
  await expect(invoice).toContainText('Refund due: INR 0.000');
  await expect(invoice.getByRole('region', { name: 'Refund history' })).toContainText('INR 50.000');
});
