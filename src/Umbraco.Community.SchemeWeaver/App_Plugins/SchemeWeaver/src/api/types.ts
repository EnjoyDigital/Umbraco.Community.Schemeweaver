/**
 * TypeScript interfaces aligned to C# API models (camelCase serialisation).
 * See: Models/Api/*.cs
 */

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
  sourceType: string;
  contentTypePropertyAlias: string | null;
  sourceContentTypeAlias: string | null;
  transformType: string | null;
  isAutoMapped: boolean;
  staticValue: string | null;
  nestedSchemaTypeName: string | null;
}

/** Matches C# SchemaMappingDto */
export interface SchemaMappingDto {
  contentTypeAlias: string;
  contentTypeKey: string;
  schemaTypeName: string;
  isEnabled: boolean;
  propertyMappings: PropertyMappingDto[];
}

/** Matches C# PropertyMappingSuggestion — returned as flat array by POST /mappings/{alias}/auto-map */
export interface PropertyMappingSuggestion {
  schemaPropertyName: string;
  schemaPropertyType: string | null;
  suggestedContentTypePropertyAlias: string | null;
  suggestedSourceType: string;
  confidence: number;
  isAutoMapped: boolean;
}

/** Matches C# JsonLdPreviewResponse */
export interface JsonLdPreviewResponse {
  jsonLd: string;
  isValid: boolean;
  errors: string[];
}

/** Matches C# ContentTypeGenerationRequest — sent to POST /generate-content-type */
export interface ContentTypeGenerationRequest {
  schemaTypeName: string;
  documentTypeName: string;
  documentTypeAlias: string;
  selectedProperties: string[];
  propertyGroupName?: string;
}
