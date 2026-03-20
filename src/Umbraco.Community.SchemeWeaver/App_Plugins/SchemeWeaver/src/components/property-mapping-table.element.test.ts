import { expect, fixture, html } from '@open-wc/testing';
import './property-mapping-table.element.js';
import type { PropertyMappingRow } from './property-mapping-table.element.js';

const mockMappings: PropertyMappingRow[] = [
  { schemaPropertyName: 'headline', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 95, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null },
  { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: 'static', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: 'John Doe', confidence: 60, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null },
  { schemaPropertyName: 'datePublished', schemaPropertyType: 'Date', sourceType: 'ancestor', contentTypePropertyAlias: 'publishDate', sourceContentTypeAlias: 'blogRoot', staticValue: '', confidence: 30, editorAlias: 'Umbraco.DateTime', nestedSchemaTypeName: '', resolverConfig: null },
  { schemaPropertyName: 'image', schemaPropertyType: 'ImageObject', sourceType: 'property', contentTypePropertyAlias: 'heroImage', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.MediaPicker3', nestedSchemaTypeName: '', resolverConfig: null },
];

describe('PropertyMappingTableElement', () => {
  it('renders table headers', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[]}></schemeweaver-property-mapping-table>`);
    const headers = el.shadowRoot!.querySelectorAll('uui-table-head-cell');
    expect(headers.length).to.equal(5);
  });

  it('renders correct number of rows', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(4);
  });

  it('shows positive confidence badge for >= 80', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const firstRowBadge = (rows[0] as unknown as HTMLElement).querySelector('uui-badge');
    expect(firstRowBadge).to.exist;
    expect(firstRowBadge!.getAttribute('color')).to.equal('positive');
  });

  it('shows warning confidence badge for >= 50', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const badge = (rows[1] as unknown as HTMLElement).querySelector('uui-badge');
    expect(badge).to.exist;
    expect(badge!.getAttribute('color')).to.equal('warning');
  });

  it('shows danger confidence badge for < 50', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const badge = (rows[2] as unknown as HTMLElement).querySelector('uui-badge');
    expect(badge).to.exist;
    expect(badge!.getAttribute('color')).to.equal('danger');
  });

  it('shows no badge when confidence is null', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const badge = (rows[3] as unknown as HTMLElement).querySelector('uui-badge');
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
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'static', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: 'hello', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${staticMapping}></schemeweaver-property-mapping-table>`);
    const input = el.shadowRoot!.querySelector('uui-input');
    expect(input).to.exist;
  });

  it('renders content type alias input for ancestor source type', async () => {
    const ancestorMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'ancestor', contentTypePropertyAlias: '', sourceContentTypeAlias: 'blogRoot', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${ancestorMapping} .availableProperties=${['title', 'name']}></schemeweaver-property-mapping-table>`);
    const input = el.shadowRoot!.querySelector('.content-type-input');
    expect(input).to.exist;
  });

  it('dispatches mappings-changed event on source type change', async () => {
    const mapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null },
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

  it('renders nested schema type input for blockContent source type', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const nestedInput = el.shadowRoot!.querySelector('.nested-schema-input');
    expect(nestedInput).to.exist;
  });

  it('shows configure button for blockContent source type', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button');
    expect(configButton).to.exist;
  });

  it('shows nested mapping count badge when resolverConfig is set', async () => {
    const config = JSON.stringify({ nestedMappings: [{ blockAlias: 'faqItem', schemaProperty: 'name', contentProperty: 'question' }] });
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: config },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const badge = el.shadowRoot!.querySelector('.nested-count-badge');
    expect(badge).to.exist;
  });

  it('shows auto URL indicator for media picker properties', async () => {
    const mediaMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'image', schemaPropertyType: 'ImageObject', sourceType: 'property', contentTypePropertyAlias: 'heroImage', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.MediaPicker3', nestedSchemaTypeName: '', resolverConfig: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mediaMapping} .availableProperties=${['heroImage']}></schemeweaver-property-mapping-table>`);
    const autoUrlIndicator = el.shadowRoot!.querySelector('.auto-url-indicator');
    expect(autoUrlIndicator).to.exist;
  });

  it('shows editor badge for complex editor types', async () => {
    const blockListMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'property', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockListMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const editorBadge = el.shadowRoot!.querySelector('.editor-badge');
    expect(editorBadge).to.exist;
  });

  it('dispatches configure-nested-mapping event when configure button is clicked', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);

    let eventFired = false;
    let eventDetail: any = null;
    el.addEventListener('configure-nested-mapping', (e: Event) => {
      eventFired = true;
      eventDetail = (e as CustomEvent).detail;
    });

    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button') as HTMLElement;
    configButton?.click();

    expect(eventFired).to.be.true;
    expect(eventDetail.nestedSchemaTypeName).to.equal('Question');
    expect(eventDetail.index).to.equal(0);
  });
});
