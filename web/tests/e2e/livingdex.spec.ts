import { expect, test } from '@playwright/test';
import { boxRow, items, newEmail, pushAs, rewrite, seedAccount, signInApi, slot } from './seed';

// The living dex region (National Dex order, 30 per box from the start box) is labelled in the sidebar and the box
// header, empty region slots show their dex number, and a Pokémon of another species in a region slot is marked.

const LIVE = { timeout: 3_000 };
const insp = (page: import('@playwright/test').Page) => page.locator('aside.insp');

test('labels, slot numbers and an off-slot Pokémon follow the living dex start box live', async ({ page }) => {
  // Seeded with the start at box 2 (index 1), which holds #001–030; empty #001's slot and put a Wurmple in #025's.
  const email = newEmail('livingdex');
  const { deviceKey } = await seedAccount(email);
  const all = await items(deviceKey);
  const at = (box: number, s: number) => [...all.values()].find((i) => i.kind === 'mon' && i.value.box === box && i.value.slot === s)!;
  const bulbasaur = at(1, 0), pikachu = at(1, 24);
  expect(await pushAs(deviceKey, [[
    { kind: 'mon', id: bulbasaur.id, baseVersion: bulbasaur.version, value: null, deleted: true },
    rewrite(pikachu, { ...pikachu.value, info: { ...pikachu.value.info, species: 265, nickname: 'Wormy' } }),
  ]])).toEqual(['accepted']);
  await signInApi(page, email);
  await page.goto('/bank/2');
  await boxRow(page, 9).waitFor({ state: 'attached' });

  await expect(boxRow(page, 0).locator('.sub')).toHaveCount(0);
  await expect(boxRow(page, 1).locator('.sub')).toHaveText('Living dex · #001–030');
  await expect(boxRow(page, 9).locator('.sub')).toHaveText('Living dex · #241–270');
  await expect(page.locator('.workhead .metatext')).toHaveText('Box 2 of 10 · 29/30 · Living dex #001–030');
  // Phones show the box picker instead of the sidebar list: region boxes carry their range there too.
  const pick = page.locator('select.boxpick option');
  await expect(pick.nth(0)).toHaveText('Keepers · 3/30');
  await expect(pick.nth(1)).toHaveText('Box 02 · 29/30 · #001–030');
  await expect(pick.nth(9)).toHaveText('Shiny hunts in progress · 0/30 · #241–270');

  // The empty first slot is #001's.
  await expect(slot(page, 0).locator('.dexslot')).toHaveText('#001');
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, empty, living dex #001 Bulbasaur');
  await expect(slot(page, 1).locator('.dexslot')).toHaveCount(0); // Ivysaur is home

  // Wurmple sits in Pikachu's slot.
  await expect(slot(page, 24).locator('.offslot')).toBeVisible();
  await expect(page.locator('.grid .offslot')).toHaveCount(1);
  await expect(slot(page, 24)).toHaveAttribute('aria-label', "Slot 25, Wormy, in #025 Pikachu's living dex slot");
  await slot(page, 24).click();
  await expect(insp(page).locator('.offnote')).toHaveText("#265 Wurmple is in #025 Pikachu's living dex slot. While it's here, Pikachu can't go into the living dex.");
  await slot(page, 1).click();
  await expect(insp(page).locator('.offnote')).toHaveCount(0);

  // Another device moves the start to box 3: everything follows without a reload.
  await expect(page.locator('.side-foot .status')).toHaveText(/^Live/);
  await page.evaluate(() => { (window as unknown as { stay: boolean }).stay = true; });
  const settings = (await items(deviceKey)).get('settings:bank')!;
  expect(await pushAs(deviceKey, [[rewrite(settings, { ...settings.value, livingDexStartBox: 2 })]])).toEqual(['accepted']);

  await expect(boxRow(page, 2).locator('.sub')).toHaveText('Living dex · #001–030', LIVE);
  await expect(boxRow(page, 1).locator('.sub')).toHaveCount(0);
  await expect(boxRow(page, 9).locator('.sub')).toHaveText('Living dex · #211–240');
  await expect(pick.nth(1)).toHaveText('Box 02 · 29/30');
  await expect(pick.nth(2)).toHaveText('Box 03 · 10/30 · #001–030');
  await expect(page.locator('.workhead .tag')).toHaveCount(0);
  await expect(slot(page, 0).locator('.dexslot')).toHaveCount(0);
  await expect(slot(page, 0)).toHaveAttribute('aria-label', 'Slot 1, empty');
  await expect(page.locator('.grid .offslot')).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { stay?: boolean }).stay)).toBe(true);
});
