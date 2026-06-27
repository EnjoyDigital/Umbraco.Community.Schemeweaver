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

describe('NestedMappingModalElement (flat block-mapping panel)', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });

  // homePage.contentBlocks is a heterogeneous mock block list:
  //   faqBlock  → Question @ mainEntity
  //   teamBlock → Person   @ hasPart
  //   heroBlock → WPHeader @ hasPart
  //   richTextBlock → SKIP (rich text)
  function createElement(
    contentTypeAlias = 'homePage',
    contentTypePropertyAlias = 'contentBlocks',
    existingMappings: any[] | undefined = undefined,
  ): any {
    const el = document.createElement('schemeweaver-nested-mapping-modal') as any;
    el.data = { contentTypeAlias, contentTypePropertyAlias, existingMappings };
    document.body.appendChild(el);
    return el;
  }

  afterEach(() => {
    document.querySelectorAll('schemeweaver-nested-mapping-modal').forEach((el) => el.remove());
  });

  it('renders one row per block element type', async () => {
    const el = createElement();
    await waitForLoad(el);
    const rows = el.shadowRoot!.querySelectorAll('.block-row');
    expect(rows.length).to.equal(4);
  });

  it('pre-fills mapped rows from the block-suggest response and marks SKIP blocks unmapped', async () => {
    const el = createElement();
    await waitForLoad(el);
    const mapped = el.shadowRoot!.querySelectorAll('.block-row.mapped');
    const unmapped = el.shadowRoot!.querySelectorAll('.block-row.unmapped');
    expect(mapped.length).to.equal(3);
    expect(unmapped.length).to.equal(1);

    // The rich-text block is the SKIP row.
    const richTextRow = el._blockRows.find((r: any) => r.alias === 'richTextBlock');
    expect(richTextRow.mapped).to.equal(false);
  });

  it('routes the dominant FAQ block to mainEntity and the rest to hasPart', async () => {
    const el = createElement();
    await waitForLoad(el);
    const faq = el._blockRows.find((r: any) => r.alias === 'faqBlock');
    const team = el._blockRows.find((r: any) => r.alias === 'teamBlock');
    const hero = el._blockRows.find((r: any) => r.alias === 'heroBlock');
    expect(faq.targetProperty).to.equal('mainEntity');
    expect(faq.nestedSchemaType).to.equal('Question');
    expect(team.targetProperty).to.equal('hasPart');
    expect(team.nestedSchemaType).to.equal('Person');
    expect(hero.targetProperty).to.equal('hasPart');
  });

  it('Auto-map all re-fills every mapped row', async () => {
    const el = createElement();
    await waitForLoad(el);

    // Clear all rows, then auto-map all should restore the suggestions.
    el._blockRows = el._blockRows.map((r: any) => ({ ...r, mapped: false, nestedSchemaType: '', targetProperty: '' }));
    await el.updateComplete;

    await el._handleAutoMapAll();
    await el.updateComplete;

    const faq = el._blockRows.find((r: any) => r.alias === 'faqBlock');
    expect(faq.mapped).to.equal(true);
    expect(faq.nestedSchemaType).to.equal('Question');
    expect(faq.targetProperty).to.equal('mainEntity');
  });

  it('editing a block property mapping updates state', async () => {
    const el = createElement();
    await waitForLoad(el);
    const faqIndex = el._blockRows.findIndex((r: any) => r.alias === 'faqBlock');

    el._handleContentPropertyChange(faqIndex, 0, 'text');
    await el.updateComplete;

    expect(el._blockRows[faqIndex].propertyMappings[0].contentProperty).to.equal('text');
  });

  it('save serialises rows grouped by target property into routed ResolverConfig', async () => {
    const el = createElement();
    await waitForLoad(el);

    const targetMappings = el._buildTargetMappings();
    // Two targets: mainEntity (faqBlock) and hasPart (teamBlock + heroBlock).
    const targets = targetMappings.map((m: any) => m.schemaPropertyName).sort();
    expect(targets).to.deep.equal(['hasPart', 'mainEntity']);

    const mainEntity = targetMappings.find((m: any) => m.schemaPropertyName === 'mainEntity');
    expect(mainEntity.contentTypePropertyAlias).to.equal('contentBlocks');
    const mainConfig = JSON.parse(mainEntity.resolverConfig);
    expect(mainConfig.routes).to.have.length(1);
    expect(mainConfig.routes[0].blockAlias).to.equal('faqBlock');
    expect(mainConfig.routes[0].nestedSchemaType).to.equal('Question');
    // name←name and text←text were auto-mapped; acceptedAnswer was not.
    const mappedSchemaProps = mainConfig.routes[0].propertyMappings.map((p: any) => p.schemaProperty);
    expect(mappedSchemaProps).to.include('name');

    const hasPart = targetMappings.find((m: any) => m.schemaPropertyName === 'hasPart');
    const hasPartConfig = JSON.parse(hasPart.resolverConfig);
    const blockAliases = hasPartConfig.routes.map((r: any) => r.blockAlias).sort();
    expect(blockAliases).to.deep.equal(['heroBlock', 'teamBlock']);
  });

  it('opting a SKIP block in marks it mapped', async () => {
    const el = createElement();
    await waitForLoad(el);
    const richIndex = el._blockRows.findIndex((r: any) => r.alias === 'richTextBlock');
    await el._enableRow(richIndex);
    await el.updateComplete;
    expect(el._blockRows[richIndex].mapped).to.equal(true);
  });

  it('pre-fills from existing saved routed config when re-editing', async () => {
    const existing = [
      {
        schemaPropertyName: 'about',
        nestedSchemaTypeName: null,
        resolverConfig: JSON.stringify({
          routes: [
            {
              blockAlias: 'teamBlock',
              nestedSchemaType: 'Organization',
              propertyMappings: [{ schemaProperty: 'name', contentProperty: 'name' }],
            },
          ],
        }),
      },
    ];
    const el = createElement('homePage', 'contentBlocks', existing);
    await waitForLoad(el);
    const team = el._blockRows.find((r: any) => r.alias === 'teamBlock');
    // Existing config wins over the heuristic suggestion (Person @ hasPart).
    expect(team.targetProperty).to.equal('about');
    expect(team.nestedSchemaType).to.equal('Organization');
  });

  it('shows the block-list property alias in the headline', async () => {
    const el = createElement();
    await waitForLoad(el);
    const headline = el.shadowRoot!.querySelector('umb-body-layout');
    expect(headline!.getAttribute('headline')).to.contain('contentBlocks');
  });

  // companyPage.blocks is a NESTED block list: a Company block whose `member`
  // property is itself a Block List of Member Card blocks (→ Person).
  describe('nested blocks', () => {
    function companyRow(el: any) {
      return el._blockRows.find((r: any) => r.alias === 'companyBlock');
    }

    it('seeds nested routes from the block-suggest response', async () => {
      const el = createElement('companyPage', 'blocks');
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
      const el = createElement('companyPage', 'blocks');
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
      const el = createElement('companyPage', 'blocks');
      await waitForLoad(el);

      const targetMappings = el._buildTargetMappings();
      const mainEntity = targetMappings.find((m: any) => m.schemaPropertyName === 'mainEntity');
      expect(mainEntity, 'mainEntity target emitted').to.not.equal(undefined);

      const config = JSON.parse(mainEntity.resolverConfig);
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
      const el = createElement('companyPage', 'blocks');
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
