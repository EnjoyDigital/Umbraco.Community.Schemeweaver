# Umbraco.Community.SchemeWeaver

Map Umbraco Content Types to [Schema.org](https://schema.org) types and automatically generate [JSON-LD](https://json-ld.org/) structured data for your pages. SchemeWeaver provides a backoffice UI for configuring mappings, an auto-mapper that suggests property assignments, and runtime JSON-LD generation that works with both server-rendered templates and the headless Delivery API.

## Why structured data?

Search engines use JSON-LD to understand page content. A blog post tagged as `BlogPosting` with a `headline`, `author`, and `datePublished` can appear as a rich result in Google, Bing, and other search engines. Manually maintaining JSON-LD scripts is tedious and error-prone -- SchemeWeaver automates it from your existing content.

## Features

- **500+ Schema.org types** -- discovers every type in the [Schema.NET](https://github.com/RehanSaeed/Schema.NET) library at startup (Article, Product, FAQPage, Event, Person, Organisation, and hundreds more)
- **Auto-mapping with confidence scores** -- suggests property mappings using exact matching, synonym dictionaries, and substring matching, each with a transparency score so you can verify suggestions
- **Seven source types** -- pull values from the current node, a static value, the parent node, an ancestor of a specific type, a sibling node, block content, or nested complex types
- **Transforms** -- strip HTML, convert to absolute URL, or format dates before output
- **Backoffice dashboard** -- bulk view and manage all content type mappings from the Settings section
- **Workspace view** -- edit the Schema.org mapping directly from the Content Type editor
- **Entity actions** -- right-click a Content Type to map it or generate a new one from a schema
- **Content Type generation** -- scaffold a new Umbraco document type from any Schema.org type, with properties pre-grouped into Content, SEO, and Metadata tabs
- **Delivery API integration** -- JSON-LD is automatically indexed and available via the `schemaOrg` field in API responses
- **Tag helper** -- drop `<scheme-weaver content="@Model" />` into any Razor template to render the JSON-LD script tag
- **CSP nonce support** -- add `nonce="@cspNonce"` or `nonce-data-attribute` to the tag helper for Content Security Policy protected sites
- **Inherited schemas** -- mark a mapping as inherited and it outputs on all descendant pages (e.g. Organisation schema on the homepage cascading site-wide)
- **Automatic block traversal** -- BlockList/BlockGrid elements with their own schema mappings are automatically rendered as separate JSON-LD blocks, zero configuration needed
- **BreadcrumbList auto-generation** -- a BreadcrumbList JSON-LD block is automatically generated from the content's ancestor hierarchy
- **@id for AI discoverability** -- each schema output includes an `@id` set to the content's absolute URL
- **Localisation** -- all UI strings use Umbraco's localisation system

## Requirements

- Umbraco 17+
- .NET 10

## Installation

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

No additional configuration needed. The package registers all services, creates its database tables on first run, and adds the backoffice UI automatically.

## How it works

### The mapping model

Each mapping connects one Umbraco **Content Type** to one **Schema.org type**:

```
Content Type: blogPost
       maps to
Schema.org Type: BlogPosting
```

Within that mapping, individual **property mappings** define where each schema property gets its value:

| Schema Property | Source Type | Value | Description |
|---|---|---|---|
| `headline` | property | `title` | Read from the current node's `title` property |
| `author` | static | `Jane Smith` | Hardcoded string value |
| `datePublished` | property | `publishDate` | Read from `publishDate`, formatted as ISO date |
| `publisher` | parent | `organisationName` | Read from the parent node |
| `articleSection` | ancestor | `categoryName` on `blogRoot` | Walk up the tree to find a `blogRoot` node, read its `categoryName` |
| `about` | sibling | `topicDescription` on `topicPage` | Find a sibling of type `topicPage`, read its `topicDescription` |

### Source types explained

| Source | Behaviour |
|---|---|
| **property** | Reads a property from the current content node |
| **static** | Uses a hardcoded string value (useful for `@type`, organisation names, etc.) |
| **parent** | Reads a property from the immediate parent node |
| **ancestor** | Walks up the content tree to find the first ancestor matching a specific content type alias, then reads the property |
| **sibling** | Looks at the parent's children to find the first sibling matching a specific content type alias, then reads the property |
| **blockContent** | Extracts values from BlockList/BlockGrid items, creating nested Schema.org objects |
| **complexType** | Creates a nested Schema.org type with its own sub-property mappings |

### Auto-mapping algorithm

When you map a content type to a schema type, the auto-mapper suggests property assignments using three tiers:

1. **Exact match (100% confidence)** -- property alias matches schema property name exactly (case-insensitive)
2. **Synonym match (80% confidence)** -- uses a built-in dictionary of common aliases:
   - `name` matches `title`, `heading`, `pageTitle`, `nodeName`
   - `description` matches `metaDescription`, `excerpt`, `summary`
   - `articleBody` matches `content`, `bodyText`, `richText`, `mainContent`
   - `image` matches `heroImage`, `mainImage`, `thumbnail`, `featuredImage`
   - `datePublished` matches `publishDate`, `createDate`, `articleDate`
   - ...and 20+ more synonym groups
3. **Partial match (50% confidence)** -- schema property name appears as a substring in the Umbraco property alias (e.g., `name` matches `authorName`)

Unmatched schema properties appear with 0% confidence so you can configure them manually.

### JSON-LD generation

At runtime (on publish or page request), the `JsonLdGenerator` service:

1. Looks up the mapping for the content type
2. Creates a Schema.NET object (e.g., `BlogPosting`)
3. For each property mapping, resolves the value using the configured source type
4. Applies any transforms (strip HTML, format date, make URL absolute)
5. Sets the value on the Schema.NET object using reflection and implicit type operators
6. Serialises to JSON-LD

The generated output looks like:

```json
{
  "@context": "https://schema.org",
  "@type": "BlogPosting",
  "headline": "10 Tips for Better SEO",
  "author": {
    "@type": "Person",
    "name": "Jane Smith"
  },
  "datePublished": "2024-01-15",
  "articleBody": "Full article content here..."
}
```

## Usage

### Option 1: Tag helper (server-rendered sites)

In any Razor template or view:

```html
@addTagHelper *, Umbraco.Community.SchemeWeaver

<scheme-weaver content="@Model" />
```

This renders `<script type="application/ld+json">` tags with the generated JSON-LD (main schema, BreadcrumbList, inherited ancestor schemas, and auto-discovered block element schemas). If the content type has no mapping or the mapping is disabled, nothing is rendered.

For CSP-protected sites, add a nonce:

```html
<scheme-weaver content="@Model" nonce="@cspNonce" />

@* Or use data-nonce attribute instead *@
<scheme-weaver content="@Model" nonce="@cspNonce" nonce-data-attribute />
```

### Option 2: Delivery API (headless sites)

JSON-LD is automatically indexed when content is published. Fetch it from the Delivery API response:

```typescript
const response = await fetch('/umbraco/delivery/api/v2/content/item/my-blog-post');
const data = await response.json();

// JSON-LD is in the schemaOrg field
const jsonLd = data.properties.schemaOrg;
if (jsonLd) {
  const script = document.createElement('script');
  script.type = 'application/ld+json';
  script.textContent = jsonLd;
  document.head.appendChild(script);
}
```

### Option 3: Direct service injection

```csharp
@inject IJsonLdGenerator JsonLdGenerator

@{
    var jsonLd = JsonLdGenerator.GenerateJsonLdString(Model);
    if (!string.IsNullOrEmpty(jsonLd))
    {
        <script type="application/ld+json">@Html.Raw(jsonLd)</script>
    }
}
```

## Backoffice UI

### Dashboard

Navigate to **Settings > Schema.org Mappings** to see all content types with their mapping status:

- **Mapped** content types show the Schema.org type, property count, and action buttons (Edit, Preview, Delete)
- **Unmapped** content types have a "Map" button to start the mapping flow
- Search and filter content types by name or alias

### Mapping flow

1. Click **Map** on an unmapped content type
2. **Schema Picker** modal opens -- browse or search 500+ Schema.org types, grouped by parent type
3. Select a type and click **Select**
4. **Property Mapping** modal opens -- auto-mapped suggestions appear with confidence badges:
   - **High** (green, 80%+) -- very likely correct
   - **Medium** (amber, 50%+) -- probable match, worth checking
   - **Low** (red, <50%) -- needs manual review
5. Adjust source types and values as needed
6. Click **Generate Preview** to see the JSON-LD output
7. Click **Save** to persist the mapping

### Content Type generation

From the dashboard or via entity action, you can generate a new Umbraco Content Type from any Schema.org type:

1. Select a Schema.org type
2. Choose which properties to include
3. SchemeWeaver creates the Content Type with:
   - Properties mapped to appropriate editors (Textstring, Rich Text, Date Picker, etc.)
   - Properties grouped into Content, SEO, and Metadata tabs
   - The Schema.org mapping pre-configured

## Database

SchemeWeaver creates two tables on first run via Umbraco's migration system:

**SchemeWeaverSchemaMapping**

| Column | Type | Description |
|---|---|---|
| Id | int (PK) | Auto-increment |
| ContentTypeAlias | string (unique) | Umbraco content type alias |
| ContentTypeKey | Guid | Umbraco content type key |
| SchemaTypeName | string | Schema.org type name (e.g., "BlogPosting") |
| IsEnabled | bool | Whether JSON-LD generation is active |
| IsInherited | bool | Whether this schema is also output on all descendant pages |
| CreatedDate | DateTime | When the mapping was created |
| UpdatedDate | DateTime | Last modification time |

**SchemeWeaverPropertyMapping**

| Column | Type | Description |
|---|---|---|
| Id | int (PK) | Auto-increment |
| SchemaMappingId | int (FK) | References SchemaMapping |
| SchemaPropertyName | string | Schema.org property (e.g., "headline") |
| SourceType | string | "property", "static", "parent", "ancestor", "sibling", "blockContent", or "complexType" |
| ContentTypePropertyAlias | string? | Which Umbraco property to read |
| SourceContentTypeAlias | string? | For ancestor/sibling: which content type to look for |
| TransformType | string? | "stripHtml", "toAbsoluteUrl", or "formatDate" |
| IsAutoMapped | bool | Whether this was created by the auto-mapper |
| StaticValue | string? | Hardcoded value (when source type is "static") |
| NestedSchemaTypeName | string? | For complex nested schema types |
| ResolverConfig | text? | JSON configuration for nested property mappings and block content extraction |

## API reference

All endpoints are under `/umbraco/management/api/v1/schemeweaver` and require backoffice authentication.

### Schema types

| Method | Endpoint | Description |
|---|---|---|
| GET | `/schema-types?search={query}` | List or search Schema.org types |
| GET | `/schema-types/{name}/properties` | Get properties for a schema type |

### Content types

| Method | Endpoint | Description |
|---|---|---|
| GET | `/content-types` | List all Umbraco content types |
| GET | `/content-types/{alias}/properties` | Get properties for a content type |

### Mappings

| Method | Endpoint | Description |
|---|---|---|
| GET | `/mappings` | Get all mappings |
| GET | `/mappings/{alias}` | Get mapping for a content type |
| POST | `/mappings` | Create or update a mapping |
| DELETE | `/mappings/{alias}` | Delete a mapping |
| POST | `/mappings/{alias}/auto-map?schemaTypeName={name}` | Get auto-mapping suggestions |
| POST | `/mappings/{alias}/preview?contentKey={key}` | Preview JSON-LD for a content item |

### Content type generation

| Method | Endpoint | Description |
|---|---|---|
| POST | `/generate-content-type` | Create a content type from a schema |

## Architecture

```
Umbraco Backoffice
    |
    v
SchemeWeaver Dashboard / Modals / Workspace View (Lit Web Components)
    |
    v
SchemeWeaverServerDataSource (fetch wrapper)
    |
    v
SchemeWeaverApiController (Management API)
    |
    v
SchemeWeaverService (orchestrator)
    |
    +---> SchemaTypeRegistry (singleton, scans Schema.NET assembly)
    +---> SchemaMappingRepository (NPoco, reads/writes database)
    +---> SchemaAutoMapper (suggests property mappings)
    +---> JsonLdGenerator (produces JSON-LD from IPublishedContent)
    +---> ContentTypeGenerator (creates Umbraco content types)

Runtime Output:
    JsonLdGenerator -----> TagHelper (<scheme-weaver> in Razor)
    JsonLdGenerator -----> SchemaJsonLdContentIndexHandler (Delivery API)
```

## Development

### Prerequisites

- .NET 10 SDK
- Node.js 22+

### Build

```bash
# Full solution (backend + TestHost)
dotnet build

# Frontend (from App_Plugins/SchemeWeaver/)
cd src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver
npm install
npm run build    # builds to ../../wwwroot/dist/
```

### Test

```bash
# C# unit tests (215 tests)
dotnet test

# Frontend unit + component tests
cd src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver
npm test
```

### TestHost

A runnable Umbraco 17 instance is included for local development and testing:

```bash
dotnet run --project src/SchemeWeaver.TestHost
```

Login at `http://localhost:5000/umbraco` with `admin@test.com` / `SecurePass1234`.

### Pack

```bash
dotnet pack src/Umbraco.Community.SchemeWeaver/Umbraco.Community.SchemeWeaver.csproj \
  --configuration Release --output ./artifacts
```

## Licence

MIT -- see [LICENSE](LICENSE).
