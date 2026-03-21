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

  it('renders wizard step indicators', async () => {
    const el = createElement();
    await waitForLoad(el);
    const steps = el.shadowRoot!.querySelectorAll('.step-indicator');
    expect(steps.length).to.equal(3);
  });

  it('auto-selects single block type and shows mappings step', async () => {
    const el = createElement();
    await waitForLoad(el);
    // With only 1 block type (faqItem), wizard auto-advances to mappings step
    const table = el.shadowRoot!.querySelector('uui-table');
    expect(table).to.exist;
    const mappingInfo = el.shadowRoot!.querySelector('.mapping-header-info');
    expect(mappingInfo).to.exist;
  });

  it('renders schema properties as rows', async () => {
    const el = createElement();
    await waitForLoad(el);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    // Question has 3 properties: name, acceptedAnswer, text
    expect(rows.length).to.equal(3);
  });

  it('has Back and Preview buttons on mappings step', async () => {
    const el = createElement();
    await waitForLoad(el);
    const buttons = el.shadowRoot!.querySelectorAll('uui-button');
    const labels = Array.from(buttons).map((b) => (b as Element).getAttribute('label'));
    expect(labels).to.include('Back');
    expect(labels).to.include('Preview');
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

    // Should have rows for the existing mappings (schema has 3 props, merged with config)
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

  it('renders property dropdowns from block element type properties', async () => {
    const el = createElement();
    await waitForLoad(el);
    // Should be on mappings step with dropdowns for block type properties
    const selects = el.shadowRoot!.querySelectorAll('uui-table-row uui-select');
    expect(selects.length).to.be.greaterThan(0);
  });

  it('renders wrap-in type dropdown for complex schema properties', async () => {
    const el = createElement();
    await waitForLoad(el);
    // Question.acceptedAnswer has acceptedTypes: ['Answer'] and isComplexType: true
    // So it should get a wrap-in dropdown
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    // Find the acceptedAnswer row (index 1)
    const acceptedAnswerRow = rows[1];
    if (acceptedAnswerRow) {
      const cells = acceptedAnswerRow.querySelectorAll('uui-table-cell');
      // Last cell should have a uui-select for wrap-in type
      const wrapInSelect = cells[2]?.querySelector('uui-select');
      expect(wrapInSelect).to.exist;
    }
  });

  it('shows block type picker on step 1 when navigating back', async () => {
    const el = createElement();
    await waitForLoad(el);
    // Currently on mappings step. Click Back to go to block-type step
    const backButton = Array.from(el.shadowRoot!.querySelectorAll('uui-button'))
      .find((b: Element) => b.getAttribute('label') === 'Back') as HTMLElement;
    expect(backButton).to.exist;
    backButton?.click();
    await el.updateComplete;
    await new Promise((r) => requestAnimationFrame(r));
    await el.updateComplete;
    // Should show block type picker (either list or fallback input)
    const blockTypeList = el.shadowRoot!.querySelector('.block-type-list');
    const fallbackHint = el.shadowRoot!.querySelector('.no-block-types-hint');
    expect(blockTypeList || fallbackHint).to.exist;
  });

  it('shows preview step with Save button when advancing from mappings', async () => {
    const config = JSON.stringify({
      nestedMappings: [
        { blockAlias: 'faqItem', schemaProperty: 'name', contentProperty: 'question' },
      ],
    });
    const el = createElement('Question', config);
    await waitForLoad(el);
    // Click Preview button
    const previewButton = Array.from(el.shadowRoot!.querySelectorAll('uui-button'))
      .find((b: Element) => b.getAttribute('label') === 'Preview') as HTMLElement;
    expect(previewButton).to.exist;
    previewButton?.click();
    await el.updateComplete;
    await new Promise((r) => requestAnimationFrame(r));
    await el.updateComplete;
    // Should be on preview step with Save button
    const buttons = Array.from(el.shadowRoot!.querySelectorAll('uui-button'));
    const labels = buttons.map((b: Element) => b.getAttribute('label'));
    expect(labels).to.include('Save Mapping');
    // Should show preview content
    const previewSummary = el.shadowRoot!.querySelector('.preview-summary');
    expect(previewSummary).to.exist;
  });

  it('shows JSON preview in collapsible details on preview step', async () => {
    const config = JSON.stringify({
      nestedMappings: [
        { blockAlias: 'faqItem', schemaProperty: 'name', contentProperty: 'question' },
      ],
    });
    const el = createElement('Question', config);
    await waitForLoad(el);
    // Advance to preview
    const previewButton = Array.from(el.shadowRoot!.querySelectorAll('uui-button'))
      .find((b: Element) => b.getAttribute('label') === 'Preview') as HTMLElement;
    previewButton?.click();
    await el.updateComplete;
    await new Promise((r) => requestAnimationFrame(r));
    await el.updateComplete;
    const jsonDetails = el.shadowRoot!.querySelector('.json-details');
    expect(jsonDetails).to.exist;
    const jsonPreview = el.shadowRoot!.querySelector('.json-preview');
    expect(jsonPreview).to.exist;
    expect(jsonPreview!.textContent).to.contain('nestedMappings');
  });

  it('shows fallback input when no block element types are available', async () => {
    // Use a property that has no mock block types
    const el = document.createElement('schemeweaver-nested-mapping-modal') as any;
    el.data = {
      nestedSchemaTypeName: 'Question',
      contentTypePropertyAlias: 'nonexistentProperty',
      contentTypeAlias: 'faqPage',
      existingConfig: null,
    };
    document.body.appendChild(el);
    await waitForLoad(el);
    // Should show step 1 with fallback input (no block types found)
    const hint = el.shadowRoot!.querySelector('.no-block-types-hint');
    expect(hint).to.exist;
    el.remove();
  });
});
