import { expect, test } from '@playwright/test';
import { ADMIN_EMAIL, BASE, call, PASSWORD } from './seed';

// Runs first, on the server global setup just started: no accounts yet.

test('on a fresh server, creating an account on the sign-in page lands in the Bank as its admin', async ({ page }) => {
  const info = await call<{ registrationOpen: boolean }>('/v1/server-info');
  expect(info.body.registrationOpen).toBe(true);

  await page.goto('/');
  await expect(page).toHaveURL(/\/signin$/);
  await page.getByRole('button', { name: 'Create an account' }).click();
  await expect(page.getByRole('heading', { name: 'Create an account' })).toBeVisible();

  await page.getByLabel('Email').fill(ADMIN_EMAIL);
  await page.getByLabel('Password', { exact: true }).fill('short');
  await page.getByLabel('Confirm password').fill('short');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.locator('.formerr')).toHaveText('Use at least 8 characters.');

  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByLabel('Confirm password').fill(PASSWORD + 'x');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.locator('.formerr')).toHaveText("The passwords don't match.");

  await page.getByLabel('Confirm password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Create account' }).click();
  // Back where the link pointed: "/" is the Bank.
  await expect(page).toHaveURL(`${BASE}/`);
  await expect(page.getByLabel('Email')).toHaveCount(0);
  await page.locator('.side-foot .status', { hasText: /^(Synced|Live)/ }).waitFor({ state: 'attached' });

  await page.getByRole('button', { name: 'Account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Server' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: `${ADMIN_EMAIL} (you)` })).toBeVisible();
  // This browser is the account's only device.
  await expect(page.locator('.dev')).toHaveCount(1);
});
