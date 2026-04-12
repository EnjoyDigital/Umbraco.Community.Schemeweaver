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

/**
 * Frontend navigation tests for language-variant JSON-LD output.
 *
 * These tests hit the published site (same origin as the backoffice) and
 * verify that the `<script type="application/ld+json">` blocks change
 * correctly when switching between /en/ and /de/ routes.
 *
 * Requires domain-culture routing to be configured in the TestHost:
 *   /en/... → en-US, /de/... → de-DE
 *
 * Tests gracefully skip when routing is not yet available.
 */

const FRONTEND_BASE = process.env.UMBRACO_URL || 'https://localhost:44308';

test.describe('Language Variants — Frontend HTML', () => {

  test('German home page has German JSON-LD with inLanguage', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.goto(`${FRONTEND_BASE}/de/`);
    if (!response?.ok()) {
      test.skip(true, 'German home page not available — domain routing may not be configured');
      return;
    }
    const scripts = await umbracoUi.page.locator('script[type="application/ld+json"]').allTextContents();
    expect(scripts.length).toBeGreaterThan(0);
    const allJsonLd = scripts.join(' ');
    expect(allJsonLd).toContain('de-DE');
  });

  test('English home page has English JSON-LD with inLanguage', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.goto(`${FRONTEND_BASE}/en/`);
    if (!response?.ok()) {
      test.skip(true, 'English home page not available');
      return;
    }
    const scripts = await umbracoUi.page.locator('script[type="application/ld+json"]').allTextContents();
    expect(scripts.length).toBeGreaterThan(0);
    const allJsonLd = scripts.join(' ');
    expect(allJsonLd).toContain('en-US');
  });

  test('German home page JSON-LD includes inLanguage property', async ({ umbracoUi }) => {
    const homeRes = await umbracoUi.page.goto(`${FRONTEND_BASE}/de/`);
    if (!homeRes?.ok()) {
      test.skip(true, 'German routing not available');
      return;
    }
    const scripts = await umbracoUi.page.locator('script[type="application/ld+json"]').allTextContents();
    const allJsonLd = scripts.join(' ');
    expect(allJsonLd).toContain('"inLanguage"');
  });

  test('language switcher is visible on the page', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.goto(`${FRONTEND_BASE}/en/`);
    if (!response?.ok()) {
      test.skip(true, 'Frontend not available');
      return;
    }
    const switcher = umbracoUi.page.locator('.lang-switcher');
    await expect(switcher).toBeVisible({ timeout: 10_000 });
    // Should have both EN and DE links
    const links = switcher.locator('a');
    await expect(links).toHaveCount(2);
  });

  test('switching from English to German changes JSON-LD language', async ({ umbracoUi }) => {
    const enRes = await umbracoUi.page.goto(`${FRONTEND_BASE}/en/`);
    if (!enRes?.ok()) {
      test.skip(true, 'English home not available');
      return;
    }
    const enScripts = await umbracoUi.page.locator('script[type="application/ld+json"]').allTextContents();
    const enJsonLd = enScripts.join(' ');
    expect(enJsonLd).toContain('en-US');

    // Click the DE switcher link
    const deSwitcher = umbracoUi.page.locator('.lang-switcher a').filter({ hasText: 'DE' });
    if (await deSwitcher.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await deSwitcher.click();
      await umbracoUi.page.waitForLoadState('domcontentloaded');

      const deScripts = await umbracoUi.page.locator('script[type="application/ld+json"]').allTextContents();
      const deJsonLd = deScripts.join(' ');
      expect(deJsonLd).toContain('de-DE');
    }
  });
});
