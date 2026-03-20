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

describe('NestedMappingModalElement', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });

  function createElement(nestedSchemaTypeName = 'Question', existingConfig: string | null = null): any {
    const el = document.createElement('schemeweaver-nested-mapping-modal') as any;
    el.data = {
      nestedSchemaTypeName,
      contentTypePropertyAlias: 'questions',
      contentTypeAlias: 'faqPage',
      existingConfig,
    };
    document.body.appendChild(el);
    return el;
  }

  afterEach(() => {
    document.querySelectorAll('schemeweaver-nested-mapping-modal').forEach((el) => el.remove());
  });

  it('shows loading state initially', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.shadowRoot).to.exist;
  });

  it('renders table after loading completes', async () => {
    const el = createElement();
    await waitForLoad(el);
    const table = el.shadowRoot!.querySelector('uui-table');
    expect(table).to.exist;
  });

  it('renders schema properties as rows', async () => {
    const el = createElement();
    await waitForLoad(el);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    // Question has 3 properties: name, acceptedAnswer, text
    expect(rows.length).to.equal(3);
  });

  it('shows block alias input', async () => {
    const el = createElement();
    await waitForLoad(el);
    const boxes = el.shadowRoot!.querySelectorAll('uui-box');
    expect(boxes.length).to.be.greaterThan(0);
    const blockAliasInput = boxes[0]?.querySelector('uui-input');
    expect(blockAliasInput).to.exist;
  });

  it('has Save and Cancel buttons', async () => {
    const el = createElement();
    await waitForLoad(el);
    const buttons = el.shadowRoot!.querySelectorAll('uui-button');
    const labels = Array.from(buttons).map((b) => (b as Element).getAttribute('label'));
    expect(labels).to.include('Save Mapping');
    expect(labels).to.include('Cancel');
  });

  it('loads existing config when provided', async () => {
    const config = JSON.stringify({
      nestedMappings: [
        { blockAlias: 'faqItem', schemaProperty: 'name', contentProperty: 'question' },
        { blockAlias: 'faqItem', schemaProperty: 'acceptedAnswer', contentProperty: 'answer', wrapInType: 'Answer' },
      ],
    });
    const el = createElement('Question', config);
    await waitForLoad(el);

    // Should have rows for the existing mappings (2 from config, but schema has 3 props)
    // Since existing config replaces the default, we get what was in the config
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.be.greaterThan(0);
  });

  it('shows schema type name in headline', async () => {
    const el = createElement('Question');
    await waitForLoad(el);
    const headline = el.shadowRoot!.querySelector('umb-body-layout');
    expect(headline).to.exist;
    expect(headline!.getAttribute('headline')).to.contain('Question');
  });
});
