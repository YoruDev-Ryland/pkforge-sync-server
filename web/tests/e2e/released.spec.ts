import { expect, test, type Page } from '@playwright/test';
import { monsByName, openBank, pushAs, rewrite, slot } from './seed';

// Recently released: releases (here or on another device) can be undone from the web Bank for a while.

const toast = (page: Page) => page.locator('.toast');
const list = (page: Page) => page.locator('.rellist');
const row = (page: Page, name: string) => list(page).locator('.rel', { hasText: name });
const placeOf = async (key: string, name: string) => {
  const v = (await monsByName(key)).get(name)?.value;
  return v ? [v.box, v.slot] : null;
};

/** Releases Pokémon as the seeding phone ("another device"). */
async function releaseOnPhone(key: string, ...names: string[]): Promise<void> {
  const mons = await monsByName(key);
  const status = await pushAs(key, [names.map((n) => ({ kind: 'mon', id: mons.get(n)!.id, baseVersion: mons.get(n)!.version, value: null, deleted: true }))]);
  expect(status).toEqual(['accepted']);
}

test('releasing here, then restoring from Recently released puts it back in its slot', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'rel-roundtrip');
  await slot(page, 0).click();
  await page.locator('aside.insp').getByRole('button', { name: 'Release' }).click();
  const dialog = page.getByRole('dialog', { name: 'Release LEAFY?' });
  await expect(dialog).toContainText('You can restore it for 30 days from Recently released.');
  await dialog.getByRole('button', { name: 'Release' }).click();
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, empty');
  expect(await placeOf(deviceKey, 'LEAFY')).toBeNull();

  await page.locator('.relrow').click();
  await expect(page).toHaveURL(/\/bank\/released$/);
  await expect(row(page, 'LEAFY')).toContainText('Sceptile · Lv. 62 · Released just now'); // one line on a wide screen
  await row(page, 'LEAFY').getByRole('button', { name: 'Restore' }).click();
  await expect(toast(page)).toHaveText('Restored LEAFY to Keepers');
  await expect(row(page, 'LEAFY')).toHaveCount(0);
  await expect(list(page)).toContainText('Nothing released in the last 30 days.');
  expect(await placeOf(deviceKey, 'LEAFY')).toEqual([0, 0]);

  await page.getByRole('button', { name: 'Bank', exact: true }).click();
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, LEAFY');
  // The bytes came back with it: the inspector reads the file.
  await slot(page, 0).click();
  await expect(page.locator('aside.insp')).toContainText('Overgrow');
});

test('a release on another device shows up live; a taken slot sends it to the first free one', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'rel-live', '/bank/released');
  await expect(list(page)).toContainText('Nothing released in the last 30 days.');

  // The phone releases Garchomp and Charizard and, in the same change, moves LEAFY into Garchomp's old slot 2.
  const mons = await monsByName(deviceKey);
  const gone = (n: string) => ({ kind: 'mon', id: mons.get(n)!.id, baseVersion: mons.get(n)!.version, value: null, deleted: true });
  const leafy = mons.get('LEAFY')!;
  expect(await pushAs(deviceKey, [[gone('Garchomp'), gone('Charizard'), rewrite(leafy, { ...leafy.value, slot: 1 })]])).toEqual(['accepted']);
  await expect(list(page).locator('.rel .pn b')).toHaveText(['Charizard', 'Garchomp']);

  await row(page, 'Garchomp').getByRole('button', { name: 'Restore' }).click();
  await expect(toast(page)).toHaveText('Restored Garchomp to Keepers');
  expect(await placeOf(deviceKey, 'Garchomp')).toEqual([0, 0]);
  await expect(list(page).locator('.rel .pn b')).toHaveText(['Charizard']);
});

test('read-only: Restore is disabled after the server refuses, and the Pokémon stays released', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'rel-readonly');
  await releaseOnPhone(deviceKey, 'Garchomp');
  const reason = 'Storage quota reached.';
  await page.route('**/v1/sync/push', (r) => r.fulfill({
    status: 403, contentType: 'application/json', body: JSON.stringify({ code: 'writes_disabled', message: reason }),
  }));

  await page.locator('.relrow').click();
  const restore = row(page, 'Garchomp').getByRole('button', { name: 'Restore' });
  await restore.click();
  await expect(toast(page)).toHaveText(`This server isn't accepting changes for your account: ${reason}`);
  await expect(page.locator('.side-foot')).toContainText('Read-only');
  await expect(restore).toBeDisabled();
  await expect(restore).toHaveAttribute('title', 'Read-only');
  await expect(row(page, 'Garchomp')).toBeVisible();
  expect(await placeOf(deviceKey, 'Garchomp')).toBeNull();
});

test.describe('phone width', () => {
  test.use({ viewport: { width: 400, height: 900 } });

  test('the box picker opens Recently released', async ({ page }) => {
    const { deviceKey } = await openBank(page, 'rel-narrow');
    await releaseOnPhone(deviceKey, 'Garchomp');
    await page.locator('select.boxpick').selectOption({ label: 'Recently released' });
    await expect(page).toHaveURL(/\/bank\/released$/);
    await row(page, 'Garchomp').getByRole('button', { name: 'Restore' }).click();
    await expect(toast(page)).toHaveText('Restored Garchomp to Keepers');
    await page.locator('select.boxpick').selectOption({ index: 0 });
    await expect(slot(page, 1)).toHaveAttribute('aria-label', 'Slot 2, Garchomp');
  });
});
