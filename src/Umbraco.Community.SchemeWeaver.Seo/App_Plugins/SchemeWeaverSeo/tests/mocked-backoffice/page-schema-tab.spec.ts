import { expect, test } from '@playwright/test';
import { openBackoffice, resetMockDbs } from './fixtures/seed';

/**
 * The SEO "Schema" workspace tab mounts on a document workspace and the entry point hides the
 * core "JSON-LD" tab (the mock server-context reports hideDeveloperJsonLdTab=true).
 */
test.describe('Mocked Backoffice — page Schema tab', () => {
  test.beforeEach(async ({ page }) => {
    await openBackoffice(page);
    await resetMockDbs(page);
  });

  test('Schema tab is attached and the developer JSON-LD tab is excluded', async ({ page }) => {
    await page.goto('/section/content/workspace/document/edit/11111111-1111-1111-1111-111111111111');
    await expect(page.locator('[data-mark="workspace:view-link:SchemeWeaver.Seo.WorkspaceView.PageSchema"]')).toBeAttached({ timeout: 30_000 });
    await expect(page.locator('[data-mark="workspace:view-link:SchemeWeaver.WorkspaceView.JsonLdPreview"]')).toHaveCount(0);
  });
});
