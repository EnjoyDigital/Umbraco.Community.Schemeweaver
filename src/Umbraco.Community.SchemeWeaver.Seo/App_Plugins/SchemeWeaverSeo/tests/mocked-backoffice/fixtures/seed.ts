import type { Page } from '@playwright/test';

/** Reset both mock DBs (core mappings and the SEO identity/overrides) between mutating tests. */
export async function resetMockDbs(page: Page): Promise<void> {
  await page.evaluate(() => {
    const w = window as unknown as {
      __schemeweaverMockDb?: { reset: () => void };
      __schemeweaverSeoMockDb?: { reset: () => void };
    };
    if (!w.__schemeweaverMockDb || !w.__schemeweaverSeoMockDb) {
      throw new Error('Mock DB handles are missing on window — the bundles did not register their MSW handlers.');
    }
    w.__schemeweaverMockDb.reset();
    w.__schemeweaverSeoMockDb.reset();
  });
}

/**
 * Boots the backoffice shell and waits until both bundles have registered their handlers.
 * The Umbraco mock user has a fixed section list; the SEO handlers patch `user/current` so the
 * SEO Schema section condition passes (see src/mocks/seo-handlers.ts).
 */
export async function openBackoffice(page: Page, path = '/'): Promise<void> {
  const errors: string[] = [];
  page.on('console', (msg) => {
    if (msg.type() === 'error') errors.push(msg.text());
  });
  page.on('pageerror', (err) => errors.push(err.message));

  await page.goto(path);
  await page.waitForSelector('umb-app', { timeout: 60_000 });
  try {
    await page.waitForFunction(
      () => {
        const w = window as unknown as { __schemeweaverMocksRegistered?: boolean; __schemeweaverSeoMocksRegistered?: boolean };
        return w.__schemeweaverMocksRegistered === true && w.__schemeweaverSeoMocksRegistered === true;
      },
      undefined,
      { timeout: 30_000 },
    );
  } catch (err) {
    throw new Error(`${(err as Error).message}\n\nRecent errors:\n${errors.slice(-20).join('\n') || '  (none)'}`);
  }
}
