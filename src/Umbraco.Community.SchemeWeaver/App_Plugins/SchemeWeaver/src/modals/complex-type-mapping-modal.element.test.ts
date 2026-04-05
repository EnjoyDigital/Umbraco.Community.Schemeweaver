import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './complex-type-mapping-modal.element.js';

async function waitForLoad(el: any): Promise<void> {
  await el.updateComplete;
  await waitUntil(
    () => el.shadowRoot && !el.shadowRoot.querySelector('.loading'),
    'Loading did not complete',
    { timeout: 5000 }
  );
  await el.updateComplete;
}

describe('ComplexTypeMappingModalElement', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });

  function createElement(acceptedTypes: string[] = ['Person', 'Organization']): any {
    const el = document.createElement('schemeweaver-complex-type-mapping-modal') as any;
    el.data = {
      schemaPropertyName: 'author',
      acceptedTypes,
      selectedSubType: '',
      contentTypeAlias: 'blogArticle',
      availableProperties: ['authorName', 'authorEmail'],
      existingConfig: null,
    };
    document.body.appendChild(el);
    return el;
  }

  afterEach(() => {
    document.querySelectorAll('schemeweaver-complex-type-mapping-modal').forEach(el => el.remove());
  });

  it('renders with shadow root', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.shadowRoot).to.exist;
  });

  it('has correct tag name', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.tagName.toLowerCase()).to.equal('schemeweaver-complex-type-mapping-modal');
  });

  it('renders wizard step indicators after load', async () => {
    const el = createElement();
    await waitForLoad(el);
    const steps = el.shadowRoot!.querySelectorAll('.step-indicator');
    expect(steps.length).to.equal(3);
  });
});
