import { defineConfig, devices } from '@playwright/test';
import { BASE_URL, DEV_SERVER_PORT, UMBRACO_CLIENT_PATH, VITE_EXAMPLE_PATH } from './fixtures/env';

/**
 * Mocked-backoffice tier for the SEO bundle: the real Umbraco backoffice (Vite dev server with
 * VITE_UMBRACO_USE_MSW=on) loads the core AND SEO bundles from a composite mirror, with every
 * HTTP call served by the two bundles' MSW handlers. Prerequisites are the same as the core
 * harness (pinned, patched Umbraco-CMS clone; UMBRACO_CLIENT_PATH) — see
 * ../../../../../Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/tests/mocked-backoffice/README.md.
 */
export default defineConfig({
  testDir: '.',
  testMatch: ['*.spec.ts'],
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: process.env.CI ? 'line' : 'list',
  globalSetup: require.resolve('./fixtures/global-setup'),
  webServer: {
    command: `npm run dev -- --port ${DEV_SERVER_PORT}`,
    cwd: UMBRACO_CLIENT_PATH,
    url: BASE_URL,
    timeout: 180_000,
    reuseExistingServer: !process.env.CI,
    env: { VITE_EXAMPLE_PATH, VITE_UMBRACO_USE_MSW: 'on' },
  },
  use: { baseURL: BASE_URL, trace: 'retain-on-failure', ignoreHTTPSErrors: true },
  projects: [{ name: 'mocked-backoffice-seo', use: { ...devices['Desktop Chrome'] } }],
});
