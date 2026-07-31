import { aTimeout, expect, fixture, html, waitUntil } from '@open-wc/testing';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { __mockContextRegistry } from '../__mocks__/context-api.js';
import { startMockServiceWorker, stopMockServiceWorker, worker } from '../mocks/setup.js';
import { serverErrorHandlers } from '../mocks/handlers.js';
import type { SchemaMappingDto } from '../api/types.js';
import { SchemeWeaverMappingChangedEvent } from '../utils/mapping-changed-event.js';
import './schema-mapping-view.element.js';

const BASE = '/umbraco/management/api/v1/schemeweaver';

/** Record of the modals a flow opened, in order, with the data each received. */
interface OpenedModal {
  alias?: string;
  data?: Record<string, unknown>;
}

/**
 * Stands in for the modal manager so a change-type flow can run headless:
 * the picker resolves to `schemaType` (or rejects, i.e. the user cancelled) and
 * the confirm dialog resolves or rejects per `confirm`.
 */
function stubModalManager(responses: { schemaType?: string; confirm?: boolean }): OpenedModal[] {
  const opened: OpenedModal[] = [];
  const manager = {
    open(_host: unknown, token: { alias?: string }, options?: { data?: Record<string, unknown> }) {
      opened.push({ alias: token?.alias, data: options?.data });
      const cancelled = () => ({ onSubmit: () => Promise.reject(new Error('cancelled')) });
      if (token?.alias === 'SchemeWeaver.Modal.SchemaPicker') {
        return responses.schemaType
          ? { onSubmit: () => Promise.resolve({ schemaType: responses.schemaType }) }
          : cancelled();
      }
      return responses.confirm ? { onSubmit: () => Promise.resolve(undefined) } : cancelled();
    },
  };
  __mockContextRegistry.provide(UMB_MODAL_MANAGER_CONTEXT, manager);
  return opened;
}

/** The mock DB is module-global, so type changes have to be undone between tests. */
async function restoreMapping(mapping: SchemaMappingDto) {
  await fetch(`${BASE}/mappings`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(mapping),
  });
}

describe('SchemaMappingViewElement', () => {
  before(async () => {
    await startMockServiceWorker();
  });

  after(() => {
    stopMockServiceWorker();
  });

  it('renders loading state initially', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`);
    const loader = el.shadowRoot!.querySelector('#loader uui-loader');
    expect(loader).to.exist;
    // The workspace editor provides the body layout — the view must not nest another.
    expect(el.shadowRoot!.querySelector('umb-body-layout')).to.equal(null);
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
    const mapButton = el.shadowRoot!.querySelector('[data-mark="schemeweaver:map-to-schema"]');
    expect(mapButton).to.exist;
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

    const badge = el.shadowRoot!.querySelector('[data-mark="schemeweaver:schema-type-badge"]');
    expect(badge).to.exist;
    const tag = badge!.querySelector('uui-tag[color="primary"]');
    expect(tag).to.exist;
    expect(tag!.textContent!.trim()).to.equal('Article');
  });

  it('renders auto-map button when mapping exists', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    el._contentTypeAlias = 'blogArticle';
    await el._fetchMapping();
    await el.updateComplete;

    const button = el.shadowRoot!.querySelector('uui-box [data-mark="schemeweaver:auto-map"]');
    expect(button).to.exist;
    expect(button!.getAttribute('slot')).to.equal('header-actions');
  });

  // Regression guard — a `reference` row (publisher → organization graph
  // piece) has no property alias, so the save filter must key off
  // targetPieceKey. Before the fix, saving the doc type silently dropped
  // every reference row created by auto-map/MCP/uSync.
  it('a reference row survives save and refetch', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    el._contentTypeAlias = 'blogArticle';
    await el._fetchMapping();
    await el.updateComplete;

    el._rows = [
      ...el._rows,
      {
        schemaPropertyName: 'publisher',
        schemaPropertyType: '',
        sourceType: 'reference',
        contentTypePropertyAlias: '',
        sourceContentTypeAlias: '',
        staticValue: '',
        confidence: null,
        editorAlias: '',
        nestedSchemaTypeName: '',
        resolverConfig: null,
        acceptedTypes: [],
        isComplexType: false,
        expanded: false,
        subMappings: [],
        selectedSubType: '',
        sourceContentTypeProperties: [],
        targetPieceKey: 'organization',
      },
    ];

    await el._handleSave();
    await el.updateComplete;

    const publisher = el._rows.find(
      (r: any) => r.schemaPropertyName.toLowerCase() === 'publisher' && r.sourceType === 'reference',
    );
    expect(publisher, 'reference row should survive the save round-trip').to.exist;
    expect(publisher.targetPieceKey).to.equal('organization');
  });

  // Regression guard — browsing a DIFFERENT document type for a drill-down row
  // must clear the old drill config: the old alias belongs to the old type, and
  // save passes resolverConfig verbatim, so leaving it would persist a drill the
  // UI no longer shows.
  it('re-browsing the drill doc type clears the stale drill config', async () => {
    const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;

    el._contentTypeAlias = 'blogArticle';
    await el._fetchMapping();
    await el.updateComplete;

    el._rows = [{
      schemaPropertyName: 'author',
      schemaPropertyType: '',
      sourceType: 'property',
      contentTypePropertyAlias: 'authorNode',
      sourceContentTypeAlias: '',
      staticValue: '',
      confidence: null,
      editorAlias: 'Umbraco.ContentPicker',
      nestedSchemaTypeName: '',
      resolverConfig: '{"pickedPropertyAlias":"someOldAlias","pickedContentTypeAlias":"someOldType"}',
      acceptedTypes: [],
      isComplexType: true,
      expanded: false,
      subMappings: [],
      selectedSubType: '',
      sourceContentTypeProperties: [],
      pickedPropertyAlias: 'someOldAlias',
      pickedContentTypeAlias: 'someOldType',
    }];

    await el._handleResolvePickedDocumentType(new CustomEvent('resolve-picked-document-type', {
      detail: { index: 0, documentTypeUnique: '00000000-0000-0000-0000-000000000099' },
    }));

    const row = el._rows[0];
    expect(row.pickedContentTypeAlias).to.equal('authorProfile');
    expect(row.pickedContentTypeProperties).to.include('fullName');
    expect(row.pickedPropertyAlias).to.equal(undefined);
    expect(row.resolverConfig).to.equal(null);
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

    it('fan-out into a LEGACY sibling expands its flat config to equivalent routes first (no shadowing)', async () => {
      const opened = blockRow({ resolverConfig: CFG_REVIEW });
      const legacySibling = blockRow({
        schemaPropertyName: 'about',
        resolverConfig: '{"nestedMappings":[{"schemaProperty":"name","contentProperty":"teamName"}]}',
        nestedSchemaTypeName: 'Person',
      });
      const el = await createView([opened, legacySibling]);

      el._applyNestedMappingResult(0, 'Umbraco.BlockList', {
        resolverConfig: CFG_REVIEW,
        additionalTargets: [
          {
            schemaPropertyName: 'about',
            resolverConfig: '{"routes":[{"blockAlias":"promoBanner","nestedSchemaType":"Service","propertyMappings":[]}]}',
          },
        ],
      });

      const about = el._rows.find((r: any) => r.schemaPropertyName === 'about');
      const merged = JSON.parse(about.resolverConfig);
      // The legacy wildcard flat list survives as an explicit wildcard route —
      // it is NOT silently shadowed by the new routes.
      const wildcard = merged.routes.find((r: any) => r.blockAlias === '');
      expect(wildcard, 'legacy flat config expanded to a wildcard route').to.exist;
      expect(wildcard.nestedSchemaType).to.equal('Person');
      expect(wildcard.propertyMappings.map((m: any) => m.schemaProperty)).to.deep.equal(['name']);
      const promo = merged.routes.find((r: any) => r.blockAlias === 'promoBanner');
      expect(promo.nestedSchemaType).to.equal('Service');
      // nestedMappings dropped from storage, mapping-level type upgraded away.
      expect(merged.nestedMappings).to.equal(undefined);
      expect(about.nestedSchemaTypeName).to.equal('');
    });

    it('a fan-out entry naming the OPENED row is ignored — never a duplicate sibling row', async () => {
      const opened = blockRow({ resolverConfig: CFG_REVIEW });
      const el = await createView([opened]);

      el._applyNestedMappingResult(0, 'Umbraco.BlockList', {
        resolverConfig: CFG_REVIEW,
        additionalTargets: [
          {
            schemaPropertyName: 'Review',
            resolverConfig: '{"routes":[{"blockAlias":"promoBanner","nestedSchemaType":"WPHeader","propertyMappings":[]}]}',
          },
        ],
      });

      expect(el._rows).to.have.lengthOf(1);
      expect(el._rows[0].resolverConfig).to.equal(CFG_REVIEW);
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

  // Issue #41 — the mapped type used to be a read-only badge, so the only way to
  // switch was re-running the entity action, which discarded hand-made rows.
  describe('changing the schema type', () => {
    const BLOG_ARTICLE_KEY = '00000000-0000-0000-0000-000000000001';

    /**
     * A known Article mapping whose three properties all exist on BlogPosting as
     * well. Seeded per test: the mock DB is module-global and earlier specs in
     * this file save extra rows into `blogArticle`, so inheriting whatever they
     * left behind would make these assertions order-dependent.
     */
    function articleFixture(): SchemaMappingDto {
      const row = (schemaPropertyName: string, contentTypePropertyAlias: string) => ({
        schemaPropertyName,
        sourceType: 'property' as const,
        contentTypePropertyAlias,
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: false,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
        dynamicRootConfig: null,
      });
      return {
        contentTypeAlias: 'blogArticle',
        contentTypeKey: BLOG_ARTICLE_KEY,
        schemaTypeName: 'Article',
        isEnabled: true,
        isInherited: false,
        propertyMappings: [
          row('headline', 'title'),
          row('author', 'authorName'),
          row('datePublished', 'publishDate'),
        ],
      };
    }

    /** Mounts the view against the seeded `blogArticle` mapping. */
    async function mountMapped() {
      const el = await fixture(html`<schemeweaver-schema-mapping-view></schemeweaver-schema-mapping-view>`) as any;
      el._contentTypeAlias = 'blogArticle';
      el._contentTypeKey = BLOG_ARTICLE_KEY;
      await el._fetchMapping();
      await el.updateComplete;
      return el;
    }

    /** Whatever the rest of the suite expects to find, put back when we are done. */
    let preExisting: SchemaMappingDto | undefined;
    let original: SchemaMappingDto;

    before(async () => {
      preExisting = (await (await fetch(`${BASE}/mappings/blogArticle`)).json()) as SchemaMappingDto;
    });

    after(async () => {
      if (preExisting) await restoreMapping(preExisting);
    });

    beforeEach(async () => {
      original = articleFixture();
      await restoreMapping(original);
    });

    it('offers a change control next to the type badge', async () => {
      stubModalManager({});
      const el = await mountMapped();

      const badge = el.shadowRoot!.querySelector('[data-mark="schemeweaver:schema-type-badge"]');
      const change = badge!.querySelector('[data-mark="schemeweaver:change-schema-type"]');
      expect(change, 'the badge row should offer a change affordance').to.exist;
    });

    it('tells the picker which type the mapping is already on', async () => {
      const opened = stubModalManager({});
      const el = await mountMapped();

      await el._handleChangeSchemaType();

      expect(opened[0].alias).to.equal('SchemeWeaver.Modal.SchemaPicker');
      expect(opened[0].data!.currentSchemaType).to.equal('Article');
    });

    it('keeps every compatible mapping and persists the new type', async () => {
      stubModalManager({ schemaType: 'BlogPosting', confirm: true });
      const el = await mountMapped();
      const before = el._rows.map((r: any) => r.schemaPropertyName).sort();

      await el._handleChangeSchemaType();
      await el.updateComplete;

      // _handleSave re-fetches, so this state came back from the mock DB.
      expect(el._mapping.schemaTypeName).to.equal('BlogPosting');
      expect(el._rows.map((r: any) => r.schemaPropertyName).sort()).to.deep.equal(before);

      const persisted = await (await fetch(`${BASE}/mappings/blogArticle`)).json();
      expect(persisted.schemaTypeName).to.equal('BlogPosting');
      expect(persisted.propertyMappings).to.have.lengthOf(original.propertyMappings.length);
    });

    it('refreshes the kept rows metadata to the new type', async () => {
      stubModalManager({ schemaType: 'BlogPosting', confirm: true });
      const el = await mountMapped();

      await el._handleChangeSchemaType();
      await el.updateComplete;

      const author = el._rows.find((r: any) => r.schemaPropertyName === 'author');
      expect(author.acceptedTypes).to.deep.equal(['Organization', 'Person']);
      expect(author.isComplexType).to.be.true;
    });

    it('drops the rows an unrelated type cannot accept', async () => {
      stubModalManager({ schemaType: 'FAQPage', confirm: true });
      const el = await mountMapped();

      await el._handleChangeSchemaType();
      await el.updateComplete;

      expect(el._mapping.schemaTypeName).to.equal('FAQPage');
      // none of headline/author/datePublished exist on FAQPage
      expect(el._rows.some((r: any) => r.schemaPropertyName === 'headline')).to.be.false;

      const persisted = await (await fetch(`${BASE}/mappings/blogArticle`)).json();
      expect(persisted.propertyMappings).to.be.empty;
    });

    it('writes nothing when the confirmation is declined', async () => {
      stubModalManager({ schemaType: 'FAQPage', confirm: false });
      const el = await mountMapped();

      await el._handleChangeSchemaType();
      await el.updateComplete;

      expect(el._mapping.schemaTypeName).to.equal('Article');
      const persisted = await (await fetch(`${BASE}/mappings/blogArticle`)).json();
      expect(persisted.schemaTypeName).to.equal('Article');
      expect(persisted.propertyMappings).to.have.lengthOf(original.propertyMappings.length);
    });

    it('does nothing when the picker is cancelled', async () => {
      const opened = stubModalManager({});
      const el = await mountMapped();

      await el._handleChangeSchemaType();

      expect(el._mapping.schemaTypeName).to.equal('Article');
      expect(opened, 'no confirmation should follow a cancelled picker').to.have.lengthOf(1);
    });

    it('does not confirm or save when the current type is re-picked', async () => {
      const opened = stubModalManager({ schemaType: 'Article', confirm: true });
      const el = await mountMapped();

      await el._handleChangeSchemaType();

      expect(opened).to.have.lengthOf(1);
      expect(el._mapping.schemaTypeName).to.equal('Article');
    });

    // Regression guard — the view answers UmbRequestReloadStructureForEntityEvent
    // by SAVING its rows (that is how it auto-saves with the document type). If
    // the entity action announced its change with that same event, an open tab
    // would immediately write its stale mapping back over it. The change is
    // announced with a SchemeWeaver-specific event that means "re-read".
    it('re-reads, and never re-saves, when a mapping changes elsewhere', async () => {
      const actionEvents = new EventTarget();
      __mockContextRegistry.provide(UMB_ACTION_EVENT_CONTEXT, actionEvents);
      stubModalManager({});
      const el = await mountMapped();
      expect(el._mapping.schemaTypeName).to.equal('Article');

      // Simulate the entity action having changed the type behind the view.
      await restoreMapping({ ...articleFixture(), schemaTypeName: 'FAQPage', propertyMappings: [] });
      actionEvents.dispatchEvent(new SchemeWeaverMappingChangedEvent(BLOG_ARTICLE_KEY));
      // Condition-based: the re-read is several awaited round-trips, and a fixed
      // delay is too tight when the suite runs files in parallel.
      await waitUntil(() => el._mapping?.schemaTypeName === 'FAQPage', 're-read should land');
      await el.updateComplete;

      expect(el._mapping.schemaTypeName, 'the view should have re-read the changed mapping').to.equal('FAQPage');
      const persisted = await (await fetch(`${BASE}/mappings/blogArticle`)).json();
      expect(persisted.schemaTypeName, 'the view must not write its stale state back').to.equal('FAQPage');
      expect(persisted.propertyMappings).to.be.empty;
    });

    // A `unique` arrives in whatever casing its source used, and the workspace
    // context and an entity action's args need not agree. An exact comparison
    // would silently stop the tab refreshing after a change made elsewhere.
    it('matches the announced key case-insensitively', async () => {
      const actionEvents = new EventTarget();
      __mockContextRegistry.provide(UMB_ACTION_EVENT_CONTEXT, actionEvents);
      stubModalManager({});
      const el = await mountMapped();

      await restoreMapping({ ...articleFixture(), schemaTypeName: 'FAQPage', propertyMappings: [] });
      actionEvents.dispatchEvent(new SchemeWeaverMappingChangedEvent(BLOG_ARTICLE_KEY.toUpperCase()));
      await waitUntil(() => el._mapping?.schemaTypeName === 'FAQPage', 're-read should land');
      await el.updateComplete;

      expect(el._mapping.schemaTypeName).to.equal('FAQPage');
    });

    // The action event context lives at the backoffice root and outlives every
    // view, so a view that forgot to unsubscribe would keep reacting — and the
    // sibling reload listener reacts by SAVING.
    it('stops listening once destroyed', async () => {
      const actionEvents = new EventTarget();
      __mockContextRegistry.provide(UMB_ACTION_EVENT_CONTEXT, actionEvents);
      stubModalManager({});
      const el = await mountMapped();

      el.destroy();

      await restoreMapping({ ...articleFixture(), schemaTypeName: 'FAQPage', propertyMappings: [] });
      actionEvents.dispatchEvent(new SchemeWeaverMappingChangedEvent(BLOG_ARTICLE_KEY));
      // Negative assertion, so a fixed (generous) wait is the honest shape here.
      await aTimeout(300);

      expect(el._mapping.schemaTypeName, 'a destroyed view must not react').to.equal('Article');
    });

    it('ignores a second change request while one is already in flight', async () => {
      const opened = stubModalManager({ schemaType: 'BlogPosting', confirm: true });
      const el = await mountMapped();

      await Promise.all([el._handleChangeSchemaType(), el._handleChangeSchemaType()]);
      await el.updateComplete;

      // One picker, one confirmation — the second click found the flow busy.
      expect(opened).to.have.lengthOf(2);
      expect(el._mapping.schemaTypeName).to.equal('BlogPosting');
    });

    // The flow mutates state optimistically before saving, and _handleSave
    // swallows its own error — so without a re-read the badge would advertise a
    // type the database never accepted, which the next document type save would
    // then quietly persist.
    it('re-reads when the save fails, instead of showing an unpersisted type', async () => {
      stubModalManager({ schemaType: 'BlogPosting', confirm: true });
      const el = await mountMapped();

      worker.use(...serverErrorHandlers.filter((h) => (h as any).info?.method === 'POST'));
      await el._handleChangeSchemaType();
      await el.updateComplete;
      worker.resetHandlers();

      expect(el._mapping.schemaTypeName, 'the view must fall back to what is stored').to.equal('Article');
      const persisted = await (await fetch(`${BASE}/mappings/blogArticle`)).json();
      expect(persisted.schemaTypeName).to.equal('Article');
    });
  });
});
