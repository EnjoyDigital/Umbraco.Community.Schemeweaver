import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage of the Generate-from-Schema.org flow: POST to
 * /generate-content-type with a valid request and verify that a new Umbraco
 * document type is actually created in the CMS.
 *
 * Uses the management API directly (via `umbracoUi.page.request`) rather than
 * clicking through the entity action UI — the UI flow has many moving parts
 * (modal, picker, property selection) already covered by component tests, so
 * these specs focus on the server-side contract and end-to-end persistence.
 *
 * Each test uses a unique synthetic alias and cleans up in a `finally` block.
 */

const BASE = '/umbraco/management/api/v1/schemeweaver';

// This spec is pure `page.request.*` traffic: no UI interaction at all.
// `page.request` inherits cookies from the Playwright storageState that
// `auth.setup.ts` produced, so we don't need to navigate to the backoffice
// shell on every test — that was ~5 s of pure waste per test.

async function getContentTypeByAlias(umbracoUi: any, alias: string) {
  const response = await umbracoUi.page.request.get(`${BASE}/content-types`);
  if (!response.ok()) return undefined;
  const list = (await response.json()) as Array<{ alias: string; name: string; key: string }>;
  return list.find((ct) => ct.alias === alias);
}

async function deleteDocumentTypeIfExists(umbracoUi: any, key: string) {
  // Use Umbraco's management API to remove the generated document type so the
  // test leaves no residue. 404 is fine — means it's already gone.
  await umbracoUi.page.request
    .delete(`/umbraco/management/api/v1/document-type/${key}`)
    .catch(() => undefined);
}

test.describe('Generate content type from Schema.org (E2E)', () => {
  test('POST /generate-content-type creates a real Umbraco document type', async ({ umbracoUi }) => {
    // Unique alias per run so back-to-back runs (or a prior crash that
    // skipped the finally-block cleanup) can't leave a collision behind.
    const alias = `e2eGeneratedRecipe${Date.now()}`;
    const request = {
      schemaTypeName: 'Recipe',
      documentTypeName: `E2E Generated Recipe ${alias}`,
      documentTypeAlias: alias,
      selectedProperties: ['name', 'description', 'recipeIngredient'],
      propertyGroupName: 'Content',
    };

    // Belt-and-braces: if a previous run died mid-flight leaving a content
    // type with the target alias, nuke it before we start.
    const stale = await getContentTypeByAlias(umbracoUi, alias);
    if (stale) {
      await deleteDocumentTypeIfExists(umbracoUi, stale.key);
    }

    let createdKey: string | undefined;

    try {
      const response = await umbracoUi.page.request.post(
        `${BASE}/generate-content-type`,
        { data: request },
      );

      expect(response.ok(), `generate-content-type failed: ${response.status()}`).toBeTruthy();
      const body = (await response.json()) as { key?: string };
      expect(body.key, 'response should include the new content type key').toBeTruthy();
      createdKey = body.key;

      // Verify the new content type shows up in the list.
      const persisted = await getContentTypeByAlias(umbracoUi, alias);
      expect(persisted, `document type "${alias}" not found after generation`).toBeTruthy();
      expect(persisted!.name).toBe(request.documentTypeName);
    } finally {
      if (createdKey) {
        await deleteDocumentTypeIfExists(umbracoUi, createdKey);
      }
    }
  });

  test('missing schemaTypeName returns 400 BadRequest', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.post(
      `${BASE}/generate-content-type`,
      {
        data: {
          schemaTypeName: '',
          documentTypeName: 'Invalid',
          documentTypeAlias: 'e2eInvalid',
          selectedProperties: [],
          propertyGroupName: 'Content',
        },
      },
    );

    expect(response.status()).toBe(400);
  });

  test('missing documentTypeName returns 400 BadRequest', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.post(
      `${BASE}/generate-content-type`,
      {
        data: {
          schemaTypeName: 'Article',
          documentTypeName: '',
          documentTypeAlias: 'e2eInvalid2',
          selectedProperties: [],
          propertyGroupName: 'Content',
        },
      },
    );

    expect(response.status()).toBe(400);
  });
});
