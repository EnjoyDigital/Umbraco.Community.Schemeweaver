import { expect, fixture, html } from '@open-wc/testing';
import './property-mapping-table.element.js';
import type { PropertyMappingRow } from './property-mapping-table.element.js';

const mockMappings: PropertyMappingRow[] = [
  { schemaPropertyName: 'headline', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 95 },
  { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: 'static', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: 'John Doe', confidence: 60 },
  { schemaPropertyName: 'datePublished', schemaPropertyType: 'Date', sourceType: 'ancestor', contentTypePropertyAlias: 'publishDate', sourceContentTypeAlias: 'blogRoot', staticValue: '', confidence: 30 },
  { schemaPropertyName: 'image', schemaPropertyType: 'ImageObject', sourceType: 'property', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null },
];

describe('PropertyMappingTableElement', () => {
  it('renders table headers', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[]}></schemeweaver-property-mapping-table>`);
    const headers = el.shadowRoot!.querySelectorAll('uui-table-head-cell');
    const headerTexts = Array.from(headers).map(h => h.textContent!.trim());
    expect(headerTexts).to.deep.equal(['Schema Property', 'Type', 'Source', 'Value', 'Confidence']);
  });

  it('renders correct number of rows', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(4);
  });

  it('shows High confidence badge for >= 80', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const firstRowBadge = rows[0].querySelector('uui-badge');
    expect(firstRowBadge).to.exist;
    expect(firstRowBadge!.textContent!.trim()).to.equal('High');
    expect(firstRowBadge!.getAttribute('color')).to.equal('positive');
  });

  it('shows Medium confidence badge for >= 50', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const badge = rows[1].querySelector('uui-badge');
    expect(badge).to.exist;
    expect(badge!.textContent!.trim()).to.equal('Medium');
    expect(badge!.getAttribute('color')).to.equal('warning');
  });

  it('shows Low confidence badge for < 50', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const badge = rows[2].querySelector('uui-badge');
    expect(badge).to.exist;
    expect(badge!.textContent!.trim()).to.equal('Low');
    expect(badge!.getAttribute('color')).to.equal('danger');
  });

  it('shows no badge when confidence is null', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const badge = rows[3].querySelector('uui-badge');
    expect(badge).to.not.exist;
  });

  it('renders spans instead of selects in readonly mode', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings} ?readonly=${true}></schemeweaver-property-mapping-table>`);
    const selects = el.shadowRoot!.querySelectorAll('uui-select');
    expect(selects.length).to.equal(0);
    const spans = el.shadowRoot!.querySelectorAll('uui-table-cell span');
    expect(spans.length).to.be.greaterThan(0);
  });

  it('renders uui-input for static source type', async () => {
    const staticMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'static', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: 'hello', confidence: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${staticMapping}></schemeweaver-property-mapping-table>`);
    const input = el.shadowRoot!.querySelector('uui-input');
    expect(input).to.exist;
  });

  it('renders content type alias input for ancestor source type', async () => {
    const ancestorMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'ancestor', contentTypePropertyAlias: '', sourceContentTypeAlias: 'blogRoot', staticValue: '', confidence: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${ancestorMapping} .availableProperties=${['title', 'name']}></schemeweaver-property-mapping-table>`);
    const input = el.shadowRoot!.querySelector('.content-type-input');
    expect(input).to.exist;
  });

  it('dispatches mappings-changed event on source type change', async () => {
    const mapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mapping} .availableProperties=${['title']}></schemeweaver-property-mapping-table>`);

    let eventFired = false;
    el.addEventListener('mappings-changed', () => { eventFired = true; });

    const select = el.shadowRoot!.querySelector('uui-select');
    if (select) {
      (select as any).value = 'static';
      select.dispatchEvent(new Event('change'));
    }
    expect(eventFired).to.be.true;
  });
});
