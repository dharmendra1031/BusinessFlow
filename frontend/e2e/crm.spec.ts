import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

const apiUrl = 'http://localhost:5081';
const adminEmail = process.env.CRM_ADMIN_EMAIL!;
const adminPassword = process.env.CRM_ADMIN_PASSWORD!;
const employeePassword = 'EmployeeUiTest123!';
const unique = () => Date.now().toString();

async function signIn(page: Page, email = adminEmail, password = adminPassword) {
  await page.goto('/');
  await page.getByLabel('Email address').fill(email);
  await page.locator('input[autocomplete="current-password"]').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Good to see you!' })).toBeVisible();
}

async function adminToken(request: APIRequestContext): Promise<string> {
  const response = await request.post(apiUrl + '/api/auth/login', {
    data: { email: adminEmail, password: adminPassword },
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()).token as string;
}

async function createLead(request: APIRequestContext, name: string): Promise<number> {
  const token = await adminToken(request);
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
  const token = await adminToken(request);
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
  await page.locator('input[autocomplete="current-password"]').fill(adminPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();
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
  await expect(page.getByRole('status').filter({ hasText: 'Lead created' })).toBeVisible();

  await page.getByLabel('Search leads').fill(customer);
  await expect(page.getByRole('button', { name: customer, exact: true })).toBeVisible();
  await page.getByRole('button', { name: customer, exact: true }).click();

  const details = page.getByRole('dialog', { name: customer });
  await details.getByLabel('Address').fill('Kuwait City');
  await details.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Changes saved' })).toBeVisible();

  await details.getByRole('button', { name: 'New follow-up' }).click();
  await details.getByLabel('Due date and time').fill(
    new Date(Date.now() + 2 * 86_400_000).toISOString().slice(0, 16));
  await details.getByLabel('Notes').fill('Playwright follow-up check');
  await details.getByRole('button', { name: 'Schedule follow-up' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Follow-up scheduled' })).toBeVisible();
  await details.getByRole('button', { name: 'History' }).click();
  await expect(details.getByText('Playwright follow-up check')).toBeVisible();
  await details.getByRole('button', { name: 'Close lead details' }).click();

  await page.getByLabel('Status for ' + customer).selectOption({ label: 'Contacted' });
  await expect(page.getByRole('status').filter({ hasText: 'Status updated' })).toBeVisible();
  await page.getByRole('button', { name: customer, exact: true }).click();
  await expect(page.getByRole('dialog', { name: customer }).getByLabel('Address'))
    .toHaveValue('Kuwait City');
});

test('Follow-ups: edit, complete and filter tasks', async ({ page, request }) => {
  const name = 'Follow-up Customer ' + unique();
  const id = await createLead(request, name);
  const token = await adminToken(request);
  const created = await request.post(apiUrl + '/api/leads/' + id + '/followups', {
    headers: { Authorization: 'Bearer ' + token },
    data: { dueAtUtc: new Date(Date.now() + 86_400_000).toISOString(), notes: 'Initial UI reminder' },
  });
  expect(created.ok()).toBeTruthy();

  await signIn(page);
  await page.getByRole('button', { name: 'Follow-ups', exact: true }).click();
  const task = page.locator('.followup-row').filter({ hasText: 'Initial UI reminder' });
  await expect(task).toBeVisible();
  await task.getByRole('button', { name: 'Edit' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Next follow-up').fill(
    new Date(Date.now() + 3 * 86_400_000).toISOString().slice(0, 16));
  await dialog.getByLabel('Remarks').fill('Updated UI reminder');
  await dialog.getByRole('button', { name: 'Save changes' }).click();
  const updated = page.locator('.followup-row').filter({ hasText: 'Updated UI reminder' });
  await expect(updated).toBeVisible();
  await updated.getByRole('button', { name: 'Complete' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Mark complete' }).click();
  await page.getByRole('tab', { name: 'Completed' }).click();
  await expect(page.locator('.followup-row').filter({ hasText: 'Updated UI reminder' })).toBeVisible();
  await page.getByRole('tab', { name: 'Overdue' }).click();
  await expect(page.locator('.followup-row').filter({ hasText: 'Updated UI reminder' })).toHaveCount(0);
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
    await page.getByLabel('Report', { exact: true }).selectOption(type);
    await page.getByRole('button', { name: 'Run report' }).click();
    await expect(page.getByRole('heading', { name: /\d+ records/ })).toBeVisible();
  }

  await page.getByLabel('Report', { exact: true }).selectOption('customers');
  await page.getByLabel('Lead given by').fill('Playwright QA');
  await page.getByRole('button', { name: 'Run report' }).click();
  await expect(page.getByRole('table').getByText(name)).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export CSV' }).click();
  expect((await download).suggestedFilename()).toBe('customers-report.csv');
});

test('Team: create, manage, reset password and deactivate', async ({ page }) => {
  const email = 'team-' + unique() + '@example.test';
  await signIn(page);
  await page.getByRole('button', { name: 'Team', exact: true }).click();
  await page.getByRole('button', { name: 'Add employee' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add employee' });
  await dialog.getByLabel('Full name').fill('Playwright Employee');
  await dialog.getByLabel('Mobile').fill('+96555501234');
  await dialog.getByLabel('Email', { exact: true }).fill(email);
  await dialog.getByLabel('Temporary password').fill(employeePassword);
  await dialog.getByRole('button', { name: 'Create employee' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Employee created' })).toBeVisible();

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
    .getByLabel('New password').fill('ResetUiPassword123!');
  await page.getByRole('dialog', { name: 'Playwright Staff' })
    .getByRole('button', { name: 'Reset password' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Password reset' })).toBeVisible();

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
  await page.getByLabel('New password').fill('ChangedUiPassword123!');
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
