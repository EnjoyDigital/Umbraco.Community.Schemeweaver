import { setupWorker } from 'msw/browser';
import { seoHandlers } from './seo-handlers.js';
import { SchemeWeaverSeoContext } from '../context/seo.context.js';

export const worker = setupWorker(...seoHandlers);

let rootContext: SchemeWeaverSeoContext | undefined;

/** Provides the SEO context the way the entry point does in production (via the mock registry). */
export function ensureRootContext(): SchemeWeaverSeoContext {
  if (!rootContext) {
    rootContext = new SchemeWeaverSeoContext({} as never);
  }
  return rootContext;
}

export async function startMockServiceWorker() {
  await worker.start({
    onUnhandledFrame: 'bypass',
    quiet: true,
    // The test server's root is the repo's src/ folder (shared core modules), so the worker
    // script lives below it rather than at '/'.
    serviceWorker: { url: '/Umbraco.Community.SchemeWeaver.Seo/App_Plugins/SchemeWeaverSeo/mockServiceWorker.js', options: { scope: '/' } },
  });
  ensureRootContext();
  return worker;
}

export async function stopMockServiceWorker() {
  await worker.stop();
}
