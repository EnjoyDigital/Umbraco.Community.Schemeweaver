# uSync Integration

SchemeWeaver provides an optional uSync addon package that serializes schema mappings to XML files for deployment between environments. This means you can configure mappings in your development environment and deploy them to staging and production via source control, without manually recreating them.

> Using **Umbraco Deploy or Umbraco Cloud** instead of uSync? See the equivalent [Umbraco Deploy Integration](deploy.md) addon.

---

## Requirements

| Requirement | Version |
|---|---|
| Umbraco.Community.SchemeWeaver | Same major-aligned version as the uSync addon |
| uSync | 17.x (on Umbraco 17) or 18.x (on Umbraco 18) |

The addon ships one stable build per Umbraco major, matching the main package: the Umbraco 17 build depends on uSync 17.x, the Umbraco 18 build on uSync 18.x. NuGet selects the build matching your Umbraco major automatically.

## Installation

Install the uSync addon alongside SchemeWeaver and uSync:

```bash
dotnet add package Umbraco.Community.SchemeWeaver.uSync
```

The `SchemeWeaverUSyncComposer` registers the addon on startup; uSync discovers the serializer and the dashboard handler automatically.

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
| `IdOverride` | Optional `@id` template overriding the default `{url}#{type}` convention (see [`@id` precedence](json-ld-output.md#id-precedence)) |

### PropertyMappings (multiple per SchemaMapping)

| Field | Description |
|---|---|
| `SchemaPropertyName` | Schema.org property name (e.g. `headline`, `author`) |
| `SourceType` | Value source: `property`, `static`, `parent`, `ancestor`, `sibling`, `blockContent`, `complexType`, `reference` |
| `ContentTypePropertyAlias` | Umbraco property alias to read from |
| `SourceContentTypeAlias` | Content type filter for parent/ancestor/sibling sources |
| `TransformType` | Transform to apply: `stripHtml`, `toAbsoluteUrl`, `formatDate` |
| `IsAutoMapped` | Whether this mapping was created by auto-map |
| `StaticValue` | Fixed value for static source type |
| `NestedSchemaTypeName` | Nested Schema.org type for complex type mappings |
| `ResolverConfig` | JSON configuration for property resolvers (e.g. block content sub-mappings) |
| `DynamicRootConfig` | JSON configuration for Umbraco dynamic root settings (origin and query steps) used by `parent`/`ancestor`/`sibling` sources |
| `TargetPieceKey` | For the `reference` source type: the key of the graph piece whose `@id` the property resolves to (e.g. `organization`) |

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

Optional fields (`IdOverride`, `SourceContentTypeAlias`, `TransformType`, `StaticValue`, `NestedSchemaTypeName`, `ResolverConfig`, `DynamicRootConfig`, `TargetPieceKey`) are omitted from the XML when null. `ResolverConfig` and `DynamicRootConfig` use CDATA to preserve JSON formatting.

---

## Workflow

1. **Configure mappings** in your development environment using the backoffice UI
2. **Export** via uSync (the serializer converts database records to XML files)
3. **Commit** the exported XML files to source control
4. **Deploy** to staging/production
5. **Import** via uSync on the target environment (XML files are deserialized back to database records)

The serializer handles upserts: if a mapping already exists for a content type alias, it is updated rather than duplicated.

---

## The uSync Dashboard Handler

SchemeWeaver ships a full uSync handler (`SchemaMappingHandler`), making schema mappings a first-class uSync entity. In the uSync dashboard they appear as a **Schemas** row (with SchemeWeaver's brackets icon) and take part in **Import All** and **Export All** alongside document types and the rest of your settings. Exports are written as flat `{alias}.config` files under `uSync/{version}/SchemeWeaverMappings/`, the same folder the export-on-save handler and the boot importer use, so files round-trip cleanly whichever route produced them.

---

## Export on Save

By default, saving a mapping in the backoffice writes to the database only. Set the SchemeWeaver-owned `ExportMappingsToUSyncOnSave` option to `true` (default `false`) and the addon exports the mapping to the uSync data folder on every backoffice save or delete, so the change is ready to commit to source control:

```json
{
  "SchemeWeaver": {
    "ExportMappingsToUSyncOnSave": true
  }
}
```

The flag is deliberately independent of uSync's global `ExportOnSave`, so enabling doc-type export-on-save never silently starts writing mapping files.

---

## Boot Import Modes

The `USyncBootImport` option controls how committed mapping `.config` files are imported when the application starts:

| Mode | Behaviour |
|---|---|
| `Off` (default) | First-boot-only seeding: import all configs only when the database has zero mappings; once populated, do nothing on boot. Backoffice edits always survive restarts. |
| `Seed` | Create-missing on every boot: import a config only when no mapping with that alias exists in the database. Never overwrites an existing mapping, so backoffice edits survive, but a committed config for a backoffice-deleted mapping is recreated on restart. |
| `Upsert` | Disk wins on every boot: import and overwrite all configs from disk on each start (full config-as-code). Unexported backoffice edits are overwritten on restart. |

---

## Drift and Export Endpoints

Two management API endpoints let you inspect and reconcile the database against the on-disk uSync files programmatically:

- `GET /umbraco/management/api/v1/schemeweaver/mappings/drift` reports each mapping's drift status (`in-sync`, `db-only`, `disk-only`, `content-differs`, or `usync-unavailable` when the addon is not installed).
- `POST /umbraco/management/api/v1/schemeweaver/mappings/export` exports all mappings (or a single one, when the request body names a `contentTypeAlias`) to the uSync data folder on demand.

See the [API Reference](api-reference.md) for request and response shapes.

---

## The ExportToUSync Advisory

When the uSync addon is installed but a save only reached the database (export-on-save is off and the mapping has not been exported), SchemeWeaver raises a Suggestion-severity **ExportToUSync** advisory in the mapping's validation issues: the mapping works locally but will not reproduce on other environments until exported. Export via the dashboard, via the endpoint above, or enable `ExportMappingsToUSyncOnSave`.

---

## Further Reading

- **[Getting Started](getting-started.md)**: installation and first mapping
- **[Extending SchemeWeaver](extending.md)**: replacing the `ISchemaMappingRepository` for custom persistence
