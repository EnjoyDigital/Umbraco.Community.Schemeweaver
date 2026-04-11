import { expect, test } from '@playwright/test';
import { openBackoffice, resetMockDb } from './fixtures/seed';

const BASE = '/umbraco/management/api/v1/schemeweaver';

/**
 * Proves that SchemeWeaver's MSW handlers actually intercept real HTTP
 * traffic inside the mocked backoffice — the wiring from Tier A (CMS
 * patch) through Tier B (src/index.ts addMockHandlers call) all the way
 * to the in-memory mock DB. If this passes, every other HTTP-shaped UI
 * test in the tier has a reliable backend.
 *
 * We drive the fetches from `page.evaluate` rather than `page.request`
 * because `page.request` bypasses MSW (it talks to the Node-side
 * Playwright API, not the browser), which would defeat the whole point.
 */
test.describe('Mocked Backoffice — MSW handlers intercept', () => {
  test.beforeEach(async ({ page }) => {
    await openBackoffice(page);
    await resetMockDb(page);
  });

  test('GET /mappings returns seeded mock data', async ({ page }) => {
    const payload = await page.evaluate(async (url) => {
      const res = await fetch(url);
      return { status: res.status, body: (await res.json()) as unknown };
    }, BASE + '/mappings');

    expect(payload.status).toBe(200);
    expect(Array.isArray(payload.body)).toBe(true);
    // The mock DB seeds a handful of default mappings — proving the
    // response is coming from our handler and not from an
    // unhandled-request passthrough.
    expect((payload.body as unknown[]).length).toBeGreaterThan(0);
  });

  test('GET /content-types returns seeded content types', async ({ page }) => {
    const payload = await page.evaluate(async (url) => {
      const res = await fetch(url);
      return { status: res.status, body: (await res.json()) as Array<{ alias: string; name: string }> };
    }, BASE + '/content-types');

    expect(payload.status).toBe(200);
    expect(payload.body.length).toBeGreaterThan(0);
    // Every content type must carry the alias + name fields the DTO promises.
    for (const ct of payload.body) {
      expect(typeof ct.alias).toBe('string');
      expect(typeof ct.name).toBe('string');
    }
  });

  test('GET /schema-types supports the search param', async ({ page }) => {
    const payload = await page.evaluate(async (url) => {
      const res = await fetch(url);
      return { status: res.status, body: (await res.json()) as Array<{ name: string }> };
    }, BASE + '/schema-types?search=Article');

    expect(payload.status).toBe(200);
    expect(Array.isArray(payload.body)).toBe(true);
    // Search should narrow the list — if it didn't, we'd be hitting an
    // unfiltered passthrough or a stubbed handler that ignores params.
    expect(payload.body.some((t) => t.name.toLowerCase().includes('article'))).toBe(true);
  });
});
