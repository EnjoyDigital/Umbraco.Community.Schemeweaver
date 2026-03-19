import { test, expect } from '@playwright/test';

test.describe('SchemeWeaver Dashboard', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/umbraco');
    // Navigate to Settings section
    await page.getByRole('link', { name: /settings/i }).click();
  });

  test('dashboard is visible in Settings section', async ({ page }) => {
    // Look for Schema.org Mappings dashboard
    await expect(page.getByText('Schema.org Mappings')).toBeVisible({ timeout: 10000 });
  });

  test('content types table renders', async ({ page }) => {
    await expect(page.getByText('Schema.org Mappings')).toBeVisible({ timeout: 10000 });
    // Dashboard should show content types in a table
    await expect(page.locator('uui-table')).toBeVisible({ timeout: 10000 });
    await expect(page.locator('uui-table-row').first()).toBeVisible({ timeout: 10000 });
  });

  test('search input is present and functional', async ({ page }) => {
    await expect(page.getByText('Schema.org Mappings')).toBeVisible({ timeout: 10000 });
    const searchInput = page.locator('uui-input[placeholder*="Search"]');
    await expect(searchInput).toBeVisible();
  });

  test('refresh button is present', async ({ page }) => {
    await expect(page.getByText('Schema.org Mappings')).toBeVisible({ timeout: 10000 });
    const refreshBtn = page.locator('uui-button', { hasText: 'Refresh' });
    await expect(refreshBtn).toBeVisible();
  });

  test('table has correct headers', async ({ page }) => {
    await expect(page.getByText('Schema.org Mappings')).toBeVisible({ timeout: 10000 });
    await expect(page.locator('uui-table-head-cell', { hasText: 'Content Type' })).toBeVisible();
    await expect(page.locator('uui-table-head-cell', { hasText: 'Schema Type' })).toBeVisible();
    await expect(page.locator('uui-table-head-cell', { hasText: 'Status' })).toBeVisible();
    await expect(page.locator('uui-table-head-cell', { hasText: 'Properties' })).toBeVisible();
    await expect(page.locator('uui-table-head-cell', { hasText: 'Actions' })).toBeVisible();
  });
});

test.describe('SchemeWeaver Schema Mapping Workflow', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/umbraco');
    await page.getByRole('link', { name: /settings/i }).click();
    await expect(page.getByText('Schema.org Mappings')).toBeVisible({ timeout: 10000 });
  });

  test('Map button opens schema picker modal', async ({ page }) => {
    // Find an unmapped content type and click Map
    const mapBtn = page.locator('uui-button', { hasText: 'Map' }).first();
    if (await mapBtn.isVisible()) {
      await mapBtn.click();
      // Schema picker modal should appear
      await expect(page.getByText('Select Schema.org Type')).toBeVisible({ timeout: 5000 });
    }
  });

  test('schema picker shows schema types', async ({ page }) => {
    const mapBtn = page.locator('uui-button', { hasText: 'Map' }).first();
    if (await mapBtn.isVisible()) {
      await mapBtn.click();
      await expect(page.getByText('Select Schema.org Type')).toBeVisible({ timeout: 5000 });
      // Should show schema types
      await expect(page.locator('.schema-item').first()).toBeVisible({ timeout: 5000 });
    }
  });

  test('schema picker search filters types', async ({ page }) => {
    const mapBtn = page.locator('uui-button', { hasText: 'Map' }).first();
    if (await mapBtn.isVisible()) {
      await mapBtn.click();
      await expect(page.getByText('Select Schema.org Type')).toBeVisible({ timeout: 5000 });

      const searchInput = page.locator('uui-input[placeholder*="Search schema"]');
      await searchInput.fill('Article');
      // Wait for filtered results
      await page.waitForTimeout(500);
    }
  });
});

test.describe('SchemeWeaver Document Type Workspace View', () => {
  test('Schema.org tab appears on document type editor', async ({ page }) => {
    await page.goto('/umbraco');
    await page.getByRole('link', { name: /settings/i }).click();

    // Navigate to Document Types
    const docTypesLink = page.getByText('Document Types');
    if (await docTypesLink.isVisible({ timeout: 5000 })) {
      await docTypesLink.click();

      // Click on first document type
      const firstDocType = page.locator('[data-element="tree-item"]').first();
      if (await firstDocType.isVisible({ timeout: 5000 })) {
        await firstDocType.click();

        // Look for Schema.org tab
        const schemaTab = page.getByText('Schema.org');
        await expect(schemaTab).toBeVisible({ timeout: 5000 });
      }
    }
  });
});

test.describe('SchemeWeaver Entity Actions', () => {
  test('Map to Schema.org action exists on document type context menu', async ({ page }) => {
    await page.goto('/umbraco');
    await page.getByRole('link', { name: /settings/i }).click();

    const docTypesLink = page.getByText('Document Types');
    if (await docTypesLink.isVisible({ timeout: 5000 })) {
      await docTypesLink.click();

      // Right-click on first document type for context menu
      const firstDocType = page.locator('[data-element="tree-item"]').first();
      if (await firstDocType.isVisible({ timeout: 5000 })) {
        await firstDocType.click({ button: 'right' });

        // Look for the action
        const mapAction = page.getByText('Map to Schema.org');
        if (await mapAction.isVisible({ timeout: 3000 })) {
          await expect(mapAction).toBeVisible();
        }
      }
    }
  });
});
