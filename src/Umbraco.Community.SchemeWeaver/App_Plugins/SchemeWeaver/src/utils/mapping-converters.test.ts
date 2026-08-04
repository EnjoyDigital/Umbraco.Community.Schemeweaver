import { expect } from '@open-wc/testing';
import type { PropertyMappingDto, PropertyMappingSuggestion, ValidationIssue } from '../api/types.js';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import { SourceType } from '../constants/source-type.js';
import { sortMappingRows, mergeAutoMapSuggestions, dtoToRow, rowsInPersistenceOrder, applySourceTypeChange, applyWarningsToRows, drillConfigToResolverConfig, isRowConfigured, rowsToPropertyMappingDtos, reconcileRowsForSchemaType, parsePickedComplexConfig, pickedComplexConfigToResolverConfig } from './mapping-converters.js';
import type { RankedSchemaPropertyInfo } from '../api/types.js';

/** Helper to create a minimal RankedSchemaPropertyInfo as the ranked endpoint returns it */
function makeSchemaProp(
  overrides: Partial<RankedSchemaPropertyInfo> & { name: string },
): RankedSchemaPropertyInfo {
  return {
    propertyType: 'Text',
    isRequired: false,
    acceptedTypes: [],
    isComplexType: false,
    confidence: 50,
    isPopular: false,
    ...overrides,
  };
}

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

  it('preserves an existing reference row through an auto-map merge', () => {
    // Regression: reference rows have no property alias/static value/resolver
    // config, so rowHasUserData must count targetPieceKey or the merge treats
    // them as empty placeholders and deletes them (publisher is not in
    // POPULAR_PROPERTIES).
    const existing = [makeRow({
      schemaPropertyName: 'publisher',
      sourceType: SourceType.Reference,
      targetPieceKey: 'organization',
    })];
    const suggestions = [makeSuggestion({
      schemaPropertyName: 'headline',
      suggestedContentTypePropertyAlias: 'title',
      confidence: 90,
    })];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    const publisher = result.find((r) => r.schemaPropertyName === 'publisher');
    expect(publisher).to.exist;
    expect(publisher!.sourceType).to.equal(SourceType.Reference);
    expect(publisher!.targetPieceKey).to.equal('organization');
  });

  it('adds a reference suggestion carrying its target piece key', () => {
    const suggestions = [makeSuggestion({
      schemaPropertyName: 'publisher',
      suggestedSourceType: SourceType.Reference,
      suggestedTargetPieceKey: 'organization',
      confidence: 80,
    })];
    const result = mergeAutoMapSuggestions([], suggestions);
    const publisher = result.find((r) => r.schemaPropertyName === 'publisher');
    expect(publisher).to.exist;
    expect(publisher!.sourceType).to.equal(SourceType.Reference);
    expect(publisher!.targetPieceKey).to.equal('organization');
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

  it('carries targetPieceKey for reference rows', () => {
    const dto = makeDto({
      schemaPropertyName: 'publisher',
      sourceType: SourceType.Reference,
      targetPieceKey: 'organization',
    });
    const row = dtoToRow(dto);
    expect(row.targetPieceKey).to.equal('organization');
  });

  it('parses picker drill-down fields for property rows', () => {
    const dto = makeDto({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      resolverConfig: '{"pickedPropertyAlias":"fullName","pickedContentTypeAlias":"authorProfile"}',
    });
    const row = dtoToRow(dto);
    expect(row.pickedPropertyAlias).to.equal('fullName');
    expect(row.pickedContentTypeAlias).to.equal('authorProfile');
    // The raw config stays on the row untouched — save mappers pass it verbatim.
    expect(row.resolverConfig).to.contain('pickedPropertyAlias');
  });

  it('does not treat complexType or blockContent configs as drill config', () => {
    const complexRow = dtoToRow(makeDto({
      schemaPropertyName: 'author',
      sourceType: SourceType.ComplexType,
      nestedSchemaTypeName: 'Person',
      resolverConfig: '{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"authorName"}]}',
    }));
    expect(complexRow.pickedPropertyAlias).to.equal(undefined);

    const blockRow = dtoToRow(makeDto({
      schemaPropertyName: 'mainEntity',
      sourceType: SourceType.BlockContent,
      contentTypePropertyAlias: 'contentGrid',
      resolverConfig: '{"routes":[{"blockAlias":"hero","nestedSchemaType":"WebPageElement","propertyMappings":[]}]}',
    }));
    expect(blockRow.pickedPropertyAlias).to.equal(undefined);
    expect(blockRow.resolverConfig).to.contain('routes');
  });

  it('ignores malformed drill config without throwing', () => {
    const row = dtoToRow(makeDto({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      resolverConfig: '{not json',
    }));
    expect(row.pickedPropertyAlias).to.equal(undefined);
  });
});

describe('picker drill-down config', () => {
  it('drillConfigToResolverConfig → dtoToRow round-trips both fields', () => {
    const config = drillConfigToResolverConfig('fullName', 'authorProfile');
    const row = dtoToRow(makeDto({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      resolverConfig: config,
    }));
    expect(row.pickedPropertyAlias).to.equal('fullName');
    expect(row.pickedContentTypeAlias).to.equal('authorProfile');
  });

  it('drillConfigToResolverConfig returns null when no alias is set', () => {
    expect(drillConfigToResolverConfig(undefined, 'authorProfile')).to.equal(null);
  });

  it('a drill-configured row survives an auto-map merge (resolverConfig counts as user data)', () => {
    const existing = [makeRow({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      resolverConfig: '{"pickedPropertyAlias":"fullName"}',
      pickedPropertyAlias: 'fullName',
    })];
    const suggestions = [makeSuggestion({
      schemaPropertyName: 'author',
      suggestedContentTypePropertyAlias: 'authorName',
      confidence: 90,
    })];
    const result = mergeAutoMapSuggestions(existing, suggestions);
    const author = result.find((r) => r.schemaPropertyName === 'author');
    expect(author).to.exist;
    expect(author!.contentTypePropertyAlias).to.equal('authorNode');
    expect(author!.pickedPropertyAlias).to.equal('fullName');
  });

  it('applySourceTypeChange clears drill fields', () => {
    const row = makeRow({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      resolverConfig: '{"pickedPropertyAlias":"fullName"}',
      pickedPropertyAlias: 'fullName',
      pickedContentTypeAlias: 'authorProfile',
      pickedContentTypeProperties: ['fullName', 'bio'],
    });
    const result = applySourceTypeChange(row, SourceType.Static);
    expect(result.pickedPropertyAlias).to.equal(undefined);
    expect(result.pickedContentTypeAlias).to.equal(undefined);
    expect(result.pickedContentTypeProperties).to.equal(undefined);
    expect(result.resolverConfig).to.equal(null);
  });

  it('switching a drill row to complexType does NOT carry the drill config across', () => {
    // Regression: resolverConfig used to be preserved for complexType targets
    // unconditionally, so a drilled picker row switched to Complex Type kept
    // {"pickedPropertyAlias":…} masquerading as its complex config — passing
    // the save filter while rendering nothing.
    const row = makeRow({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      resolverConfig: '{"pickedPropertyAlias":"fullName"}',
      pickedPropertyAlias: 'fullName',
    });
    const result = applySourceTypeChange(row, SourceType.ComplexType);
    expect(result.resolverConfig).to.equal(null);
  });

  it('switching between complexType and blockContent still preserves genuine config', () => {
    const row = makeRow({
      schemaPropertyName: 'mainEntity',
      sourceType: SourceType.ComplexType,
      nestedSchemaTypeName: 'WebPageElement',
      resolverConfig: '{"selectedSubType":"WebPageElement","complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"title"}]}',
    });
    const result = applySourceTypeChange(row, SourceType.BlockContent);
    expect(result.resolverConfig).to.contain('complexTypeMappings');
  });
});

// Issue #40 — a fourth picked-item mode: a per-usage inline object built from
// the PICKED node's own properties, nested under `pickedComplexType` so it can
// never be mistaken for a config that resolves against the page.
describe('picked-object config', () => {
  const INNER = '{"selectedSubType":"Person","complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"fullName"}]}';
  const STORED = `{"pickedContentTypeAlias":"authorProfile","pickedComplexType":${INNER}}`;

  it('parsePickedComplexConfig matches the nested key and recovers the picked type alias', () => {
    const parsed = parsePickedComplexConfig(SourceType.Property, STORED);
    expect(parsed).to.exist;
    expect(parsed!.pickedContentTypeAlias).to.equal('authorProfile');
    expect(JSON.parse(parsed!.pickedComplexType)).to.deep.equal(JSON.parse(INNER));
  });

  it('parsePickedComplexConfig rejects a FLAT complexTypeMappings payload', () => {
    // The whole point of the nested key: a flat payload means "resolve against
    // THIS node", so accepting it here would let a source-type switch silently
    // re-base the object onto the page.
    expect(parsePickedComplexConfig(SourceType.Property, INNER)).to.equal(null);
  });

  it('parsePickedComplexConfig rejects non-property rows, empty mappings and malformed JSON', () => {
    expect(parsePickedComplexConfig(SourceType.ComplexType, STORED)).to.equal(null);
    expect(parsePickedComplexConfig(SourceType.BlockContent, STORED)).to.equal(null);
    expect(parsePickedComplexConfig(SourceType.Property, '{"pickedComplexType":{"selectedSubType":"Person","complexTypeMappings":[]}}')).to.equal(null);
    expect(parsePickedComplexConfig(SourceType.Property, '{not json')).to.equal(null);
    expect(parsePickedComplexConfig(SourceType.Property, null)).to.equal(null);
  });

  it('pickedComplexConfigToResolverConfig nests the inner object and keeps the alias top-level', () => {
    const config = pickedComplexConfigToResolverConfig(INNER, 'authorProfile');
    const parsed = JSON.parse(config!);
    expect(parsed.pickedContentTypeAlias).to.equal('authorProfile');
    expect(parsed.complexTypeMappings, 'the inner object must NOT be hoisted').to.equal(undefined);
    expect(parsed.pickedComplexType.selectedSubType).to.equal('Person');
  });

  it('pickedComplexConfigToResolverConfig returns null with nothing to serialise', () => {
    expect(pickedComplexConfigToResolverConfig(undefined, 'authorProfile')).to.equal(null);
    expect(pickedComplexConfigToResolverConfig('{not json', 'authorProfile')).to.equal(null);
  });

  it('pickedComplexConfigToResolverConfig → dtoToRow round-trips the object and the alias', () => {
    const row = dtoToRow(makeDto({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      nestedSchemaTypeName: 'Person',
      resolverConfig: pickedComplexConfigToResolverConfig(INNER, 'authorProfile'),
    }));
    expect(row.pickedComplexType).to.exist;
    expect(JSON.parse(row.pickedComplexType!)).to.deep.equal(JSON.parse(INNER));
    expect(row.pickedContentTypeAlias).to.equal('authorProfile');
    expect(row.pickedPropertyAlias).to.equal(undefined);
    // nestedSchemaTypeName is authoritative for the range check and must survive.
    expect(row.nestedSchemaTypeName).to.equal('Person');
  });

  it('dtoToRow leaves pickedComplexType undefined for a drilled row', () => {
    const row = dtoToRow(makeDto({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      resolverConfig: '{"pickedPropertyAlias":"fullName","pickedContentTypeAlias":"authorProfile"}',
    }));
    expect(row.pickedComplexType).to.equal(undefined);
    expect(row.pickedPropertyAlias).to.equal('fullName');
  });

  it('a picked-object row survives the save filter (it has a property alias)', () => {
    const row = makeRow({
      schemaPropertyName: 'author',
      contentTypePropertyAlias: 'authorNode',
      nestedSchemaTypeName: 'Person',
      resolverConfig: pickedComplexConfigToResolverConfig(INNER, 'authorProfile'),
      pickedComplexType: INNER,
      pickedContentTypeAlias: 'authorProfile',
    });
    expect(isRowConfigured(row)).to.be.true;
    const dto = rowsToPropertyMappingDtos([row])[0];
    expect(dto).to.exist;
    expect(dto.nestedSchemaTypeName).to.equal('Person');
    expect(JSON.parse(dto.resolverConfig!).pickedComplexType.selectedSubType).to.equal('Person');
  });

  it('applySourceTypeChange from a picked-object row to complexType DROPS the config', () => {
    // This pins the whole design decision. The object's sub-rows read the PICKED
    // node; carried onto a complexType row they would be silently re-based onto
    // the page while still passing the complexType save filter.
    const row = makeRow({
      schemaPropertyName: 'author',
      sourceType: SourceType.Property,
      contentTypePropertyAlias: 'authorNode',
      nestedSchemaTypeName: 'Person',
      resolverConfig: STORED,
      pickedComplexType: INNER,
      pickedContentTypeAlias: 'authorProfile',
    });
    const result = applySourceTypeChange(row, SourceType.ComplexType);
    expect(result.resolverConfig).to.equal(null);
    expect(result.pickedComplexType).to.equal(undefined);
    expect(result.pickedContentTypeAlias).to.equal(undefined);
  });

  it('applySourceTypeChange clears the picked-object fields for every other target too', () => {
    const row = makeRow({
      schemaPropertyName: 'author',
      contentTypePropertyAlias: 'authorNode',
      resolverConfig: STORED,
      pickedComplexType: INNER,
      pickedContentTypeAlias: 'authorProfile',
    });
    const result = applySourceTypeChange(row, SourceType.Static);
    expect(result.pickedComplexType).to.equal(undefined);
    expect(result.resolverConfig).to.equal(null);
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

  it('clears targetPieceKey when switching a reference row to another source type', () => {
    const row = makeRow({
      schemaPropertyName: 'publisher',
      sourceType: SourceType.Reference,
      targetPieceKey: 'organization',
    });
    const result = applySourceTypeChange(row, SourceType.Property);
    expect(result.targetPieceKey).to.equal(null);
  });
});

describe('applyWarningsToRows', () => {
  const warn = (path: string, message: string): ValidationIssue => ({
    severity: 'warning', schemaType: 'Article', path, message,
  });

  it('keys a warning to the row whose schemaPropertyName matches path', () => {
    const rows = [makeRow({ schemaPropertyName: 'hasPart' }), makeRow({ schemaPropertyName: 'name' })];
    const result = applyWarningsToRows(rows, [warn('hasPart', 'out of range')]);
    expect(result[0].rangeWarning).to.equal('out of range');
    expect(result[1].rangeWarning).to.be.undefined;
  });

  it('joins multiple warnings for the same property (e.g. several block routes)', () => {
    const rows = [makeRow({ schemaPropertyName: 'hasPart' })];
    const result = applyWarningsToRows(rows, [warn('hasPart', 'route A bad'), warn('hasPart', 'route B bad')]);
    expect(result[0].rangeWarning).to.equal('route A bad\nroute B bad');
  });

  it('clears stale warnings when none match', () => {
    const rows = [makeRow({ schemaPropertyName: 'hasPart', rangeWarning: 'stale' })];
    const result = applyWarningsToRows(rows, []);
    expect(result[0].rangeWarning).to.be.undefined;
  });

  it('ignores info/non-warning severities for the range warning field', () => {
    const rows = [makeRow({ schemaPropertyName: 'hasPart' })];
    const info: ValidationIssue = { severity: 'info', schemaType: 'Article', path: 'hasPart', message: 'fyi' };
    const result = applyWarningsToRows(rows, [info]);
    expect(result[0].rangeWarning).to.be.undefined;
    expect(result[0].suggestion).to.be.undefined;
  });

  it('treats undefined warnings as no-op', () => {
    const rows = [makeRow({ schemaPropertyName: 'hasPart' })];
    const result = applyWarningsToRows(rows, undefined);
    expect(result[0].rangeWarning).to.be.undefined;
    expect(result[0].suggestion).to.be.undefined;
  });

  it('attaches a suggestion-severity advisory to the matching row (not as a range warning)', () => {
    const rows = [makeRow({ schemaPropertyName: 'description' }), makeRow({ schemaPropertyName: 'name' })];
    const suggestion: ValidationIssue = {
      severity: 'suggestion', schemaType: 'Article', path: 'description',
      message: 'Strip HTML from this RichText value',
    };
    const result = applyWarningsToRows(rows, [suggestion]);
    expect(result[0].suggestion).to.equal('Strip HTML from this RichText value');
    expect(result[0].rangeWarning).to.be.undefined;
    expect(result[1].suggestion).to.be.undefined;
  });

  it('joins multiple suggestion advisories for the same property', () => {
    const rows = [makeRow({ schemaPropertyName: 'description' })];
    const result = applyWarningsToRows(rows, [
      { severity: 'suggestion', schemaType: 'Article', path: 'description', message: 'strip HTML' },
      { severity: 'suggestion', schemaType: 'Article', path: 'description', message: 'wrap in list item' },
    ]);
    expect(result[0].suggestion).to.equal('strip HTML\nwrap in list item');
  });

  it('keeps range warning and suggestion as independent fields on one row', () => {
    const rows = [makeRow({ schemaPropertyName: 'hasPart' })];
    const result = applyWarningsToRows(rows, [
      warn('hasPart', 'out of range'),
      { severity: 'suggestion', schemaType: 'Article', path: 'hasPart', message: 'consider stripHtml' },
    ]);
    expect(result[0].rangeWarning).to.equal('out of range');
    expect(result[0].suggestion).to.equal('consider stripHtml');
  });
});

describe('persistence fidelity (loadOrder / isAutoMapped / transformType round-trip)', () => {
  it('dtoToRow captures load position, stored isAutoMapped and top-level transformType', () => {
    const dtos = [
      makeDto({ schemaPropertyName: 'Name', isAutoMapped: true, transformType: 'stripHtml' }),
      makeDto({ schemaPropertyName: 'Review' }),
    ];
    const rows = dtos.map(dtoToRow);
    expect(rows[0].loadOrder).to.equal(0);
    expect(rows[1].loadOrder).to.equal(1);
    expect(rows[0].isAutoMapped).to.equal(true);
    expect(rows[0].transformType).to.equal('stripHtml');
    expect(rows[1].isAutoMapped).to.equal(false);
  });

  it('rowsInPersistenceOrder restores stored order regardless of display sorting, new rows last', () => {
    const stored = [
      makeDto({ schemaPropertyName: 'Name' }),
      makeDto({ schemaPropertyName: 'Description' }),
      makeDto({ schemaPropertyName: 'Review' }),
    ].map(dtoToRow);
    const fresh = makeRow({ schemaPropertyName: 'AggregateRating' }); // no loadOrder
    const displaySorted = sortMappingRows([fresh, stored[2], stored[0], stored[1]]);
    const persisted = rowsInPersistenceOrder(displaySorted).map((r) => r.schemaPropertyName);
    expect(persisted).to.deep.equal(['Name', 'Description', 'Review', 'AggregateRating']);
  });

  it('mergeAutoMapSuggestions and applySourceTypeChange preserve loadOrder and stored flags', () => {
    const row = dtoToRow(
      makeDto({
        schemaPropertyName: 'Name',
        contentTypePropertyAlias: 'title',
        isAutoMapped: true,
        transformType: 'stripHtml',
      }),
      3,
    );
    const merged = mergeAutoMapSuggestions([row], [])[0];
    expect(merged.loadOrder).to.equal(3);
    expect(merged.isAutoMapped).to.equal(true);
    expect(merged.transformType).to.equal('stripHtml');
    const changed = applySourceTypeChange(merged, SourceType.Static);
    expect(changed.loadOrder).to.equal(3);
    expect(changed.isAutoMapped).to.equal(true);
  });
});

describe('isRowConfigured', () => {
  it('requires the field that matters for each source type', () => {
    expect(isRowConfigured(makeRow({ schemaPropertyName: 'name', contentTypePropertyAlias: 'title' }))).to.be.true;
    expect(isRowConfigured(makeRow({ schemaPropertyName: 'name' }))).to.be.false;

    expect(isRowConfigured(makeRow({ schemaPropertyName: 'name', sourceType: SourceType.Static, staticValue: 'x' }))).to.be.true;
    expect(isRowConfigured(makeRow({ schemaPropertyName: 'name', sourceType: SourceType.Static, contentTypePropertyAlias: 'title' }))).to.be.false;

    expect(isRowConfigured(makeRow({ schemaPropertyName: 'author', sourceType: SourceType.ComplexType, resolverConfig: '{}' }))).to.be.true;
    expect(isRowConfigured(makeRow({ schemaPropertyName: 'author', sourceType: SourceType.ComplexType }))).to.be.false;

    // reference rows key off the graph piece and never carry a property alias
    expect(isRowConfigured(makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Reference, targetPieceKey: 'organization' }))).to.be.true;
    expect(isRowConfigured(makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Reference }))).to.be.false;
  });
});

describe('rowsToPropertyMappingDtos', () => {
  it('emits configured rows only, in stored order', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'description', contentTypePropertyAlias: 'standfirst', loadOrder: 1 }),
      makeRow({ schemaPropertyName: 'unconfigured' }),
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title', loadOrder: 0 }),
    ];
    const dtos = rowsToPropertyMappingDtos(rows);
    expect(dtos.map((d) => d.schemaPropertyName)).to.deep.equal(['headline', 'description']);
  });

  it('carries the wire fields through, normalising empties to null', () => {
    const dto = rowsToPropertyMappingDtos([
      makeRow({
        schemaPropertyName: 'author',
        sourceType: SourceType.ComplexType,
        resolverConfig: '{"complexTypeMappings":[]}',
        nestedSchemaTypeName: 'Person',
        transformType: 'stripHtml',
        dynamicRootConfig: { originAlias: 'Site' } as never,
      }),
    ])[0];
    expect(dto.resolverConfig).to.equal('{"complexTypeMappings":[]}');
    expect(dto.nestedSchemaTypeName).to.equal('Person');
    expect(dto.transformType).to.equal('stripHtml');
    expect(dto.dynamicRootConfig).to.equal('{"originAlias":"Site"}');
    expect(dto.contentTypePropertyAlias).to.equal(null);
    expect(dto.staticValue).to.equal(null);
  });

  it('marks a row auto-mapped from either the live confidence or the stored flag', () => {
    const fromConfidence = rowsToPropertyMappingDtos([
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title', confidence: 90 }),
    ])[0];
    const fromStoredFlag = rowsToPropertyMappingDtos([
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title', isAutoMapped: true }),
    ])[0];
    const handMade = rowsToPropertyMappingDtos([
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title' }),
    ])[0];
    expect(fromConfidence.isAutoMapped).to.be.true;
    expect(fromStoredFlag.isAutoMapped).to.be.true;
    expect(handMade.isAutoMapped).to.be.false;
  });
});

describe('reconcileRowsForSchemaType', () => {
  const blogPostingProps = [
    makeSchemaProp({ name: 'headline', propertyType: 'string', confidence: 95 }),
    makeSchemaProp({ name: 'author', propertyType: 'Person', acceptedTypes: ['Person', 'Organization'], isComplexType: true, confidence: 80 }),
  ];

  it('keeps rows whose property exists on the new type and drops the rest', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title' }),
      makeRow({ schemaPropertyName: 'printSection', contentTypePropertyAlias: 'sectionName' }),
    ];
    const { kept, droppedConfigured } = reconcileRowsForSchemaType(rows, blogPostingProps);
    expect(kept.map((r) => r.schemaPropertyName)).to.deep.equal(['headline']);
    expect(droppedConfigured.map((r) => r.schemaPropertyName)).to.deep.equal(['printSection']);
  });

  it('matches property names case-insensitively', () => {
    const rows = [makeRow({ schemaPropertyName: 'HeadLine', contentTypePropertyAlias: 'title' })];
    const { kept, droppedConfigured } = reconcileRowsForSchemaType(rows, blogPostingProps);
    expect(kept).to.have.lengthOf(1);
    expect(droppedConfigured).to.be.empty;
    // the row keeps the casing it was stored with
    expect(kept[0].schemaPropertyName).to.equal('HeadLine');
  });

  it('refreshes type-derived metadata from the new type', () => {
    const rows = [
      makeRow({
        schemaPropertyName: 'author',
        contentTypePropertyAlias: 'authorName',
        schemaPropertyType: 'string',
        acceptedTypes: ['Thing'],
        isComplexType: false,
        schemaRank: 10,
      }),
    ];
    const { kept } = reconcileRowsForSchemaType(rows, blogPostingProps);
    expect(kept[0].schemaPropertyType).to.equal('Person');
    expect(kept[0].acceptedTypes).to.deep.equal(['Person', 'Organization']);
    expect(kept[0].isComplexType).to.be.true;
    expect(kept[0].schemaRank).to.equal(80);
  });

  it('preserves every user-authored field on a kept row', () => {
    const rows = [
      makeRow({
        schemaPropertyName: 'author',
        sourceType: SourceType.ComplexType,
        resolverConfig: '{"complexTypeMappings":[{"schemaProperty":"name","contentTypePropertyAlias":"authorName"}]}',
        nestedSchemaTypeName: 'Person',
        transformType: 'stripHtml',
        loadOrder: 4,
        isAutoMapped: true,
      }),
    ];
    const kept = reconcileRowsForSchemaType(rows, blogPostingProps).kept[0];
    expect(kept.sourceType).to.equal(SourceType.ComplexType);
    expect(kept.resolverConfig).to.contain('authorName');
    expect(kept.nestedSchemaTypeName).to.equal('Person');
    expect(kept.transformType).to.equal('stripHtml');
    expect(kept.loadOrder).to.equal(4);
    expect(kept.isAutoMapped).to.equal(true);
  });

  it('clears badges that described the old type', () => {
    const rows = [
      makeRow({
        schemaPropertyName: 'headline',
        contentTypePropertyAlias: 'title',
        rangeWarning: 'stale warning',
        suggestion: 'stale suggestion',
      }),
    ];
    const kept = reconcileRowsForSchemaType(rows, blogPostingProps).kept[0];
    expect(kept.rangeWarning).to.equal(undefined);
    expect(kept.suggestion).to.equal(undefined);
  });

  it('drops unconfigured rows silently, without listing them as losses', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'printSection' }), // placeholder never filled in
      makeRow({ schemaPropertyName: 'printPage', contentTypePropertyAlias: 'pageNo' }),
    ];
    const { kept, droppedConfigured } = reconcileRowsForSchemaType(rows, blogPostingProps);
    expect(kept).to.be.empty;
    expect(droppedConfigured.map((r) => r.schemaPropertyName)).to.deep.equal(['printPage']);
  });

  it('drops everything when the new type shares no properties', () => {
    const rows = [makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title' })];
    const { kept, droppedConfigured } = reconcileRowsForSchemaType(rows, [makeSchemaProp({ name: 'cookTime' })]);
    expect(kept).to.be.empty;
    expect(droppedConfigured).to.have.lengthOf(1);
  });

  it('returns kept rows in display order', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'author' }), // unmapped, rank 80
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title' }), // mapped
    ];
    const { kept } = reconcileRowsForSchemaType(rows, blogPostingProps);
    expect(kept.map((r) => r.schemaPropertyName)).to.deep.equal(['headline', 'author']);
  });
});
