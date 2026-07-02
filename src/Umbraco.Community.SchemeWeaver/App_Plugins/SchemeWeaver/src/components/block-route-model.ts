/**
 * Shared data model + pure helpers for the recursive block-route editor used by both the
 * top-level nested-mapping modal and the {@link NestedBlockRoutesElement} sub-component.
 *
 * The routing model is recursive: a block element type maps to a Schema.org type with a
 * table of property mappings; any property mapping whose chosen content property is itself
 * a Block List/Grid can carry its own nested `routes` for that inner block's element types.
 * Keeping the model + serialisation here lets the modal (depth 0, scoped to one parent
 * property-mapping row) and the sub-component (depth ≥ 1) share one implementation instead
 * of copy-pasting it.
 */
import type {
  RankedSchemaPropertyInfo,
  BlockElementTypeInfo,
  BlockElementPropertyInfo,
  BlockRoute,
  BlockRouteSuggestion,
  BlockRoutePropertyMapping,
  BlockRoutePropertyMappingSuggestion,
  RoutedResolverConfig,
} from '../api/types.js';
import { filterOutPrimitiveAcceptedTypes } from '../utils/schema-primitives.js';

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
  /** Ranking confidence (0–100) of this schema property for the chosen type — drives ordering. */
  confidence: number;
  /** True when this property is recommended (Google-relevant; confidence ≥ 60). */
  recommended: boolean;
  /** Schema.org types this property may take (drives the nested-block type dropdown). */
  acceptedTypes: string[];
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
  /**
   * Stored fields this editor does not actively edit (string-list extraction, nested
   * ListItem wrapping). Captured on load and spread back on save so existing configs
   * round-trip without silent loss.
   */
  extras?: Pick<
    BlockRoutePropertyMapping,
    'extractAs' | 'nestedContentProperty' | 'wrapInListItem' | 'positionProperty'
  >;
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
  /** UI: reveal the long tail of low-confidence unmapped properties (progressive disclosure). */
  showAll: boolean;
  confidence: number | null;
  /**
   * The parent row's Schema.org property this block maps into. Always equals the
   * panel's `data.schemaPropertyName` at the top level (the panel is scoped to one
   * row and never re-targets); undefined for nested levels. Kept on the row so the
   * explicit fan-out affordance can group off-target routes it creates.
   */
  targetProperty?: string;
  /**
   * The heuristic suggester's preferred target for this block (e.g. `hasPart`),
   * when it differs from the panel's target. Display-only hint — never applied.
   */
  suggestedTarget?: string;
  /** Sibling rows (other targets) that already route this block — read-only context tags. */
  claimedBy?: string[];
  /** Route-level required schema properties, preserved verbatim from the stored route. */
  requiredProperties?: string[];
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

/**
 * The Schema.org object types valid for a property — its object accepted types. Drives both the
 * nested-block type dropdown and the "Wrap in Type" dropdown. Returns `[]` (→ caller falls back
 * to a free-text / searchable input) when the set is empty, all-primitive, or includes the
 * universal `Thing` root, where a fixed dropdown is useless.
 */
export function allowedObjectSchemaTypes(entry: RoutePropEntry): string[] {
  const allowed = filterOutPrimitiveAcceptedTypes(entry.acceptedTypes ?? []);
  if (allowed.length === 0 || allowed.includes('Thing')) return [];
  return allowed;
}

/**
 * Build `<uui-select>` options for a constrained Schema.org type field. Includes a leading
 * "none" option and preserves a current value that is outside the allowed set (so an existing
 * saved mapping is never silently dropped).
 */
export function schemaTypeSelectOptions(
  allowed: string[],
  current: string,
  noneLabel: string,
): Array<{ name: string; value: string; selected: boolean }> {
  const known = allowed.includes(current);
  return [
    { name: noneLabel, value: '', selected: !current },
    ...(current && !known ? [{ name: current, value: current, selected: true }] : []),
    ...allowed.map((t) => ({ name: t, value: t, selected: current === t })),
  ];
}

export interface PropEntrySeed {
  contentProperty?: string;
  wrapInType?: string | null;
  wrapInProperty?: string | null;
  transformType?: string | null;
  nestedSeed?: BlockRoute[];
  nestedRoutes?: BlockRoute[];
  nestedSuggestedRoutes?: BlockRouteSuggestion[];
  extras?: RoutePropEntry['extras'];
}

/** Build one property-mapping row, resolving nested block element types from the content property. */
export function makePropEntry(
  schemaProperty: string,
  schemaPropertyType: string,
  isComplexType: boolean,
  acceptedTypes: string[],
  propertyInfos: BlockElementPropertyInfo[],
  seed?: PropEntrySeed,
  confidence = 0,
  recommended = false,
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
    confidence,
    recommended,
    acceptedTypes,
    nestedBlockElementTypes: resolveNestedBlockTypes(propertyInfos, contentProperty),
    nestedSeed: seed?.nestedSeed ?? seed?.nestedRoutes ?? [],
    nestedRoutes: seed?.nestedRoutes ?? seed?.nestedSeed ?? [],
    nestedSuggestedRoutes: seed?.nestedSuggestedRoutes ?? [],
    nestedExpanded: false,
    extras: seed?.extras,
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
    const stored = fromSuggestion ? undefined : (m as BlockRoutePropertyMapping);
    const hasExtras =
      stored &&
      (stored.extractAs != null ||
        stored.nestedContentProperty != null ||
        stored.wrapInListItem === true ||
        stored.positionProperty != null);
    return makePropEntry(m.schemaProperty, '', false, [], propertyInfos, {
      contentProperty: m.contentProperty,
      wrapInType: m.wrapInType,
      wrapInProperty: m.wrapInProperty,
      transformType: m.transformType,
      nestedSeed: nested,
      nestedRoutes: nested,
      nestedSuggestedRoutes: fromSuggestion
        ? (m as BlockRoutePropertyMappingSuggestion).routes ?? []
        : [],
      extras: hasExtras
        ? {
            extractAs: stored.extractAs,
            nestedContentProperty: stored.nestedContentProperty,
            wrapInListItem: stored.wrapInListItem,
            positionProperty: stored.positionProperty,
          }
        : undefined,
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
    return makePropEntry(sp.name, sp.propertyType, sp.isComplexType, sp.acceptedTypes ?? [], propertyInfos, prev && {
      contentProperty: prev.contentProperty,
      wrapInType: prev.wrapInType,
      wrapInProperty: prev.wrapInProperty,
      transformType: prev.transformType,
      nestedSeed: prev.nestedSeed,
      nestedRoutes: prev.nestedRoutes,
      nestedSuggestedRoutes: prev.nestedSuggestedRoutes,
      extras: prev.extras,
    }, sp.confidence, sp.isPopular);
  });
}

/** Build a block row from an element type and an optional seed. */
export interface RowSeed {
  nestedSchemaType: string;
  propertyMappings: RoutePropEntry[];
  confidence?: number | null;
  targetProperty?: string;
  suggestedTarget?: string;
  requiredProperties?: string[];
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
    showAll: false,
    confidence: seed?.confidence ?? null,
    targetProperty: seed?.targetProperty,
    suggestedTarget: seed?.suggestedTarget,
    requiredProperties: seed?.requiredProperties,
  };
}

/** Count of property rows with a chosen content value. */
export function mappedCount(row: BlockMappingRow): number {
  return row.propertyMappings.filter((m) => m.contentProperty.trim() !== '').length;
}

/** Count of property rows shown by default — recommended OR already mapped. */
export function recommendedCount(row: BlockMappingRow): number {
  return row.propertyMappings.filter((m) => m.recommended || m.contentProperty.trim() !== '').length;
}

/** Total recommended (Google-relevant) property rows for the chosen type. */
export function recommendedTotal(row: BlockMappingRow): number {
  return row.propertyMappings.filter((m) => m.recommended).length;
}

/** Recommended property rows that actually have a chosen content value (mapped ∩ recommended). */
export function recommendedMapped(row: BlockMappingRow): number {
  return row.propertyMappings.filter((m) => m.recommended && m.contentProperty.trim() !== '').length;
}

/**
 * The property rows to render, paired with their ORIGINAL index in `row.propertyMappings`
 * (callers edit rows by that index, so it must survive filtering/sorting). Ordered mapped-first
 * then confidence-DESC. When `showAll` is false, the long tail of low-confidence unmapped
 * properties is hidden — but any row with a chosen content value is always kept visible, so a
 * saved low-confidence mapping is never collapsed out of sight.
 */
export function visibleEntries(row: BlockMappingRow): Array<{ entry: RoutePropEntry; index: number }> {
  const paired = row.propertyMappings.map((entry, index) => ({ entry, index }));
  const filtered = row.showAll
    ? paired
    : paired.filter(({ entry }) => entry.recommended || entry.contentProperty.trim() !== '');
  return filtered.sort((a, b) => {
    const am = a.entry.contentProperty.trim() !== '' ? 1 : 0;
    const bm = b.entry.contentProperty.trim() !== '' ? 1 : 0;
    if (am !== bm) return bm - am; // mapped first
    return b.entry.confidence - a.entry.confidence; // then by confidence (stable sort keeps alpha within ties)
  });
}

/** How many property rows are hidden by the collapsed view (0 when `showAll`). */
export function hiddenCount(row: BlockMappingRow): number {
  if (row.showAll) return 0;
  return row.propertyMappings.length - visibleEntries(row).length;
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
      // Spread stored-but-unedited fields back so a load→save round-trip never loses them.
      if (m.extras) {
        if (m.extras.extractAs != null) pm.extractAs = m.extras.extractAs;
        if (m.extras.nestedContentProperty != null) pm.nestedContentProperty = m.extras.nestedContentProperty;
        if (m.extras.wrapInListItem === true) pm.wrapInListItem = m.extras.wrapInListItem;
        if (m.extras.positionProperty != null) pm.positionProperty = m.extras.positionProperty;
      }
      return pm;
    });
}

/** Serialise block rows to a `routes` array (the panel's target is the owning row). */
export function serialiseRoutes(rows: BlockMappingRow[]): BlockRoute[] {
  return rows
    .filter((r) => r.mapped && r.nestedSchemaType)
    .map((r) => {
      const route: BlockRoute = {
        blockAlias: r.alias,
        nestedSchemaType: r.nestedSchemaType,
        propertyMappings: serialisePropertyMappings(r.propertyMappings),
      };
      if (r.requiredProperties?.length) route.requiredProperties = r.requiredProperties;
      return route;
    });
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

/** Safe-parse a stored ResolverConfig JSON string; `null` for empty/invalid JSON. */
export function parseResolverConfig(json: string | null | undefined): RoutedResolverConfig | null {
  if (!json?.trim()) return null;
  try {
    const parsed = JSON.parse(json);
    return parsed && typeof parsed === 'object' ? (parsed as RoutedResolverConfig) : null;
  } catch {
    return null;
  }
}

/**
 * Seed per-block RowSeeds from a LEGACY flat config (`nestedMappings` + the mapping-level
 * `nestedSchemaTypeName`), matching the renderer's actual semantics: entries with an empty
 * or absent `blockAlias` are WILDCARD and apply to every block element type; entries with a
 * `blockAlias` apply only to that type (in addition to any wildcard entries). This is the
 * fix for the old modal keying a wildcard legacy config by `''` and matching no block.
 *
 * Returns a map keyed by LOWERCASED block alias → RowSeed. Block types with no applicable
 * entries are omitted (they stay unmapped).
 */
export function seedRowsFromLegacyConfig(
  blockTypes: BlockElementTypeInfo[],
  nestedMappings: BlockRoutePropertyMapping[],
  nestedSchemaTypeName: string | null | undefined,
): Map<string, RowSeed> {
  const seeds = new Map<string, RowSeed>();
  const wildcard = nestedMappings.filter((m) => !m.blockAlias);
  const byAlias = new Map<string, BlockRoutePropertyMapping[]>();
  for (const m of nestedMappings) {
    if (!m.blockAlias) continue;
    const key = m.blockAlias.toLowerCase();
    const list = byAlias.get(key) ?? [];
    list.push(m);
    byAlias.set(key, list);
  }
  for (const bt of blockTypes) {
    const entries = [...wildcard, ...(byAlias.get(bt.alias.toLowerCase()) ?? [])];
    if (entries.length === 0) continue;
    seeds.set(bt.alias.toLowerCase(), {
      nestedSchemaType: nestedSchemaTypeName ?? '',
      propertyMappings: seedEntriesFromRaw(entries, rowPropertyInfos(bt), false),
    });
  }
  return seeds;
}

/** A human-readable summary of a stored blockContent ResolverConfig, for the mapping table row. */
export type ResolverConfigSummary =
  | { kind: 'routes'; routes: Array<{ blockAlias: string; nestedSchemaType: string }> }
  | { kind: 'stringList'; contentProperty: string }
  | { kind: 'empty' };

/**
 * Summarise a stored config for display: routed configs list `blockAlias → type` pairs
 * (legacy flat configs summarise as a single wildcard pair using the mapping-level nested
 * type, since they apply to every block); string-list extraction reports its source
 * property; anything else is `empty`. `blockAlias === ''` means "any block" — callers
 * localise that label.
 */
export function summariseResolverConfig(
  resolverConfig: string | null | undefined,
  nestedSchemaTypeName?: string | null,
): ResolverConfigSummary {
  const config = parseResolverConfig(resolverConfig);
  if (config?.extractAs === 'stringList') {
    return { kind: 'stringList', contentProperty: config.contentProperty ?? '' };
  }
  if (config?.routes?.length) {
    return {
      kind: 'routes',
      routes: config.routes.map((r) => ({
        blockAlias: r.blockAlias ?? '',
        nestedSchemaType: r.nestedSchemaType ?? '',
      })),
    };
  }
  if (config?.nestedMappings?.length || (!config && nestedSchemaTypeName)) {
    // Legacy flat shape (or a bare nestedSchemaTypeName with auto-mapped properties):
    // one implicit route for every block, typed by the mapping-level nested type.
    const aliases = new Set((config?.nestedMappings ?? []).map((m) => m.blockAlias || ''));
    const list = aliases.size > 0 ? [...aliases] : [''];
    return {
      kind: 'routes',
      routes: list.map((blockAlias) => ({ blockAlias, nestedSchemaType: nestedSchemaTypeName ?? '' })),
    };
  }
  return { kind: 'empty' };
}
