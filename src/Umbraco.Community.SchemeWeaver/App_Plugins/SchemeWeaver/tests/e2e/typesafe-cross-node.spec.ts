import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * Cross-node auto-map rows in the backoffice (TypeSafe satellite v2).
 *
 * A `parent` / `ancestor` / `sibling` suggestion arrives from the auto-map
 * endpoint with only the related type's alias (`suggestedSourceContentTypeAlias`).
 * The workspace view hydrates it (`enrichRelatedSourceRows`) so the row shows the
 * related document type and a populated property combobox without a re-browse,
 * and the document-type Save persists the row with its source type and alias.
 *
 * Self-restoring: the mapping is snapshotted first and put back afterwards.
 * Skips cleanly on a host whose mapper emits no cross-node row (the heuristic
 * on its own never does), so the suite stays green without the satellite.
 */

const BASE = '/umbraco/management/api/v1/schemeweaver';
const DOC_TYPE_NAME = 'Department Page';
const ALIAS = 'departmentPage';
const RELATED = new Set(['parent', 'ancestor', 'sibling']);

async function getMapping(umbracoUi: any): Promise<any | null> {
  const res = await umbracoUi.page.request.get(`${BASE}/mappings/${ALIAS}`);
  return res.ok() ? res.json() : null;
}

async function goToDocTypeSchemaTab(umbracoUi: any, docTypeName: string) {
  await umbracoUi.goToBackOffice();
  const res = await umbracoUi.page.request.get(`${BASE}/content-types`);
  expect(res.ok(), `content-types GET failed: ${res.status()}`).toBeTruthy();
  const docType = (await res.json()).find((ct: any) => ct.name === docTypeName);
  expect(docType, `Document type "${docTypeName}" not found`).toBeTruthy();

  await umbracoUi.page.goto(`/umbraco/section/settings/workspace/document-type/edit/${docType.key}`);
  const schemaTab = umbracoUi.page.getByRole('tab', { name: /Schema\.org/i });
  await schemaTab.waitFor({ timeout: 15_000 });
  await schemaTab.click();
  await umbracoUi.page.locator('schemeweaver-schema-mapping-view').waitFor({ timeout: 15_000 });
}

test.describe('TypeSafe cross-node suggestions in the backoffice', () => {
  test('an auto-mapped parent/ancestor/sibling row shows its related type and property list, and saves with its source type', async ({ umbracoUi }) => {
    test.setTimeout(150_000);
    const page = umbracoUi.page;

    const snapshot = await getMapping(umbracoUi);
    expect(snapshot, `${ALIAS} must be mapped in the TestHost`).toBeTruthy();

    // Ask the endpoint first: on a host without the satellite there is nothing to click through.
    const probe = await page.request.post(
      `${BASE}/mappings/${ALIAS}/auto-map?schemaTypeName=${encodeURIComponent(snapshot.schemaTypeName)}`,
      { data: {} },
    );
    expect(probe.ok(), `auto-map probe failed: ${probe.status()}`).toBeTruthy();
    const suggestions: any[] = await probe.json();
    const alreadyMapped = new Set(
      (snapshot.propertyMappings ?? []).map((pm: any) => String(pm.schemaPropertyName).toLowerCase()),
    );
    const expected = suggestions.find(
      (s) => RELATED.has(s.suggestedSourceType) && !alreadyMapped.has(String(s.schemaPropertyName).toLowerCase()),
    );
    test.skip(!expected, 'this host suggests no cross-node row for an unmapped schema property (TypeSafe not active)');

    try {
      await goToDocTypeSchemaTab(umbracoUi, DOC_TYPE_NAME);
      const view = page.locator('schemeweaver-schema-mapping-view');
      await view.getByTestId('schemeweaver:auto-map').click();

      // The hydrated row renders the related-type picker and the property combobox.
      const table = view.locator('schemeweaver-property-mapping-table');
      const row = table
        .locator('uui-table-row')
        .filter({ has: page.locator('schemeweaver-property-combobox') })
        .filter({ hasText: new RegExp(expected.schemaPropertyName, 'i') })
        .first();
      await expect(row, 'a hydrated cross-node row should render after auto-map').toBeVisible({ timeout: 30_000 });

      const typeInput = row.locator('umb-input-document-type');
      await expect(typeInput).toBeVisible();
      await expect
        .poll(async () => typeInput.evaluate((el: any) => (el.selection ?? []).length), {
          timeout: 15_000,
          message: 'the related document type should be pre-selected from suggestedSourceContentTypeAlias',
        })
        .toBeGreaterThan(0);

      const combobox = row.locator('schemeweaver-property-combobox');
      await expect(combobox).toHaveJSProperty('value', expected.suggestedContentTypePropertyAlias);
      await expect
        .poll(async () => combobox.evaluate((el: any) => (el.properties ?? []).length), {
          timeout: 15_000,
          message: 'the property list of the related type should be populated',
        })
        .toBeGreaterThan(0);

      // Persist through the document-type workspace Save and read back through the API.
      await umbracoUi.documentType.clickSaveButton();
      await expect
        .poll(
          async () => {
            const mapping = await getMapping(umbracoUi);
            const hit = (mapping?.propertyMappings ?? []).find(
              (pm: any) =>
                RELATED.has(pm.sourceType) &&
                String(pm.schemaPropertyName).toLowerCase() === String(expected.schemaPropertyName).toLowerCase(),
            );
            return hit ? `${hit.sourceType}:${hit.sourceContentTypeAlias}:${hit.contentTypePropertyAlias}` : 'not persisted yet';
          },
          { timeout: 20_000, message: 'the cross-node row should persist with its source type, type alias and property alias' },
        )
        .toBe(`${expected.suggestedSourceType}:${expected.suggestedSourceContentTypeAlias}:${expected.suggestedContentTypePropertyAlias}`);
    } finally {
      const restore = await page.request.post(`${BASE}/mappings`, { data: snapshot });
      expect(restore.ok(), `restore POST failed: ${restore.status()}`).toBeTruthy();
    }
  });
});
