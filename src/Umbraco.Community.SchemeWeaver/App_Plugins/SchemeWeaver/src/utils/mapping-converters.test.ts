import { expect } from '@open-wc/testing';
import type { PropertyMappingDto, PropertyMappingSuggestion, ValidationIssue } from '../api/types.js';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import { SourceType } from '../constants/source-type.js';
import { sortMappingRows, mergeAutoMapSuggestions, dtoToRow, rowsInPersistenceOrder, applySourceTypeChange, applyWarningsToRows, drillConfigToResolverConfig } from './mapping-converters.js';

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
