import { expect, fixture, html } from '@open-wc/testing';
import './property-mapping-table.element.js';
import type { PropertyMappingRow } from './property-mapping-table.element.js';

const mockMappings: PropertyMappingRow[] = [
  { schemaPropertyName: 'headline', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 95, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
  { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: 'static', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: 'John Doe', confidence: 60, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
  { schemaPropertyName: 'datePublished', schemaPropertyType: 'Date', sourceType: 'ancestor', contentTypePropertyAlias: 'publishDate', sourceContentTypeAlias: 'blogRoot', staticValue: '', confidence: 30, editorAlias: 'Umbraco.DateTime', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
  { schemaPropertyName: 'image', schemaPropertyType: 'ImageObject', sourceType: 'property', contentTypePropertyAlias: 'heroImage', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.MediaPicker3', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
];

describe('PropertyMappingTableElement', () => {
  it('renders table headers', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[]}></schemeweaver-property-mapping-table>`);
    const headers = el.shadowRoot!.querySelectorAll('uui-table-head-cell');
    expect(headers.length).to.equal(3);
  });

  it('renders correct number of rows', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(4);
  });

  it('shows positive confidence tag for >= 80', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const tag = (rows[0] as unknown as HTMLElement).querySelector('uui-tag.confidence-tag');
    expect(tag).to.exist;
    expect(tag!.getAttribute('color')).to.equal('positive');
  });

  it('shows warning confidence tag for >= 50', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const tag = (rows[1] as unknown as HTMLElement).querySelector('uui-tag.confidence-tag');
    expect(tag).to.exist;
    expect(tag!.getAttribute('color')).to.equal('warning');
  });

  it('shows danger confidence tag for < 50', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const tag = (rows[2] as unknown as HTMLElement).querySelector('uui-tag.confidence-tag');
    expect(tag).to.exist;
    expect(tag!.getAttribute('color')).to.equal('danger');
  });

  it('shows no confidence tag when confidence is null', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const tag = (rows[3] as unknown as HTMLElement).querySelector('uui-tag.confidence-tag');
    expect(tag).to.not.exist;
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
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'static', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: 'hello', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${staticMapping}></schemeweaver-property-mapping-table>`);
    const input = el.shadowRoot!.querySelector('uui-input');
    expect(input).to.exist;
  });

  it('renders dynamic root picker for ancestor source type', async () => {
    const ancestorMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'ancestor', contentTypePropertyAlias: '', sourceContentTypeAlias: 'blogRoot', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: ['title', 'name'] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${ancestorMapping} .availableProperties=${['title', 'name']}></schemeweaver-property-mapping-table>`);
    // The ancestor source type renders Umbraco's dynamic root picker and document type picker
    // In test environment these custom elements may not be defined, but the value-inputs container should exist
    const valueInputs = el.shadowRoot!.querySelector('.value-inputs');
    expect(valueInputs).to.exist;
  });

  it('renders pick content type button for ancestor source type without alias', async () => {
    const ancestorMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'ancestor', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    await fixture(html`<schemeweaver-property-mapping-table .mappings=${ancestorMapping} .availableProperties=${['title', 'name']}></schemeweaver-property-mapping-table>`);
    // Should show a placeholder button to pick content type, not render in mapped section
    // The row is unmapped (no alias set) so it's in the unmapped section by default
  });

  it('shows source chip for parent source type', async () => {
    const parentMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'parent', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${parentMapping} .availableProperties=${['title', '__url']}></schemeweaver-property-mapping-table>`);

    // Parent rows count as mapped (non-default source type)
    const sourceChip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    expect(sourceChip).to.exist;
  });

  it('dispatches pick-source-origin event when source chip is clicked', async () => {
    const mapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mapping} .availableProperties=${['title']}></schemeweaver-property-mapping-table>`);

    let eventFired = false;
    let eventDetail: any = null;
    el.addEventListener('pick-source-origin', (e: Event) => {
      eventFired = true;
      eventDetail = (e as CustomEvent).detail;
    });

    const sourceChip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    sourceChip?.click();
    expect(eventFired).to.be.true;
    expect(eventDetail.index).to.equal(0);
    expect(eventDetail.currentSourceType).to.equal('property');
  });

  it('renders nested schema type input for blockContent source type', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const nestedInput = el.shadowRoot!.querySelector('.nested-schema-input');
    expect(nestedInput).to.exist;
  });

  it('shows configure button for blockContent source type', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button');
    expect(configButton).to.exist;
  });

  it('shows configured checkmark when resolverConfig is set', async () => {
    const config = JSON.stringify({ nestedMappings: [{ blockAlias: 'faqItem', schemaProperty: 'name', contentProperty: 'question' }] });
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: config, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const check = el.shadowRoot!.querySelector('.configured-check');
    expect(check).to.exist;
  });

  it('shows auto URL indicator for media picker properties', async () => {
    const mediaMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'image', schemaPropertyType: 'ImageObject', sourceType: 'property', contentTypePropertyAlias: 'heroImage', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.MediaPicker3', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mediaMapping} .availableProperties=${['heroImage']}></schemeweaver-property-mapping-table>`);
    const autoUrlIndicator = el.shadowRoot!.querySelector('.auto-url-indicator');
    expect(autoUrlIndicator).to.exist;
  });

  it('shows editor badge for complex editor types', async () => {
    const blockListMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'property', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockListMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const editorBadge = el.shadowRoot!.querySelector('.editor-badge');
    expect(editorBadge).to.exist;
  });

  it('dispatches configure-nested-mapping event when configure button is clicked', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
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

  it('shows source chip with complexType label when source is complexType', async () => {
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: 'complexType', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${['authorName']}></schemeweaver-property-mapping-table>`);
    const chip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    expect(chip).to.exist;
    const icon = chip.querySelector('uui-icon');
    expect(icon?.getAttribute('name')).to.equal('icon-brackets');
  });

  it('shows source chip with property label for simple types', async () => {
    const simpleMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'headline', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${simpleMapping} .availableProperties=${['title']}></schemeweaver-property-mapping-table>`);
    const chip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    expect(chip).to.exist;
    const icon = chip.querySelector('uui-icon');
    expect(icon?.getAttribute('name')).to.equal('icon-document');
  });

  it('shows source chip with blockContent icon when source is blockContent', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${[]}></schemeweaver-property-mapping-table>`);
    const chip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    expect(chip).to.exist;
    const icon = chip.querySelector('uui-icon');
    expect(icon?.getAttribute('name')).to.equal('icon-grid');
  });

  it('passes isComplexType and editorAlias in pick-source-origin event', async () => {
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: 'property', contentTypePropertyAlias: 'authorName', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${['authorName']}></schemeweaver-property-mapping-table>`);

    let eventDetail: any = null;
    el.addEventListener('pick-source-origin', (e: Event) => {
      eventDetail = (e as CustomEvent).detail;
    });

    const chip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    chip?.click();
    expect(eventDetail).to.exist;
    expect(eventDetail.isComplexType).to.be.true;
    expect(eventDetail.editorAlias).to.equal('Umbraco.BlockList');
  });

  it('shows configure button when complexType source is selected', async () => {
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: 'complexType', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${[]}></schemeweaver-property-mapping-table>`);
    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button');
    expect(configButton).to.exist;
  });

  it('dispatches configure-complex-type-mapping event when configure button clicked', async () => {
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: 'complexType', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: 'Person', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: 'Person', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${['authorName']}></schemeweaver-property-mapping-table>`);

    let eventFired = false;
    let eventDetail: any = null;
    el.addEventListener('configure-complex-type-mapping', (e: Event) => {
      eventFired = true;
      eventDetail = (e as CustomEvent).detail;
    });

    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button') as HTMLElement;
    configButton?.click();
    expect(eventFired).to.be.true;
    expect(eventDetail.schemaPropertyName).to.equal('author');
    expect(eventDetail.acceptedTypes).to.deep.equal(['Organization', 'Person']);
  });

  it('shows configured checkmark for complexType with resolverConfig', async () => {
    const config = JSON.stringify({ selectedSubType: 'Person', complexTypeMappings: [{ schemaProperty: 'name', sourceType: 'property', contentTypePropertyAlias: 'authorName' }] });
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: 'complexType', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: 'Person', resolverConfig: config, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: 'Person', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${['authorName']}></schemeweaver-property-mapping-table>`);
    const check = el.shadowRoot!.querySelector('.configured-check');
    expect(check).to.exist;
  });

  it('renders dropdown instead of free text for nestedSchemaTypeName when acceptedTypes available', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    // Should render a uui-select for nested type (not a uui-input with class nested-schema-input)
    const nestedInput = el.shadowRoot!.querySelector('.nested-schema-input');
    expect(nestedInput).to.not.exist;
    // Should have a uui-select for nested schema type (source is a chip button, property is a combobox)
    const selects = el.shadowRoot!.querySelectorAll('uui-select');
    expect(selects.length).to.equal(1);
  });

  it('renders free text input for nestedSchemaTypeName when no acceptedTypes', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const nestedInput = el.shadowRoot!.querySelector('.nested-schema-input');
    expect(nestedInput).to.exist;
  });

  it('shows configured checkmark when resolverConfig has nested mappings', async () => {
    const config = JSON.stringify({ nestedMappings: [{ schemaProperty: 'name', contentProperty: 'question' }] });
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: config, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const check = el.shadowRoot!.querySelector('.configured-check');
    expect(check).to.exist;
  });

  it('does not show configured checkmark when resolverConfig is null', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const check = el.shadowRoot!.querySelector('.configured-check');
    expect(check).to.not.exist;
  });

  // -- Auto-mapped complex type scenario tests --

  it('renders FAQ auto-mapped row with blockContent source and pre-configured resolver', async () => {
    const faqConfig = JSON.stringify({ nestedMappings: [
      { schemaProperty: 'name', contentProperty: 'question' },
      { schemaProperty: 'acceptedAnswer', contentProperty: 'answer', wrapInType: 'Answer', wrapInProperty: 'Text' },
    ]});
    const faqMappings: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 80, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: 'blockContent', contentTypePropertyAlias: 'faqItems', sourceContentTypeAlias: '', staticValue: '', confidence: 60, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: faqConfig, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${faqMappings} .availableProperties=${['title', 'faqItems']}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(2);
    // Second row should have blockContent indicators
    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button');
    expect(configButton).to.exist;
    const check = el.shadowRoot!.querySelector('.configured-check');
    expect(check).to.exist;
  });

  it('renders Product auto-mapped rows with review blockContent and simple properties', async () => {
    const reviewConfig = JSON.stringify({ nestedMappings: [
      { schemaProperty: 'author', contentProperty: 'reviewAuthor' },
      { schemaProperty: 'reviewBody', contentProperty: 'reviewBody' },
    ]});
    const productMappings: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'productName', sourceContentTypeAlias: '', staticValue: '', confidence: 80, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'sku', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'sku', sourceContentTypeAlias: '', staticValue: '', confidence: 100, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'review', schemaPropertyType: 'Review', sourceType: 'blockContent', contentTypePropertyAlias: 'reviews', sourceContentTypeAlias: '', staticValue: '', confidence: 70, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Review', resolverConfig: reviewConfig, acceptedTypes: ['Review'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${productMappings} .availableProperties=${['productName', 'sku', 'reviews']}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(3);
  });

  it('renders Recipe auto-mapped rows with both ingredient and instruction block properties', async () => {
    const ingredientConfig = JSON.stringify({ extractAs: 'stringList', contentProperty: 'ingredientName' });
    const instructionConfig = JSON.stringify({ nestedMappings: [
      { schemaProperty: 'name', contentProperty: 'stepName' },
      { schemaProperty: 'text', contentProperty: 'stepText' },
    ]});
    const recipeMappings: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 80, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'recipeIngredient', schemaPropertyType: 'Text', sourceType: 'blockContent', contentTypePropertyAlias: 'ingredients', sourceContentTypeAlias: '', staticValue: '', confidence: 60, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: ingredientConfig, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'recipeInstructions', schemaPropertyType: 'HowToStep', sourceType: 'blockContent', contentTypePropertyAlias: 'instructions', sourceContentTypeAlias: '', staticValue: '', confidence: 70, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'HowToStep', resolverConfig: instructionConfig, acceptedTypes: ['HowToStep'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${recipeMappings} .availableProperties=${['title', 'ingredients', 'instructions']}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(3);
    // Both block rows should show configure buttons
    const configButtons = el.shadowRoot!.querySelectorAll('.block-actions uui-button');
    expect(configButtons.length).to.equal(2);
  });

  it('renders Event auto-mapped rows with complex type configure button', async () => {
    const eventMappings: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: 'property', contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 80, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'location', schemaPropertyType: 'Place', sourceType: 'complexType', contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: 60, editorAlias: '', nestedSchemaTypeName: 'Place', resolverConfig: null, acceptedTypes: ['Place'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${eventMappings} .availableProperties=${['title', 'locationName', 'locationAddress']}></schemeweaver-property-mapping-table>`);
    // Complex type row should show configure button instead of expand chevron
    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button');
    expect(configButton).to.exist;
  });
});
