import { expect, test, type Page } from '@playwright/test';
import { ADMIN_EMAIL, call, newEmail, openBank, PASSWORD, seedAccount, signInApi } from './seed';

// The Account page's Server part: only the server's admin (made by fresh.spec.ts) sees it.

async function openAdmin(page: Page): Promise<void> {
  await signInApi(page, ADMIN_EMAIL);
  await page.goto('/account');
  await expect(page.getByRole('table', { name: 'Users' })).toHaveAttribute('aria-busy', 'false');
}

const row = (page: Page, email: string) => page.getByRole('row').filter({ hasText: email });
const login = (email: string, password: string) =>
  call('/v1/auth/login', { json: { email, password, deviceName: 'Test phone' } }).then((r) => r.status);

test('someone who is not an admin sees no Server part', async ({ page }) => {
  await openBank(page, 'notadmin');
  await page.getByRole('button', { name: 'Account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Devices' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Server' })).toHaveCount(0);
  await expect(page.getByRole('switch', { name: 'Allow new accounts' })).toHaveCount(0);
});

test.describe('as the admin', () => {
  test('the Account page only scrolls its column, never the whole page', async ({ page }) => {
    await page.setViewportSize({ width: 1708, height: 1275 });
    await openAdmin(page);
    const overflow = await page.evaluate(() => document.documentElement.scrollHeight - innerHeight);
    expect(overflow).toBe(0);
  });

  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });

  test('adds a user, whose password is shown once, then resets it', async ({ page }) => {
    const email = newEmail('added');
    await openAdmin(page);
    await page.getByRole('button', { name: 'Add user' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Email').fill(email);
    await dialog.getByRole('button', { name: 'Add user' }).click();

    await expect(dialog.getByRole('heading', { name: 'Account added' })).toBeVisible();
    const password = (await dialog.getByLabel('New password').textContent())!;
    expect(password).toMatch(/^.{16}$/);
    await dialog.getByRole('button', { name: 'Copy' }).click();
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(password);
    expect(await login(email, password)).toBe(200);
    // Esc and the scrim don't lose it; Done does, for good.
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Done' }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByText(password)).toHaveCount(0);
    await expect(row(page, email)).toBeVisible();

    await row(page, email).getByRole('button', { name: `Manage ${email}` }).click();
    await page.getByRole('button', { name: 'Reset password' }).click();
    await expect(dialog.getByRole('heading')).toHaveText(`Reset the password of ${email}?`);
    await dialog.getByRole('button', { name: 'Reset password' }).click();
    await expect(dialog.getByRole('heading', { name: 'Password reset' })).toBeVisible();
    const reset = (await dialog.getByLabel('New password').textContent())!;
    expect(reset).not.toBe(password);
    await dialog.getByRole('button', { name: 'Done' }).click();
    expect(await login(email, password)).toBe(401);
    expect(await login(email, reset)).toBe(200);
  });

  test('makes a user an admin, signs out their devices and deletes the account', async ({ page }) => {
    const email = newEmail('managed');
    const { deviceKey } = await seedAccount(email);
    await openAdmin(page);
    const r = row(page, email);
    await expect(r).toContainText('1 device');

    await r.getByRole('button', { name: `Manage ${email}` }).click();
    await page.getByRole('button', { name: 'Make admin' }).click();
    await expect(page.locator('.toast')).toHaveText(`${email} is now an admin`);
    expect((await call<{ isAdmin: boolean }>('/v1/account', { key: deviceKey })).body.isAdmin).toBe(true);

    await r.getByRole('button', { name: `Manage ${email}` }).click();
    await page.getByRole('button', { name: 'Sign out all devices' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Sign out' }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect((await call('/v1/account', { key: deviceKey })).status).toBe(401);
    await expect(r).toContainText('0 devices');

    await r.getByRole('button', { name: `Manage ${email}` }).click();
    await page.getByRole('button', { name: 'Delete account' }).click();
    const dialog = page.getByRole('dialog');
    const del = dialog.getByRole('button', { name: 'Delete account' });
    await expect(del).toBeDisabled();
    await dialog.getByLabel('Type their email to confirm').fill(email.toUpperCase());
    await del.click();
    await expect(dialog).toHaveCount(0);
    await expect(row(page, email)).toHaveCount(0);
    expect(await login(email, PASSWORD)).toBe(401);
  });

  test('cannot remove its own admin or delete itself from the list', async ({ page }) => {
    await openAdmin(page);
    await row(page, `${ADMIN_EMAIL} (you)`).getByRole('button', { name: `Manage ${ADMIN_EMAIL}` }).click();
    await expect(page.getByRole('button', { name: 'Remove admin' })).toHaveAttribute('aria-disabled', 'true');
    await expect(page.getByRole('button', { name: 'Delete account' })).toHaveAttribute('aria-disabled', 'true');
    await page.getByRole('button', { name: 'Delete account' }).click({ force: true });
    await expect(page.getByRole('dialog', { name: /Delete/ })).toHaveCount(0);
  });
});
