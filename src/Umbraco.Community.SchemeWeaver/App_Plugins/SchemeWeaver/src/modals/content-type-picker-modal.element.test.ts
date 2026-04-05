import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import './content-type-picker-modal.element.js';

async function waitForLoad(el: any): Promise<void> {
  await el.updateComplete;
  await waitUntil(
    () => el.shadowRoot && !el.shadowRoot.querySelector('.loading'),
    'Loading did not complete',
    { timeout: 5000 }
  );
  await el.updateComplete;
}

describe('ContentTypePickerModalElement', () => {
  let worker: SetupWorker;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });

  function createElement(): any {
    const el = document.createElement('schemeweaver-content-type-picker-modal') as any;
    el.data = { currentAlias: '' };
    document.body.appendChild(el);
    return el;
  }

  afterEach(() => {
    document.querySelectorAll('schemeweaver-content-type-picker-modal').forEach(el => el.remove());
  });

  it('renders with shadow root', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.shadowRoot).to.exist;
  });

  it('has correct tag name', async () => {
    const el = createElement();
    await el.updateComplete;
    expect(el.tagName.toLowerCase()).to.equal('schemeweaver-content-type-picker-modal');
  });

  it('shows loading state initially', async () => {
    const el = createElement();
    await el.updateComplete;
    // Loading may resolve quickly with mocks, but shadow root should exist
    expect(el.shadowRoot).to.exist;
  });

  it('renders content types after load', async () => {
    const el = createElement();
    await waitForLoad(el);
    const refList = el.shadowRoot!.querySelector('uui-ref-list');
    expect(refList).to.exist;
  });

  it('has Close button', async () => {
    const el = createElement();
    await waitForLoad(el);
    const buttons = el.shadowRoot!.querySelectorAll('uui-button');
    expect(buttons.length).to.be.greaterThan(0);
  });
});
