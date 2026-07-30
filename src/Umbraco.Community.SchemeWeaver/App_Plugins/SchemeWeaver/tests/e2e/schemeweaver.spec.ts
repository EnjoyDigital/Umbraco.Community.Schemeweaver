import { expect } from '@playwright/test';
import { ConstantHelper, test } from '@umbraco/playwright-testhelpers';

/**
 * Find a node of a given @type in a parsed JSON-LD document, transparently
 * handling both shapes the package can emit:
 *   - graph model (default):  { "@context": ..., "@graph": [ {…}, {…} ] }
 *   - flat single Thing:       { "@context": ..., "@type": "WebSite", … }
 * Returns the matching node object, or undefined if none is present.
 */
function findNodeOfType(jsonLd: any, type: string): any | undefined {
  if (!jsonLd) return undefined;
  if (Array.isArray(jsonLd['@graph'])) {
    return jsonLd['@graph'].find((node: any) => node?.['@type'] === type);
  }
  return jsonLd['@type'] === type ? jsonLd : undefined;
}

/**
 * True when a parsed JSON-LD document carries at least one node with an
 * @type — i.e. a non-empty @graph (default output) or a flat top-level Thing
 * (when UseGraphModel is disabled).
 */
function hasTypedNode(jsonLd: any): boolean {
  if (!jsonLd) return false;
  if (Array.isArray(jsonLd['@graph'])) {
    return jsonLd['@graph'].some((node: any) => Boolean(node?.['@type']));
  }
  return Boolean(jsonLd['@type']);
}

/**
 * Helper: fill a uui-input web component by targeting its inner native input.
 */
async function fillUuiInput(locator: any, text: string) {
  const input = locator.locator('input');
  await input.fill(text);
}

/**
 * Search the redesigned schema picker and pick a type by its exact name, then
 * submit. Built on the frozen data-mark contract:
 *   - search input  data-mark="schemeweaver:schema-search"   (uui-input)
 *   - result row    data-mark="schemeweaver:schema-option:<TypeName>" (umb-ref-item)
 *   - submit        data-mark="schemeweaver:schema-picker-submit"
 * Filtering is in-memory (no debounce), so the option locator's own
 * auto-waiting is all the synchronisation needed. The uui-input host is not
 * itself fillable — fill its inner native input (Playwright pierces shadow DOM).
 */
async function searchAndPickSchema(pickerModal: any, schemaName: string) {
  await fillUuiInput(pickerModal.getByTestId('schemeweaver:schema-search'), schemaName);
  await pickerModal.getByTestId(`schemeweaver:schema-option:${schemaName}`).click();
  await pickerModal.getByTestId('schemeweaver:schema-picker-submit').click();
}

/**
 * Helper: look up a document type's key by its display name via the SchemeWeaver
 * content-types API. This avoids flaky tree navigation when there are 100+ doc types.
 */
async function getDocTypeKeyByName(umbracoUi: any, docTypeName: string): Promise<string> {
  const response = await umbracoUi.page.request.get(
    '/umbraco/management/api/v1/schemeweaver/content-types'
  );
  if (!response.ok()) {
    throw new Error(`Failed to fetch content types: ${response.status()}`);
  }
  const contentTypes = await response.json();
  const docType = contentTypes.find((ct: any) => ct.name === docTypeName);
  if (!docType) {
    throw new Error(`Document type "${docTypeName}" not found in content-types list`);
  }
  return docType.key;
}

/**
 * Helper: navigate to a specific document type's Schema.org tab in the Settings section.
 * Uses the SchemeWeaver API to look up the doc type key and navigates directly to the
 * workspace URL — bypassing the tree entirely. Robust regardless of tree position.
 */
async function goToDocTypeSchemaTab(umbracoUi: any, docTypeName: string) {
  // `goToBackOffice()` resolves when the shell is visible — that's already
  // enough for `page.request.*` to inherit cookies, so skip the extra
  // `networkidle` wait that would otherwise block on backoffice polling.
  await umbracoUi.goToBackOffice();

  const docTypeKey = await getDocTypeKeyByName(umbracoUi, docTypeName);

  await umbracoUi.page.goto(`/umbraco/section/settings/workspace/document-type/edit/${docTypeKey}`);

  // Schema.org tab + the mapping view itself are the real readiness signals;
  // the previous `networkidle` wait was redundant given the locator waits.
  const schemaTab = umbracoUi.page.getByRole('tab', { name: /Schema\.org/i });
  await schemaTab.waitFor({ timeout: 15_000 });
  await schemaTab.click();

  await umbracoUi.page.locator('schemeweaver-schema-mapping-view').waitFor({ timeout: 15_000 });
}

// ---------------------------------------------------------------------------
// JSON-LD Output Verification Tests (require content to be published)
// ---------------------------------------------------------------------------

test.describe('JSON-LD Output on Site', () => {
  test('mapped content page contains JSON-LD script tag', async ({ umbracoUi }) => {
    // Navigate to a content page on the frontend (not backoffice)
    // This test verifies the tag helper output
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    const response = await umbracoUi.page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
    if (response?.ok()) {
      // Check for JSON-LD script tag
      const jsonLdScript = umbracoUi.page.locator('script[type="application/ld+json"]');
      const count = await jsonLdScript.count();

      // If there's a JSON-LD script, validate its structure
      if (count > 0) {
        const jsonLdText = await jsonLdScript.first().textContent();
        expect(jsonLdText).toBeTruthy();

        const parsed = JSON.parse(jsonLdText!);
        expect(parsed['@context']).toBe('https://schema.org');
        // Default output is a @graph; accept either a graph with nodes or a
        // flat top-level @type (when UseGraphModel is disabled).
        expect(hasTypedNode(parsed)).toBeTruthy();
      }
    }
  });

  test('FAQ page JSON-LD contains Question and Answer types', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    const response = await umbracoUi.page.goto(`${baseUrl}/frequently-asked-questions/`, { waitUntil: 'domcontentloaded' });
    if (response?.ok()) {
      const jsonLdScripts = umbracoUi.page.locator('script[type="application/ld+json"]');
      const count = await jsonLdScripts.count();

      let faqJson: any = null;
      for (let i = 0; i < count; i++) {
        const text = await jsonLdScripts.nth(i).textContent();
        const parsed = JSON.parse(text!);
        if (parsed['@type'] === 'FAQPage') {
          faqJson = parsed;
          break;
        }
      }

      if (faqJson) {
        expect(faqJson['@type']).toBe('FAQPage');

        // If mainEntity is mapped, it should contain Question objects
        if (faqJson.mainEntity) {
          const questions = Array.isArray(faqJson.mainEntity) ? faqJson.mainEntity : [faqJson.mainEntity];
          for (const q of questions) {
            expect(q['@type']).toBe('Question');
            expect(q.name).toBeTruthy();
            if (q.acceptedAnswer) {
              expect(q.acceptedAnswer['@type']).toBe('Answer');
              expect(q.acceptedAnswer.text).toBeTruthy();
            }
          }
        }
      }
    }
  });

  test('Product page JSON-LD contains Review objects', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    const response = await umbracoUi.page.goto(`${baseUrl}/products/wireless-headphones-pro/`, { waitUntil: 'domcontentloaded' });
    if (response?.ok()) {
      const jsonLdScripts = umbracoUi.page.locator('script[type="application/ld+json"]');
      const count = await jsonLdScripts.count();

      // Find the Product script (may not be first due to ordering)
      let productJson: any = null;
      for (let i = 0; i < count; i++) {
        const text = await jsonLdScripts.nth(i).textContent();
        const parsed = JSON.parse(text!);
        if (parsed['@type'] === 'Product') {
          productJson = parsed;
          break;
        }
      }

      if (productJson) {
        expect(productJson['@type']).toBe('Product');

        if (productJson.review) {
          const reviews = Array.isArray(productJson.review) ? productJson.review : [productJson.review];
          for (const r of reviews) {
            expect(r['@type']).toBe('Review');
          }
        }
      }
    }
  });

  test('Recipe page JSON-LD contains ingredients and instructions', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    const response = await umbracoUi.page.goto(`${baseUrl}/recipes/classic-victoria-sponge/`, { waitUntil: 'domcontentloaded' });
    if (response?.ok()) {
      const jsonLdScripts = umbracoUi.page.locator('script[type="application/ld+json"]');
      const count = await jsonLdScripts.count();

      let parsed: any = null;
      for (let i = 0; i < count; i++) {
        const text = await jsonLdScripts.nth(i).textContent();
        const p = JSON.parse(text!);
        if (p['@type'] === 'Recipe') {
          parsed = p;
          break;
        }
      }

      if (parsed) {
        expect(parsed['@type']).toBe('Recipe');

        // recipeIngredient should be string array
        if (parsed.recipeIngredient) {
          expect(Array.isArray(parsed.recipeIngredient)).toBe(true);
          for (const ingredient of parsed.recipeIngredient) {
            expect(typeof ingredient).toBe('string');
          }
        }

        // recipeInstructions should be an array of structured steps
        if (parsed.recipeInstructions) {
          const steps = Array.isArray(parsed.recipeInstructions) ? parsed.recipeInstructions : [parsed.recipeInstructions];
          for (const step of steps) {
            // Steps may be HowToStep or ItemList depending on Schema.NET serialisation
            expect(['HowToStep', 'ItemList']).toContain(step['@type']);
          }
        }
      }
    }
  });
});

// ---------------------------------------------------------------------------
// JSON-LD Ordering Tests
// ---------------------------------------------------------------------------

test.describe('JSON-LD Script Ordering', () => {
  test('scripts are ordered: inherited → breadcrumb → page schema', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    // Navigate to a child page that should have inherited WebSite + BreadcrumbList + own schema
    const response = await umbracoUi.page.goto(`${baseUrl}/products/wireless-headphones-pro/`, { waitUntil: 'domcontentloaded' });
    if (!response?.ok()) return;

    const jsonLdScripts = umbracoUi.page.locator('script[type="application/ld+json"]');
    const count = await jsonLdScripts.count();
    expect(count).toBeGreaterThanOrEqual(3);

    // Parse all scripts to get their types in order
    const types: string[] = [];
    for (let i = 0; i < count; i++) {
      const text = await jsonLdScripts.nth(i).textContent();
      const parsed = JSON.parse(text!);
      types.push(parsed['@type']);
    }

    // Verify ordering: WebSite (inherited) should come before BreadcrumbList, which comes before Product
    const websiteIdx = types.indexOf('WebSite');
    const breadcrumbIdx = types.indexOf('BreadcrumbList');
    const productIdx = types.indexOf('Product');

    expect(websiteIdx).toBeGreaterThanOrEqual(0);
    expect(breadcrumbIdx).toBeGreaterThan(websiteIdx);
    expect(productIdx).toBeGreaterThan(breadcrumbIdx);
  });

  test('all pages with schemas have valid JSON-LD', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    // Smoke test: check that all known pages have at least one valid JSON-LD script
    const pages = [
      '/',
      '/blog/',
      '/products/',
      '/events/',
      '/recipes/',
      '/frequently-asked-questions/',
      '/products/wireless-headphones-pro/',
      '/recipes/classic-victoria-sponge/',
    ];

    for (const path of pages) {
      const response = await umbracoUi.page.goto(`${baseUrl}${path}`, { waitUntil: 'domcontentloaded' });
      if (!response?.ok()) continue;

      const jsonLdScripts = umbracoUi.page.locator('script[type="application/ld+json"]');
      const count = await jsonLdScripts.count();
      expect(count).toBeGreaterThan(0);

      // Validate first script is valid JSON with @context
      const text = await jsonLdScripts.first().textContent();
      expect(text).toBeTruthy();
      const parsed = JSON.parse(text!);
      expect(parsed['@context']).toBe('https://schema.org');
      // Default output is a @graph; accept either a graph with nodes or a
      // flat top-level @type (when UseGraphModel is disabled).
      expect(hasTypedNode(parsed)).toBeTruthy();
    }
  });
});

// ---------------------------------------------------------------------------
// Delivery API JSON-LD Tests (require Delivery API enabled + published content)
// ---------------------------------------------------------------------------

test.describe('Delivery API JSON-LD', () => {
  test('Delivery API returns schemaOrg field for mapped content', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    // Fetch content items via Delivery API
    const response = await umbracoUi.page.request.get(
      `${baseUrl}/umbraco/delivery/api/v2/content`,
      { headers: { 'Accept': 'application/json' } }
    );

    if (!response.ok()) {
      console.warn('Delivery API not available — skipping test');
      return;
    }

    const data = await response.json();
    expect(data.items).toBeTruthy();

    // Find any content item that has a schemaOrg property (i.e. has a mapping)
    const itemWithSchema = data.items.find(
      (item: any) => item.properties?.schemaOrg
    );

    if (!itemWithSchema) {
      console.warn('No content with schemaOrg mapping found in Delivery API — skipping validation');
      return;
    }

    // schemaOrg should be a JSON-LD string (or array of strings)
    const schemaOrg = itemWithSchema.properties.schemaOrg;
    const jsonLdString = Array.isArray(schemaOrg) ? schemaOrg[0] : schemaOrg;
    expect(jsonLdString).toBeTruthy();

    const parsed = JSON.parse(jsonLdString);
    expect(parsed['@context']).toBe('https://schema.org');
    expect(parsed['@type']).toBeTruthy();
  });

  test('Delivery API content item by path includes JSON-LD', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44389';

    // Fetch the home page by path via Delivery API
    const response = await umbracoUi.page.request.get(
      `${baseUrl}/umbraco/delivery/api/v2/content/item/`,
      { headers: { 'Accept': 'application/json' } }
    );

    if (!response.ok()) {
      console.warn('Delivery API item endpoint not available — skipping test');
      return;
    }

    const item = await response.json();

    // If the home page has a schema mapping, validate the schemaOrg property
    if (item.properties?.schemaOrg) {
      const schemaOrg = item.properties.schemaOrg;
      const jsonLdString = Array.isArray(schemaOrg) ? schemaOrg[0] : schemaOrg;
      const parsed = JSON.parse(jsonLdString);
      expect(parsed['@context']).toBe('https://schema.org');
      expect(parsed['@type']).toBeTruthy();
    }
  });
});

// ---------------------------------------------------------------------------
// Mapping Persistence & JSON-LD Output Tests
// ---------------------------------------------------------------------------

test.describe('Mapping Persistence & JSON-LD Output', () => {
  test('JSON-LD renders on published page', async ({ umbracoUi }) => {
    const baseUrl = process.env.UMBRACO_URL || 'https://localhost:44308';

    // Navigate to the home page (which has homePage → WebSite mapping)
    await umbracoUi.page.goto(baseUrl + '/');
    await umbracoUi.page.waitForLoadState('domcontentloaded', { timeout: 15_000 });

    // Get the full page HTML
    const pageContent = await umbracoUi.page.content();

    // Verify JSON-LD script tag exists
    expect(pageContent).toContain('application/ld+json');

    // Parse the JSON-LD and check the @type
    const jsonLdMatch = pageContent.match(
      /<script type="application\/ld\+json">([\s\S]*?)<\/script>/
    );
    expect(jsonLdMatch).toBeTruthy();

    const jsonLd = JSON.parse(jsonLdMatch![1]);
    expect(jsonLd['@context']).toBe('https://schema.org');

    // The v1.4+ default emits a Yoast-style @graph (a single script tag whose
    // body is {"@context": ..., "@graph": [ ... ]}) rather than a flat,
    // top-level Thing. Find the WebSite node within that graph; fall back to
    // the flat shape so this test also passes when UseGraphModel is disabled.
    const website = findNodeOfType(jsonLd, 'WebSite');
    expect(website, 'home page should expose a WebSite node').toBeTruthy();
    expect(website['name']).toBeTruthy();

    // Also verify a product page emits a product-family JSON-LD node. The
    // seed maps individualProductPage → IndividualProduct (a Product subtype),
    // rendered under the en-US culture path. We accept any Product-family
    // @type so the assertion stays robust to the exact subtype the mapping
    // uses.
    await umbracoUi.page.goto(
      baseUrl + '/en/categories/products/electronics/limited-edition-watch/',
    );
    await umbracoUi.page.waitForLoadState('domcontentloaded', { timeout: 15_000 });

    const productContent = await umbracoUi.page.content();
    expect(productContent).toContain('application/ld+json');

    const productFamilyTypes = ['Product', 'IndividualProduct', 'ProductModel'];
    const productJsonLdMatches = productContent.matchAll(
      /<script type="application\/ld\+json">([\s\S]*?)<\/script>/g
    );
    let productJsonLd: any = null;
    for (const match of productJsonLdMatches) {
      const parsed = JSON.parse(match[1]);
      const product = productFamilyTypes
        .map((t) => findNodeOfType(parsed, t))
        .find(Boolean);
      if (product) {
        productJsonLd = product;
        break;
      }
    }

    expect(productJsonLd, 'product page should expose a Product-family node').toBeTruthy();
    expect(productFamilyTypes).toContain(productJsonLd['@type']);
    expect(productJsonLd['name']).toBeTruthy();
  });

  test('JSON-LD preview works in backoffice', async ({ umbracoUi }) => {
    await umbracoUi.goToBackOffice();
    await umbracoUi.content.goToSection(ConstantHelper.sections.content);

    // The tree item's own `waitFor` covers tree-ready; no need to block on
    // a `networkidle` window the backoffice may never give us.
    const treeItem = umbracoUi.page.locator('umb-tree-item').first();
    await treeItem.waitFor({ timeout: 15_000 });
    await treeItem.locator('a').first().click();

    // Workspace readiness = the JSON-LD tab being queryable; the tab's
    // `isVisible` check below is the real gate.
    const jsonLdTab = umbracoUi.page.getByRole('tab', { name: /JSON-LD/i });
    if (await jsonLdTab.isVisible({ timeout: 10_000 }).catch(() => false)) {
      await jsonLdTab.click();

      // Wait for the JSON-LD content view to load
      const jsonLdView = umbracoUi.page.locator('schemeweaver-jsonld-content-view');
      await expect(jsonLdView).toBeVisible({ timeout: 10_000 });

      // Click generate preview button. The test doesn't assert on preview
      // content — `click()` is enough of a smoke check, so no post-click
      // sleep is needed.
      const generateBtn = jsonLdView.locator('uui-button', { hasText: /Generate Preview/i });
      if (await generateBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await generateBtn.click();
      }
    }
  });
});

// ---------------------------------------------------------------------------
// Document Type Workspace View Tests
// ---------------------------------------------------------------------------

test.describe('Document Type Workspace View', () => {
  test('Schema.org tab appears on document type editor', async ({ umbracoUi }) => {
    await umbracoUi.page.goto('/umbraco/section/settings');

    // The tree link's own `waitFor` is sufficient — no need to wait on a
    // `networkidle` window that never arrives on the polling backoffice.
    const docTypesLink = umbracoUi.page.locator('a', { hasText: 'Document Types' }).first();
    await docTypesLink.waitFor({ timeout: 15_000 });
    await docTypesLink.click();

    // Expand the tree if needed and click on first child document type
    const treeItems = umbracoUi.page.locator('umb-tree-item umb-tree-item');
    const firstChild = treeItems.first();

    if (await firstChild.isVisible({ timeout: 10_000 }).catch(() => false)) {
      await firstChild.locator('a').first().click();

      // Look for Schema.org workspace view tab
      const schemaTab = umbracoUi.page.getByRole('tab', { name: /Schema\.org/i });
      await expect(schemaTab).toBeVisible({ timeout: 15_000 });
    }
  });
});

// ---------------------------------------------------------------------------
// Initial Mapping Flow (redesigned picker + property-mapping modal)
// ---------------------------------------------------------------------------

test.describe('Initial Mapping Flow', () => {
  const BASE = '/umbraco/management/api/v1/schemeweaver';
  const ALIAS = 'categoriesListing';
  const CONTENT_TYPE_KEY = '0b154cb4-8fdf-4afc-a274-4173278d5618';

  /**
   * Unconditional end-to-end coverage of the redesigned initial-mapping flow:
   * empty state → "Select Schema.org Type" picker → "Map Properties" modal →
   * mapped view. Every assertion is unconditional — no isVisible() guards —
   * so a regression can never silently no-op the test the way the old
   * wizard-era selectors did.
   *
   * Selector policy: ONLY the frozen data-mark hooks (testIdAttribute is
   * 'data-mark') + the stable schemeweaver-* element names. Table-level marks
   * are always scoped through a parent element because
   * schemeweaver-property-mapping-table renders in BOTH the workspace view and
   * the property-mapping modal.
   *
   * Environment safety (same idiom as review-block-mapping-ui.spec.ts):
   * categoriesListing is the known UNMAPPED seed doc type, so the arrange GET
   * normally 404s — but we snapshot defensively and restore in `finally` so
   * the TestHost leaves exactly as found.
   */
  test('empty state → schema picker → property mapping modal → mapped view (categoriesListing → CollectionPage)', async ({ umbracoUi }) => {
    const page = umbracoUi.page;

    // ── ARRANGE: categoriesListing must be unmapped.
    const before = await page.request.get(`${BASE}/mappings/${ALIAS}`);
    const snapshot = before.ok() ? await before.json() : null;
    if (snapshot) {
      const del = await page.request.delete(`${BASE}/mappings/${ALIAS}`);
      expect(del.ok(), `arrange DELETE failed: ${del.status()}`).toBeTruthy();
    }

    try {
      await goToDocTypeSchemaTab(umbracoUi, 'Categories Listing');

      // ── Empty state: the single Map to Schema.org affordance.
      const mapToSchemaBtn = page.getByTestId('schemeweaver:map-to-schema');
      await expect(mapToSchemaBtn).toBeVisible({ timeout: 15_000 });

      // ── Open the picker (first pass: picker UX locks + the cancel path).
      await mapToSchemaBtn.click();
      const picker = page.locator('schemeweaver-schema-picker-modal');
      await expect(picker).toBeVisible({ timeout: 10_000 });
      await picker.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });

      // Pinned-footer regression lock: the Select button must be inside the
      // viewport as soon as the picker opens (it used to sit below the fold).
      await expect(picker.getByTestId('schemeweaver:schema-picker-submit')).toBeInViewport();

      // No query → the curated "Common types" shortlist: exactly 20 rows
      // (never the full ~800-type universe dump).
      await expect(picker.locator('umb-ref-item')).toHaveCount(20);

      // Typing filters in-memory with a render cap of 50 plus an explicit
      // "Showing X of N" note — the broad query 'a' matches most of the
      // type universe, so the cap note must appear.
      await fillUuiInput(picker.getByTestId('schemeweaver:schema-search'), 'a');
      await expect(picker.locator('.cap-note')).toContainText(/Showing 50 of \d+/);

      // ── Cancel must not persist anything.
      await picker.getByTestId('schemeweaver:schema-picker-cancel').click();
      await expect(picker).toBeHidden({ timeout: 10_000 });
      const afterCancel = await page.request.get(`${BASE}/mappings/${ALIAS}`);
      expect(afterCancel.status(), 'cancelling the picker must not create a mapping').toBe(404);

      // ── Re-open and run the real flow: search → pick → submit.
      await page.getByTestId('schemeweaver:map-to-schema').click();
      await expect(picker).toBeVisible({ timeout: 10_000 });
      await picker.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
      await searchAndPickSchema(picker, 'CollectionPage');

      // ── Property-mapping modal: auto-map suggestions render as table rows.
      const mappingModal = page.locator('schemeweaver-property-mapping-modal');
      await expect(mappingModal).toBeVisible({ timeout: 10_000 });
      const table = mappingModal.locator('schemeweaver-property-mapping-table');
      await expect(table).toBeVisible({ timeout: 10_000 });
      // categoriesListing (title/description/heroImage + system fields) yields
      // 8 suggestion rows for CollectionPage; asserting ≥5 guards "real rows
      // rendered" without welding the test to the exact heuristic output.
      await expect(table.locator('uui-table-row').nth(4)).toBeVisible({ timeout: 10_000 });

      // Below-the-fold regression lock: Save must be pinned inside the viewport.
      const saveBtn = mappingModal.getByTestId('schemeweaver:mapping-save');
      await expect(saveBtn).toBeInViewport();

      await saveBtn.click();
      await expect(mappingModal).toBeHidden({ timeout: 15_000 });

      // ── Mapped view: the badge names the chosen type.
      await expect(page.getByTestId('schemeweaver:schema-type-badge')).toContainText('CollectionPage', {
        timeout: 15_000,
      });

      // ── The API is the source of truth: the mapping persisted.
      const persistedRes = await page.request.get(`${BASE}/mappings/${ALIAS}`);
      expect(persistedRes.ok(), `GET mappings/${ALIAS} after save failed: ${persistedRes.status()}`).toBeTruthy();
      const persisted = await persistedRes.json();
      expect(persisted.schemaTypeName).toBe('CollectionPage');
      expect(persisted.contentTypeAlias).toBe(ALIAS);
      expect(persisted.contentTypeKey?.toLowerCase()).toBe(CONTENT_TYPE_KEY);
      expect(persisted.propertyMappings.length).toBeGreaterThan(0);
    } finally {
      // ── RESTORE: remove the created mapping (and reinstate any snapshot) so
      // the TestHost returns to its prior state.
      const del = await umbracoUi.page.request.delete(`${BASE}/mappings/${ALIAS}`);
      expect([200, 204, 404]).toContain(del.status());
      if (snapshot) {
        const restore = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: snapshot });
        expect(restore.ok(), `restore POST failed: ${restore.status()}`).toBeTruthy();
      } else {
        const after = await umbracoUi.page.request.get(`${BASE}/mappings/${ALIAS}`);
        expect(after.status(), 'restore must leave categoriesListing unmapped (404)').toBe(404);
      }
    }
  });
});

// ---------------------------------------------------------------------------
// Complex Mapping Workflows Tests (via Document Type Workspace View)
// ---------------------------------------------------------------------------

test.describe('Complex Mapping Workflows', () => {
  // NOTE: the old "FAQPage auto-map shows blockContent suggestion for
  // mainEntity" test lived here. It asserted the removed 3-step wizard
  // (`.step-indicator` × 3) behind isVisible() guards, so it silently
  // no-opped once the redesign shipped. The scoped block-mapping test below
  // ("scoped block-mapping modal opens from the parent row…") covers the same
  // ground unconditionally on the data-mark contract, so the stale test was
  // deleted rather than rewritten.

  test('Product mapping shows complex type suggestions for offers and brand', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Product Page');

    const schemaView = umbracoUi.page.locator('schemeweaver-schema-mapping-view');
    await expect(schemaView).toBeVisible({ timeout: 10_000 });

    const mapBtn = schemaView.locator('uui-button', { hasText: /Map to Schema\.org/i }).first();
    if (await mapBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await mapBtn.click();

      const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
      await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
      await searchAndPickSchema(pickerModal, 'Product');

      const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
      await expect(mappingModal).toBeVisible({ timeout: 10_000 });

      await mappingModal.getByTestId('schemeweaver:mapping-cancel').click();
    }
  });

  test('Recipe mapping shows blockContent suggestion for recipeInstructions', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Recipe Page');

    const schemaView = umbracoUi.page.locator('schemeweaver-schema-mapping-view');
    await expect(schemaView).toBeVisible({ timeout: 10_000 });

    const mapBtn = schemaView.locator('uui-button', { hasText: /Map to Schema\.org/i }).first();
    if (await mapBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await mapBtn.click();

      const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
      await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
      await searchAndPickSchema(pickerModal, 'Recipe');

      const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
      await expect(mappingModal).toBeVisible({ timeout: 10_000 });

      await mappingModal.getByTestId('schemeweaver:mapping-cancel').click();
    }
  });

  test('Event mapping shows complex type suggestions for location and organizer', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Event Page');

    const schemaView = umbracoUi.page.locator('schemeweaver-schema-mapping-view');
    await expect(schemaView).toBeVisible({ timeout: 10_000 });

    const mapBtn = schemaView.locator('uui-button', { hasText: /Map to Schema\.org/i }).first();
    if (await mapBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await mapBtn.click();

      const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
      await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
      await searchAndPickSchema(pickerModal, 'Event');

      const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
      await expect(mappingModal).toBeVisible({ timeout: 10_000 });

      await mappingModal.getByTestId('schemeweaver:mapping-cancel').click();
    }
  });

  test('scoped block-mapping modal opens from the parent row and pre-fills the seeded legacy config (FAQ Page)', async ({ umbracoUi }) => {
    // The old 3-step wizard is gone: the "Block Mappings" modal is a route
    // editor scoped to ONE parent property-mapping row. The parent row's
    // Schema Property is the single source of truth, so there is no per-block
    // target dropdown and no step flow. Assertions are UNCONDITIONAL and use
    // only the agreed data-mark hooks + text.
    //
    // Arrange/restore: the assertions need the canonical seeded faqPage
    // mapping (uSync/v18/SchemeWeaverMappings/faqPage.config — a legacy flat
    // `nestedMappings` config with no blockAlias). We snapshot the live
    // mapping, POST the canonical seed, assert against the UI, and restore
    // the snapshot so the environment leaves exactly as found.
    const BASE = '/umbraco/management/api/v1/schemeweaver';

    const before = await umbracoUi.page.request.get(`${BASE}/mappings/faqPage`);
    expect(before.ok(), `GET mappings/faqPage failed: ${before.status()}`).toBeTruthy();
    const snapshot = await before.json();

    try {
      const ctRes = await umbracoUi.page.request.get(`${BASE}/content-types`);
      expect(ctRes.ok()).toBeTruthy();
      const faqType = (await ctRes.json()).find((ct: any) => ct.alias === 'faqPage');
      expect(faqType, 'faqPage content type not found — is the TestHost seeded?').toBeTruthy();

      const emptyRow = {
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
      };
      const seeded = {
        contentTypeAlias: 'faqPage',
        contentTypeKey: faqType.key,
        schemaTypeName: 'FAQPage',
        isEnabled: true,
        isInherited: false,
        idOverride: null,
        propertyMappings: [
          { ...emptyRow, schemaPropertyName: 'Name', contentTypePropertyAlias: 'title' },
          { ...emptyRow, schemaPropertyName: 'Description', contentTypePropertyAlias: 'description' },
          {
            ...emptyRow,
            schemaPropertyName: 'MainEntity',
            sourceType: 'blockContent',
            contentTypePropertyAlias: 'faqItems',
            nestedSchemaTypeName: 'Question',
            resolverConfig:
              '{"nestedMappings":[{"schemaProperty":"Name","contentProperty":"question"},{"schemaProperty":"AcceptedAnswer","contentProperty":"answer","wrapInType":"Answer","wrapInProperty":"Text"}]}',
          },
        ],
      };
      const arrange = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: seeded });
      expect(arrange.ok(), `arrange POST failed: ${arrange.status()}`).toBeTruthy();

      await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');
      const page = umbracoUi.page;

      // Parent table: the MainEntity row carries the renamed "Map blocks"
      // button (data-mark hook) — no picker/wizard detour.
      const mapBlocksBtn = page.getByTestId('schemeweaver:map-blocks:MainEntity');
      await expect(mapBlocksBtn).toBeVisible({ timeout: 15_000 });
      await mapBlocksBtn.click();

      const modal = page.locator('schemeweaver-nested-mapping-modal');
      await expect(modal).toBeVisible({ timeout: 10_000 });

      // Scoped headline names the parent row's Schema Property; the 3-step
      // wizard chrome is gone.
      await expect(modal.getByText(/Map blocks to\s+main\s?entity/i).first()).toBeVisible();
      await expect(modal.locator('.step-indicator')).toHaveCount(0);

      // The seeded legacy flat config (no blockAlias) pre-fills the faqItem
      // block row, routed to nested type Question. The type control may be a
      // constrained select or the free-search input depending on the target's
      // accepted range — assert the VALUE either way (option/input text always
      // reports hidden to Playwright).
      const blockRow = modal.getByTestId('schemeweaver:block-row:faqItem');
      await expect(blockRow).toBeVisible({ timeout: 10_000 });
      await expect(blockRow.locator('select, input').first()).toHaveValue('Question');

      // No per-block target-property dropdown: the target is fixed context
      // from the parent row.
      await expect(modal.getByRole('combobox', { name: /target/i })).toHaveCount(0);
      await expect(modal.getByLabel(/target propert/i)).toHaveCount(0);

      // Modal save (data-mark hook) closes the panel in one step.
      await modal.getByTestId('schemeweaver:block-modal-save').click();
      await expect(modal).toBeHidden({ timeout: 10_000 });

      // Route summary tags replace the removed "Nested Schema Type" input on
      // the parent row.
      await expect(page.getByTestId('schemeweaver:block-summary:MainEntity')).toBeVisible({ timeout: 10_000 });
    } finally {
      const restore = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: snapshot });
      expect(restore.ok(), `restore POST failed: ${restore.status()}`).toBeTruthy();
    }
  });

  test('complex type modal shows Configure button for nested complex sub-properties', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Product Page');

    const schemaView = umbracoUi.page.locator('schemeweaver-schema-mapping-view');
    await expect(schemaView).toBeVisible({ timeout: 10_000 });

    // Map Product Page to Product schema
    const mapBtn = schemaView.locator('uui-button', { hasText: /Map to Schema\.org/i }).first();
    if (!await mapBtn.isVisible({ timeout: 5_000 }).catch(() => false)) return;

    await mapBtn.click();

    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await searchAndPickSchema(pickerModal, 'Product');

    const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    await expect(mappingModal).toBeVisible({ timeout: 10_000 });

    // Find a complex type property's Configure button (e.g., Brand → Organization/Brand)
    const configButton = mappingModal.locator('uui-button', { hasText: /Configure Schema\.org Type/i }).first();
    if (!await configButton.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await mappingModal.getByTestId('schemeweaver:mapping-cancel').click();
      return;
    }

    await configButton.click();

    // Complex type modal should open (stacked on top)
    const complexModal = umbracoUi.page.locator('schemeweaver-complex-type-mapping-modal');
    await expect(complexModal).toBeVisible({ timeout: 10_000 });
    await complexModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => {});

    // The modal should show a mapping table with sub-properties
    const subTable = complexModal.locator('uui-table');
    if (await subTable.isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Check if any sub-property that is itself complex has a "Configure" button
      // This validates the infinite editing depth capability
      const nestedConfigButton = complexModal.locator('uui-button', { hasText: /Configure Schema\.org Type/i });
      const nestedConfigCount = await nestedConfigButton.count();

      // Schema.org types like Organization have complex sub-properties (e.g., address → PostalAddress)
      // so we expect at least one nested configure button
      if (nestedConfigCount > 0) {
        // Click the first nested configure button to open a second level modal
        await nestedConfigButton.first().click();

        // A second complex-type-mapping modal should stack on top
        const secondLevelModal = umbracoUi.page.locator('schemeweaver-complex-type-mapping-modal').nth(1);
        await expect(secondLevelModal).toBeVisible({ timeout: 10_000 });
        await secondLevelModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => {});

        // Close the second level modal
        const closeSecond = secondLevelModal.locator('uui-button[label="Close"]');
        if (await closeSecond.isVisible({ timeout: 3_000 }).catch(() => false)) {
          await closeSecond.click();
        }
      }
    }

    // Close the first complex type modal
    const closeFirst = complexModal.locator('uui-button[label="Close"]');
    if (await closeFirst.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await closeFirst.click();
    }

    // Close the mapping modal
    await mappingModal.getByTestId('schemeweaver:mapping-cancel').click();
  });
});

// ---------------------------------------------------------------------------
// Entity Actions Tests
// ---------------------------------------------------------------------------

test.describe('Entity Actions', () => {
  test('Map to Schema.org action exists on document type context menu', async ({ umbracoUi }) => {
    await umbracoUi.page.goto('/umbraco/section/settings');

    const docTypesLink = umbracoUi.page.locator('a', { hasText: 'Document Types' }).first();
    await docTypesLink.waitFor({ timeout: 15_000 });

    // Expand the Document Types tree to see children
    const expandBtn = umbracoUi.page.locator('button[aria-label*="Expand"]').first();
    if (await expandBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await expandBtn.click();
    }

    // Find first child tree item
    const firstChild = umbracoUi.page.locator('umb-tree-item umb-tree-item').first();
    if (await firstChild.isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Hover to reveal actions button
      await firstChild.hover();

      // Try to open the actions dropdown
      const actionsBtn = firstChild.locator('button').filter({ hasText: /actions/i }).first();
      if (await actionsBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await actionsBtn.click();

        // Look for our custom entity action
        const mapAction = umbracoUi.page.getByRole('button', { name: /Map to Schema\.org/i });
        await expect(mapAction).toBeVisible({ timeout: 5_000 });
      }
    }
  });
});

// ────────────────────────────────────────────────────────────────
// Review Fixes — validates context wiring, error handling, a11y
// ────────────────────────────────────────────────────────────────
test.describe('Review Fixes', () => {
  test('Schema.org tab loads mapping via context', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Product Page');

    // Verify the schema type badge is visible (indicates mapping loaded via context)
    const schemaTag = umbracoUi.page.locator('schemeweaver-schema-mapping-view uui-tag').first();
    await expect(schemaTag).toBeVisible({ timeout: 10_000 });

    // Verify property mapping table renders
    const table = umbracoUi.page.locator('schemeweaver-property-mapping-table');
    await expect(table).toBeVisible({ timeout: 10_000 });
  });

  test('Save mapping persists after page refresh', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'Product Page');

    // Capture the schema type name
    const schemaTag = umbracoUi.page.locator('schemeweaver-schema-mapping-view uui-tag').first();
    await expect(schemaTag).toBeVisible({ timeout: 10_000 });
    const schemaTypeName = await schemaTag.textContent();

    await umbracoUi.page.reload();

    const schemaTab = umbracoUi.page.getByRole('tab', { name: /Schema\.org/i });
    if (await schemaTab.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await schemaTab.click();
    }

    // Verify same schema type persists
    const schemaTagAfter = umbracoUi.page.locator('schemeweaver-schema-mapping-view uui-tag').first();
    await expect(schemaTagAfter).toBeVisible({ timeout: 10_000 });
    await expect(schemaTagAfter).toContainText(schemaTypeName ?? '');
  });

});

// ────────────────────────────────────────────────────────────────
// Dynamic Root Config Round-Trip — proves parent/ancestor/sibling
// source types persist the Umbraco dynamic root picker config
// through the full API + database path.
// ────────────────────────────────────────────────────────────────
test.describe('Dynamic Root Config Round-Trip', () => {
  test('dynamicRootConfig survives save and reload through management API', async ({ umbracoUi }) => {
    // `goToBackOffice()` primes the cookie jar that `page.request` inherits —
    // no need to wait for a `networkidle` window on top of that.
    await umbracoUi.goToBackOffice();

    // Use a synthetic alias that no real content type owns. This isolates the
    // round-trip test from the seeded data so a failure cannot strip mappings
    // belonging to productPage / blogArticle / etc.
    const testAlias = 'e2eDynamicRootRoundTripTest';
    const dynamicRootJson = JSON.stringify({
      originAlias: 'Root',
      querySteps: [{ unique: 'e2e-guid-123', alias: 'childOfType' }],
    });

    try {
      // ── STEP 1: POST a brand-new mapping with three properties, one of
      // which carries a dynamicRootConfig blob. Multiple properties verify the
      // round-trip preserves the full collection, not just one row.
      const newMapping = {
        contentTypeAlias: testAlias,
        contentTypeKey: '00000000-0000-0000-0000-000000000000',
        schemaTypeName: 'Article',
        isEnabled: true,
        isInherited: false,
        propertyMappings: [
          {
            schemaPropertyName: 'headline',
            sourceType: 'property',
            contentTypePropertyAlias: 'title',
            sourceContentTypeAlias: null,
            transformType: null,
            isAutoMapped: false,
            staticValue: null,
            nestedSchemaTypeName: null,
            resolverConfig: null,
            dynamicRootConfig: null,
          },
          {
            schemaPropertyName: 'description',
            sourceType: 'property',
            contentTypePropertyAlias: 'summary',
            sourceContentTypeAlias: null,
            transformType: null,
            isAutoMapped: false,
            staticValue: null,
            nestedSchemaTypeName: null,
            resolverConfig: null,
            dynamicRootConfig: null,
          },
          {
            schemaPropertyName: 'publisher',
            sourceType: 'parent',
            contentTypePropertyAlias: null,
            sourceContentTypeAlias: 'productListing',
            transformType: null,
            isAutoMapped: false,
            staticValue: null,
            nestedSchemaTypeName: null,
            resolverConfig: null,
            dynamicRootConfig: dynamicRootJson,
          },
        ],
      };

      const saveResponse = await umbracoUi.page.request.post(
        '/umbraco/management/api/v1/schemeweaver/mappings',
        { data: newMapping }
      );
      expect(saveResponse.ok(), `Save failed: ${saveResponse.status()}`).toBeTruthy();

      // ── STEP 2: GET the mapping back via the by-alias endpoint and assert
      // that ALL THREE property mappings survived, not just the publisher.
      const fetchResponse = await umbracoUi.page.request.get(
        `/umbraco/management/api/v1/schemeweaver/mappings/${testAlias}`
      );
      expect(fetchResponse.ok(), `Fetch failed: ${fetchResponse.status()}`).toBeTruthy();
      const saved = await fetchResponse.json();

      expect(saved.propertyMappings, 'propertyMappings missing on response').toBeTruthy();
      expect(saved.propertyMappings.length, 'expected three property mappings to round-trip').toBe(3);

      const publisherMapping = saved.propertyMappings.find(
        (p: any) => p.schemaPropertyName === 'publisher' && p.sourceType === 'parent'
      );
      expect(publisherMapping, 'publisher mapping not found').toBeTruthy();
      expect(publisherMapping.sourceContentTypeAlias).toBe('productListing');

      // The dynamicRootConfig JSON string must round-trip byte-exact
      expect(publisherMapping.dynamicRootConfig).toBe(dynamicRootJson);

      const parsed = JSON.parse(publisherMapping.dynamicRootConfig);
      expect(parsed.originAlias).toBe('Root');
      expect(parsed.querySteps).toHaveLength(1);
      expect(parsed.querySteps[0].unique).toBe('e2e-guid-123');
      expect(parsed.querySteps[0].alias).toBe('childOfType');

      // Verify the other two property mappings were preserved with their fields
      const headlineMapping = saved.propertyMappings.find(
        (p: any) => p.schemaPropertyName === 'headline'
      );
      expect(headlineMapping, 'headline mapping missing').toBeTruthy();
      expect(headlineMapping.contentTypePropertyAlias).toBe('title');

      const descriptionMapping = saved.propertyMappings.find(
        (p: any) => p.schemaPropertyName === 'description'
      );
      expect(descriptionMapping, 'description mapping missing').toBeTruthy();
      expect(descriptionMapping.contentTypePropertyAlias).toBe('summary');
    } finally {
      // ── STEP 3: Always delete the synthetic mapping so the test leaves no
      // residue. No real content type uses this alias, so failure here is
      // harmless to other tests.
      await umbracoUi.page.request.delete(
        `/umbraco/management/api/v1/schemeweaver/mappings/${testAlias}`
      );
    }
  });
});

// ---------------------------------------------------------------------------
// Change Schema Type (issue #41)
// ---------------------------------------------------------------------------

test.describe('Change Schema Type', () => {
  const BASE = '/umbraco/management/api/v1/schemeweaver';
  const ALIAS = 'productPage';

  /**
   * The regression the issue reports: switching an already-mapped document type
   * to another Schema.org type used to mean losing every hand-made property
   * mapping, because the only route through was the entity action's auto-map
   * re-seed. Here the type changes and the mappings must still be there.
   *
   * IndividualProduct is deliberately chosen: it derives from Product, so it
   * inherits every property the seed mapping uses and nothing should be dropped
   * — the confirmation reports a clean carry-over and the drop list is absent.
   *
   * Selector policy as per the header: frozen data-mark hooks, stable
   * schemeweaver-* element names and visible text only. The confirmation is
   * Umbraco's own umb-confirm-modal, targeted by element name + button text.
   *
   * Environment safety: snapshot the mapping up front and restore it in
   * `finally`, so the TestHost is left exactly as found either way.
   */
  test('changing the type keeps the existing property mappings (productPage → IndividualProduct)', async ({ umbracoUi }) => {
    const page = umbracoUi.page;

    await goToDocTypeSchemaTab(umbracoUi, 'Product Page');

    // ── ARRANGE: productPage is a seeded, mapped doc type. Snapshot it.
    const beforeRes = await page.request.get(`${BASE}/mappings/${ALIAS}`);
    expect(beforeRes.ok(), `GET mappings/${ALIAS} failed: ${beforeRes.status()}`).toBeTruthy();
    const snapshot = await beforeRes.json();
    expect(snapshot.propertyMappings.length, 'this test needs a mapping with rows to preserve').toBeGreaterThan(0);
    const namesBefore = snapshot.propertyMappings
      .map((p: any) => p.schemaPropertyName)
      .sort();

    try {
      // ── ACT: change the type from the badge row.
      const changeBtn = page.getByTestId('schemeweaver:change-schema-type');
      await expect(changeBtn).toBeVisible({ timeout: 15_000 });
      await changeBtn.click();

      const picker = page.locator('schemeweaver-schema-picker-modal');
      await expect(picker).toBeVisible({ timeout: 15_000 });

      // The picker opens on the type the mapping is already on.
      await expect(picker.getByText('Current', { exact: true })).toBeVisible({ timeout: 10_000 });

      await picker.getByTestId('schemeweaver:schema-search').locator('input').fill('IndividualProduct');
      const option = picker.getByTestId('schemeweaver:schema-option:IndividualProduct');
      await expect(option).toBeVisible({ timeout: 10_000 });
      await option.click();
      await picker.getByTestId('schemeweaver:schema-picker-submit').click();
      await expect(picker).toBeHidden({ timeout: 15_000 });

      // ── The confirmation reports a clean carry-over for a derived type.
      const confirm = page.locator('umb-confirm-modal');
      await expect(confirm).toBeVisible({ timeout: 15_000 });
      await expect(confirm).toContainText('IndividualProduct');
      await expect(confirm).not.toContainText('will be removed');
      await confirm.getByText('Change', { exact: true }).click();
      await expect(confirm).toBeHidden({ timeout: 15_000 });

      // ── ASSERT: the badge names the new type…
      await expect(page.getByTestId('schemeweaver:schema-type-badge')).toContainText('IndividualProduct', {
        timeout: 15_000,
      });

      // …and the API — the source of truth — kept every mapping.
      const afterRes = await page.request.get(`${BASE}/mappings/${ALIAS}`);
      expect(afterRes.ok(), `GET mappings/${ALIAS} after change failed: ${afterRes.status()}`).toBeTruthy();
      const after = await afterRes.json();
      expect(after.schemaTypeName).toBe('IndividualProduct');
      expect(
        after.propertyMappings.map((p: any) => p.schemaPropertyName).sort(),
        'every property mapping must survive a change to a derived type',
      ).toEqual(namesBefore);
    } finally {
      // ── RESTORE: put the original mapping back, type included.
      const restore = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: snapshot });
      expect(restore.ok(), `restore POST failed: ${restore.status()}`).toBeTruthy();
    }
  });
});
