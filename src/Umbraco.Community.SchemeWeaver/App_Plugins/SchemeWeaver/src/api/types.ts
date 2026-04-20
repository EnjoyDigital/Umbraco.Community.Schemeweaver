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

/** Matches C# BlockElementTypeInfo — returned by GET /content-types/{alias}/properties/{propertyAlias}/block-types */
export interface BlockElementTypeInfo {
  alias: string;
  name: string;
  properties: string[];
}

/** Matches C# JsonLdPreviewResponse */
export interface JsonLdPreviewResponse {
  jsonLd: string;
  isValid: boolean;
  errors: string[];
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
