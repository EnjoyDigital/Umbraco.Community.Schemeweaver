import { expect, test } from '@playwright/test';
import { openBackoffice } from './fixtures/seed';

/**
 * Smoke tests for the Mocked Backoffice harness itself. If any of these
 * fail, nothing else in the tier can be trusted — fix these first before
 * looking at the more specific specs.
 */

test.describe('Mocked Backoffice — bootstrap', () => {
  test('backoffice boots with MSW enabled', async ({ page }) => {
    await openBackoffice(page);

    // The Umbraco MSW setup stashes its primitives on window.MockServiceWorker.
    // If MSW isn't active our addMockHandlers patch would also be missing,
    // so this is a stricter check than just "the page loaded".
    const mswReady = await page.evaluate(
      () =>
        typeof (window as unknown as { MockServiceWorker?: unknown }).MockServiceWorker === 'object',
    );
    expect(mswReady, 'window.MockServiceWorker should exist when VITE_UMBRACO_USE_MSW=on').toBe(true);

    // Our one-line patch exposes addMockHandlers. If the patch wasn't
    // applied, SchemeWeaver's handlers never registered.
    const hasAddMockHandlers = await page.evaluate(
      () =>
        typeof (
          window as unknown as { MockServiceWorker?: { addMockHandlers?: unknown } }
        ).MockServiceWorker?.addMockHandlers === 'function',
    );
    expect(
      hasAddMockHandlers,
      'window.MockServiceWorker.addMockHandlers should be present — apply the umbraco-cms patch',
    ).toBe(true);

    // SchemeWeaver's bootstrap flips this flag once it has registered its
    // own MSW handlers. It's the one signal that's specific to SchemeWeaver
    // rather than to Umbraco-CMS itself.
    const schemeWeaverMocksRegistered = await page.evaluate(
      () => (window as unknown as { __schemeweaverMocksRegistered?: boolean }).__schemeweaverMocksRegistered === true,
    );
    expect(
      schemeWeaverMocksRegistered,
      'SchemeWeaver should have registered its MSW handlers — check the browser console for [SchemeWeaver] errors',
    ).toBe(true);
  });

  test('mock DB handle is exposed on window', async ({ page }) => {
    await openBackoffice(page);

    // SchemeWeaver's src/index.ts stashes the mock DB on window during the
    // MSW handler registration path. This doubles as a positive signal
    // that the extension's bootstrap block ran to completion — if the
    // dynamic imports had failed silently, the handle would be undefined.
    const dbReady = await page.evaluate(() => {
      const db = (
        window as unknown as { __schemeweaverMockDb?: { reset: () => void; getMappings: () => unknown[] } }
      ).__schemeweaverMockDb;
      return {
        hasReset: typeof db?.reset === 'function',
        hasGetMappings: typeof db?.getMappings === 'function',
      };
    });

    expect(dbReady.hasReset).toBe(true);
    expect(dbReady.hasGetMappings).toBe(true);
  });
});

// Manifest registration is proved transitively by workspace-view.spec.ts:
// if the Schema.org tab mounts on a document-type workspace, the workspace
// view manifest must have been registered, which only happens if Umbraco's
// `VITE_EXAMPLE_PATH` bootstrap found and loaded SchemeWeaver's src/index.ts.
// A direct registry check would need a bare module specifier in the
// browser, which Vite dev mode doesn't resolve at runtime.
