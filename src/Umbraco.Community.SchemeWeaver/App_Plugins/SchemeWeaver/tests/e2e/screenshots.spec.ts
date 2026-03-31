import { expect } from '@playwright/test';
import { ConstantHelper, test } from '@umbraco/playwright-testhelpers';
import { join } from 'path';

/** Root screenshots directory (repo root). */
const SCREENSHOTS_DIR = join(__dirname, '..', '..', '..', '..', '..', '..', 'screenshots');

/**
 * Helper: navigate to SchemeWeaver dashboard in Settings section.
 * Uses testhelpers for core Umbraco navigation, then waits for the custom dashboard.
 */
async function goToSchemeWeaverDashboard(umbracoUi: any) {
  await umbracoUi.goToBackOffice();
  await umbracoUi.content.goToSection(ConstantHelper.sections.settings);

  // Wait for the SchemeWeaver dashboard tab to appear and click it
  const dashboardTab = umbracoUi.page.getByRole('tab', { name: 'Schema.org Mappings' });
  await dashboardTab.waitFor({ timeout: 15_000 });
  await dashboardTab.click();

  // Wait for the dashboard custom element to load
  await umbracoUi.page
    .locator('schemeweaver-schema-mappings-dashboard')
    .waitFor({ timeout: 15_000 });
}

/**
 * Helper: wait for the dashboard table to finish loading.
 */
async function waitForDashboardTable(page: any) {
  // Wait for loader to disappear
  await page.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
  // Table should be visible
  await expect(page.locator('uui-table')).toBeVisible({ timeout: 10_000 });
}

/**
 * Helper: fill a uui-input web component by targeting its inner native input.
 */
async function fillUuiInput(locator: any, text: string) {
  const input = locator.locator('input');
  await input.fill(text);
}

/**
 * Helper: open the schema picker for a content type row.
 * Clicks either "Map to Schema.org" or "Edit mapping" depending on the row state.
 * Returns whether the picker was opened via "Map" (true) or "Edit" (false).
 */
async function openMappingForRow(page: any, rowLocator: any): Promise<'map' | 'edit'> {
  const mapBtn = rowLocator.locator('uui-button[label="Map to Schema.org"]');
  const editBtn = rowLocator.locator('uui-button[label="Edit mapping"]');

  if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
    await mapBtn.click();
    return 'map';
  }

  // Type is already mapped — use Edit to open the property mapping modal directly
  await expect(editBtn).toBeVisible({ timeout: 5_000 });
  await editBtn.click();
  return 'edit';
}

/**
 * Helper: from the schema picker, search for and select a schema type, then submit.
 * Assumes the schema picker modal is already open.
 */
async function pickSchemaType(page: any, searchTerm: string, itemText: string) {
  const pickerModal = page.locator('schemeweaver-schema-picker-modal');
  await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

  // Search
  const searchInput = pickerModal.locator('uui-input');
  await fillUuiInput(searchInput, searchTerm);
  await page.waitForTimeout(1_000); // debounce

  // Select the matching item
  const item = pickerModal.locator('.schema-item', { hasText: itemText });
  await expect(item).toBeVisible({ timeout: 10_000 });
  await item.click();

  // Submit
  await pickerModal.locator('uui-button[look="primary"]').last().click();
}

/**
 * Helper: wait for the property mapping modal and its table to fully render.
 */
async function waitForMappingModal(page: any) {
  const mappingModal = page.locator('schemeweaver-property-mapping-modal');
  await expect(mappingModal).toBeVisible({ timeout: 10_000 });

  const table = mappingModal.locator('schemeweaver-property-mapping-table');
  await expect(table).toBeVisible({ timeout: 10_000 });

  // Wait for table rows to populate
  await expect(table.locator('uui-table-row').first()).toBeVisible({ timeout: 10_000 });

  return mappingModal;
}

/**
 * Helper: cancel the property mapping modal.
 */
async function cancelMappingModal(page: any) {
  const mappingModal = page.locator('schemeweaver-property-mapping-modal');
  await mappingModal.locator('uui-button[label="Cancel"]').click();
  await expect(mappingModal).not.toBeVisible({ timeout: 5_000 });
}

/**
 * Helper: open a content type row, pick a schema type, and wait for the mapping modal.
 * If the type is already mapped (Edit button), opens the mapping modal directly and
 * skips the picker step. Returns the mapping modal locator.
 */
async function openMappingFlow(
  page: any,
  rowText: string,
  searchTerm: string,
  itemText: string,
) {
  const row = page.locator('uui-table-row', { hasText: rowText }).first();
  const action = await openMappingForRow(page, row);

  if (action === 'map') {
    await pickSchemaType(page, searchTerm, itemText);
  }

  return waitForMappingModal(page);
}

// ---------------------------------------------------------------------------
// Documentation Screenshots
// ---------------------------------------------------------------------------

test.describe.serial('Documentation Screenshots', () => {

  // 01: Dashboard overview with mix of mapped/unmapped types
  test('01 — dashboard overview', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const rows = umbracoUi.page.locator('uui-table-row');
    await expect(rows.first()).toBeVisible({ timeout: 10_000 });

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '01-dashboard-overview.png'),
      fullPage: true,
    });
  });

  // 02: Schema picker modal
  test('02 — schema picker modal', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Click Map on first unmapped type
    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await expect(mapBtn).toBeVisible({ timeout: 10_000 });
    await mapBtn.click();

    // Wait for picker to load and show items
    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await expect(pickerModal.locator('.schema-item').first()).toBeVisible({ timeout: 10_000 });

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '02-schema-picker.png'),
      fullPage: true,
    });

    // Close the picker without proceeding
    await pickerModal.locator('uui-button', { hasText: 'Cancel' }).click();
    await expect(pickerModal).not.toBeVisible({ timeout: 5_000 });
  });

  // 03: Basic property mapping
  test('03 — basic property mapping', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Click Map on first unmapped type
    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await expect(mapBtn).toBeVisible({ timeout: 10_000 });
    await mapBtn.click();

    // Pick first schema type and submit
    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await pickerModal.locator('.schema-item').first().click();
    await pickerModal.locator('uui-button[look="primary"]').last().click();

    // Wait for mapping modal and table
    await waitForMappingModal(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '03-basic-mapping.png'),
      fullPage: true,
    });

    // Cancel — do not save
    await cancelMappingModal(umbracoUi.page);
  });

  // 04: Source types in table
  test('04 — source types', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await expect(mapBtn).toBeVisible({ timeout: 10_000 });
    await mapBtn.click();

    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await pickerModal.locator('.schema-item').first().click();
    await pickerModal.locator('uui-button[look="primary"]').last().click();

    await waitForMappingModal(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '04-source-types.png'),
      fullPage: true,
    });

    await cancelMappingModal(umbracoUi.page);
  });

  // 05: Property table detail (headers: Schema Property, Source, Confidence)
  test('05 — property table detail', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Use Edit on a pre-mapped type to guarantee populated table rows
    const editBtn = umbracoUi.page.locator('uui-button[label="Edit mapping"]').first();
    await expect(editBtn).toBeVisible({ timeout: 10_000 });
    await editBtn.click();

    await waitForMappingModal(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '05-property-table.png'),
      fullPage: true,
    });

    await cancelMappingModal(umbracoUi.page);
  });

  // 06: Mapping persistence — edit a pre-mapped type
  test('06 — mapping persistence', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Click Edit on the first pre-mapped type
    const editBtn = umbracoUi.page.locator('uui-button[label="Edit mapping"]').first();
    await expect(editBtn).toBeVisible({ timeout: 10_000 });
    await editBtn.click();

    // Wait for mapping modal with table rows
    const mappingModal = await waitForMappingModal(umbracoUi.page);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '06-mapping-persistence.png'),
      fullPage: true,
    });

    await cancelMappingModal(umbracoUi.page);
  });

  // 07: Dashboard with mapped types
  test('07 — dashboard with mapped types', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Verify at least one mapped type is present (status uses uui-badge)
    const mappedBadge = umbracoUi.page.locator('uui-badge', { hasText: 'Mapped' }).first();
    await expect(mappedBadge).toBeVisible({ timeout: 10_000 });

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '07-dashboard-all-mapped.png'),
      fullPage: true,
    });
  });

  // 08: JSON-LD page output on the published home page
  test('08 — JSON-LD page output', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44308';

    const response = await umbracoUi.page.goto(baseUrl + '/', { waitUntil: 'domcontentloaded' });
    expect(response?.ok()).toBeTruthy();

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '08-jsonld-page-output.png'),
      fullPage: true,
    });
  });

  // 10: FAQPage auto-map
  test('10 — FAQPage auto-map', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    await openMappingFlow(umbracoUi.page, 'FAQ Page', 'FAQPage', 'FAQPage');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '10-faqpage-auto-map.png'),
      fullPage: true,
    });

    await cancelMappingModal(umbracoUi.page);
  });

  // 11: FAQ wizard — Configure Block Mapping
  test('11 — FAQ wizard', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    await openMappingFlow(umbracoUi.page, 'FAQ Page', 'FAQPage', 'FAQPage');

    // Click "Configure Block Mapping" button
    const configureBtn = umbracoUi.page.locator('uui-button', { hasText: 'Configure Block Mapping' });
    await expect(configureBtn).toBeVisible({ timeout: 10_000 });
    await configureBtn.click();

    // Wait for the nested modal to appear
    await umbracoUi.page.waitForTimeout(1_000);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '11-faqpage-wizard.png'),
      fullPage: true,
    });

    // Cleanup: dismiss any open modals via Escape key (more reliable than clicking Cancel)
    await umbracoUi.page.keyboard.press('Escape');
    await umbracoUi.page.waitForTimeout(500);
    await umbracoUi.page.keyboard.press('Escape');
    await umbracoUi.page.waitForTimeout(500);
  });

  // 12: Product auto-map
  test('12 — Product auto-map', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    await openMappingFlow(umbracoUi.page, 'Product Page', 'Product', 'Product');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '12-product-auto-map.png'),
      fullPage: true,
    });

    await cancelMappingModal(umbracoUi.page);
  });

  // 13: Recipe auto-map
  test('13 — Recipe auto-map', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    await openMappingFlow(umbracoUi.page, 'Recipe Page', 'Recipe', 'Recipe');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '13-recipe-auto-map.png'),
      fullPage: true,
    });

    await cancelMappingModal(umbracoUi.page);
  });

  // 14: Event auto-map
  test('14 — Event auto-map', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    await openMappingFlow(umbracoUi.page, 'Event Page', 'Event', 'Event');

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '14-event-auto-map.png'),
      fullPage: true,
    });

    await cancelMappingModal(umbracoUi.page);
  });

  // 15-16: Wizard steps 2 and 3 (FAQ block mapping flow)
  test('15 — FAQ wizard step 2 (block mapping table)', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    await openMappingFlow(umbracoUi.page, 'FAQ Page', 'FAQPage', 'FAQPage');

    // Click "Configure Block Mapping" button
    const configureBtn = umbracoUi.page.locator('uui-button', { hasText: 'Configure Block Mapping' });
    await expect(configureBtn).toBeVisible({ timeout: 10_000 });
    await configureBtn.click();

    // Wait for the nested modal and its mapping table to render
    await umbracoUi.page.waitForTimeout(1_500);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '15-wizard-step2-mappings.png'),
      fullPage: true,
    });

    // Cleanup via Escape
    await umbracoUi.page.keyboard.press('Escape');
    await umbracoUi.page.waitForTimeout(500);
    await umbracoUi.page.keyboard.press('Escape');
    await umbracoUi.page.waitForTimeout(500);
  });

  test('16 — FAQ wizard step 3 (preview summary)', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    await openMappingFlow(umbracoUi.page, 'FAQ Page', 'FAQPage', 'FAQPage');

    // Click "Configure Block Mapping" button
    const configureBtn = umbracoUi.page.locator('uui-button', { hasText: 'Configure Block Mapping' });
    await expect(configureBtn).toBeVisible({ timeout: 10_000 });
    await configureBtn.click();

    // Wait for nested modal to render
    await umbracoUi.page.waitForTimeout(1_500);

    // Click Preview button to advance to step 3
    const previewBtn = umbracoUi.page.locator('uui-button', { hasText: 'Preview' });
    await expect(previewBtn).toBeVisible({ timeout: 10_000 });
    await previewBtn.click();

    // Wait for the preview summary to render
    await umbracoUi.page.waitForTimeout(2_000);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '16-wizard-step3-preview.png'),
      fullPage: true,
    });

    // Cleanup via Escape
    await umbracoUi.page.keyboard.press('Escape');
    await umbracoUi.page.waitForTimeout(500);
    await umbracoUi.page.keyboard.press('Escape');
    await umbracoUi.page.waitForTimeout(500);
  });

  // 09: JSON-LD preview in backoffice (last — navigates away from Settings)
  test('09 — JSON-LD preview in backoffice', async ({ umbracoUi }) => {
    await umbracoUi.goToBackOffice();
    await umbracoUi.content.goToSection(ConstantHelper.sections.content);

    // Wait for content tree and click the first node
    const treeItem = umbracoUi.page.locator('umb-tree-item').first();
    await expect(treeItem).toBeVisible({ timeout: 15_000 });
    await treeItem.locator('a').first().click();

    // Wait for workspace to load
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Click the JSON-LD tab
    const jsonLdTab = umbracoUi.page.getByRole('tab', { name: /JSON-LD/i });
    await expect(jsonLdTab).toBeVisible({ timeout: 15_000 });
    await jsonLdTab.click();

    // Click Generate Preview
    const generateBtn = umbracoUi.page.locator('uui-button', { hasText: /Generate Preview/i });
    await expect(generateBtn).toBeVisible({ timeout: 10_000 });
    await generateBtn.click();

    // Wait for preview to render
    await umbracoUi.page.waitForTimeout(3_000);

    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '09-jsonld-preview.png'),
      fullPage: true,
    });
  });
});
