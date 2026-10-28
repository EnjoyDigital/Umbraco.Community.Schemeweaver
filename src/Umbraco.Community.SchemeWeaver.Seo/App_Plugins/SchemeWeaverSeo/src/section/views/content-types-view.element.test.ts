import { expect, fixture, html, waitUntil } from '@open-wc/testing';
import type { SetupWorker } from 'msw/browser';
import { startMockServiceWorker, stopMockServiceWorker } from '../../mocks/setup.js';
import { seoDb } from '../../mocks/data/seo.db.js';
import './content-types-view.element.js';
import type { SeoContentTypesViewElement } from './content-types-view.element.js';
import type { SeoStatusTagElement } from '../../components/status-tag.element.js';

describe('schemeweaver-seo-content-types-view', function () {
  this.timeout(10000);
  let worker: SetupWorker;
  before(async () => {
    worker = await startMockServiceWorker();
  });
  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });
  beforeEach(() => seoDb.reset());

  async function mount() {
    const el = await fixture<SeoContentTypesViewElement>(html`<schemeweaver-seo-content-types-view></schemeweaver-seo-content-types-view>`);
    await waitUntil(() => el.shadowRoot!.querySelector('[data-mark="schemeweaver-seo:content-types-table"]'), 'table never rendered', { timeout: 5000 });
    return el;
  }

  it('lists page types with their status, hiding element and non-routable types by default', async () => {
    const el = await mount();
    const rows = [...el.shadowRoot!.querySelectorAll('uui-table-row[data-mark^="schemeweaver-seo:content-type:"]')].map((r) => r.getAttribute('data-mark'));
    expect(rows).to.include('schemeweaver-seo:content-type:blogArticle');
    expect(rows).to.include('schemeweaver-seo:content-type:homePage');
    expect(rows).to.not.include('schemeweaver-seo:content-type:cta', 'element types are hidden');
    expect(rows).to.not.include('schemeweaver-seo:content-type:seoSettings', 'types without a template, root or pages are hidden');

    const home = el.shadowRoot!.querySelector('[data-mark="schemeweaver-seo:content-type:homePage"]')!;
    expect(home.querySelector<SeoStatusTagElement>('schemeweaver-seo-status-tag')!.status).to.equal('set-up');
    const blog = el.shadowRoot!.querySelector('[data-mark="schemeweaver-seo:content-type:blogArticle"]')!;
    expect(blog.querySelector<SeoStatusTagElement>('schemeweaver-seo-status-tag')!.status).to.equal('not-set-up');
    expect(blog.querySelector('[data-mark="schemeweaver-seo:set-up:blogArticle"]')).to.exist;
  });

  it('shows everything when the filters are switched off', async () => {
    const el = await mount();
    const checkboxes = el.shadowRoot!.querySelectorAll('uui-checkbox');
    for (const box of checkboxes) {
      (box as unknown as HTMLInputElement).checked = false;
      box.dispatchEvent(new Event('change'));
    }
    await el.updateComplete;
    const rows = [...el.shadowRoot!.querySelectorAll('uui-table-row[data-mark^="schemeweaver-seo:content-type:"]')];
    expect(rows.length).to.equal(4);
  });
});
