import { expect } from '@playwright/test';
import { ConstantHelper, test } from '@umbraco/playwright-testhelpers';
import { join } from 'path';

const SCREENSHOTS_DIR = join(__dirname, '..', '..', '..', '..', '..', '..', 'screenshots');
const API_BASE = '/umbraco/management/api/v1/schemeweaver';

async function fillUuiInput(locator: any, text: string) {
  await locator.locator('input').fill(text);
}

async function goToDocTypeSchemaTab(umbracoUi: any, docTypeName: string) {
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
  const view = page.locator('schemeweaver-schema-mapping-view');
  const mapBtn = view.locator('uui-button', { hasText: /Map to Schema\.org/i }).first();
  await expect(mapBtn).toBeVisible({ timeout: 15_000 });
  await mapBtn.click();

  const pickerModal = page.locator('schemeweaver-schema-picker-modal');
  await expect(pickerModal).toBeVisible({ timeout: 10_000 });
  await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
  await expect(pickerModal.locator('.schema-item').first()).toBeVisible({ timeout: 10_000 });
  return pickerModal;
}

async function pickSchemaType(page: any, searchTerm: string, itemText: string) {
  const pickerModal = page.locator('schemeweaver-schema-picker-modal');
  await fillUuiInput(pickerModal.locator('uui-input').first(), searchTerm);
  await page.waitForTimeout(1_000);
  const item = pickerModal.locator('.schema-item', { hasText: itemText }).first();
  await expect(item).toBeVisible({ timeout: 10_000 });
  await item.click();
  await pickerModal.locator('uui-button[look="primary"]').last().click();
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
  const saveBtn = mappingModal.locator('uui-button[look="primary"]').last();
  await expect(saveBtn).toBeVisible({ timeout: 10_000 });
  await saveBtn.click();
  await expect(mappingModal).not.toBeVisible({ timeout: 15_000 });
  await expect(page.locator('schemeweaver-schema-mapping-view uui-tag')).toBeVisible({ timeout: 15_000 });
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
  const configureBtn = page.locator('uui-button', { hasText: /Configure Block Mapping/i }).first();
  await expect(configureBtn).toBeVisible({ timeout: 10_000 });
  await configureBtn.click();
  const nestedModal = page.locator('schemeweaver-nested-mapping-modal');
  await expect(nestedModal).toBeVisible({ timeout: 10_000 });
  await page.waitForTimeout(1_500);
  return nestedModal;
}

test.describe.serial('Documentation Screenshots', () => {
  test('02 — schema picker modal', async ({ umbracoUi }) => {
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
    await umbracoUi.goToBackOffice();
    await umbracoUi.content.goToSection(ConstantHelper.sections.content);

    const treeItem = umbracoUi.page.locator('umb-tree-item').first();
    await expect(treeItem).toBeVisible({ timeout: 15_000 });
    await treeItem.locator('a').first().click();
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

  test('11 — FAQ wizard', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await openNestedMapping(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '11-faqpage-wizard.png'),
      fullPage: true,
    });

    await umbracoUi.page.keyboard.press('Escape');
  });

  test('12 — Product auto-map', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Product Page');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '12-product-auto-map.png'),
      fullPage: true,
    });
  });

  test('13 — Recipe auto-map', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Recipe Page');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '13-recipe-auto-map.png'),
      fullPage: true,
    });
  });

  test('14 — Event auto-map', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Event Page');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '14-event-auto-map.png'),
      fullPage: true,
    });
  });

  test('15 — FAQ wizard step 2 (block mapping table)', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    await openNestedMapping(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '15-wizard-step2-mappings.png'),
      fullPage: true,
    });

    await umbracoUi.page.keyboard.press('Escape');
  });

  test('16 — FAQ wizard step 3 (preview summary)', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
    const nestedModal = await openNestedMapping(umbracoUi.page);

    const previewBtn = nestedModal.locator('uui-button', { hasText: 'Preview' });
    await expect(previewBtn).toBeVisible({ timeout: 10_000 });
    await previewBtn.click();
    await umbracoUi.page.waitForTimeout(2_000);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '16-wizard-step3-preview.png'),
      fullPage: true,
    });

    await umbracoUi.page.keyboard.press('Escape');
  });
});
