export { manifests } from './manifests.js';

// Mocked-backoffice tier support. When the extension is loaded into the
// Umbraco.Web.UI.Client dev server with VITE_UMBRACO_USE_MSW=on, register
// SchemeWeaver's MSW handlers so the backoffice UI can talk to our mock
// API without a running .NET backend. The branch is dead-code-eliminated
// in production builds because VITE_UMBRACO_USE_MSW resolves at build time.
//
// Requires the umbraco-cms addMockHandlers patch under
// tests/mocked-backoffice/patches/ — see that folder's README for setup.
if (import.meta.env.VITE_UMBRACO_USE_MSW === 'on' && typeof window !== 'undefined') {
  const w = window as unknown as {
    __schemeweaverMocksRegistered?: boolean;
    __schemeweaverMockDb?: unknown;
    MockServiceWorker?: { addMockHandlers?: (...handlers: unknown[]) => void };
  };

  if (!w.__schemeweaverMocksRegistered) {
    const msw = w.MockServiceWorker;
    if (typeof msw?.addMockHandlers !== 'function') {
      // eslint-disable-next-line no-console
      console.error(
        '[SchemeWeaver] MockServiceWorker.addMockHandlers is missing — apply ' +
          'tests/mocked-backoffice/patches/umbraco-cms-v17.2.2-addmockhandlers.patch ' +
          'to your local Umbraco-CMS clone before starting the mocked-backoffice harness.',
      );
    } else {
      const [{ handlers }, { schemeWeaverDb }] = await Promise.all([
        import('./mocks/handlers.js'),
        import('./mocks/data/schemeweaver.db.js'),
      ]);
      msw.addMockHandlers(...handlers);
      // Expose the mock DB to the Playwright harness for reset/seed helpers.
      // Tests reach it via `page.evaluate(() => window.__schemeweaverMockDb.reset())`,
      // which avoids fragile URL-based dynamic imports.
      w.__schemeweaverMockDb = schemeWeaverDb;
      w.__schemeweaverMocksRegistered = true;
    }
  }
}
