import type { PropertyMappingDto, PropertyMappingSuggestion, SchemaPropertyInfo, ValidationIssue } from '../api/types.js';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import { SourceType, type SourceTypeValue } from '../constants/source-type.js';

/** Popular Schema.org properties shown first in sorted order */
export const POPULAR_PROPERTIES = [
  'name', 'headline', 'description', 'image', 'url',
  'author', 'datePublished', 'dateModified', 'sku', 'price',
];

/**
 * Convert stored PropertyMappingDto to UI row model. `loadOrder` (the DTO's
 * position in the stored mapping — Array.map supplies it automatically) is
 * carried so persistence can re-emit rows in stored order; display sorting
 * stays presentation-only.
 */
export function dtoToRow(dto: PropertyMappingDto, loadOrder?: number): PropertyMappingRow {
  const drill = parseDrillConfig(dto.sourceType, dto.resolverConfig);
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
    loadOrder,
    isAutoMapped: dto.isAutoMapped,
    transformType: dto.transformType ?? null,
    targetPieceKey: dto.targetPieceKey ?? null,
    pickedPropertyAlias: drill?.pickedPropertyAlias,
    pickedContentTypeAlias: drill?.pickedContentTypeAlias,
  };
}

/**
 * Parses picker drill-down fields out of a row's resolverConfig. Deliberately
 * strict: only `property`-sourced rows can drill (the config key namespace is
 * shared with complexType/blockContent shapes, which must pass through
 * untouched), and only a truthy `pickedPropertyAlias` counts as drill config.
 */
export function parseDrillConfig(
  sourceType: string | undefined,
  resolverConfig: string | null | undefined,
): { pickedPropertyAlias: string; pickedContentTypeAlias?: string } | null {
  if (sourceType !== SourceType.Property || !resolverConfig) return null;
  try {
    const parsed = JSON.parse(resolverConfig);
    if (typeof parsed?.pickedPropertyAlias === 'string' && parsed.pickedPropertyAlias) {
      return {
        pickedPropertyAlias: parsed.pickedPropertyAlias,
        pickedContentTypeAlias:
          typeof parsed.pickedContentTypeAlias === 'string' && parsed.pickedContentTypeAlias
            ? parsed.pickedContentTypeAlias
            : undefined,
      };
    }
  } catch {
    // Malformed config — treat as no drill config, mirroring the backend.
  }
  return null;
}

/**
 * Serialises a row's drill-down state into its resolverConfig (or clears it).
 * Used by the table's edit handlers so the save mappers can keep passing
 * `resolverConfig` through verbatim.
 */
export function drillConfigToResolverConfig(
  pickedPropertyAlias: string | undefined,
  pickedContentTypeAlias: string | undefined,
): string | null {
  if (!pickedPropertyAlias) return null;
  return JSON.stringify(
    pickedContentTypeAlias
      ? { pickedPropertyAlias, pickedContentTypeAlias }
      : { pickedPropertyAlias },
  );
}

/**
 * Rows in persistence order: stored rows by their load position, new rows
 * (no loadOrder) appended in display order. Array.sort is stable, so ties keep
 * their relative display order.
 */
export function rowsInPersistenceOrder(rows: PropertyMappingRow[]): PropertyMappingRow[] {
  return [...rows].sort(
    (a, b) => (a.loadOrder ?? Number.MAX_SAFE_INTEGER) - (b.loadOrder ?? Number.MAX_SAFE_INTEGER),
  );
}

/**
 * Whether a row carries enough configuration to be worth persisting. Each source
 * type has its own notion of "filled in": static rows need a value, reference
 * rows key off a graph piece and have no property alias at all, and everything
 * else needs a content property.
 */
export function isRowConfigured(row: PropertyMappingRow): boolean {
  if (row.sourceType === SourceType.Static) return !!row.staticValue;
  if (row.sourceType === SourceType.ComplexType) return !!row.resolverConfig;
  if (row.sourceType === SourceType.BlockContent) return !!row.contentTypePropertyAlias;
  // reference rows have no property alias — they key off the graph piece
  if (row.sourceType === SourceType.Reference) return !!row.targetPieceKey;
  // property/parent/ancestor/sibling: need a content property alias
  return !!row.contentTypePropertyAlias;
}

/**
 * Rows as persistable DTOs — stored order, unconfigured rows dropped. Shared by
 * every save path (workspace view, property mapping modal, entity action) so the
 * wire shape is defined in exactly one place.
 */
export function rowsToPropertyMappingDtos(rows: PropertyMappingRow[]): PropertyMappingDto[] {
  return rowsInPersistenceOrder(rows)
    .filter(isRowConfigured)
    .map((row) => ({
      schemaPropertyName: row.schemaPropertyName,
      sourceType: row.sourceType,
      contentTypePropertyAlias: row.contentTypePropertyAlias || null,
      sourceContentTypeAlias: row.sourceContentTypeAlias || null,
      transformType: row.transformType ?? null,
      isAutoMapped: row.confidence !== null || row.isAutoMapped === true,
      staticValue: row.staticValue || null,
      nestedSchemaTypeName: row.nestedSchemaTypeName || null,
      resolverConfig: row.resolverConfig,
      dynamicRootConfig: row.dynamicRootConfig ? JSON.stringify(row.dynamicRootConfig) : null,
      targetPieceKey: row.targetPieceKey || null,
    }));
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
    targetPieceKey: s.suggestedTargetPieceKey ?? null,
  };
}

/** Check whether a row has user-provided data */
function rowHasUserData(row: PropertyMappingRow): boolean {
  return !!(row.contentTypePropertyAlias || row.staticValue || row.resolverConfig || row.targetPieceKey);
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
      (suggestion.suggestedSourceType === SourceType.Reference && suggestion.suggestedTargetPieceKey) ||
      (suggestion.isComplexType && suggestion.suggestedNestedSchemaTypeName && suggestion.confidence > 0)
    ) {
      // Only add suggestions that have an actual property match, reference a
      // graph piece, or are complex types the auto-mapper actually matched
      // (confidence > 0). Zero-confidence unmatched properties can be added
      // on-demand via the "Add property" combobox.
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
 * Apply a source type change to a mapping row, resetting dependent fields.
 * Shared between property-mapping-table, schema-mapping-view, and property-mapping-modal.
 */
export function applySourceTypeChange(row: PropertyMappingRow, newSourceType: SourceTypeValue): PropertyMappingRow {
  const needsRelated = newSourceType === SourceType.Parent || newSourceType === SourceType.Ancestor || newSourceType === SourceType.Sibling;
  // resolverConfig is only meaningful across a source change when it is a
  // complexType/blockContent shape moving between those two types. A picker
  // drill config (property-sourced) must NOT masquerade as complex config —
  // it would pass the complexType save filter while rendering nothing.
  const keepsConfigShape =
    (newSourceType === SourceType.BlockContent || newSourceType === SourceType.ComplexType)
    && !parseDrillConfig(row.sourceType, row.resolverConfig);
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
    resolverConfig: keepsConfigShape ? row.resolverConfig : null,
    expanded: newSourceType === SourceType.ComplexType ? row.expanded : false,
    subMappings: newSourceType === SourceType.ComplexType ? row.subMappings : [],
    selectedSubType: newSourceType === SourceType.ComplexType ? row.selectedSubType : '',
    targetPieceKey: newSourceType === SourceType.Reference ? row.targetPieceKey : null,
    pickedPropertyAlias: undefined,
    pickedContentTypeAlias: undefined,
    pickedContentTypeProperties: undefined,
    pickedContentTypeUnique: undefined,
  };
}

/**
 * Applies server-authoritative warnings to rows, keyed by
 * `warning.path === row.schemaPropertyName`. Multiple warnings for one property
 * (e.g. several offending block routes) are joined onto one badge. Returns a new
 * array; rows with no matching warning have `rangeWarning`/`suggestion` cleared.
 * Matching the server's `Path` exactly is what keeps the badge in sync after a
 * live save.
 *
 * `warning`-severity issues populate `rangeWarning` (a blocking red badge);
 * `suggestion`-severity advisories (stripHtml/wrapInListItem/missing-required/
 * export hints) populate the separate `suggestion` field rendered as a
 * non-blocking lightbulb hint. Other severities are left for the JSON-LD
 * preview's validation panel.
 */
export function applyWarningsToRows(
  rows: PropertyMappingRow[],
  warnings: ValidationIssue[] | undefined,
): PropertyMappingRow[] {
  const byProperty = new Map<string, string[]>();
  const suggestionsByProperty = new Map<string, string[]>();
  for (const w of warnings ?? []) {
    if (!w.path) continue;
    if (w.severity === 'warning') {
      const list = byProperty.get(w.path) ?? [];
      list.push(w.message);
      byProperty.set(w.path, list);
    } else if (w.severity === 'suggestion') {
      const list = suggestionsByProperty.get(w.path) ?? [];
      list.push(w.message);
      suggestionsByProperty.set(w.path, list);
    }
  }

  return rows.map((row) => {
    const messages = byProperty.get(row.schemaPropertyName);
    const suggestions = suggestionsByProperty.get(row.schemaPropertyName);
    return {
      ...row,
      rangeWarning: messages?.length ? messages.join('\n') : undefined,
      suggestion: suggestions?.length ? suggestions.join('\n') : undefined,
    };
  });
}

/**
 * Display-order rank for a row: prefers the schema recommendation rank (the ranked endpoint's
 * confidence) when present; falls back to the hardcoded POPULAR_PROPERTIES list (older backends
 * with no ranked data), then to the auto-map match confidence.
 */
function schemaRankOf(row: PropertyMappingRow): number {
  if (typeof row.schemaRank === 'number') return row.schemaRank;
  const popIdx = POPULAR_PROPERTIES.indexOf(row.schemaPropertyName);
  if (popIdx !== -1) return 100 - popIdx; // popular get a high score, in their defined order
  return row.confidence ?? 0;
}

/**
 * Sort mapping rows in display order:
 * 1. Mapped (user-provided) properties first
 * 2. Then by schema recommendation rank (recommended properties surface to the top)
 * 3. Alphabetical within the same rank
 */
export function sortMappingRows(rows: PropertyMappingRow[]): PropertyMappingRow[] {
  return [...rows].sort((a, b) => {
    const aMapped = rowHasUserData(a);
    const bMapped = rowHasUserData(b);
    if (aMapped !== bMapped) return aMapped ? -1 : 1;

    const aRank = schemaRankOf(a);
    const bRank = schemaRankOf(b);
    if (aRank !== bRank) return bRank - aRank;

    return a.schemaPropertyName.localeCompare(b.schemaPropertyName);
  });
}

/** Outcome of re-keying an existing mapping's rows onto a different Schema.org type. */
export interface SchemaTypeReconciliation {
  /** Rows whose schema property exists on the new type, with type metadata refreshed. */
  kept: PropertyMappingRow[];
  /**
   * Rows that cannot carry over AND that the user had actually configured — i.e.
   * the genuine losses worth confirming. Unconfigured placeholder rows are
   * dropped silently and never appear here.
   */
  droppedConfigured: PropertyMappingRow[];
}

/**
 * Re-keys an existing mapping's rows onto a different Schema.org type.
 *
 * A row survives when the new type has a property of the same name
 * (case-insensitively, matching how the rest of this module and the view's
 * enrichment pass compare property names). Survivors keep every user-authored
 * field verbatim — source type, transform, resolverConfig, nested/complex config,
 * graph piece — but have their *type-derived* metadata refreshed from the new
 * type: the mapping table never re-derives `acceptedTypes`/`isComplexType` after
 * a row is created, so leaving the old type's values in place would silently
 * drive the wrong editor affordances. Server-issued badges are cleared because
 * they describe the old type; the next fetch re-applies them.
 *
 * Nested `complexTypeMappings` sub-rows are deliberately NOT re-validated here:
 * if the parent property survives, its config is kept as-is and any range or
 * sub-property mismatch is left to the server-side validator, which reports it
 * through the normal warning path after the save.
 */
export function reconcileRowsForSchemaType(
  rows: PropertyMappingRow[],
  newSchemaProperties: readonly (SchemaPropertyInfo & { confidence?: number })[],
): SchemaTypeReconciliation {
  const byName = new Map(newSchemaProperties.map((p) => [p.name.toLowerCase(), p]));

  const kept: PropertyMappingRow[] = [];
  const droppedConfigured: PropertyMappingRow[] = [];

  for (const row of rows) {
    const schemaProp = byName.get(row.schemaPropertyName.toLowerCase());
    if (!schemaProp) {
      if (isRowConfigured(row)) droppedConfigured.push(row);
      continue;
    }

    kept.push({
      ...row,
      schemaPropertyType: schemaProp.propertyType || '',
      acceptedTypes: schemaProp.acceptedTypes ?? [],
      isComplexType: schemaProp.isComplexType ?? false,
      schemaRank: schemaProp.confidence,
      rangeWarning: undefined,
      suggestion: undefined,
    });
  }

  return { kept: sortMappingRows(kept), droppedConfigured };
}
