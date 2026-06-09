import 'dotenv/config';
import { defineConfig, devices } from '@playwright/test';
import { join } from 'path';

export const STORAGE_STATE = join(__dirname, 'tests/e2e/.auth/user.json');

// CRITICAL: Testhelpers read auth tokens from this env var
process.env.STORAGE_STAGE_PATH = STORAGE_STATE;

export default defineConfig({
  testDir: './tests/e2e',
  // screenshots.spec.ts is a docs-generation tier, not regression — it has
  // its own `npm run test:screenshots` script (which passes the file path
  // explicitly and therefore bypasses testIgnore). Keeping it out of the
  // default run cuts ~2 min off the loop.
  testIgnore: ['**/screenshots.spec.ts'],
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // Each worker gets its own browser context + its own cookie jar read from
  // the file-backed storageState, so parallelism is safe. Keep CI at 2 to
  // avoid starving shared runners; local dev can use 4.
  workers: process.env.CI ? 2 : 4,
  reporter: process.env.CI ? 'line' : 'html',
  use: {
    baseURL: process.env.URL || process.env.UMBRACO_URL || 'https://localhost:44308',
    trace: 'retain-on-failure',
    ignoreHTTPSErrors: true,
    // CRITICAL: Umbraco uses 'data-mark' not 'data-testid'
    testIdAttribute: 'data-mark',
  },
  projects: [
    {
      name: 'setup',
      testMatch: '**/*.setup.ts',
    },
    {
      // Main suite. Excludes generate-doctype.spec.ts: creating/deleting a
      // document type at runtime forces Umbraco's InMemoryAuto ModelsBuilder
      // to regenerate its in-memory model assembly, after which every Razor
      // view bound to a generated PublishedModel (the whole TestHost site)
      // throws ModelBindingException ("…in an unstable state and should be
      // restarted") until the host is rebooted. The page-rendering specs
      // (JSON-LD on published pages) must therefore run BEFORE any model
      // mutation. See the e2e-mutating project below.
      name: 'e2e',
      testMatch: '**/*.spec.ts',
      // Project-level testIgnore replaces (does not merge with) the top-level
      // one, so screenshots.spec.ts must be repeated here alongside the
      // model-mutating spec that is deferred to the e2e-mutating project.
      testIgnore: ['**/screenshots.spec.ts', '**/generate-doctype.spec.ts'],
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        ignoreHTTPSErrors: true,
        storageState: STORAGE_STATE,
      },
    },
    {
      // Model-mutating specs, isolated to run only after every rendering spec
      // in the `e2e` project has completed (via `dependencies`). These leave
      // the InMemoryAuto host in an unstable state, so nothing that renders a
      // published page may run after them without a host restart.
      name: 'e2e-mutating',
      testMatch: '**/generate-doctype.spec.ts',
      dependencies: ['e2e'],
      use: {
        ...devices['Desktop Chrome'],
        ignoreHTTPSErrors: true,
        storageState: STORAGE_STATE,
      },
    },
  ],
});
