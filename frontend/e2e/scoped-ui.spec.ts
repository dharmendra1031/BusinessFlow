import { expect, test } from './fixtures';

const apiUrl = 'http://localhost:5081';
const password = 'EmployeeUiTest123!';
const headers = () => ({ Authorization: 'Bearer ' + process.env.E2E_ADMIN_TOKEN! });
const unique = () => Date.now() + '-' + Math.floor(Math.random() * 100000);

async function createEmployee(request: import('@playwright/test').APIRequestContext) {
  const email = 'permission-ui-' + unique() + '@example.test';
  const response = await request.post(apiUrl + '/api/employees', {
    headers: headers(),
    data: { fullName: 'Scoped UI Employee', email, password, role: 'Employee' },
  });
  expect(response.status()).toBe(201);
  return { id: (await response.json()).id as number, email };
}

async function signIn(page: import('@playwright/test').Page, email: string) {
  await page.goto('/');
  await page.getByLabel('Email address').fill(email);
  await page.locator('input[autocomplete="current-password"]').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Good to see you!' })).toBeVisible();
}

test('Restricted employee sees lead details without unauthorized editing controls', async ({ page, request }) => {
  const employee = await createEmployee(request);
  const leadName = 'Scoped UI Lead ' + unique();
  const created = await request.post(apiUrl + '/api/leads', {
    headers: headers(),
    data: { name: leadName, phone: '+96555501234', sourceId: 1 },
  });
  expect(created.status()).toBe(201);
  const leadId = (await created.json()).id as number;
  const assigned = await request.post(apiUrl + '/api/leads/' + leadId + '/assign', {
    headers: headers(), data: { userId: employee.id },
  });
  expect(assigned.status()).toBe(204);

  for (const permission of ['leads.create', 'leads.edit', 'followups.manage']) {
    const response = await request.put(apiUrl + '/api/admin/roles/employees/' +
      employee.id + '/restrictions/' + permission, {
      headers: headers(), data: { denied: true },
    });
    expect(response.status(), permission).toBe(200);
  }

  await signIn(page, employee.email);
  await page.getByRole('button', { name: 'All leads', exact: true }).click();
  await expect(page.getByRole('button', { name: leadName, exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add lead' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'New lead' })).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Status for ' + leadName })).toHaveCount(0);
  await page.getByRole('button', { name: leadName, exact: true }).click();
  const details = page.getByRole('dialog', { name: leadName });
  await expect(details.getByLabel('Full name')).toHaveAttribute('readonly', '');
  await expect(details.getByRole('button', { name: 'Save changes' })).toHaveCount(0);
  await expect(details.getByRole('button', { name: 'New follow-up' })).toHaveCount(0);
});

test('Employee with a custom employees.view grant can open Team directory', async ({ page, request }) => {
  const employee = await createEmployee(request);
  const created = await request.post(apiUrl + '/api/admin/roles', {
    headers: headers(), data: { name: 'Directory Reader ' + unique() },
  });
  expect(created.status()).toBe(200);
  const roleId = (await created.json()).id as number;
  const grant = await request.put(apiUrl + '/api/admin/roles/' + roleId + '/grants/employees.view', {
    headers: headers(), data: { scope: 'Self' },
  });
  expect(grant.status()).toBe(200);
  const membership = await request.put(apiUrl + '/api/admin/roles/' + roleId +
    '/members/' + employee.id, { headers: headers(), data: { assigned: true } });
  expect(membership.status()).toBe(200);

  await signIn(page, employee.email);
  await page.getByRole('button', { name: 'Team', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Team members' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: employee.email })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add employee' })).toHaveCount(0);
});

test('Restricted project member sees tasks without denied activity or status controls', async ({ page, request }) => {
  const employee = await createEmployee(request);
  const projectTitle = 'Scoped Task Project ' + unique();
  const lead = await request.post(apiUrl + '/api/leads', {
    headers: headers(), data: { name: projectTitle, phone: '+96555501234', sourceId: 1 },
  });
  expect(lead.status()).toBe(201);
  const leadId = (await lead.json()).id as number;
  const won = await request.patch(apiUrl + '/api/leads/' + leadId + '/status', {
    headers: headers(), data: { statusId: 5 },
  });
  expect(won.status()).toBe(204);
  const project = await request.post(apiUrl + '/api/projects', {
    headers: headers(), data: {
      leadId, title: projectTitle, ownerId: JSON.parse(process.env.E2E_ADMIN_USER!).id,
    },
  });
  expect(project.status()).toBe(201);
  const projectId = (await project.json()).id as number;
  const task = await request.post(apiUrl + '/api/projects/' + projectId + '/tasks', {
    headers: headers(), data: {
      title: 'Developer assigned task', priority: 'Medium', assignedToId: employee.id,
    },
  });
  expect(task.status()).toBe(201);

  for (const permission of ['tasks.status', 'bugs.view', 'bugs.create', 'worklogs.view',
    'worklogs.post', 'planning.view']) {
    const response = await request.put(apiUrl + '/api/admin/roles/employees/' +
      employee.id + '/restrictions/' + permission, {
      headers: headers(), data: { denied: true },
    });
    expect(response.status(), permission).toBe(200);
  }

  await signIn(page, employee.email);
  await page.getByRole('button', { name: 'Development projects' }).click();
  await page.getByRole('row').filter({ hasText: projectTitle })
    .getByRole('button', { name: 'View project' }).click();
  const details = page.getByRole('dialog', { name: 'Project ' + projectTitle });
  await expect(details.getByText('Developer assigned task')).toBeVisible();
  await expect(details.getByRole('combobox', { name: 'Status for Developer assigned task' })).toHaveCount(0);
  await expect(details.getByRole('region', { name: 'Project bugs' })).toHaveCount(0);
  await expect(details.getByRole('region', { name: 'Daily developer worklogs' })).toHaveCount(0);
  await expect(details.getByRole('region', { name: 'Sprint and milestone planning' })).toHaveCount(0);
  await expect(details.getByText("Log today's work")).toHaveCount(0);
});
