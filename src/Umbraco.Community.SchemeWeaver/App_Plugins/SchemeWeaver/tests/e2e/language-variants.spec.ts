import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage for language-variant JSON-LD preview. The TestHost seeds a
 * "variantArticle" content type (Variations = Culture) with a published
 * content node carrying both en-US and de-DE property values plus an
 * "Article" SchemeWeaver mapping.
 *
 * These tests exercise the `?culture=` query parameter on the preview
 * endpoint added by the culture plumbing work (Worktree A). Until that
 * branch merges, the culture param is ignored and these tests will fail —
 * that is expected.
 *
 * Pure API suite — `page.request.*` inherits cookies from storageState
 * produced by `auth.setup.ts`.
 */

const BASE = '/umbraco/management/api/v1/schemeweaver';
const VARIANT_ALIAS = 'variantArticle';

/**
 * Resolve the content key of the first published "variantArticle" node.
 * Uses Umbraco's content delivery or management API to find it.
 */
async function getVariantArticleContentKey(umbracoUi: any): Promise<string | undefined> {
  // The SchemeWeaver content-types endpoint returns all Umbraco content types
  // but not content items. Use the Umbraco management API to find content
  // of type variantArticle.
  const response = await umbracoUi.page.request.get(
    '/umbraco/management/api/v1/document?filter=contentType:variantArticle&take=1',
  );
  if (!response.ok()) {
    // Fallback: try the tree children endpoint at the root
    const rootResponse = await umbracoUi.page.request.get(
      '/umbraco/management/api/v1/tree/document/children?parentId=-1&take=50',
    );
    if (!rootResponse.ok()) return undefined;
    const tree = await rootResponse.json();
    const items = tree.items ?? tree;
    const match = Array.isArray(items)
      ? items.find(
          (item: any) =>
            item.contentTypeAlias === VARIANT_ALIAS ||
            item.variants?.some((v: any) => v.name === 'Test Variant Article'),
        )
      : undefined;
    return match?.id ?? match?.key;
  }
  const body = await response.json();
  const items = body.items ?? body;
  return Array.isArray(items) && items.length > 0 ? items[0].id ?? items[0].key : undefined;
}

test.describe('Language Variants (E2E)', () => {
  let contentKey: string | undefined;

  test.beforeAll(async ({ browser }) => {
    // Resolve the variant article content key once for all tests.
    // We need a page context with storageState for authenticated requests.
    const context = await browser.newContext({
      storageState: process.env.STORAGE_STAGE_PATH,
      ignoreHTTPSErrors: true,
    });
    const page = await context.newPage();
    const response = await page.request.get(
      `${process.env.URL || process.env.UMBRACO_URL || 'https://localhost:44308'}/umbraco/management/api/v1/tree/document/children?parentId=-1&take=50`,
    );
    if (response.ok()) {
      const tree = await response.json();
      const items = tree.items ?? tree;
      if (Array.isArray(items)) {
        const match = items.find(
          (item: any) =>
            item.contentTypeAlias === VARIANT_ALIAS ||
            item.variants?.some((v: any) => v.name === 'Test Variant Article'),
        );
        contentKey = match?.id ?? match?.key;
      }
    }
    await context.close();
  });

  test('preview with culture=de-DE returns German values and inLanguage', async ({ umbracoUi }) => {
    test.skip(!contentKey, 'Variant article content not found in TestHost — seed may not have run');

    const response = await umbracoUi.page.request.post(
      `${BASE}/mappings/${VARIANT_ALIAS}/preview?contentKey=${contentKey}&culture=de-DE`,
    );
    expect(response.ok(), `Preview failed: ${response.status()}`).toBeTruthy();

    const body = await response.json();
    const jsonLd = typeof body.jsonLd === 'string' ? body.jsonLd : JSON.stringify(body.jsonLd ?? body);

    expect(jsonLd).toContain('Sieben Dinge');
    expect(jsonLd).toContain('Deutscher Textkörper');
    expect(jsonLd).toContain('"inLanguage"');
    expect(jsonLd).toContain('de-DE');
  });

  test('preview with culture=en-US returns English values and inLanguage', async ({ umbracoUi }) => {
    test.skip(!contentKey, 'Variant article content not found in TestHost — seed may not have run');

    const response = await umbracoUi.page.request.post(
      `${BASE}/mappings/${VARIANT_ALIAS}/preview?contentKey=${contentKey}&culture=en-US`,
    );
    expect(response.ok(), `Preview failed: ${response.status()}`).toBeTruthy();

    const body = await response.json();
    const jsonLd = typeof body.jsonLd === 'string' ? body.jsonLd : JSON.stringify(body.jsonLd ?? body);

    expect(jsonLd).toContain('Seven things');
    expect(jsonLd).toContain('English body text');
    expect(jsonLd).toContain('"inLanguage"');
    expect(jsonLd).toContain('en-US');
  });

  test('preview without culture param returns default language values', async ({ umbracoUi }) => {
    test.skip(!contentKey, 'Variant article content not found in TestHost — seed may not have run');

    const response = await umbracoUi.page.request.post(
      `${BASE}/mappings/${VARIANT_ALIAS}/preview?contentKey=${contentKey}`,
    );
    // Should succeed (backwards compatible) — the server picks the default culture
    expect(response.ok(), `Preview without culture failed: ${response.status()}`).toBeTruthy();

    const body = await response.json();
    const jsonLd = typeof body.jsonLd === 'string' ? body.jsonLd : JSON.stringify(body.jsonLd ?? body);

    // Default language is en-US, so English values should appear
    expect(jsonLd).toContain('Seven things');
  });

  test('variant article mapping exists with Article schema type', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.get(`${BASE}/mappings/${VARIANT_ALIAS}`);
    expect(response.ok(), `GET mapping failed: ${response.status()}`).toBeTruthy();

    const mapping = await response.json();
    expect(mapping.contentTypeAlias).toBe(VARIANT_ALIAS);
    expect(mapping.schemaTypeName).toBe('Article');
    expect(mapping.propertyMappings.length).toBeGreaterThanOrEqual(2);

    const propNames = mapping.propertyMappings.map((p: any) => p.schemaPropertyName);
    expect(propNames).toContain('Name');
    expect(propNames).toContain('ArticleBody');
  });
});
