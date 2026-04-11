import { defineConfig, devices } from '@playwright/test';
import { BASE_URL, DEV_SERVER_PORT, UMBRACO_CLIENT_PATH, VITE_EXAMPLE_PATH } from './fixtures/env';

/**
 * Playwright harness for the Mocked Backoffice tier. Boots the real
 * Umbraco.Web.UI.Client Vite dev server with VITE_UMBRACO_USE_MSW=on and
 * loads SchemeWeaver via VITE_EXAMPLE_PATH, so tests drive the real
 * backoffice UI against mocked HTTP traffic — no .NET required.
 *
 * Prerequisites and patch instructions live in ../mocked-backoffice/README.md.
 */
export default defineConfig({
  testDir: '.',
  testMatch: ['*.spec.ts'],
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 2,
  reporter: process.env.CI ? 'line' : 'list',

  // Runs once before any worker starts. Applies the Umbraco-CMS patch
  // check and mirrors SchemeWeaver's src into the client's examples/
  // directory so Vite's fs.allow lets it through.
  globalSetup: require.resolve('./fixtures/global-setup'),

  webServer: {
    // Let Umbraco's own dev script pick up the port flag. Using `npm run dev`
    // rather than `vite` directly keeps the Umbraco build plugins in scope.
    command: `npm run dev -- --port ${DEV_SERVER_PORT}`,
    cwd: UMBRACO_CLIENT_PATH,
    url: BASE_URL,
    // First compile of the Umbraco backoffice is slow on a cold cache.
    timeout: 180_000,
    reuseExistingServer: !process.env.CI,
    env: {
      VITE_EXAMPLE_PATH,
      VITE_UMBRACO_USE_MSW: 'on',
    },
  },

  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    ignoreHTTPSErrors: true,
  },

  projects: [
    {
      name: 'mocked-backoffice',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
