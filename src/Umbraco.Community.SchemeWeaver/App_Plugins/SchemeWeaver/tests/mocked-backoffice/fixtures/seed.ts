import type { Page } from '@playwright/test';

/**
 * The SchemeWeaver mock DB (`src/mocks/data/schemeweaver.db.ts`) is a single
 * module-scoped singleton inside the Vite dev server. `context.newPage()` and
 * even `browser.newContext()` share the same dev server, so spec order and
 * prior mutations can leak. Call `resetMockDb` in `beforeEach` for any test
 * that mutates mappings.
 *
 * SchemeWeaver's `src/index.ts` stashes the DB on
 * `window.__schemeweaverMockDb` during the MSW handler-registration phase,
 * so the reset is a single `page.evaluate`.
 */
export async function resetMockDb(page: Page): Promise<void> {
  await page.evaluate(() => {
    const db = (window as unknown as { __schemeweaverMockDb?: { reset: () => void } })
      .__schemeweaverMockDb;
    if (!db) {
      throw new Error(
        '__schemeweaverMockDb is missing on window — SchemeWeaver failed to register its MSW handlers. ' +
          'Check the browser console for [SchemeWeaver] errors.',
      );
    }
    db.reset();
  });
}

/** Convenience helper: boot the backoffice shell and wait for manifests. */
export async function openBackoffice(page: Page, path = '/'): Promise<void> {
  // Collect console + page errors so a failure in the mocked backoffice
  // boot shows up in the Playwright report instead of disappearing into
  // the browser devtools.
  const consoleMessages: string[] = [];
  const pageErrors: string[] = [];
  page.on('console', (msg) => {
    if (['error', 'warning'].includes(msg.type())) {
      consoleMessages.push(`[${msg.type()}] ${msg.text()}`);
    }
  });
  page.on('pageerror', (err) => {
    pageErrors.push(err.message);
  });

  await page.goto(path);
  // Wait for the Umbraco app shell. `umb-app` only renders once the main
  // app element has finished bootstrapping. We also need SchemeWeaver's
  // async handler registration to complete before any test assertions
  // run — poll for the flag the bootstrap sets.
  await page.waitForSelector('umb-app', { timeout: 60_000 });
  try {
    await page.waitForFunction(
      () =>
        (window as unknown as { __schemeweaverMocksRegistered?: boolean }).__schemeweaverMocksRegistered ===
        true,
      undefined,
      { timeout: 30_000 },
    );
  } catch (err) {
    // Dump anything useful before failing so debugging doesn't require
    // a manual dev server run.
    const diag = [
      'Timed out waiting for __schemeweaverMocksRegistered.',
      'Recent console errors/warnings:',
      ...(consoleMessages.length ? consoleMessages.slice(-20) : ['  (none)']),
      'Page errors:',
      ...(pageErrors.length ? pageErrors.slice(-20) : ['  (none)']),
    ].join('\n');
    throw new Error(`${(err as Error).message}\n\n${diag}`);
  }
}
