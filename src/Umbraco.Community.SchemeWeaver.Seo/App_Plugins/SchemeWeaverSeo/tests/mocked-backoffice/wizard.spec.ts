import { expect, test } from '@playwright/test';
import { openBackoffice, resetMockDbs } from './fixtures/seed';

/** Drives the set-up wizard end to end against the mock DB and checks what it saved. */
test.describe('Mocked Backoffice — set-up wizard', () => {
  test.beforeEach(async ({ page }) => {
    await openBackoffice(page);
    await resetMockDbs(page);
  });

  test('sets blogArticle up as an article and saves the pre-filled headline', async ({ page }) => {
    await page.goto('/section/seo-schema/view/content-types');
    await page.locator('[data-mark="schemeweaver-seo:set-up:blogArticle"]').click();

    const card = page.locator('[data-mark="schemeweaver-seo:template-card:article"]');
    await expect(card).toBeVisible({ timeout: 30_000 });
    await card.click();
    await page.locator('[data-mark="schemeweaver-seo:wizard-next"]').click();

    await expect(page.locator('[data-mark="schemeweaver-seo:slot:headline"]')).toBeVisible({ timeout: 30_000 });
    await page.locator('[data-mark="schemeweaver-seo:wizard-next"]').click();
    await page.locator('[data-mark="schemeweaver-seo:wizard-save"]').click();
    await expect(page.locator('[data-mark="schemeweaver-seo:wizard-finish"]')).toBeEnabled({ timeout: 30_000 });

    const saved = await page.evaluate(() => {
      const db = (window as unknown as { __schemeweaverSeoMockDb: { getMapping: (alias: string) => { templateId: string; slots: Record<string, { propertyAlias?: string }> } | undefined } }).__schemeweaverSeoMockDb;
      return db.getMapping('blogArticle');
    });
    expect(saved?.templateId).toBe('article');
    expect(saved?.slots.headline?.propertyAlias).toBe('title');
  });
});
