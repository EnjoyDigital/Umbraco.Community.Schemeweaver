# SchemeWeaver

Map Umbraco Content Types to Schema.org types and automatically generate JSON-LD structured data for your pages.

## Features

- **780+ Schema.org types** -- every type in the Schema.NET library, including pending types
- **Auto-mapping with confidence scores** -- suggests property mappings using exact matching, synonym dictionaries, and substring matching
- **Seven source types** -- pull values from the current node, a static value, the parent, an ancestor, a sibling, block content, or nested complex types
- **Transforms** -- strip HTML, convert to absolute URL, or format dates before output
- **Content Type generation** -- scaffold a new Umbraco document type from any Schema.org type
- **Language variants** -- culture-aware JSON-LD generation for multi-language sites with automatic `inLanguage` population
- **Delivery API integration** -- JSON-LD is automatically indexed per culture and available via the `schemaOrg` field
- **Tag helper** -- drop `<scheme-weaver content="@Model" />` into any Razor template
- **Inherited schemas** -- mark a mapping as inherited and it outputs on all descendant pages
- **BreadcrumbList** -- automatically generated from the content's ancestor hierarchy

## Requirements

- Umbraco 17+
- .NET 10

## Installation

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

No additional configuration needed. The package registers all services, creates its database tables on first run, and adds the backoffice UI automatically.

## Quick Start

### 1. Add the tag helper

In your master layout (e.g. `_Layout.cshtml`):

```html
@addTagHelper *, Umbraco.Community.SchemeWeaver

<head>
    ...
    <scheme-weaver content="@Model" />
</head>
```

### 2. Map your content types

1. Open any document type in **Settings > Document Types**
2. Click the **Schema.org** tab
3. Click **Map to Schema.org** and select a type (e.g. Product, Article, Event)
4. Review the auto-suggested property mappings and click **Save**
5. Publish content -- JSON-LD appears in the page source

### 3. Headless / Delivery API

JSON-LD is automatically indexed when content is published:

```typescript
const response = await fetch('/umbraco/delivery/api/v2/content/item/my-blog-post');
const data = await response.json();
const jsonLd = data.properties.schemaOrg;
```

## How it works

Each mapping connects one Umbraco Content Type to one Schema.org type. Within that mapping, individual property mappings define where each schema property gets its value:

| Schema Property | Source | Value | Description |
|---|---|---|---|
| `headline` | property | `title` | Read from the current node |
| `author` | static | `Jane Smith` | Hardcoded string value |
| `datePublished` | property | `publishDate` | Formatted as ISO date |
| `publisher` | parent | `organisationName` | Read from the parent node |
| `mainEntity` | blockContent | `faqItems` | Built from BlockList items |

## Documentation

Full documentation, source code, and contribution guidelines at [github.com/EnjoyDigital/Umbraco.Community.Schemeweaver](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver).

## Licence

MIT

## Author

Oliver Picton / [Enjoy Digital](https://www.enjoy-digital.co.uk)
