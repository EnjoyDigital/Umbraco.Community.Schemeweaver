export { manifests } from './manifests.js';

// Mocked-backoffice support (VITE_UMBRACO_USE_MSW=on): register the SEO handlers next to the
// core's. Dead-code-eliminated in production builds.
if (import.meta.env.VITE_UMBRACO_USE_MSW === 'on' && typeof window !== 'undefined') {
  const w = window as unknown as {
    __schemeweaverSeoMocksRegistered?: boolean;
    __schemeweaverSeoMockDb?: unknown;
    MockServiceWorker?: { addMockHandlers?: (...handlers: unknown[]) => void };
  };

  if (!w.__schemeweaverSeoMocksRegistered && typeof w.MockServiceWorker?.addMockHandlers === 'function') {
    const [{ seoHandlers }, { seoDb }] = await Promise.all([
      import('./mocks/seo-handlers.js'),
      import('./mocks/data/seo.db.js'),
    ]);
    w.MockServiceWorker.addMockHandlers(...seoHandlers);
    w.__schemeweaverSeoMockDb = seoDb;
    w.__schemeweaverSeoMocksRegistered = true;
  }
}
