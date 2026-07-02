import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './property-mapping-modal.element.js';

async function waitForLoad(el: any): Promise<void> {
  await el.updateComplete;
  await waitUntil(
    () => el.shadowRoot && !el.shadowRoot.querySelector('#loading'),
    'Loading did not complete',
    { timeout: 5000 }
  );
  await el.updateComplete;
}

describe('PropertyMappingModalElement', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });

  function createElement(alias = 'blogArticle', schemaType = 'Article', contentTypeKey = ''): any {
    const el = document.createElement('schemeweaver-property-mapping-modal') as any;
    el.data = { contentTypeAlias: alias, schemaType, contentTypeKey };
    document.body.appendChild(el);
    return el;
  }

  afterEach(() => {
    document.querySelectorAll('schemeweaver-property-mapping-modal').forEach(el => el.remove());
  });

  it('shows loading state initially', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.shadowRoot).to.exist;
  });

  it('renders property mapping table after load', async () => {
    const el = createElement();
    await waitForLoad(el);
    const table = el.shadowRoot!.querySelector('schemeweaver-property-mapping-table');
    expect(table).to.exist;
  });

  it('has Save and Cancel buttons', async () => {
    const el = createElement();
    await waitForLoad(el);
    const saveBtn = el.shadowRoot!.querySelector('uui-button[data-mark="schemeweaver:mapping-save"]');
    const cancelBtn = el.shadowRoot!.querySelector('uui-button[data-mark="schemeweaver:mapping-cancel"]');
    expect(saveBtn).to.exist;
    expect(cancelBtn).to.exist;
    expect(saveBtn!.getAttribute('label')).to.equal('Save Mapping');
    expect(cancelBtn!.getAttribute('label')).to.equal('Cancel');
  });

  // Pinned-footer contract: both footer buttons must live in the body-layout's
  // actions slot so umb-body-layout can pin them below the scrolling #main —
  // buttons rendered in the default slot scroll out of view at short viewports.
  it('renders Save and Cancel inside the [slot="actions"] footer', async () => {
    const el = createElement();
    await waitForLoad(el);
    const saveBtn = el.shadowRoot!.querySelector('uui-button[data-mark="schemeweaver:mapping-save"]');
    const cancelBtn = el.shadowRoot!.querySelector('uui-button[data-mark="schemeweaver:mapping-cancel"]');
    expect(saveBtn!.closest('[slot="actions"]')).to.exist;
    expect(cancelBtn!.closest('[slot="actions"]')).to.exist;
  });

  it('shows schema type in headline', async () => {
    const el = createElement();
    await waitForLoad(el);
    const headline = el.shadowRoot!.querySelector('umb-body-layout');
    expect(headline).to.exist;
    expect(headline!.getAttribute('headline')).to.contain('Article');
  });

  // Regression guard — two modals opened concurrently for different doc
  // types must each reflect only their own assigned contentTypeAlias in
  // `_mappings`, not the other's. Shared-context refactors that break
  // isolation should fail here.
  it('two modals opened concurrently for different doc types do not cross-contaminate', async () => {
    const elA = createElement('blogArticle', 'Article');
    const elB = createElement('homePage', 'WebSite');

    await Promise.all([waitForLoad(elA), waitForLoad(elB)]);

    const aliasesA: string[] = elA._mappings
      .map((m: { contentTypePropertyAlias: string }) => m.contentTypePropertyAlias)
      .filter(Boolean);
    const aliasesB: string[] = elB._mappings
      .map((m: { contentTypePropertyAlias: string }) => m.contentTypePropertyAlias)
      .filter(Boolean);

    expect(aliasesA).to.not.include('siteName');
    expect(aliasesB).to.not.include('title');
    expect(aliasesB).to.not.include('authorName');
    expect(aliasesB).to.not.include('publishDate');
  });

  // The block panel result must patch ONLY the opened row (never delete/rebuild
  // every blockContent row for the property) — mirrors the workspace view.
  it('nested-mapping result patches only the opened row and appends fan-out rows', async () => {
    const el = createElement('productPage', 'Product');
    await waitForLoad(el);

    function blockRow(overrides: Record<string, unknown> = {}): any {
      return {
        schemaPropertyName: 'review',
        schemaPropertyType: 'Review',
        sourceType: 'blockContent',
        contentTypePropertyAlias: 'reviews',
        sourceContentTypeAlias: '',
        staticValue: '',
        confidence: 80,
        editorAlias: 'Umbraco.BlockList',
        nestedSchemaTypeName: '',
        resolverConfig: null,
        acceptedTypes: ['Review'],
        isComplexType: true,
        expanded: false,
        subMappings: [],
        selectedSubType: '',
        sourceContentTypeProperties: [],
        ...overrides,
      };
    }

    const openedConfig = '{"routes":[{"blockAlias":"reviewItem","nestedSchemaType":"Review","propertyMappings":[]}]}';
    const siblingConfig = '{"routes":[{"blockAlias":"teamBlock","nestedSchemaType":"Person","propertyMappings":[]}]}';
    const opened = blockRow({ resolverConfig: null });
    const sibling = blockRow({ schemaPropertyName: 'hasPart', resolverConfig: siblingConfig });
    el._mappings = [opened, sibling];

    const fanOutConfig = '{"routes":[{"blockAlias":"promoBanner","nestedSchemaType":"WPHeader","propertyMappings":[]}]}';
    el._applyNestedMappingResult(0, 'Umbraco.BlockList', {
      resolverConfig: openedConfig,
      additionalTargets: [{ schemaPropertyName: 'mentions', resolverConfig: fanOutConfig }],
    });

    expect(el._mappings).to.have.lengthOf(3);
    const review = el._mappings.find((r: any) => r.schemaPropertyName === 'review');
    expect(review.resolverConfig).to.equal(openedConfig);
    // Sibling untouched — same object, byte-identical config.
    expect(el._mappings).to.include(sibling);
    expect(sibling.resolverConfig).to.equal(siblingConfig);
    // Fan-out target appended as a new blockContent row on the same property.
    const mentions = el._mappings.find((r: any) => r.schemaPropertyName === 'mentions');
    expect(mentions).to.exist;
    expect(mentions.sourceType).to.equal('blockContent');
    expect(mentions.contentTypePropertyAlias).to.equal('reviews');
    expect(mentions.resolverConfig).to.equal(fanOutConfig);
    expect(mentions.confidence).to.equal(null);
  });
});
