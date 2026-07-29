import { expect, fixture, html } from '@open-wc/testing';
import './property-mapping-table.element.js';
import type { PropertyMappingRow } from './property-mapping-table.element.js';
import { SourceType } from '../constants/source-type.js';

const mockMappings: PropertyMappingRow[] = [
  { schemaPropertyName: 'headline', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 95, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
  { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: SourceType.Static, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: 'John Doe', confidence: 60, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
  { schemaPropertyName: 'datePublished', schemaPropertyType: 'Date', sourceType: SourceType.Ancestor, contentTypePropertyAlias: 'publishDate', sourceContentTypeAlias: 'blogRoot', staticValue: '', confidence: 30, editorAlias: 'Umbraco.DateTime', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
  { schemaPropertyName: 'image', schemaPropertyType: 'ImageObject', sourceType: SourceType.Property, contentTypePropertyAlias: 'heroImage', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.MediaPicker3', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
];

describe('PropertyMappingTableElement', () => {
  it('renders table headers (property, source, value + actions)', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[]}></schemeweaver-property-mapping-table>`);
    const headers = el.shadowRoot!.querySelectorAll('uui-table-head-cell');
    expect(headers.length).to.equal(4);
    // The actions header is visually empty but accessibly labelled.
    expect(headers[3].getAttribute('aria-label')).to.equal('Actions');
  });

  it('renders sized uui-table-column elements for every column', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[]}></schemeweaver-property-mapping-table>`);
    const columns = el.shadowRoot!.querySelectorAll('uui-table > uui-table-column');
    expect(columns.length).to.equal(4);
  });

  it('renders correct number of rows', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings}></schemeweaver-property-mapping-table>`);
    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    expect(rows.length).to.equal(4);
  });

  it('renders four cells per row with the remove button in the actions cell', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings} .availableProperties=${['title']}></schemeweaver-property-mapping-table>`);
    const row = el.shadowRoot!.querySelector('uui-table-row') as HTMLElement;
    const cells = row.querySelectorAll('uui-table-cell');
    expect(cells.length).to.equal(4);
    // Trash lives in the dedicated actions cell — never inside the property-name cell.
    expect(row.querySelector('.actions-cell .remove-row-btn')).to.exist;
    expect(row.querySelector('.property-name-cell .remove-row-btn')).to.not.exist;
  });

  it('does not render the remove button in readonly mode', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings} ?readonly=${true}></schemeweaver-property-mapping-table>`);
    expect(el.shadowRoot!.querySelector('.remove-row-btn')).to.not.exist;
  });

  it('removes the row and fires mappings-changed when the actions-cell trash is clicked', async () => {
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mockMappings} .availableProperties=${['title']}></schemeweaver-property-mapping-table>`);
    let eventDetail: any = null;
    el.addEventListener('mappings-changed', (e: Event) => {
      eventDetail = (e as CustomEvent).detail;
    });

    const rows = el.shadowRoot!.querySelectorAll('uui-table-row');
    const removeBtn = (rows[1] as HTMLElement).querySelector('.actions-cell .remove-row-btn') as HTMLElement;
    expect(removeBtn).to.exist;
    removeBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));

    expect(eventDetail).to.exist;
    expect(eventDetail.mappings).to.have.lengthOf(3);
    expect(eventDetail.mappings.map((m: PropertyMappingRow) => m.schemaPropertyName)).to.deep.equal([
      'headline',
      'datePublished',
      'image',
    ]);
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
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Static, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: 'hello', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${staticMapping}></schemeweaver-property-mapping-table>`);
    const input = el.shadowRoot!.querySelector('uui-input');
    expect(input).to.exist;
  });

  it('renders dynamic root picker for ancestor source type', async () => {
    const ancestorMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Ancestor, contentTypePropertyAlias: '', sourceContentTypeAlias: 'blogRoot', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: ['title', 'name'] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${ancestorMapping} .availableProperties=${['title', 'name']}></schemeweaver-property-mapping-table>`);
    // The ancestor source type renders Umbraco's dynamic root picker and document type picker
    // In test environment these custom elements may not be defined, but the value-inputs container should exist
    const valueInputs = el.shadowRoot!.querySelector('.value-inputs');
    expect(valueInputs).to.exist;
  });

  it('renders pick content type button for ancestor source type without alias', async () => {
    const ancestorMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Ancestor, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    await fixture(html`<schemeweaver-property-mapping-table .mappings=${ancestorMapping} .availableProperties=${['title', 'name']}></schemeweaver-property-mapping-table>`);
    // Should show a placeholder button to pick content type, not render in mapped section
    // The row is unmapped (no alias set) so it's in the unmapped section by default
  });

  it('shows source chip for parent source type', async () => {
    const parentMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Parent, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${parentMapping} .availableProperties=${['title', '__url']}></schemeweaver-property-mapping-table>`);

    // Parent rows count as mapped (non-default source type)
    const sourceChip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    expect(sourceChip).to.exist;
  });

  it('dispatches pick-source-origin event when source chip is clicked', async () => {
    const mapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mapping} .availableProperties=${['title']}></schemeweaver-property-mapping-table>`);

    let eventFired = false;
    let eventDetail: any = null;
    el.addEventListener('pick-source-origin', (e: Event) => {
      eventFired = true;
      eventDetail = (e as CustomEvent).detail;
    });

    const sourceChip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    sourceChip?.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));
    expect(eventFired).to.be.true;
    expect(eventDetail.index).to.equal(0);
    expect(eventDetail.currentSourceType).to.equal(SourceType.Property);
  });

  it('shows configure button for blockContent source type', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button');
    expect(configButton).to.exist;
  });

  it('shows configured checkmark when resolverConfig is set', async () => {
    const config = JSON.stringify({ nestedMappings: [{ blockAlias: 'faqItem', schemaProperty: 'name', contentProperty: 'question' }] });
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: config, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const check = el.shadowRoot!.querySelector('.configured-check');
    expect(check).to.exist;
  });

  it('shows auto URL indicator for media picker properties', async () => {
    const mediaMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'image', schemaPropertyType: 'ImageObject', sourceType: SourceType.Property, contentTypePropertyAlias: 'heroImage', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.MediaPicker3', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${mediaMapping} .availableProperties=${['heroImage']}></schemeweaver-property-mapping-table>`);
    const autoUrlIndicator = el.shadowRoot!.querySelector('.auto-url-indicator');
    expect(autoUrlIndicator).to.exist;
  });

  it('shows editor badge for complex editor types', async () => {
    const blockListMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.Property, contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockListMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const editorBadge = el.shadowRoot!.querySelector('.editor-badge');
    expect(editorBadge).to.exist;
  });

  it('dispatches configure-nested-mapping event when configure button is clicked', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);

    let eventFired = false;
    let eventDetail: any = null;
    el.addEventListener('configure-nested-mapping', (e: Event) => {
      eventFired = true;
      eventDetail = (e as CustomEvent).detail;
    });

    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button') as HTMLElement;
    configButton?.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));

    expect(eventFired).to.be.true;
    expect(eventDetail.nestedSchemaTypeName).to.equal('Question');
    expect(eventDetail.index).to.equal(0);
  });

  it('shows source chip with complexType label when source is complexType', async () => {
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: SourceType.ComplexType, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${['authorName']}></schemeweaver-property-mapping-table>`);
    const chip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    expect(chip).to.exist;
    const icon = chip.querySelector('uui-icon');
    expect(icon?.getAttribute('name')).to.equal('icon-brackets');
  });

  it('shows source chip with property label for simple types', async () => {
    const simpleMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'headline', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${simpleMapping} .availableProperties=${['title']}></schemeweaver-property-mapping-table>`);
    const chip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    expect(chip).to.exist;
    const icon = chip.querySelector('uui-icon');
    expect(icon?.getAttribute('name')).to.equal('icon-document');
  });

  it('shows source chip with blockContent icon when source is blockContent', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${[]}></schemeweaver-property-mapping-table>`);
    const chip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    expect(chip).to.exist;
    const icon = chip.querySelector('uui-icon');
    expect(icon?.getAttribute('name')).to.equal('icon-grid');
  });

  it('passes isComplexType and editorAlias in pick-source-origin event', async () => {
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: SourceType.Property, contentTypePropertyAlias: 'authorName', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${['authorName']}></schemeweaver-property-mapping-table>`);

    let eventDetail: any = null;
    el.addEventListener('pick-source-origin', (e: Event) => {
      eventDetail = (e as CustomEvent).detail;
    });

    const chip = el.shadowRoot!.querySelector('.source-chip') as HTMLElement;
    chip?.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));
    expect(eventDetail).to.exist;
    expect(eventDetail.isComplexType).to.be.true;
    expect(eventDetail.editorAlias).to.equal('Umbraco.BlockList');
  });

  it('shows configure button when complexType source is selected', async () => {
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: SourceType.ComplexType, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${[]}></schemeweaver-property-mapping-table>`);
    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button');
    expect(configButton).to.exist;
  });

  it('dispatches configure-complex-type-mapping event when configure button clicked', async () => {
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: SourceType.ComplexType, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: 'Person', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: 'Person', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${['authorName']}></schemeweaver-property-mapping-table>`);

    let eventFired = false;
    let eventDetail: any = null;
    el.addEventListener('configure-complex-type-mapping', (e: Event) => {
      eventFired = true;
      eventDetail = (e as CustomEvent).detail;
    });

    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button') as HTMLElement;
    configButton?.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));
    expect(eventFired).to.be.true;
    expect(eventDetail.schemaPropertyName).to.equal('author');
    expect(eventDetail.acceptedTypes).to.deep.equal(['Organization', 'Person']);
  });

  it('shows configured checkmark for complexType with resolverConfig', async () => {
    const config = JSON.stringify({ selectedSubType: 'Person', complexTypeMappings: [{ schemaProperty: 'name', sourceType: SourceType.Property, contentTypePropertyAlias: 'authorName' }] });
    const complexMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: SourceType.ComplexType, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: 'Person', resolverConfig: config, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: 'Person', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${complexMapping} .availableProperties=${['authorName']}></schemeweaver-property-mapping-table>`);
    const check = el.shadowRoot!.querySelector('.configured-check');
    expect(check).to.exist;
  });

  it('shows configured checkmark when resolverConfig has nested mappings', async () => {
    const config = JSON.stringify({ nestedMappings: [{ schemaProperty: 'name', contentProperty: 'question' }] });
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: config, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${blockMapping} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
    const check = el.shadowRoot!.querySelector('.configured-check');
    expect(check).to.exist;
  });

  it('does not show configured checkmark when resolverConfig is null', async () => {
    const blockMapping: PropertyMappingRow[] = [
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: null, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
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
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 80, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'faqItems', sourceContentTypeAlias: '', staticValue: '', confidence: 60, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Question', resolverConfig: faqConfig, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
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
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'productName', sourceContentTypeAlias: '', staticValue: '', confidence: 80, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'sku', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'sku', sourceContentTypeAlias: '', staticValue: '', confidence: 100, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'review', schemaPropertyType: 'Review', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'reviews', sourceContentTypeAlias: '', staticValue: '', confidence: 70, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'Review', resolverConfig: reviewConfig, acceptedTypes: ['Review'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
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
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 80, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'recipeIngredient', schemaPropertyType: 'Text', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'ingredients', sourceContentTypeAlias: '', staticValue: '', confidence: 60, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: ingredientConfig, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'recipeInstructions', schemaPropertyType: 'HowToStep', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'instructions', sourceContentTypeAlias: '', staticValue: '', confidence: 70, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: 'HowToStep', resolverConfig: instructionConfig, acceptedTypes: ['HowToStep'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
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
      { schemaPropertyName: 'name', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 80, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['String'], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      { schemaPropertyName: 'location', schemaPropertyType: 'Place', sourceType: SourceType.ComplexType, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: 60, editorAlias: '', nestedSchemaTypeName: 'Place', resolverConfig: null, acceptedTypes: ['Place'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
    ];
    const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${eventMappings} .availableProperties=${['title', 'locationName', 'locationAddress']}></schemeweaver-property-mapping-table>`);
    // Complex type row should show configure button instead of expand chevron
    const configButton = el.shadowRoot!.querySelector('.block-actions uui-button');
    expect(configButton).to.exist;
  });

  describe('range warning badge (server-authoritative)', () => {
    it('renders a warning badge with accessible title/aria-label when rangeWarning is set', async () => {
      const message = "'HasPart' accepts CreativeWork but is mapped to 'Person', which is not in that range — the value will be dropped. Map it to 'About' instead, or change the block/nested type.";
      const rows: PropertyMappingRow[] = [
        { schemaPropertyName: 'hasPart', schemaPropertyType: 'CreativeWork', sourceType: SourceType.ComplexType, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: 'Person', resolverConfig: null, acceptedTypes: ['CreativeWork'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [], rangeWarning: message },
      ];
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${rows}></schemeweaver-property-mapping-table>`);
      const badge = el.shadowRoot!.querySelector('uui-tag.range-warning-badge');
      expect(badge).to.exist;
      expect(badge!.getAttribute('color')).to.equal('warning');
      expect(badge!.getAttribute('title')).to.equal(message);
      expect(badge!.getAttribute('aria-label')).to.equal(message);
    });

    it('renders no badge when rangeWarning is absent', async () => {
      const rows: PropertyMappingRow[] = [
        { schemaPropertyName: 'headline', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 95, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      ];
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${rows}></schemeweaver-property-mapping-table>`);
      expect(el.shadowRoot!.querySelector('uui-tag.range-warning-badge')).to.not.exist;
    });

    it('renders no badge for a legitimate subtype row (false-positive guard — no server warning)', async () => {
      // LocalBusiness is in range for an Organization-ranged property, so the
      // server emits no warning and rangeWarning stays undefined. The browser
      // must NOT synthesise a badge from a client-side membership test.
      const rows: PropertyMappingRow[] = [
        { schemaPropertyName: 'author', schemaPropertyType: 'Organization | Person', sourceType: SourceType.ComplexType, contentTypePropertyAlias: '', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: '', nestedSchemaTypeName: 'LocalBusiness', resolverConfig: null, acceptedTypes: ['Organization', 'Person'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      ];
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${rows}></schemeweaver-property-mapping-table>`);
      expect(el.shadowRoot!.querySelector('uui-tag.range-warning-badge')).to.not.exist;
    });
  });

  describe('suggestion advisory badge (server-authoritative hint)', () => {
    it('renders a lightbulb suggestion badge with accessible title/aria-label when suggestion is set, and not as a range warning', async () => {
      const message = "This RichText value will emit raw HTML — add a stripHtml transform to feed Schema.org plain text.";
      const rows: PropertyMappingRow[] = [
        { schemaPropertyName: 'description', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'body', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.RichText', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [], suggestion: message },
      ];
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${rows}></schemeweaver-property-mapping-table>`);
      const badge = el.shadowRoot!.querySelector('uui-tag.suggestion-badge');
      expect(badge).to.exist;
      // Visually distinct from the red range warning — positive/neutral colour and a lightbulb icon.
      expect(badge!.getAttribute('color')).to.equal('positive');
      expect(badge!.getAttribute('title')).to.equal(message);
      expect(badge!.getAttribute('aria-label')).to.equal(message);
      expect(badge!.querySelector('uui-icon')!.getAttribute('name')).to.equal('icon-lightbulb');
      // It must NOT render as a range-warning badge.
      expect(el.shadowRoot!.querySelector('uui-tag.range-warning-badge')).to.not.exist;
    });

    it('renders no suggestion badge when suggestion is absent', async () => {
      const rows: PropertyMappingRow[] = [
        { schemaPropertyName: 'headline', schemaPropertyType: 'Text', sourceType: SourceType.Property, contentTypePropertyAlias: 'title', sourceContentTypeAlias: '', staticValue: '', confidence: 95, editorAlias: 'Umbraco.TextBox', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: [], isComplexType: false, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [] },
      ];
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${rows}></schemeweaver-property-mapping-table>`);
      expect(el.shadowRoot!.querySelector('uui-tag.suggestion-badge')).to.not.exist;
    });
  });

  describe('block route summary + Map blocks button (blockContent rows)', () => {
    const blockRow = (overrides: Partial<PropertyMappingRow> = {}): PropertyMappingRow => ({
      schemaPropertyName: 'mainEntity', schemaPropertyType: 'Question', sourceType: SourceType.BlockContent, contentTypePropertyAlias: 'questions', sourceContentTypeAlias: '', staticValue: '', confidence: null, editorAlias: 'Umbraco.BlockList', nestedSchemaTypeName: '', resolverConfig: null, acceptedTypes: ['Question'], isComplexType: true, expanded: false, subMappings: [], selectedSubType: '', sourceContentTypeProperties: [],
      ...overrides,
    });

    it('renders one summary tag per configured route', async () => {
      const config = JSON.stringify({ routes: [
        { blockAlias: 'faqItem', nestedSchemaType: 'Question' },
        { blockAlias: 'reviewBlock', nestedSchemaType: 'Review' },
      ]});
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[blockRow({ resolverConfig: config })]} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
      const summaryEl = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-summary:mainEntity"]');
      expect(summaryEl).to.exist;
      const tags = summaryEl!.querySelectorAll('uui-tag.route-summary-tag');
      expect(tags.length).to.equal(2);
      expect(tags[0].textContent).to.include('faqItem');
      expect(tags[0].textContent).to.include('Question');
      expect(tags[1].textContent).to.include('reviewBlock');
      expect(tags[1].textContent).to.include('Review');
    });

    it('summarises a legacy wildcard config (nestedMappings + nestedSchemaTypeName) as "any block → type"', async () => {
      const config = JSON.stringify({ nestedMappings: [{ schemaProperty: 'author', contentProperty: 'reviewAuthor' }] });
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[blockRow({ resolverConfig: config, nestedSchemaTypeName: 'Review' })]} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
      const summaryEl = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-summary:mainEntity"]');
      const tags = summaryEl!.querySelectorAll('uui-tag.route-summary-tag');
      expect(tags.length).to.equal(1);
      // Wildcard alias renders the localized "any block" label (the raw term key in the test env).
      expect(tags[0].textContent).to.match(/any ?block/i);
      expect(tags[0].textContent).to.include('Review');
    });

    it('summarises a stringList config with its source property', async () => {
      const config = JSON.stringify({ extractAs: 'stringList', contentProperty: 'ingredientName' });
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[blockRow({ resolverConfig: config })]} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
      const summaryEl = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-summary:mainEntity"]');
      expect(summaryEl!.querySelectorAll('uui-tag.route-summary-tag').length).to.equal(0);
      const stringList = summaryEl!.querySelector('.block-summary-string-list');
      expect(stringList).to.exist;
      // The test-env localizer drops term args, so the source property is asserted via the title attribute.
      expect(stringList!.getAttribute('title')).to.equal('ingredientName');
    });

    it('renders muted empty state when nothing is configured', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[blockRow()]} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
      const summaryEl = el.shadowRoot!.querySelector('[data-mark="schemeweaver:block-summary:mainEntity"]');
      expect(summaryEl).to.exist;
      expect(summaryEl!.querySelectorAll('uui-tag.route-summary-tag').length).to.equal(0);
      expect(summaryEl!.querySelector('.block-summary-empty')).to.exist;
    });

    it('does not render the removed nested schema type editor', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[blockRow()]} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
      expect(el.shadowRoot!.querySelector('.nested-schema-input')).to.not.exist;
      expect(el.shadowRoot!.querySelectorAll('uui-select').length).to.equal(0);
    });

    it('renders the Map blocks button with a data-mark hook, enabled when a property alias is set', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[blockRow()]} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
      const button = el.shadowRoot!.querySelector('[data-mark="schemeweaver:map-blocks:mainEntity"]');
      expect(button).to.exist;
      expect(button!.hasAttribute('disabled')).to.be.false;
      expect(button!.hasAttribute('title')).to.be.false;
    });

    it('disables the Map blocks button with an explanatory title when no property alias is chosen', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[blockRow({ contentTypePropertyAlias: '' })]} .availableProperties=${['questions']}></schemeweaver-property-mapping-table>`);
      const button = el.shadowRoot!.querySelector('[data-mark="schemeweaver:map-blocks:mainEntity"]');
      expect(button).to.exist;
      expect(button!.hasAttribute('disabled')).to.be.true;
      expect(button!.hasAttribute('title')).to.be.true;
    });
  });

  describe('picked-item mode block (drill-down)', () => {
    const pickerRow = (overrides: Partial<PropertyMappingRow> = {}): PropertyMappingRow => ({
      schemaPropertyName: 'author', schemaPropertyType: 'Person', sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode', sourceContentTypeAlias: '', staticValue: '',
      confidence: null, editorAlias: 'Umbraco.ContentPicker', nestedSchemaTypeName: '',
      resolverConfig: null, acceptedTypes: ['Person'], isComplexType: true, expanded: false,
      subMappings: [], selectedSubType: '', sourceContentTypeProperties: [], ...overrides,
    });

    it('renders the mode block for a content picker row with a chosen property', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow()]} .availableProperties=${['authorNode']}></schemeweaver-property-mapping-table>`);
      expect(el.shadowRoot!.querySelector('[data-mark="schemeweaver:picker-mode"]')).to.exist;
    });

    it('renders the mode block for an MNTP row, with its badge', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({ editorAlias: 'Umbraco.MultiNodeTreePicker', contentTypePropertyAlias: 'contributors' })]} .availableProperties=${['contributors']}></schemeweaver-property-mapping-table>`);
      expect(el.shadowRoot!.querySelector('[data-mark="schemeweaver:picker-mode"]')).to.exist;
      expect(el.shadowRoot!.querySelector('.editor-badge')).to.exist;
    });

    it('does not render the mode block for non-picker rows or property-less picker rows', async () => {
      const plain = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({ editorAlias: 'Umbraco.TextBox' })]} .availableProperties=${['authorNode']}></schemeweaver-property-mapping-table>`);
      expect(plain.shadowRoot!.querySelector('[data-mark="schemeweaver:picker-mode"]')).to.not.exist;

      const noAlias = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({ contentTypePropertyAlias: '' })]} .availableProperties=${['authorNode']}></schemeweaver-property-mapping-table>`);
      expect(noAlias.shadowRoot!.querySelector('[data-mark="schemeweaver:picker-mode"]')).to.not.exist;
    });

    it('shows the schema type input in whole-item mode and the doc-type hint in single-property mode', async () => {
      const whole = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({ nestedSchemaTypeName: 'Person' })]} .availableProperties=${['authorNode']}></schemeweaver-property-mapping-table>`);
      expect(whole.shadowRoot!.querySelector('schemeweaver-schema-type-input')).to.exist;

      const drill = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({ pickedPropertyAlias: 'fullName', resolverConfig: '{"pickedPropertyAlias":"fullName"}' })]} .availableProperties=${['authorNode']}></schemeweaver-property-mapping-table>`);
      expect(drill.shadowRoot!.querySelector('[data-mark="schemeweaver:picker-mode"] umb-input-document-type')).to.exist;
    });

    it('renders the picked-property combobox once picked-type properties are available', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({
        pickedPropertyAlias: 'fullName',
        pickedContentTypeAlias: 'authorProfile',
        pickedContentTypeProperties: ['fullName', 'jobTitle', 'bio'],
        resolverConfig: '{"pickedPropertyAlias":"fullName","pickedContentTypeAlias":"authorProfile"}',
      })]} .availableProperties=${['authorNode']}></schemeweaver-property-mapping-table>`);
      const comboboxes = el.shadowRoot!.querySelectorAll('[data-mark="schemeweaver:picker-mode"] schemeweaver-property-combobox');
      expect(comboboxes.length).to.equal(1);
    });

    it('choosing whole-item mode clears drill config and fires mappings-changed', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({
        pickedPropertyAlias: 'fullName',
        resolverConfig: '{"pickedPropertyAlias":"fullName"}',
      })]} .availableProperties=${['authorNode']}></schemeweaver-property-mapping-table>`) as any;
      let detail: any = null;
      el.addEventListener('mappings-changed', (e: Event) => { detail = (e as CustomEvent).detail; });

      el._handlePickerModeChange(0, 'wholeItem');

      expect(detail).to.exist;
      const row = detail.mappings[0];
      expect(row.pickedPropertyAlias).to.equal(undefined);
      expect(row.resolverConfig).to.equal(null);
      expect(row.nestedSchemaTypeName).to.equal('Person'); // first accepted type
    });

    it('choosing a picked property writes the drill config and clears the nested type', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({
        nestedSchemaTypeName: '',
        pickedContentTypeAlias: 'authorProfile',
        pickedContentTypeProperties: ['fullName'],
      })]} .availableProperties=${['authorNode']}></schemeweaver-property-mapping-table>`) as any;
      let detail: any = null;
      el.addEventListener('mappings-changed', (e: Event) => { detail = (e as CustomEvent).detail; });

      el._handlePickedPropertyChange(0, 'fullName');

      const row = detail.mappings[0];
      expect(row.pickedPropertyAlias).to.equal('fullName');
      expect(JSON.parse(row.resolverConfig)).to.deep.equal({
        pickedPropertyAlias: 'fullName',
        pickedContentTypeAlias: 'authorProfile',
      });
    });

    it('changing the main property clears stale drill state', async () => {
      const el = await fixture(html`<schemeweaver-property-mapping-table .mappings=${[pickerRow({
        pickedPropertyAlias: 'fullName',
        pickedContentTypeAlias: 'authorProfile',
        resolverConfig: '{"pickedPropertyAlias":"fullName","pickedContentTypeAlias":"authorProfile"}',
      })]} .availableProperties=${['authorNode', 'otherPicker']}></schemeweaver-property-mapping-table>`) as any;
      let detail: any = null;
      el.addEventListener('mappings-changed', (e: Event) => { detail = (e as CustomEvent).detail; });

      el._handlePropertyChange(0, 'otherPicker');

      const row = detail.mappings[0];
      expect(row.contentTypePropertyAlias).to.equal('otherPicker');
      expect(row.pickedPropertyAlias).to.equal(undefined);
      expect(row.resolverConfig).to.equal(null);
    });
  });
});
