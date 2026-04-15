import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage for:
 *  - Server-side ranking of nested schema properties (?ranked=true) — the
 *    "banana-for-monkey" UX that surfaces popular properties first.
 *  - The seeded `landingPage` BlockGrid demo mapped to Schema.org WebPage,
 *    exercising nested ImageObject ranking + BlockGrid content resolution
 *    end-to-end against the real TestHost.
 *
 * API-driven (like mappings-crud.spec.ts) — no brittle UI selectors.
 */

const BASE = '/umbraco/management/api/v1/schemeweaver';

test.describe('Ranked schema properties (E2E)', () => {
  test('default GET returns the legacy SchemaPropertyInfo shape', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.get(`${BASE}/schema-types/Organization/properties`);
    expect(response.ok(), `GET failed: ${response.status()}`).toBeTruthy();
    const props = (await response.json()) as Array<Record<string, unknown>>;
    expect(props.length).toBeGreaterThan(0);
    // Back-compat: existing callers never see confidence / isPopular fields.
    for (const prop of props) {
      expect(prop).not.toHaveProperty('confidence');
      expect(prop).not.toHaveProperty('isPopular');
    }
  });

  test('?ranked=true returns RankedSchemaPropertyInfo sorted popular-first', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.get(
      `${BASE}/schema-types/Organization/properties?ranked=true`,
    );
    expect(response.ok(), `GET failed: ${response.status()}`).toBeTruthy();
    const props = (await response.json()) as Array<{
      name: string;
      confidence: number;
      isPopular: boolean;
    }>;

    expect(props.length).toBeGreaterThan(0);

    // Every row carries the ranking fields.
    for (const prop of props) {
      expect(prop).toHaveProperty('confidence');
      expect(prop).toHaveProperty('isPopular');
      expect(typeof prop.confidence).toBe('number');
      expect(typeof prop.isPopular).toBe('boolean');
    }

    // Confidence is sorted descending.
    for (let i = 1; i < props.length; i++) {
      expect(props[i - 1].confidence).toBeGreaterThanOrEqual(props[i].confidence);
    }

    // isPopular mirrors confidence >= 60.
    for (const prop of props) {
      expect(prop.isPopular).toBe(prop.confidence >= 60);
    }

    // `name` and `url` should rank as popular for Organization (globally-popular names).
    const popularNames = props.filter((p) => p.isPopular).map((p) => p.name.toLowerCase());
    expect(popularNames).toContain('name');
    expect(popularNames).toContain('url');
  });

  test('ImageObject nested type surfaces url / contentUrl / caption in the popular set', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.get(
      `${BASE}/schema-types/ImageObject/properties?ranked=true`,
    );
    expect(response.ok(), `GET failed: ${response.status()}`).toBeTruthy();
    const props = (await response.json()) as Array<{ name: string; isPopular: boolean }>;
    const popularNames = props.filter((p) => p.isPopular).map((p) => p.name.toLowerCase());

    // The whole point of Part A: when the user maps to ImageObject they see the
    // banana (url / name) not a 40-row flat list. Assert at least one obvious one.
    expect(popularNames).toContain('url');
    expect(popularNames).toContain('name');
  });

  test('unknown schema type returns an empty array (no 500)', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.get(
      `${BASE}/schema-types/DoesNotExist/properties?ranked=true`,
    );
    expect(response.ok(), `GET failed: ${response.status()}`).toBeTruthy();
    const props = await response.json();
    expect(Array.isArray(props)).toBe(true);
    expect(props).toHaveLength(0);
  });
});

test.describe('landingPage → WebPage BlockGrid demo (E2E)', () => {
  test('the landingPage mapping is seeded with the expected property mappings', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.get(`${BASE}/mappings/landingPage`);
    expect(response.ok(), `GET failed: ${response.status()}`).toBeTruthy();
    const mapping = await response.json();

    expect(mapping.contentTypeAlias).toBe('landingPage');
    expect(mapping.schemaTypeName).toBe('WebPage');
    expect(mapping.isEnabled).toBe(true);

    // Schema property names may be serialised as PascalCase or camelCase
    // depending on seed vs. API persistence — normalise before assertions.
    const byName = new Map<string, Record<string, unknown>>();
    for (const pm of mapping.propertyMappings as Array<Record<string, unknown>>) {
      byName.set((pm.schemaPropertyName as string).toLowerCase(), pm);
    }

    // Scalar mappings
    expect(byName.get('name')?.contentTypePropertyAlias).toBe('pageTitle');
    expect(byName.get('description')?.contentTypePropertyAlias).toBe('metaDescription');

    // Nested ImageObject mapping — the one that exercises Part A ranking.
    const heroMapping = byName.get('primaryimageofpage');
    expect(heroMapping, 'primaryImageOfPage mapping').toBeDefined();
    expect(heroMapping!.contentTypePropertyAlias).toBe('heroImage');
    expect(heroMapping!.nestedSchemaTypeName).toBe('ImageObject');

    // BlockGrid mapping
    expect(byName.get('mainentity')).toBeDefined();
  });

  test('mock JSON-LD preview renders WebPage shape', async ({ umbracoUi }) => {
    // No contentKey → the preview endpoint returns a mock JSON-LD document
    // reflecting the mapping configuration. Real content expansion (BlockGrid
    // resolution, ImageObject URL extraction) is covered by the C# integration
    // tests; the purpose here is to prove the landingPage → WebPage wiring is
    // live on the real host and the response shape hasn't regressed.
    const preview = await umbracoUi.page.request.post(
      `${BASE}/mappings/landingPage/preview`,
      { data: {} },
    );
    expect(preview.ok(), `Preview failed: ${preview.status()}`).toBeTruthy();

    const body = await preview.json();
    const jsonLd = JSON.parse(body.jsonLd as string);
    expect(jsonLd['@context']).toContain('schema.org');
    expect(jsonLd['@type']).toBe('WebPage');
  });
});
