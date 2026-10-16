import { expect, test, type Page } from '@playwright/test';
import { call, monsByName, openBank, pushAs, rewrite, slot } from './seed';

// Changes made elsewhere reach an open page over the live event stream: no focus, visibility trick or reload.

const LIVE = { timeout: 3_000 };

/** Waits until the status line says the page's live stream is connected. */
async function live(page: Page): Promise<void> {
  await expect(page.locator('.side-foot .status')).toHaveText(/^Live/);
}

test('a move pushed by another device shows up on the open page at once', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'live-push');
  await live(page);
  const leafy = (await monsByName(deviceKey)).get('LEAFY')!;
  expect(await pushAs(deviceKey, [[rewrite(leafy, { ...leafy.value, slot: 7 })]])).toEqual(['accepted']);

  await expect(slot(page, 7)).toHaveAttribute('aria-label', 'Slot 8, LEAFY', LIVE);
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, empty');
});

test('removing this browser from another device signs it out at once', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'live-removed');
  await live(page);
  const devices = (await call<{ id: string; isCurrent: boolean }[]>('/v1/devices', { key: deviceKey })).body;
  const browser = devices.find((d) => !d.isCurrent)!;
  expect((await call(`/v1/devices/${browser.id}`, { key: deviceKey, method: 'DELETE' })).status).toBe(204);

  await expect(page).toHaveURL(/\/signin$/, LIVE);
  await expect(page.locator('.signnote')).toHaveText('This browser was signed out from another device.');
});

test('a theme saved from another device applies on the open page at once', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'live-prefs');
  await live(page);
  const html = page.locator('html');
  await expect(html).not.toHaveAttribute('data-mode', 'dark');

  expect((await call('/v1/account/preferences', { key: deviceKey, method: 'PUT', json: { mode: 'dark', theme: 'default' } })).status).toBe(204);
  await expect(html).toHaveAttribute('data-mode', 'dark', LIVE);
});

test('a new device shows up in the status line at once', async ({ page }) => {
  const { email } = await openBank(page, 'live-devices');
  await live(page);
  await expect(page.locator('.side-foot .status')).toContainText('2 devices');
  const r = await call('/v1/auth/login', { json: { email, password: 'e2e-password-1', deviceName: 'Laptop' } });
  expect(r.status).toBe(200);
  await expect(page.locator('.side-foot .status')).toContainText('3 devices', LIVE);
});
