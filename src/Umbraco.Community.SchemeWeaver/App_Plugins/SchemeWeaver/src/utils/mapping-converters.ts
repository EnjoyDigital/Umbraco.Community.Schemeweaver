import type { PropertyMappingDto, PropertyMappingSuggestion } from '../api/types.js';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import { SourceType, type SourceTypeValue } from '../constants/source-type.js';

/** Popular Schema.org properties shown first in sorted order */
export const POPULAR_PROPERTIES = [
  'name', 'headline', 'description', 'image', 'url',
  'author', 'datePublished', 'dateModified', 'sku', 'price',
];

/** Convert stored PropertyMappingDto to UI row model */
export function dtoToRow(dto: PropertyMappingDto): PropertyMappingRow {
  return {
    schemaPropertyName: dto.schemaPropertyName || '',
    schemaPropertyType: '',
    sourceType: dto.sourceType || SourceType.Property,
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
    dynamicRootConfig: dto.dynamicRootConfig ? JSON.parse(dto.dynamicRootConfig) : undefined,
    sourceDocumentTypeUnique: undefined,
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
    dynamicRootConfig: undefined,
    sourceDocumentTypeUnique: undefined,
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

  const suggestionKeys = new Set<string>();

  for (const suggestion of suggestions) {
    const key = suggestion.schemaPropertyName.toLowerCase();
    suggestionKeys.add(key);
    const existing = rowMap.get(key);

    if (existing && rowHasUserData(existing)) {
      // Preserve user data, only update confidence
      rowMap.set(key, { ...existing, confidence: suggestion.confidence });
    } else if (
      suggestion.suggestedContentTypePropertyAlias ||
      (suggestion.isComplexType && suggestion.suggestedNestedSchemaTypeName && suggestion.confidence > 0)
    ) {
      // Only add suggestions that have an actual property match or are complex
      // types the auto-mapper actually matched (confidence > 0). Zero-confidence
      // unmatched properties can be added on-demand via the "Add property" combobox.
      rowMap.set(key, suggestionToRow(suggestion));
    }
  }

  // Remove stale placeholder rows that weren't in auto-map suggestions,
  // have no user data, and aren't popular or complex type properties.
  const popularSet = new Set(POPULAR_PROPERTIES.map(p => p.toLowerCase()));
  for (const [key, row] of rowMap) {
    if (!rowHasUserData(row) && !suggestionKeys.has(key) && row.confidence === null
        && !row.isComplexType && !popularSet.has(key)) {
      rowMap.delete(key);
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
export function applySourceTypeChange(row: PropertyMappingRow, newSourceType: SourceTypeValue): PropertyMappingRow {
  const needsRelated = newSourceType === SourceType.Parent || newSourceType === SourceType.Ancestor || newSourceType === SourceType.Sibling;
  return {
    ...row,
    sourceType: newSourceType,
    contentTypePropertyAlias: '',
    staticValue: '',
    sourceContentTypeAlias: needsRelated ? row.sourceContentTypeAlias : '',
    sourceContentTypeProperties: needsRelated ? row.sourceContentTypeProperties : [],
    dynamicRootConfig: needsRelated ? row.dynamicRootConfig : undefined,
    sourceDocumentTypeUnique: needsRelated ? row.sourceDocumentTypeUnique : undefined,
    nestedSchemaTypeName: (newSourceType === SourceType.BlockContent || newSourceType === SourceType.ComplexType)
      ? row.nestedSchemaTypeName : '',
    resolverConfig: (newSourceType === SourceType.BlockContent || newSourceType === SourceType.ComplexType)
      ? row.resolverConfig : null,
    expanded: newSourceType === SourceType.ComplexType ? row.expanded : false,
    subMappings: newSourceType === SourceType.ComplexType ? row.subMappings : [],
    selectedSubType: newSourceType === SourceType.ComplexType ? row.selectedSubType : '',
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
