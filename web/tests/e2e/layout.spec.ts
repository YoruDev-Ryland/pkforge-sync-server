import { expect, test, type Page } from '@playwright/test';
import { openBank, slot } from './seed';

// Three columns: each one scrolls on its own and the box never moves when the inspector scrolls.
// Narrower layouts stay one scrolling page.

const body = (page: Page) => page.locator('aside.insp .ibody');
const top = async (page: Page, selector: string) => (await page.locator(selector).boundingBox())!.y;

async function statsTab(page: Page): Promise<void> {
  await slot(page, 0).click();
  await page.locator('aside.insp').getByRole('tab', { name: 'Stats', exact: true }).click();
  await expect(body(page)).toHaveAttribute('aria-busy', 'false');
}

test.describe('1280×720', () => {
  test.use({ viewport: { width: 1280, height: 720 } });

  test('a tall Stats tab scrolls inside the inspector while the box stays put', async ({ page }) => {
    await openBank(page, 'layout-720');
    await statsTab(page);
    const ibody = body(page);
    expect(await ibody.evaluate((e) => e.scrollHeight > e.clientHeight)).toBe(true);
    const gridTop = await top(page, '.grid');
    const actions = page.locator('aside.insp .actions');
    await expect(actions).toBeInViewport({ ratio: 1 });
    await expect(page.locator('aside.insp .itabs')).toBeInViewport({ ratio: 1 });

    await ibody.hover();
    await page.mouse.wheel(0, 400);
    await expect.poll(() => ibody.evaluate((e) => e.scrollTop)).toBeGreaterThan(0);
    expect(await top(page, '.grid')).toBe(gridTop);
    expect(await page.evaluate(() => scrollY)).toBe(0);
    await expect(actions).toBeInViewport({ ratio: 1 });
    await expect(page.locator('aside.insp .itabs')).toBeInViewport({ ratio: 1 });

    // The wheel over the box scrolls the box column (if anything), never the inspector.
    const scrolled = await ibody.evaluate((e) => e.scrollTop);
    await slot(page, 7).hover();
    await page.mouse.wheel(0, 400);
    await page.waitForTimeout(200);
    expect(await ibody.evaluate((e) => e.scrollTop)).toBe(scrolled);
    expect(await page.evaluate(() => scrollY)).toBe(0);
  });

  test('the Account page scrolls within its column', async ({ page }) => {
    await openBank(page, 'layout-acct');
    await page.goto('/account');
    const acct = page.locator('main.acct');
    await expect(acct).toBeVisible();
    expect(await acct.evaluate((e) => e.scrollHeight > e.clientHeight)).toBe(true);
    await acct.hover();
    await page.mouse.wheel(0, 600);
    await expect.poll(() => acct.evaluate((e) => e.scrollTop)).toBeGreaterThan(0);
    expect(await page.evaluate(() => scrollY)).toBe(0);
    await expect(page.locator('.side-foot')).toBeInViewport();
  });
});

test.describe('1280×900', () => {
  test.use({ viewport: { width: 1280, height: 900 } });

  test('the shell fills the window exactly and Download/Release sit at the bottom of the inspector', async ({ page }) => {
    await openBank(page, 'layout-900');
    await statsTab(page);
    expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBe(900);
    const insp = (await page.locator('aside.insp').boundingBox())!;
    const actions = (await page.locator('aside.insp .actions').boundingBox())!;
    expect(Math.round(actions.y + actions.height)).toBe(Math.round(insp.y + insp.height));
  });
});

test.describe('1280×460, too short for separate columns', () => {
  test.use({ viewport: { width: 1280, height: 460 } });

  test('scrolls as one page so everything stays reachable', async ({ page }) => {
    await openBank(page, 'layout-short');
    await statsTab(page);
    await page.mouse.move(600, 300);
    await page.mouse.wheel(0, 600);
    await expect.poll(() => page.evaluate(() => scrollY)).toBeGreaterThan(0);
    await expect(page.locator('aside.insp .actions')).toBeInViewport();
  });
});

test.describe('400 px', () => {
  test.use({ viewport: { width: 400, height: 800 } });

  test('stays one scrolling page', async ({ page }) => {
    await openBank(page, 'layout-400');
    await statsTab(page);
    expect(await body(page).evaluate((e) => getComputedStyle(e).overflowY)).toBe('visible');
    const gridTop = await page.locator('.grid').evaluate((e) => e.getBoundingClientRect().top);
    await page.mouse.move(200, 400);
    await page.mouse.wheel(0, 500);
    await expect.poll(() => page.evaluate(() => scrollY)).toBeGreaterThan(0);
    expect(await page.locator('.grid').evaluate((e) => e.getBoundingClientRect().top)).toBeLessThan(gridTop);
  });
});

// The box always fits: never a scroll to reach a slot. Three columns fit it to the area under the header, centered.

/** The grid, the fit area under the header, the box column, and every slot, as rects (page at the top). */
async function boxRects(page: Page) {
  return page.evaluate(() => {
    const r = (e: Element) => { const b = e.getBoundingClientRect(); return { l: b.left, t: b.top, r: b.right, b: b.bottom }; };
    const work = document.querySelector('main.work')!;
    return {
      grid: r(document.querySelector('.grid')!), fit: r(document.querySelector('.work > .fit')!), work: r(work),
      slots: [...document.querySelectorAll('.grid .slot')].map(r), overflow: work.scrollHeight - work.clientHeight,
      vw: innerWidth, vh: innerHeight,
    };
  });
}

for (const [width, height] of [[1280, 600], [1280, 900], [1440, 900], [1920, 1080], [1100, 700]]) {
  test(`${width}×${height}: the whole box fits its column, centered, and the column never scrolls`, async ({ page }) => {
    await page.setViewportSize({ width, height });
    await openBank(page, `fit-${width}x${height}`);
    const m = await boxRects(page);
    expect(m.slots).toHaveLength(30);
    expect(m.overflow).toBeLessThanOrEqual(0);
    const visBottom = Math.min(m.work.b, m.vh);
    for (const s of m.slots) {
      expect(s.l).toBeGreaterThanOrEqual(m.work.l - 0.5);
      expect(s.r).toBeLessThanOrEqual(m.work.r + 0.5);
      expect(s.t).toBeGreaterThanOrEqual(m.work.t - 0.5);
      expect(s.b).toBeLessThanOrEqual(visBottom + 0.5);
      expect(Math.abs((s.r - s.l) - (s.b - s.t))).toBeLessThan(1); // square
    }
    expect(Math.abs((m.grid.l - m.fit.l) - (m.fit.r - m.grid.r))).toBeLessThanOrEqual(2);
    expect(Math.abs((m.grid.t - m.fit.t) - (m.fit.b - m.grid.b))).toBeLessThanOrEqual(2);
  });
}

for (const [width, height] of [[1000, 640], [900, 600], [1280, 460]]) {
  test(`${width}×${height}: the box fits the window's height under the header`, async ({ page }) => {
    await page.setViewportSize({ width, height });
    await openBank(page, `fitv-${width}x${height}`);
    const m = await boxRects(page);
    expect(m.grid.b).toBeLessThanOrEqual(m.vh);
    expect(m.grid.r).toBeLessThanOrEqual(m.work.r);
    for (const s of m.slots) expect(Math.abs((s.r - s.l) - (s.b - s.t))).toBeLessThan(1);
  });
}

test('400 px: the box fits the width', async ({ page }) => {
  await page.setViewportSize({ width: 400, height: 800 });
  await openBank(page, 'fit-400');
  const m = await boxRects(page);
  expect(m.grid.l).toBeGreaterThanOrEqual(0);
  expect(m.grid.r).toBeLessThanOrEqual(m.vw);
  expect(m.grid.r - m.grid.l).toBeGreaterThan(m.vw - 40);
});
