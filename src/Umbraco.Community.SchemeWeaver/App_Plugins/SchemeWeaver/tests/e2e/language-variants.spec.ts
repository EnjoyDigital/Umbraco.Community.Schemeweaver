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

// Well-known key assigned by TestDataSeeder.VariantArticleContentKey.
// Using a deterministic GUID avoids the need to discover the content key
// via the Umbraco management tree API, which requires OAuth tokens the
// Playwright storageState doesn't carry.
const VARIANT_CONTENT_KEY = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

test.describe('Language Variants (E2E)', () => {

  test('preview with culture=de-DE returns German values and inLanguage', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.post(
      `${BASE}/mappings/${VARIANT_ALIAS}/preview?contentKey=${VARIANT_CONTENT_KEY}&culture=de-DE`,
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
    const response = await umbracoUi.page.request.post(
      `${BASE}/mappings/${VARIANT_ALIAS}/preview?contentKey=${VARIANT_CONTENT_KEY}&culture=en-US`,
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
    const response = await umbracoUi.page.request.post(
      `${BASE}/mappings/${VARIANT_ALIAS}/preview?contentKey=${VARIANT_CONTENT_KEY}`,
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
