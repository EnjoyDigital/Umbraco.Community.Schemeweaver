import { expect, type Page } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * Close-the-loop proof for the scoped "Map blocks" modal (block-mapping team
 * fix): the parent property-mapping row's Schema Property is the single source
 * of truth, legacy wildcard configs pre-fill correctly, a no-change open+save
 * is a byte-identical persistence no-op, and the rendered JSON-LD emits the
 * routed Review objects end to end.
 *
 * Selector policy: ONLY the agreed data-mark hooks (testIdAttribute is
 * 'data-mark' per playwright.config.ts) + visible text, plus the stable
 * schemeweaver-* element names already used across this suite:
 *   - table button   data-mark="schemeweaver:map-blocks:{schemaPropertyName}"
 *   - row summary    data-mark="schemeweaver:block-summary:{schemaPropertyName}"
 *   - modal block row data-mark="schemeweaver:block-row:{alias}"
 *   - modal save     data-mark="schemeweaver:block-modal-save"
 *   - auto-map all   data-mark="schemeweaver:block-automap-all"
 *
 * Environment safety: these tests exercise the REAL seeded productPage
 * mapping. Every test snapshots the live mapping first and restores it by
 * POSTing the identical snapshot back in `finally`, so the environment leaves
 * exactly as it was found. Because the live DB can drift from the uSync seed
 * (previous sessions save through the same API), each test also ARRANGES the
 * canonical seeded shape (quoted verbatim from
 * uSync/v18/SchemeWeaverMappings/productPage.config) before acting — the
 * legacy wildcard shape is the precondition these tests exist to exercise.
 *
 * Serial on purpose: all three tests mutate/restore the same productPage
 * mapping, and the rendered-JSON-LD assertions read pages whose output depends
 * on that mapping. Parallel workers would race each other's arrange/restore.
 */
test.describe.configure({ mode: 'serial' });

const BASE = '/umbraco/management/api/v1/schemeweaver';
const PAGE_ALIAS = 'productPage';
const REVIEWS_PROPERTY = 'reviews';

// Content GUIDs from the committed uSync content seed (stable fixture keys):
//   uSync/v18/Content/wireless-headphones-pro.config — TWO populated reviewItem blocks
//   uSync/v18/Content/c64-syntax-watch.config       — EMPTY reviews Block List
const WIRELESS_HEADPHONES_KEY = '6478be49-015e-4628-9209-2feac2b7d79b';
const C64_WATCH_KEY = '697a075d-86b9-42fb-ae5d-d52c7cd6e4ab';

/**
 * The seeded legacy WILDCARD resolverConfig — flat `nestedMappings`, NO
 * blockAlias — verbatim from uSync/v18/SchemeWeaverMappings/productPage.config.
 * This exact shape used to be invisible in the old modal (the wildcard-keyed-
 * by-'' bug) and must now pre-fill the scoped panel correctly.
 */
const SEEDED_LEGACY_RESOLVER_CONFIG =
  '{"nestedMappings":[{"schemaProperty":"Author","contentProperty":"reviewAuthor","wrapInType":"Person","wrapInProperty":"Name"},{"schemaProperty":"ReviewRating","contentProperty":"ratingValue"},{"schemaProperty":"ReviewBody","contentProperty":"reviewBody"},{"schemaProperty":"DatePublished","contentProperty":"reviewDate"}]}';

function seededRow(overrides: Record<string, unknown>) {
  return {
    sourceType: 'property',
    contentTypePropertyAlias: null,
    sourceContentTypeAlias: null,
    transformType: null,
    isAutoMapped: false,
    staticValue: null,
    nestedSchemaTypeName: null,
    resolverConfig: null,
    dynamicRootConfig: null,
    targetPieceKey: null,
    ...overrides,
  };
}

/** The canonical seeded productPage mapping, mirrored from the uSync seed. */
function seededProductMapping(contentTypeKey: string) {
  return {
    contentTypeAlias: PAGE_ALIAS,
    contentTypeKey,
    schemaTypeName: 'Product',
    isEnabled: true,
    isInherited: false,
    idOverride: null,
    propertyMappings: [
      seededRow({ schemaPropertyName: 'Name', contentTypePropertyAlias: 'title' }),
      seededRow({ schemaPropertyName: 'Description', contentTypePropertyAlias: 'description' }),
      seededRow({ schemaPropertyName: 'Sku', contentTypePropertyAlias: 'sku' }),
      seededRow({ schemaPropertyName: 'Brand', contentTypePropertyAlias: 'brand' }),
      seededRow({ schemaPropertyName: 'Image', contentTypePropertyAlias: 'productImage' }),
      seededRow({
        schemaPropertyName: 'Review',
        sourceType: 'blockContent',
        contentTypePropertyAlias: REVIEWS_PROPERTY,
        nestedSchemaTypeName: 'Review',
        resolverConfig: SEEDED_LEGACY_RESOLVER_CONFIG,
      }),
      seededRow({
        schemaPropertyName: 'Category',
        sourceType: 'parent',
        contentTypePropertyAlias: 'title',
        sourceContentTypeAlias: 'productListing',
      }),
    ],
  };
}

/** Resolve the content-type GUID key for a doc-type alias via the management API. */
async function contentTypeKeyFor(umbracoUi: any, alias: string): Promise<string> {
  const res = await umbracoUi.page.request.get(`${BASE}/content-types`);
  expect(res.ok(), `content-types GET failed: ${res.status()}`).toBeTruthy();
  const body = await res.json();
  const items: Array<{ alias: string; key: string }> = Array.isArray(body) ? body : body.items ?? [];
  const match = items.find((c) => c.alias === alias);
  expect(match, `content type '${alias}' not found — is the TestHost seeded?`).toBeTruthy();
  return match!.key;
}

/** GET the live productPage mapping (the snapshot used for restore). */
async function getMapping(umbracoUi: any): Promise<any> {
  const res = await umbracoUi.page.request.get(`${BASE}/mappings/${PAGE_ALIAS}`);
  expect(res.ok(), `GET mappings/${PAGE_ALIAS} failed: ${res.status()}`).toBeTruthy();
  return res.json();
}

/** POST a mapping payload back (used for both arrange and restore). */
async function postMapping(umbracoUi: any, mapping: any): Promise<void> {
  const res = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: mapping });
  expect(res.ok(), `POST mappings failed: ${res.status()}`).toBeTruthy();
}

/**
 * Arrange: put the canonical seeded legacy mapping in place and return the
 * SERVER's canonical form of it (what GET returns after the POST) — byte
 * comparisons must run against what the server persists, not our payload.
 */
async function arrangeSeededMapping(umbracoUi: any): Promise<any> {
  const contentTypeKey = await contentTypeKeyFor(umbracoUi, PAGE_ALIAS);
  await postMapping(umbracoUi, seededProductMapping(contentTypeKey));
  return getMapping(umbracoUi);
}

function blockRowsFor(mapping: any, propertyAlias: string): any[] {
  return (mapping.propertyMappings ?? []).filter(
    (m: any) => m.sourceType === 'blockContent' && m.contentTypePropertyAlias === propertyAlias,
  );
}

/**
 * Navigate to a document type's Schema.org tab (same pattern as
 * schemeweaver.spec.ts — direct workspace URL, no tree walking).
 */
async function goToDocTypeSchemaTab(umbracoUi: any, docTypeName: string) {
  await umbracoUi.goToBackOffice();

  const res = await umbracoUi.page.request.get(`${BASE}/content-types`);
  expect(res.ok(), `content-types GET failed: ${res.status()}`).toBeTruthy();
  const contentTypes = await res.json();
  const docType = contentTypes.find((ct: any) => ct.name === docTypeName);
  expect(docType, `Document type "${docTypeName}" not found`).toBeTruthy();

  await umbracoUi.page.goto(`/umbraco/section/settings/workspace/document-type/edit/${docType.key}`);

  const schemaTab = umbracoUi.page.getByRole('tab', { name: /Schema\.org/i });
  await schemaTab.waitFor({ timeout: 15_000 });
  await schemaTab.click();

  await umbracoUi.page.locator('schemeweaver-schema-mapping-view').waitFor({ timeout: 15_000 });
}

/** All JSON-LD nodes on the current page, graph-aware (same as schemeweaver.spec.ts). */
async function collectJsonLdNodes(page: Page): Promise<any[]> {
  const scripts = page.locator('script[type="application/ld+json"]');
  const count = await scripts.count();
  const nodes: any[] = [];
  for (let i = 0; i < count; i++) {
    const text = await scripts.nth(i).textContent();
    if (!text) continue;
    const doc = JSON.parse(text);
    nodes.push(...(Array.isArray(doc['@graph']) ? doc['@graph'] : [doc]));
  }
  return nodes;
}

/** Discover a content node's real front-end URL from its GUID via the Delivery API. */
async function routePathForContent(umbracoUi: any, contentKey: string): Promise<string> {
  const res = await umbracoUi.page.request.get(`/umbraco/delivery/api/v2/content/item/${contentKey}`);
  expect(res.ok(), `Delivery API item lookup failed for ${contentKey}: ${res.status()}`).toBeTruthy();
  const item = await res.json();
  expect(item.route?.path, `no route path for content ${contentKey}`).toBeTruthy();
  return item.route.path as string;
}

test.describe('Review block mapping — scoped modal E2E', () => {
  test('scoped modal pre-fills the seeded legacy wildcard config and persists a routed config through workspace save', async ({ umbracoUi }) => {
    const snapshot = await getMapping(umbracoUi);

    try {
      const baseline = await arrangeSeededMapping(umbracoUi);
      const baselineOtherRows = (baseline.propertyMappings as any[])
        .filter((m) => !(m.sourceType === 'blockContent' && m.contentTypePropertyAlias === REVIEWS_PROPERTY))
        .map((m) => m.schemaPropertyName);

      await goToDocTypeSchemaTab(umbracoUi, 'Product Page');
      const page = umbracoUi.page;
      const mappingView = page.locator('schemeweaver-schema-mapping-view');

      // ── Parent table: the Review row sources Block Content from `reviews`,
      // and carries the renamed "Map blocks" button (agreed hook).
      const mapBlocksBtn = page.getByTestId('schemeweaver:map-blocks:Review');
      await expect(mapBlocksBtn).toBeVisible({ timeout: 15_000 });

      const reviewRow = mappingView.locator('uui-table-row, tr').filter({ has: mapBlocksBtn });
      await expect(reviewRow).toContainText(/Block Content/i);
      await expect(reviewRow).toContainText(REVIEWS_PROPERTY);

      // ── Open the scoped modal.
      await mapBlocksBtn.click();
      const modal = page.locator('schemeweaver-nested-mapping-modal');
      await expect(modal).toBeVisible({ timeout: 10_000 });

      // Headline names the parent row's Schema Property — the single source of
      // truth for where block output lands.
      await expect(modal.getByText(/Map blocks to\s+Review/i).first()).toBeVisible();

      // The reviewItem block row is PRE-SEEDED as mapped from the legacy
      // wildcard config (the wildcard-keyed-by-'' fix), routed to nested type
      // 'Review'. The narrow accepted range renders the type as a constrained
      // select, so assert its VALUE — option text always reports hidden.
      const blockRow = modal.getByTestId('schemeweaver:block-row:reviewItem');
      await expect(blockRow).toBeVisible({ timeout: 10_000 });
      await expect(blockRow.locator('select').first()).toHaveValue('Review');

      // Expanded (dedicated expand button, not the row itself), the legacy
      // nestedMappings pre-fill the property table: schema-property labels are
      // text cells, while the chosen content properties are SELECT VALUES.
      await blockRow.locator('.row-expand').click();
      await expect(blockRow.getByText('Author', { exact: true }).first()).toBeVisible({ timeout: 10_000 });
      await expect(async () => {
        const values = await blockRow
          .locator('select')
          .evaluateAll((els) => els.map((el) => (el as HTMLSelectElement).value));
        expect(values).toContain('reviewAuthor');
        expect(values).toContain('reviewBody');
      }).toPass({ timeout: 10_000 });

      // The per-block target-property dropdown is GONE by design: the target
      // is fixed context from the parent row, never chosen inside the modal.
      await expect(modal.getByRole('combobox', { name: /target/i })).toHaveCount(0);
      await expect(modal.getByLabel(/target propert/i)).toHaveCount(0);

      // ── Make an explicit edit before saving. By contract a no-change save
      // returns the legacy config VERBATIM (byte fidelity — see the next
      // test), so we opt into the auto-map affordance to exercise the
      // legacy → routes upgrade path.
      await modal.getByTestId('schemeweaver:block-automap-all').click();

      await modal.getByTestId('schemeweaver:block-modal-save').click();
      await expect(modal).toBeHidden({ timeout: 10_000 });

      // Row summary tags replace the removed "Nested Schema Type" input.
      await expect(page.getByTestId('schemeweaver:block-summary:Review')).toBeVisible({ timeout: 10_000 });

      // ── Persist through the document-type workspace Save.
      await umbracoUi.documentType.clickSaveButton();

      // Poll the management API until the routed shape lands (save wiring is
      // asynchronous; the API is the source of truth we ultimately assert on).
      await expect
        .poll(
          async () => {
            const mapping = await getMapping(umbracoUi);
            const rows = blockRowsFor(mapping, REVIEWS_PROPERTY);
            if (rows.length !== 1 || !rows[0].resolverConfig) return 'no single blockContent row yet';
            try {
              return Array.isArray(JSON.parse(rows[0].resolverConfig).routes) ? 'routes' : 'legacy';
            } catch {
              return 'unparsable resolverConfig';
            }
          },
          { timeout: 15_000, message: 'workspace save should persist a routes-shaped resolverConfig' },
        )
        .toBe('routes');

      // ── Full persisted-state assertions.
      const persisted = await getMapping(umbracoUi);
      const blockRows = blockRowsFor(persisted, REVIEWS_PROPERTY);
      expect(blockRows, `expected exactly ONE blockContent row for '${REVIEWS_PROPERTY}'`).toHaveLength(1);

      const persistedRow = blockRows[0];
      expect(persistedRow.schemaPropertyName.toLowerCase()).toBe('review');

      const config = JSON.parse(persistedRow.resolverConfig);
      expect(Array.isArray(config.routes), 'resolverConfig must be routes-shaped').toBeTruthy();
      expect(config.routes).toHaveLength(1);
      expect(config.routes[0].blockAlias).toBe('reviewItem');
      expect(config.routes[0].nestedSchemaType).toBe('Review');

      const routedProps = (config.routes[0].propertyMappings ?? []).map((p: any) =>
        String(p.schemaProperty).toLowerCase(),
      );
      expect(routedProps).toContain('author');
      expect(routedProps).toContain('reviewbody');

      // Every other row survived untouched (same count, same properties).
      const persistedOtherRows = (persisted.propertyMappings as any[])
        .filter((m) => !(m.sourceType === 'blockContent' && m.contentTypePropertyAlias === REVIEWS_PROPERTY))
        .map((m) => m.schemaPropertyName);
      expect(persistedOtherRows.sort()).toEqual([...baselineOtherRows].sort());
      expect(persisted.propertyMappings).toHaveLength(baseline.propertyMappings.length);
    } finally {
      await postMapping(umbracoUi, snapshot);
    }
  });

  test('no-change open + save is a byte-identical persistence no-op (legacy config NOT converted)', async ({ umbracoUi }) => {
    const snapshot = await getMapping(umbracoUi);

    try {
      const baseline = await arrangeSeededMapping(umbracoUi);
      const baselineRow = blockRowsFor(baseline, REVIEWS_PROPERTY)[0];
      expect(baselineRow, 'arranged mapping must carry the seeded blockContent row').toBeTruthy();
      const baselineOrder = (baseline.propertyMappings as any[]).map((m) => m.schemaPropertyName);

      await goToDocTypeSchemaTab(umbracoUi, 'Product Page');
      const page = umbracoUi.page;

      // Open the scoped modal and save WITHOUT touching anything.
      await page.getByTestId('schemeweaver:map-blocks:Review').click();
      const modal = page.locator('schemeweaver-nested-mapping-modal');
      await expect(modal).toBeVisible({ timeout: 10_000 });
      await expect(modal.getByTestId('schemeweaver:block-row:reviewItem')).toBeVisible({ timeout: 10_000 });

      await modal.getByTestId('schemeweaver:block-modal-save').click();
      await expect(modal).toBeHidden({ timeout: 10_000 });

      // Save the document type and wait for the action to settle.
      await umbracoUi.documentType.clickSaveButton();
      await umbracoUi.documentType.isSuccessStateVisibleForSaveButton();

      // The stored row must be BYTE-IDENTICAL to the baseline: same legacy
      // resolverConfig string (no legacy → routes conversion on a no-change
      // save), same nestedSchemaTypeName, same isAutoMapped, same row order.
      const persisted = await getMapping(umbracoUi);
      const persistedRows = blockRowsFor(persisted, REVIEWS_PROPERTY);
      expect(persistedRows).toHaveLength(1);

      expect(persistedRows[0].resolverConfig).toBe(baselineRow.resolverConfig);
      expect(persistedRows[0].nestedSchemaTypeName).toBe(baselineRow.nestedSchemaTypeName);
      expect(persistedRows[0].isAutoMapped).toBe(baselineRow.isAutoMapped);

      const persistedOrder = (persisted.propertyMappings as any[]).map((m) => m.schemaPropertyName);
      expect(persistedOrder).toEqual(baselineOrder);
    } finally {
      await postMapping(umbracoUi, snapshot);
    }
  });

  test('rendered JSON-LD: populated reviews emit 2 Review objects; empty list emits no review property', async ({ umbracoUi }) => {
    const snapshot = await getMapping(umbracoUi);

    try {
      // Arrange the seeded legacy wildcard mapping — the render path resolves
      // it on the live page (unchanged by the modal rewrite).
      await arrangeSeededMapping(umbracoUi);
      const page = umbracoUi.page;

      // ── wireless-headphones-pro: TWO populated reviewItem blocks.
      const wirelessPath = await routePathForContent(umbracoUi, WIRELESS_HEADPHONES_KEY);
      await page.goto(wirelessPath, { waitUntil: 'domcontentloaded' });

      const wirelessNodes = await collectJsonLdNodes(page);
      const product = wirelessNodes.find((n) => n['@type'] === 'Product');
      expect(product, 'wireless-headphones-pro must emit a Product node').toBeTruthy();

      expect(Array.isArray(product.review), 'Product.review must be an array').toBeTruthy();
      expect(product.review).toHaveLength(2);
      for (const review of product.review) {
        expect(review['@type']).toBe('Review');
        expect(review.author, 'review.author missing').toBeTruthy();
        expect(review.reviewBody, 'review.reviewBody missing').toBeTruthy();
        expect(review.reviewRating, 'review.reviewRating missing').toBeTruthy();
      }

      // ── c64-syntax-watch: EMPTY reviews Block List → the Product node must
      // NOT carry an empty review property.
      const c64Path = await routePathForContent(umbracoUi, C64_WATCH_KEY);
      await page.goto(c64Path, { waitUntil: 'domcontentloaded' });

      const c64Nodes = await collectJsonLdNodes(page);
      const c64Product = c64Nodes.find((n) => n['@type'] === 'Product');
      expect(c64Product, 'c64-syntax-watch must emit a Product node').toBeTruthy();
      expect(c64Product.review, 'empty Block List must not emit a review property').toBeUndefined();
    } finally {
      await postMapping(umbracoUi, snapshot);
    }
  });
});
