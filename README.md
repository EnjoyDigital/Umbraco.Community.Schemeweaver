<p align="center">
  <img src="https://raw.githubusercontent.com/EnjoyDigital/Umbraco.Community.Schemeweaver/main/icon.png" alt="SchemeWeaver" width="140" />
</p>

# Umbraco.Community.SchemeWeaver

Map Umbraco content types to [Schema.org](https://schema.org) types and generate [JSON-LD](https://json-ld.org/) structured data automatically.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.SchemeWeaver)](https://www.nuget.org/packages/Umbraco.Community.SchemeWeaver) [![License](https://img.shields.io/github/license/EnjoyDigital/Umbraco.Community.Schemeweaver)](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/blob/main/LICENSE)

> **Building in public.** SchemeWeaver is usable today, but the editor UX is still settling, so expect small UI and behavioural changes between releases. Each one is flagged in the [release notes](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/releases). Hit a rough edge? Please [open an issue](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/issues): that feedback is what shapes the package.

**New here?** Start with the [Quick Start for Developers](docs/quickstart-developer.md) or, if you work in the backoffice rather than the code, the [Quick Start for SEOs](docs/quickstart-seo.md). The full documentation index is at [docs/README.md](docs/README.md).

## Overview

Search engines read JSON-LD to understand a page: a post tagged `BlogPosting` with a `headline`, `author` and `datePublished` can show as a rich result in Google, Bing and others. Maintaining that markup by hand is tedious and error-prone, so SchemeWeaver generates it from the content you already have.

You get a document-type editor UI for configuring mappings, an auto-mapper that suggests them, and runtime output that works with both server-rendered templates and the headless Delivery API. By default everything is emitted as a single connected `@graph` (Organization, WebSite, breadcrumb and page entity cross-referenced by `@id`), the same shape Yoast and Rank Math produce. See [The JSON-LD Output Model](docs/json-ld-output.md).

## Features

- **Full Schema.org vocabulary**: every type in [Schema.NET.Pending](https://github.com/RehanSaeed/Schema.NET) (around 800, including pending ones like `RealEstateListing`).
- **Connected `@graph` output**: site-level Organization and WebSite entities, breadcrumb and page entity in one cross-referenced graph, with a legacy one-script-per-entity mode available.
- **Auto-mapping with confidence scores**: suggests property mappings via exact, synonym and substring matching.
- **Eight source types**: pull values from the current node, a static value, the parent, an ancestor, a sibling, block content, nested complex types, or a shared graph reference.
- **Transforms**: strip HTML, convert to an absolute URL, or format dates before output.
- **Content type generation**: scaffold a new Umbraco document type from any Schema.org type.
- **Culture-aware**: localised values on variant content, with `inLanguage` auto-populated from the BCP 47 culture code.
- **Delivery API integration**: a dedicated, culture-aware JSON-LD endpoint for headless front-ends, cached with event-driven invalidation.
- **Razor tag helper**: drop `<scheme-weaver content="@Model" />` into any template.
- **Inherited schemas**: mark a mapping as inherited and it outputs on all descendant pages.
- **BreadcrumbList**: generated automatically from the content's ancestor hierarchy.
- **Rich Results validation and suggestions**: the backoffice preview flags missing required and recommended properties against Google's rules, and suggests richer mapping modes where your content supports them.

## Requirements

- Umbraco 17.4+ or 18
- .NET 10

## Installation

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

No configuration needed: the package registers its services, creates its database tables on first run, and adds the backoffice UI automatically.

### Umbraco 17 vs 18

Umbraco 18 made a binary-breaking change to `IPublishedContent`, so one assembly cannot serve both majors. SchemeWeaver therefore ships **one stable package per Umbraco major**, with the version's major tracking the CMS major, exactly like uSync:

| Umbraco | SchemeWeaver | Install |
|---|---|---|
| 17 | `17.x` | `dotnet add package Umbraco.Community.SchemeWeaver` |
| 18 | `18.x` | `dotnet add package Umbraco.Community.SchemeWeaver` |

The command is identical for both: each build's `Umbraco.Cms` dependency range is mutually exclusive (`[17.4.0, 18.0.0)` vs `[18.0.0, 19.0.0)`), so NuGet resolves the right build for your project automatically, no `--prerelease` needed. (The Umbraco 17 build requires 17.4+, where the JSON-Schema services SchemeWeaver uses for value-schema awareness landed; 17.0–17.3 sites should stay on SchemeWeaver 17.8.x.)

To sync mappings between environments, add the optional [uSync](https://jumoo.co.uk/usync/) addon (`dotnet add package Umbraco.Community.SchemeWeaver.uSync`), which follows the same major-aligned scheme. See [uSync Integration](docs/usync.md).

Using [Umbraco Deploy](https://umbraco.com/products/add-ons/deploy/) or Umbraco Cloud instead? Add the optional Deploy addon (`dotnet add package Umbraco.Community.SchemeWeaver.Deploy`) and mappings deploy as `.uda` artifacts alongside your document types. See [Umbraco Deploy Integration](docs/deploy.md).

## Quick start

The condensed version; the [developer quick start](docs/quickstart-developer.md) walks the same path with screenshots.

**1. Add the tag helper** to your master layout (e.g. `_Layout.cshtml`):

```html
@addTagHelper *, Umbraco.Community.SchemeWeaver

<head>
    ...
    <scheme-weaver content="@Model" />
</head>
```

**2. Map a content type:** open a document type in **Settings → Document Types**, click the **Schema.org** tab, choose **Map to Schema.org** and pick a type (e.g. Product, Article, Event), review the auto-suggested mappings, and **Save**. Publish content and the JSON-LD appears in the page source. Changed your mind about the type later? **Change** next to the type tag switches it, keeping the property mappings the new type still accepts.

**3. Site identity:** create and publish a node of a document type aliased `schemaSiteSettings`, map it to `Organization`, and every page's graph gains your organisation and website entities. See [The JSON-LD Output Model](docs/json-ld-output.md#the-site-settings-node).

**4. Headless?** Fetch the per-page JSON-LD from the Delivery API and inject it as `<script type="application/ld+json">` tags:

```typescript
const response = await fetch(
  '/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?route=/my-blog-post',
  { headers: { 'Api-Key': process.env.UMBRACO_DELIVERY_API_KEY! } },
);
const { schemaOrg }: { schemaOrg: string[] } = await response.json();
```

Responses are cached and invalidated automatically on publish/unpublish/move/delete. Under the default graph output, `schemaOrg` is a single-element array containing the whole graph. See [Delivery API](docs/delivery-api.md) for the full endpoint surface and a Next.js example.

**5. Multi-language?** Nothing to configure: on variant content SchemeWeaver resolves values in the requested culture, sets `inLanguage`, and generates culture-correct URLs. Mappings stay invariant (one mapping, all cultures), and the backoffice preview follows the variant selector. See [Language Variants](docs/language-variants.md).

## Use it with an AI assistant (MCP)

SchemeWeaver ships an [MCP server](docs/mcp-server.md) that gives an AI assistant (Claude and others) tools to inspect your content types, reason about the best-fitting Schema.org type, save the mapping, and verify the JSON-LD. That is typically a better result than the name-matching auto-mapper alone.

**You need:** an Umbraco instance (v17/18) with SchemeWeaver installed and an [API user](docs/mcp-server.md#prerequisites) with client credentials.

### Quick start (Claude Code)

Install it as a Claude Code plugin, no clone, no build:

```text
/plugin marketplace add EnjoyDigital/Umbraco.Community.Schemeweaver
/plugin install schemeweaver-mcp@schemeweaver
/reload-plugins
```

Claude Code prompts for your **Umbraco Base URL** and the API user's **Client ID / Client Secret** (the secret is stored in your OS keychain), then starts the `schemeweaver` server automatically.

### The included skills

The plugin **also installs three skills**, guided workflows that drive the tools end to end. Nothing extra to install:

- **`schemeweaver-setup`**: connect Claude to your Umbraco site (API user, plugin config) and verify it, with troubleshooting for the classic failures.
- **`schemeweaver-map`**: map one content type (inspect, pick the most specific Schema.org type, improve on the heuristic, save, then preview and validate until it passes Google's Rich Results rules).
- **`schemeweaver-audit`**: audit the whole site's structured-data coverage and quality, report, then fix the gaps.

Just ask in plain language and the right one triggers automatically:

> Map my `blogPost` document type to Schema.org and validate the JSON-LD.

Or invoke one explicitly, e.g. `/schemeweaver-mcp:schemeweaver-map`. See [MCP Server](docs/mcp-server.md) for the full tool list, the skills, and advanced options.

## How it works

Each mapping links one Umbraco **content type** to one **Schema.org type**, and each **property mapping** says where a schema property gets its value: the current node, a static value, a parent, ancestor or sibling, block content, a nested complex type, or a shared graph reference. The auto-mapper proposes a starting point by matching names (exact, then synonym, then substring) with a confidence score; you refine and save. A `blogPost` mapped to `BlogPosting` contributes an entity like this to the page's graph:

```json
{
  "@type": "BlogPosting",
  "@id": "https://example.com/blog/10-tips/#blogposting",
  "headline": "10 Tips for Better SEO",
  "author": { "@type": "Person", "name": "Jane Smith" },
  "datePublished": "2024-01-15",
  "inLanguage": "en-US",
  "isPartOf": { "@id": "https://example.com/#website" }
}
```

See [Mapping Content Types](docs/mapping-content-types.md) and [Property Mappings](docs/property-mappings.md) for the full model: source types, transforms, and confidence tiers.

## Documentation

The full index lives at **[docs/README.md](docs/README.md)**. Highlights:

- [Quick Start for Developers](docs/quickstart-developer.md) and [Quick Start for SEOs](docs/quickstart-seo.md)
- [Getting Started](docs/getting-started.md): installation, tag helper, first mapping in detail
- [Mapping Content Types](docs/mapping-content-types.md) and [Property Mappings](docs/property-mappings.md): the mapping model
- [Block Content](docs/block-content.md): BlockList/BlockGrid mapping, nested types, text extraction
- [The JSON-LD Output Model](docs/json-ld-output.md): the `@graph`, site settings node, `@id` templates
- [Language Variants](docs/language-variants.md) and [Delivery API](docs/delivery-api.md)
- [Content Type Generation](docs/content-type-generation.md), [Extending](docs/extending.md), [API Reference](docs/api-reference.md)
- [uSync](docs/usync.md), [Umbraco Deploy](docs/deploy.md), [AI Integration](docs/ai-integration.md), [MCP Server](docs/mcp-server.md)
- [Advanced](docs/advanced.md): validation and suggestions, configuration, troubleshooting

## Notes

- **Block content nested types**: complex Schema.org properties (e.g. `acceptedAnswer`, `reviewRating`) need a wrapper type. The auto-mapper pre-configures the common patterns (FAQ, Product, Recipe); for custom ones see the [`wrapInType` guide](docs/block-content.md#wrapintype-configuration).
- **Media picker edge cases**: multi-crop scenarios with specific crop aliases may need manual URL configuration. See [Property Mappings](docs/property-mappings.md#property-value-resolvers).

## Contributing

Contributions are very welcome: bug reports, fixes, docs, new property resolvers, extra auto-mapper synonyms, whole features. Small PRs are fine. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to get set up, run the tests, and use an AI assistant, and [`CLAUDE.md`](CLAUDE.md) for architecture and conventions.

## Licence

MIT, see [LICENSE](LICENSE). By submitting a pull request you agree to license your contribution under the same terms.

## Author

Oliver Picton / [Enjoy Digital](https://www.enjoy-digital.co.uk)
