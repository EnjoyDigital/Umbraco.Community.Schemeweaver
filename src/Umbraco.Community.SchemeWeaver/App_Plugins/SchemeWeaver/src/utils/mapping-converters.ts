import type { PropertyMappingDto, PropertyMappingSuggestion } from '../api/types.js';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';

/** Popular Schema.org properties shown first in sorted order */
const POPULAR_PROPERTIES = [
  'name', 'headline', 'description', 'image', 'url',
  'author', 'datePublished', 'dateModified', 'sku', 'price',
];

/** Convert stored PropertyMappingDto to UI row model */
export function dtoToRow(dto: PropertyMappingDto): PropertyMappingRow {
  return {
    schemaPropertyName: dto.schemaPropertyName || '',
    schemaPropertyType: '',
    sourceType: dto.sourceType || 'property',
    contentTypePropertyAlias: dto.contentTypePropertyAlias || '',
    sourceContentTypeAlias: dto.sourceContentTypeAlias || '',
    staticValue: dto.staticValue || '',
    confidence: null,
    editorAlias: '',
    nestedSchemaTypeName: dto.nestedSchemaTypeName || '',
    resolverConfig: dto.resolverConfig || null,
    acceptedTypes: [],
    isComplexType: false,
    expanded: false,
    subMappings: [],
    selectedSubType: '',
    sourceContentTypeProperties: [],
  };
}

/** Convert PropertyMappingSuggestion to UI row model */
export function suggestionToRow(s: PropertyMappingSuggestion): PropertyMappingRow {
  return {
    schemaPropertyName: s.schemaPropertyName,
    schemaPropertyType: s.schemaPropertyType || '',
    sourceType: s.suggestedSourceType,
    contentTypePropertyAlias: s.suggestedContentTypePropertyAlias || '',
    sourceContentTypeAlias: '',
    staticValue: '',
    confidence: s.confidence,
    editorAlias: s.editorAlias || '',
    nestedSchemaTypeName: s.suggestedNestedSchemaTypeName || '',
    resolverConfig: s.suggestedResolverConfig || null,
    acceptedTypes: s.acceptedTypes || [],
    isComplexType: s.isComplexType || false,
    expanded: false,
    subMappings: [],
    selectedSubType: '',
    sourceContentTypeProperties: [],
  };
}

/** Check whether a row has user-provided data */
function rowHasUserData(row: PropertyMappingRow): boolean {
  return !!(row.contentTypePropertyAlias || row.staticValue || row.resolverConfig);
}

/**
 * Merge auto-map suggestions into existing rows, preserving user mappings.
 * - If a row already exists for a schema property AND has user data, keep the
 *   user's choices and only update the confidence score.
 * - New schema properties from suggestions are added as new rows.
 * - Existing rows not in suggestions are preserved unchanged.
 */
export function mergeAutoMapSuggestions(
  existingRows: PropertyMappingRow[],
  suggestions: PropertyMappingSuggestion[],
): PropertyMappingRow[] {
  const rowMap = new Map<string, PropertyMappingRow>();

  // Index existing rows by schema property name (case-insensitive)
  for (const row of existingRows) {
    rowMap.set(row.schemaPropertyName.toLowerCase(), { ...row });
  }

  for (const suggestion of suggestions) {
    const key = suggestion.schemaPropertyName.toLowerCase();
    const existing = rowMap.get(key);

    if (existing && rowHasUserData(existing)) {
      // Preserve user data, only update confidence
      rowMap.set(key, { ...existing, confidence: suggestion.confidence });
    } else {
      // New property or existing without user data — use suggestion
      rowMap.set(key, suggestionToRow(suggestion));
    }
  }

  return sortMappingRows([...rowMap.values()]);
}

/**
 * Sort mapping rows in display order:
 * 1. Popular Schema.org properties (in POPULAR_PROPERTIES order)
 * 2. Mapped properties (alphabetical)
 * 3. Unmapped properties (alphabetical)
 */
/**
 * Apply a source type change to a mapping row, resetting dependent fields.
 * Shared between property-mapping-table, schema-mapping-view, and property-mapping-modal.
 */
export function applySourceTypeChange(row: PropertyMappingRow, newSourceType: string): PropertyMappingRow {
  return {
    ...row,
    sourceType: newSourceType,
    contentTypePropertyAlias: '',
    staticValue: '',
    sourceContentTypeAlias: '',
    sourceContentTypeProperties: [],
    nestedSchemaTypeName: (newSourceType === 'blockContent' || newSourceType === 'complexType')
      ? row.nestedSchemaTypeName : '',
    resolverConfig: (newSourceType === 'blockContent' || newSourceType === 'complexType')
      ? row.resolverConfig : null,
    expanded: newSourceType === 'complexType' ? row.expanded : false,
    subMappings: newSourceType === 'complexType' ? row.subMappings : [],
    selectedSubType: newSourceType === 'complexType' ? row.selectedSubType : '',
  };
}

export function sortMappingRows(rows: PropertyMappingRow[]): PropertyMappingRow[] {
  return [...rows].sort((a, b) => {
    const aPopIdx = POPULAR_PROPERTIES.indexOf(a.schemaPropertyName);
    const bPopIdx = POPULAR_PROPERTIES.indexOf(b.schemaPropertyName);
    const aIsPopular = aPopIdx !== -1;
    const bIsPopular = bPopIdx !== -1;
    const aMapped = rowHasUserData(a);
    const bMapped = rowHasUserData(b);

    // Popular properties first, in their defined order
    if (aIsPopular && bIsPopular) return aPopIdx - bPopIdx;
    if (aIsPopular) return -1;
    if (bIsPopular) return 1;

    // Mapped before unmapped
    if (aMapped && !bMapped) return -1;
    if (!aMapped && bMapped) return 1;

    // Higher confidence before lower confidence
    const aConf = a.confidence ?? -1;
    const bConf = b.confidence ?? -1;
    if (aConf !== bConf) return bConf - aConf;

    // Alphabetical within same group
    return a.schemaPropertyName.localeCompare(b.schemaPropertyName);
  });
}
