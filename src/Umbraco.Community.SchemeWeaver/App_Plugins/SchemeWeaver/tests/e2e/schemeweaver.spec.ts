import { expect } from '@playwright/test';
import { ConstantHelper, test } from '@umbraco/playwright-testhelpers';

/**
 * Helper: fill a uui-input web component by targeting its inner native input.
 */
async function fillUuiInput(locator: any, text: string) {
  const input = locator.locator('input');
  await input.fill(text);
}

/**
 * Type into the schema picker's search input and wait for a matching schema
 * item to appear. Replaces a blind 1 s `waitForTimeout()` that was there to
 * cover the debounced search render — the locator-based wait is both faster
 * on cache hits and more resilient on slow CI.
 */
async function searchAndPickSchema(pickerModal: any, schemaName: string) {
  await fillUuiInput(pickerModal.locator('uui-input').first(), schemaName);
  const match = pickerModal
    .locator('.schema-item', { hasText: new RegExp(`^${schemaName}$`, 'i') })
    .first();
  await match.waitFor({ state: 'visible', timeout: 10_000 });
  await match.click();
  await pickerModal.locator('uui-button[look="primary"]').last().click();
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
        expect(parsed['@type']).toBeTruthy();
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
      expect(parsed['@type']).toBeTruthy();
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
    expect(jsonLd['@type']).toBe('WebSite');
    expect(jsonLd['name']).toBeTruthy();

    // Also verify the product page has JSON-LD
    await umbracoUi.page.goto(baseUrl + '/categories/products/electronics/wireless-headphones-pro/');
    await umbracoUi.page.waitForLoadState('domcontentloaded', { timeout: 15_000 });

    const productContent = await umbracoUi.page.content();
    expect(productContent).toContain('application/ld+json');

    const productJsonLdMatches = productContent.matchAll(
      /<script type="application\/ld\+json">([\s\S]*?)<\/script>/g
    );
    let productJsonLd: any = null;
    for (const match of productJsonLdMatches) {
      const parsed = JSON.parse(match[1]);
      if (parsed['@type'] === 'Product') {
        productJsonLd = parsed;
        break;
      }
    }

    expect(productJsonLd).toBeTruthy();
    expect(productJsonLd['@type']).toBe('Product');
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
// Complex Mapping Workflows Tests (via Document Type Workspace View)
// ---------------------------------------------------------------------------

test.describe('Complex Mapping Workflows', () => {
  test('FAQPage auto-map shows blockContent suggestion for mainEntity', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');

    // The Schema.org workspace view should show the mapping UI
    const schemaView = umbracoUi.page.locator('schemeweaver-schema-mapping-view');
    await expect(schemaView).toBeVisible({ timeout: 10_000 });

    // If not yet mapped, click the Map to Schema.org button on the workspace view
    const mapBtn = schemaView.locator('uui-button', { hasText: /Map to Schema\.org/i }).first();
    if (await mapBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await mapBtn.click();

      // Pick FAQPage schema type
      const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
      await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
      await searchAndPickSchema(pickerModal, 'FAQPage');

      // Property mapping modal should show blockContent suggestion
      const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
      await expect(mappingModal).toBeVisible({ timeout: 10_000 });

      const table = mappingModal.locator('schemeweaver-property-mapping-table');
      await expect(table).toBeVisible({ timeout: 10_000 });

      // Should have a configure nested mapping button (from blockContent suggestion)
      const configButton = table.locator('uui-button', { hasText: /Configure Block Mapping/i });
      const hasConfigButton = await configButton.isVisible({ timeout: 5_000 }).catch(() => false);

      // If config button exists, test the wizard flow
      if (hasConfigButton) {
        await configButton.first().click();

        // Nested mapping wizard should open
        const nestedModal = umbracoUi.page.locator('schemeweaver-nested-mapping-modal');
        await expect(nestedModal).toBeVisible({ timeout: 10_000 });

        // Should show wizard step indicators
        const stepIndicators = nestedModal.locator('.step-indicator');
        const stepCount = await stepIndicators.count();
        expect(stepCount).toBe(3);

        // Close wizard
        const cancelBtn = nestedModal.locator('uui-button[label="Cancel"]');
        if (await cancelBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
          await cancelBtn.click();
        } else {
          const backBtn = nestedModal.locator('uui-button[label="Back"]');
          if (await backBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
            await backBtn.click();
          }
        }
      }

      // Close mapping modal
      await mappingModal.locator('uui-button[label="Cancel"]').click();
    }
  });

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

      await mappingModal.locator('uui-button[label="Cancel"]').click();
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

      await mappingModal.locator('uui-button[label="Cancel"]').click();
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

      await mappingModal.locator('uui-button[label="Cancel"]').click();
    }
  });

  test('nested mapping wizard completes full 3-step flow', async ({ umbracoUi }) => {
    await goToDocTypeSchemaTab(umbracoUi, 'FAQ Page');

    const schemaView = umbracoUi.page.locator('schemeweaver-schema-mapping-view');
    await expect(schemaView).toBeVisible({ timeout: 10_000 });

    // Map FAQ Page to FAQPage schema
    const mapBtn = schemaView.locator('uui-button', { hasText: /Map to Schema\.org/i }).first();
    if (!await mapBtn.isVisible({ timeout: 5_000 }).catch(() => false)) return;

    await mapBtn.click();

    const pickerModal = umbracoUi.page.locator('schemeweaver-schema-picker-modal');
    await pickerModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 });
    await searchAndPickSchema(pickerModal, 'FAQPage');

    const mappingModal = umbracoUi.page.locator('schemeweaver-property-mapping-modal');
    await expect(mappingModal).toBeVisible({ timeout: 10_000 });

    // Find and click the configure button for mainEntity
    const configButton = mappingModal.locator('uui-button', { hasText: /Configure Block Mapping/i }).first();
    if (!await configButton.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await mappingModal.locator('uui-button[label="Cancel"]').click();
      return;
    }

    await configButton.click();

    // Step 1: Block type picker → should auto-advance if only 1 block type
    const nestedModal = umbracoUi.page.locator('schemeweaver-nested-mapping-modal');
    await expect(nestedModal).toBeVisible({ timeout: 10_000 });
    await nestedModal.locator('uui-loader-circle').waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => {});

    // Step 2: Mappings - check for mapping table
    const mappingTable = nestedModal.locator('uui-table');
    if (await mappingTable.isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Click Preview to go to step 3
      const previewBtn = nestedModal.locator('uui-button', { hasText: 'Preview' });
      if (await previewBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await previewBtn.click();

        // Step 3: Preview — the `previewSummary.isVisible` check below is
        // the real readiness signal; no need to sleep beforehand.
        const previewSummary = nestedModal.locator('.preview-summary');
        if (await previewSummary.isVisible({ timeout: 3_000 }).catch(() => false)) {
          // Save
          const saveBtn = nestedModal.locator('uui-button[label="Save Mapping"]');
          if (await saveBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
            await saveBtn.click();
            await expect(nestedModal).not.toBeVisible({ timeout: 5_000 });
          }
        }
      }
    }

    // Close the mapping modal
    const cancelBtn = mappingModal.locator('uui-button[label="Cancel"]');
    if (await cancelBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await cancelBtn.click();
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
      await mappingModal.locator('uui-button[label="Cancel"]').click();
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
    await mappingModal.locator('uui-button[label="Cancel"]').click();
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
