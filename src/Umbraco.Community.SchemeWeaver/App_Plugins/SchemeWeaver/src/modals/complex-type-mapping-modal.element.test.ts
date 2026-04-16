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

  // ── Popular / Other ranking (Part B banana-for-monkey UX) ──────────────

  function createElementWithSelectedType(typeName = 'Organization'): any {
    const el = document.createElement('schemeweaver-complex-type-mapping-modal') as any;
    el.data = {
      schemaPropertyName: 'author',
      acceptedTypes: [typeName],
      selectedSubType: typeName,
      contentTypeAlias: 'blogArticle',
      availableProperties: ['authorName', 'authorEmail'],
      existingConfig: null,
    };
    document.body.appendChild(el);
    return el;
  }

  it('renders a Popular section for ranked nested properties', async () => {
    const el = createElementWithSelectedType('Organization');
    await waitForLoad(el);
    const header = el.shadowRoot!.querySelector('.section-header');
    expect(header, 'popular section header').to.exist;
    // Localisation in tests returns the raw key — assert on that.
    expect(header!.textContent).to.include('schemeWeaver_popularProperties');
  });

  it('collapses non-popular properties behind a "Show more" disclosure by default', async () => {
    const el = createElementWithSelectedType('Organization');
    await waitForLoad(el);
    const toggle = el.shadowRoot!.querySelector('.disclosure-toggle') as HTMLElement | null;
    expect(toggle, 'disclosure button').to.exist;
    expect(toggle!.getAttribute('label')).to.include('schemeWeaver_showMoreProperties');
    // Only one table visible while collapsed (popular)
    const tables = el.shadowRoot!.querySelectorAll('uui-table');
    expect(tables.length).to.equal(1);
  });

  it('reveals the Other properties table when the disclosure is toggled', async () => {
    const el = createElementWithSelectedType('Organization');
    await waitForLoad(el);
    // Toggle via state — uui-button click propagation is unreliable in @open-wc test env.
    el._showAdditional = true;
    await el.updateComplete;
    const tables = el.shadowRoot!.querySelectorAll('uui-table');
    expect(tables.length, 'both popular + other tables').to.equal(2);
    const toggleAfter = el.shadowRoot!.querySelector('.disclosure-toggle') as HTMLElement;
    expect(toggleAfter.getAttribute('label')).to.include('schemeWeaver_hideAdditionalProperties');
  });

  it('renders the Popular section above the Other disclosure in DOM order', async () => {
    const el = createElementWithSelectedType('Organization');
    await waitForLoad(el);
    const nodes = Array.from(el.shadowRoot!.querySelectorAll('.section-header, .disclosure-wrap')) as Element[];
    expect(nodes.length).to.be.greaterThan(1);
    expect(nodes[0].classList.contains('section-header')).to.equal(true);
  });
});
