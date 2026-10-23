import { expect, test } from './fixtures';

test('Follow-up inbox filters due-soon reminders by owner and refreshes list', async ({ page }) => {
  const requests: string[] = [];
  await page.route('**/api/followups?**', async route => {
    const url = new URL(route.request().url());
    requests.push(url.search);
    const dueSoon = url.searchParams.get('view') === 'dueSoon';
    const mine = url.searchParams.get('mine') === 'true';
    const items = dueSoon && mine ? [{
      id: 801, leadId: 101, leadName: 'Assigned customer',
      notes: 'Call customer before noon',
      dueAtUtc: new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString(),
      completedAtUtc: null, assignedToId: 99, assignedTo: 'Sales member',
    }] : [];
    await route.fulfill({
      status: 200, contentType: 'application/json',
      body: JSON.stringify({ items, total: items.length, page: 1, pageSize: 50 }),
    });
  });
  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!,
    user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Follow-ups', exact: true }).click();
  await page.getByRole('tab', { name: 'Due Soon (24h)' }).click();
  await page.getByRole('checkbox', { name: 'Assigned to me only' }).check();

  await expect(page.getByText('Assigned customer')).toBeVisible();
  await expect(page.getByText('Call customer before noon')).toBeVisible();
  await expect(page.getByText('Owner: Sales member')).toBeVisible();
  expect(requests.some(x => x.includes('view=dueSoon') && x.includes('mine=true'))).toBeTruthy();

  await page.getByRole('tab', { name: 'Overdue' }).click();
  await expect(page.getByText('No overdue follow-ups in this list.')).toBeVisible();
});
