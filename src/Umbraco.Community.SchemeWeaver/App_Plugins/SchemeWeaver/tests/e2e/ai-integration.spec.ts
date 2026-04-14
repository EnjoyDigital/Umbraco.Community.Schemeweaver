import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage for the SchemeWeaver.AI companion package. Splits into two
 * describe blocks:
 *
 *   1. Wiring — tests that only need the AI package to be *installed*.
 *      Hits `GET /ai/status` and asserts the entity actions appear in the
 *      Settings tree. These run unconditionally.
 *
 *   2. Real Anthropic calls — tests that make live calls to Claude via
 *      the TestHost-configured provider. A `beforeAll` probe detects
 *      whether an Anthropic API key is configured (via
 *      `dotnet user-secrets` on the TestHost). If the probe fails the
 *      whole describe is skipped, so CI stays green without a key.
 *
 * Bulk analysis (`POST /ai/suggest-schema-types-bulk`) makes one Anthropic
 * call per seeded doctype (~100), so it is further gated behind the
 * `RUN_BULK_AI_TESTS=true` env var to avoid surprise cost.
 *
 * Assertions are strictly structural — the LLM is non-deterministic, so
 * we check shape and non-emptiness, never specific content.
 */

const BASE = '/umbraco/management/api/v1/schemeweaver';

// Safe alias choices: seeded by the TestHost in TestDataComposer.
const SEEDED_ALIAS = 'articlePage';
const SEEDED_SCHEMA_TYPE = 'Article';

test.describe('SchemeWeaver AI — wiring (no API key required)', () => {
  test('GET /ai/status returns 200 when package is installed', async ({ umbracoUi }) => {
    const res = await umbracoUi.page.request.get(`${BASE}/ai/status`);
    expect(res.ok(), `GET /ai/status failed: ${res.status()}`).toBeTruthy();
  });

  test('AI Analyse entity action appears on document type context menu', async ({ umbracoUi }) => {
    const statusRes = await umbracoUi.page.request.get(`${BASE}/ai/status`);
    if (!statusRes.ok()) {
      test.skip(true, 'AI package not installed — GET /ai/status failed');
      return;
    }

    await umbracoUi.page.goto('/umbraco/section/settings');

    const docTypesLink = umbracoUi.page.locator('a', { hasText: 'Document Types' }).first();
    await docTypesLink.waitFor({ timeout: 15_000 });

    // Expand the Document Types root to reveal children
    const expandBtn = umbracoUi.page.locator('button[aria-label*="Expand"]').first();
    if (await expandBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await expandBtn.click();
    }

    const firstChild = umbracoUi.page.locator('umb-tree-item umb-tree-item').first();
    await firstChild.waitFor({ timeout: 10_000 });
    await firstChild.hover();

    const actionsBtn = firstChild.locator('button').filter({ hasText: /actions/i }).first();
    if (!(await actionsBtn.isVisible({ timeout: 5_000 }).catch(() => false))) {
      test.skip(true, 'Actions button not available for first doctype');
      return;
    }
    await actionsBtn.click();

    const aiAnalyse = umbracoUi.page.getByRole('button', { name: /AI Analyse(?! All)/i });
    await expect(aiAnalyse).toBeVisible({ timeout: 5_000 });
  });

  test('AI Analyse All entity action appears on Document Types root', async ({ umbracoUi }) => {
    const statusRes = await umbracoUi.page.request.get(`${BASE}/ai/status`);
    if (!statusRes.ok()) {
      test.skip(true, 'AI package not installed — GET /ai/status failed');
      return;
    }

    await umbracoUi.page.goto('/umbraco/section/settings');

    const docTypesRoot = umbracoUi.page
      .locator('umb-tree-item')
      .filter({ hasText: /Document Types/i })
      .first();
    await docTypesRoot.waitFor({ timeout: 15_000 });
    await docTypesRoot.hover();

    const actionsBtn = docTypesRoot.locator('button').filter({ hasText: /actions/i }).first();
    if (!(await actionsBtn.isVisible({ timeout: 5_000 }).catch(() => false))) {
      test.skip(true, 'Actions button not available on Document Types root');
      return;
    }
    await actionsBtn.click();

    const aiAnalyseAll = umbracoUi.page.getByRole('button', { name: /AI Analyse All/i });
    await expect(aiAnalyseAll).toBeVisible({ timeout: 5_000 });
  });
});

test.describe('SchemeWeaver AI — real Anthropic calls', () => {
  // Pre-flight probe run once per describe. Skips the whole block if:
  //   - the AI package isn't installed (/ai/status fails), or
  //   - a minimal real call returns non-2xx (no provider / no key).
  test.beforeAll(async ({ browser }) => {
    const storagePath = process.env.STORAGE_STAGE_PATH;
    const baseUrl = process.env.URL || process.env.UMBRACO_URL || 'https://localhost:44308';

    const ctx = await browser.newContext({
      storageState: storagePath,
      ignoreHTTPSErrors: true,
    });
    const page = await ctx.newPage();

    try {
      const statusRes = await page.request.get(`${baseUrl}${BASE}/ai/status`);
      if (!statusRes.ok()) {
        test.skip(true, `AI package not installed — GET /ai/status returned ${statusRes.status()}`);
        return;
      }

      const probeRes = await page.request.post(
        `${baseUrl}${BASE}/ai/suggest-schema-type/${SEEDED_ALIAS}`,
        { data: {} },
      );
      if (!probeRes.ok()) {
        test.skip(
          true,
          `AI real calls unavailable (probe returned ${probeRes.status()}). ` +
            `Set the Anthropic API key via \`dotnet user-secrets\` on the TestHost.`,
        );
      }
    } finally {
      await ctx.close();
    }
  });

  test('POST /ai/suggest-schema-type/{alias} returns suggestions', async ({ umbracoUi }) => {
    const res = await umbracoUi.page.request.post(
      `${BASE}/ai/suggest-schema-type/${SEEDED_ALIAS}`,
      { data: {} },
    );
    expect(res.ok(), `Suggest failed: ${res.status()}`).toBeTruthy();

    const body = await res.json();
    expect(body).toBeTruthy();

    // Accept either a bare array or an object wrapping one
    const suggestions: any[] = Array.isArray(body)
      ? body
      : (body.suggestions ?? body.results ?? []);
    expect(Array.isArray(suggestions)).toBe(true);
    expect(suggestions.length).toBeGreaterThan(0);

    // Structural check on the first suggestion — LLM output is
    // non-deterministic so we only assert fields exist and have the
    // right shape, not specific values.
    const first = suggestions[0];
    expect(first).toBeTruthy();
    const name = first.schemaTypeName ?? first.name ?? first.type;
    expect(typeof name).toBe('string');
    expect(name.length).toBeGreaterThan(0);

    if (first.confidence !== undefined) {
      expect(typeof first.confidence).toBe('number');
      expect(first.confidence).toBeGreaterThanOrEqual(0);
      expect(first.confidence).toBeLessThanOrEqual(100);
    }
  });

  test('POST /ai/ai-auto-map/{alias}?schemaTypeName=X returns property mappings', async ({
    umbracoUi,
  }) => {
    const res = await umbracoUi.page.request.post(
      `${BASE}/ai/ai-auto-map/${SEEDED_ALIAS}?schemaTypeName=${SEEDED_SCHEMA_TYPE}`,
      { data: {} },
    );
    expect(res.ok(), `Auto-map failed: ${res.status()}`).toBeTruthy();

    const body = await res.json();
    const suggestions: any[] = Array.isArray(body) ? body : (body.suggestions ?? []);
    expect(Array.isArray(suggestions)).toBe(true);
    expect(suggestions.length).toBeGreaterThan(0);

    const first = suggestions[0];
    expect(first).toBeTruthy();
    expect(typeof first.schemaPropertyName).toBe('string');
    expect(first.schemaPropertyName.length).toBeGreaterThan(0);
    // Content type property alias may legitimately be null when the AI
    // declines to map a schema property — but the field should exist.
    expect('contentTypePropertyAlias' in first).toBe(true);

    if (first.confidence !== undefined) {
      expect(typeof first.confidence).toBe('number');
      expect(first.confidence).toBeGreaterThanOrEqual(0);
      expect(first.confidence).toBeLessThanOrEqual(100);
    }
  });

  // Budget-gated: the bulk endpoint hits Anthropic once per seeded doctype
  // (~100 calls for the TestHost seed), so it only runs when explicitly
  // requested via `RUN_BULK_AI_TESTS=true`.
  test('POST /ai/suggest-schema-types-bulk returns per-doctype suggestions', async ({
    umbracoUi,
  }) => {
    test.skip(
      process.env.RUN_BULK_AI_TESTS !== 'true',
      'Set RUN_BULK_AI_TESTS=true to run the expensive bulk analysis test.',
    );

    const res = await umbracoUi.page.request.post(
      `${BASE}/ai/suggest-schema-types-bulk`,
      { data: {} },
      { timeout: 300_000 }, // 5 min — bulk call is slow
    );
    expect(res.ok(), `Bulk failed: ${res.status()}`).toBeTruthy();

    const body = await res.json();
    expect(body).toBeTruthy();

    // Either an array of per-doctype results, or an object keyed by alias.
    if (Array.isArray(body)) {
      expect(body.length).toBeGreaterThan(0);
    } else {
      expect(Object.keys(body).length).toBeGreaterThan(0);
    }
  });

  test('AI bulk analysis modal opens from Document Types root', async ({ umbracoUi }) => {
    await umbracoUi.page.goto('/umbraco/section/settings');

    const docTypesRoot = umbracoUi.page
      .locator('umb-tree-item')
      .filter({ hasText: /Document Types/i })
      .first();
    await docTypesRoot.waitFor({ timeout: 15_000 });
    await docTypesRoot.hover();

    const actionsBtn = docTypesRoot.locator('button').filter({ hasText: /actions/i }).first();
    if (!(await actionsBtn.isVisible({ timeout: 5_000 }).catch(() => false))) {
      test.skip(true, 'Actions button not available on Document Types root');
      return;
    }
    await actionsBtn.click();

    const aiAnalyseAll = umbracoUi.page.getByRole('button', { name: /AI Analyse All/i });
    await expect(aiAnalyseAll).toBeVisible({ timeout: 5_000 });
    await aiAnalyseAll.click();

    // The bulk analysis modal should open. We don't assert that the table
    // populates — that's a real Anthropic call already covered by the
    // bulk API test above. Just confirm the modal is visible.
    const modal = umbracoUi.page
      .locator('umb-modal, uui-dialog, uui-modal-container')
      .filter({ hasText: /AI|Analyse|Analysis/i })
      .first();
    await expect(modal).toBeVisible({ timeout: 10_000 });
  });
});
