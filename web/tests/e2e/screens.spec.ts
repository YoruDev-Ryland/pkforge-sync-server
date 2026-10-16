import { expect, test, type Page } from '@playwright/test';
import { monsByName, openBank, pushAs, slot } from './seed';

// Screenshot baselines of every screen, at desktop and phone widths, light and dark.
// The status-line clock and anything else that changes by the minute is masked.

const masks = (page: Page) => [page.locator('.side-foot .status'), page.locator('.dev .m'), page.locator('.toast')];

/** Fonts loaded, details fetched, every image on screen decoded, pointer parked where it hovers nothing. */
async function settle(page: Page): Promise<void> {
  await page.mouse.move(0, 0);
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await page.waitForFunction(() => [...document.images].every((img) => {
    const r = img.getBoundingClientRect();
    const onScreen = r.width > 0 && r.bottom > 0 && r.top < innerHeight * 3;
    return !onScreen || (img.complete && img.naturalWidth > 0);
  }));
}

/**
 * Desktop shots are the viewport (the sidebar and the inspector's actions are sticky to it, so a full-page capture
 * would show them stranded mid-page). Phone shots are the whole page, so the inspector below the box is in them,
 * except under a dialog, whose scrim covers only the viewport.
 */
async function shot(page: Page, name: string, opts: { dialog?: boolean } = {}): Promise<void> {
  // Clicking a button low on a phone page scrolls it into view by however much it needs; a dialog's shot starts at the top.
  if (opts.dialog) await page.evaluate(() => scrollTo(0, 0));
  await settle(page);
  const fullPage = (page.viewportSize()?.width ?? 0) < 760 && !opts.dialog;
  await expect(page).toHaveScreenshot(`${name}.png`, { fullPage, mask: masks(page) });
}

const insp = (page: Page) => page.locator('aside.insp');

for (const scheme of ['light', 'dark'] as const) {
  for (const width of [1280, 400]) {
    const tag = `${width}-${scheme}`;
    test.describe(tag, () => {
      test.use({ colorScheme: scheme, viewport: { width, height: 900 } });

      test('sign-in', async ({ page }) => {
        await page.goto('/signin');
        await expect(page.getByLabel('Email')).toBeVisible();
        await expect(page.locator('.serverinfo')).toContainText('PKForge Sync');
        await shot(page, `signin-${tag}`);
      });

      test('create an account', async ({ page }) => {
        await page.goto('/signin');
        await page.getByRole('button', { name: 'Create an account' }).click();
        await expect(page.getByLabel('Confirm password')).toBeVisible();
        await shot(page, `signup-${tag}`);
      });

      for (const [id, label] of [['info', 'Info'], ['stats', 'Stats'], ['moves', 'Moves'], ['contest', 'Contest']] as const) {
        test(`bank, ${label} tab`, async ({ page }) => {
          await openBank(page, `shot-${id}`);
          await slot(page, 0).click();
          await insp(page).getByRole('tab', { name: label, exact: true }).click();
          await expect(insp(page).getByRole('tab', { name: label, exact: true })).toHaveAttribute('aria-selected', 'true');
          await shot(page, `bank-${id}-${tag}`);
        });
      }

      test('several selected, Move to… open', async ({ page }) => {
        await openBank(page, 'shot-move');
        await slot(page, 0).click();
        await slot(page, 2).click({ modifiers: ['Shift'] });
        await page.getByRole('button', { name: 'Move to…' }).click();
        await expect(page.getByRole('dialog', { name: 'Move to box' })).toBeVisible();
        await shot(page, `move-to-${tag}`);
      });

      test('wallpaper picker', async ({ page }) => {
        await openBank(page, 'shot-wallpaper');
        await page.getByRole('button', { name: 'Wallpaper' }).click();
        await expect(page.getByRole('dialog', { name: 'Wallpaper' })).toBeVisible();
        await shot(page, `wallpaper-${tag}`);
      });

      test('release dialog', async ({ page }) => {
        await openBank(page, 'shot-release');
        await slot(page, 0).click();
        await insp(page).getByRole('button', { name: 'Release' }).click();
        await expect(page.getByRole('dialog', { name: 'Release LEAFY?' })).toBeVisible();
        await shot(page, `release-${tag}`, { dialog: true });
      });

      // Match box (no stored theme): the colors follow the box's wallpaper.
      if (width === 1280) {
        for (const [name, wallpaper] of [['ground', 'art:box_wp06rs'], ['grass', 'art:box_wp01rs']] as const) {
          test(`box in the ${name} theme`, async ({ page }) => {
            await openBank(page, `shot-${name}`, '/bank/1', { wallpapers: { 0: wallpaper } });
            await slot(page, 0).click();
            await shot(page, `box-${name}-${tag}`);
          });
        }
      }

      test('recently released', async ({ page }) => {
        const { deviceKey } = await openBank(page, 'shot-released');
        const mons = await monsByName(deviceKey);
        const gone = (n: string) => ({ kind: 'mon', id: mons.get(n)!.id, baseVersion: mons.get(n)!.version, value: null, deleted: true });
        expect(await pushAs(deviceKey, [['LEAFY', 'Garchomp', 'Squirtle', 'Charizard', 'Nidoran♀'].map(gone)])).toEqual(['accepted']);
        await page.locator('.relrow').dispatchEvent('click'); // hidden at phone width; the route is the same either way
        await expect(page.locator('.rellist .rel')).toHaveCount(5);
        await shot(page, `released-${tag}`);
      });

      test('account', async ({ page }) => {
        await openBank(page, 'shot-account');
        await page.getByRole('button', { name: 'Account', exact: true }).click();
        await expect(page.locator('.dev')).toHaveCount(2);
        await shot(page, `account-${tag}`);
      });
    });
  }
}
