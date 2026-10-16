import { expect, test, type Page } from '@playwright/test';
import { boxRow, call, openBank } from './seed';

// "Match box": with no stored theme, the colors follow the wallpaper of the box on screen.

const GROUND = '#ab6531', GRASS = '#00ae00', DEFAULT = '#2789cd', FIRE = '#dc182a';
/** Box 3 gets a volcano (→ Ground); box 10 is seeded with a forest (→ Grass); box 1's dark palette color is grey (→ Default). */
const WALLPAPERS = { 2: 'art:box_wp06rs' };

const hue = (page: Page) => page.evaluate(() => document.documentElement.style.getPropertyValue('--hue').trim().toLowerCase());
const swatch = (page: Page, name: string) => page.getByRole('radiogroup', { name: 'Color theme' }).getByRole('radio', { name, exact: true });

test('with no stored theme the colors follow the box, a fixed theme stops that, and both survive a reload', async ({ page }) => {
  const { deviceKey } = await openBank(page, 'boxtheme', '/bank/3', { wallpapers: WALLPAPERS });
  expect((await call<Record<string, unknown>>('/v1/account/preferences', { key: deviceKey })).body?.theme).toBeUndefined();
  await expect.poll(() => hue(page)).toBe(GROUND);

  await boxRow(page, 9).click();
  await expect(page).toHaveURL(/\/bank\/10$/);
  await expect.poll(() => hue(page)).toBe(GRASS);
  await boxRow(page, 0).click();
  await expect.poll(() => hue(page)).toBe(DEFAULT);
  await boxRow(page, 2).click();
  await expect.poll(() => hue(page)).toBe(GROUND);

  // Account keeps the last box's colors, and offers Match box first, chosen.
  await page.getByRole('button', { name: 'Account', exact: true }).click();
  await expect.poll(() => hue(page)).toBe(GROUND);
  const radios = page.getByRole('radiogroup', { name: 'Color theme' }).getByRole('radio');
  await expect(radios.first()).toHaveAccessibleName('Match box');
  await expect(swatch(page, 'Match box')).toHaveAttribute('aria-checked', 'true');

  // A fixed theme stays put while boxes change, also after a reload.
  await swatch(page, 'Fire').click();
  await expect.poll(() => hue(page)).toBe(FIRE);
  await expect.poll(async () => (await call<Record<string, unknown>>('/v1/account/preferences', { key: deviceKey })).body?.theme).toBe('fire');
  await page.getByRole('button', { name: 'Bank', exact: true }).click();
  await boxRow(page, 9).click();
  await expect(page).toHaveURL(/\/bank\/10$/);
  await expect.poll(() => hue(page)).toBe(FIRE);
  await page.reload();
  await page.locator('.boxrow[data-box="9"]').waitFor({ state: 'attached' });
  expect(await hue(page)).toBe(FIRE);
  await boxRow(page, 2).click();
  await expect(page).toHaveURL(/\/bank\/3$/);
  await expect.poll(() => hue(page)).toBe(FIRE);

  // Back to Match box: stored as "box", and a reload opens straight in the box's colors (cached, no flash).
  await page.getByRole('button', { name: 'Account', exact: true }).click();
  await swatch(page, 'Match box').click();
  await expect.poll(() => hue(page)).toBe(GROUND);
  await expect.poll(async () => (await call<Record<string, unknown>>('/v1/account/preferences', { key: deviceKey })).body?.theme).toBe('box');
  await page.goto('/bank/10');
  await expect.poll(() => hue(page)).toBe(GRASS);
  await page.reload({ waitUntil: 'commit' });
  await page.waitForFunction(() => document.documentElement.style.getPropertyValue('--hue') !== '');
  expect(await hue(page)).toBe(GRASS);
  await page.locator('.boxrow[data-box="9"]').waitFor({ state: 'attached' });
  expect(await hue(page)).toBe(GRASS);
});

test('the sign-in page uses Default in Match box mode', async ({ page }) => {
  await openBank(page, 'boxtheme-signin', '/bank/10');
  await expect.poll(() => hue(page)).toBe(GRASS);
  await page.getByRole('button', { name: 'Account', exact: true }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/signin$/);
  await expect.poll(() => hue(page)).toBe(DEFAULT);
});
