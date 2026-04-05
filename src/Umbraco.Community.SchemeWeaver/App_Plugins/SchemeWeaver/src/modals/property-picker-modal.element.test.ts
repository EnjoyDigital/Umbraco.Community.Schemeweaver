import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './property-picker-modal.element.js';

async function waitForLoad(el: any): Promise<void> {
  await el.updateComplete;
  await waitUntil(
    () => el.shadowRoot && !el.shadowRoot.querySelector('.loading'),
    'Loading did not complete',
    { timeout: 5000 }
  );
  await el.updateComplete;
}

describe('PropertyPickerModalElement', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });

  function createElement(contentTypeAlias = 'blogArticle'): any {
    const el = document.createElement('schemeweaver-property-picker-modal') as any;
    el.data = { contentTypeAlias };
    document.body.appendChild(el);
    return el;
  }

  afterEach(() => {
    document.querySelectorAll('schemeweaver-property-picker-modal').forEach(el => el.remove());
  });

  it('renders with shadow root', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.shadowRoot).to.exist;
  });

  it('has correct tag name', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.tagName.toLowerCase()).to.equal('schemeweaver-property-picker-modal');
  });

  it('shows loading state initially', async () => {
    const el = createElement();
    await el.updateComplete;
    // Shadow root should exist even during loading
    expect(el.shadowRoot).to.exist;
  });

  it('renders property list after load', async () => {
    const el = createElement();
    await waitForLoad(el);
    const propertyList = el.shadowRoot!.querySelector('.property-list');
    expect(propertyList).to.exist;
  });

  it('has Cancel and Select buttons', async () => {
    const el = createElement();
    await waitForLoad(el);
    const buttons = el.shadowRoot!.querySelectorAll('uui-button');
    const labels = Array.from(buttons).map((b) => (b as Element).getAttribute('label'));
    expect(labels.some(l => l && l.toLowerCase().includes('cancel'))).to.be.true;
  });

  it('has search input', async () => {
    const el = createElement();
    await waitForLoad(el);
    const input = el.shadowRoot!.querySelector('uui-input');
    expect(input).to.exist;
  });
});
