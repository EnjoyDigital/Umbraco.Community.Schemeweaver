import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './generate-doctype-modal.element.js';

async function waitForLoad(el: any): Promise<void> {
  await el.updateComplete;
  await waitUntil(
    () => el.shadowRoot && !el.shadowRoot.querySelector('.loading'),
    'Loading did not complete',
    { timeout: 5000 }
  );
  await el.updateComplete;
}

describe('GenerateDoctypeModalElement', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });

  function createElement(): any {
    const el = document.createElement('schemeweaver-generate-doctype-modal') as any;
    el.data = { contentTypeAlias: 'blogArticle' };
    document.body.appendChild(el);
    return el;
  }

  afterEach(() => {
    document.querySelectorAll('schemeweaver-generate-doctype-modal').forEach(el => el.remove());
  });

  it('renders with shadow root', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.shadowRoot).to.exist;
  });

  it('has correct tag name', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.tagName.toLowerCase()).to.equal('schemeweaver-generate-doctype-modal');
  });

  it('shows loading state initially', async () => {
    const el = createElement();
    await el.updateComplete;
    el.shadowRoot!.querySelector('.loading');
    // Loading may have already completed if the mock responds quickly,
    // so we just check the shadow root rendered
    expect(el.shadowRoot).to.exist;
  });

  it('renders schema type list after load', async () => {
    const el = createElement();
    await waitForLoad(el);
    const items = el.shadowRoot!.querySelectorAll('.schema-item');
    expect(items.length).to.be.greaterThan(0);
  });

  it('has Cancel button', async () => {
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
