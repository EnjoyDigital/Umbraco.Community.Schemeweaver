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
    expect(rows.length).to.equal(23); // all content types in mock DB
  });

  it('shows Mapped badge for mapped content type', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    const badges = el.shadowRoot!.querySelectorAll('uui-badge');
    const mappedBadge = Array.from(badges).find(b => b.textContent!.trim() === 'Mapped');
    expect(mappedBadge).to.exist;
    expect(mappedBadge!.getAttribute('color')).to.equal('positive');
  });

  it('shows Unmapped badge for unmapped content type', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    const badges = el.shadowRoot!.querySelectorAll('uui-badge');
    const unmappedBadge = Array.from(badges).find(b => b.textContent!.trim() === 'Unmapped');
    expect(unmappedBadge).to.exist;
    expect(unmappedBadge!.getAttribute('color')).to.equal('default');
  });

  it('shows Edit and Delete buttons for mapped type', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    const editBtn = el.shadowRoot!.querySelector('uui-button[label="Edit mapping"]');
    const deleteBtn = el.shadowRoot!.querySelector('uui-button[label="Delete mapping"]');
    expect(editBtn).to.exist;
    expect(deleteBtn).to.exist;
  });

  it('shows Map button for unmapped type', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    const mapBtn = el.shadowRoot!.querySelector('uui-button[label="Map to Schema.org"]');
    expect(mapBtn).to.exist;
  });

  it('renders Properties header with capital P', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);
    const headCells = el.shadowRoot!.querySelectorAll('uui-table-head-cell');
    const propertiesHeader = Array.from(headCells).find(cell => cell.textContent!.trim() === 'Properties');
    expect(propertiesHeader).to.exist;
  });

  it('filters results by search term', async () => {
    const el = await fixture(html`<schemeweaver-schema-mappings-dashboard></schemeweaver-schema-mappings-dashboard>`);
    await waitForLoad(el);

    const searchInput = el.shadowRoot!.querySelector('uui-input') as any;
    searchInput.value = 'blog';
    searchInput.dispatchEvent(new Event('input'));

    await (el as any).updateComplete;

    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(2); // blogArticle + blogListing
  });
});
