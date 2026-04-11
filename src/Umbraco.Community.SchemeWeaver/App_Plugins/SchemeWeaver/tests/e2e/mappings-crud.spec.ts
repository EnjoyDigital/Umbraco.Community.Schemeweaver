import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage of the SchemeWeaver mapping CRUD endpoints over the real
 * management API. Follows the API-driven pattern from the Dynamic Root
 * Config Round-Trip suite: all requests go through `umbracoUi.page.request`
 * (which inherits the authenticated backoffice session) so the tests are
 * isolated from the UI and stay stable even when the backoffice DOM changes.
 *
 * Every test uses a unique synthetic alias that no real seeded content type
 * owns, so a failure cannot strip mappings belonging to productPage /
 * blogArticle / etc. Each test is responsible for cleaning up its own
 * synthetic mapping in a `finally` block.
 */

const BASE = '/umbraco/management/api/v1/schemeweaver';

async function ensureAuthenticated(umbracoUi: any) {
  await umbracoUi.goToBackOffice();
  await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
}

async function deleteMappingIfExists(umbracoUi: any, alias: string) {
  // Swallow errors — cleanup should never fail a test.
  await umbracoUi.page.request
    .delete(`${BASE}/mappings/${alias}`)
    .catch(() => undefined);
}

test.describe('Mappings CRUD (E2E)', () => {
  test('create → get → delete round trip through the management API', async ({ umbracoUi }) => {
    await ensureAuthenticated(umbracoUi);

    const alias = 'e2eCrudRoundTrip';
    const payload = {
      contentTypeAlias: alias,
      contentTypeKey: '00000000-0000-0000-0000-000000000000',
      schemaTypeName: 'Article',
      isEnabled: true,
      isInherited: false,
      propertyMappings: [
        {
          schemaPropertyName: 'headline',
          sourceType: 'property',
          contentTypePropertyAlias: 'title',
          sourceContentTypeAlias: null,
          transformType: null,
          isAutoMapped: false,
          staticValue: null,
          nestedSchemaTypeName: null,
          resolverConfig: null,
          dynamicRootConfig: null,
        },
      ],
    };

    try {
      // CREATE
      const create = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: payload });
      expect(create.ok(), `POST failed: ${create.status()}`).toBeTruthy();

      // GET by alias
      const get = await umbracoUi.page.request.get(`${BASE}/mappings/${alias}`);
      expect(get.ok(), `GET failed: ${get.status()}`).toBeTruthy();
      const saved = await get.json();
      expect(saved.contentTypeAlias).toBe(alias);
      expect(saved.schemaTypeName).toBe('Article');
      expect(saved.propertyMappings).toHaveLength(1);
      expect(saved.propertyMappings[0].schemaPropertyName).toBe('headline');

      // DELETE
      const del = await umbracoUi.page.request.delete(`${BASE}/mappings/${alias}`);
      expect(del.ok(), `DELETE failed: ${del.status()}`).toBeTruthy();

      // Confirm gone
      const missing = await umbracoUi.page.request.get(`${BASE}/mappings/${alias}`);
      expect(missing.status()).toBe(404);
    } finally {
      await deleteMappingIfExists(umbracoUi, alias);
    }
  });

  test('saving the same alias twice updates in place without duplicating', async ({ umbracoUi }) => {
    await ensureAuthenticated(umbracoUi);

    const alias = 'e2eUpdateInPlace';
    const initial = {
      contentTypeAlias: alias,
      contentTypeKey: '00000000-0000-0000-0000-000000000000',
      schemaTypeName: 'Article',
      isEnabled: true,
      isInherited: false,
      propertyMappings: [],
    };
    const updated = { ...initial, schemaTypeName: 'BlogPosting' };

    try {
      const firstSave = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: initial });
      expect(firstSave.ok(), `First POST failed: ${firstSave.status()}`).toBeTruthy();

      const secondSave = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: updated });
      expect(secondSave.ok(), `Second POST failed: ${secondSave.status()}`).toBeTruthy();

      // Only one row should exist — the list endpoint must not return two rows
      // for the same alias.
      const all = await umbracoUi.page.request.get(`${BASE}/mappings`);
      expect(all.ok(), `List GET failed: ${all.status()}`).toBeTruthy();
      const list = (await all.json()) as Array<{ contentTypeAlias: string; schemaTypeName: string }>;
      const matches = list.filter((m) => m.contentTypeAlias === alias);
      expect(matches).toHaveLength(1);
      expect(matches[0].schemaTypeName).toBe('BlogPosting');
    } finally {
      await deleteMappingIfExists(umbracoUi, alias);
    }
  });

  test('delete endpoint returns 204 and removes the mapping from the list', async ({ umbracoUi }) => {
    await ensureAuthenticated(umbracoUi);

    const alias = 'e2eDeleteViaApi';
    const payload = {
      contentTypeAlias: alias,
      contentTypeKey: '00000000-0000-0000-0000-000000000000',
      schemaTypeName: 'Thing',
      isEnabled: true,
      isInherited: false,
      propertyMappings: [],
    };

    try {
      // Seed
      const create = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: payload });
      expect(create.ok()).toBeTruthy();

      // Baseline list
      const beforeList = await umbracoUi.page.request.get(`${BASE}/mappings`);
      const before = (await beforeList.json()) as Array<{ contentTypeAlias: string }>;
      expect(before.some((m) => m.contentTypeAlias === alias)).toBe(true);

      // Delete
      const del = await umbracoUi.page.request.delete(`${BASE}/mappings/${alias}`);
      expect(del.status()).toBe(204);

      // Post-delete list
      const afterList = await umbracoUi.page.request.get(`${BASE}/mappings`);
      const after = (await afterList.json()) as Array<{ contentTypeAlias: string }>;
      expect(after.some((m) => m.contentTypeAlias === alias)).toBe(false);
    } finally {
      await deleteMappingIfExists(umbracoUi, alias);
    }
  });

  test('save with missing schemaTypeName returns 400 BadRequest', async ({ umbracoUi }) => {
    await ensureAuthenticated(umbracoUi);

    const payload = {
      contentTypeAlias: 'e2eValidationTest',
      contentTypeKey: '00000000-0000-0000-0000-000000000000',
      schemaTypeName: '', // invalid
      isEnabled: true,
      isInherited: false,
      propertyMappings: [],
    };

    const response = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: payload });
    expect(response.status()).toBe(400);
  });
});
