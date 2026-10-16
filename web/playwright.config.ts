import { defineConfig } from '@playwright/test';
import { BASE } from './tests/e2e/seed';

/**
 * End-to-end tests against a real server (tests/e2e/server.ts starts it on port 5199 with a throwaway data folder).
 * `npm run e2e` runs them; `npx playwright test --update-snapshots` rewrites the screenshot baselines.
 */
export default defineConfig({
  testDir: 'tests/e2e',
  globalSetup: './tests/e2e/server.ts',
  timeout: 30_000,
  expect: {
    timeout: 7_000,
    toHaveScreenshot: { maxDiffPixelRatio: 0.01, animations: 'disabled', caret: 'hide' },
  },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: [['list']],
  snapshotPathTemplate: '{testDir}/__screenshots__/{testFilePath}/{arg}{ext}',
  use: {
    baseURL: BASE,
    browserName: 'chromium',
    viewport: { width: 1280, height: 900 },
    locale: 'en-US',
    timezoneId: 'UTC',
    colorScheme: 'light',
    contextOptions: { reducedMotion: 'reduce' },
    acceptDownloads: true,
    trace: 'retain-on-failure',
  },
});
