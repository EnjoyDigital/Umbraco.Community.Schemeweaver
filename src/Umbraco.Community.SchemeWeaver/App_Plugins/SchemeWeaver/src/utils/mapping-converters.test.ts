import { expect } from '@open-wc/testing';
import type { PropertyMappingDto, PropertyMappingSuggestion } from '../api/types.js';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import { SourceType } from '../constants/source-type.js';
import { sortMappingRows, mergeAutoMapSuggestions, dtoToRow, applySourceTypeChange } from './mapping-converters.js';

/** Helper to create a minimal PropertyMappingDto */
function makeDto(overrides: Partial<PropertyMappingDto> & { schemaPropertyName: string }): PropertyMappingDto {
  return {
    sourceType: SourceType.Property,
    contentTypePropertyAlias: null,
    sourceContentTypeAlias: null,
    transformType: null,
    isAutoMapped: false,
    staticValue: null,
    nestedSchemaTypeName: null,
    resolverConfig: null,
    dynamicRootConfig: null,
    ...overrides,
  };
}

/** Helper to create a minimal PropertyMappingRow */
function makeRow(overrides: Partial<PropertyMappingRow> & { schemaPropertyName: string }): PropertyMappingRow {
  return {
    schemaPropertyType: '',
    sourceType: SourceType.Property,
    contentTypePropertyAlias: '',
    sourceContentTypeAlias: '',
    staticValue: '',
    confidence: null,
    editorAlias: '',
    nestedSchemaTypeName: '',
    resolverConfig: null,
    acceptedTypes: [],
    isComplexType: false,
    expanded: false,
    subMappings: [],
    selectedSubType: '',
    sourceContentTypeProperties: [],
    ...overrides,
  };
}

/** Helper to create a minimal PropertyMappingSuggestion */
function makeSuggestion(overrides: Partial<PropertyMappingSuggestion> & { schemaPropertyName: string }): PropertyMappingSuggestion {
  return {
    schemaPropertyType: null,
    suggestedContentTypePropertyAlias: null,
    suggestedSourceType: SourceType.Property,
    confidence: 0,
    isAutoMapped: true,
    editorAlias: null,
    acceptedTypes: [],
    isComplexType: false,
    ...overrides,
  };
}

describe('sortMappingRows', () => {
  it('places popular properties first in defined order', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'author', contentTypePropertyAlias: 'authorName' }),
      makeRow({ schemaPropertyName: 'name', contentTypePropertyAlias: 'nodeName' }),
      makeRow({ schemaPropertyName: 'description', contentTypePropertyAlias: 'desc' }),
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title' }),
    ];
    const sorted = sortMappingRows(rows);
    expect(sorted[0].schemaPropertyName).to.equal('name');
    expect(sorted[1].schemaPropertyName).to.equal('headline');
    expect(sorted[2].schemaPropertyName).to.equal('description');
    expect(sorted[3].schemaPropertyName).to.equal('author');
  });

  it('places mapped properties before unmapped', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'telephone' }),
      makeRow({ schemaPropertyName: 'address', contentTypePropertyAlias: 'homeAddress' }),
      makeRow({ schemaPropertyName: 'faxNumber' }),
    ];
    const sorted = sortMappingRows(rows);
    expect(sorted[0].schemaPropertyName).to.equal('address');
    expect(sorted[1].schemaPropertyName).to.equal('faxNumber');
    expect(sorted[2].schemaPropertyName).to.equal('telephone');
  });

  it('sorts alphabetically within same group', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'zeta' }),
      makeRow({ schemaPropertyName: 'alpha' }),
      makeRow({ schemaPropertyName: 'mid' }),
    ];
    const sorted = sortMappingRows(rows);
    expect(sorted[0].schemaPropertyName).to.equal('alpha');
    expect(sorted[1].schemaPropertyName).to.equal('mid');
    expect(sorted[2].schemaPropertyName).to.equal('zeta');
  });

  it('considers static value as mapped', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'telephone' }),
      makeRow({ schemaPropertyName: 'faxNumber', staticValue: '123-456' }),
    ];
    const sorted = sortMappingRows(rows);
    expect(sorted[0].schemaPropertyName).to.equal('faxNumber');
    expect(sorted[1].schemaPropertyName).to.equal('telephone');
  });

  it('considers resolverConfig as mapped', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'telephone' }),
      makeRow({ schemaPropertyName: 'mainEntity', resolverConfig: '{"nestedMappings":[]}' }),
    ];
    const sorted = sortMappingRows(rows);
    expect(sorted[0].schemaPropertyName).to.equal('mainEntity');
    expect(sorted[1].schemaPropertyName).to.equal('telephone');
  });

  it('returns empty array for empty input', () => {
    expect(sortMappingRows([])).to.deep.equal([]);
  });

  it('popular + mapped + unmapped full ordering', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'zeta', contentTypePropertyAlias: 'z' }),
      makeRow({ schemaPropertyName: 'alpha' }),
      makeRow({ schemaPropertyName: 'url', contentTypePropertyAlias: '__url' }),
      makeRow({ schemaPropertyName: 'name', contentTypePropertyAlias: '__name' }),
      makeRow({ schemaPropertyName: 'beta', contentTypePropertyAlias: 'b' }),
    ];
    const sorted = sortMappingRows(rows);
    expect(sorted.map(r => r.schemaPropertyName)).to.deep.equal([
      'name',       // popular #1
      'url',        // popular #5
      'beta',       // mapped, alphabetical
      'zeta',       // mapped, alphabetical
      'alpha',      // unmapped
    ]);
  });
});

describe('mergeAutoMapSuggestions', () => {
  it('adds new suggestions as rows', () => {
    const existing: PropertyMappingRow[] = [];
    const suggestions = [
      makeSuggestion({ schemaPropertyName: 'headline', confidence: 90, suggestedContentTypePropertyAlias: 'title' }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(1);
    expect(result[0].schemaPropertyName).to.equal('headline');
    expect(result[0].contentTypePropertyAlias).to.equal('title');
    expect(result[0].confidence).to.equal(90);
  });

  it('preserves existing user mapping and updates confidence', () => {
    const existing = [
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'myCustomTitle', confidence: null }),
    ];
    const suggestions = [
      makeSuggestion({ schemaPropertyName: 'headline', confidence: 85, suggestedContentTypePropertyAlias: 'title' }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(1);
    // User's custom alias preserved
    expect(result[0].contentTypePropertyAlias).to.equal('myCustomTitle');
    // Confidence updated from suggestion
    expect(result[0].confidence).to.equal(85);
  });

  it('replaces existing row without user data', () => {
    const existing = [
      makeRow({ schemaPropertyName: 'headline' }), // no user data
    ];
    const suggestions = [
      makeSuggestion({ schemaPropertyName: 'headline', confidence: 90, suggestedContentTypePropertyAlias: 'title' }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(1);
    expect(result[0].contentTypePropertyAlias).to.equal('title');
    expect(result[0].confidence).to.equal(90);
  });

  it('preserves existing rows not in suggestions', () => {
    const existing = [
      makeRow({ schemaPropertyName: 'author', staticValue: 'John Doe' }),
    ];
    const suggestions = [
      makeSuggestion({ schemaPropertyName: 'headline', confidence: 90, suggestedContentTypePropertyAlias: 'title' }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(2);
    const author = result.find(r => r.schemaPropertyName === 'author');
    expect(author).to.exist;
    expect(author!.staticValue).to.equal('John Doe');
  });

  it('matches schema property names case-insensitively', () => {
    const existing = [
      makeRow({ schemaPropertyName: 'Headline', contentTypePropertyAlias: 'myTitle' }),
    ];
    const suggestions = [
      makeSuggestion({ schemaPropertyName: 'headline', confidence: 80, suggestedContentTypePropertyAlias: 'title' }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(1);
    // User data preserved
    expect(result[0].contentTypePropertyAlias).to.equal('myTitle');
  });

  it('preserves user mapping with resolverConfig', () => {
    const existing = [
      makeRow({ schemaPropertyName: 'mainEntity', resolverConfig: '{"nestedMappings":[{"blockAlias":"faq"}]}' }),
    ];
    const suggestions = [
      makeSuggestion({ schemaPropertyName: 'mainEntity', confidence: 70, suggestedContentTypePropertyAlias: 'faqItems' }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(1);
    expect(result[0].resolverConfig).to.contain('faq');
    expect(result[0].confidence).to.equal(70);
  });

  it('returns sorted results', () => {
    const existing: PropertyMappingRow[] = [];
    const suggestions = [
      makeSuggestion({ schemaPropertyName: 'zeta', confidence: 50, suggestedContentTypePropertyAlias: 'zetaProp' }),
      makeSuggestion({ schemaPropertyName: 'name', confidence: 90, suggestedContentTypePropertyAlias: '__name' }),
      makeSuggestion({ schemaPropertyName: 'alpha', confidence: 30, suggestedContentTypePropertyAlias: 'alphaProp' }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result[0].schemaPropertyName).to.equal('name');
  });

  it('does not add zero-confidence complex type suggestions without property match', () => {
    const existing: PropertyMappingRow[] = [];
    const suggestions = [
      makeSuggestion({
        schemaPropertyName: 'offers',
        confidence: 0,
        isComplexType: true,
        suggestedNestedSchemaTypeName: 'Offer',
      }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(0);
  });

  it('adds complex type suggestions with confidence > 0', () => {
    const existing: PropertyMappingRow[] = [];
    const suggestions = [
      makeSuggestion({
        schemaPropertyName: 'offers',
        confidence: 60,
        isComplexType: true,
        suggestedNestedSchemaTypeName: 'Offer',
        suggestedSourceType: SourceType.ComplexType,
      }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(1);
    expect(result[0].schemaPropertyName).to.equal('offers');
  });

  it('excludes suggestions with no property match and zero confidence', () => {
    const existing: PropertyMappingRow[] = [];
    const suggestions = [
      makeSuggestion({ schemaPropertyName: 'obscureField', confidence: 0 }),
      makeSuggestion({ schemaPropertyName: 'headline', confidence: 80, suggestedContentTypePropertyAlias: 'title' }),
    ];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    expect(result.length).to.equal(1);
    expect(result[0].schemaPropertyName).to.equal('headline');
  });
});

describe('dtoToRow', () => {
  it('parses dynamicRootConfig JSON into an object', () => {
    const dto = makeDto({
      schemaPropertyName: 'author',
      sourceType: SourceType.Parent,
      dynamicRootConfig: '{"originAlias":"Root","querySteps":[]}',
    });
    const row = dtoToRow(dto);
    expect(row.dynamicRootConfig).to.deep.equal({ originAlias: 'Root', querySteps: [] });
  });

  it('handles null dynamicRootConfig as undefined', () => {
    const dto = makeDto({
      schemaPropertyName: 'author',
      dynamicRootConfig: null,
    });
    const row = dtoToRow(dto);
    expect(row.dynamicRootConfig).to.equal(undefined);
  });
});

describe('applySourceTypeChange', () => {
  it('clears dynamicRootConfig when switching to property source type', () => {
    const row: PropertyMappingRow = {
      schemaPropertyName: 'author',
      schemaPropertyType: '',
      sourceType: SourceType.Parent,
      contentTypePropertyAlias: '',
      sourceContentTypeAlias: 'parentDocType',
      staticValue: '',
      confidence: null,
      editorAlias: '',
      nestedSchemaTypeName: '',
      resolverConfig: null,
      acceptedTypes: [],
      isComplexType: false,
      expanded: false,
      subMappings: [],
      selectedSubType: '',
      sourceContentTypeProperties: [],
      dynamicRootConfig: { originAlias: 'Root' },
    };
    const result = applySourceTypeChange(row, SourceType.Property);
    expect(result.dynamicRootConfig).to.equal(undefined);
  });

  it('preserves dynamicRootConfig when switching between related source types', () => {
    const row: PropertyMappingRow = {
      schemaPropertyName: 'author',
      schemaPropertyType: '',
      sourceType: SourceType.Parent,
      contentTypePropertyAlias: '',
      sourceContentTypeAlias: 'parentDocType',
      staticValue: '',
      confidence: null,
      editorAlias: '',
      nestedSchemaTypeName: '',
      resolverConfig: null,
      acceptedTypes: [],
      isComplexType: false,
      expanded: false,
      subMappings: [],
      selectedSubType: '',
      sourceContentTypeProperties: [],
      dynamicRootConfig: { originAlias: 'Root' },
    };
    const result = applySourceTypeChange(row, SourceType.Ancestor);
    expect(result.dynamicRootConfig).to.deep.equal({ originAlias: 'Root' });
  });
});
