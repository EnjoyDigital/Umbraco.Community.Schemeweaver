import type { UmbEntryPointOnInit } from '@umbraco-cms/backoffice/extension-api';
import { SchemeWeaverContext } from './context/schemeweaver.context.js';

/**
 * Backoffice entry point — constructs a single `SchemeWeaverContext` mounted
 * at the app root. Because `SchemeWeaverContext.provideContext` is called from
 * its constructor, every descendant element (workspace views, modals, entity
 * actions, etc.) can now consume `SCHEMEWEAVER_CONTEXT` via `consumeContext`
 * rather than building its own context or repository.
 *
 * Previously every workspace view did `new SchemeWeaverContext(this)` and
 * every modal / entity action did `new SchemeWeaverRepository(this)`, which
 * meant observable state was never actually shared.
 */
export const onInit: UmbEntryPointOnInit = (host) => {
  new SchemeWeaverContext(host);

  // Warm the Schema.org workspace-view chunk while the browser is idle so the
  // first switch to the tab doesn't stall on a lazy chunk load (which left the
  // previous view's content visible for seconds).
  const warm = () => void import('./workspace-views/schema-mapping-view.element.js');
  if ('requestIdleCallback' in globalThis) {
    requestIdleCallback(warm, { timeout: 4000 });
  } else {
    setTimeout(warm, 2000);
  }
};
