import { expect, test } from '@playwright/test';
import { openBackoffice, resetMockDb } from './fixtures/seed';

const BASE = '/umbraco/management/api/v1/schemeweaver';

/**
 * Drives the real Umbraco backoffice UI into the Schema.org workspace
 * view and verifies it mounts against mocked data. This is the
 * highest-value spec in the tier — it's the only layer where the
 * workspace-view condition (workspaceAlias match), the Lit element,
 * the repository, and the MSW handler chain all run together inside
 * the real Umbraco shell.
 *
 * We pick a content type out of SchemeWeaver's own mock DB (rather
 * than relying on Umbraco-CMS mock seed data, which may shift between
 * releases), grab its key from the API, then navigate to the
 * document-type workspace at that URL.
 */
test.describe('Mocked Backoffice — workspace view', () => {
  test.beforeEach(async ({ page }) => {
    await openBackoffice(page);
    await resetMockDb(page);
  });

  test('Schema.org tab mounts on a seeded document type', async ({ page }) => {
    // Ask SchemeWeaver for any seeded content type. The mock DB ships
    // with a meaningful set, so we can pick the first one.
    const firstContentType = await page.evaluate(async (url) => {
      const res = await fetch(url);
      const list = (await res.json()) as Array<{ alias: string; name: string; key: string }>;
      return list[0];
    }, BASE + '/content-types');

    expect(firstContentType, 'mock DB should seed at least one content type').toBeTruthy();
    expect(firstContentType.key, 'content type should have a key').toBeTruthy();

    // Navigate directly to the doctype workspace. The Umbraco-CMS MSW seed
    // data may not include this specific key, so the main-editor body may
    // be empty or error — we only care that our workspace-view manifest
    // reaches the `umb-workspace-editor` shell and mounts a Schema.org tab.
    await page.goto(`/section/settings/workspace/document-type/edit/${firstContentType.key}`);

    // The workspace shell renders our view as a `<uui-tab>` carrying the
    // `data-mark` attribute Umbraco attaches to every workspace view link.
    // The specific mark is stable across versions and proves both that
    // the manifest was registered AND that the shell attached the view
    // to the doctype workspace URL. Assert attached rather than visible
    // because the mocked shell may route some tabs into an overflow menu.
    const schemaTab = page.locator(
      '[data-mark="workspace:view-link:SchemeWeaver.WorkspaceView.SchemaMapping"]',
    );
    await expect(schemaTab).toBeAttached({ timeout: 30_000 });
  });
});
