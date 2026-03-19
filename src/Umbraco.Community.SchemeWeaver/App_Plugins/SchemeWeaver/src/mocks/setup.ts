import { setupWorker } from 'msw/browser';
import { handlers } from './handlers.js';

export const worker = setupWorker(...handlers);

export async function startMockServiceWorker() {
  await worker.start({
    onUnhandledRequest: 'bypass',
    quiet: true,
  });
  return worker;
}

export async function stopMockServiceWorker() {
  worker.stop();
}
