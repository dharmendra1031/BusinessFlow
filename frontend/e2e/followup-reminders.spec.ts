import { expect, test } from './fixtures';

test('In-app reminder filters show the assigned employee and next 24-hour queue', async ({ page }) => {
  let lastQuery = '';
  await page.route('**/api/followups/reminder-summary?**', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ overdue: 1, dueSoon: 1, later: 2, pendingTotal: 4 }) });
  });
  await page.route('**/api/followups?**', async route => {
    const query = new URL(route.request().url()).searchParams;
    lastQuery = query.toString();
    const mine = query.get('mineOnly') === 'true';
    const due = query.get('view') === 'dueSoon';
    const base = { leadId: 9001, leadName: 'Own reminder lead', notes: 'Call customer',
      dueAtUtc: new Date(Date.now() + 2 * 3600000).toISOString(), completedAtUtc: null,
      assignedToId: 10 };
    const items = due ? mine ? [{ ...base, id: 1001 }] :
      [{ ...base, id: 1001 }, { ...base, id: 1002, leadName: 'Team reminder lead' }] : [];
    await route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ items, total: items.length, page: 1, pageSize: 50 }) });
  });
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Follow-ups', exact: true }).click();
  await expect(page.getByText(/Overdue: 1/)).toBeVisible();
  await page.getByRole('button', { name: 'View my due reminders' }).click();
  await expect(page.getByRole('checkbox', { name: 'Assigned to me only' })).toBeChecked();
  await expect(page.getByRole('tab', { name: 'Due next 24h' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Own reminder lead')).toBeVisible();
  expect(lastQuery).toContain('mineOnly=true');
  await page.getByRole('checkbox', { name: 'Assigned to me only' }).uncheck();
  await expect(page.getByText('Team reminder lead')).toBeVisible();
  expect(lastQuery).toContain('mineOnly=false');
});
