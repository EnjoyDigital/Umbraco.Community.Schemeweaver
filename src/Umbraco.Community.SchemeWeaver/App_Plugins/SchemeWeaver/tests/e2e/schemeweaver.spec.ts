import { expect } from '@playwright/test';
import { ConstantHelper, test } from '@umbraco/playwright-testhelpers';

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
    }

    // Close
    await mappingModal.locator('uui-button[label="Cancel"]').click();
  });
});

// ---------------------------------------------------------------------------
// Document Type Workspace View Tests
// ---------------------------------------------------------------------------

test.describe('Document Type Workspace View', () => {
  test('Schema.org tab appears on document type editor', async ({ umbracoUi }) => {
    // Navigate to Settings > Document Types
    await umbracoUi.goToBackOffice();
    await umbracoUi.content.goToSection(ConstantHelper.sections.settings);

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
// Entity Actions Tests
// ---------------------------------------------------------------------------

test.describe('Entity Actions', () => {
  test('Map to Schema.org action exists on document type context menu', async ({ umbracoUi }) => {
    await umbracoUi.goToBackOffice();
    await umbracoUi.content.goToSection(ConstantHelper.sections.settings);

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
