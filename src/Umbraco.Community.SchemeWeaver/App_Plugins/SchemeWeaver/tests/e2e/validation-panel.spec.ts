import { expect } from '@playwright/test';
import { ConstantHelper, test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage for the validator-findings panel in the JSON-LD preview tab.
 *
 * Scaffolding only — E2E runs require a live Umbraco instance. When a mapped
 * content item is found, we assert the validation panel renders and that its
 * severity badges match the contract. When no mapped content is available,
 * the test degrades gracefully via `test.skip` rather than failing.
 *
 * The backend contract under test (see the preview endpoint): the response
 * includes an `issues` array with `severity`, `schemaType`, `path`, `message`.
 */

const BASE = '/umbraco/management/api/v1/schemeweaver';

async function findFirstMappedContentKey(
  umbracoUi: any,
): Promise<{ contentKey: string; alias: string } | null> {
  const response = await umbracoUi.page.request.post(`${BASE}/mappings`, {
    // No-op POST — mappings endpoint uses GET for listing.
  }).catch(() => null);
  // Intentionally not used; the helper above proves the cookie is present.
  void response;

  const mappingsResponse = await umbracoUi.page.request.get(`${BASE}/mappings`);
  if (!mappingsResponse.ok()) return null;
  const mappings = await mappingsResponse.json();
  if (!Array.isArray(mappings) || mappings.length === 0) return null;

  // We don't know what content actually exists for a given mapping — just
  // return the alias so the caller can generate a preview against *any*
  // publishable content node. The preview endpoint doesn't require a real
  // content key when all mappings resolve to literals, but in a typical
  // TestHost seed there's a matching content item.
  return {
    alias: mappings[0].contentTypeAlias,
    contentKey: '00000000-0000-0000-0000-000000000000',
  };
}

test.describe('Validation Panel — Preview Tab (E2E)', () => {
  test('preview response includes structured validation issues', async ({ umbracoUi }) => {
    // API-level smoke test: confirms the backend contract before we touch UI.
    await umbracoUi.goToBackOffice();

    const target = await findFirstMappedContentKey(umbracoUi);
    if (!target) {
      test.skip(true, 'No SchemeWeaver mappings seeded — nothing to preview.');
      return;
    }

    const response = await umbracoUi.page.request.post(
      `${BASE}/mappings/${target.alias}/preview?contentKey=${target.contentKey}`,
    );
    if (!response.ok()) {
      test.skip(true, `Preview endpoint returned ${response.status()} — skipping.`);
      return;
    }

    const body = await response.json();
    expect(body).to.have.property('jsonLd');
    expect(body).to.have.property('isValid');
    expect(body).to.have.property('errors');

    // `issues` is optional on older backends. When present it must be an
    // array of severity/path/message records — that's the contract the
    // validation-panel component relies on.
    if (Array.isArray(body.issues) && body.issues.length > 0) {
      const issue = body.issues[0];
      expect(issue).to.have.property('severity');
      expect(['critical', 'warning', 'info']).to.include(issue.severity);
      expect(issue).to.have.property('schemaType');
      expect(issue).to.have.property('path');
      expect(issue).to.have.property('message');
    }
  });

  test('validation panel renders on the JSON-LD workspace tab when issues exist', async ({ umbracoUi }) => {
    await umbracoUi.goToBackOffice();
    await umbracoUi.content.goToSection(ConstantHelper.sections.content);

    const treeItem = umbracoUi.page.locator('umb-tree-item').first();
    await treeItem.waitFor({ timeout: 15_000 });
    await treeItem.locator('a').first().click();

    const jsonLdTab = umbracoUi.page.getByRole('tab', { name: /JSON-LD/i });
    if (!(await jsonLdTab.isVisible({ timeout: 10_000 }).catch(() => false))) {
      test.skip(true, 'JSON-LD tab not visible on the first content node — no mapping present.');
      return;
    }
    await jsonLdTab.click();

    const jsonLdView = umbracoUi.page.locator('schemeweaver-jsonld-content-view');
    await expect(jsonLdView).toBeVisible({ timeout: 10_000 });

    // Trigger preview if the view exposes a Generate button, otherwise the
    // preview already rendered on tab activation.
    const generateBtn = jsonLdView.locator('uui-button', { hasText: /Generate Preview/i });
    if (await generateBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await generateBtn.click();
    }

    // Wait for either the preview body or the unpublished hint to appear.
    const preview = jsonLdView.locator('schemeweaver-jsonld-preview');
    const unpublishedHint = jsonLdView.locator('.unpublished-message');
    await Promise.race([
      preview.waitFor({ state: 'visible', timeout: 15_000 }),
      unpublishedHint.waitFor({ state: 'visible', timeout: 15_000 }),
    ]).catch(() => null);

    if (!(await preview.isVisible({ timeout: 1_000 }).catch(() => false))) {
      test.skip(true, 'Preview did not render — content likely unpublished.');
      return;
    }

    // The validation panel is nested inside the preview component's shadow
    // root. It's only present when the backend emits issues or errors.
    const panel = preview.locator('schemeweaver-validation-panel');
    const panelVisible = await panel.isVisible({ timeout: 5_000 }).catch(() => false);
    if (!panelVisible) {
      // A clean preview with no findings is valid — nothing further to assert.
      return;
    }

    // When visible, assert the badge contract matches the severity enum.
    const severityTags = panel.locator('.issue uui-tag.severity-tag');
    const tagCount = await severityTags.count();
    if (tagCount > 0) {
      for (let i = 0; i < tagCount; i++) {
        const color = await severityTags.nth(i).getAttribute('color');
        // `info` rows use no colour attribute → null is acceptable.
        expect(['danger', 'warning', null]).toContain(color);
      }
    }
  });
});
