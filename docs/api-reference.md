# API Reference

Complete reference for the SchemeWeaver management API. All endpoints are used by the backoffice UI and are available for programmatic access.

## Base URL

```
/umbraco/management/api/v1/schemeweaver
```

## Authentication

All endpoints require Umbraco backoffice authentication. Requests must include a valid backoffice authentication cookie or bearer token (using the `Umbraco.Cms.Core.Constants.Security.BackOfficeAuthenticationType` scheme).

Unauthenticated requests will receive a `401 Unauthorized` response.

---

## Schema Types

### GET /schema-types

Returns all available Schema.org types, or a filtered subset when a search query is provided. Types are sourced from the Schema.NET assembly at startup.

**Query Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `search` | `string` | No | Filter types by name (case-insensitive substring match) |

**Response**: `200 OK`

```json
[
  {
    "name": "Article",
    "description": "An article, such as a news article or piece of investigative report.",
    "parentTypeName": "CreativeWork",
    "propertyCount": 42
  }
]
```

**TypeScript Interface**

```typescript
interface SchemaTypeInfo {
  name: string;
  description: string | null;
  parentTypeName: string | null;
  propertyCount: number;
}
```

---

### GET /schema-types/{name}/properties

Returns all properties for a specific Schema.org type, including inherited properties from parent types.

**Path Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | `string` | Yes | Schema.org type name (e.g. `Article`, `Event`) |

**Response**: `200 OK`

```json
[
  {
    "name": "headline",
    "propertyType": "Text",
    "isRequired": false,
    "acceptedTypes": ["Text"],
    "isComplexType": false
  },
  {
    "name": "author",
    "propertyType": "Person or Organization",
    "isRequired": false,
    "acceptedTypes": ["Person", "Organization"],
    "isComplexType": true
  }
]
```

**TypeScript Interface**

```typescript
interface SchemaPropertyInfo {
  name: string;
  propertyType: string;
  isRequired: boolean;
  acceptedTypes: string[];
  isComplexType: boolean;
}
```

---

## Content Types

### GET /content-types

Returns all Umbraco content types (document types) with basic metadata.

**Response**: `200 OK`

```json
[
  {
    "alias": "blogPost",
    "name": "Blog Post",
    "key": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "propertyCount": 8
  }
]
```

Results are ordered alphabetically by name.

**TypeScript Interface**

```typescript
interface ContentTypeInfo {
  alias: string;
  name: string;
  key: string;
  propertyCount: number;
}
```

---

### GET /content-types/{alias}/properties

Returns all properties for a specific Umbraco content type, including built-in properties that SchemeWeaver makes available for mapping.

**Path Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `alias` | `string` | Yes | Umbraco content type alias (e.g. `blogPost`) |

**Response**: `200 OK`

Built-in properties appear first (prefixed with `__`), followed by the content type's custom properties.

```json
[
  {
    "alias": "__url",
    "name": "url",
    "editorAlias": "SchemeWeaver.BuiltIn",
    "description": null
  },
  {
    "alias": "__name",
    "name": "name",
    "editorAlias": "SchemeWeaver.BuiltIn",
    "description": null
  },
  {
    "alias": "__createDate",
    "name": "createDate",
    "editorAlias": "SchemeWeaver.BuiltIn",
    "description": null
  },
  {
    "alias": "__updateDate",
    "name": "updateDate",
    "editorAlias": "SchemeWeaver.BuiltIn",
    "description": null
  },
  {
    "alias": "title",
    "name": "Title",
    "editorAlias": "Umbraco.TextBox",
    "description": "The page title"
  }
]
```

**Status Codes**

| Status | Description |
|---|---|
| `200 OK` | Properties returned successfully |
| `404 Not Found` | Content type alias does not exist |

**TypeScript Interface**

```typescript
interface ContentTypeProperty {
  alias: string;
  name: string;
  editorAlias: string;
  description: string;
}
```

---

### GET /content-types/{contentTypeAlias}/properties/{propertyAlias}/block-types

Returns the block element types configured within a BlockList or BlockGrid property.

**Path Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `contentTypeAlias` | `string` | Yes | Umbraco content type alias |
| `propertyAlias` | `string` | Yes | Property alias of a BlockList or BlockGrid property |

**Response**: `200 OK`

```json
[
  {
    "alias": "featureBlock",
    "name": "Feature Block",
    "properties": ["heading", "description", "image"]
  }
]
```

**TypeScript Interface**

```typescript
interface BlockElementTypeInfo {
  alias: string;
  name: string;
  properties: string[];
}
```

---

## Mappings

### GET /mappings

Returns all schema mappings with their property mappings.

**Response**: `200 OK`

```json
[
  {
    "contentTypeAlias": "blogPost",
    "contentTypeKey": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "schemaTypeName": "Article",
    "isEnabled": true,
    "isInherited": false,
    "propertyMappings": [
      {
        "schemaPropertyName": "headline",
        "sourceType": "property",
        "contentTypePropertyAlias": "title",
        "sourceContentTypeAlias": null,
        "transformType": null,
        "isAutoMapped": true,
        "staticValue": null,
        "nestedSchemaTypeName": null,
        "resolverConfig": null
      }
    ]
  }
]
```

**TypeScript Interface**

```typescript
interface SchemaMappingDto {
  contentTypeAlias: string;
  contentTypeKey: string;
  schemaTypeName: string;
  isEnabled: boolean;
  isInherited: boolean;
  propertyMappings: PropertyMappingDto[];
}

interface PropertyMappingDto {
  schemaPropertyName: string;
  sourceType: string;
  contentTypePropertyAlias: string | null;
  sourceContentTypeAlias: string | null;
  transformType: string | null;
  isAutoMapped: boolean;
  staticValue: string | null;
  nestedSchemaTypeName: string | null;
  resolverConfig: string | null;
}
```

---

### GET /mappings/{contentTypeAlias}

Returns a single schema mapping for the specified content type.

**Path Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `contentTypeAlias` | `string` | Yes | Umbraco content type alias |

**Response**: `200 OK` -- returns `SchemaMappingDto` (see above)

**Status Codes**

| Status | Description |
|---|---|
| `200 OK` | Mapping found and returned |
| `404 Not Found` | No mapping exists for this content type |

---

### POST /mappings

Creates or updates a schema mapping. If a mapping already exists for the content type alias, it is overwritten.

**Request Body**: `SchemaMappingDto`

```json
{
  "contentTypeAlias": "blogPost",
  "contentTypeKey": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "schemaTypeName": "Article",
  "isEnabled": true,
  "isInherited": false,
  "propertyMappings": [
    {
      "schemaPropertyName": "headline",
      "sourceType": "property",
      "contentTypePropertyAlias": "title",
      "sourceContentTypeAlias": null,
      "transformType": null,
      "isAutoMapped": false,
      "staticValue": null,
      "nestedSchemaTypeName": null,
      "resolverConfig": null
    },
    {
      "schemaPropertyName": "@type",
      "sourceType": "static",
      "contentTypePropertyAlias": null,
      "sourceContentTypeAlias": null,
      "transformType": null,
      "isAutoMapped": false,
      "staticValue": "BlogPosting",
      "nestedSchemaTypeName": null,
      "resolverConfig": null
    }
  ]
}
```

**Response**: `200 OK` -- returns the saved `SchemaMappingDto`

**Status Codes**

| Status | Description |
|---|---|
| `200 OK` | Mapping saved successfully |
| `400 Bad Request` | `ContentTypeAlias` or `SchemaTypeName` is missing |

**Source Type Values**

The `sourceType` field accepts the following lowercase string values:

| Value | Description |
|---|---|
| `property` | Read from a property on the current content node |
| `static` | Use the `staticValue` field directly |
| `parent` | Read from the parent content node |
| `ancestor` | Read from an ancestor node (optionally filtered by `sourceContentTypeAlias`) |
| `sibling` | Read from a sibling node (optionally filtered by `sourceContentTypeAlias`) |
| `blockContent` | Read from block elements within a BlockList/BlockGrid property |
| `complexType` | Create a nested Schema.org type with sub-property mappings stored in `resolverConfig` |

**Transform Type Values**

The `transformType` field accepts the following values (or null for no transform):

| Value | Description |
|---|---|
| `stripHtml` | Removes HTML tags from the value |
| `toAbsoluteUrl` | Converts a relative URL to absolute using the request's scheme and host |
| `formatDate` | Formats a date string as `yyyy-MM-dd` |

---

### DELETE /mappings/{contentTypeAlias}

Deletes the schema mapping for the specified content type, including all its property mappings.

**Path Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `contentTypeAlias` | `string` | Yes | Umbraco content type alias |

**Response**: `204 No Content`

---

### POST /mappings/{contentTypeAlias}/auto-map

Runs the auto-mapping algorithm to suggest property mappings between the Umbraco content type and a Schema.org type. Returns a flat array of suggestions -- it does not persist anything.

**Path Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `contentTypeAlias` | `string` | Yes | Umbraco content type alias |

**Query Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `schemaTypeName` | `string` | Yes | Schema.org type name to map against |

**Response**: `200 OK`

```json
[
  {
    "schemaPropertyName": "headline",
    "schemaPropertyType": "Text",
    "suggestedContentTypePropertyAlias": "title",
    "suggestedSourceType": "property",
    "confidence": 85,
    "isAutoMapped": true,
    "editorAlias": "Umbraco.TextBox",
    "acceptedTypes": ["Text"],
    "isComplexType": false,
    "suggestedNestedSchemaTypeName": null,
    "suggestedResolverConfig": null
  },
  {
    "schemaPropertyName": "author",
    "schemaPropertyType": "Person or Organization",
    "suggestedContentTypePropertyAlias": null,
    "suggestedSourceType": "property",
    "confidence": 0,
    "isAutoMapped": false,
    "editorAlias": null,
    "acceptedTypes": ["Person", "Organization"],
    "isComplexType": true,
    "suggestedNestedSchemaTypeName": null,
    "suggestedResolverConfig": null
  }
]
```

**Confidence Thresholds**

The `confidence` field is an integer from 0 to 100. The UI interprets these thresholds:

| Range | Label |
|---|---|
| 80--100 | High confidence |
| 50--79 | Medium confidence |
| 0--49 | Low / no match |

**Status Codes**

| Status | Description |
|---|---|
| `200 OK` | Suggestions returned |
| `400 Bad Request` | `schemaTypeName` query parameter is missing |

**TypeScript Interface**

```typescript
interface PropertyMappingSuggestion {
  schemaPropertyName: string;
  schemaPropertyType: string | null;
  suggestedContentTypePropertyAlias: string | null;
  suggestedSourceType: string;
  confidence: number;
  isAutoMapped: boolean;
  editorAlias: string | null;
  acceptedTypes: string[];
  isComplexType: boolean;
  suggestedNestedSchemaTypeName?: string;
  suggestedResolverConfig?: string;
}
```

---

### POST /mappings/{contentTypeAlias}/preview

Generates a JSON-LD preview for a content type. Supports both real (published content) and mock preview modes.

**Path Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `contentTypeAlias` | `string` | Yes | Umbraco content type alias |

**Query Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `contentKey` | `Guid` | No | Published content GUID key. If provided, generates real JSON-LD from live content. If omitted, generates a mock preview. |

**Response**: `200 OK`

```json
{
  "jsonLd": "{\"@context\":\"https://schema.org\",\"@type\":\"Article\",\"headline\":\"My Blog Post\"}",
  "isValid": true,
  "errors": []
}
```

When validation errors are present:

```json
{
  "jsonLd": "{\"@context\":\"https://schema.org\",\"@type\":\"Article\"}",
  "isValid": false,
  "errors": ["Missing recommended property: headline"]
}
```

**Status Codes**

| Status | Description |
|---|---|
| `200 OK` | Preview generated (real or mock) |
| `404 Not Found` | `contentKey` was provided but the content does not exist in the published cache |
| `500 Internal Server Error` | Unable to access the Umbraco context |

**TypeScript Interface**

```typescript
interface JsonLdPreviewResponse {
  jsonLd: string;
  isValid: boolean;
  errors: string[];
}
```

---

## Content Type Generation

### POST /generate-content-type

Creates a new Umbraco document type from a Schema.org type definition. This is the reverse of mapping -- instead of mapping an existing content type to a schema, it generates a content type with properties based on selected Schema.org properties.

**Request Body**: `ContentTypeGenerationRequest`

```json
{
  "schemaTypeName": "Event",
  "documentTypeName": "Event Page",
  "documentTypeAlias": "eventPage",
  "selectedProperties": ["name", "description", "startDate", "endDate", "location", "image"],
  "propertyGroupName": "Content"
}
```

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `schemaTypeName` | `string` | Yes | -- | Schema.org type to generate from |
| `documentTypeName` | `string` | Yes | -- | Display name for the new document type |
| `documentTypeAlias` | `string` | Yes | -- | Alias for the new document type |
| `selectedProperties` | `string[]` | Yes | -- | Schema.org property names to create as Umbraco properties |
| `propertyGroupName` | `string` | No | `"Content"` | Tab/group name for the generated properties |

**Response**: `200 OK`

```json
{
  "key": "f1e2d3c4-b5a6-7890-cdef-1234567890ab"
}
```

The response contains the GUID key of the newly created document type.

**Status Codes**

| Status | Description |
|---|---|
| `200 OK` | Content type created successfully |
| `400 Bad Request` | `SchemaTypeName` or `DocumentTypeName` is missing |

**TypeScript Interface**

```typescript
interface ContentTypeGenerationRequest {
  schemaTypeName: string;
  documentTypeName: string;
  documentTypeAlias: string;
  selectedProperties: string[];
  propertyGroupName?: string;  // defaults to "Content"
}
```

---

## Error Handling

All endpoints follow standard HTTP status code conventions:

| Status Code | Meaning |
|---|---|
| `200 OK` | Request succeeded |
| `204 No Content` | Request succeeded with no response body (e.g. DELETE) |
| `400 Bad Request` | Missing required parameters; response body contains error message |
| `401 Unauthorized` | Not authenticated to the backoffice |
| `404 Not Found` | Requested resource does not exist |
| `500 Internal Server Error` | Unexpected server error |

Error responses for `400 Bad Request` return a plain string message describing the issue (e.g. `"ContentTypeAlias is required."`).
