import { expect, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SetupWorker } from 'msw/browser';
import { SourceType } from '../constants/source-type.js';
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

  // ── Re-opening a saved config (data-loss regressions) ──────────────────

  function createElementWithExistingConfig(stored: unknown, typeName = 'Organization'): any {
    const el = document.createElement('schemeweaver-complex-type-mapping-modal') as any;
    el.data = {
      schemaPropertyName: 'author',
      acceptedTypes: [typeName],
      selectedSubType: typeName,
      contentTypeAlias: 'blogArticle',
      availableProperties: ['authorName', 'authorEmail'],
      existingConfig: JSON.stringify(stored),
    };
    document.body.appendChild(el);
    return el;
  }

  it('round-trips sub-mapping keys the UI does not model (transformType plus future keys)', async () => {
    const stored = {
      selectedSubType: 'Organization',
      complexTypeMappings: [
        {
          schemaProperty: 'name',
          sourceType: SourceType.Property,
          contentTypePropertyAlias: 'authorName',
          transformType: 'stripHtml',
          futureKeyTheUiDoesNotKnow: { nested: ['a', 'b'], count: 2 },
        },
        {
          schemaProperty: 'email',
          sourceType: SourceType.Static,
          staticValue: 'hello@example.com',
          transformType: 'trim',
        },
        {
          schemaProperty: 'url',
          sourceType: SourceType.Parent,
          sourceContentTypeAlias: 'blogRoot',
          contentTypePropertyAlias: 'siteUrl',
        },
        {
          schemaProperty: 'logo',
          sourceType: SourceType.ComplexType,
          resolverConfig: '{"selectedSubType":"ImageObject","complexTypeMappings":[]}',
        },
      ],
    };

    const el = createElementWithExistingConfig(stored);
    await waitForLoad(el);

    // Round-trip through JSON exactly as `_handleSave` does.
    const rebuilt = JSON.parse(JSON.stringify(el._buildConfig()));
    expect(rebuilt.selectedSubType).to.equal('Organization');
    expect(rebuilt.complexTypeMappings).to.have.lengthOf(4);

    const byProperty = new Map<string, any>(
      rebuilt.complexTypeMappings.map((m: any) => [m.schemaProperty, m]),
    );

    const name = byProperty.get('name');
    expect(name, 'name sub-mapping').to.exist;
    expect(name.sourceType).to.equal(SourceType.Property);
    expect(name.contentTypePropertyAlias).to.equal('authorName');
    expect(name.transformType, 'transformType survives a load → save cycle').to.equal('stripHtml');
    expect(name.futureKeyTheUiDoesNotKnow).to.deep.equal({ nested: ['a', 'b'], count: 2 });

    const email = byProperty.get('email');
    expect(email, 'email sub-mapping').to.exist;
    expect(email.sourceType).to.equal(SourceType.Static);
    expect(email.staticValue).to.equal('hello@example.com');
    expect(email.transformType).to.equal('trim');

    const url = byProperty.get('url');
    expect(url, 'url sub-mapping').to.exist;
    expect(url.sourceType).to.equal(SourceType.Parent);
    expect(url.sourceContentTypeAlias).to.equal('blogRoot');
    expect(url.contentTypePropertyAlias).to.equal('siteUrl');

    const logo = byProperty.get('logo');
    expect(logo, 'logo sub-mapping').to.exist;
    expect(logo.sourceType).to.equal(SourceType.ComplexType);
    expect(logo.resolverConfig).to.equal('{"selectedSubType":"ImageObject","complexTypeMappings":[]}');
  });

  it('enriches a loaded config with schema metadata so nested objects stay re-editable', async () => {
    const el = createElementWithExistingConfig({
      selectedSubType: 'Organization',
      complexTypeMappings: [
        {
          schemaProperty: 'logo',
          sourceType: SourceType.ComplexType,
          resolverConfig: '{"selectedSubType":"ImageObject","complexTypeMappings":[]}',
        },
      ],
    });
    await waitForLoad(el);

    const logo = el._subMappings.find((m: any) => m.schemaProperty === 'logo');
    expect(logo, 'logo sub-mapping').to.exist;
    expect(logo.schemaPropertyType, 'schemaPropertyType').to.equal('ImageObject');
    expect(logo.isComplexType, 'isComplexType').to.equal(true);
    expect(logo.acceptedTypes, 'acceptedTypes').to.include('ImageObject');

    // Without the metadata the value cell falls back to a plain property combobox,
    // which is what made a saved nested object impossible to reopen.
    expect(el.shadowRoot!.querySelector('.block-actions'), 'configure button').to.exist;
  });

  it('retains a loaded sub-mapping whose schema property is absent from the schema-properties response', async () => {
    const el = createElementWithExistingConfig({
      selectedSubType: 'Organization',
      complexTypeMappings: [
        { schemaProperty: 'name', sourceType: SourceType.Property, contentTypePropertyAlias: 'authorName' },
        // `vatID` is not in the mock Organization property list.
        { schemaProperty: 'vatID', sourceType: SourceType.Static, staticValue: 'GB123456789', transformType: 'trim' },
      ],
    });
    await waitForLoad(el);

    // Re-running the merge is exactly what Back → re-pick the same type does.
    await el._loadSubTypeProperties('Organization');
    await el.updateComplete;

    expect(
      el._subMappings.map((m: any) => m.schemaProperty),
      'unmatched sub-mapping retained',
    ).to.include('vatID');

    const rebuilt = el._buildConfig();
    const vat = rebuilt.complexTypeMappings.find((m: any) => m.schemaProperty === 'vatID');
    expect(vat, 'vatID survives the rebuild').to.exist;
    expect(vat.sourceType).to.equal(SourceType.Static);
    expect(vat.staticValue).to.equal('GB123456789');
    expect(vat.transformType).to.equal('trim');
  });
});
