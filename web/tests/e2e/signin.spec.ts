import { expect, test } from '@playwright/test';
import { BASE, call, newEmail, openBank, PASSWORD, seedAccount } from './seed';

test('a wrong password says so and stays on the sign-in page', async ({ page }) => {
  const email = newEmail('wrongpw');
  await seedAccount(email);
  await page.goto('/');
  await expect(page).toHaveURL(/\/signin$/);
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('not-the-password');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.locator('.formerr')).toHaveText('Email or password is wrong.');
  await expect(page).toHaveURL(/\/signin$/);
});

test('the right password opens the Bank with its ten boxes, and a reload keeps the session', async ({ page }) => {
  const email = newEmail('rightpw');
  await seedAccount(email);
  await page.goto('/signin');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/bank$/);
  await expect(page.locator('.boxrow[data-box]')).toHaveCount(10);
  await expect(page.locator('.boxrow[data-box="0"] .nm')).toHaveText('Keepers');

  await page.reload();
  await expect(page).toHaveURL(/\/bank$/);
  await expect(page.locator('.boxrow[data-box]')).toHaveCount(10);
  await expect(page.getByLabel('Email')).toHaveCount(0);
});

test('a pairing code from the app signs in', async ({ page }) => {
  const email = newEmail('pairing');
  const { deviceKey } = await seedAccount(email);
  const { status, body } = await call<{ code: string }>('/v1/pairing', { key: deviceKey, method: 'POST' });
  expect(status).toBe(200);

  await page.goto('/signin');
  await page.getByRole('button', { name: 'Use a code from the app' }).click();
  // People type the code with a dash and in lowercase; the field cleans it up.
  await page.getByLabel('Code from the app').fill(`${body.code.slice(0, 4)}-${body.code.slice(4)}`.toLowerCase());
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/bank$/);
  await expect(page.locator('.boxrow[data-box]')).toHaveCount(10);
});

test('signing out returns to the sign-in page and the old cookie stops working', async ({ page, context }) => {
  const email = newEmail('signout');
  await seedAccount(email);
  await page.goto('/signin');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.locator('.boxrow[data-box]')).toHaveCount(10);

  const cookie = (await context.cookies(BASE)).find((c) => c.name === 'pkf_device');
  expect(cookie, 'the device cookie').toBeTruthy();
  expect(cookie!.httpOnly).toBe(true);

  await page.getByRole('button', { name: 'Account' }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/signin$/);
  await expect(page.getByLabel('Email')).toBeVisible();

  const r = await fetch(`${BASE}/v1/devices`, { headers: { 'X-PKForge-Protocol': '1', cookie: `pkf_device=${cookie!.value}` } });
  expect(r.status).toBe(401);
});

test.describe('adding a device from the Account page', () => {
  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });

  test('shows the server and code to type in the app, and copies them', async ({ page }) => {
    await openBank(page, 'pairline');
    await page.getByRole('button', { name: 'Account', exact: true }).click();
    await page.getByRole('button', { name: 'Show a code' }).click();
    const shown = page.getByLabel('Server and code to type');
    await expect(shown).toHaveText(/^http:\/\/127\.0\.0\.1:5199  [A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$/);
    const code = (await page.locator('.paircode').getAttribute('aria-label'))!.replace('Pairing code ', '');
    await expect(shown).toHaveText(`${BASE}  ${code.slice(0, 4)}-${code.slice(4)}`);
    await page.locator('.pairline').getByRole('button', { name: 'Copy' }).click();
    await expect(page.locator('.toast')).toHaveText('Copied.');
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(`${BASE}  ${code.slice(0, 4)}-${code.slice(4)}`);
  });
});
