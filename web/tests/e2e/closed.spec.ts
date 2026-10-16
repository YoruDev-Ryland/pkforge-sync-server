import { expect, test } from '@playwright/test';
import { ADMIN_EMAIL, call, PASSWORD, signInApi } from './seed';

// Runs after everything else: closing sign-ups would refuse other tests' seeding.

const open = async () => (await call<{ registrationOpen: boolean }>('/v1/server-info')).body.registrationOpen;

test('the admin closes sign-ups and the sign-in page says so', async ({ page, browser }) => {
  try {
    await signInApi(page, ADMIN_EMAIL);
    await page.goto('/account');
    const toggle = page.getByRole('switch', { name: 'Allow new accounts' });
    await expect(toggle).toHaveAttribute('aria-checked', 'true'); // ALLOW_REGISTRATION=true until an admin sets it
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', 'false');
    await expect(page.getByText('Only admins can add accounts.')).toBeVisible();
    await expect.poll(open).toBe(false);

    const visitor = await browser.newPage();
    await visitor.goto('/signin');
    await expect(visitor.getByText('Sign-ups are closed on this server. Ask its admin for an account.')).toBeVisible();
    await expect(visitor.getByRole('button', { name: 'Create an account' })).toHaveCount(0);
    await visitor.close();

    await page.reload();
    await expect(toggle).toHaveAttribute('aria-checked', 'false');
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', 'true');
    await expect.poll(open).toBe(true);
  } finally {
    // Leave the server as other runs expect it, whatever happened above.
    const key = (await call<{ deviceKey: string }>('/v1/auth/login', { json: { email: ADMIN_EMAIL, password: PASSWORD, deviceName: 'cleanup' } })).body.deviceKey;
    await call('/v1/admin/settings', { key, method: 'PUT', json: { allowRegistration: true } });
  }
});
