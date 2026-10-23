import { expect, test } from './fixtures';

test('In-app follow-up reminder indicator opens the employee due list', async ({ page }) => {
  const now = Date.now();
  await page.route('**/api/followups/reminders?**', async route => {
    await route.fulfill({
      status: 200, contentType: 'application/json',
      body: JSON.stringify({
        total: 2, overdue: 1, upcoming: 1, page: 1, pageSize: 5,
        items: [
          { id: 9001, leadId: 4001, leadName: 'Reminder overdue customer',
            notes: 'Call back today', dueAtUtc: new Date(now - 3_600_000).toISOString(),
            assignedToId: JSON.parse(process.env.E2E_ADMIN_USER!).id },
          { id: 9002, leadId: 4002, leadName: 'Reminder upcoming customer',
            notes: 'Send proposal', dueAtUtc: new Date(now + 3_600_000).toISOString(),
            assignedToId: JSON.parse(process.env.E2E_ADMIN_USER!).id },
        ],
      }),
    });
  });

  await page.addInitScript(session => sessionStorage.setItem('crmSession', JSON.stringify(session)), {
    token: process.env.E2E_ADMIN_TOKEN!, user: JSON.parse(process.env.E2E_ADMIN_USER!),
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'My follow-up reminders: 2' }).click();
  await expect(page.getByRole('heading', { name: 'Follow-ups' })).toBeVisible();
  const section = page.getByRole('region', { name: 'My follow-up reminders' });
  await expect(section.getByText('1 overdue · 1 due soon')).toBeVisible();
  await expect(section.getByText('Reminder overdue customer')).toBeVisible();
  await expect(section.getByText('Reminder upcoming customer')).toBeVisible();
  await expect(section.getByText('Reminders refresh every minute while this page is open.')).toBeVisible();
});
