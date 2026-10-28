import { expect, test } from '@playwright/test';
import { openBackoffice, resetMockDbs } from './fixtures/seed';

/**
 * Proves the SEO section manifest, its four views and the section-permission condition all
 * resolve inside the real backoffice shell against mocked HTTP.
 */
test.describe('Mocked Backoffice — SEO Schema section', () => {
  test.beforeEach(async ({ page }) => {
    await openBackoffice(page);
    await resetMockDbs(page);
  });

  test('the section renders its four views with Overview as the default', async ({ page }) => {
    await page.goto('/section/seo-schema');
    await expect(page.locator('[data-mark="schemeweaver-seo:overview-identity"]')).toBeVisible({ timeout: 30_000 });

    const tabs = page.locator('uui-tab');
    for (const view of ['overview', 'site-identity', 'content-types', 'settings']) {
      await expect(tabs.filter({ has: page.locator(`a[href$="/view/${view}"]`) }).or(page.locator(`uui-tab[href$="/view/${view}"]`)).first()).toBeAttached();
    }
  });

  test('the Content types view lists the mock page types with statuses', async ({ page }) => {
    await page.goto('/section/seo-schema/view/content-types');
    await expect(page.locator('[data-mark="schemeweaver-seo:content-types-table"]')).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('[data-mark="schemeweaver-seo:content-type:blogArticle"]')).toBeVisible();
    await expect(page.locator('[data-mark="schemeweaver-seo:content-type:homePage"] schemeweaver-seo-status-tag')).toHaveAttribute('status', 'set-up');
  });
});
