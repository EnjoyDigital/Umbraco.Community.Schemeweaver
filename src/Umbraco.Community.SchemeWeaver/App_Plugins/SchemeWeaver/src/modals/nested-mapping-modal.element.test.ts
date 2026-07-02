import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './nested-mapping-modal.element.js';

async function waitForLoad(el: any): Promise<void> {
  await el.updateComplete;
  await waitUntil(
    () => el.shadowRoot && !el.shadowRoot.querySelector('.loading'),
    'Loading did not complete',
    { timeout: 5000 }
  );
  await el.updateComplete;
}

/**
 * The seeded Product.review legacy flat shape: WILDCARD nestedMappings (no
 * blockAlias) + the mapping-level nested type carried separately on the row.
 */
const PRODUCT_REVIEW_LEGACY_CONFIG = JSON.stringify({
  nestedMappings: [
    { schemaProperty: 'author', contentProperty: 'reviewAuthor' },
    { schemaProperty: 'reviewRating', contentProperty: 'ratingValue', wrapInType: 'Rating', wrapInProperty: 'RatingValue' },
    { schemaProperty: 'reviewBody', contentProperty: 'reviewBody' },
  ],
});

describe('NestedMappingModalElement (row-scoped block-route editor)', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });

  // productPage.reviews is the scoped-panel fixture:
  //   reviewItem  → suggester says Review (FITS Product.review)
  //   promoBanner → suggester says WPHeader @ hasPart (does NOT fit → fan-out)
  function createElement(data: Record<string, unknown> = {}): any {
    const el = document.createElement('schemeweaver-nested-mapping-modal') as any;
    el.data = {
      contentTypeAlias: 'productPage',
      contentTypePropertyAlias: 'reviews',
      schemaPropertyName: 'review',
      schemaPropertyType: 'Review',
      acceptedTypes: ['Review'],
      existingConfig: null,
      ...data,
    };
    document.body.appendChild(el);
    return el;
  }

  /** Capture the value handed to the modal context on save. */
  function captureValue(el: any): { value?: any } {
    const captured: { value?: any } = {};
    el.modalContext = {
      setValue: (v: any) => {
        captured.value = v;
      },
      submit: () => {},
      reject: () => {},
    };
    return captured;
  }

  function rowOf(el: any, alias: string): any {
    return el._blockRows.find((r: any) => r.alias === alias);
  }

  afterEach(() => {
    document.querySelectorAll('schemeweaver-nested-mapping-modal').forEach((el) => el.remove());
  });

  it('renders one row per block element type', async () => {
    const el = createElement();
    await waitForLoad(el);
    const rows = el.shadowRoot!.querySelectorAll('.block-row');
    expect(rows.length).to.equal(2);
    expect(el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-row:reviewItem"]')).to.exist;
    expect(el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-row:promoBanner"]')).to.exist;
  });

  it('headline and context strip state the parent property as immutable context', async () => {
    const el = createElement();
    await waitForLoad(el);

    const layout = el.shadowRoot!.querySelector('umb-body-layout');
    expect(layout!.getAttribute('headline')).to.equal('Map blocks to review');

    const strip = el.shadowRoot!.querySelector('.context-strip');
    expect(strip, 'context strip rendered').to.exist;
    expect(strip!.textContent).to.contain('reviews');
    expect(strip!.textContent).to.contain('Block List/Grid');
    expect(strip!.textContent).to.contain('review');
    expect(strip!.textContent).to.contain('(Review)');
    expect(strip!.textContent).to.contain('Blocks mapped here are output as the review property of productPage.');
    expect(strip!.textContent).to.contain('Accepts: Review');
  });

  it('renders NO target-property dropdown anywhere — the target is fixed', async () => {
    const el = createElement();
    await waitForLoad(el);

    expect(el.shadowRoot!.querySelector('.target-select')).to.equal(null);
    // The old grouped-save surface is gone entirely.
    expect((el as any)._buildTargetMappings).to.equal(undefined);
    expect((el as any)._handleTargetChange).to.equal(undefined);
    // No select offers the old hardcoded target list.
    const selects = [...el.shadowRoot!.querySelectorAll('uui-select')] as any[];
    const offersTargets = selects.some((s) =>
      (s.options ?? []).some((o: any) => ['mainEntity', 'hasPart', 'about', 'mentions'].includes(o.value)),
    );
    expect(offersTargets).to.equal(false);
  });

  it('pre-fills EVERY block row from a legacy WILDCARD nestedMappings config', async () => {
    const el = createElement({
      existingConfig: PRODUCT_REVIEW_LEGACY_CONFIG,
      nestedSchemaTypeName: 'Review',
    });
    await waitForLoad(el);

    const review = rowOf(el, 'reviewItem');
    expect(review.mapped).to.equal(true);
    expect(review.nestedSchemaType).to.equal('Review');
    const author = review.propertyMappings.find((m: any) => m.schemaProperty === 'author');
    expect(author.contentProperty).to.equal('reviewAuthor');
    const rating = review.propertyMappings.find((m: any) => m.schemaProperty === 'reviewRating');
    expect(rating.contentProperty).to.equal('ratingValue');
    expect(rating.wrapInType).to.equal('Rating');
    const body = review.propertyMappings.find((m: any) => m.schemaProperty === 'reviewBody');
    expect(body.contentProperty).to.equal('reviewBody');

    // Wildcard entries apply to every block type — the old modal keyed them by ''
    // and showed nothing (the invisible-legacy bug).
    const promo = rowOf(el, 'promoBanner');
    expect(promo.mapped).to.equal(true);
    expect(promo.nestedSchemaType).to.equal('Review');
  });

  it('pre-fills from a routed config, including requiredProperties passthrough', async () => {
    const el = createElement({
      existingConfig: JSON.stringify({
        routes: [
          {
            blockAlias: 'reviewItem',
            nestedSchemaType: 'Review',
            propertyMappings: [{ schemaProperty: 'reviewBody', contentProperty: 'reviewBody' }],
            requiredProperties: ['reviewBody'],
          },
        ],
      }),
    });
    await waitForLoad(el);

    const review = rowOf(el, 'reviewItem');
    expect(review.mapped).to.equal(true);
    expect(review.nestedSchemaType).to.equal('Review');
    expect(review.requiredProperties).to.deep.equal(['reviewBody']);
    const body = review.propertyMappings.find((m: any) => m.schemaProperty === 'reviewBody');
    expect(body.contentProperty).to.equal('reviewBody');

    // Only the routed block is mapped — no wildcard here.
    expect(rowOf(el, 'promoBanner').mapped).to.equal(false);
  });

  it('seeds every block row from a bare nestedSchemaTypeName with an honest empty table', async () => {
    const el = createElement({ existingConfig: null, nestedSchemaTypeName: 'Review' });
    await waitForLoad(el);

    for (const alias of ['reviewItem', 'promoBanner']) {
      const row = rowOf(el, alias);
      expect(row.mapped, `${alias} mapped`).to.equal(true);
      expect(row.nestedSchemaType).to.equal('Review');
      // The renderer auto-maps by name today — shown honestly as an empty table.
      expect(row.propertyMappings.length).to.be.greaterThan(0);
      expect(row.propertyMappings.every((m: any) => m.contentProperty === '')).to.equal(true);
    }
  });

  it('stringList configs show a read-only notice and round-trip VERBATIM', async () => {
    const config = '{"extractAs":"stringList","contentProperty":"ingredientName","transformType":"trim"}';
    const el = createElement({ existingConfig: config });
    await waitForLoad(el);

    const notice = el.shadowRoot!.querySelector('.string-list-notice');
    expect(notice, 'string-list notice rendered').to.exist;
    expect(notice!.textContent).to.contain('ingredientName');
    expect(el.shadowRoot!.querySelectorAll('.block-row').length).to.equal(0);

    const captured = captureValue(el);
    el._handleSave();
    expect(captured.value.resolverConfig).to.equal(config);
    expect(captured.value.additionalTargets).to.deep.equal([]);
  });

  it('a no-change open + save returns existingConfig byte-identical', async () => {
    // Deliberately quirky formatting — byte fidelity means we return this exact string.
    const quirky =
      '{ "nestedMappings": [ {"schemaProperty":"author","contentProperty":"reviewAuthor"} ],  "wrapInListItem": true }';
    const el = createElement({ existingConfig: quirky, nestedSchemaTypeName: 'Review' });
    await waitForLoad(el);

    // Pure UI interactions must not count as changes.
    el._toggleExpand(0);
    await el.updateComplete;
    el._toggleShowAll(0);
    await el.updateComplete;

    const captured = captureValue(el);
    el._handleSave();
    expect(captured.value.resolverConfig).to.equal(quirky);
    expect(captured.value.additionalTargets).to.deep.equal([]);
  });

  it('a dirty save upgrades to the routes shape, preserving root extras and dropping nestedMappings', async () => {
    const legacyWithExtras = JSON.stringify({
      nestedMappings: [
        { schemaProperty: 'author', contentProperty: 'reviewAuthor' },
        { schemaProperty: 'reviewBody', contentProperty: 'reviewBody' },
      ],
      wrapInListItem: true,
      positionProperty: 'sortOrder',
      requiredProperties: ['author'],
    });
    const el = createElement({ existingConfig: legacyWithExtras, nestedSchemaTypeName: 'Review' });
    await waitForLoad(el);

    const reviewIdx = el._blockRows.findIndex((r: any) => r.alias === 'reviewItem');
    const dateIdx = el._blockRows[reviewIdx].propertyMappings.findIndex(
      (m: any) => m.schemaProperty === 'datePublished',
    );
    el._handleContentPropertyChange(reviewIdx, dateIdx, 'reviewDate');
    await el.updateComplete;

    const captured = captureValue(el);
    el._handleSave();

    const parsed = JSON.parse(captured.value.resolverConfig);
    expect(parsed.nestedMappings, 'legacy shape dropped on real edit').to.equal(undefined);
    expect(parsed.wrapInListItem).to.equal(true);
    expect(parsed.positionProperty).to.equal('sortOrder');
    expect(parsed.requiredProperties).to.deep.equal(['author']);
    expect(Array.isArray(parsed.routes)).to.equal(true);

    const reviewRoute = parsed.routes.find((r: any) => r.blockAlias === 'reviewItem');
    expect(reviewRoute.nestedSchemaType).to.equal('Review');
    const props = reviewRoute.propertyMappings.map((p: any) => p.schemaProperty);
    expect(props).to.include('author');
    expect(props).to.include('datePublished');
    const date = reviewRoute.propertyMappings.find((p: any) => p.schemaProperty === 'datePublished');
    expect(date.contentProperty).to.equal('reviewDate');

    // The wildcard applied to every block, so the upgrade keeps both routes.
    expect(parsed.routes.some((r: any) => r.blockAlias === 'promoBanner')).to.equal(true);
  });

  it('"Map this block" never defaults hasPart — target is the parent property, type from fit', async () => {
    const el = createElement();
    await waitForLoad(el);

    // promoBanner's suggestion (WPHeader) does NOT fit → falls back to the single
    // accepted object type.
    const promoIdx = el._blockRows.findIndex((r: any) => r.alias === 'promoBanner');
    expect(el._blockRows[promoIdx].mapped).to.equal(false);
    await el._enableRow(promoIdx);
    await el.updateComplete;
    const promo = el._blockRows[promoIdx];
    expect(promo.mapped).to.equal(true);
    expect(promo.targetProperty).to.equal('review');
    expect(promo.targetProperty).to.not.equal('hasPart');
    expect(promo.nestedSchemaType).to.equal('Review');
  });

  it('"Map this block" seeds a FITTING suggestion type; empty when nothing fits a broad range', async () => {
    const el = createElement({
      contentTypeAlias: 'homePage',
      contentTypePropertyAlias: 'contentBlocks',
      schemaPropertyName: 'mainEntity',
      schemaPropertyType: '',
      acceptedTypes: [],
      existingConfig: null,
    });
    await waitForLoad(el);

    // Reset all rows to unmapped so _enableRow does the seeding.
    el._blockRows = el._blockRows.map((r: any) => ({ ...r, mapped: false, nestedSchemaType: '', targetProperty: '' }));
    await el.updateComplete;

    const faqIdx = el._blockRows.findIndex((r: any) => r.alias === 'faqBlock');
    await el._enableRow(faqIdx);
    expect(el._blockRows[faqIdx].nestedSchemaType).to.equal('Question');
    expect(el._blockRows[faqIdx].targetProperty).to.equal('mainEntity');

    // No suggestion + broad accepted types → no guessed type, never 'hasPart'.
    const richIdx = el._blockRows.findIndex((r: any) => r.alias === 'richTextBlock');
    await el._enableRow(richIdx);
    expect(el._blockRows[richIdx].nestedSchemaType).to.equal('');
    expect(el._blockRows[richIdx].targetProperty).to.equal('mainEntity');
  });

  it('auto-map applies only fitting routes; off-target routes fan out via the explicit button', async () => {
    const el = createElement();
    await waitForLoad(el);

    // Fresh seed already applied the fitting route and hinted the off-target one.
    expect(rowOf(el, 'reviewItem').mapped).to.equal(true);
    expect(rowOf(el, 'reviewItem').nestedSchemaType).to.equal('Review');
    expect(rowOf(el, 'promoBanner').mapped).to.equal(false);
    expect(rowOf(el, 'promoBanner').suggestedTarget).to.equal('hasPart');
    const promoRowEl = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-row:promoBanner"]');
    expect(promoRowEl!.textContent).to.contain('Suggested: WPHeader via hasPart');

    await el._handleAutoMapAll();
    await el.updateComplete;

    // Never applied to this row — offered as an explicit fan-out instead.
    expect(rowOf(el, 'promoBanner').mapped).to.equal(false);
    const fanOutButton = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-fanout-create"]');
    expect(fanOutButton, 'fan-out affordance rendered').to.exist;

    (fanOutButton as HTMLElement).click();
    await el.updateComplete;

    const captured = captureValue(el);
    el._handleSave();

    expect(captured.value.additionalTargets).to.have.length(1);
    const target = captured.value.additionalTargets[0];
    expect(target.schemaPropertyName).to.equal('hasPart');
    const targetConfig = JSON.parse(target.resolverConfig);
    expect(targetConfig.routes).to.have.length(1);
    expect(targetConfig.routes[0].blockAlias).to.equal('promoBanner');
    expect(targetConfig.routes[0].nestedSchemaType).to.equal('WPHeader');

    // This row's own config only routes the fitting block.
    const own = JSON.parse(captured.value.resolverConfig);
    expect(own.routes.map((r: any) => r.blockAlias)).to.deep.equal(['reviewItem']);
  });

  it('without the fan-out opt-in, off-target routes are NOT emitted', async () => {
    const el = createElement();
    await waitForLoad(el);
    await el._handleAutoMapAll();
    await el.updateComplete;

    const captured = captureValue(el);
    el._handleSave();
    expect(captured.value.additionalTargets).to.deep.equal([]);
  });

  it('renders read-only sibling-claim tags and keeps "Map this block" available', async () => {
    const el = createElement({
      siblingClaims: [{ schemaPropertyName: 'hasPart', blockAliases: ['promoBanner'] }],
    });
    await waitForLoad(el);

    expect(rowOf(el, 'promoBanner').claimedBy).to.deep.equal(['hasPart']);
    const promoRowEl = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-row:promoBanner"]')!;
    expect(promoRowEl.textContent).to.contain('Mapped via hasPart');
    // Explicit fan-out is legitimate — opting the block in stays possible.
    expect(promoRowEl.querySelector('.map-block-btn')).to.exist;
  });

  it('constrains the per-block type picker to the accepted types with an "Other type…" escape hatch', async () => {
    const el = createElement();
    await waitForLoad(el);

    const reviewRowEl = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-row:reviewItem"]')!;
    const select = reviewRowEl.querySelector('.schema-type-select') as any;
    expect(select, 'constrained select rendered').to.exist;
    const values = select.options.map((o: any) => o.value);
    expect(values).to.include('Review');
    expect(values).to.include('__schemeweaver-other-type__');
    expect(reviewRowEl.querySelector('schemeweaver-schema-type-input')).to.equal(null);

    // Choosing "Other type…" swaps to the free searchable input.
    const reviewIdx = el._blockRows.findIndex((r: any) => r.alias === 'reviewItem');
    await el._handleTypeSelectChange(reviewIdx, '__schemeweaver-other-type__');
    await el.updateComplete;
    const reviewRowEl2 = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-row:reviewItem"]')!;
    expect(reviewRowEl2.querySelector('schemeweaver-schema-type-input')).to.exist;
  });

  it('editing a block property mapping updates state', async () => {
    const el = createElement();
    await waitForLoad(el);
    const reviewIdx = el._blockRows.findIndex((r: any) => r.alias === 'reviewItem');

    el._handleContentPropertyChange(reviewIdx, 0, 'reviewBody');
    await el.updateComplete;

    expect(el._blockRows[reviewIdx].propertyMappings[0].contentProperty).to.equal('reviewBody');
  });

  it('has E2E hooks on the save and auto-map-all actions', async () => {
    const el = createElement();
    await waitForLoad(el);
    expect(el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-modal-save"]')).to.exist;
    expect(el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-automap-all"]')).to.exist;
  });

  // companyPage.blocks is a NESTED block list: a Company block whose `member`
  // property is itself a Block List of Member Card blocks (→ Person).
  describe('nested blocks', () => {
    function createCompanyElement(): any {
      return createElement({
        contentTypeAlias: 'companyPage',
        contentTypePropertyAlias: 'blocks',
        schemaPropertyName: 'mainEntity',
        schemaPropertyType: 'Thing',
        acceptedTypes: [],
        existingConfig: null,
      });
    }

    function companyRow(el: any) {
      return el._blockRows.find((r: any) => r.alias === 'companyBlock');
    }

    it('seeds nested routes from the block-suggest response', async () => {
      const el = createCompanyElement();
      await waitForLoad(el);

      const company = companyRow(el);
      expect(company.mapped).to.equal(true);
      expect(company.targetProperty).to.equal('mainEntity');
      expect(company.nestedSchemaType).to.equal('Organization');

      const member = company.propertyMappings.find((m: any) => m.schemaProperty === 'member');
      expect(member, 'member mapping exists').to.not.equal(undefined);
      expect(member.contentProperty).to.equal('member');
      // The `member` block property is itself a block list → nested routing is offered.
      expect(member.nestedBlockElementTypes.length).to.equal(1);
      expect(member.nestedBlockElementTypes[0].alias).to.equal('memberBlock');
      // Nested routes were suggested: memberBlock → Person.
      expect(member.nestedRoutes.length).to.equal(1);
      expect(member.nestedRoutes[0].blockAlias).to.equal('memberBlock');
      expect(member.nestedRoutes[0].nestedSchemaType).to.equal('Person');
    });

    it('expands the nested block routing tree into a child editor', async () => {
      const el = createCompanyElement();
      await waitForLoad(el);

      const rowIndex = el._blockRows.findIndex((r: any) => r.alias === 'companyBlock');
      const propIndex = el._blockRows[rowIndex].propertyMappings.findIndex((m: any) => m.schemaProperty === 'member');

      el._toggleExpand(rowIndex);
      await el.updateComplete;
      el._toggleNested(rowIndex, propIndex);
      await el.updateComplete;

      const child = el.shadowRoot!.querySelector('schemeweaver-nested-block-routes');
      expect(child, 'nested editor mounted').to.not.equal(null);
    });

    it('save emits recursive nested routes on the member property mapping', async () => {
      const el = createCompanyElement();
      await waitForLoad(el);

      const captured = captureValue(el);
      el._handleSave();

      const config = JSON.parse(captured.value.resolverConfig);
      expect(config.routes[0].blockAlias).to.equal('companyBlock');
      const member = config.routes[0].propertyMappings.find((p: any) => p.schemaProperty === 'member');
      expect(member, 'member property mapping serialised').to.not.equal(undefined);
      expect(member.routes, 'nested routes serialised').to.not.equal(undefined);
      expect(member.routes[0].blockAlias).to.equal('memberBlock');
      expect(member.routes[0].nestedSchemaType).to.equal('Person');
      const nestedProps = member.routes[0].propertyMappings.map((p: any) => p.schemaProperty);
      expect(nestedProps).to.include('name');
    });

    it('clearing the value of a nested-block property drops its nested routes', async () => {
      const el = createCompanyElement();
      await waitForLoad(el);

      const rowIndex = el._blockRows.findIndex((r: any) => r.alias === 'companyBlock');
      const propIndex = el._blockRows[rowIndex].propertyMappings.findIndex((m: any) => m.schemaProperty === 'member');

      el._handleContentPropertyChange(rowIndex, propIndex, '');
      await el.updateComplete;

      const member = el._blockRows[rowIndex].propertyMappings[propIndex];
      expect(member.nestedBlockElementTypes.length).to.equal(0);
      expect(member.nestedRoutes.length).to.equal(0);
    });
  });
});
