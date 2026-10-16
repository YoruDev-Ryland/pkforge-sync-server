import { expect, test, type Page } from '@playwright/test';
import { boxRow, items, monsByName, openBank, slot } from './seed';

const toast = (page: Page) => page.locator('.toast');
/** Where the server says a Pokémon is, as [box, slot]. */
const placeOf = async (key: string, name: string) => {
  const v = (await monsByName(key)).get(name)?.value;
  return v ? [v.box, v.slot] : null;
};
const boxes = async (key: string) => (await items(key)).get('boxes:bank')!.value;

test('dragging Sceptile to slot 10 moves it there on the server', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'drag');
  await slot(page, 0).dragTo(slot(page, 9));
  await expect(toast(page)).toHaveText('Moved LEAFY');
  await expect(slot(page, 9)).toHaveAttribute('aria-label', 'Slot 10, LEAFY');
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, empty');
  expect(await placeOf(deviceKey, 'LEAFY')).toEqual([0, 9]);
});

test('dropping onto another Pokémon swaps them', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'swap');
  await slot(page, 0).dragTo(slot(page, 1));
  await expect(toast(page)).toHaveText('Swapped LEAFY and Garchomp');
  expect(await placeOf(deviceKey, 'LEAFY')).toEqual([0, 1]);
  expect(await placeOf(deviceKey, 'Garchomp')).toEqual([0, 0]);
});

test('dropping on a sidebar box sends it to that box\'s first free slot', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'sidebar');
  // Box 3 holds ten Pokémon in slots 1-10, so the first free slot is 11.
  await slot(page, 0).dragTo(boxRow(page, 2));
  await expect(toast(page)).toHaveText('Sent LEAFY to Box 03, slot 11');
  expect(await placeOf(deviceKey, 'LEAFY')).toEqual([2, 10]);
  await slot(page, 1).dragTo(boxRow(page, 8));
  await expect(toast(page)).toHaveText('Sent Garchomp to Box 09, slot 1');
  expect(await placeOf(deviceKey, 'Garchomp')).toEqual([8, 0]);
  await expect(boxRow(page, 8).locator('.ct')).toHaveText('1');
});

test('Shift-selecting three and moving them to box 3', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'movemany');
  await slot(page, 0).click();
  await slot(page, 2).click({ modifiers: ['Shift'] });
  const bar = page.getByRole('toolbar', { name: '3 Pokémon selected' });
  await bar.getByRole('button', { name: 'Move to…' }).click();
  const picker = page.getByRole('dialog', { name: 'Move to box' });
  // Box 2 is full: it can't take three.
  await expect(picker.locator('.boxpick-row', { hasText: 'Box 02' })).toBeDisabled();
  await picker.locator('.boxpick-row', { hasText: 'Box 03' }).click();
  await expect(toast(page)).toHaveText('Moved 3 to Box 03');
  await expect(bar).toHaveCount(0);
  expect(await placeOf(deviceKey, 'LEAFY')).toEqual([2, 10]);
  expect(await placeOf(deviceKey, 'Garchomp')).toEqual([2, 11]);
  expect(await placeOf(deviceKey, 'Pikachu')).toEqual([2, 12]);
});

test('renaming takes 24 characters and no more', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'rename');
  await page.getByRole('button', { name: 'Rename box' }).click();
  const input = page.getByRole('textbox', { name: /Box name/ });
  await expect(input).toHaveAttribute('maxlength', '24');
  await input.fill('');
  await input.pressSequentially('ABCDEFGHIJKLMNOPQRSTUVWXYZ'); // 26 typed, 24 kept
  await expect(input).toHaveValue('ABCDEFGHIJKLMNOPQRSTUVWX');
  await input.press('Enter');
  await expect(toast(page)).toHaveText('Box renamed');
  await expect(page.locator('.workhead h2')).toHaveText('ABCDEFGHIJKLMNOPQRSTUVWX');
  expect((await boxes(deviceKey)).names[0]).toBe('ABCDEFGHIJKLMNOPQRSTUVWX');
});

test('choosing an Emerald wallpaper saves art:box_wp01e', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'wallpaper');
  await page.getByRole('button', { name: 'Wallpaper' }).click();
  await page.getByRole('dialog', { name: 'Wallpaper' }).getByRole('button', { name: 'Emerald 1', exact: true }).click();
  await expect(toast(page)).toHaveText('Wallpaper changed');
  await expect(page.locator('.grid')).toHaveCSS('background-image', /box_wp01e\.png/);
  expect((await boxes(deviceKey)).wallpapers[0]).toBe('art:box_wp01e');
});

test('dragging box 3 above box 1 takes its name, wallpaper, Pokémon and the dex start box along', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'reorder');
  await boxRow(page, 2).dragTo(boxRow(page, 0));
  await expect(toast(page)).toHaveText('Boxes reordered');
  // The open box (Keepers) stays open, now second.
  await expect(page).toHaveURL(/\/bank\/2$/);
  await expect(page.locator('.workhead h2')).toHaveText('Keepers');

  const all = await items(deviceKey);
  const b = all.get('boxes:bank')!.value;
  expect(b.names.slice(0, 3)).toEqual([null, 'Keepers', null]);
  expect(b.wallpapers.slice(0, 3)).toEqual([null, 'color:2', null]);
  expect(b.names[9]).toBe('Shiny hunts in progress');
  expect(all.get('settings:bank')!.value.livingDexStartBox).toBe(2);
  const mons = await monsByName(deviceKey);
  expect([mons.get('LEAFY')!.value.box, mons.get('LEAFY')!.value.slot]).toEqual([1, 0]);
  expect(mons.get('Bulbasaur')!.value.box).toBe(2); // was box 2 (the dex box), now third
  expect(mons.get('Clefable')!.value.box).toBe(0); // was box 3, now first
  await expect(boxRow(page, 2).locator('.sub')).toHaveText('Living dex · #001–030');
  await expect(boxRow(page, 1).locator('.sub')).toHaveCount(0);
});

test('Release asks first: Keep does nothing, Release deletes', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'release');
  const insp = page.locator('aside.insp');
  await slot(page, 0).click();
  await insp.getByRole('button', { name: 'Release' }).click();
  const dialog = page.getByRole('dialog', { name: 'Release LEAFY?' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Keep' }).click();
  await expect(dialog).toHaveCount(0);
  expect(await placeOf(deviceKey, 'LEAFY')).toEqual([0, 0]);

  await insp.getByRole('button', { name: 'Release' }).click();
  await dialog.getByRole('button', { name: 'Release' }).click();
  await expect(toast(page)).toHaveText('Released 1');
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, empty');
  expect(await placeOf(deviceKey, 'LEAFY')).toBeNull();
});
