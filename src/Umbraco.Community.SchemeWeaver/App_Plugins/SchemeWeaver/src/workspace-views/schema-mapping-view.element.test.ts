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

  // Regression guards — two side-by-side workspace views for different
  // doc types must render fully independent state. If the SchemeWeaverContext
  // is ever shared as a singleton again, these should catch the leak.
  it('two side-by-side views render only their own doc type data', async () => {
    const viewA = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;
    const viewB = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    viewA._contentTypeAlias = 'blogArticle';
    viewB._contentTypeAlias = 'homePage';

    await Promise.all([viewA._fetchMapping(), viewB._fetchMapping()]);
    await Promise.all([viewA.updateComplete, viewB.updateComplete]);

    expect(viewA._mapping?.schemaTypeName).to.equal('Article');
    expect(viewB._mapping?.schemaTypeName).to.equal('WebSite');

    const aliasesA = viewA._rows.map((r: { contentTypePropertyAlias: string }) => r.contentTypePropertyAlias);
    const aliasesB = viewB._rows.map((r: { contentTypePropertyAlias: string }) => r.contentTypePropertyAlias);

    expect(aliasesA).to.include('title');
    expect(aliasesA).to.not.include('siteName');
    expect(aliasesB).to.include('siteName');
    expect(aliasesB).to.not.include('title');
  });

  it('refetching after alias changes does not retain the previous doc type state', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    el._contentTypeAlias = 'blogArticle';
    await el._fetchMapping();
    expect(el._mapping?.schemaTypeName).to.equal('Article');
    const articleAliases = el._rows.map((r: { contentTypePropertyAlias: string }) => r.contentTypePropertyAlias);
    expect(articleAliases).to.include('title');

    el._contentTypeAlias = 'faqPage';
    await el._fetchMapping();

    expect(el._mapping).to.equal(null);
    expect(el._rows).to.have.lengthOf(0);
  });

  // ── Row-scoped block-mapping merge ────────────────────────────────────────
  // The panel result patches ONLY the opened row; siblings stay byte-identical;
  // explicit fan-out entries merge into an existing sibling or append a new
  // row. No row is ever deleted or re-keyed (the old delete/rebuild silently
  // re-targeted rows — e.g. Product.review became hasPart).
  describe('row-scoped nested-mapping merge', () => {
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

    const CFG_REVIEW = '{"routes":[{"blockAlias":"reviewItem","nestedSchemaType":"Review","propertyMappings":[]}]}';
    const CFG_HASPART =
      '{"routes":[{"blockAlias":"teamBlock","nestedSchemaType":"Person","propertyMappings":[]},{"blockAlias":"promoBanner","nestedSchemaType":"Thing","propertyMappings":[]}]}';

    async function createView(rows: any[]): Promise<any> {
      const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;
      el._rows = rows;
      el._allSchemaProperties = [
        { name: 'review', propertyType: 'Review', isRequired: false, acceptedTypes: ['Review'], isComplexType: true, confidence: 60, isPopular: true },
        { name: 'hasPart', propertyType: 'CreativeWork', isRequired: false, acceptedTypes: ['CreativeWork'], isComplexType: true, confidence: 60, isPopular: true },
        { name: 'mentions', propertyType: 'Thing', isRequired: false, acceptedTypes: ['Thing'], isComplexType: true, confidence: 30, isPopular: false },
      ];
      return el;
    }

    it('a verbatim-unchanged result touches NOTHING — all row objects keep identity', async () => {
      const opened = blockRow({ resolverConfig: CFG_REVIEW });
      const sibling = blockRow({ schemaPropertyName: 'hasPart', resolverConfig: CFG_HASPART });
      const unrelated = blockRow({ schemaPropertyName: 'name', sourceType: 'property', contentTypePropertyAlias: 'productName' });
      const el = await createView([opened, sibling, unrelated]);

      el._applyNestedMappingResult(0, 'Umbraco.BlockList', { resolverConfig: CFG_REVIEW, additionalTargets: [] });

      expect(el._rows).to.have.lengthOf(3);
      expect(el._rows).to.include(opened);
      expect(el._rows).to.include(sibling);
      expect(el._rows).to.include(unrelated);
      expect(sibling.resolverConfig).to.equal(CFG_HASPART);
    });

    it('a changed result patches only the opened row; siblings stay byte-identical', async () => {
      const opened = blockRow({ resolverConfig: CFG_REVIEW });
      const sibling = blockRow({ schemaPropertyName: 'hasPart', resolverConfig: CFG_HASPART });
      const unrelated = blockRow({ schemaPropertyName: 'name', sourceType: 'property', contentTypePropertyAlias: 'productName' });
      const el = await createView([opened, sibling, unrelated]);

      const newConfig =
        '{"routes":[{"blockAlias":"reviewItem","nestedSchemaType":"Review","propertyMappings":[{"schemaProperty":"reviewBody","contentProperty":"reviewBody"}]}]}';
      el._applyNestedMappingResult(0, 'Umbraco.BlockList', { resolverConfig: newConfig, additionalTargets: [] });

      expect(el._rows).to.have.lengthOf(3);
      const patched = el._rows.find((r: any) => r.schemaPropertyName === 'review');
      expect(patched.resolverConfig).to.equal(newConfig);
      // Siblings untouched — same object references, byte-identical configs.
      expect(el._rows).to.include(sibling);
      expect(sibling.resolverConfig).to.equal(CFG_HASPART);
      expect(el._rows).to.include(unrelated);
    });

    it('clears nestedSchemaTypeName when a legacy config upgrades to routes', async () => {
      const legacy = '{"nestedMappings":[{"schemaProperty":"author","contentProperty":"reviewAuthor"}]}';
      const opened = blockRow({ resolverConfig: legacy, nestedSchemaTypeName: 'Review' });
      const el = await createView([opened]);

      el._applyNestedMappingResult(0, 'Umbraco.BlockList', { resolverConfig: CFG_REVIEW, additionalTargets: [] });

      const patched = el._rows.find((r: any) => r.schemaPropertyName === 'review');
      expect(patched.resolverConfig).to.equal(CFG_REVIEW);
      expect(patched.nestedSchemaTypeName).to.equal('');
    });

    it('additionalTargets merge into an existing sibling (replace same-alias routes, keep others) and append new rows', async () => {
      const opened = blockRow({ resolverConfig: CFG_REVIEW });
      const sibling = blockRow({ schemaPropertyName: 'hasPart', resolverConfig: CFG_HASPART });
      const el = await createView([opened, sibling]);

      const mentionsConfig = '{"routes":[{"blockAlias":"quoteBlock","nestedSchemaType":"Quotation","propertyMappings":[]}]}';
      el._applyNestedMappingResult(0, 'Umbraco.BlockList', {
        resolverConfig: CFG_REVIEW,
        additionalTargets: [
          {
            // Case-insensitive row match is required.
            schemaPropertyName: 'HasPart',
            resolverConfig: '{"routes":[{"blockAlias":"promoBanner","nestedSchemaType":"WPHeader","propertyMappings":[]}]}',
          },
          { schemaPropertyName: 'mentions', resolverConfig: mentionsConfig },
        ],
      });

      expect(el._rows).to.have.lengthOf(3);

      // Merged sibling: promoBanner route replaced, teamBlock route kept, row NOT re-keyed.
      const hasPart = el._rows.find((r: any) => r.schemaPropertyName === 'hasPart');
      expect(hasPart, 'existing sibling row kept its key').to.exist;
      const merged = JSON.parse(hasPart.resolverConfig);
      const byAlias = new Map(merged.routes.map((r: any) => [r.blockAlias, r.nestedSchemaType]));
      expect(byAlias.get('teamBlock')).to.equal('Person');
      expect(byAlias.get('promoBanner')).to.equal('WPHeader');

      // New row appended for the target with no existing row.
      const mentions = el._rows.find((r: any) => r.schemaPropertyName === 'mentions');
      expect(mentions).to.exist;
      expect(mentions.sourceType).to.equal('blockContent');
      expect(mentions.contentTypePropertyAlias).to.equal('reviews');
      expect(mentions.resolverConfig).to.equal(mentionsConfig);
      expect(mentions.confidence).to.equal(null);
      expect(mentions.schemaPropertyType).to.equal('Thing');
    });

    it('computes sibling claims from routed configs only, skipping legacy-wildcard siblings', async () => {
      const opened = blockRow({ resolverConfig: CFG_REVIEW });
      const routedSibling = blockRow({ schemaPropertyName: 'hasPart', resolverConfig: CFG_HASPART });
      const legacySibling = blockRow({
        schemaPropertyName: 'about',
        resolverConfig: '{"nestedMappings":[{"schemaProperty":"name","contentProperty":"name"}]}',
        nestedSchemaTypeName: 'Organization',
      });
      const otherProperty = blockRow({
        schemaPropertyName: 'mentions',
        contentTypePropertyAlias: 'otherBlocks',
        resolverConfig: '{"routes":[{"blockAlias":"quoteBlock","nestedSchemaType":"Quotation","propertyMappings":[]}]}',
      });
      const el = await createView([opened, routedSibling, legacySibling, otherProperty]);

      const claims = el._computeSiblingClaims(0, 'reviews');
      expect(claims).to.deep.equal([
        { schemaPropertyName: 'hasPart', blockAliases: ['teamBlock', 'promoBanner'] },
      ]);
    });
  });
});
