/**
 * TypeScript interfaces aligned to C# API models (camelCase serialisation).
 * See: Models/Api/*.cs
 */

import type { SourceTypeValue } from '../constants/source-type.js';

/** Matches C# SchemaTypeInfo — returned by GET /schema-types */
export interface SchemaTypeInfo {
  name: string;
  description: string | null;
  parentTypeName: string | null;
  propertyCount: number;
}

/** Matches C# SchemaPropertyInfo — returned by GET /schema-types/{name}/properties */
export interface SchemaPropertyInfo {
  name: string;
  propertyType: string;
  isRequired: boolean;
  acceptedTypes: string[];
  isComplexType: boolean;
}

/** Matches C# RankedSchemaPropertyInfo — returned by GET /schema-types/{name}/properties?ranked=true */
export interface RankedSchemaPropertyInfo extends SchemaPropertyInfo {
  confidence: number;
  isPopular: boolean;
}

/** Matches anonymous type from GET /content-types */
export interface ContentTypeInfo {
  alias: string;
  name: string;
  key: string;
  propertyCount: number;
}

/** Matches anonymous type from GET /content-types/{alias}/properties */
export interface ContentTypeProperty {
  alias: string;
  name: string;
  editorAlias: string;
  description: string;
}

/** Matches C# PropertyMappingDto */
export interface PropertyMappingDto {
  schemaPropertyName: string;
  sourceType: SourceTypeValue;
  contentTypePropertyAlias: string | null;
  sourceContentTypeAlias: string | null;
  transformType: string | null;
  isAutoMapped: boolean;
  staticValue: string | null;
  nestedSchemaTypeName: string | null;
  resolverConfig: string | null;
  dynamicRootConfig: string | null;
  /**
   * For `reference` source type: the graph piece key (e.g. "organization")
   * whose @id this property resolves to.
   */
  targetPieceKey?: string | null;
}

/** Matches C# SchemaMappingDto */
export interface SchemaMappingDto {
  contentTypeAlias: string;
  contentTypeKey: string;
  schemaTypeName: string;
  isEnabled: boolean;
  isInherited: boolean;
  /**
   * Optional @id template. Tokens: {url}, {type}, {key}, {culture}, {siteUrl}.
   * When null/omitted the generator falls back to {url}#{type}.
   */
  idOverride?: string | null;
  propertyMappings: PropertyMappingDto[];
  /**
   * Output-only. How this content type emits JSON-LD: `routed-page`,
   * `composed-from-block` or `unknown`. Set by the server on read/save; ignored
   * on input. May be omitted by older backends.
   */
  reachability?: string | null;
  /**
   * Output-only. Structural range-compatibility warnings, keyed back to a
   * mapping row by `path === propertyMapping.schemaPropertyName`. Set by the
   * server on single read and save. May be omitted by older backends.
   */
  warnings?: ValidationIssue[];
}

/** Matches C# PropertyMappingSuggestion — returned as flat array by POST /mappings/{alias}/auto-map */
export interface PropertyMappingSuggestion {
  schemaPropertyName: string;
  schemaPropertyType: string | null;
  suggestedContentTypePropertyAlias: string | null;
  suggestedSourceType: SourceTypeValue;
  confidence: number;
  isAutoMapped: boolean;
  editorAlias: string | null;
  acceptedTypes: string[];
  isComplexType: boolean;
  suggestedNestedSchemaTypeName?: string;
  suggestedResolverConfig?: string;
  /** For `reference` source-type suggestions: the piece key (e.g. "organization") to ref. */
  suggestedTargetPieceKey?: string;
}

/** Matches C# BlockElementPropertyInfo — a single property on a block element type. */
export interface BlockElementPropertyInfo {
  alias: string;
  name: string;
  editorAlias: string;
  /**
   * When this property is itself a Block List/Grid (a block nested inside a block),
   * the element types allowed within it — resolved recursively (depth-capped) so the
   * UI and the suggester can route nested blocks. Empty/omitted for non-block properties.
   */
  nestedBlockElementTypes?: BlockElementTypeInfo[];
}

/** Matches C# BlockElementTypeInfo — returned by GET /content-types/{alias}/properties/{propertyAlias}/block-types */
export interface BlockElementTypeInfo {
  alias: string;
  name: string;
  /** Back-compat: plain property aliases. New callers should prefer `propertyInfos`. */
  properties: string[];
  /** Full per-property info (alias, name, editor alias). Additive — may be omitted by older backends. */
  propertyInfos?: BlockElementPropertyInfo[];
}

/**
 * A single property mapping within a block route: block content property → nested schema property.
 * Matches C# NestedPropertyMapping (the stored/save shape, camelCase).
 *
 * The shape is recursive: when `contentProperty` points at a block element property that is
 * itself a Block List/Grid, `routes` maps that nested block's element types to their own
 * Schema.org types — exactly like the top-level {@link BlockRoute} list. The C# resolver
 * (`BlockContentResolver.NestedPropertyMapping`) reads `routes` and recurses.
 */
export interface BlockRoutePropertyMapping {
  schemaProperty: string;
  contentProperty: string;
  wrapInType?: string | null;
  wrapInProperty?: string | null;
  /** Recursive: routes for the nested Block List/Grid this property's value points at. */
  routes?: BlockRoute[];
  /**
   * When the nested block list should be flattened to a `string[]` (rather than nested
   * Things) — e.g. nested "ingredient" blocks feeding `recipeIngredient`.
   */
  extractAs?: string | null;
  /** Inner block property alias read when `extractAs === 'stringList'`. */
  nestedContentProperty?: string | null;
}

/**
 * A route for one block element type: which Schema.org type to instantiate for blocks
 * of this element type, and the per-property mappings to apply. Matches C# BlockRoute.
 * `blockAlias === ''` is the wildcard ("any block").
 */
export interface BlockRoute {
  blockAlias: string;
  nestedSchemaType: string;
  propertyMappings: BlockRoutePropertyMapping[];
}

/**
 * The routed ResolverConfig shape stored on a `blockContent` PropertyMappingDto.
 * Serialised (JSON.stringify) into `PropertyMappingDto.resolverConfig`.
 */
export interface RoutedResolverConfig {
  routes: BlockRoute[];
}

/**
 * Matches C# BlockRoutePropertyMappingSuggestion — a suggested property mapping within a
 * suggested route. Like {@link BlockRoutePropertyMapping} but its nested `routes` carry the
 * suggestion shape (with `confidence`).
 */
export interface BlockRoutePropertyMappingSuggestion {
  schemaProperty: string;
  contentProperty: string;
  wrapInType?: string | null;
  wrapInProperty?: string | null;
  /** Suggested nested routes when `contentProperty` is itself a block list. */
  routes?: BlockRouteSuggestion[];
}

/** Matches C# BlockRouteSuggestion — a suggested route for one block element type. */
export interface BlockRouteSuggestion {
  blockAlias: string;
  nestedSchemaType: string;
  confidence: number;
  propertyMappings: BlockRoutePropertyMappingSuggestion[];
}

/**
 * Matches C# BlockMappingSuggestion — returned as a flat array by
 * POST /content-types/{alias}/properties/{propertyAlias}/block-suggest.
 * One suggestion per TARGET page property (mainEntity | hasPart | about | …),
 * carrying the block-element routes that feed that target.
 */
export interface BlockMappingSuggestion {
  schemaProperty: string;
  confidence: number;
  routes: BlockRouteSuggestion[];
}

/**
 * Severity of a validator finding. Kept as a string union so it serialises
 * verbatim to/from the C# `ValidationIssue.Severity` enum (lower-case JSON).
 */
export type ValidationIssueSeverity = 'critical' | 'warning' | 'info';

/**
 * Matches C# `ValidationIssue` on `JsonLdPreviewResponse.Issues`.
 * Each finding carries enough context (schema type, JSON path, human-readable
 * message) to render a Rich Results / SEO-style validation panel in the UI.
 */
export interface ValidationIssue {
  severity: ValidationIssueSeverity;
  schemaType: string;
  path: string;
  message: string;
}

/** Matches C# JsonLdPreviewResponse */
export interface JsonLdPreviewResponse {
  jsonLd: string;
  isValid: boolean;
  /**
   * Legacy string-only messages populated alongside `issues` for backwards
   * compatibility. Frontends should prefer `issues` when rendering detailed
   * panels — `errors` only ever contains critical-level messages.
   */
  errors: string[];
  /** Structured validator findings grouped by severity. May be omitted by older backends. */
  issues?: ValidationIssue[];
  /**
   * The context this preview was generated in — always `backoffice-preview`
   * from the management API. A reminder that `isValid` reflects
   * backoffice-context structural validity only. May be omitted by older backends.
   */
  context?: string;
  /**
   * The base URL the generator resolved for `@id`/`url` tokens. In the
   * backoffice this is the backoffice host, so preview URLs can diverge from
   * the live render. May be omitted by older backends.
   */
  resolvedBaseUrl?: string | null;
}

/** Matches C# SchemaTypeSuggestion from SchemeWeaver.AI */
export interface SchemaTypeSuggestion {
  schemaTypeName: string;
  confidence: number;
  reasoning: string | null;
}

/** Matches C# BulkSchemaTypeSuggestion from SchemeWeaver.AI */
export interface BulkSchemaTypeSuggestion {
  contentTypeAlias: string;
  contentTypeName: string | null;
  suggestions: SchemaTypeSuggestion[];
}

/** Matches C# ContentTypeGenerationRequest — sent to POST /generate-content-type */
export interface ContentTypeGenerationRequest {
  schemaTypeName: string;
  documentTypeName: string;
  documentTypeAlias: string;
  selectedProperties: string[];
  propertyGroupName?: string;
}
