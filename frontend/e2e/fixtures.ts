import { test as base, expect, type APIRequestContext } from '@playwright/test';

// Legacy regression scenarios use the latest server version immediately before a
// write. Dedicated concurrency scenarios use the native fixture and retain stale
// versions deliberately to verify conflicts.
export const test = base.extend({
  page: async ({ page }, use) => {
    const success = page.getByRole('alertdialog').filter({ has: page.getByRole('button', { name: 'Done', exact: true }) });
    await page.addLocatorHandler(success, async () => {
      await success.getByRole('button', { name: 'Done', exact: true }).click();
    });
    await use(page);
  },
  request: async ({ request }, use) => {
    const wrapped = new Proxy(request, {
      get(target, property) {
        if (!['put', 'patch', 'post'].includes(String(property))) {
          const value = Reflect.get(target, property);
          return typeof value === 'function' ? value.bind(target) : value;
        }
        return async (url: string, options: Parameters<APIRequestContext['put']>[1] = {}) => {
          const parsed = new URL(url, 'http://localhost:5081');
          const lead = parsed.pathname.match(/^\/api\/leads\/(\d+)(?:\/(assign|status))?$/);
          const project = parsed.pathname.match(/^\/api\/projects\/(\d+)$/);
          const task = parsed.pathname.match(/^\/api\/projects\/(\d+)\/tasks\/(\d+)(?:\/status)?$/);
          let data = options?.data;
          if ((lead || project || task) && data && typeof data === 'object' && !Buffer.isBuffer(data) && !('rowVersion' in data)) {
            const resource = task ? '/api/projects/' + task[1] : lead ? '/api/leads/' + lead[1] : '/api/projects/' + project![1];
            const current = await target.get(parsed.origin + resource, { headers: options?.headers });
            if (current.ok()) {
              const record = await current.json();
              const rowVersion = task ? record.tasks?.find((item: { id: number }) => item.id === Number(task[2]))?.rowVersion : record.rowVersion;
              data = { ...data, rowVersion };
            }
          }
          if (lead?.[2] === 'status' && data && typeof data === 'object' && 'statusId' in data &&
              (data.statusId === 5 || data.statusId === 6) && !('overrideReason' in data)) {
            data = { ...data, overrideReason: 'Regression setup: verified terminal status for project scenarios' };
          }
          const operation = Reflect.get(target, property).bind(target);
          return operation(url, { ...options, data });
        };
      },
    });
    await use(wrapped);
  },
});

export { expect };
