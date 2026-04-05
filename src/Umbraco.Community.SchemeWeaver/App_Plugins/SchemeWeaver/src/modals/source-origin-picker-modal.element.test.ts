import { expect, fixture, html } from '@open-wc/testing';
import './source-origin-picker-modal.element.js';

describe('SourceOriginPickerModalElement', () => {
  it('renders with shadow root', async () => {
    const el = await fixture(html`<schemeweaver-source-origin-picker-modal></schemeweaver-source-origin-picker-modal>`);
    expect(el.shadowRoot).to.exist;
  });

  it('has correct tag name', async () => {
    const el = await fixture(html`<schemeweaver-source-origin-picker-modal></schemeweaver-source-origin-picker-modal>`);
    expect(el.tagName.toLowerCase()).to.equal('schemeweaver-source-origin-picker-modal');
  });

  it('renders source origin options', async () => {
    const el = await fixture(html`<schemeweaver-source-origin-picker-modal></schemeweaver-source-origin-picker-modal>`);
    const refItems = el.shadowRoot!.querySelectorAll('umb-ref-item');
    // At minimum, "property" and "static" options are always shown
    expect(refItems.length).to.be.greaterThanOrEqual(2);
  });

  it('has a close button', async () => {
    const el = await fixture(html`<schemeweaver-source-origin-picker-modal></schemeweaver-source-origin-picker-modal>`);
    const buttons = el.shadowRoot!.querySelectorAll('uui-button');
    expect(buttons.length).to.be.greaterThan(0);
  });
});
