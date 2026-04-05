# uSync Integration

SchemeWeaver provides an optional uSync addon package that serializes schema mappings to XML files for deployment between environments. This means you can configure mappings in your development environment and deploy them to staging and production via source control, without manually recreating them.

---

## Requirements

| Requirement | Version |
|---|---|
| Umbraco.Community.SchemeWeaver | Same version as the uSync addon |
| uSync | 17.x (uSync.Core 17.0.4 or later) |

## Installation

Install the uSync addon alongside SchemeWeaver and uSync:

```bash
dotnet add package Umbraco.Community.SchemeWeaver.uSync --prerelease
```

The `SchemeWeaverUSyncComposer` registers the serializer with uSync automatically on startup.

---

## What Gets Synced

The serializer exports and imports the complete mapping configuration for each content type:

### SchemaMapping (one per content type)

| Field | Description |
|---|---|
| `ContentTypeAlias` | The Umbraco content type alias (used as the unique key) |
| `ContentTypeKey` | The content type's GUID key |
| `SchemaTypeName` | The Schema.org type name (e.g. `BlogPosting`, `Product`) |
| `IsEnabled` | Whether JSON-LD generation is active for this mapping |
| `IsInherited` | Whether this schema is output on all descendant pages |

### PropertyMappings (multiple per SchemaMapping)

| Field | Description |
|---|---|
| `SchemaPropertyName` | Schema.org property name (e.g. `headline`, `author`) |
| `SourceType` | Value source: `property`, `static`, `parent`, `ancestor`, `sibling`, `blockContent`, `complexType` |
| `ContentTypePropertyAlias` | Umbraco property alias to read from |
| `SourceContentTypeAlias` | Content type filter for parent/ancestor/sibling sources |
| `TransformType` | Transform to apply: `stripHtml`, `toAbsoluteUrl`, `formatDate` |
| `IsAutoMapped` | Whether this mapping was created by auto-map |
| `StaticValue` | Fixed value for static source type |
| `NestedSchemaTypeName` | Nested Schema.org type for complex type mappings |
| `ResolverConfig` | JSON configuration for property resolvers (e.g. block content sub-mappings) |

---

## XML Format

Each mapping is serialized as an XML file. Here is an example:

```xml
<SchemeWeaverMapping Key="a1b2c3d4-e5f6-..." Alias="blogPost">
  <Info>
    <ContentTypeAlias>blogPost</ContentTypeAlias>
    <ContentTypeKey>a1b2c3d4-e5f6-...</ContentTypeKey>
    <SchemaTypeName>BlogPosting</SchemaTypeName>
    <IsEnabled>true</IsEnabled>
    <IsInherited>false</IsInherited>
  </Info>
  <PropertyMappings>
    <PropertyMapping>
      <SchemaPropertyName>headline</SchemaPropertyName>
      <SourceType>property</SourceType>
      <ContentTypePropertyAlias>title</ContentTypePropertyAlias>
      <IsAutoMapped>true</IsAutoMapped>
    </PropertyMapping>
    <PropertyMapping>
      <SchemaPropertyName>mainEntity</SchemaPropertyName>
      <SourceType>blockContent</SourceType>
      <ContentTypePropertyAlias>faqItems</ContentTypePropertyAlias>
      <NestedSchemaTypeName>Question</NestedSchemaTypeName>
      <ResolverConfig><![CDATA[{"mappings":[...]}]]></ResolverConfig>
      <IsAutoMapped>false</IsAutoMapped>
    </PropertyMapping>
  </PropertyMappings>
</SchemeWeaverMapping>
```

Optional fields (`SourceContentTypeAlias`, `TransformType`, `StaticValue`, `NestedSchemaTypeName`, `ResolverConfig`) are omitted from the XML when null. `ResolverConfig` uses CDATA to preserve JSON formatting.

---

## Workflow

1. **Configure mappings** in your development environment using the backoffice UI
2. **Export** via uSync (the serializer converts database records to XML files)
3. **Commit** the exported XML files to source control
4. **Deploy** to staging/production
5. **Import** via uSync on the target environment (XML files are deserialized back to database records)

The serializer handles upserts — if a mapping already exists for a content type alias, it is updated rather than duplicated.

---

## Limitations

The current uSync integration provides a **serializer** for data conversion between XML and the database. A full uSync **handler** (which enables automatic discovery of items during dashboard export/import operations) is not yet implemented because SchemeWeaver's entity model does not implement Umbraco's `IEntity` interface. The serializer can be used programmatically via uSync's serializer collection for custom import/export workflows.

---

## Further Reading

- **[Getting Started](getting-started.md)** -- installation and first mapping
- **[Extending SchemeWeaver](extending.md)** -- replacing the `ISchemaMappingRepository` for custom persistence
