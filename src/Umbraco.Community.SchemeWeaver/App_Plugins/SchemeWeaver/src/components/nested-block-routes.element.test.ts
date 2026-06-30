import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './nested-block-routes.element.js';
import type { NestedBlockRoutesElement } from './nested-block-routes.element.js';
import type { BlockElementTypeInfo } from '../api/types.js';

const FAQ_ITEM: BlockElementTypeInfo = {
  alias: 'faqItem',
  name: 'FAQ Item',
  properties: ['question', 'answer'],
  propertyInfos: [
    { alias: 'question', name: 'Question', editorAlias: 'Umbraco.TextBox' },
    { alias: 'answer', name: 'Answer', editorAlias: 'Umbraco.TextArea' },
  ],
};

async function mount(allowedSchemaTypes: string[] = []): Promise<NestedBlockRoutesElement> {
  const el = document.createElement('schemeweaver-nested-block-routes') as NestedBlockRoutesElement;
  el.blockElementTypes = [FAQ_ITEM];
  el.routes = []; // nothing routed yet — matches a freshly-picked nested block list
  el.suggestedRoutes = [];
  el.allowedSchemaTypes = allowedSchemaTypes;
  document.body.appendChild(el);
  await waitUntil(() => (el as any)._built, 'never built', { timeout: 5000 });
  await el.updateComplete;
  return el;
}

describe('NestedBlockRoutesElement — manual enable + set nested schema type', () => {
  let worker: SetupWorker;
  before(async () => { worker = await startMockServiceWorker(); });
  after(() => { worker.resetHandlers(); stopMockServiceWorker(); });
  afterEach(() => {
    document.querySelectorAll('schemeweaver-nested-block-routes').forEach((el) => el.remove());
  });

  it('starts the block row unmapped', async () => {
    const el = await mount();
    expect((el as any)._rows[0].mapped).to.equal(false);
  });

  it('falls back to the searchable picker when no accepted types are given (broad case)', async () => {
    const el = await mount(); // allowedSchemaTypes = []
    (el as any)._enableRow(0);
    await el.updateComplete;
    expect(el.shadowRoot!.querySelector('schemeweaver-schema-type-input'), 'picker rendered').to.not.equal(null);
    expect(el.shadowRoot!.querySelector('uui-select.schema-type-input'), 'no dropdown').to.equal(null);
  });

  it('renders a constrained dropdown of exactly the allowed types', async () => {
    const el = await mount(['Answer', 'ItemList']);
    (el as any)._enableRow(0);
    await el.updateComplete;

    const select = el.shadowRoot!.querySelector('uui-select.schema-type-input') as any;
    expect(select, 'dropdown rendered').to.not.equal(null);
    expect(el.shadowRoot!.querySelector('schemeweaver-schema-type-input'), 'no free picker').to.equal(null);

    const values = select.options.map((o: any) => o.value);
    expect(values).to.deep.equal(['', 'Answer', 'ItemList']); // placeholder + the two allowed
  });

  it('selecting an allowed type hydrates the property table', async () => {
    const el = await mount(['Answer', 'ItemList']);
    (el as any)._enableRow(0);
    await el.updateComplete;
    await (el as any)._handleSchemaTypeChange(0, 'Answer');
    await el.updateComplete;

    const row = (el as any)._rows[0];
    expect(row.nestedSchemaType).to.equal('Answer');
    expect(row.propertyMappings.length, 'hydrated from Answer props').to.be.greaterThan(0);
    expect(el.value[0].nestedSchemaType).to.equal('Answer');
  });

  it('renders "Wrap in Type" as a constrained dropdown for a complex scalar property', async () => {
    // faqItem mapped to Question → its property table includes AcceptedAnswer (complex, accepts
    // Answer). That row's Wrap in Type must be a dropdown of [Answer], not a free-text box.
    const el = await mount();
    (el as any)._enableRow(0);
    await el.updateComplete;
    await (el as any)._handleSchemaTypeChange(0, 'Question');
    await el.updateComplete;

    const selects = Array.from(el.shadowRoot!.querySelectorAll('uui-select')) as any[];
    const wrap = selects.find((s) => {
      const vals = (s.options ?? []).map((o: any) => o.value);
      return vals.length === 2 && vals[0] === '' && vals[1] === 'Answer';
    });
    expect(wrap, 'wrap-in-type dropdown of [Answer] present').to.not.equal(undefined);
  });

  it('preserves an out-of-list current value as a dropdown option', async () => {
    const el = await mount(['Answer', 'ItemList']);
    (el as any)._enableRow(0);
    // Simulate a previously saved value that is not in the allowed set.
    await (el as any)._handleSchemaTypeChange(0, 'Question');
    await el.updateComplete;

    const select = el.shadowRoot!.querySelector('uui-select.schema-type-input') as any;
    const values = select.options.map((o: any) => o.value);
    expect(values).to.deep.equal(['', 'Question', 'Answer', 'ItemList']);
    expect(select.options.find((o: any) => o.value === 'Question').selected).to.equal(true);
  });

  it('setting the nested schema type hydrates the property table and serialises the route', async () => {
    const el = await mount();
    (el as any)._enableRow(0);
    await el.updateComplete;
    await (el as any)._handleSchemaTypeChange(0, 'Question');
    await el.updateComplete;

    const row = (el as any)._rows[0];
    expect(row.nestedSchemaType).to.equal('Question');
    expect(row.propertyMappings.length, 'table hydrated from Question props').to.be.greaterThan(0);

    expect(el.value.length).to.equal(1);
    expect(el.value[0].blockAlias).to.equal('faqItem');
    expect(el.value[0].nestedSchemaType).to.equal('Question');
  });
});

describe('NestedBlockRoutesElement — auto-map wand', () => {
  let worker: SetupWorker;
  before(async () => { worker = await startMockServiceWorker(); });
  after(() => { worker.resetHandlers(); stopMockServiceWorker(); });
  afterEach(() => {
    document.querySelectorAll('schemeweaver-nested-block-routes').forEach((el) => el.remove());
  });

  async function mountWithSuggestion(
    propertyMappings: Array<{ schemaProperty: string; contentProperty: string; wrapInType?: string | null; wrapInProperty?: string | null }>,
  ): Promise<NestedBlockRoutesElement> {
    const el = document.createElement('schemeweaver-nested-block-routes') as NestedBlockRoutesElement;
    el.blockElementTypes = [FAQ_ITEM];
    el.routes = [];
    el.suggestedRoutes = [{ blockAlias: 'faqItem', nestedSchemaType: 'Question', confidence: 80, propertyMappings }];
    el.allowedSchemaTypes = [];
    document.body.appendChild(el);
    await waitUntil(() => (el as any)._built, 'never built', { timeout: 5000 });
    await el.updateComplete;
    return el;
  }

  it('populates the property table from the suggestion', async () => {
    const el = await mountWithSuggestion([
      { schemaProperty: 'name', contentProperty: 'question' },
      { schemaProperty: 'acceptedAnswer', contentProperty: 'answer', wrapInType: 'Answer', wrapInProperty: 'Text' },
    ]);

    await (el as any)._autoMapRow(0);
    await el.updateComplete;

    const row = (el as any)._rows[0];
    expect(row.mapped, 'row mapped').to.equal(true);
    expect(row.nestedSchemaType).to.equal('Question');
    const mapped = row.propertyMappings.filter((m: any) => m.contentProperty.trim() !== '');
    expect(mapped.length, 'mapped properties seeded').to.be.greaterThan(0);
    expect(el.value[0].propertyMappings.some((m) => m.contentProperty === 'question')).to.equal(true);
  });

  // Regression guard for the old silent `if (!sugg) return` no-op: a hit that resolves to
  // no property mappings must STILL open the table so the wand never looks dead.
  it('still opens the table when the suggestion has no resolvable mappings', async () => {
    const el = await mountWithSuggestion([]);

    await (el as any)._autoMapRow(0);
    await el.updateComplete;

    const row = (el as any)._rows[0];
    expect(row.mapped).to.equal(true);
    expect(row.expanded, 'table opened even with zero mappings').to.equal(true);
  });
});
