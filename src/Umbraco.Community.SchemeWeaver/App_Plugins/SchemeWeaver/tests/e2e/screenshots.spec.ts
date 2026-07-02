import { expect } from '@playwright/test';
import { ConstantHelper, test } from '@umbraco/playwright-testhelpers';
import { join } from 'path';

const SCREENSHOTS_DIR = join(__dirname, '..', '..', '..', '..', '..', '..', 'screenshots');
const API_BASE = '/umbraco/management/api/v1/schemeweaver';

async function fillUuiInput(locator: any, text: string) {
  await locator.locator('input').fill(text);
}

async function resolveDocTypeKey(page: any, contentTypeAlias: string): Promise<string> {
  const response = await page.request.get(`${API_BASE}/content-types`);
  const data = await response.json();
  const ct = data.find((t: any) => t.alias === contentTypeAlias);
  if (!ct) throw new Error(`Content type '${contentTypeAlias}' not found`);
  return ct.key;
}

async function goToDocTypeSchemaTab(umbracoUi: any, docTypeName: string, contentTypeAlias?: string) {
  if (contentTypeAlias) {
    // Navigate directly by URL for reliability with deep tree structures
    const key = await resolveDocTypeKey(umbracoUi.page, contentTypeAlias);
    await umbracoUi.page.goto(`/umbraco/section/settings/workspace/document-type/edit/${key}`);
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
  } else {
    // Fall back to tree navigation for simpler doc types
    await umbracoUi.goToBackOffice();
    await umbracoUi.content.goToSection(ConstantHelper.sections.settings);

    const docTypesLink = umbracoUi.page.locator('a', { hasText: 'Document Types' }).first();
    await docTypesLink.waitFor({ timeout: 15_000 });
    await docTypesLink.click();
    await umbracoUi.page.waitForTimeout(1_000);

    const expandBtn = umbracoUi.page.locator('button[aria-label*="Expand"]').first();
    if (await expandBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await expandBtn.click();
      await umbracoUi.page.waitForTimeout(1_000);
    }

    const treeItem = umbracoUi.page.locator('umb-tree-item umb-tree-item', { hasText: docTypeName }).first();
    await treeItem.waitFor({ timeout: 15_000 });
    await treeItem.locator('a').first().click();

    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
  }

  const schemaTab = umbracoUi.page.getByRole('tab', { name: /Schema\.org/i });
  await schemaTab.waitFor({ timeout: 15_000 });
  await schemaTab.click();

  await umbracoUi.page.locator('schemeweaver-schema-mapping-view').waitFor({ timeout: 15_000 });
}

async function ensureMappingDeleted(page: any, contentTypeAlias: string) {
  const response = await page.request.delete(`${API_BASE}/mappings/${contentTypeAlias}`);
  if (![200, 204, 404].includes(response.status())) {
    throw new Error(`Failed to delete mapping for ${contentTypeAlias}. Status: ${response.status()}`);
  }
}

async function openSchemaPickerFromWorkspace(page: any) {
  // Empty-state affordance on the workspace view (data-mark contract).
  const mapBtn = page.getByTestId('schemeweaver:map-to-schema');
  await expect(mapBtn).toBeVisible({ timeout: 15_000 });
  await mapBtn.click();

  const pickerModal = page.locator('schemeweaver-schema-picker-modal');
  await expect(pickerModal).toBeVisible({ timeout: 10_000 });
  await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
  // The redesigned picker opens on the curated "Common types" shortlist
  // (20 umb-ref-item rows) — not the full type-universe dump.
  await expect(pickerModal.locator('umb-ref-item').first()).toBeVisible({ timeout: 10_000 });
  return pickerModal;
}

async function pickSchemaType(page: any, searchTerm: string, itemText: string) {
  const pickerModal = page.locator('schemeweaver-schema-picker-modal');
  // Search filters in-memory (no debounce); the option row's own auto-waiting
  // is all the synchronisation needed. Data-mark contract:
  // schemeweaver:schema-search / schemeweaver:schema-option:<TypeName> /
  // schemeweaver:schema-picker-submit.
  await fillUuiInput(pickerModal.getByTestId('schemeweaver:schema-search'), searchTerm);
  const item = pickerModal.getByTestId(`schemeweaver:schema-option:${itemText}`);
  await expect(item).toBeVisible({ timeout: 10_000 });
  await item.click();
  await pickerModal.getByTestId('schemeweaver:schema-picker-submit').click();
}

async function waitForMappingModal(page: any) {
  const mappingModal = page.locator('schemeweaver-property-mapping-modal');
  await expect(mappingModal).toBeVisible({ timeout: 10_000 });
  await expect(mappingModal.locator('schemeweaver-property-mapping-table')).toBeVisible({ timeout: 10_000 });
  await expect(mappingModal.locator('uui-table-row').first()).toBeVisible({ timeout: 10_000 });
  return mappingModal;
}

async function saveMappingModal(page: any) {
  const mappingModal = page.locator('schemeweaver-property-mapping-modal');
  const saveBtn = mappingModal.getByTestId('schemeweaver:mapping-save');
  await expect(saveBtn).toBeVisible({ timeout: 10_000 });
  await saveBtn.click();
  await expect(mappingModal).not.toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('schemeweaver:schema-type-badge')).toBeVisible({ timeout: 15_000 });
}

async function openSourcePicker(page: any, rowIndex = 0) {
  const sourceChip = page.locator('schemeweaver-property-mapping-table .source-chip').nth(rowIndex);
  await expect(sourceChip).toBeVisible({ timeout: 10_000 });
  await sourceChip.click();
  const picker = page.locator('schemeweaver-source-origin-picker-modal');
  await expect(picker).toBeVisible({ timeout: 10_000 });
  return picker;
}

async function openNestedMapping(page: any) {
  // The old 3-step wizard's "Configure Block Mapping" entry point is gone.
  // Blocks are mapped per parent row via the scoped "Map blocks" button
  // (data-mark schemeweaver:map-blocks:<SchemaProperty>). Scope through the
  // workspace view — the same mark also renders inside the property-mapping
  // modal's table when that modal is open.
  const mapBlocksBtn = page
    .locator('schemeweaver-schema-mapping-view')
    .getByTestId(/^schemeweaver:map-blocks:/)
    .first();
  await expect(mapBlocksBtn).toBeVisible({ timeout: 10_000 });
  await mapBlocksBtn.click();
  const nestedModal = page.locator('schemeweaver-nested-mapping-modal');
  await expect(nestedModal).toBeVisible({ timeout: 10_000 });
  await page.waitForTimeout(1_500);
  return nestedModal;
}

test.describe.serial('Documentation Screenshots', () => {
  test('02 — schema picker modal', async ({ umbracoUi }) => {
    // Captures the picker's INITIAL state — with the redesign that is the
    // 20 curated "Common types", which is the intended documentation shot.
    await ensureMappingDeleted(umbracoUi.page, 'faqPage');
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await openSchemaPickerFromWorkspace(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '02-schema-picker.png'),
      fullPage: true,
    });

    await umbracoUi.page.keyboard.press('Escape');
  });

  test('03 — basic property mapping', async ({ umbracoUi }) => {
    await ensureMappingDeleted(umbracoUi.page, 'faqPage');
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await openSchemaPickerFromWorkspace(umbracoUi.page);
    await pickSchemaType(umbracoUi.page, 'FAQPage', 'FAQPage');
    await waitForMappingModal(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '03-basic-mapping.png'),
      fullPage: true,
    });

    await saveMappingModal(umbracoUi.page);
  });

  test('04 — source types', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await openSourcePicker(umbracoUi.page, 0);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '04-source-types.png'),
      fullPage: true,
    });

    await umbracoUi.page.keyboard.press('Escape');
  });

  test('05 — property table detail', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await expect(umbracoUi.page.locator('schemeweaver-property-mapping-table uui-table-row').first()).toBeVisible({ timeout: 15_000 });

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '05-property-table.png'),
      fullPage: true,
    });
  });

  test('06 — mapping persistence', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await expect(umbracoUi.page.locator('schemeweaver-schema-mapping-view uui-tag').first()).toBeVisible({ timeout: 15_000 });

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '06-mapping-persistence.png'),
      fullPage: true,
    });
  });

  test('08 — JSON-LD page output', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44308';
    const response = await umbracoUi.page.goto(`${baseUrl}/`, { waitUntil: 'domcontentloaded' });
    expect(response?.ok()).toBeTruthy();

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '08-jsonld-page-output.png'),
      fullPage: true,
    });
  });

  test('09 — JSON-LD preview in backoffice', async ({ umbracoUi }) => {
    // Find a content item that has a schema mapping by checking the mappings API
    const mappingsResponse = await umbracoUi.page.request.get(`${API_BASE}/mappings`);
    const mappings = await mappingsResponse.json();
    const firstMapping = mappings.find((m: any) => m.contentTypeAlias === 'homePage') || mappings[0];
    if (!firstMapping) {
      test.skip();
      return;
    }

    // Navigate directly to the content section and find a content item of this type
    await umbracoUi.page.goto('/umbraco/section/content');
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Wait for tree and click the first content item
    const treeItem = umbracoUi.page.locator('umb-tree-item a[href*="workspace/document/edit"]').first();
    await expect(treeItem).toBeVisible({ timeout: 15_000 });
    await treeItem.click();
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    const jsonLdTab = umbracoUi.page.getByRole('tab', { name: /JSON-LD/i });
    await expect(jsonLdTab).toBeVisible({ timeout: 15_000 });
    await jsonLdTab.click();
    await expect(umbracoUi.page.locator('schemeweaver-jsonld-content-view')).toBeVisible({ timeout: 10_000 });
    await umbracoUi.page.waitForTimeout(2_000);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '09-jsonld-preview.png'),
      fullPage: true,
    });
  });

  test('10 — FAQPage auto-map', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '10-faqpage-auto-map.png'),
      fullPage: true,
    });
  });

  test('11 — FAQ scoped block-mapping modal', async ({ umbracoUi }) => {
    // Filename kept from the wizard era so existing docs references still
    // resolve; the content is now the scoped "Map blocks" modal.
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await openNestedMapping(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '11-faqpage-wizard.png'),
      fullPage: true,
    });

    await umbracoUi.page.keyboard.press('Escape');
  });

  test('12 — Product auto-map', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Product Page', 'productPage');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '12-product-auto-map.png'),
      fullPage: true,
    });
  });

  test('13 — Recipe auto-map', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Recipe Page', 'recipePage');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '13-recipe-auto-map.png'),
      fullPage: true,
    });
  });

  test('14 — Event auto-map', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Event Page', 'eventPage');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '14-event-auto-map.png'),
      fullPage: true,
    });
  });

  test('15 — block mapping rows (scoped modal)', async ({ umbracoUi }) => {
    // Filename kept from the wizard era so existing docs references still
    // resolve; the wizard's "step 2" table is now the modal's block-row list.
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await openNestedMapping(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '15-wizard-step2-mappings.png'),
      fullPage: true,
    });

    await umbracoUi.page.keyboard.press('Escape');
  });

  test('16 — block row expanded (property mappings detail)', async ({ umbracoUi }) => {
    // The wizard's step-3 "Preview" summary no longer exists. The closest
    // redesigned analogue is an EXPANDED block row showing its per-property
    // mappings. Filename kept so existing docs references still resolve.
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    const nestedModal = await openNestedMapping(umbracoUi.page);

    const expandBtn = nestedModal.locator('.row-expand').first();
    await expect(expandBtn).toBeVisible({ timeout: 10_000 });
    await expandBtn.click();
    await umbracoUi.page.waitForTimeout(1_000);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '16-wizard-step3-preview.png'),
      fullPage: true,
    });

    await umbracoUi.page.keyboard.press('Escape');
  });
});
