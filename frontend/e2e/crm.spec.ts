import { type APIRequestContext, type Page } from '@playwright/test';
import { expect, test } from './fixtures';

const apiUrl = 'http://localhost:5081';
const adminEmail = process.env.CRM_ADMIN_EMAIL!;
const adminPassword = process.env.CRM_ADMIN_PASSWORD!;
const employeePassword = 'EmployeeUiTest123!';
const unique = () => Date.now().toString();

function adminSession() {
  return {
    token: process.env.E2E_ADMIN_TOKEN!,
    user: JSON.parse(process.env.E2E_ADMIN_USER!),
  };
}

async function signIn(page: Page, email = adminEmail, password = adminPassword) {
  if (email === adminEmail) {
    await page.addInitScript(session => {
      sessionStorage.setItem('crmSession', JSON.stringify(session));
    }, adminSession());
    await page.goto('/');
  } else {
    await page.goto('/');
    await page.getByLabel('Email address').fill(email);
    await page.locator('input[autocomplete="current-password"]').fill(password);
    await page.getByRole('button', { name: 'Sign in' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Good to see you!' })).toBeVisible();
}

function adminToken() {
  return process.env.E2E_ADMIN_TOKEN!;
}

async function createLead(request: APIRequestContext, name: string): Promise<number> {
  const token = adminToken();
  const response = await request.post(apiUrl + '/api/leads', {
    headers: { Authorization: 'Bearer ' + token },
    data: {
      name, phone: '+96555501234', sourceId: 1,
      leadGivenBy: 'Playwright QA', projectAmount: 250.5,
    },
  });
  expect(response.status()).toBe(201);
  return (await response.json()).id as number;
}

async function createEmployee(request: APIRequestContext, role: 'Manager' | 'Employee') {
  const token = adminToken();
  const email = 'ui-' + role.toLowerCase() + '-' + unique() + '@example.test';
  const response = await request.post(apiUrl + '/api/employees', {
    headers: { Authorization: 'Bearer ' + token },
    data: { fullName: 'UI ' + role, email, password: employeePassword, role },
  });
  expect(response.status()).toBe(201);
  return { email, id: (await response.json()).id as number };
}

test('Login: invalid credentials, visibility toggle and sign out', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Sign in to your account' })).toBeVisible();
  await page.getByLabel('Email address').fill(adminEmail);
  await page.locator('input[autocomplete="current-password"]').fill('WrongPassword123!');
  await page.getByRole('button', { name: 'Show password' }).click();
  await expect(page.locator('input[autocomplete="current-password"]')).toHaveAttribute('type', 'text');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toContainText('Invalid login.');
  await page.evaluate(session => {
    sessionStorage.setItem('crmSession', JSON.stringify(session));
  }, adminSession());
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Good to see you!' })).toBeVisible();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Sign in to your account' })).toBeVisible();
});

test('Admin: every main screen loads with a real API', async ({ page }, testInfo) => {
  await signIn(page);
  const pageErrors: string[] = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  const screens = [
    ['Dashboard', 'Good to see you!'],
    ['All leads', 'Manage leads'],
    ['Follow-ups', 'Follow-ups'],
    ['Team', 'Team members'],
    ['Reports', 'Reports'],
    ['Change password', 'Change password'],
  ] as const;

  for (const [nav, heading] of screens) {
    await page.getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: nav, exact: true }).click();
    await expect(page.locator('main.content').getByRole('heading', { name: heading })).toBeVisible();
    await expect(page.locator('.loading-state')).toHaveCount(0);
    await expect(page.locator('.error-state')).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath(nav.replaceAll(' ', '-') + '.png'), fullPage: true });
  }

  await page.getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Dashboard' }).click();
  await page.getByLabel('Lead creation period').selectOption('today');
  await expect(page.getByLabel('From date')).not.toHaveValue('');
  await page.getByRole('button', { name: 'Refresh' }).click();
  expect(pageErrors).toEqual([]);
});

test('Leads: create, search, edit, status, schedule and history', async ({ page }) => {
  const customer = 'Playwright Customer ' + unique();
  await signIn(page);
  await page.getByRole('button', { name: 'All leads', exact: true }).click();
  await page.getByRole('button', { name: 'Add lead' }).click();
  const create = page.getByRole('dialog', { name: 'Add a lead' });
  await create.getByPlaceholder('Customer name').fill(customer);
  await create.getByPlaceholder('+965 ...').fill('+96555501001');
  await create.getByLabel('Lead given by').fill('Playwright QA');
  await create.getByLabel('Project amount (KWD)').fill('315.750');
  await create.getByRole('button', { name: 'Create lead' }).click();

  await page.getByLabel('Search leads').fill(customer);
  await expect(page.getByRole('button', { name: customer, exact: true })).toBeVisible();
  await page.getByRole('button', { name: customer, exact: true }).click();

  const details = page.getByRole('dialog', { name: customer });
  await details.getByLabel('Address', { exact: true }).fill('Kuwait City');
  await details.getByRole('button', { name: 'Save changes' }).click();

  await details.getByRole('button', { name: 'New follow-up' }).click();
  await details.getByLabel('Due date and time').fill(
    new Date(Date.now() + 2 * 86_400_000).toISOString().slice(0, 16));
  await details.getByLabel('Notes').fill('Playwright follow-up check');
  await details.getByRole('button', { name: 'Schedule follow-up' }).click();
  await details.getByRole('button', { name: 'History' }).click();
  await expect(details.getByText('Playwright follow-up check')).toBeVisible();
  await details.getByRole('button', { name: 'Close lead details' }).click();

  await page.getByLabel('Status for ' + customer).selectOption({ label: 'Contacted' });
  await page.getByRole('button', { name: customer, exact: true }).click();
  await expect(page.getByRole('dialog', { name: customer }).getByLabel('Address', { exact: true }))
    .toHaveValue('Kuwait City');
});

test('Follow-ups: edit, complete and filter tasks', async ({ page, request }) => {
  const name = 'Follow-up Customer ' + unique();
  const id = await createLead(request, name);
  const token = adminToken();
  const created = await request.post(apiUrl + '/api/leads/' + id + '/followups', {
    headers: { Authorization: 'Bearer ' + token },
    data: { dueAtUtc: new Date(Date.now() + 86_400_000).toISOString(), notes: 'Initial UI reminder' },
  });
  expect(created.ok()).toBeTruthy();

  await signIn(page);
  await page.getByRole('button', { name: 'Follow-ups', exact: true }).click();
  // The reminder panel can show the same follow-up; edit controls live in the task list.
  const taskList = page.locator('section.followup-panel').nth(1);
  const task = taskList.locator('.followup-row').filter({ hasText: 'Initial UI reminder' });
  await expect(task).toBeVisible();
  await task.getByRole('button', { name: 'Edit' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Next follow-up').fill(
    new Date(Date.now() + 3 * 86_400_000).toISOString().slice(0, 16));
  await dialog.getByLabel('Remarks').fill('Updated UI reminder');
  await dialog.getByRole('button', { name: 'Save changes' }).click();
  const updated = taskList.locator('.followup-row').filter({ hasText: name }).filter({ hasText: 'Updated UI reminder' });
  await expect(updated).toBeVisible();
  await updated.getByRole('button', { name: 'Complete' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Mark complete' }).click();
  await page.getByRole('tab', { name: 'Completed' }).click();
  await expect(taskList.locator('.followup-row').filter({ hasText: name }).filter({ hasText: 'Updated UI reminder' })).toBeVisible();
  await page.getByRole('tab', { name: 'Overdue' }).click();
  await expect(taskList.locator('.followup-row').filter({ hasText: name }).filter({ hasText: 'Updated UI reminder' })).toHaveCount(0);
});

test('Reports: all ten categories, filters and CSV export', async ({ page, request }) => {
  const name = 'Report Customer ' + unique();
  await createLead(request, name);
  await signIn(page);
  await page.getByRole('button', { name: 'Reports', exact: true }).click();

  const options = [
    'customers', 'user-customers', 'active', 'closed', 'not-interested',
    'followups', 'overdue', 'project', 'user-project', 'created',
  ];
  for (const type of options) {
    await page.locator('.table-toolbar label').first().locator('select').selectOption(type);
    await page.getByRole('button', { name: 'Run report' }).click();
    await expect(page.getByRole('heading', { name: /\d+ records/ })).toBeVisible();
  }

  await page.locator('.table-toolbar label').first().locator('select').selectOption('customers');
  await page.getByLabel('Lead given by').fill('Playwright QA');
  await page.getByRole('button', { name: 'Run report' }).click();
  await expect(page.getByRole('table').getByText(name)).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export CSV' }).click();
  expect((await download).suggestedFilename()).toBe('customers-report.csv');
});

test('Reports: pages are distinct and CSV includes all filtered rows', async ({ request }) => {
  const firstId = await createLead(request, 'Paged First ' + unique());
  const secondId = await createLead(request, 'Paged Second ' + unique());
  const headers = { Authorization: 'Bearer ' + adminToken() };
  const path = apiUrl + '/api/reports?type=customers&leadGivenBy=Playwright%20QA&pageSize=1';
  const first = await (await request.get(path + '&page=1', { headers })).json();
  const second = await (await request.get(path + '&page=2', { headers })).json();
  expect(first.total).toBeGreaterThanOrEqual(2);
  expect(first.rows).toHaveLength(1);
  expect(second.rows).toHaveLength(1);
  expect(first.rows[0].customerId).not.toBe(second.rows[0].customerId);
  const exportResponse = await request.get(apiUrl + '/api/reports/export?type=customers&leadGivenBy=Playwright%20QA', { headers });
  expect(exportResponse.status()).toBe(200);
  const csv = await exportResponse.text();
  expect(csv).toContain(String(firstId));
  expect(csv).toContain(String(secondId));
  expect(csv.trim().split(/\r?\n/)).toHaveLength(first.total + 1);
});

test('Team: create, manage, reset password and deactivate', async ({ page }) => {
  const email = 'team-' + unique() + '@example.test';
  await signIn(page);
  await page.getByRole('button', { name: 'Team', exact: true }).click();
  await page.getByRole('button', { name: 'Add employee' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add employee' });
  await dialog.getByLabel('Full name').fill('Playwright Employee');
  await dialog.getByLabel('Mobile').fill('+96555501234');
  await dialog.locator('input[type="email"]').fill(email);
  await dialog.getByLabel('Temporary password').fill(employeePassword);
  await dialog.getByRole('button', { name: 'Create employee' }).click();

  const row = page.getByRole('row').filter({ hasText: email });
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: 'Manage' }).click();
  const manage = page.getByRole('dialog', { name: 'Playwright Employee' });
  await manage.getByLabel('Full name').fill('Playwright Staff');
  await manage.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByRole('row').filter({ hasText: email })).toContainText('Playwright Staff');

  await page.getByRole('row').filter({ hasText: email })
    .getByRole('button', { name: 'Manage' }).click();
  await page.getByRole('dialog', { name: 'Playwright Staff' })
    .getByLabel('New password', { exact: true }).fill('ResetUiPassword123!');
  await page.getByRole('dialog', { name: 'Playwright Staff' })
    .getByLabel('Confirm new password').fill('ResetUiPassword123!');
  await page.getByRole('dialog', { name: 'Playwright Staff' })
    .getByRole('button', { name: 'Reset password' }).click();

  await page.getByRole('dialog', { name: 'Playwright Staff' })
    .getByRole('button', { name: 'Deactivate' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Deactivate' }).click();
  await expect(page.getByRole('row').filter({ hasText: email })).toContainText('Inactive');
});

test('Employee: permissions, password change and mobile navigation', async ({ page, request }, testInfo) => {
  const employee = await createEmployee(request, 'Employee');
  await signIn(page, employee.email, employeePassword);
  await expect(page.getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Team' })).toHaveCount(0);

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await expect(page.getByRole('button', { name: 'All leads', exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('mobile-navigation.png'), fullPage: true });
  await page.getByRole('button', { name: 'All leads', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Manage leads' })).toBeVisible();
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await page.getByRole('button', { name: 'Change password', exact: true }).click();
  await page.getByLabel('Current password').fill(employeePassword);
  await page.getByLabel('New password', { exact: true }).fill('ChangedUiPassword123!');
  await page.getByLabel('Confirm new password').fill('ChangedUiPassword123!');
  await page.getByRole('main').getByRole('button', { name: 'Change password' }).click();
  await expect(page.getByRole('heading', { name: 'Sign in to your account' })).toBeVisible();
  await signIn(page, employee.email, 'ChangedUiPassword123!');
});

test('Manager: team is read-only and employee creation is restricted', async ({ page, request }) => {
  const manager = await createEmployee(request, 'Manager');
  await signIn(page, manager.email, employeePassword);
  await page.getByRole('button', { name: 'Team', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Team members' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add employee' })).toHaveCount(0);
  await expect(page.locator('.error-state')).toHaveCount(0);
});


test('Keyboard: Login moves with Enter and forms reject invalid values', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Email address').fill(adminEmail);
  await page.getByLabel('Email address').press('Enter');
  await expect(page.locator('input[autocomplete="current-password"]')).toBeFocused();
  await page.getByRole('button', { name: 'Show password' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('input[autocomplete="current-password"]')).toHaveAttribute('type', 'text');

  await signIn(page);
  await page.getByRole('button', { name: 'All leads', exact: true }).click();
  await page.getByRole('button', { name: 'Add lead' }).focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Add a lead' });
  await expect(dialog.getByPlaceholder('Customer name')).toBeFocused();

  await dialog.getByPlaceholder('Customer name').fill('    ');
  await dialog.getByPlaceholder('+965 ...').fill('+96555501234');
  await dialog.getByRole('button', { name: 'Create lead' }).click();
  await expect(dialog.getByPlaceholder('Customer name')).toBeFocused();
  expect(await dialog.getByPlaceholder('Customer name').evaluate(
    (input: HTMLInputElement) => input.validity.customError)).toBe(true);

  await dialog.getByPlaceholder('Customer name').fill('Keyboard Test');
  await dialog.getByPlaceholder('Customer name').press('Enter');
  await expect(dialog.getByPlaceholder('+965 ...')).toBeFocused();
  await dialog.getByPlaceholder('+965 ...').fill('invalid');
  await dialog.getByRole('button', { name: 'Create lead' }).click();
  expect(await dialog.getByPlaceholder('+965 ...').evaluate(
    (input: HTMLInputElement) => input.validity.customError)).toBe(true);
  await dialog.getByPlaceholder('+965 ...').fill('+96555501011');
  await dialog.getByPlaceholder('+965 ...').press('Enter');
  await expect(dialog.getByPlaceholder('customer@example.com')).toBeFocused();

  await dialog.getByPlaceholder('Customer name').focus();
  await page.keyboard.press('Shift+Tab');
  await expect(dialog.getByRole('button', { name: 'Close form' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
});

test('Keyboard: Team validation, dialog focus, and password confirmation', async ({ page }) => {
  await signIn(page);
  await page.getByRole('button', { name: 'Team', exact: true }).click();
  await page.getByRole('button', { name: 'Add employee' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add employee' });
  await expect(dialog.getByLabel('Full name')).toBeFocused();
  await dialog.getByLabel('Full name').fill('  ');
  await dialog.locator('input[type="email"]').fill('invalid-email');
  expect(await dialog.locator('input[type="email"]').evaluate(
    (input: HTMLInputElement) => input.validity.typeMismatch)).toBe(true);
  await dialog.locator('input[type="email"]').fill('valid@example.test');
  await dialog.getByLabel('Temporary password').fill('password123');
  expect(await dialog.getByLabel('Temporary password').evaluate(
    (input: HTMLInputElement) => input.validity.patternMismatch)).toBe(true);
  await dialog.getByLabel('Temporary password').fill(employeePassword);
  await dialog.getByRole('button', { name: 'Create employee' }).click();
  await expect(dialog.getByLabel('Full name')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);

  await page.getByRole('button', { name: 'Change password', exact: true }).click();
  await page.getByLabel('Current password').fill('NotUsedHere123!');
  await page.getByLabel('New password', { exact: true }).fill('ValidNewPassword123!');
  await page.getByLabel('Confirm new password').fill('DifferentPassword123!');
  await page.locator('main.content').getByRole('button', { name: 'Change password' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Passwords do not match' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Change password' })).toBeVisible();
});

test('Reports: keyboard submit and custom date choice', async ({ page }) => {
  await signIn(page);
  await page.getByRole('button', { name: 'Reports', exact: true }).click();
  await page.getByLabel('From', { exact: true }).fill('2026-01-01');
  await page.getByLabel('Period').selectOption('custom');
  await expect(page.getByLabel('From', { exact: true })).toHaveValue('2026-01-01');
  await page.getByLabel('Lead given by').fill('Playwright QA');
  await page.getByLabel('Lead given by').press('Enter');
  await expect(page.getByLabel('Status', { exact: true })).toBeFocused();
  await page.getByRole('button', { name: 'Run report' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: /\d+ records/ })).toBeVisible();
});

test('Lead form rejects short names, bad phones, invalid email and amount boundaries', async ({ page }) => {
  await signIn(page);
  await page.getByRole('button', { name: 'All leads', exact: true }).click();
  await page.getByRole('button', { name: 'Add lead' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add a lead' });
  const name = dialog.getByPlaceholder('Customer name');
  const phone = dialog.getByPlaceholder('+965 ...');
  const email = dialog.locator('input[type="email"]');
  const amount = dialog.getByLabel('Project amount (KWD)');

  await name.fill('A');
  await expect.poll(() => name.evaluate((field: HTMLInputElement) => field.validity.tooShort))
    .toBe(true);
  await name.fill('A ');
  await phone.fill('+96555501001');
  await dialog.getByRole('button', { name: 'Create lead' }).click();
  await expect.poll(() => name.evaluate((field: HTMLInputElement) => field.validity.customError))
    .toBe(true);
  await name.fill('Validation Customer');
  await phone.fill('123456');
  await dialog.getByRole('button', { name: 'Create lead' }).click();
  await expect.poll(() => phone.evaluate((field: HTMLInputElement) => field.validity.customError))
    .toBe(true);

  await phone.fill('1234567890123456');
  await dialog.getByRole('button', { name: 'Create lead' }).click();
  await expect.poll(() => phone.evaluate((field: HTMLInputElement) => field.validity.customError))
    .toBe(true);
  await phone.fill('+96555501001');

  await email.fill('not-an-email');
  await expect.poll(() => email.evaluate((field: HTMLInputElement) => field.validity.typeMismatch))
    .toBe(true);
  await email.fill('valid@example.test');

  await amount.fill('-1');
  await expect.poll(() => amount.evaluate((field: HTMLInputElement) => field.validity.rangeUnderflow))
    .toBe(true);
  await amount.fill('1000000000000000');
  await dialog.getByRole('button', { name: 'Create lead' }).click();
  await expect.poll(() => amount.evaluate(
    (field: HTMLInputElement) => field.validity.rangeOverflow || field.validity.customError))
    .toBe(true);
  await amount.fill('10.0001');
  await expect.poll(() => amount.evaluate((field: HTMLInputElement) => field.validity.stepMismatch))
    .toBe(true);
  await amount.fill('10.125');
  await dialog.getByRole('button', { name: 'Create lead' }).click();
});

test('API rejects invalid lead and employee values even without browser validation', async ({ request }) => {
  const token = adminToken();
  const headers = { Authorization: 'Bearer ' + token };
  const lead = { name: 'Validation Sample', phone: '1234567', sourceId: 1 };
  for (const phone of ['1234567', '123456789012345']) {
    const accepted = await request.post(apiUrl + '/api/leads', {
      headers, data: { ...lead, name: 'Phone Boundary ' + phone, phone },
    });
    expect(accepted.status(), phone).toBe(201);
  }

  for (const data of [
    { ...lead, name: '  ' },
    { ...lead, name: 'A' },
    { ...lead, phone: '123456' },
    { ...lead, phone: '1234567890123456' },
    { ...lead, email: 'bad-email' },
    { ...lead, email: 'a'.repeat(250) + '@example.com' },
    { ...lead, projectAmount: -1 },
    { ...lead, projectAmount: 1000000000000000 },
  ]) {
    const response = await request.post(apiUrl + '/api/leads', { headers, data });
    expect(response.status(), JSON.stringify(data)).toBe(400);
  }

  const employee = {
    fullName: 'Validation Employee', email: 'valid-' + unique() + '@example.test',
    password: employeePassword, role: 'Employee',
  };
  for (const data of [
    { ...employee, fullName: '  ' },
    { ...employee, email: 'invalid' },
    { ...employee, phoneNumber: '+1234567890123456' },
    { ...employee, phoneNumber: '12345' },
    { ...employee, password: 'A1!' },
    { ...employee, password: 'A1!' + 'x'.repeat(126) },
  ]) {
    const response = await request.post(apiUrl + '/api/employees', { headers, data });
    expect(response.status(), JSON.stringify(data)).toBe(400);
  }
});

test('Employee UI and dashboard date fields use aligned constraints', async ({ page }) => {
  await signIn(page);
  await page.getByLabel('From date').fill('2026-10-20');
  await page.getByLabel('To date').fill('2026-10-10');
  await expect(page.getByLabel('From date')).toHaveValue('2026-10-10');
  await expect(page.getByLabel('To date')).toHaveValue('2026-10-10');

  await page.getByRole('button', { name: 'Team', exact: true }).click();
  await page.getByRole('button', { name: 'Add employee' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add employee' });
  await expect(dialog.locator('input[type="email"]')).toHaveAttribute('maxlength', '256');
  await expect(dialog.getByLabel('Mobile')).toHaveAttribute('maxlength', '30');
  await expect(dialog.getByLabel('Temporary password')).toHaveAttribute('maxlength', '128');
});

test('API protects edit, follow-up, password and date filter boundaries', async ({ request }) => {
  const token = adminToken();
  const headers = { Authorization: 'Bearer ' + token };
  const leadId = await createLead(request, 'Validation Edit Customer');
  const employee = await createEmployee(request, 'Employee');

  const invalidLead = await request.put(apiUrl + '/api/leads/' + leadId, {
    headers,
    data: { name: 'Updated Customer', phone: '1234567890123456', sourceId: 1 },
  });
  expect(invalidLead.status()).toBe(400);

  const invalidUser = await request.put(apiUrl + '/api/admin/users/' + employee.id, {
    headers,
    data: { fullName: '  ', phoneNumber: '+96555501234', role: 'Employee' },
  });
  expect(invalidUser.status()).toBe(400);

  const invalidMobile = await request.put(apiUrl + '/api/admin/users/' + employee.id, {
    headers,
    data: { fullName: 'Valid Employee', phoneNumber: '1234567890123456', role: 'Employee' },
  });
  expect(invalidMobile.status()).toBe(400);

  const duplicateEmail = await request.post(apiUrl + '/api/employees', {
    headers,
    data: { fullName: 'Duplicate Employee', email: employee.email,
      password: employeePassword, role: 'Employee' },
  });
  expect(duplicateEmail.status()).toBe(400);

  const longPassword = await request.post(apiUrl + '/api/password/reset/' + employee.id, {
    headers, data: { newPassword: 'A1!' + 'x'.repeat(126) },
  });
  expect(longPassword.status()).toBe(400);

  for (const data of [
    { dueAtUtc: new Date(Date.now() - 86_400_000).toISOString(), notes: 'Check date' },
    { dueAtUtc: new Date(Date.now() + 86_400_000).toISOString(), notes: '  ' },
    { dueAtUtc: new Date(Date.now() + 86_400_000).toISOString(), notes: 'x'.repeat(2001) },
  ]) {
    const result = await request.post(apiUrl + '/api/leads/' + leadId + '/followups', {
      headers, data,
    });
    expect(result.status(), JSON.stringify(data).slice(0, 120)).toBe(400);
  }

  for (const path of [
    '/api/leads?search=' + 'x'.repeat(151),
    '/api/reports?leadGivenBy=' + 'x'.repeat(151),
    '/api/leads?from=2026-10-12&to=2026-10-11',
  ]) {
    const invalidFilter = await request.get(apiUrl + path, { headers });
    expect(invalidFilter.status(), path.slice(0, 90)).toBe(400);
  }

  for (const path of ['dashboard', 'reports']) {
    const invalidDates = await request.get(
      apiUrl + '/api/' + path + '?from=2026-10-12&to=2026-10-11', { headers });
    expect(invalidDates.status()).toBe(400);
  }
});

test('Development: convert a won lead, assign tasks and complete progress', async ({ page, request }) => {
  const title = 'Client Portal ' + unique();
  const taskTitle = 'Build login page ' + unique();
  const leadId = await createLead(request, title);
  const headers = { Authorization: 'Bearer ' + adminToken() };
  const won = await request.patch(apiUrl + '/api/leads/' + leadId + '/status', {
    headers, data: { statusId: 5 },
  });
  expect(won.ok()).toBeTruthy();

  await signIn(page);
  await page.getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Development projects' }).click();
  await expect(page.locator('main.content').getByRole('heading', { name: 'Development projects', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'New project' }).click();
  const form = page.getByRole('dialog', { name: 'New development project' });
  await form.getByRole('combobox', { name: /^Won lead/ }).selectOption(String(leadId));
  await form.getByLabel('Project name').fill(title);
  await form.getByRole('button', { name: 'Create project' }).click();

  const details = page.getByRole('dialog', { name: 'Project ' + title });
  await expect(details).toBeVisible();
  await details.getByText('Add development task').click();
  await details.getByLabel('Task title').fill(taskTitle);
  await details.getByLabel('Priority').selectOption('High');
  await details.getByRole('button', { name: 'Add task' }).click();
  await expect(details.locator('.project-task').getByText(taskTitle, { exact: true })).toBeVisible();
  await details.getByRole('button', { name: 'Edit task' }).click();
  await details.getByLabel('Priority').selectOption('Urgent');
  await details.getByRole('button', { name: 'Save task' }).click();
  await expect(details.locator('.project-task').filter({ hasText: taskTitle })).toContainText('Urgent');
  await details.getByLabel('Status for ' + taskTitle).selectOption('Done');
  await expect(details.getByText('1/1 tasks done')).toBeVisible();

  await details.getByText('Edit project details').click();
  await details.getByLabel('Project status').selectOption('Completed');
  await details.getByRole('button', { name: 'Save project' }).click();
  await expect(details.locator('.drawer-header')).toContainText('Completed');
  await details.getByRole('button', { name: 'Close' }).click();
  await expect(page.getByRole('row').filter({ hasText: title })).toContainText('100%');
});

test('Development API validates winning status, project access, tasks and duplicate conversion', async ({ request }) => {
  const headers = { Authorization: 'Bearer ' + adminToken() };
  const freshLeadId = await createLead(request, 'Unqualified Project ' + unique());
  const projectPayload = {
    leadId: freshLeadId, title: 'Unqualified conversion', ownerId: adminSession().user.id,
  };
  const invalid = await request.post(apiUrl + '/api/projects', { headers, data: projectPayload });
  expect(invalid.status()).toBe(400);

  const updated = await request.patch(apiUrl + '/api/leads/' + freshLeadId + '/status', {
    headers, data: { statusId: 5 },
  });
  expect(updated.ok()).toBeTruthy();
  const created = await request.post(apiUrl + '/api/projects', { headers, data: projectPayload });
  expect(created.status()).toBe(201);
  const projectId = (await created.json()).id;
  const again = await request.post(apiUrl + '/api/projects', { headers, data: projectPayload });
  expect(again.status()).toBe(409);

  const task = await request.post(apiUrl + '/api/projects/' + projectId + '/tasks', {
    headers, data: { title: 'Configure database', priority: 'High' },
  });
  expect(task.status()).toBe(201);
  const taskId = (await task.json()).id;
  const invalidTask = await request.post(apiUrl + '/api/projects/' + projectId + '/tasks', {
    headers, data: { title: 'x', priority: 'Invalid' },
  });
  expect(invalidTask.status()).toBe(400);
  const completedTooEarly = await request.put(apiUrl + '/api/projects/' + projectId, {
    headers, data: { title: 'Unqualified conversion', status: 'Completed',
      ownerId: adminSession().user.id },
  });
  expect(completedTooEarly.status()).toBe(400);
  const finished = await request.patch(apiUrl + '/api/projects/' + projectId +
    '/tasks/' + taskId + '/status', { headers, data: { status: 'Done' } });
  expect(finished.status()).toBe(204);

  const employee = await createEmployee(request, 'Employee');
  const login = await request.post(apiUrl + '/api/auth/login', {
    data: { email: employee.email, password: employeePassword },
  });
  expect(login.status()).toBe(200);
  const employeeToken = (await login.json()).token;
  const invisible = await request.get(apiUrl + '/api/projects/' + projectId, {
    headers: { Authorization: 'Bearer ' + employeeToken },
  });
  expect(invisible.status()).toBe(404);
});

test('Development permissions: employee can update own task, not another task', async ({ request }) => {
  const adminHeaders = { Authorization: 'Bearer ' + adminToken() };
  const employee = await createEmployee(request, 'Employee');
  const login = await request.post(apiUrl + '/api/auth/login', {
    data: { email: employee.email, password: employeePassword },
  });
  expect(login.status()).toBe(200);
  const employeeHeaders = { Authorization: 'Bearer ' + (await login.json()).token };

  const leadId = await createLead(request, 'Assigned Dev Project ' + unique());
  await request.patch(apiUrl + '/api/leads/' + leadId + '/status', {
    headers: adminHeaders, data: { statusId: 5 },
  });
  const created = await request.post(apiUrl + '/api/projects', {
    headers: adminHeaders,
    data: { leadId, title: 'Developer Access Test', ownerId: adminSession().user.id },
  });
  expect(created.status()).toBe(201);
  const id = (await created.json()).id;
  const assigned = await request.post(apiUrl + '/api/projects/' + id + '/tasks', {
    headers: adminHeaders,
    data: { title: 'Assigned development task', priority: 'Medium', assignedToId: employee.id },
  });
  expect(assigned.status()).toBe(201);
  const assignedId = (await assigned.json()).id;
  const privateTask = await request.post(apiUrl + '/api/projects/' + id + '/tasks', {
    headers: adminHeaders, data: { title: 'Another developer task', priority: 'Low' },
  });
  expect(privateTask.status()).toBe(201);
  const privateId = (await privateTask.json()).id;

  const visible = await request.get(apiUrl + '/api/projects/' + id, { headers: employeeHeaders });
  expect(visible.status()).toBe(200);

  const allowed = await request.patch(apiUrl + '/api/projects/' + id + '/tasks/' + assignedId + '/status', {
    headers: employeeHeaders, data: { status: 'InProgress' },
  });
  expect(allowed.status()).toBe(204);

  const forbidden = await request.patch(apiUrl + '/api/projects/' + id + '/tasks/' + privateId + '/status', {
    headers: employeeHeaders, data: { status: 'Done' },
  });
  expect(forbidden.status()).toBe(403);

  const cannotCreate = await request.post(apiUrl + '/api/projects/' + id + '/tasks', {
    headers: employeeHeaders, data: { title: 'Unapproved task', priority: 'High' },
  });
  expect(cannotCreate.status()).toBe(403);
});

test('Development activity: bug tracking and daily worklog are usable from keyboard-friendly UI', async ({ page, request }) => {
  const title = 'QA Activity Project ' + unique();
  const leadId = await createLead(request, title);
  const headers = { Authorization: 'Bearer ' + adminToken() };
  await request.patch(apiUrl + '/api/leads/' + leadId + '/status', { headers, data: { statusId: 5 } });
  const project = await request.post(apiUrl + '/api/projects', {
    headers, data: { leadId, title, ownerId: adminSession().user.id },
  });
  expect(project.status()).toBe(201);

  await signIn(page);
  await page.getByRole('button', { name: 'Development projects' }).click();
  await page.getByRole('row').filter({ hasText: title }).getByRole('button', { name: 'View project' }).click();
  const details = page.getByRole('dialog', { name: 'Project ' + title });
  await expect(details.getByRole('heading', { name: 'Bug tracking (0 open)' })).toBeVisible();

  await details.getByText('Report a bug', { exact: true }).click();
  await details.getByLabel('Bug title').fill('Login form breaks on mobile');
  await details.getByLabel('What went wrong?').fill('Submit button overlaps the fields.');
  await details.getByLabel('Severity').selectOption('High');
  await details.getByRole('button', { name: 'Report bug' }).click();
  await expect(details.getByText('Login form breaks on mobile')).toBeVisible();
  await expect(details.getByRole('heading', { name: 'Bug tracking (1 open)' })).toBeVisible();

  await details.getByText("Log today's work").click();
  await details.getByLabel('Hours worked').fill('1.25');
  await details.getByLabel('Work summary').fill('Reproduced the login bug and investigated CSS.');
  await details.getByRole('button', { name: 'Save worklog' }).click();
  await expect(details.getByText('Reproduced the login bug and investigated CSS.')).toBeVisible();
  await expect(details.getByRole('heading', { name: /Daily worklogs.*1.25 hours/ })).toBeVisible();

  await details.getByLabel('Bug status for Login form breaks on mobile').selectOption('Closed');
  await expect(details.getByRole('heading', { name: 'Bug tracking (0 open)' })).toBeVisible();
});

test('Development activity API: validate hours, assign bugs and enforce project security', async ({ request }) => {
  const admin = { Authorization: 'Bearer ' + adminToken() };
  const developer = await createEmployee(request, 'Employee');
  const login = await request.post(apiUrl + '/api/auth/login', {
    data: { email: developer.email, password: employeePassword },
  });
  expect(login.status()).toBe(200);
  const user = { Authorization: 'Bearer ' + (await login.json()).token };
  const leadId = await createLead(request, 'Worklog API ' + unique());
  await request.patch(apiUrl + '/api/leads/' + leadId + '/status', { headers: admin, data: { statusId: 5 } });
  const p = await request.post(apiUrl + '/api/projects', {
    headers: admin, data: { leadId, title: 'Development Activity API', ownerId: adminSession().user.id },
  });
  expect(p.status()).toBe(201);
  const id = (await p.json()).id as number;
  const task = await request.post(apiUrl + '/api/projects/' + id + '/tasks', {
    headers: admin, data: { title: 'Fix dashboard', priority: 'High', assignedToId: developer.id },
  });
  expect(task.status()).toBe(201);
  const taskId = (await task.json()).id as number;
  const issue = await request.post(apiUrl + '/api/projects/' + id + '/bugs', {
    headers: admin, data: {
      title: 'Dashboard count incorrect', severity: 'Critical',
      assignedToId: developer.id, taskId,
    },
  });
  expect(issue.status()).toBe(201);
  const bugId = (await issue.json()).id as number;
  const base = apiUrl + '/api/projects/' + id;
  const today = new Date().toISOString().slice(0, 10);

  const visible = await request.get(base + '/bugs', { headers: user });
  expect(visible.status()).toBe(200);
  expect((await visible.json()).items[0].title).toBe('Dashboard count incorrect');

  const forbiddenAssign = await request.post(base + '/bugs', {
    headers: user, data: { title: 'Another bug', severity: 'High', assignedToId: developer.id },
  });
  expect(forbiddenAssign.status()).toBe(403);
  const ownReport = await request.post(base + '/bugs', {
    headers: user, data: { title: 'Unexpected empty page', severity: 'Low' },
  });
  expect(ownReport.status()).toBe(201);

  const ownUpdate = await request.patch(base + '/bugs/' + bugId + '/status', {
    headers: user, data: { status: 'Resolved' },
  });
  expect(ownUpdate.status()).toBe(204);
  const cannotClose = await request.patch(base + '/bugs/' + bugId + '/status', {
    headers: user, data: { status: 'Closed' },
  });
  expect(cannotClose.status()).toBe(403);

  const logPayload = { workDate: today, hours: 1.25, taskId, description: 'Investigated and fixed count query.' };
  const logged = await request.post(base + '/worklogs', { headers: user, data: logPayload });
  expect(logged.status()).toBe(201);
  const logs = await request.get(base + '/worklogs', { headers: admin });
  expect(logs.status()).toBe(200);
  const data = await logs.json();
  expect(data.totalHours).toBe(1.25);
  expect(data.items[0].developer).toBe('UI Employee');

  for (const data of [
    { ...logPayload, hours: 0.1 },
    { ...logPayload, hours: 25 },
    { ...logPayload, hours: 1.234 },
    { ...logPayload, workDate: '2099-01-01' },
    { ...logPayload, description: ' ' },
    { ...logPayload, taskId: 987654321 },
  ]) {
    const invalid = await request.post(base + '/worklogs', { headers: user, data });
    expect(invalid.status(), JSON.stringify(data)).toBe(400);
  }
  const tooMuch = await request.post(base + '/worklogs', {
    headers: user, data: { ...logPayload, hours: 23 },
  });
  expect(tooMuch.status()).toBe(400);

  await request.patch(base + '/tasks/' + taskId + '/status', {
    headers: admin, data: { status: 'Done' },
  });
  const cannotFinish = await request.put(base, {
    headers: admin,
    data: { title: 'Development Activity API', status: 'Completed', ownerId: adminSession().user.id },
  });
  expect(cannotFinish.status()).toBe(400);

  const closed = await request.patch(base + '/bugs/' + bugId + '/status', {
    headers: admin, data: { status: 'Closed' },
  });
  expect(closed.status()).toBe(204);
  const newBugId = (await ownReport.json()).id as number;
  const closedOther = await request.patch(base + '/bugs/' + newBugId + '/status', {
    headers: admin, data: { status: 'Closed' },
  });
  expect(closedOther.status()).toBe(204);
  const completed = await request.put(base, {
    headers: admin,
    data: { title: 'Development Activity API', status: 'Completed', ownerId: adminSession().user.id },
  });
  expect(completed.status()).toBe(204);
  const afterComplete = await request.post(base + '/worklogs', { headers: user, data: logPayload });
  expect(afterComplete.status()).toBe(400);
});

test('Development permissions: a bug-only assignee can access their project, not close the issue', async ({ request }) => {
  const admin = { Authorization: 'Bearer ' + adminToken() };
  const employee = await createEmployee(request, 'Employee');
  const session = await request.post(apiUrl + '/api/auth/login', {
    data: { email: employee.email, password: employeePassword },
  });
  expect(session.status()).toBe(200);
  const developer = { Authorization: 'Bearer ' + (await session.json()).token };
  const leadId = await createLead(request, 'Bug Only Project ' + unique());
  await request.patch(apiUrl + '/api/leads/' + leadId + '/status', {
    headers: admin, data: { statusId: 5 },
  });
  const result = await request.post(apiUrl + '/api/projects', {
    headers: admin,
    data: { leadId, title: 'Issue Ownership Test', ownerId: adminSession().user.id },
  });
  expect(result.status()).toBe(201);
  const projectId = (await result.json()).id as number;
  const base = apiUrl + '/api/projects/' + projectId;
  expect((await request.get(base, { headers: developer })).status()).toBe(404);

  const reported = await request.post(base + '/bugs', {
    headers: admin,
    data: { title: 'Bug assigned without task', severity: 'High', assignedToId: employee.id },
  });
  expect(reported.status()).toBe(201);
  const bugId = (await reported.json()).id as number;
  expect((await request.get(base, { headers: developer })).status()).toBe(200);
  const started = await request.patch(base + '/bugs/' + bugId + '/status', {
    headers: developer, data: { status: 'InProgress' },
  });
  expect(started.status()).toBe(204);
  const forbidden = await request.patch(base + '/bugs/' + bugId + '/status', {
    headers: developer, data: { status: 'Closed' },
  });
  expect(forbidden.status()).toBe(403);
  const directEdit = await request.put(base + '/bugs/' + bugId, {
    headers: developer,
    data: { title: 'Change severity illegally', severity: 'Critical', status: 'Closed' },
  });
  expect(directEdit.status()).toBe(403);

  const time = await request.post(base + '/worklogs', {
    headers: developer,
    data: { workDate: new Date().toISOString().slice(0, 10), hours: 0.5,
      description: 'Investigated bug assignment and reproduced it.' },
  });
  expect(time.status()).toBe(201);
  const personal = await request.get(base + '/worklogs', { headers: developer });
  expect(personal.status()).toBe(200);
  expect((await personal.json()).totalHours).toBe(0.5);
});

test('Planning API: milestones, sprints, task links and completion gates', async ({ request }) => {
  const headers = { Authorization: 'Bearer ' + adminToken() };
  const leadId = await createLead(request, 'Milestone QA ' + unique());
  await request.patch(apiUrl + '/api/leads/' + leadId + '/status', { headers, data: { statusId: 5 } });
  const created = await request.post(apiUrl + '/api/projects', {
    headers,
    data: { leadId, title: 'Planning Integration', ownerId: adminSession().user.id,
      startDate: '2026-10-20', dueDate: '2026-11-15' },
  });
  expect(created.status()).toBe(201);
  const id = (await created.json()).id as number;
  const base = apiUrl + '/api/projects/' + id;
  const plan = base + '/planning';

  const invalid = await request.post(plan + '/sprints', {
    headers, data: { title: 'Invalid dates', status: 'Planned',
      startDate: '2026-11-10', dueDate: '2026-10-20' },
  });
  expect(invalid.status()).toBe(400);
  const badDeadline = await request.post(plan + '/milestones', {
    headers, data: { title: 'Too late', status: 'Planned', dueDate: '2026-12-01' },
  });
  expect(badDeadline.status()).toBe(400);

  const m = await request.post(plan + '/milestones', {
    headers, data: { title: 'Core module', status: 'Planned', dueDate: '2026-11-10' },
  });
  expect(m.status()).toBe(201);
  const milestoneId = (await m.json()).id as number;
  const sp = await request.post(plan + '/sprints', {
    headers, data: { title: 'Sprint One', goal: 'Authentication ready', status: 'Planned',
      startDate: '2026-10-20', dueDate: '2026-10-30' },
  });
  expect(sp.status()).toBe(201);
  const sprintId = (await sp.json()).id as number;
  const task = await request.post(base + '/tasks', {
    headers, data: { title: 'Implement authorization', priority: 'High',
      dueDate: '2026-10-29', milestoneId, sprintId },
  });
  expect(task.status()).toBe(201);
  const taskId = (await task.json()).id as number;
  const info = await request.get(plan, { headers });
  expect(info.status()).toBe(200);
  const planning = await info.json();
  expect(planning.milestones[0].taskCount).toBe(1);
  expect(planning.sprints[0].taskCount).toBe(1);

  const unfinishedSprint = await request.put(plan + '/sprints/' + sprintId, {
    headers, data: { title: 'Sprint One', status: 'Completed',
      startDate: '2026-10-20', dueDate: '2026-10-30' },
  });
  expect(unfinishedSprint.status()).toBe(400);
  const cannotFinish = await request.put(base, {
    headers,
    data: { title: 'Planning Integration', ownerId: adminSession().user.id, status: 'Completed',
      startDate: '2026-10-20', dueDate: '2026-11-15' },
  });
  expect(cannotFinish.status()).toBe(400);
  await request.patch(base + '/tasks/' + taskId + '/status', {
    headers, data: { status: 'Done' },
  });

  const finishSprint = await request.put(plan + '/sprints/' + sprintId, {
    headers, data: { title: 'Sprint One', status: 'Completed',
      startDate: '2026-10-20', dueDate: '2026-10-30' },
  });
  expect(finishSprint.status()).toBe(204);
  const finishMilestone = await request.put(plan + '/milestones/' + milestoneId, {
    headers, data: { title: 'Core module', status: 'Completed', dueDate: '2026-11-10' },
  });
  expect(finishMilestone.status()).toBe(204);
  const nowDone = await request.put(base, {
    headers, data: { title: 'Planning Integration', ownerId: adminSession().user.id,
      status: 'Completed', startDate: '2026-10-20', dueDate: '2026-11-15' },
  });
  expect(nowDone.status()).toBe(204);
});

test('Planning UI: create milestones and sprints and assign a development task', async ({ page, request }) => {
  const title = 'Planning UI ' + unique();
  const leadId = await createLead(request, title);
  const headers = { Authorization: 'Bearer ' + adminToken() };
  await request.patch(apiUrl + '/api/leads/' + leadId + '/status', { headers, data: { statusId: 5 } });
  const project = await request.post(apiUrl + '/api/projects', {
    headers, data: { leadId, title, ownerId: adminSession().user.id },
  });
  expect(project.status()).toBe(201);

  await signIn(page);
  await page.getByRole('button', { name: 'Development projects' }).click();
  await page.getByRole('row').filter({ hasText: title }).getByRole('button', { name: 'View project' }).click();
  const details = page.getByRole('dialog', { name: 'Project ' + title });
  await details.getByText('Add milestone', { exact: true }).click();
  await details.getByLabel('Milestone title').fill('MVP release');
  await details.getByLabel('Milestone deadline').fill('2026-11-15');
  await details.getByRole('button', { name: 'Create milestone' }).click();
  await expect(details.locator('.project-task').getByText('MVP release', { exact: true })).toBeVisible();

  await details.getByText('Plan sprint', { exact: true }).click();
  await details.getByLabel('Sprint title').fill('Sprint Alpha');
  await details.getByLabel('Sprint start').fill('2026-10-20');
  await details.getByLabel('Sprint end').fill('2026-10-27');
  await details.getByRole('button', { name: 'Create sprint' }).click();
  await expect(details.locator('.project-task').getByText('Sprint Alpha', { exact: true })).toBeVisible();

  await details.getByText('Add development task').click();
  await details.getByLabel('Task title').fill('Deliver sprint tasks');
  await details.getByLabel('Priority').selectOption('High');
  await details.getByLabel('Milestone', { exact: true }).selectOption({ label: 'MVP release' });
  await details.getByLabel('Sprint', { exact: true }).selectOption({ label: 'Sprint Alpha' });
  await details.getByRole('button', { name: 'Add task' }).click();
  await expect(details.locator('.project-task').getByText('Deliver sprint tasks', { exact: true })).toBeVisible();
  await expect(details.getByText('0/1 tasks (0%)').first()).toBeVisible();
  await details.getByLabel('Status for Deliver sprint tasks').selectOption('Done');
  await expect(details.getByText('1/1 tasks (100%)').first()).toBeVisible();
});

test('Phase 4: audit history is paginated and restricted to administrators', async ({ page, request }) => {
  const name = 'Audit QA ' + unique();
  await createLead(request, name);
  const admin = { Authorization: 'Bearer ' + adminToken() };
  const entries = await request.get(apiUrl + '/api/admin/audit?page=1&pageSize=5&table=Leads', { headers: admin });
  expect(entries.status()).toBe(200);
  const result = await entries.json();
  expect(result.pageSize).toBe(5);
  expect(result.items.length).toBeLessThanOrEqual(5);
  expect(result.items.some((entry: { tableName: string }) => entry.tableName === 'Leads')).toBe(true);

  const invalid = await request.get(apiUrl + '/api/admin/audit?pageSize=501', { headers: admin });
  expect(invalid.status()).toBe(400);
  const employee = await createEmployee(request, 'Employee');
  const signedIn = await request.post(apiUrl + '/api/auth/login', {
    data: { email: employee.email, password: employeePassword },
  });
  expect(signedIn.status()).toBe(200);
  const staffToken = (await signedIn.json()).token;
  const denied = await request.get(apiUrl + '/api/admin/audit', {
    headers: { Authorization: 'Bearer ' + staffToken },
  });
  expect(denied.status()).toBe(403);

  await signIn(page);
  await page.getByRole('button', { name: 'Audit history' }).click();
  await expect(page.getByRole('heading', { name: 'Audit history' })).toBeVisible();
  await page.getByLabel('Module').fill('Leads');
  await page.getByRole('button', { name: 'Search audit' }).click();
  await expect(page.getByRole('heading', { name: /audit entries/ })).toBeVisible();
});

test('Phase 4: browser cookie requires CSRF for writes and logout clears session', async ({ request }) => {
  const login = await request.post(apiUrl + '/api/auth/login', {
    data: { email: adminEmail, password: adminPassword },
  });
  expect(login.status()).toBe(200);
  const setCookie = login.headers()['set-cookie'] || '';
  expect(setCookie).toContain('CRM.Session=');
  expect(setCookie).toMatch(/httponly/i);
  expect(setCookie.toLowerCase()).toContain('samesite=strict');
  const me = await request.get(apiUrl + '/api/auth/me');
  expect(me.status()).toBe(200);
  const current = await me.json();
  expect(typeof current.csrfToken).toBe('string');
  const lead = { name: 'Cookie CSRF Test ' + unique(), phone: '+96555501111', sourceId: 1 };
  const missingToken = await request.post(apiUrl + '/api/leads', { data: lead });
  expect(missingToken.status()).toBe(400);
  const allowed = await request.post(apiUrl + '/api/leads', {
    data: lead, headers: { 'X-CSRF-TOKEN': current.csrfToken },
  });
  expect(allowed.status()).toBe(201);
  const logoutWithoutCsrf = await request.post(apiUrl + '/api/auth/logout');
  expect(logoutWithoutCsrf.status()).toBe(400);
  expect((await request.get(apiUrl + '/api/auth/me')).status()).toBe(200);
  const logout = await request.post(apiUrl + '/api/auth/logout', {
    headers: { 'X-CSRF-TOKEN': current.csrfToken },
  });
  expect(logout.status()).toBe(204);
  expect((await request.get(apiUrl + '/api/auth/me')).status()).toBe(401);
});

test('Phase 4: role permission override is enforced by API and cannot escalate existing roles', async ({ page, request }) => {
  const admin = { Authorization: 'Bearer ' + adminToken() };
  const staff = await createEmployee(request, 'Manager');
  const login = await request.post(apiUrl + '/api/auth/login', {
    data: { email: staff.email, password: employeePassword },
  });
  expect(login.status()).toBe(200);
  const manager = { Authorization: 'Bearer ' + (await login.json()).token };
  const route = apiUrl + '/api/admin/permissions/Manager/reports.view';

  expect((await request.get(apiUrl + '/api/admin/permissions', { headers: manager })).status()).toBe(403);
  expect((await request.get(apiUrl + '/api/reports', { headers: manager })).status()).toBe(200);
  try {
    const blocked = await request.put(route, { headers: admin, data: { enabled: false } });
    expect(blocked.status()).toBe(204);
    expect((await request.get(apiUrl + '/api/reports', { headers: manager })).status()).toBe(403);
    const others = await request.get(apiUrl + '/api/leads', { headers: manager });
    expect(others.status()).toBe(200);
  } finally {
    const restored = await request.put(route, { headers: admin, data: { enabled: true } });
    expect(restored.status()).toBe(204);
  }
  expect((await request.get(apiUrl + '/api/reports', { headers: manager })).status()).toBe(200);
  const cannotElevate = await request.put(
    apiUrl + '/api/admin/permissions/Employee/leads.assign',
    { headers: admin, data: { enabled: true } },
  );
  expect(cannotElevate.status()).toBe(400);

  await signIn(page);
  await page.getByRole('button', { name: 'Role permissions' }).click();
  await expect(page.getByRole('heading', { name: 'Role permissions' })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Manager reports.view' })).toBeChecked();
  await expect(page.getByRole('checkbox', { name: 'Employee leads.assign' })).toBeDisabled();
});
