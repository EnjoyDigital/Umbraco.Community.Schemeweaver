import 'dotenv/config';
import { defineConfig, devices } from '@playwright/test';
import { STORAGE_STATE } from './playwright.config';

/**
 * Minimal config for `npm run test:screenshots` only. Inherits auth
 * setup from the main config but drops testIgnore so screenshots.spec.ts
 * is discoverable.
 */
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: ['screenshots.spec.ts'],
  timeout: 120_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: process.env.URL || process.env.UMBRACO_URL || 'https://localhost:44308',
    trace: 'off',
    ignoreHTTPSErrors: true,
    testIdAttribute: 'data-mark',
  },
  projects: [
    {
      name: 'setup',
      testMatch: '**/*.setup.ts',
    },
    {
      name: 'screenshots',
      testMatch: 'screenshots.spec.ts',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        ignoreHTTPSErrors: true,
        storageState: STORAGE_STATE,
      },
    },
  ],
});
