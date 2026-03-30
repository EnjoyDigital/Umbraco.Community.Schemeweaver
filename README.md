# Umbraco.Community.SchemeWeaver

Map Umbraco Content Types to [Schema.org](https://schema.org) types and automatically generate [JSON-LD](https://json-ld.org/) structured data for your pages. SchemeWeaver provides a backoffice UI for configuring mappings, an auto-mapper that suggests property assignments, and runtime JSON-LD generation that works with both server-rendered templates and the headless Delivery API.

## Why structured data?

Search engines use JSON-LD to understand page content. A blog post tagged as `BlogPosting` with a `headline`, `author`, and `datePublished` can appear as a rich result in Google, Bing, and other search engines. Manually maintaining JSON-LD scripts is tedious and error-prone -- SchemeWeaver automates it from your existing content.

## Features

- **657 Schema.org types** -- discovers every type in the [Schema.NET](https://github.com/RehanSaeed/Schema.NET) library at startup
- **Auto-mapping with confidence scores** -- suggests property mappings using exact matching, synonym dictionaries, and substring matching
- **Smart property UI** -- shows the most likely properties first, with a "Show more" toggle for the rest
- **Seven source types** -- pull values from the current node, a static value, the parent, an ancestor, a sibling, block content, or nested complex types
- **Transforms** -- strip HTML, convert to absolute URL, or format dates before output
- **Content Type generation** -- scaffold a new Umbraco document type from any Schema.org type
- **Delivery API integration** -- JSON-LD is automatically indexed and available via the `schemaOrg` field
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

1. Go to **Settings > Schema.org Mappings** in the backoffice
2. Click **Map** on any content type
3. Select a Schema.org type (e.g. Product, Article, Event)
4. Review the auto-suggested property mappings and save
5. Publish content -- JSON-LD appears in the page source

### 3. Headless / Delivery API

JSON-LD is automatically indexed when content is published:

```typescript
const response = await fetch('/umbraco/delivery/api/v2/content/item/my-blog-post');
const data = await response.json();
const jsonLd = data.properties.schemaOrg;
```

## How it works

Each mapping connects one Umbraco **Content Type** to one **Schema.org type**. Within that mapping, individual **property mappings** define where each schema property gets its value:

| Schema Property | Source | Value | Description |
|---|---|---|---|
| `headline` | property | `title` | Read from the current node |
| `author` | static | `Jane Smith` | Hardcoded string value |
| `datePublished` | property | `publishDate` | Formatted as ISO date |
| `publisher` | parent | `organisationName` | Read from the parent node |
| `mainEntity` | blockContent | `faqItems` | Built from BlockList items |

The auto-mapper suggests assignments using three confidence tiers:

- **High (100%)** -- exact property name match
- **Medium (80%)** -- synonym match (e.g. `title` to `name`, `bodyText` to `articleBody`)
- **Low (50%)** -- substring match

The generated output:

```json
{
  "@context": "https://schema.org",
  "@type": "BlogPosting",
  "headline": "10 Tips for Better SEO",
  "author": {
    "@type": "Person",
    "name": "Jane Smith"
  },
  "datePublished": "2024-01-15"
}
```

## Known Issues (v0.1.0-alpha)

- **Block content nested types** -- some complex Schema.NET property types (e.g. `ReviewRating`) cannot be set from a plain string. Use `wrapInType` config to explicitly construct the nested type.
- **Media picker on block elements** -- complex multi-crop scenarios may need manual URL mapping.

## Contributing

```bash
# Build
dotnet build

# Run tests
dotnet test                    # C# unit tests
cd src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver
npm test                       # Frontend tests

# Run the test host (100+ sample content types with Schema.org mappings)
dotnet run --project src/SchemeWeaver.TestHost
```

## Licence

MIT -- see [LICENSE](LICENSE).

## Author

Oliver Picton / [Enjoy Digital](https://enjoydigital.co.uk)
