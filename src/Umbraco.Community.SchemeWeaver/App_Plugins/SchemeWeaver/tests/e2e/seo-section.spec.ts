import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage for the SchemeWeaver SEO satellite: the "SEO Schema" section, its views, the
 * set-up wizard writing an ordinary mapping, the page "Schema" tab and the site identity.
 *
 * Runs against the TestHost (which includes the SEO satellite). Every mutation targets the
 * synthetic `seoE2e*` state or restores what it changed in a `finally` block, so the seeded
 * mappings the other suites rely on are never touched.
 */

const SEO = '/umbraco/management/api/v1/schemeweaver-seo';
const CORE = '/umbraco/management/api/v1/schemeweaver';

async function firstContentTypeWithPages(umbracoUi: any, preferAlias = 'blogArticle') {
  const response = await umbracoUi.page.request.get(`${SEO}/content-types`);
  expect(response.ok(), `content-types failed: ${response.status()}`).toBeTruthy();
  const rows = (await response.json()) as Array<{ alias: string; isElement: boolean; publishedCount: number; status: string }>;
  return rows.find((r) => r.alias === preferAlias && r.publishedCount > 0)
    ?? rows.find((r) => !r.isElement && r.publishedCount > 0)
    ?? null;
}

async function snapshotMapping(umbracoUi: any, alias: string) {
  const response = await umbracoUi.page.request.get(`${CORE}/mappings/${alias}`);
  return response.ok() ? await response.json() : null;
}

async function restoreMapping(umbracoUi: any, alias: string, snapshot: any) {
  if (snapshot) {
    await umbracoUi.page.request.post(`${CORE}/mappings`, { data: snapshot }).catch(() => undefined);
  } else {
    await umbracoUi.page.request.delete(`${CORE}/mappings/${alias}`).catch(() => undefined);
  }
}

test.describe('SEO Schema section (E2E)', () => {
  test('the section is visible to the admin and its views load', async ({ umbracoUi }) => {
    await umbracoUi.goToBackOffice();
    await umbracoUi.page.goto('/umbraco/section/seo-schema/view/overview');

    const overview = umbracoUi.page.getByTestId('schemeweaver-seo:overview-identity');
    await expect(overview).toBeVisible({ timeout: 30_000 });
    await expect(umbracoUi.page.getByTestId('schemeweaver-seo:overview-content-types')).toBeVisible();

    await umbracoUi.page.goto('/umbraco/section/seo-schema/view/content-types');
    await expect(umbracoUi.page.getByTestId('schemeweaver-seo:content-types-table')).toBeVisible({ timeout: 30_000 });

    await umbracoUi.page.goto('/umbraco/section/seo-schema/view/site-identity');
    await expect(umbracoUi.page.getByTestId('schemeweaver-seo:identity-name')).toBeVisible({ timeout: 30_000 });
  });

  test('server context reports the section alias the frontend registers', async ({ umbracoUi }) => {
    const response = await umbracoUi.page.request.get(`${SEO}/server-context`);
    expect(response.ok()).toBeTruthy();
    const context = await response.json();
    expect(context.sectionAlias).toBe('SchemeWeaver.Seo.Section');
    expect(typeof context.hideDeveloperJsonLdTab).toBe('boolean');
  });

  test('the wizard sets a page type up as a kind and the developer API sees the same mapping', async ({ umbracoUi }) => {
    const target = await firstContentTypeWithPages(umbracoUi);
    test.skip(!target, 'No published page type available in this host.');
    const alias = target!.alias;
    const snapshot = await snapshotMapping(umbracoUi, alias);

    try {
      await umbracoUi.goToBackOffice();
      await umbracoUi.page.goto('/umbraco/section/seo-schema/view/content-types');
      await umbracoUi.page.getByTestId(`schemeweaver-seo:set-up:${alias}`).click();

      // Step 1: pick the Article kind.
      const card = umbracoUi.page.getByTestId('schemeweaver-seo:template-card:article');
      await expect(card).toBeVisible({ timeout: 30_000 });
      await card.click();
      await umbracoUi.page.getByTestId('schemeweaver-seo:wizard-next').click();

      // Step 2: the headline slot is pre-filled (title/headline synonym) and shown.
      await expect(umbracoUi.page.getByTestId('schemeweaver-seo:slot:headline')).toBeVisible({ timeout: 30_000 });
      await umbracoUi.page.getByTestId('schemeweaver-seo:wizard-next').click();

      // Step 3: save and preview.
      await umbracoUi.page.getByTestId('schemeweaver-seo:wizard-save').click();
      await expect(umbracoUi.page.getByTestId('schemeweaver-seo:wizard-finish')).toBeEnabled({ timeout: 30_000 });

      const saved = await snapshotMapping(umbracoUi, alias);
      expect(saved).not.toBeNull();
      expect(['Article', 'BlogPosting', 'NewsArticle', 'TechArticle']).toContain(saved.schemaTypeName);
      expect(saved.propertyMappings.some((r: any) => r.schemaPropertyName === 'headline')).toBeTruthy();

      await umbracoUi.page.getByTestId('schemeweaver-seo:wizard-finish').click();
    } finally {
      await restoreMapping(umbracoUi, alias, snapshot);
    }
  });

  test('the page Schema tab replaces the developer JSON-LD tab and can retype and exclude a page', async ({ umbracoUi }) => {
    const target = await firstContentTypeWithPages(umbracoUi);
    test.skip(!target, 'No published page type available in this host.');
    const alias = target!.alias;
    const snapshot = await snapshotMapping(umbracoUi, alias);

    const samples = await (await umbracoUi.page.request.get(`${SEO}/content-types/${alias}/sample-pages`)).json();
    test.skip(!samples.length, 'No sample page.');
    const key = samples[0].key as string;

    try {
      const setup = await umbracoUi.page.request.put(`${SEO}/content-types/${alias}/mapping`, {
        data: {
          templateId: 'article',
          schemaTypeName: 'Article',
          slots: { headline: { kind: 'property', propertyAlias: '__name' }, datePublished: { kind: 'property', propertyAlias: '__createDate' } },
        },
      });
      expect(setup.ok(), `mapping save failed: ${setup.status()}`).toBeTruthy();

      await umbracoUi.goToBackOffice();
      await umbracoUi.page.goto(`/umbraco/section/content/workspace/document/edit/${key}`);
      await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
      const schemaTab = umbracoUi.page.getByRole('tab', { name: /^Schema$/ });
      await schemaTab.waitFor({ timeout: 30_000 });
      // The TestHost keeps the developer tab (SchemeWeaver:Seo:HideDeveloperJsonLdTab=false) so the
      // core specs still find it; on a default install the SEO tab replaces it.
      const context = await (await umbracoUi.page.request.get(`${SEO}/server-context`)).json();
      await expect(umbracoUi.page.getByRole('tab', { name: /JSON-LD/i })).toHaveCount(context.hideDeveloperJsonLdTab ? 0 : 1);
      await schemaTab.click();
      await expect(umbracoUi.page.getByTestId('schemeweaver-seo:page-type-select')).toBeVisible({ timeout: 30_000 });

      // Retype via the API (the select is a UUI component) and confirm the summary follows.
      const retyped = await umbracoUi.page.request.put(`${SEO}/pages/${key}`, { data: { schemaTypeName: 'NewsArticle', isExcluded: false } });
      expect(retyped.ok()).toBeTruthy();
      const summary = await (await umbracoUi.page.request.get(`${SEO}/pages/${key}/summary`)).json();
      expect(summary.effectiveSchemaTypeName).toBe('NewsArticle');
      expect(summary.preview.jsonLd).toContain('"NewsArticle"');

      const excluded = await umbracoUi.page.request.put(`${SEO}/pages/${key}`, { data: { isExcluded: true } });
      expect(excluded.ok()).toBeTruthy();
      await umbracoUi.page.reload();
      await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
      await umbracoUi.page.getByRole('tab', { name: /^Schema$/ }).click();
      await expect(umbracoUi.page.getByTestId('schemeweaver-seo:page-excluded')).toBeVisible({ timeout: 30_000 });
    } finally {
      await umbracoUi.page.request.delete(`${SEO}/pages/${key}`).catch(() => undefined);
      await restoreMapping(umbracoUi, alias, snapshot);
    }
  });

  test('site identity saves and reaches the live page graph', async ({ umbracoUi }) => {
    const before = await (await umbracoUi.page.request.get(`${SEO}/site-identity`)).json();
    try {
      const bad = await umbracoUi.page.request.put(`${SEO}/site-identity`, { data: { kind: 'organisation', name: '', sameAs: [], openingHours: [] } });
      expect(bad.status()).toBe(400);

      const ok = await umbracoUi.page.request.put(`${SEO}/site-identity`, {
        data: { kind: 'organisation', name: 'SEO E2E Org', sameAs: ['https://www.linkedin.com/company/seo-e2e'], openingHours: [] },
      });
      expect(ok.ok()).toBeTruthy();

      const context = await (await umbracoUi.page.request.get(`${SEO}/server-context`)).json();
      expect(context.siteIdentityConfigured).toBe(true);

      const target = await firstContentTypeWithPages(umbracoUi);
      if (target) {
        const samples = await (await umbracoUi.page.request.get(`${SEO}/content-types/${target.alias}/sample-pages`)).json();
        if (samples.length) {
          const summary = await (await umbracoUi.page.request.get(`${SEO}/pages/${samples[0].key}/summary`)).json();
          if (summary.preview) {
            expect(summary.preview.jsonLd).toContain('SEO E2E Org');
          }
        }
      }
    } finally {
      if (before?.isConfigured) {
        await umbracoUi.page.request.put(`${SEO}/site-identity`, { data: before }).catch(() => undefined);
      } else {
        await umbracoUi.page.request.delete(`${SEO}/site-identity`).catch(() => undefined);
      }
    }
  });
});
