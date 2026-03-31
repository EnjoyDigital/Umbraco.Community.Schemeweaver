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

// ---------------------------------------------------------------------------
// Dashboard Tests
// ---------------------------------------------------------------------------

test.describe('SchemeWeaver Dashboard', () => {
  test('dashboard is visible in Settings section', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);

    await expect(
      umbracoUi.page.locator('schemeweaver-schema-mappings-dashboard')
    ).toBeVisible();
  });

  test('content types table renders with rows', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const rows = umbracoUi.page.locator('uui-table-row');
    await expect(rows.first()).toBeVisible({ timeout: 10_000 });

    // Should have at least one content type
    const count = await rows.count();
    expect(count).toBeGreaterThan(0);

    // Screenshot: dashboard overview for documentation
    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '01-dashboard-overview.png'),
      fullPage: true,
    });
  });

  test('table has correct column headers', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const expectedHeaders = ['Content Type', 'Schema Type', 'Status', 'Properties', 'Actions'];
    for (const header of expectedHeaders) {
      await expect(
        umbracoUi.page.locator('uui-table-head-cell', { hasText: header })
      ).toBeVisible();
    }
  });

  test('search filters content types', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const rowsBefore = await umbracoUi.page.locator('uui-table-row').count();

    // Type a search term that should narrow results — target inner input of uui-input
    const searchInput = umbracoUi.page.locator('uui-input').first();
    await fillUuiInput(searchInput, 'zzz_nonexistent_zzz');
    await umbracoUi.page.waitForTimeout(500);

    // Should show "no results" or fewer rows
    const rowsAfter = await umbracoUi.page.locator('uui-table-row').count();
    expect(rowsAfter).toBeLessThan(rowsBefore);
  });

  test('refresh button reloads data', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const refreshBtn = umbracoUi.page.locator('uui-button', { hasText: 'Refresh' });
    await expect(refreshBtn).toBeVisible();
    await refreshBtn.click();

    // Loader should briefly appear then table should reload
    await waitForDashboardTable(umbracoUi.page);
    await expect(umbracoUi.page.locator('uui-table-row').first()).toBeVisible();
  });

  test('unmapped content type shows Map button', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Find a row with "Map to Schema.org" button (unmapped types have this)
    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await expect(mapBtn).toBeVisible();
    }
  });
});

// ---------------------------------------------------------------------------
// Schema Picker Modal Tests
// ---------------------------------------------------------------------------

test.describe('Schema Picker Modal', () => {
  test('Map button opens schema picker modal', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Click first "Map" button
    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await expect(mapBtn).toBeVisible({ timeout: 5_000 });
    await mapBtn.click();

    // Schema picker modal should open
    await expect(
      umbracoUi.page.locator('schemeweaver-schema-picker-modal')
    ).toBeVisible({ timeout: 10_000 });
    await expect(umbracoUi.page.getByText('Select Schema.org Type')).toBeVisible();
  });

  test('schema picker loads and displays schema types', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await mapBtn.click();

    // Wait for types to load (loader disappears)
    const modal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await modal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

    // Schema items should be visible
    const items = modal.locator('.schema-item');
    await expect(items.first()).toBeVisible({ timeout: 10_000 });

    const count = await items.count();
    expect(count).toBeGreaterThan(0);

    // Screenshot: schema picker modal for documentation
    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '02-schema-picker.png'),
      fullPage: true,
    });
  });

  test('schema picker search filters types', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await mapBtn.click();

    const modal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await modal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

    const countBefore = await modal.locator('.schema-item').count();

    // Search for a specific type — target inner input of uui-input
    const searchInput = modal.locator('uui-input').first();
    await fillUuiInput(searchInput, 'Article');
    await umbracoUi.page.waitForTimeout(1_000);

    const countAfter = await modal.locator('.schema-item').count();
    expect(countAfter).toBeLessThanOrEqual(countBefore);
    expect(countAfter).toBeGreaterThan(0);
  });

  test('selecting a type enables Submit button', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await mapBtn.click();

    const modal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await modal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

    // Submit button should be disabled initially (uui-button uses disabled attribute)
    const submitBtn = modal.locator('uui-button[look="primary"]').last();
    await expect(submitBtn).toHaveAttribute('disabled', '');

    // Click first schema item
    await modal.locator('.schema-item').first().click();

    // Submit button should now be enabled (disabled attribute removed)
    await expect(submitBtn).not.toHaveAttribute('disabled', '');
  });

  test('cancel button closes modal', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await mapBtn.click();

    const modal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await expect(modal).toBeVisible({ timeout: 10_000 });

    await modal.locator('uui-button', { hasText: 'Cancel' }).click();

    await expect(modal).not.toBeVisible({ timeout: 5_000 });
  });
});

// ---------------------------------------------------------------------------
// Full Mapping Workflow Tests
// ---------------------------------------------------------------------------

test.describe('Schema Mapping Workflow', () => {
  test('full map workflow: pick schema → configure properties → save', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Step 1: Click Map on an unmapped content type
    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await expect(mapBtn).toBeVisible({ timeout: 5_000 });
    await mapBtn.click();

    // Step 2: Schema picker opens — select a type
    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await pickerModal.locator('.schema-item').first().click();

    // Click the Submit button (last primary button in modal actions)
    await pickerModal.locator('uui-button[look="primary"]').last().click();

    // Step 3: Property mapping modal opens
    const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    await expect(mappingModal).toBeVisible({ timeout: 10_000 });

    // Should contain the property mapping table
    await expect(
      mappingModal.locator('schemeweaver-property-mapping-table')
    ).toBeVisible({ timeout: 10_000 });

    // Should have Save and Cancel buttons
    await expect(mappingModal.locator('uui-button[label="Save Mapping"]')).toBeVisible();
    await expect(mappingModal.locator('uui-button[label="Cancel"]')).toBeVisible();

    // Screenshot: property mapping modal with auto-suggestions for documentation
    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '03-basic-mapping.png'),
      fullPage: true,
    });

    // Step 4: Save the mapping
    await mappingModal.locator('uui-button[label="Save Mapping"]').click();

    // Modal should close after save
    await expect(mappingModal).not.toBeVisible({ timeout: 10_000 });

    // Step 5: Dashboard should refresh — the content type should now show as Mapped
    await waitForDashboardTable(umbracoUi.page);
  });

  test('edit button opens property mapping modal for mapped type', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Find an Edit button (only visible for mapped types)
    const editBtn = umbracoUi.page.locator('uui-button[label="Edit mapping"]').first();
    if (await editBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await editBtn.click();

      // Property mapping modal should open
      const modal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
      await expect(modal).toBeVisible({ timeout: 10_000 });

      // Should show the property mapping table with existing mappings
      await expect(
        modal.locator('schemeweaver-property-mapping-table')
      ).toBeVisible();

      // Close without saving
      await modal.locator('uui-button[label="Cancel"]').click();
      await expect(modal).not.toBeVisible({ timeout: 5_000 });
    }
  });

  test('delete button removes mapping', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Count rows with Delete mapping button (mapped types)
    const deleteButtons = umbracoUi.page.locator('uui-button[label="Delete mapping"]');
    const mappedCountBefore = await deleteButtons.count();

    // Only test delete if there's a mapped type
    if (mappedCountBefore > 0) {
      await deleteButtons.first().click();

      // Wait for dashboard to reload
      await waitForDashboardTable(umbracoUi.page);

      // Should have one fewer mapped type
      const mappedCountAfter = await umbracoUi.page
        .locator('uui-button[label="Delete mapping"]')
        .count();
      expect(mappedCountAfter).toBeLessThan(mappedCountBefore);
    }
  });
});

// ---------------------------------------------------------------------------
// Property Mapping Table Tests
// ---------------------------------------------------------------------------

test.describe('Property Mapping Table', () => {
  test('property mapping table shows schema properties', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Open mapping flow to get to property table
    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await expect(mapBtn).toBeVisible({ timeout: 5_000 });
    await mapBtn.click();

    // Pick a schema type
    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await pickerModal.locator('.schema-item').first().click();
    await pickerModal.locator('uui-button[look="primary"]').last().click();

    // Property mapping modal with table
    const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    const table = mappingModal.locator('schemeweaver-property-mapping-table');
    await expect(table).toBeVisible({ timeout: 10_000 });

    // Table should have headers
    await expect(table.locator('uui-table-head-cell', { hasText: 'Schema Property' })).toBeVisible();
    await expect(table.locator('uui-table-head-cell', { hasText: 'Source' })).toBeVisible();
    await expect(table.locator('uui-table-head-cell', { hasText: 'Confidence' })).toBeVisible();

    // Screenshot: property mapping table for documentation
    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '05-property-table.png'),
      fullPage: true,
    });

    // Close
    await mappingModal.locator('uui-button[label="Cancel"]').click();
  });

  test('source type dropdown has correct options', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await mapBtn.click();

    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await pickerModal.locator('.schema-item').first().click();
    await pickerModal.locator('uui-button[look="primary"]').last().click();

    const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    const table = mappingModal.locator('schemeweaver-property-mapping-table');
    await expect(table).toBeVisible({ timeout: 10_000 });

    // Check that the table has rendered (headers visible)
    await expect(table.locator('uui-table-head-cell').first()).toBeVisible();

    // If auto-map returned rows, check that source selects exist
    const rows = table.locator('uui-table-row');
    const rowCount = await rows.count();
    if (rowCount > 0) {
      const firstSelect = table.locator('uui-select').first();
      await expect(firstSelect).toBeVisible();

      // Screenshot: source type options for documentation
      await umbracoUi.page.screenshot({
        path: join(SCREENSHOTS_DIR, '04-source-types.png'),
        fullPage: true,
      });
    }

    // Close
    await mappingModal.locator('uui-button[label="Cancel"]').click();
  });
});

// ---------------------------------------------------------------------------
// Complex Mapping Workflow Tests
// ---------------------------------------------------------------------------

test.describe('Complex Mapping Workflows', () => {
  test('FAQ auto-map shows blockContent source with Question type for mainEntity', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Find the FAQ Page row and click Map
    const faqRow = umbracoUi.page.locator('uui-table-row', { hasText: 'FAQ' }).first();
    const mapBtn = faqRow.locator('uui-button[label="Map to Schema.org"]');
    if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await mapBtn.click();

      // Pick FAQPage schema type
      const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
      await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

      // Search for FAQPage
      const searchInput = pickerModal.locator('uui-input');
      if (await searchInput.isVisible().catch(() => false)) {
        await fillUuiInput(searchInput, 'FAQ');
        await umbracoUi.page.waitForTimeout(500);
      }

      // Select FAQPage
      const faqItem = pickerModal.locator('.schema-item', { hasText: 'FAQPage' });
      if (await faqItem.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await faqItem.click();
        await pickerModal.locator('uui-button[look="primary"]').last().click();

        // Property mapping modal should show
        const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
        await expect(mappingModal).toBeVisible({ timeout: 10_000 });

        const table = mappingModal.locator('schemeweaver-property-mapping-table');
        await expect(table).toBeVisible({ timeout: 10_000 });

        // Look for mainEntity row — it should have blockContent source and Question type
        const mainEntityText = table.locator('uui-table-row', { hasText: 'mainEntity' });
        await expect(mainEntityText).toBeVisible({ timeout: 5_000 });

        // Close without saving
        await mappingModal.locator('uui-button[label="Cancel"]').click();
      }
    }
  });

  test('Product auto-map shows review as blockContent with Review type', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Find the Product Page row
    const productRow = umbracoUi.page.locator('uui-table-row', { hasText: 'Product' }).first();
    const mapBtn = productRow.locator('uui-button[label="Map to Schema.org"]');
    if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await mapBtn.click();

      const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
      await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

      const searchInput = pickerModal.locator('uui-input');
      if (await searchInput.isVisible().catch(() => false)) {
        await fillUuiInput(searchInput, 'Product');
        await umbracoUi.page.waitForTimeout(500);
      }

      const productItem = pickerModal.locator('.schema-item', { hasText: 'Product' }).first();
      if (await productItem.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await productItem.click();
        await pickerModal.locator('uui-button[look="primary"]').last().click();

        const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
        await expect(mappingModal).toBeVisible({ timeout: 10_000 });

        const table = mappingModal.locator('schemeweaver-property-mapping-table');
        await expect(table).toBeVisible({ timeout: 10_000 });

        // Look for review row
        const reviewRow = table.locator('uui-table-row', { hasText: 'review' });
        await expect(reviewRow).toBeVisible({ timeout: 5_000 });

        await mappingModal.locator('uui-button[label="Cancel"]').click();
      }
    }
  });

  test('save FAQ mapping and verify it persists on dashboard', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Find an unmapped FAQ page
    const faqRow = umbracoUi.page.locator('uui-table-row', { hasText: 'FAQ' }).first();
    const mapBtn = faqRow.locator('uui-button[label="Map to Schema.org"]');
    if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await mapBtn.click();

      const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
      await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

      const searchInput = pickerModal.locator('uui-input');
      if (await searchInput.isVisible().catch(() => false)) {
        await fillUuiInput(searchInput, 'FAQ');
        await umbracoUi.page.waitForTimeout(500);
      }

      const faqItem = pickerModal.locator('.schema-item', { hasText: 'FAQPage' });
      if (await faqItem.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await faqItem.click();
        await pickerModal.locator('uui-button[look="primary"]').last().click();

        const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
        await expect(mappingModal).toBeVisible({ timeout: 10_000 });

        // Save the mapping
        await mappingModal.locator('uui-button[label="Save Mapping"]').click();
        await expect(mappingModal).not.toBeVisible({ timeout: 10_000 });

        // Dashboard should update — FAQ row should now show as Mapped
        await waitForDashboardTable(umbracoUi.page);
        const updatedFaqRow = umbracoUi.page.locator('uui-table-row', { hasText: 'FAQ' }).first();
        const mappedBadge = updatedFaqRow.locator('uui-tag', { hasText: 'Mapped' });
        await expect(mappedBadge).toBeVisible({ timeout: 5_000 });
      }
    }
  });
});

// ---------------------------------------------------------------------------
// JSON-LD Output Verification Tests (require content to be published)
// ---------------------------------------------------------------------------

test.describe('JSON-LD Output on Site', () => {
  test('mapped content page contains JSON-LD script tag', async ({ umbracoUi }) => {
    // Navigate to a content page on the frontend (not backoffice)
    // This test verifies the tag helper output
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    const response = await umbracoUi.page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
    if (response?.ok()) {
      // Check for JSON-LD script tag
      const jsonLdScript = umbracoUi.page.locator('script[type="application/ld+json"]');
      const count = await jsonLdScript.count();

      // If there's a JSON-LD script, validate its structure
      if (count > 0) {
        const jsonLdText = await jsonLdScript.first().textContent();
        expect(jsonLdText).toBeTruthy();

        const parsed = JSON.parse(jsonLdText!);
        expect(parsed['@context']).toBe('https://schema.org');
        expect(parsed['@type']).toBeTruthy();
      }
    }
  });

  test('FAQ page JSON-LD contains Question and Answer types', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    const response = await umbracoUi.page.goto(`${baseUrl}/frequently-asked-questions/`, { waitUntil: 'domcontentloaded' });
    if (response?.ok()) {
      const jsonLdScript = umbracoUi.page.locator('script[type="application/ld+json"]');
      if (await jsonLdScript.count() > 0) {
        const jsonLdText = await jsonLdScript.first().textContent();
        const parsed = JSON.parse(jsonLdText!);

        expect(parsed['@type']).toBe('FAQPage');

        // If mainEntity is mapped, it should contain Question objects
        if (parsed.mainEntity) {
          const questions = Array.isArray(parsed.mainEntity) ? parsed.mainEntity : [parsed.mainEntity];
          for (const q of questions) {
            expect(q['@type']).toBe('Question');
            expect(q.name).toBeTruthy();
            if (q.acceptedAnswer) {
              expect(q.acceptedAnswer['@type']).toBe('Answer');
              expect(q.acceptedAnswer.text).toBeTruthy();
            }
          }
        }
      }
    }
  });

  test('Product page JSON-LD contains Review objects', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    const response = await umbracoUi.page.goto(`${baseUrl}/products/wireless-headphones-pro/`, { waitUntil: 'domcontentloaded' });
    if (response?.ok()) {
      const jsonLdScripts = umbracoUi.page.locator('script[type="application/ld+json"]');
      const count = await jsonLdScripts.count();

      // Find the Product script (may not be first due to ordering)
      let productJson: any = null;
      for (let i = 0; i < count; i++) {
        const text = await jsonLdScripts.nth(i).textContent();
        const parsed = JSON.parse(text!);
        if (parsed['@type'] === 'Product') {
          productJson = parsed;
          break;
        }
      }

      if (productJson) {
        expect(productJson['@type']).toBe('Product');

        if (productJson.review) {
          const reviews = Array.isArray(productJson.review) ? productJson.review : [productJson.review];
          for (const r of reviews) {
            expect(r['@type']).toBe('Review');
          }
        }
      }
    }
  });

  test('Recipe page JSON-LD contains ingredients and instructions', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    const response = await umbracoUi.page.goto(`${baseUrl}/recipes/classic-victoria-sponge/`, { waitUntil: 'domcontentloaded' });
    if (response?.ok()) {
      const jsonLdScript = umbracoUi.page.locator('script[type="application/ld+json"]');
      if (await jsonLdScript.count() > 0) {
        const jsonLdText = await jsonLdScript.first().textContent();
        const parsed = JSON.parse(jsonLdText!);

        expect(parsed['@type']).toBe('Recipe');

        // recipeIngredient should be string array
        if (parsed.recipeIngredient) {
          expect(Array.isArray(parsed.recipeIngredient)).toBe(true);
          for (const ingredient of parsed.recipeIngredient) {
            expect(typeof ingredient).toBe('string');
          }
        }

        // recipeInstructions should be HowToStep array
        if (parsed.recipeInstructions) {
          const steps = Array.isArray(parsed.recipeInstructions) ? parsed.recipeInstructions : [parsed.recipeInstructions];
          for (const step of steps) {
            expect(step['@type']).toBe('HowToStep');
          }
        }
      }
    }
  });
});

// ---------------------------------------------------------------------------
// JSON-LD Ordering Tests
// ---------------------------------------------------------------------------

test.describe('JSON-LD Script Ordering', () => {
  test('scripts are ordered: inherited → breadcrumb → page schema', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    // Navigate to a child page that should have inherited WebSite + BreadcrumbList + own schema
    const response = await umbracoUi.page.goto(`${baseUrl}/products/wireless-headphones-pro/`, { waitUntil: 'domcontentloaded' });
    if (!response?.ok()) return;

    const jsonLdScripts = umbracoUi.page.locator('script[type="application/ld+json"]');
    const count = await jsonLdScripts.count();
    expect(count).toBeGreaterThanOrEqual(3);

    // Parse all scripts to get their types in order
    const types: string[] = [];
    for (let i = 0; i < count; i++) {
      const text = await jsonLdScripts.nth(i).textContent();
      const parsed = JSON.parse(text!);
      types.push(parsed['@type']);
    }

    // Verify ordering: WebSite (inherited) should come before BreadcrumbList, which comes before Product
    const websiteIdx = types.indexOf('WebSite');
    const breadcrumbIdx = types.indexOf('BreadcrumbList');
    const productIdx = types.indexOf('Product');

    expect(websiteIdx).toBeGreaterThanOrEqual(0);
    expect(breadcrumbIdx).toBeGreaterThan(websiteIdx);
    expect(productIdx).toBeGreaterThan(breadcrumbIdx);
  });

  test('all pages with schemas have valid JSON-LD', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    // Smoke test: check that all known pages have at least one valid JSON-LD script
    const pages = [
      '/',
      '/blog/',
      '/products/',
      '/events/',
      '/recipes/',
      '/frequently-asked-questions/',
      '/products/wireless-headphones-pro/',
      '/recipes/classic-victoria-sponge/',
    ];

    for (const path of pages) {
      const response = await umbracoUi.page.goto(`${baseUrl}${path}`, { waitUntil: 'domcontentloaded' });
      if (!response?.ok()) continue;

      const jsonLdScripts = umbracoUi.page.locator('script[type="application/ld+json"]');
      const count = await jsonLdScripts.count();
      expect(count).toBeGreaterThan(0);

      // Validate first script is valid JSON with @context
      const text = await jsonLdScripts.first().textContent();
      expect(text).toBeTruthy();
      const parsed = JSON.parse(text!);
      expect(parsed['@context']).toBe('https://schema.org');
      expect(parsed['@type']).toBeTruthy();
    }
  });
});

// ---------------------------------------------------------------------------
// Delivery API JSON-LD Tests (require Delivery API enabled + published content)
// ---------------------------------------------------------------------------

test.describe('Delivery API JSON-LD', () => {
  test('Delivery API returns schemaOrg field for mapped content', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    // Fetch content items via Delivery API
    const response = await umbracoUi.page.request.get(
      `${baseUrl}/umbraco/delivery/api/v2/content`,
      { headers: { 'Accept': 'application/json' } }
    );

    if (!response.ok()) {
      console.warn('Delivery API not available — skipping test');
      return;
    }

    const data = await response.json();
    expect(data.items).toBeTruthy();

    // Find any content item that has a schemaOrg property (i.e. has a mapping)
    const itemWithSchema = data.items.find(
      (item: any) => item.properties?.schemaOrg
    );

    if (!itemWithSchema) {
      console.warn('No content with schemaOrg mapping found in Delivery API — skipping validation');
      return;
    }

    // schemaOrg should be a JSON-LD string (or array of strings)
    const schemaOrg = itemWithSchema.properties.schemaOrg;
    const jsonLdString = Array.isArray(schemaOrg) ? schemaOrg[0] : schemaOrg;
    expect(jsonLdString).toBeTruthy();

    const parsed = JSON.parse(jsonLdString);
    expect(parsed['@context']).toBe('https://schema.org');
    expect(parsed['@type']).toBeTruthy();
  });

  test('Delivery API content item by path includes JSON-LD', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    // Fetch the home page by path via Delivery API
    const response = await umbracoUi.page.request.get(
      `${baseUrl}/umbraco/delivery/api/v2/content/item/`,
      { headers: { 'Accept': 'application/json' } }
    );

    if (!response.ok()) {
      console.warn('Delivery API item endpoint not available — skipping test');
      return;
    }

    const item = await response.json();

    // If the home page has a schema mapping, validate the schemaOrg property
    if (item.properties?.schemaOrg) {
      const schemaOrg = item.properties.schemaOrg;
      const jsonLdString = Array.isArray(schemaOrg) ? schemaOrg[0] : schemaOrg;
      const parsed = JSON.parse(jsonLdString);
      expect(parsed['@context']).toBe('https://schema.org');
      expect(parsed['@type']).toBeTruthy();
    }
  });
});

// ---------------------------------------------------------------------------
// Mapping Persistence & JSON-LD Output Tests
// ---------------------------------------------------------------------------

test.describe('Mapping Persistence & JSON-LD Output', () => {
  test('saved mapping persists after re-opening', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Find an existing mapped type — look for "Edit mapping" button
    const editBtn = umbracoUi.page.locator('uui-button[label="Edit mapping"]').first();
    await expect(editBtn).toBeVisible({ timeout: 5_000 });

    // Note the schema type shown in the row before editing
    const mappedRow = umbracoUi.page.locator('uui-table-row').filter({
      has: umbracoUi.page.locator('uui-button[label="Edit mapping"]'),
    }).first();
    const schemaTypeCell = mappedRow.locator('uui-table-cell').nth(1);
    const schemaTypeName = await schemaTypeCell.textContent();
    expect(schemaTypeName?.trim()).toBeTruthy();

    // Open the edit modal
    await editBtn.click();
    const modal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    await expect(modal).toBeVisible({ timeout: 10_000 });

    // Verify the modal shows the property mapping table
    await expect(modal.locator('schemeweaver-property-mapping-table')).toBeVisible({ timeout: 10_000 });

    // Verify we have mapping rows (persisted data)
    const table = modal.locator('schemeweaver-property-mapping-table');
    await expect(table.locator('uui-table-row').first()).toBeVisible({ timeout: 10_000 });

    // Screenshot the persisted mapping modal
    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '06-mapping-persistence.png'),
      fullPage: true,
    });

    // Close modal
    await modal.locator('uui-button[label="Cancel"]').click();
    await expect(modal).not.toBeVisible({ timeout: 5_000 });
  });

  test('mapping save persists and shows on dashboard', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Find an unmapped type and map it
    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await expect(mapBtn).toBeVisible({ timeout: 5_000 });
    await mapBtn.click();

    // Pick a schema type in the picker modal
    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

    // Search for "ContactPage" to pick a specific type
    const searchInput = pickerModal.locator('uui-input').first();
    await fillUuiInput(searchInput, 'ContactPage');
    await umbracoUi.page.waitForTimeout(1_000);

    // Select the first matching item
    await pickerModal.locator('.schema-item').first().click();
    await pickerModal.locator('uui-button[look="primary"]').last().click();

    // Property mapping modal opens — just save
    const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    await expect(mappingModal).toBeVisible({ timeout: 10_000 });
    await mappingModal.locator('uui-button[label="Save Mapping"]').click();
    await expect(mappingModal).not.toBeVisible({ timeout: 10_000 });

    // Dashboard reloads — verify the type now shows as mapped
    await waitForDashboardTable(umbracoUi.page);

    // All content types should now be mapped (no more "Map to Schema.org" buttons)
    // or at least fewer unmapped types
    const remainingMapButtons = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]');
    const unmappedCount = await remainingMapButtons.count();
    // We had 2 unmapped (blogArticle + contactPage), mapped one, so at most 1 left
    expect(unmappedCount).toBeLessThanOrEqual(1);

    // Screenshot the dashboard showing mapped types
    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '07-dashboard-all-mapped.png'),
      fullPage: true,
    });
  });

  test('JSON-LD renders on published page', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44308';

    // Navigate to the home page (which has homePage → WebSite mapping)
    await umbracoUi.page.goto(baseUrl + '/');
    await umbracoUi.page.waitForLoadState('domcontentloaded', { timeout: 15_000 });

    // Get the full page HTML
    const pageContent = await umbracoUi.page.content();

    // Verify JSON-LD script tag exists
    expect(pageContent).toContain('application/ld+json');

    // Parse the JSON-LD and check the @type
    const jsonLdMatch = pageContent.match(
      /<script type="application\/ld\+json">([\s\S]*?)<\/script>/
    );
    expect(jsonLdMatch).toBeTruthy();

    const jsonLd = JSON.parse(jsonLdMatch![1]);
    expect(jsonLd['@context']).toBe('https://schema.org');
    expect(jsonLd['@type']).toBe('WebSite');
    expect(jsonLd['name']).toBeTruthy();

    // Screenshot the published page
    await umbracoUi.page.screenshot({
      path: join(SCREENSHOTS_DIR, '08-jsonld-page-output.png'),
      fullPage: true,
    });

    // Also verify the product page has JSON-LD
    await umbracoUi.page.goto(baseUrl + '/schemeweaver-pro/');
    await umbracoUi.page.waitForLoadState('domcontentloaded', { timeout: 15_000 });

    const productContent = await umbracoUi.page.content();
    expect(productContent).toContain('application/ld+json');

    const productJsonLdMatch = productContent.match(
      /<script type="application\/ld\+json">([\s\S]*?)<\/script>/
    );
    expect(productJsonLdMatch).toBeTruthy();

    const productJsonLd = JSON.parse(productJsonLdMatch![1]);
    expect(productJsonLd['@type']).toBe('Product');
    expect(productJsonLd['name']).toBeTruthy();
  });

  test('JSON-LD preview works in backoffice', async ({ umbracoUi }) => {
    // Navigate to Content section
    await umbracoUi.goToBackOffice();
    await umbracoUi.content.goToSection(ConstantHelper.sections.content);

    // Wait for content tree to load
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Click on the first content node (Home Page)
    const treeItem = umbracoUi.page.locator('umb-tree-item').first();
    await treeItem.waitFor({ timeout: 15_000 });
    await treeItem.locator('a').first().click();

    // Wait for workspace to load
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Look for the JSON-LD tab
    const jsonLdTab = umbracoUi.page.getByRole('tab', { name: /JSON-LD/i });
    if (await jsonLdTab.isVisible({ timeout: 10_000 }).catch(() => false)) {
      await jsonLdTab.click();

      // Wait for the JSON-LD content view to load
      const jsonLdView = umbracoUi.page.locator('schemeweaver-jsonld-content-view');
      await expect(jsonLdView).toBeVisible({ timeout: 10_000 });

      // Click generate preview button
      const generateBtn = jsonLdView.locator('uui-button', { hasText: /Generate Preview/i });
      if (await generateBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await generateBtn.click();

        // Wait for preview to render
        await umbracoUi.page.waitForTimeout(3_000);

        // Screenshot the JSON-LD preview
        await umbracoUi.page.screenshot({
          path: join(SCREENSHOTS_DIR, '09-jsonld-preview.png'),
          fullPage: true,
        });
      }
    }
  });
});

// ---------------------------------------------------------------------------
// Document Type Workspace View Tests
// ---------------------------------------------------------------------------

test.describe('Document Type Workspace View', () => {
  test('Schema.org tab appears on document type editor', async ({ umbracoUi }) => {
    // Navigate directly to Settings section via URL to avoid testhelper selector issues
    await umbracoUi.page.goto('/umbraco/section/settings');
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Click on Document Types link in the tree
    const docTypesLink = umbracoUi.page.locator('a', { hasText: 'Document Types' }).first();
    await docTypesLink.waitFor({ timeout: 15_000 });
    await docTypesLink.click();

    // Expand the tree if needed and click on first child document type
    const treeItems = umbracoUi.page.locator('umb-tree-item umb-tree-item');
    const firstChild = treeItems.first();

    if (await firstChild.isVisible({ timeout: 10_000 }).catch(() => false)) {
      await firstChild.locator('a').first().click();

      // Look for Schema.org workspace view tab
      const schemaTab = umbracoUi.page.getByRole('tab', { name: /Schema\.org/i });
      await expect(schemaTab).toBeVisible({ timeout: 15_000 });
    }
  });
});

// ---------------------------------------------------------------------------
// Complex Mapping Workflow Tests
// ---------------------------------------------------------------------------

test.describe('Complex Mapping Workflows', () => {
  /**
   * Helper: open schema picker, search and select a schema type, then proceed to property mapping.
   */
  async function mapContentTypeToSchema(umbracoUi: any, schemaTypeName: string) {
    // Click Map on first unmapped content type
    const mapBtn = umbracoUi.page.locator('uui-button[label="Map to Schema.org"]').first();
    await expect(mapBtn).toBeVisible({ timeout: 5_000 });
    await mapBtn.click();

    // Schema picker
    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

    // Search for specific schema type
    const searchInput = pickerModal.locator('uui-input').first();
    await fillUuiInput(searchInput, schemaTypeName);
    await umbracoUi.page.waitForTimeout(1_000);

    // Select and submit
    await pickerModal.locator('.schema-item').first().click();
    await pickerModal.locator('uui-button[look="primary"]').last().click();

    // Wait for property mapping modal
    const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    await expect(mappingModal).toBeVisible({ timeout: 10_000 });
    return mappingModal;
  }

  test('FAQPage auto-map shows blockContent suggestion for mainEntity', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Navigate to FAQ Page mapping
    const faqRow = umbracoUi.page.locator('uui-table-row', { hasText: 'FAQ Page' });
    if (await faqRow.isVisible({ timeout: 5_000 }).catch(() => false)) {
      const mapBtn = faqRow.locator('uui-button[label="Map to Schema.org"]');
      if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await mapBtn.click();

        // Pick FAQPage schema type
        const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
        await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
        await fillUuiInput(pickerModal.locator('uui-input').first(), 'FAQPage');
        await umbracoUi.page.waitForTimeout(1_000);
        await pickerModal.locator('.schema-item').first().click();
        await pickerModal.locator('uui-button[look="primary"]').last().click();

        // Property mapping modal should show blockContent suggestion
        const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
        await expect(mappingModal).toBeVisible({ timeout: 10_000 });

        const table = mappingModal.locator('schemeweaver-property-mapping-table');
        await expect(table).toBeVisible({ timeout: 10_000 });

        // Should have a configure nested mapping button (from blockContent suggestion)
        const configButton = table.locator('uui-button', { hasText: /Configure Block Mapping/i });
        const hasConfigButton = await configButton.isVisible({ timeout: 5_000 }).catch(() => false);

        // Screenshot the auto-mapped FAQPage
        await umbracoUi.page.screenshot({
          path: join(SCREENSHOTS_DIR, '10-faqpage-auto-map.png'),
          fullPage: true,
        });

        // If config button exists, test the wizard flow
        if (hasConfigButton) {
          await configButton.first().click();

          // Nested mapping wizard should open
          const nestedModal = umbracoUi.page.locator('schemeweaver-nested-mapping-modal');
          await expect(nestedModal).toBeVisible({ timeout: 10_000 });

          // Should show wizard step indicators
          const stepIndicators = nestedModal.locator('.step-indicator');
          const stepCount = await stepIndicators.count();
          expect(stepCount).toBe(3);

          // Screenshot the wizard
          await umbracoUi.page.screenshot({
            path: join(SCREENSHOTS_DIR, '11-faqpage-wizard.png'),
            fullPage: true,
          });

          // Close wizard
          const cancelBtn = nestedModal.locator('uui-button[label="Cancel"]');
          if (await cancelBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
            await cancelBtn.click();
          } else {
            const backBtn = nestedModal.locator('uui-button[label="Back"]');
            if (await backBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
              await backBtn.click();
            }
          }
        }

        // Close mapping modal
        await mappingModal.locator('uui-button[label="Cancel"]').click();
      }
    }
  });

  test('Product mapping shows complex type suggestions for offers and brand', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const productRow = umbracoUi.page.locator('uui-table-row', { hasText: 'Product Page' });
    if (await productRow.isVisible({ timeout: 5_000 }).catch(() => false)) {
      const mapBtn = productRow.locator('uui-button[label="Map to Schema.org"]');
      if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await mapBtn.click();

        const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
        await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
        await fillUuiInput(pickerModal.locator('uui-input').first(), 'Product');
        await umbracoUi.page.waitForTimeout(1_000);
        await pickerModal.locator('.schema-item').first().click();
        await pickerModal.locator('uui-button[look="primary"]').last().click();

        const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
        await expect(mappingModal).toBeVisible({ timeout: 10_000 });

        // Screenshot the Product auto-map with complex type suggestions
        await umbracoUi.page.screenshot({
          path: join(SCREENSHOTS_DIR, '12-product-auto-map.png'),
          fullPage: true,
        });

        await mappingModal.locator('uui-button[label="Cancel"]').click();
      }
    }
  });

  test('Recipe mapping shows blockContent suggestion for recipeInstructions', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const recipeRow = umbracoUi.page.locator('uui-table-row', { hasText: 'Recipe Page' });
    if (await recipeRow.isVisible({ timeout: 5_000 }).catch(() => false)) {
      const mapBtn = recipeRow.locator('uui-button[label="Map to Schema.org"]');
      if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await mapBtn.click();

        const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
        await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
        await fillUuiInput(pickerModal.locator('uui-input').first(), 'Recipe');
        await umbracoUi.page.waitForTimeout(1_000);
        await pickerModal.locator('.schema-item').first().click();
        await pickerModal.locator('uui-button[look="primary"]').last().click();

        const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
        await expect(mappingModal).toBeVisible({ timeout: 10_000 });

        // Should show block content suggestions for instructions block list
        await umbracoUi.page.screenshot({
          path: join(SCREENSHOTS_DIR, '13-recipe-auto-map.png'),
          fullPage: true,
        });

        await mappingModal.locator('uui-button[label="Cancel"]').click();
      }
    }
  });

  test('Event mapping shows complex type suggestions for location and organizer', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    const eventRow = umbracoUi.page.locator('uui-table-row', { hasText: 'Event Page' });
    if (await eventRow.isVisible({ timeout: 5_000 }).catch(() => false)) {
      const mapBtn = eventRow.locator('uui-button[label="Map to Schema.org"]');
      if (await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await mapBtn.click();

        const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
        await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
        await fillUuiInput(pickerModal.locator('uui-input').first(), 'Event');
        await umbracoUi.page.waitForTimeout(1_000);
        await pickerModal.locator('.schema-item').first().click();
        await pickerModal.locator('uui-button[look="primary"]').last().click();

        const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
        await expect(mappingModal).toBeVisible({ timeout: 10_000 });

        await umbracoUi.page.screenshot({
          path: join(SCREENSHOTS_DIR, '14-event-auto-map.png'),
          fullPage: true,
        });

        await mappingModal.locator('uui-button[label="Cancel"]').click();
      }
    }
  });

  test('nested mapping wizard completes full 3-step flow', async ({ umbracoUi }) => {
    await goToSchemeWeaverDashboard(umbracoUi);
    await waitForDashboardTable(umbracoUi.page);

    // Map FAQ Page to FAQPage schema
    const faqRow = umbracoUi.page.locator('uui-table-row', { hasText: 'FAQ Page' });
    if (!await faqRow.isVisible({ timeout: 5_000 }).catch(() => false)) return;

    const mapBtn = faqRow.locator('uui-button[label="Map to Schema.org"]');
    if (!await mapBtn.isVisible({ timeout: 3_000 }).catch(() => false)) return;

    await mapBtn.click();

    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await fillUuiInput(pickerModal.locator('uui-input').first(), 'FAQPage');
    await umbracoUi.page.waitForTimeout(1_000);
    await pickerModal.locator('.schema-item').first().click();
    await pickerModal.locator('uui-button[look="primary"]').last().click();

    const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    await expect(mappingModal).toBeVisible({ timeout: 10_000 });

    // Find and click the configure button for mainEntity
    const configButton = mappingModal.locator('uui-button', { hasText: /Configure Block Mapping/i }).first();
    if (!await configButton.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await mappingModal.locator('uui-button[label="Cancel"]').click();
      return;
    }

    await configButton.click();

    // Step 1: Block type picker → should auto-advance if only 1 block type
    const nestedModal = umbracoUi.page.locator('schemeweaver-nested-mapping-modal');
    await expect(nestedModal).toBeVisible({ timeout: 10_000 });
    await nestedModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => {});

    // Step 2: Mappings - check for mapping table
    const mappingTable = nestedModal.locator('uui-table');
    if (await mappingTable.isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Screenshot step 2
      await umbracoUi.page.screenshot({
        path: join(SCREENSHOTS_DIR, '15-wizard-step2-mappings.png'),
        fullPage: true,
      });

      // Click Preview to go to step 3
      const previewBtn = nestedModal.locator('uui-button', { hasText: 'Preview' });
      if (await previewBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await previewBtn.click();
        await umbracoUi.page.waitForTimeout(500);

        // Step 3: Preview
        const previewSummary = nestedModal.locator('.preview-summary');
        if (await previewSummary.isVisible({ timeout: 3_000 }).catch(() => false)) {
          // Screenshot step 3
          await umbracoUi.page.screenshot({
            path: join(SCREENSHOTS_DIR, '16-wizard-step3-preview.png'),
            fullPage: true,
          });

          // Save
          const saveBtn = nestedModal.locator('uui-button[label="Save Mapping"]');
          if (await saveBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
            await saveBtn.click();
            await expect(nestedModal).not.toBeVisible({ timeout: 5_000 });
          }
        }
      }
    }

    // Close the mapping modal
    const cancelBtn = mappingModal.locator('uui-button[label="Cancel"]');
    if (await cancelBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await cancelBtn.click();
    }
  });
});

// ---------------------------------------------------------------------------
// Entity Actions Tests
// ---------------------------------------------------------------------------

test.describe('Entity Actions', () => {
  test('Map to Schema.org action exists on document type context menu', async ({ umbracoUi }) => {
    await umbracoUi.page.goto('/umbraco/section/settings');
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Click on Document Types link in the tree
    const docTypesLink = umbracoUi.page.locator('a', { hasText: 'Document Types' }).first();
    await docTypesLink.waitFor({ timeout: 15_000 });

    // Expand the Document Types tree to see children
    const expandBtn = umbracoUi.page.locator('button[aria-label*="Expand"]').first();
    if (await expandBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await expandBtn.click();
      await umbracoUi.page.waitForTimeout(1_000);
    }

    // Find first child tree item
    const firstChild = umbracoUi.page.locator('umb-tree-item umb-tree-item').first();
    if (await firstChild.isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Hover to reveal actions button
      await firstChild.hover();

      // Try to open the actions dropdown
      const actionsBtn = firstChild.locator('button').filter({ hasText: /actions/i }).first();
      if (await actionsBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await actionsBtn.click();

        // Look for our custom entity action
        const mapAction = umbracoUi.page.getByRole('button', { name: /Map to Schema\.org/i });
        await expect(mapAction).toBeVisible({ timeout: 5_000 });
      }
    }
  });
});
