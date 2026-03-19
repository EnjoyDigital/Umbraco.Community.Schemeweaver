import { expect, fixture, html, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import './schema-picker-modal.element.js';

async function waitForLoad(el: Element): Promise<void> {
  await waitUntil(
    () => !el.shadowRoot!.querySelector('.loading'),
    'Loading did not complete',
    { timeout: 5000 }
  );
}

describe('SchemaPickerModalElement', () => {
  before(async () => {
    await startMockServiceWorker();
  });

  after(() => {
    stopMockServiceWorker();
  });

  it('shows loading spinner while fetching', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    const loader = el.shadowRoot!.querySelector('uui-loader-circle');
    expect(loader).to.exist;
  });

  it('renders schema types after load', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    const items = el.shadowRoot!.querySelectorAll('.schema-item');
    expect(items.length).to.equal(6); // 6 schema types in mock DB
  });

  it('groups schema types by parentTypeName', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    const groups = el.shadowRoot!.querySelectorAll('.schema-group');
    // Parents: CreativeWork (Article, WebPage), Article (BlogPosting), WebPage (FAQPage), Thing (Product, Organization)
    expect(groups.length).to.be.greaterThan(1);
  });

  it('highlights selected type with .selected class', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    const firstItem = el.shadowRoot!.querySelector('.schema-item') as HTMLElement;
    firstItem.click();
    await (el as any).updateComplete;
    expect(firstItem.classList.contains('selected')).to.be.true;
  });

  it('submit button is disabled when no type selected', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    const submitBtn = el.shadowRoot!.querySelector('uui-button[look="primary"]') as any;
    expect(submitBtn.hasAttribute('disabled')).to.be.true;
  });

  it('submit button is enabled after selection', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    const firstItem = el.shadowRoot!.querySelector('.schema-item') as HTMLElement;
    firstItem.click();
    await (el as any).updateComplete;
    const submitBtn = el.shadowRoot!.querySelector('uui-button[look="primary"]') as any;
    expect(submitBtn.hasAttribute('disabled')).to.be.false;
  });
});
