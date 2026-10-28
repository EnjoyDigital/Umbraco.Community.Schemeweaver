import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';
import { join } from 'path';
import { mkdirSync } from 'fs';

/**
 * Captures the SchemeWeaver SEO documentation screenshots into docs/images/
 * (seo-*.png). Runs in the e2e-seo Playwright project. The wizard screenshots
 * restore the mapping they touched; the identity screenshot restores the
 * identity, so the host is left as found.
 */

const IMAGES_DIR = join(__dirname, '..', '..', '..', '..', '..', '..', 'docs', 'images');
const SEO = '/umbraco/management/api/v1/schemeweaver-seo';
const CORE = '/umbraco/management/api/v1/schemeweaver';

mkdirSync(IMAGES_DIR, { recursive: true });

test.use({ viewport: { width: 1440, height: 900 } });

async function shoot(page: any, name: string) {
  await page.waitForTimeout(700);
  await page.screenshot({ path: join(IMAGES_DIR, `${name}.png`), fullPage: false });
}

async function openSection(umbracoUi: any, view: string, mark: string) {
  await umbracoUi.goToBackOffice();
  await umbracoUi.page.goto(`/umbraco/section/seo-schema/view/${view}`);
  await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
  await expect(umbracoUi.page.getByTestId(mark).first()).toBeVisible({ timeout: 30_000 });
}

test.describe.serial('SEO docs screenshots', () => {
  test('seo-site-identity', async ({ umbracoUi }) => {
    const before = await (await umbracoUi.page.request.get(`${SEO}/site-identity`)).json();
    try {
      await umbracoUi.page.request.put(`${SEO}/site-identity`, {
        data: {
          kind: 'organisation',
          name: 'Enjoy Digital',
          alternateName: 'Enjoy',
          url: 'https://www.enjoy-digital.co.uk',
          sameAs: ['https://www.linkedin.com/company/enjoy-digital', 'https://x.com/enjoydigital'],
          telephone: '+44 113 000 0000',
          email: 'hello@enjoy-digital.co.uk',
          openingHours: [],
        },
      });
      await openSection(umbracoUi, 'site-identity', 'schemeweaver-seo:identity-name');
      await shoot(umbracoUi.page, 'seo-site-identity');

      await openSection(umbracoUi, 'overview', 'schemeweaver-seo:overview-identity');
      await umbracoUi.page.getByTestId('schemeweaver-seo:run-health').click();
      await expect(umbracoUi.page.locator('uui-table-row').first()).toBeVisible({ timeout: 60_000 });
      await shoot(umbracoUi.page, 'seo-overview');
    } finally {
      if (before?.isConfigured) {
        await umbracoUi.page.request.put(`${SEO}/site-identity`, { data: before }).catch(() => undefined);
      } else {
        await umbracoUi.page.request.delete(`${SEO}/site-identity`).catch(() => undefined);
      }
    }
  });

  test('seo-content-types and wizard', async ({ umbracoUi }) => {
    const alias = 'blogArticle';
    const snapshotResponse = await umbracoUi.page.request.get(`${CORE}/mappings/${alias}`);
    const snapshot = snapshotResponse.ok() ? await snapshotResponse.json() : null;
    try {
      await openSection(umbracoUi, 'content-types', 'schemeweaver-seo:content-types-table');
      await shoot(umbracoUi.page, 'seo-content-types');

      await umbracoUi.page.getByTestId(`schemeweaver-seo:set-up:${alias}`).click();
      await expect(umbracoUi.page.getByTestId('schemeweaver-seo:template-card:article')).toBeVisible({ timeout: 30_000 });
      await umbracoUi.page.getByTestId('schemeweaver-seo:template-card:article').click();
      await shoot(umbracoUi.page, 'seo-wizard-kind');

      await umbracoUi.page.getByTestId('schemeweaver-seo:wizard-next').click();
      await expect(umbracoUi.page.getByTestId('schemeweaver-seo:slot:headline')).toBeVisible({ timeout: 30_000 });
      await shoot(umbracoUi.page, 'seo-wizard-fields');

      await umbracoUi.page.getByTestId('schemeweaver-seo:wizard-next').click();
      await umbracoUi.page.getByTestId('schemeweaver-seo:wizard-save').click();
      await expect(umbracoUi.page.getByTestId('schemeweaver-seo:wizard-finish')).toBeEnabled({ timeout: 30_000 });
      await umbracoUi.page.getByTestId('schemeweaver-seo:wizard-preview').waitFor({ timeout: 30_000 }).catch(() => {});
      await shoot(umbracoUi.page, 'seo-wizard-check');
    } finally {
      if (snapshot) {
        await umbracoUi.page.request.post(`${CORE}/mappings`, { data: snapshot }).catch(() => undefined);
      } else {
        await umbracoUi.page.request.delete(`${CORE}/mappings/${alias}`).catch(() => undefined);
      }
    }
  });

  test('seo-page-schema-tab', async ({ umbracoUi }) => {
    const alias = 'blogArticle';
    const samples = await (await umbracoUi.page.request.get(`${SEO}/content-types/${alias}/sample-pages`)).json();
    test.skip(!samples.length, 'No published blog article.');
    const key = samples[0].key as string;
    await umbracoUi.goToBackOffice();
    await umbracoUi.page.goto(`/umbraco/section/content/workspace/document/edit/${key}`);
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    const tab = umbracoUi.page.getByRole('tab', { name: /^Schema$/ });
    await tab.waitFor({ timeout: 30_000 });
    await tab.click();
    await expect(umbracoUi.page.getByTestId('schemeweaver-seo:page-type-select')).toBeVisible({ timeout: 30_000 });
    await shoot(umbracoUi.page, 'seo-page-schema-tab');
  });
});
