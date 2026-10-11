import { request } from '@playwright/test';

export default async function setup() {
  const context = await request.newContext();
  try {
    const response = await context.post('http://localhost:5081/api/auth/login', {
      data: {
        email: process.env.CRM_ADMIN_EMAIL,
        password: process.env.CRM_ADMIN_PASSWORD,
      },
    });
    if (!response.ok()) throw new Error('Could not authenticate the test administrator.');

    const { token, user } = await response.json();
    process.env.E2E_ADMIN_TOKEN = token;
    process.env.E2E_ADMIN_USER = JSON.stringify(user);
  } finally {
    await context.dispose();
  }
}
