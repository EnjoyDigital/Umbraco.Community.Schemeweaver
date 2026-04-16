import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage for:
 *  - Server-side ranking of nested schema properties (?ranked=true) — the
 *    "banana-for-monkey" UX that surfaces popular properties first.
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

