import { setupWorker } from 'msw/browser';
import { handlers } from './handlers.js';
import { SchemeWeaverContext } from '../context/schemeweaver.context.js';

export const worker = setupWorker(...handlers);

// The refactored elements (workspace views, modals, entity actions) no longer
// instantiate their own SchemeWeaverContext / SchemeWeaverRepository — they
// consume SCHEMEWEAVER_CONTEXT instead. In production the context is provided
// at the app root by the backoffice entry point; tests fake this by eagerly
// constructing one instance here (its constructor calls `provideContext`,
// which the mock context registry wires to a module-global map).
let rootContext: SchemeWeaverContext | undefined;

function ensureRootContext() {
  if (!rootContext) {
    rootContext = new SchemeWeaverContext({} as never);
  }
  return rootContext;
}

export async function startMockServiceWorker() {
  await worker.start({
    onUnhandledRequest: 'bypass',
    quiet: true,
  });
  ensureRootContext();
  return worker;
}

export async function stopMockServiceWorker() {
  worker.stop();
}
