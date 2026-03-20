import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './property-mapping-modal.element.js';

async function waitForLoad(el: any): Promise<void> {
  await el.updateComplete;
  await waitUntil(
    () => el.shadowRoot && !el.shadowRoot.querySelector('.loading'),
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
    const saveBtn = el.shadowRoot!.querySelector('uui-button[label="Save Mapping"]');
    const cancelBtn = el.shadowRoot!.querySelector('uui-button[label="Cancel"]');
    expect(saveBtn).to.exist;
    expect(cancelBtn).to.exist;
  });

  it('shows schema type in headline', async () => {
    const el = createElement();
    await waitForLoad(el);
    const headline = el.shadowRoot!.querySelector('umb-body-layout');
    expect(headline).to.exist;
    expect(headline!.getAttribute('headline')).to.contain('Article');
  });
});
