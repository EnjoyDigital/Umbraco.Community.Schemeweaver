import { expect, fixture, html } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import './schema-mapping-view.element.js';

describe('SchemaMappingViewElement', () => {
  before(async () => {
    await startMockServiceWorker();
  });

  after(() => {
    stopMockServiceWorker();
  });

  it('renders loading state initially', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`);
    const loader = el.shadowRoot!.querySelector('uui-loader-circle');
    expect(loader).to.exist;
  });

  it('shows empty state when no mapping found', async () => {
    // Mock the workspace context by setting _contentTypeAlias directly and triggering fetch
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    // Simulate the alias being set to an unmapped content type
    el._contentTypeAlias = 'faqPage';
    await el._fetchMapping();
    await el.updateComplete;

    const emptyState = el.shadowRoot!.querySelector('.empty-state');
    expect(emptyState).to.exist;
  });

  it('renders property table when mapping exists', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    // Simulate the alias being set to a mapped content type
    el._contentTypeAlias = 'blogArticle';
    await el._fetchMapping();
    await el.updateComplete;

    const table = el.shadowRoot!.querySelector('schemeweaver-property-mapping-table');
    expect(table).to.exist;
  });

  it('renders schema type tag when mapping exists', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    el._contentTypeAlias = 'blogArticle';
    await el._fetchMapping();
    await el.updateComplete;

    const tag = el.shadowRoot!.querySelector('uui-tag[color="primary"]');
    expect(tag).to.exist;
    expect(tag!.textContent!.trim()).to.equal('Article');
  });

  it('renders auto-map button when mapping exists', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    el._contentTypeAlias = 'blogArticle';
    await el._fetchMapping();
    await el.updateComplete;

    const buttons = el.shadowRoot!.querySelectorAll('.actions-bar uui-button');
    expect(buttons.length).to.equal(1);
  });
});
