import { expect, test } from '@playwright/test';
import { call, focusTab, monsByName, openBank, PHONE, pushAs, rewrite, slot } from './seed';

test('a move that lost a race with another device says so and shows where the other device put it', async ({ page }) => {
  // No live stream (as with a server that doesn't offer one), so the browser can't hear about the other device's move.
  await page.route('**/v1/sync/events', (r) => r.fulfill({ status: 404, body: '' }));
  const { deviceKey } = await openBank(page, 'conflict');
  const leafy = (await monsByName(deviceKey)).get('LEAFY')!;
  expect(await pushAs(deviceKey, [[rewrite(leafy, { ...leafy.value, slot: 5 })]])).toEqual(['accepted']);

  // The browser hasn't polled yet: it still shows Sceptile in slot 1 and tries to move it from there.
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, LEAFY');
  await slot(page, 0).dragTo(slot(page, 9));
  await expect(page.locator('.toast')).toHaveText('That box changed on another device. Try again.');
  await expect(slot(page, 5)).toHaveAttribute('aria-label', 'Slot 6, LEAFY');
  await expect(slot(page, 9)).toHaveAttribute('aria-label', 'Slot 10, empty');
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, empty');
  const v = (await monsByName(deviceKey)).get('LEAFY')!.value;
  expect([v.box, v.slot]).toEqual([0, 5]);
});

test('offline: the status says so and organizing pauses until the server answers again', async ({ page }) => {
  await openBank(page, 'offline');
  const status = page.locator('.side-foot');
  const rename = page.getByRole('button', { name: 'Rename box' });
  await expect(rename).toBeEnabled();
  await expect(slot(page, 0)).toHaveAttribute('draggable', 'true');

  await page.route('**/v1/**', (r) => r.abort());
  await focusTab(page);
  // One failed poll is only a suspicion; the retry 5 s later failing too shows the banner.
  await expect(status).not.toContainText('Offline', { timeout: 3_000 });
  await expect(status).toContainText('Offline', { timeout: 10_000 });
  await expect(page.getByRole('status').filter({ hasText: "Can't reach the server. Changes are paused." })).toBeVisible();
  await expect(rename).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Wallpaper' })).toBeDisabled();
  await expect(page.locator('aside.insp').getByRole('button', { name: 'Release' })).toBeDisabled();
  await expect(page.locator('.addrow')).toBeDisabled();
  await expect(slot(page, 0)).not.toHaveAttribute('draggable', 'true');

  await page.unroute('**/v1/**');
  await focusTab(page);
  await expect(status).toContainText(/Synced|Live/);
  await expect(rename).toBeEnabled();
  await expect(slot(page, 0)).toHaveAttribute('draggable', 'true');
});

test('a server that refuses changes makes the Bank read-only with its reason; viewing keeps working', async ({ page }) => {
  await openBank(page, 'writes-disabled');
  const reason = 'Storage quota reached.';
  await page.route('**/v1/sync/push', (r) => r.fulfill({
    status: 403, contentType: 'application/json', body: JSON.stringify({ code: 'writes_disabled', message: reason }),
  }));

  await slot(page, 0).dragTo(slot(page, 9));
  const banner = `This server isn't accepting changes for your account: ${reason}`;
  await expect(page.locator('.toast')).toHaveText(banner);
  await expect(page.locator('.banner')).toContainText(`${banner} Viewing and downloads keep working.`);
  await expect(page.locator('.side-foot')).toContainText('Read-only');
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, LEAFY');
  await expect(page.getByRole('button', { name: 'Rename box' })).toBeDisabled();
  await expect(slot(page, 0)).not.toHaveAttribute('draggable', 'true');
  // Viewing still works: the inspector shows the Pokémon.
  await slot(page, 0).click();
  await expect(page.locator('aside.insp')).toContainText('LEAFY');
});

test('a browser removed from another device lands on the sign-in page and is told why', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'removed');
  const devices = (await call<{ id: string; name: string; isCurrent: boolean }[]>('/v1/devices', { key: deviceKey })).body;
  expect(devices.map((d) => d.name).sort()).toEqual(['Chrome on Linux', PHONE]);
  const browser = devices.find((d) => !d.isCurrent)!;
  expect((await call(`/v1/devices/${browser.id}`, { key: deviceKey, method: 'DELETE' })).status).toBe(204);

  await focusTab(page);
  await expect(page).toHaveURL(/\/signin$/);
  await expect(page.locator('.signnote')).toHaveText('This browser was signed out from another device.');
  await expect(page.getByLabel('Email')).toBeVisible();
});

test('a move in one tab shows in another at once', async ({ page, context }) => {
  await openBank(page, 'twotabs');
  const other = await context.newPage();
  await other.goto('/bank/1');
  await expect(slot(other, 0)).toHaveAttribute('aria-label', 'Slot 1, LEAFY');

  await slot(page, 0).dragTo(slot(page, 9));
  await expect(page.locator('.toast')).toHaveText('Moved LEAFY');
  // Both tabs are one device; the live stream still tells the other tab, no focus needed.
  await expect(slot(other, 9)).toHaveAttribute('aria-label', 'Slot 10, LEAFY');
  await expect(slot(other, 0)).toHaveAttribute('aria-label', 'Slot 1, empty');
});
