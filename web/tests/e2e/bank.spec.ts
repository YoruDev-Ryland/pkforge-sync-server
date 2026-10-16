import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { unzipSync } from 'fflate';
import { boxRow, fixture, openBank, slot } from './seed';

const insp = (page: Page) => page.locator('aside.insp');
const tab = (page: Page, name: string) => insp(page).getByRole('tab', { name, exact: true });
/** The value next to a label in the inspector's fact lists. */
const fact = (page: Page, label: string) => insp(page).locator('dl.facts dt', { hasText: new RegExp(`^${label}$`) }).locator('xpath=following-sibling::dd[1]');

test('box 1 shows the three Pokémon sprites, and the living dex runs from box 2 on', async ({ page }) => {
  await openBank(page, 'sprites');
  for (const [n, name] of [[0, 'LEAFY'], [1, 'Garchomp'], [2, 'Pikachu']] as const) {
    await expect(slot(page, n)).toHaveAttribute('aria-label', `Slot ${n + 1}, ${name}`);
    const img = slot(page, n).locator('img');
    await expect(img).toHaveAttribute('src', /\/pkhex\/sprites\/b_\d+\.png$/);
    await expect.poll(() => img.evaluate((i: HTMLImageElement) => i.complete && i.naturalWidth)).toBeGreaterThan(0);
  }
  await expect(slot(page, 3)).toHaveAttribute('aria-label', 'Slot 4, empty');

  // 10 boxes from a start of box 2: boxes 2–10 hold #001–270.
  await expect(page.locator('.boxrow .sub', { hasText: 'Living dex' })).toHaveCount(9);
  await expect(boxRow(page, 0).locator('.sub')).toHaveCount(0);
  await expect(boxRow(page, 1).locator('.sub')).toHaveText('Living dex · #001–030');
  await expect(boxRow(page, 2).locator('.sub')).toHaveText('Living dex · #031–060');
  await expect(page.locator('.workhead .tag')).toHaveCount(0);
  await boxRow(page, 1).click();
  await expect(page).toHaveURL(/\/bank\/2$/);
  await expect(page.locator('.workhead .tag')).toHaveText('Living dex #001–030');
});

test('the inspector shows each file correctly', async ({ page }) => {
  await openBank(page, 'inspector');

  // Gen 3 Sceptile: nickname, nature arrows, Leaf Blade, a Contest tab.
  await slot(page, 0).click();
  await expect(insp(page).locator('.ihead h3')).toHaveText('LEAFY♂');
  // PID 957043EC % 25 = 4: Naughty.
  await expect(fact(page, 'Nature')).toContainText('Naughty');
  await expect(fact(page, 'Nature').locator('.up')).toHaveText('+Atk');
  await expect(fact(page, 'Nature').locator('.down')).toHaveText('−SpD');
  await expect(tab(page, 'Contest')).toBeVisible();
  await tab(page, 'Moves').click();
  const leafBlade = insp(page).locator('.mvrow', { hasText: 'Leaf Blade' });
  await expect(leafBlade.locator('.num').first()).toHaveText('90');
  await tab(page, 'Contest').click();
  await expect(insp(page).getByRole('heading', { name: /Sheen/ })).toContainText('106 / 255');
  await expect(insp(page).locator('.rib')).toContainText(['Champion']);

  // Gen 4 Garchomp: the met date.
  await slot(page, 1).click();
  await expect(insp(page).locator('.ihead h3')).toContainText('Garchomp');
  await tab(page, 'Info').click();
  await expect(fact(page, 'Met')).toContainText('Mar 3, 2026');

  // Gen 9 Pikachu: Ribbons, not Contest.
  await slot(page, 2).click();
  await expect(insp(page).locator('.ihead h3')).toContainText('Pikachu');
  await expect(tab(page, 'Ribbons')).toBeVisible();
  await expect(tab(page, 'Contest')).toHaveCount(0);
  await expect(fact(page, 'Tera Type')).toHaveText('Electric');
});

test('Download .pk3 saves the original file byte for byte', async ({ page }) => {
  await openBank(page, 'download');
  await slot(page, 0).click();
  // Once the details are in, the file name carries the PID.
  await expect(insp(page).locator('.actions .file')).toHaveText('254 - LEAFY - 957043EC.pk3');
  const [download] = await Promise.all([page.waitForEvent('download'), insp(page).getByRole('button', { name: 'Download .pk3' }).click()]);
  expect(download.suggestedFilename()).toBe('254 - LEAFY - 957043EC.pk3');
  const saved = readFileSync(await download.path());
  expect(Buffer.compare(saved, fixture('sceptile.pk3'))).toBe(0);
});

test('downloading the selection gives a zip with the three files', async ({ page }) => {
  await openBank(page, 'zip');
  await slot(page, 0).click();
  await slot(page, 2).click({ modifiers: ['Shift'] });
  const bar = page.getByRole('toolbar', { name: '3 Pokémon selected' });
  await expect(bar).toBeVisible();
  const [download] = await Promise.all([page.waitForEvent('download'), bar.getByRole('button', { name: 'Download' }).click()]);
  expect(download.suggestedFilename()).toBe('Keepers.zip');
  const files = unzipSync(new Uint8Array(readFileSync(await download.path())));
  expect(Object.keys(files).sort()).toEqual(['01 Keepers/025 - PIKACHU.pk9', '01 Keepers/254 - LEAFY.pk3', '01 Keepers/445 - GARCHOMP.pk4']);
  expect(Buffer.compare(Buffer.from(files['01 Keepers/254 - LEAFY.pk3']), fixture('sceptile.pk3'))).toBe(0);
  expect(Buffer.compare(Buffer.from(files['01 Keepers/445 - GARCHOMP.pk4']), fixture('garchomp.pk4'))).toBe(0);
  expect(Buffer.compare(Buffer.from(files['01 Keepers/025 - PIKACHU.pk9']), fixture('pikachu.pk9'))).toBe(0);
});
