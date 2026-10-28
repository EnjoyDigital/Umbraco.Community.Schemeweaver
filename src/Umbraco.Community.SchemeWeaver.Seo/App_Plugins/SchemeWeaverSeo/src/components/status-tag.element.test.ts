import { expect, fixture, html } from '@open-wc/testing';
import './status-tag.element.js';
import type { SeoStatusTagElement } from './status-tag.element.js';

describe('schemeweaver-seo-status-tag', () => {
  it('renders a positive tag for set-up', async () => {
    const el = await fixture<SeoStatusTagElement>(html`<schemeweaver-seo-status-tag status="set-up"></schemeweaver-seo-status-tag>`);
    const tag = el.shadowRoot!.querySelector('uui-tag')!;
    expect(tag.getAttribute('color')).to.equal('positive');
    expect(tag.getAttribute('data-mark')).to.equal('schemeweaver-seo:status:set-up');
  });

  it('renders a warning tag for needs-attention and a neutral one for not-set-up', async () => {
    const warn = await fixture<SeoStatusTagElement>(html`<schemeweaver-seo-status-tag status="needs-attention"></schemeweaver-seo-status-tag>`);
    expect(warn.shadowRoot!.querySelector('uui-tag')!.getAttribute('color')).to.equal('warning');
    const none = await fixture<SeoStatusTagElement>(html`<schemeweaver-seo-status-tag status="not-set-up"></schemeweaver-seo-status-tag>`);
    expect(none.shadowRoot!.querySelector('uui-tag')!.getAttribute('look')).to.equal('secondary');
  });
});
