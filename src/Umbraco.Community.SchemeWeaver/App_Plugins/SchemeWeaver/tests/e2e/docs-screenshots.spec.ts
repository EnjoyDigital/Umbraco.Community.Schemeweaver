import { expect } from '@playwright/test';
import { ConstantHelper, test } from '@umbraco/playwright-testhelpers';
import { join } from 'path';
import { mkdirSync, readdirSync, readFileSync } from 'fs';

/**
 * Captures the documentation screenshot set into docs/images/.
 *
 * Unlike screenshots.spec.ts (E2E artefacts in /screenshots), these images are
 * embedded in the markdown docs, so filenames are semantic and stable. Tests
 * run serially; any test that mutates a seeded mapping snapshots it first and
 * restores it afterwards, so the demo site is left exactly as found.
 */

const IMAGES_DIR = join(__dirname, '..', '..', '..', '..', '..', '..', 'docs', 'images');
const API_BASE = '/umbraco/management/api/v1/schemeweaver';

mkdirSync(IMAGES_DIR, { recursive: true });

test.use({ viewport: { width: 1440, height: 900 } });

async function fillUuiInput(locator: any, text: string) {
  await locator.locator('input').fill(text);
}

async function resolveDocTypeKey(page: any, contentTypeAlias: string): Promise<string> {
  const response = await page.request.get(`${API_BASE}/content-types`);
  const data = await response.json();
  const ct = data.find((t: any) => t.alias === contentTypeAlias);
  if (!ct) throw new Error(`Content type '${contentTypeAlias}' not found`);
  return ct.key;
}

async function goToDocTypeSchemaTab(umbracoUi: any, contentTypeAlias: string) {
  const key = await resolveDocTypeKey(umbracoUi.page, contentTypeAlias);
  await umbracoUi.page.goto(`/umbraco/section/settings/workspace/document-type/edit/${key}`);
  await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  const schemaTab = umbracoUi.page.getByRole('tab', { name: /Schema\.org/i });
  await schemaTab.waitFor({ timeout: 15_000 });
  await schemaTab.click();
  await umbracoUi.page.locator('schemeweaver-schema-mapping-view').waitFor({ timeout: 15_000 });
  await umbracoUi.page.waitForTimeout(750);
}

async function getMapping(page: any, alias: string): Promise<any | null> {
  const res = await page.request.get(`${API_BASE}/mappings`);
  if (!res.ok()) return null;
  const all = await res.json();
  return all.find((m: any) => m.contentTypeAlias === alias) ?? null;
}

async function saveMapping(page: any, mapping: any) {
  const res = await page.request.post(`${API_BASE}/mappings`, { data: mapping });
  if (!res.ok()) throw new Error(`Failed to restore mapping for ${mapping.contentTypeAlias}: ${res.status()}`);
}

async function deleteMapping(page: any, alias: string) {
  const res = await page.request.delete(`${API_BASE}/mappings/${alias}`);
  if (![200, 204, 404].includes(res.status())) {
    throw new Error(`Failed to delete mapping for ${alias}: ${res.status()}`);
  }
}

async function shoot(page: any, name: string) {
  await page.waitForTimeout(500);
  await page.screenshot({ path: join(IMAGES_DIR, `${name}.png`), fullPage: false });
}

function resolveContentKeyByName(nodeName: RegExp): string {
  // Tree navigation is flaky under automation and the tree API needs its own
  // auth dance, so resolve the GUID from the seeded uSync content files and
  // deep-link into the workspace instead. Keys are stable in the demo DB.
  const contentDir = join(__dirname, '..', '..', '..', '..', '..', '..',
    'src', 'Umbraco.Community.SchemeWeaver.TestHost', 'uSync', 'v18', 'Content');
  for (const file of readdirSync(contentDir)) {
    if (!file.endsWith('.config')) continue;
    const head = readFileSync(join(contentDir, file), 'utf8').slice(0, 500);
    const match = head.match(/<Content Key="([0-9a-f-]+)" Alias="([^"]+)"/i);
    if (match && nodeName.test(match[2])) return match[1];
  }
  throw new Error(`No uSync content config matches ${nodeName}`);
}

async function goToContentJsonLdTab(umbracoUi: any, nodeName: RegExp) {
  const key = resolveContentKeyByName(nodeName);
  await umbracoUi.page.goto(`/umbraco/section/content/workspace/document/edit/${key}`);
  await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
  const tab = umbracoUi.page.getByRole('tab', { name: /JSON-LD/i });
  await tab.waitFor({ timeout: 15_000 });
  await tab.click();
  await umbracoUi.page.locator('schemeweaver-jsonld-content-view').waitFor({ timeout: 15_000 });
  await umbracoUi.page.waitForTimeout(1_500);
}

test.describe.serial('Docs screenshots', () => {
  let faqMappingBackup: any = null;

  test('backup seeded faqPage mapping', async ({ umbracoUi }) => {
    faqMappingBackup = await getMapping(umbracoUi.page, 'faqPage');
    expect(faqMappingBackup, 'faqPage mapping must be seeded in the TestHost').toBeTruthy();
  });

  test('schema-tab-empty', async ({ umbracoUi }) => {
    await deleteMapping(umbracoUi.page, 'faqPage');
    await goToDocTypeSchemaTab(umbracoUi, 'faqPage');
    await expect(umbracoUi.page.getByTestId('schemeweaver:map-to-schema')).toBeVisible({ timeout: 15_000 });
    await shoot(umbracoUi.page, 'schema-tab-empty');
  });

  test('schema-picker-common + schema-picker-search + ai-suggested-schema', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'faqPage');
    await umbracoUi.page.getByTestId('schemeweaver:map-to-schema').click();
    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await expect(pickerModal).toBeVisible({ timeout: 10_000 });
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await expect(pickerModal.locator('umb-ref-item').first()).toBeVisible({ timeout: 10_000 });
    await shoot(umbracoUi.page, 'schema-picker-common');

    // AI Suggested Schema box renders only when the AI package responds.
    const aiBox = pickerModal.locator('uui-box', { hasText: /AI Suggested Schema/i });
    if (await aiBox.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await shoot(umbracoUi.page, 'ai-suggested-schema');
    }

    await fillUuiInput(pickerModal.getByTestId('schemeweaver:schema-search'), 'Recipe');
    await expect(pickerModal.getByTestId('schemeweaver:schema-option:Recipe')).toBeVisible({ timeout: 10_000 });
    await shoot(umbracoUi.page, 'schema-picker-search');
    await umbracoUi.page.keyboard.press('Escape');

    // Restore the seeded mapping before the mapped-state shots.
    await saveMapping(umbracoUi.page, faqMappingBackup);
  });

  test('mapping-table + schema-type-box', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'recipePage');
    await expect(umbracoUi.page.getByTestId('schemeweaver:schema-type-badge')).toBeVisible({ timeout: 15_000 });
    await shoot(umbracoUi.page, 'schema-type-box');

    const table = umbracoUi.page.locator('schemeweaver-property-mapping-table');
    await table.scrollIntoViewIfNeeded();
    await umbracoUi.page.waitForTimeout(400);
    await shoot(umbracoUi.page, 'mapping-table');
  });

  test('source-origin-picker + related-content level', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'recipePage');
    const sourceChip = umbracoUi.page.locator('schemeweaver-property-mapping-table .source-chip').first();
    await expect(sourceChip).toBeVisible({ timeout: 10_000 });
    await sourceChip.click();
    const picker = umbracoUi.page.locator('schemeweaver-source-origin-picker-modal');
    await expect(picker).toBeVisible({ timeout: 10_000 });
    await shoot(umbracoUi.page, 'source-origin-picker');

    const related = picker.locator('umb-ref-item', { hasText: /Related Content/i }).first();
    if (await related.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await related.click();
      await umbracoUi.page.waitForTimeout(500);
      await shoot(umbracoUi.page, 'source-origin-related');
    }
    await umbracoUi.page.keyboard.press('Escape');
  });

  test('block-mapping-modal (faqPage mainEntity)', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'faqPage');
    const mapBlocksBtn = umbracoUi.page
      .locator('schemeweaver-schema-mapping-view')
      .getByTestId(/^schemeweaver:map-blocks:/)
      .first();
    await expect(mapBlocksBtn).toBeVisible({ timeout: 10_000 });
    await mapBlocksBtn.click();
    const nestedModal = umbracoUi.page.locator('schemeweaver-nested-mapping-modal');
    await expect(nestedModal).toBeVisible({ timeout: 10_000 });
    await umbracoUi.page.waitForTimeout(1_500);

    const expand = nestedModal.locator('.row-expand').first();
    if (await expand.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await expand.click();
      await umbracoUi.page.waitForTimeout(600);
    }
    await shoot(umbracoUi.page, 'block-mapping-modal');
    await umbracoUi.page.keyboard.press('Escape');
  });

  test('nested-block-routes (nestedBlocksPage)', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'nestedBlocksPage');
    const mapBlocksBtn = umbracoUi.page
      .locator('schemeweaver-schema-mapping-view')
      .getByTestId(/^schemeweaver:map-blocks:/)
      .first();
    if (!(await mapBlocksBtn.isVisible({ timeout: 5_000 }).catch(() => false))) {
      test.skip(true, 'nestedBlocksPage has no block mapping row');
      return;
    }
    await mapBlocksBtn.click();
    const nestedModal = umbracoUi.page.locator('schemeweaver-nested-mapping-modal');
    await expect(nestedModal).toBeVisible({ timeout: 10_000 });
    await umbracoUi.page.waitForTimeout(1_500);
    const expand = nestedModal.locator('.row-expand').first();
    if (await expand.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await expand.click();
      await umbracoUi.page.waitForTimeout(600);
    }
    await shoot(umbracoUi.page, 'nested-block-routes');
    await umbracoUi.page.keyboard.press('Escape');
  });

  test('picked-item-modes + complex-type-modal (blogArticle)', async ({ umbracoUi }) => {
    // Picker rows exist only where the seeded mapping uses a content picker;
    // try a few block-and-picker-rich types until one shows the modes select.
    let table: any = null;
    for (const alias of ['blogArticle', 'jobPostingPage', 'productPage', 'eventPage']) {
      await goToDocTypeSchemaTab(umbracoUi, alias);
      table = umbracoUi.page.locator('schemeweaver-property-mapping-table');
      await expect(table).toBeVisible({ timeout: 10_000 });
      const pickerBadge = table.locator('text=/^(Picker|Multi Picker)$/i').first();
      if (await pickerBadge.isVisible({ timeout: 2_000 }).catch(() => false)) break;
    }

    const pickedSelect = table.locator('uui-select, select').first();
    if (await pickedSelect.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await pickedSelect.scrollIntoViewIfNeeded();
      await shoot(umbracoUi.page, 'picked-item-modes');
    }

    const configureBtn = table.getByTestId(/^schemeweaver:configure-picked-object:/).first();
    const complexBtn = (await configureBtn.isVisible({ timeout: 3_000 }).catch(() => false))
      ? configureBtn
      : table.locator('uui-button', { hasText: /Configure/i }).first();
    if (await complexBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await complexBtn.click();
      const complexModal = umbracoUi.page.locator('schemeweaver-complex-type-mapping-modal');
      if (await complexModal.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await umbracoUi.page.waitForTimeout(1_000);
        await shoot(umbracoUi.page, 'complex-type-modal');
      }
      await umbracoUi.page.keyboard.press('Escape');
    }
  });

  test('entity-actions-menu + delete-mapping-confirm', async ({ umbracoUi }) => {
    await umbracoUi.page.goto('/umbraco/section/settings');
    const docTypesLink = umbracoUi.page.locator('a', { hasText: 'Document Types' }).first();
    await docTypesLink.waitFor({ timeout: 15_000 });
    await docTypesLink.click();
    await umbracoUi.page.waitForTimeout(1_000);
    const expandBtn = umbracoUi.page.locator('button[aria-label*="Expand"]').first();
    if (await expandBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await expandBtn.click();
      await umbracoUi.page.waitForTimeout(1_000);
    }
    const treeItem = umbracoUi.page.locator('umb-tree-item umb-tree-item', { hasText: /FAQ Page/i }).first();
    await treeItem.waitFor({ timeout: 15_000 });
    await treeItem.hover();
    let actionsBtn = treeItem.locator('button').filter({ hasText: /actions/i }).first();
    if (!(await actionsBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      actionsBtn = treeItem.getByLabel(/actions/i).first();
    }
    if (!(await actionsBtn.isVisible({ timeout: 5_000 }).catch(() => false))) {
      test.skip(true, 'Actions button not visible on FAQ Page tree item');
      return;
    }
    await actionsBtn.click();
    await expect(umbracoUi.page.locator('umb-entity-action').first()).toBeVisible({ timeout: 10_000 });
    await umbracoUi.page.waitForTimeout(500);
    await shoot(umbracoUi.page, 'entity-actions-menu');

    const deleteAction = umbracoUi.page
      .locator('umb-entity-action, uui-menu-item')
      .filter({ hasText: /Delete mapping/i })
      .first();
    if (await deleteAction.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await deleteAction.click();
      const dialog = umbracoUi.page.locator('uui-dialog, umb-confirm-modal').first();
      if (await dialog.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await shoot(umbracoUi.page, 'delete-mapping-confirm');
        const cancel = dialog.locator('uui-button', { hasText: /Cancel/i }).first();
        if (await cancel.isVisible({ timeout: 3_000 }).catch(() => false)) await cancel.click();
        else await umbracoUi.page.keyboard.press('Escape');
      }
    } else {
      await umbracoUi.page.keyboard.press('Escape');
    }
  });

  test('change-schema-type-confirm', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'faqPage');
    const changeBtn = umbracoUi.page.getByTestId('schemeweaver:change-schema-type');
    await expect(changeBtn).toBeVisible({ timeout: 10_000 });
    await changeBtn.click();

    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await expect(pickerModal).toBeVisible({ timeout: 10_000 });
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await fillUuiInput(pickerModal.getByTestId('schemeweaver:schema-search'), 'Article');
    const option = pickerModal.getByTestId('schemeweaver:schema-option:Article');
    await expect(option).toBeVisible({ timeout: 10_000 });
    await option.click();
    await pickerModal.getByTestId('schemeweaver:schema-picker-submit').click();

    const confirm = umbracoUi.page.locator('uui-dialog, umb-confirm-modal').filter({ hasText: /Change schema type/i }).first();
    if (await confirm.isVisible({ timeout: 8_000 }).catch(() => false)) {
      await shoot(umbracoUi.page, 'change-schema-type-confirm');
      const cancel = confirm.locator('uui-button', { hasText: /Cancel/i }).first();
      if (await cancel.isVisible({ timeout: 3_000 }).catch(() => false)) await cancel.click();
      else await umbracoUi.page.keyboard.press('Escape');
    } else {
      await umbracoUi.page.keyboard.press('Escape');
    }
    // Belt and braces: the seeded mapping must survive whatever happened above.
    await saveMapping(umbracoUi.page, faqMappingBackup);
  });

  test('generate-doctype-modal', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'faqPage');
    // Entity actions live behind the workspace's Actions dropdown.
    let workspaceAction = umbracoUi.page.locator('uui-button', { hasText: /Generate from Schema/i }).first();
    if (!(await workspaceAction.isVisible({ timeout: 3_000 }).catch(() => false))) {
      const actionsMenu = umbracoUi.page.getByRole('button', { name: /^Actions$/i }).first();
      if (await actionsMenu.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await actionsMenu.click();
        await umbracoUi.page.waitForTimeout(500);
        workspaceAction = umbracoUi.page
          .locator('umb-entity-action, uui-menu-item')
          .filter({ hasText: /Generate from Schema/i })
          .first();
      }
    }
    if (await workspaceAction.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await workspaceAction.click();
    } else {
      test.skip(true, 'Generate from Schema.org action not reachable from workspace');
      return;
    }
    const modal = umbracoUi.page.locator('schemeweaver-generate-doctype-modal');
    if (await modal.isVisible({ timeout: 8_000 }).catch(() => false)) {
      await umbracoUi.page.waitForTimeout(1_000);
      await shoot(umbracoUi.page, 'generate-doctype-modal');
    }
    await umbracoUi.page.keyboard.press('Escape');
  });

  test('jsonld-preview-tab (FAQs)', async ({ umbracoUi }) => {
    await goToContentJsonLdTab(umbracoUi, /^FAQs$/);
    await shoot(umbracoUi.page, 'jsonld-preview-tab');
  });

  test('validation-suggestion (block editor in property mode)', async ({ umbracoUi }) => {
    // Temporarily rewrite the faqPage mapping so mainEntity is a plain
    // property-mode row on the block editor: the PreferBlockContent advisory
    // then surfaces as a Suggestion in the JSON-LD tab's validation panel.
    const downgraded = JSON.parse(JSON.stringify(faqMappingBackup));
    const mainEntity = downgraded.propertyMappings?.find((p: any) => /mainEntity/i.test(p.schemaPropertyName));
    if (!mainEntity) {
      test.skip(true, 'faqPage mapping has no mainEntity row to downgrade');
      return;
    }
    mainEntity.sourceType = 'property';
    mainEntity.resolverConfig = null;
    mainEntity.nestedSchemaTypeName = null;
    await saveMapping(umbracoUi.page, downgraded);

    try {
      await goToContentJsonLdTab(umbracoUi, /^FAQs$/);
      const panel = umbracoUi.page.locator('schemeweaver-validation-panel');
      await expect(panel).toBeVisible({ timeout: 15_000 });
      const suggestion = panel.locator('text=/suggestion/i').first();
      await suggestion.waitFor({ timeout: 10_000 }).catch(() => {});
      await panel.scrollIntoViewIfNeeded();
      await shoot(umbracoUi.page, 'validation-suggestion');
    } finally {
      await saveMapping(umbracoUi.page, faqMappingBackup);
    }
  });

  test('language-variant-jsonld (de-DE)', async ({ umbracoUi }) => {
    await goToContentJsonLdTab(umbracoUi, /Test Variant Article/i);
    // Switch culture via the variant selector in the workspace header.
    const variantButton = umbracoUi.page.locator('umb-variant-selector uui-button, [data-mark="variant-selector"]').first();
    if (await variantButton.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await variantButton.click();
      const german = umbracoUi.page.locator('umb-variant-selector li, uui-menu-item', { hasText: /Deutsch|German|de-DE/i }).first();
      if (await german.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await german.click();
        await umbracoUi.page.waitForTimeout(2_000);
        const refresh = umbracoUi.page.locator('uui-button', { hasText: /Refresh/i }).first();
        if (await refresh.isVisible({ timeout: 3_000 }).catch(() => false)) {
          await refresh.click();
          await umbracoUi.page.waitForTimeout(1_500);
        }
      }
    }
    await shoot(umbracoUi.page, 'language-variant-jsonld');
  });

  test('usync-dashboard', async ({ umbracoUi }) => {
    await umbracoUi.page.goto('/umbraco/section/settings');
    let usyncLink = umbracoUi.page.locator('a', { hasText: /^uSync$/i }).first();
    if (!(await usyncLink.isVisible({ timeout: 5_000 }).catch(() => false))) {
      usyncLink = umbracoUi.page
        .locator('umb-menu-item, uui-menu-item, umb-tree-item')
        .filter({ hasText: /uSync/i })
        .first();
    }
    if (!(await usyncLink.isVisible({ timeout: 8_000 }).catch(() => false))) {
      test.skip(true, 'uSync tree item not found in Settings');
      return;
    }
    await usyncLink.click();
    await umbracoUi.page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await umbracoUi.page.waitForTimeout(2_000);
    await shoot(umbracoUi.page, 'usync-dashboard');
  });

  test('ai-bulk-analysis-modal', async ({ umbracoUi }) => {
    const statusRes = await umbracoUi.page.request.get(`${API_BASE}/ai/status`);
    if (!statusRes.ok()) {
      test.skip(true, 'AI package not installed');
      return;
    }
    await umbracoUi.page.goto('/umbraco/section/settings');
    const docTypesRoot = umbracoUi.page.locator('umb-tree-item').filter({ hasText: /Document Types/i }).first();
    await docTypesRoot.waitFor({ timeout: 15_000 });
    await docTypesRoot.hover();
    let actionsBtn = docTypesRoot.locator('button').filter({ hasText: /actions/i }).first();
    if (!(await actionsBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      actionsBtn = docTypesRoot.getByLabel(/actions/i).first();
    }
    if (!(await actionsBtn.isVisible({ timeout: 5_000 }).catch(() => false))) {
      test.skip(true, 'Actions button not available on Document Types root');
      return;
    }
    await actionsBtn.click();
    const analyseAll = umbracoUi.page.locator('umb-entity-action', { hasText: /AI Analyse All/i }).first();
    if (!(await analyseAll.isVisible({ timeout: 5_000 }).catch(() => false))) {
      test.skip(true, 'AI Analyse All action not present');
      return;
    }
    await analyseAll.click();
    const modal = umbracoUi.page.locator('schemeweaver-ai-bulk-analysis-modal, [class*="bulk-analysis"]').first();
    if (await modal.isVisible({ timeout: 10_000 }).catch(() => false)) {
      // Results can take a while; capture whichever state is reached in 30s.
      await umbracoUi.page
        .locator('uui-table-row', { hasText: /%|High|Medium|Low/i })
        .first()
        .waitFor({ timeout: 30_000 })
        .catch(() => {});
      await umbracoUi.page.waitForTimeout(1_000);
      await shoot(umbracoUi.page, 'ai-bulk-analysis-modal');
    }
    await umbracoUi.page.keyboard.press('Escape');
  });

  test('restore seeded faqPage mapping', async ({ umbracoUi }) => {
    await saveMapping(umbracoUi.page, faqMappingBackup);
    const restored = await getMapping(umbracoUi.page, 'faqPage');
    expect(restored?.schemaTypeName).toBe(faqMappingBackup.schemaTypeName);
  });
});
