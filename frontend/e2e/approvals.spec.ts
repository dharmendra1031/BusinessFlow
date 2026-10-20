import { test, expect } from './fixtures';

test('Employee requests Won and a different Admin approves it through the UI', async ({ page, request }) => {
  const admin = { Authorization: 'Bearer ' + process.env.E2E_ADMIN_TOKEN! };
  const email = 'approval-' + Date.now() + '@example.test';
  const password = 'ApprovalEmployee123!';
  expect((await request.post('http://localhost:5081/api/employees', { headers: admin,
    data: { fullName: 'Approval requester', email, password, role: 'Employee' } })).status()).toBe(201);
  const login = await request.post('http://localhost:5081/api/auth/login', { data: { email, password } });
  expect(login.status()).toBe(200);
  const employee = await login.json();
  const headers = { Authorization: 'Bearer ' + employee.token };
  const name = 'Approval customer ' + Date.now();
  const created = await request.post('http://localhost:5081/api/leads', { headers, data: { name, phone: '1234567890', sourceId: 1 } });
  const { id } = await created.json();
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), employee);
  await page.goto('/');
  await page.getByRole('button', { name: 'All leads', exact: true }).click();
  await page.getByLabel('Status for ' + name).selectOption({ label: 'Won' });
  await page.getByRole('dialog', { name: 'Request Won status' }).getByRole('button', { name: 'Request approval', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Request Won status' })).toHaveCount(0);
  expect((await (await request.get(`http://localhost:5081/api/leads/${id}`, { headers })).json()).statusId).toBe(1);
  const reviewer = await page.context().newPage();
  await reviewer.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await reviewer.goto('/');
  await reviewer.getByRole('button', { name: 'Lead approvals', exact: true }).click();
  const row = reviewer.getByRole('row').filter({ has: reviewer.getByRole('cell', { name: 'Lead #' + id, exact: true }) });
  await row.getByRole('button', { name: 'Approve', exact: true }).click();
  await reviewer.getByRole('alertdialog').getByRole('button', { name: 'Approve', exact: true }).click();
  await expect(row).toHaveCount(0);
  await reviewer.close();
  expect((await (await request.get(`http://localhost:5081/api/leads/${id}`, { headers: admin })).json()).statusId).toBe(5);
});
