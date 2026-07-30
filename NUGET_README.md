# SchemeWeaver

Map Umbraco Content Types to Schema.org types and automatically generate JSON-LD structured data for your pages.

## Features

- **The full Schema.org vocabulary** -- every type in the Schema.NET.Pending library (~800), including pending types
- **Auto-mapping with confidence scores** -- suggests property mappings using exact matching, synonym dictionaries, and substring matching
- **Seven source types** -- pull values from the current node, a static value, the parent, an ancestor, a sibling, block content, or nested complex types
- **Transforms** -- strip HTML, convert to absolute URL, or format dates before output
- **Content Type generation** -- scaffold a new Umbraco document type from any Schema.org type
- **Language variants** -- culture-aware JSON-LD generation for multi-language sites with automatic `inLanguage` population
- **Delivery API integration** -- a dedicated, culture-aware `/json-ld` endpoint returns each page's JSON-LD for headless front-ends, cached with event-driven invalidation
- **Tag helper** -- drop `<scheme-weaver content="@Model" />` into any Razor template
- **Inherited schemas** -- mark a mapping as inherited and it outputs on all descendant pages
- **BreadcrumbList** -- automatically generated from the content's ancestor hierarchy
- **Rich Results validation** -- the backoffice flags missing required/recommended properties against Google's structured-data rules

## Requirements

- Umbraco 17 & 18
- .NET 10

## Installation

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

No additional configuration needed. The package registers all services, creates its database tables on first run, and adds the backoffice UI automatically.

**Umbraco 17 vs 18:** Umbraco 18 made a binary-breaking change to `IPublishedContent`, so SchemeWeaver ships one **stable** build per major from the same source — `17.x` for Umbraco 17 and `18.x` for Umbraco 18, the same major-aligned scheme uSync uses. The install command above is identical for both; each build's mutually-exclusive `Umbraco.Cms` dependency range means NuGet auto-selects the one matching your Umbraco major (no `--prerelease` needed).

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

To switch to a different Schema.org type later, use **Change** next to the type tag -- the property mappings the new type still accepts are kept.

### 3. Headless / Delivery API

JSON-LD is served from a dedicated endpoint — fetch it and inject the strings as
`<script type="application/ld+json">` tags:

```typescript
const response = await fetch(
  '/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?route=/my-blog-post',
  { headers: { 'Api-Key': process.env.UMBRACO_DELIVERY_API_KEY! } },
);
const { schemaOrg }: { schemaOrg: string[] } = await response.json();
```

## Optional companions

- **uSync** — sync schema mappings between environments: `Umbraco.Community.SchemeWeaver.uSync`
- **Umbraco Deploy / Cloud** — deploy schema mappings as `.uda` artifacts: `Umbraco.Community.SchemeWeaver.Deploy`
- **AI** (Umbraco 17) — AI-powered mapping suggestions via Umbraco.AI: `Umbraco.Community.SchemeWeaver.AI`

## Use it with an AI assistant (MCP)

SchemeWeaver also ships an **MCP server** plus a **`schemeweaver-map` skill** that let an AI assistant (Claude and others) reason semantically about the best Schema.org type for a content type, save the mapping, and verify the JSON-LD — usually a richer result than the name-matching auto-mapper. With [Claude Code](https://claude.com/claude-code) it's a two-command plugin install:

```text
/plugin marketplace add EnjoyDigital/Umbraco.Community.Schemeweaver
/plugin install schemeweaver-mcp@schemeweaver
```

Then just ask: *"Map my `blogPost` document type to Schema.org and validate it."* See the [MCP Server guide](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/blob/main/docs/mcp-server.md) for details.

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
