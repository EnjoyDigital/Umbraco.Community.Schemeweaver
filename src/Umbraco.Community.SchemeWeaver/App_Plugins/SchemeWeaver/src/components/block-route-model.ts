/**
 * Shared data model + pure helpers for the recursive block-route editor used by both the
 * top-level nested-mapping modal and the {@link NestedBlockRoutesElement} sub-component.
 *
 * The routing model is recursive: a block element type maps to a Schema.org type with a
 * table of property mappings; any property mapping whose chosen content property is itself
 * a Block List/Grid can carry its own nested `routes` for that inner block's element types.
 * Keeping the model + serialisation here lets the modal (depth 0, with target pickers) and
 * the sub-component (depth ≥ 1) share one implementation instead of copy-pasting it.
 */
import type {
  RankedSchemaPropertyInfo,
  BlockElementTypeInfo,
  BlockElementPropertyInfo,
  BlockRoute,
  BlockRouteSuggestion,
  BlockRoutePropertyMapping,
  BlockRoutePropertyMappingSuggestion,
} from '../api/types.js';

/** A single nested-property mapping row inside one block's expandable table. */
export interface RoutePropEntry {
  schemaProperty: string;
  schemaPropertyType: string;
  contentProperty: string;
  wrapInType: string;
  wrapInProperty: string;
  /** Optional value transform (e.g. `"stripHtml"`) carried through suggestion → save. */
  transformType: string;
  isComplexType: boolean;
  /** Nested block element types of the chosen content property (when it is itself a block list). */
  nestedBlockElementTypes: BlockElementTypeInfo[];
  /**
   * Seed routes handed to the child nested editor as its initial value. Only changes on
   * init / auto-map — never written back from the child — so the child never rebuilds in a
   * feedback loop while the user edits it.
   */
  nestedSeed: BlockRoute[];
  /** Latest nested routes captured from the child editor — what gets serialised on save. */
  nestedRoutes: BlockRoute[];
  /** Suggested nested routes, handed to the child for its own "Auto-map" action. */
  nestedSuggestedRoutes: BlockRouteSuggestion[];
  /** UI: whether the nested editor is revealed. */
  nestedExpanded: boolean;
}

/** One block element type as a row in the (possibly nested) panel. */
export interface BlockMappingRow {
  alias: string;
  name: string;
  /** Block element property aliases (drive the value dropdown). */
  properties: string[];
  propertyInfos: BlockElementPropertyInfo[];
  /** false === SKIP / not mapped (opt-in). */
  mapped: boolean;
  nestedSchemaType: string;
  propertyMappings: RoutePropEntry[];
  /** Total nested schema properties for the chosen type (denominator of the badge). */
  totalSchemaProps: number;
  expanded: boolean;
  confidence: number | null;
  /** Target page property — top level only; undefined for nested levels. */
  targetProperty?: string;
}

/** Normalise an element type's `propertyInfos`, falling back to plain aliases. */
export function rowPropertyInfos(bt: BlockElementTypeInfo): BlockElementPropertyInfo[] {
  return bt.propertyInfos?.length
    ? bt.propertyInfos
    : bt.properties.map((alias) => ({ alias, name: alias, editorAlias: '' }));
}

/** The nested block element types of a chosen content property, if it is a nested block list. */
export function resolveNestedBlockTypes(
  propertyInfos: BlockElementPropertyInfo[],
  contentProperty: string,
): BlockElementTypeInfo[] {
  if (!contentProperty) return [];
  const info = propertyInfos.find((p) => p.alias === contentProperty);
  return info?.nestedBlockElementTypes ?? [];
}

export interface PropEntrySeed {
  contentProperty?: string;
  wrapInType?: string | null;
  wrapInProperty?: string | null;
  transformType?: string | null;
  nestedSeed?: BlockRoute[];
  nestedRoutes?: BlockRoute[];
  nestedSuggestedRoutes?: BlockRouteSuggestion[];
}

/** Build one property-mapping row, resolving nested block element types from the content property. */
export function makePropEntry(
  schemaProperty: string,
  schemaPropertyType: string,
  isComplexType: boolean,
  propertyInfos: BlockElementPropertyInfo[],
  seed?: PropEntrySeed,
): RoutePropEntry {
  const contentProperty = seed?.contentProperty ?? '';
  return {
    schemaProperty,
    schemaPropertyType,
    contentProperty,
    wrapInType: seed?.wrapInType ?? '',
    wrapInProperty: seed?.wrapInProperty ?? '',
    transformType: seed?.transformType ?? '',
    isComplexType,
    nestedBlockElementTypes: resolveNestedBlockTypes(propertyInfos, contentProperty),
    nestedSeed: seed?.nestedSeed ?? seed?.nestedRoutes ?? [],
    nestedRoutes: seed?.nestedRoutes ?? seed?.nestedSeed ?? [],
    nestedSuggestedRoutes: seed?.nestedSuggestedRoutes ?? [],
    nestedExpanded: false,
  };
}

/**
 * Seed property entries from raw stored or suggested mappings (pre-hydration). The entries are
 * later aligned to the chosen schema type's real properties via {@link alignPropertyMappings}.
 */
export function seedEntriesFromRaw(
  raw: Array<BlockRoutePropertyMapping | BlockRoutePropertyMappingSuggestion>,
  propertyInfos: BlockElementPropertyInfo[],
  fromSuggestion: boolean,
): RoutePropEntry[] {
  return raw.map((m) => {
    const nested = fromSuggestion
      ? convertSuggestedRoutes((m as BlockRoutePropertyMappingSuggestion).routes)
      : ((m as BlockRoutePropertyMapping).routes ?? []);
    return makePropEntry(m.schemaProperty, '', false, propertyInfos, {
      contentProperty: m.contentProperty,
      wrapInType: m.wrapInType,
      wrapInProperty: m.wrapInProperty,
      transformType: m.transformType,
      nestedSeed: nested,
      nestedRoutes: nested,
      nestedSuggestedRoutes: fromSuggestion
        ? (m as BlockRoutePropertyMappingSuggestion).routes ?? []
        : [],
    });
  });
}

/**
 * Thread suggested nested routes onto already-seeded entries (matched case-insensitively by
 * schema property). Used when an entry is seeded from a STORED route but a heuristic suggestion
 * also exists — so the child editor can still offer "Auto-map nested" at deeper levels.
 */
export function threadNestedSuggestions(
  entries: RoutePropEntry[],
  suggestionMappings?: BlockRoutePropertyMappingSuggestion[],
): void {
  if (!suggestionMappings?.length) return;
  const byProp = new Map(suggestionMappings.map((m) => [m.schemaProperty.toLowerCase(), m]));
  for (const e of entries) {
    const sm = byProp.get(e.schemaProperty.toLowerCase());
    if (sm?.routes?.length) e.nestedSuggestedRoutes = sm.routes;
  }
}

/**
 * Align seeded entries to a nested schema type's real properties, preserving any already-chosen
 * content property / wrap / nested routes (keyed case-insensitively by schema property name).
 */
export function alignPropertyMappings(
  props: RankedSchemaPropertyInfo[],
  seed: RoutePropEntry[],
  propertyInfos: BlockElementPropertyInfo[],
): RoutePropEntry[] {
  const byName = new Map(seed.map((m) => [m.schemaProperty.toLowerCase(), m]));
  return props.map((sp) => {
    const prev = byName.get(sp.name.toLowerCase());
    return makePropEntry(sp.name, sp.propertyType, sp.isComplexType, propertyInfos, prev && {
      contentProperty: prev.contentProperty,
      wrapInType: prev.wrapInType,
      wrapInProperty: prev.wrapInProperty,
      transformType: prev.transformType,
      nestedSeed: prev.nestedSeed,
      nestedRoutes: prev.nestedRoutes,
      nestedSuggestedRoutes: prev.nestedSuggestedRoutes,
    });
  });
}

/** Build a block row from an element type and an optional seed. */
export interface RowSeed {
  nestedSchemaType: string;
  propertyMappings: RoutePropEntry[];
  confidence?: number | null;
  targetProperty?: string;
}

export function makeBlockRow(bt: BlockElementTypeInfo, seed?: RowSeed): BlockMappingRow {
  const propertyInfos = rowPropertyInfos(bt);
  return {
    alias: bt.alias,
    name: bt.name || bt.alias,
    properties: propertyInfos.map((p) => p.alias),
    propertyInfos,
    mapped: !!seed,
    nestedSchemaType: seed?.nestedSchemaType ?? '',
    propertyMappings: seed?.propertyMappings ?? [],
    totalSchemaProps: seed?.propertyMappings?.length ?? 0,
    expanded: false,
    confidence: seed?.confidence ?? null,
    targetProperty: seed?.targetProperty,
  };
}

/** Count of property rows with a chosen content value. */
export function mappedCount(row: BlockMappingRow): number {
  return row.propertyMappings.filter((m) => m.contentProperty.trim() !== '').length;
}

/** Serialise a block row's property table to the stored NestedPropertyMapping shape. */
export function serialisePropertyMappings(entries: RoutePropEntry[]): BlockRoutePropertyMapping[] {
  return entries
    .filter((m) => m.contentProperty.trim() !== '')
    .map((m) => {
      const pm: BlockRoutePropertyMapping = {
        schemaProperty: m.schemaProperty,
        contentProperty: m.contentProperty,
        wrapInType: m.wrapInType || null,
        wrapInProperty: m.wrapInProperty || null,
      };
      if (m.transformType) pm.transformType = m.transformType;
      if (m.nestedRoutes.length > 0) pm.routes = m.nestedRoutes;
      return pm;
    });
}

/** Serialise nested block rows (no target grouping) to a `routes` array. */
export function serialiseRoutes(rows: BlockMappingRow[]): BlockRoute[] {
  return rows
    .filter((r) => r.mapped && r.nestedSchemaType)
    .map((r) => ({
      blockAlias: r.alias,
      nestedSchemaType: r.nestedSchemaType,
      propertyMappings: serialisePropertyMappings(r.propertyMappings),
    }));
}

/** Convert suggested routes (with confidence) to the stored route shape, recursively. */
export function convertSuggestedRoutes(routes?: BlockRouteSuggestion[]): BlockRoute[] {
  if (!routes?.length) return [];
  return routes.map((r) => ({
    blockAlias: r.blockAlias,
    nestedSchemaType: r.nestedSchemaType,
    propertyMappings: r.propertyMappings.map((m) => {
      const pm: BlockRoutePropertyMapping = {
        schemaProperty: m.schemaProperty,
        contentProperty: m.contentProperty,
        wrapInType: m.wrapInType ?? null,
        wrapInProperty: m.wrapInProperty ?? null,
      };
      if (m.transformType) pm.transformType = m.transformType;
      const nested = convertSuggestedRoutes(m.routes);
      if (nested.length) pm.routes = nested;
      return pm;
    }),
  }));
}
