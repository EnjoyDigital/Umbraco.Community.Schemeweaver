import { expect, fixture, html, waitUntil } from '@open-wc/testing';
import { http, HttpResponse } from 'msw';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './schema-mappings-dashboard.element.js';

// Helper to wait for loading to finish
async function waitForLoad(el: Element): Promise<void> {
  await waitUntil(
    () => !el.shadowRoot!.querySelector('uui-loader-circle'),
    'Loading did not complete',
    { timeout: 5000 }
  );
}

describe('SchemaMappingsDashboardElement', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    stopMockServiceWorker();
  });

  it('shows loading spinner initially', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    const loader = el.shadowRoot!.querySelector('uui-loader-circle');
    expect(loader).to.exist;
  });

  it('renders table with content types after load', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(3); // blogArticle, faqPage, productPage
  });

  it('shows positive badge for mapped content type', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    const badges = el.shadowRoot!.querySelectorAll('uui-badge');
    const positiveBadge = Array.from(badges).find(b => b.getAttribute('color') === 'positive');
    expect(positiveBadge).to.exist;
  });

  it('shows default badge for unmapped content type', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    const badges = el.shadowRoot!.querySelectorAll('uui-badge');
    const defaultBadge = Array.from(badges).find(b => b.getAttribute('color') === 'default');
    expect(defaultBadge).to.exist;
  });

  it('shows Edit, Preview, Delete buttons for mapped type', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    // Button labels are set via localize.term() - check for icon-based buttons
    const editIcon = el.shadowRoot!.querySelector('uui-icon[name="icon-edit"]');
    const bracketIcon = el.shadowRoot!.querySelector('uui-icon[name="icon-brackets"]');
    const trashIcon = el.shadowRoot!.querySelector('uui-icon[name="icon-trash"]');
    expect(editIcon).to.exist;
    expect(bracketIcon).to.exist;
    expect(trashIcon).to.exist;
  });

  it('shows Map button for unmapped type', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    // Look for the primary-look button (map button for unmapped types)
    const mapBtns = el.shadowRoot!.querySelectorAll('uui-button[look="primary"]');
    expect(mapBtns.length).to.be.greaterThan(0);
  });

  it('filters results by search term', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);

    const searchInput = el.shadowRoot!.querySelector('uui-input') as any;
    searchInput.value = 'blog';
    searchInput.dispatchEvent(new Event('input'));

    await (el as any).updateComplete;

    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(1);
  });

  it('handles API failure gracefully (no crash)', async () => {
    if (!worker) return;

    worker.use(
      http.get('/umbraco/management/api/v1/schemeweaver/mappings', () => {
        return HttpResponse.json({}, { status: 500 });
      })
    );

    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);

    // Dashboard uses notification context for errors, not inline error divs.
    // Verify the element rendered without crashing.
    expect(el.shadowRoot).to.exist;

    worker.resetHandlers();
  });
});
