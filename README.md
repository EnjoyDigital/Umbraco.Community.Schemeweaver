<p align="center">
  <img src="https://raw.githubusercontent.com/EnjoyDigital/Umbraco.Community.Schemeweaver/main/icon.png" alt="SchemeWeaver" width="140" />
</p>

# Umbraco.Community.SchemeWeaver

Map Umbraco content types to [Schema.org](https://schema.org) types and generate [JSON-LD](https://json-ld.org/) structured data automatically.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.SchemeWeaver)](https://www.nuget.org/packages/Umbraco.Community.SchemeWeaver) [![License](https://img.shields.io/github/license/EnjoyDigital/Umbraco.Community.Schemeweaver)](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/blob/main/LICENSE)

> **Building in public.** SchemeWeaver is usable today, but the editor UX is still settling, so expect small UI and behavioural changes between releases — each one is flagged in the [release notes](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/releases). Hit a rough edge? Please [open an issue](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/issues) — that feedback is what shapes the package.

## Overview

Search engines read JSON-LD to understand a page: a post tagged `BlogPosting` with a `headline`, `author` and `datePublished` can show as a rich result in Google, Bing and others. Maintaining that markup by hand is tedious and error-prone — SchemeWeaver generates it from the content you already have.

You get a document-type editor UI for configuring mappings, an auto-mapper that suggests them, and runtime output that works with both server-rendered templates and the headless Delivery API.

## Features

- **Full Schema.org vocabulary** — every type in [Schema.NET.Pending](https://github.com/RehanSaeed/Schema.NET) (~800, including pending ones like `RealEstateListing`).
- **Auto-mapping with confidence scores** — suggests property mappings via exact, synonym and substring matching.
- **Seven source types** — pull values from the current node, a static value, the parent, an ancestor, a sibling, block content, or nested complex types.
- **Transforms** — strip HTML, convert to an absolute URL, or format dates before output.
- **Content type generation** — scaffold a new Umbraco document type from any Schema.org type.
- **Culture-aware** — localised values on variant content, with `inLanguage` auto-populated from the BCP 47 culture code.
- **Delivery API integration** — a dedicated, culture-aware JSON-LD endpoint for headless front-ends, cached with event-driven invalidation.
- **Razor tag helper** — drop `<scheme-weaver content="@Model" />` into any template.
- **Inherited schemas** — mark a mapping as inherited and it outputs on all descendant pages.
- **BreadcrumbList** — generated automatically from the content's ancestor hierarchy.
- **Rich Results validation** — the backoffice preview flags missing required/recommended properties against Google's rules.

## Requirements

- Umbraco 17.4+ or 18
- .NET 10

## Installation

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

No configuration needed — the package registers its services, creates its database tables on first run, and adds the backoffice UI automatically.

### Umbraco 17 vs 18

Umbraco 18 made a binary-breaking change to `IPublishedContent`, so one assembly can't serve both majors. SchemeWeaver therefore ships **one stable package per Umbraco major**, with the version's major tracking the CMS major — exactly like uSync:

| Umbraco | SchemeWeaver | Install |
|---|---|---|
| 17 | `17.x` | `dotnet add package Umbraco.Community.SchemeWeaver` |
| 18 | `18.x` | `dotnet add package Umbraco.Community.SchemeWeaver` |

The command is identical for both: each build's `Umbraco.Cms` dependency range is mutually exclusive (`[17.4.0, 18.0.0)` vs `[18.0.0, 19.0.0)`), so NuGet resolves the right build for your project automatically — no `--prerelease` needed. (The Umbraco 17 build requires 17.4+, where the JSON-Schema services SchemeWeaver uses for value-schema awareness landed; 17.0–17.3 sites should stay on SchemeWeaver 17.8.x.)

To sync mappings between environments, add the optional [uSync](https://jumoo.co.uk/usync/) addon — `dotnet add package Umbraco.Community.SchemeWeaver.uSync` — which follows the same major-aligned scheme. See [uSync Integration](docs/usync.md).

Using [Umbraco Deploy](https://umbraco.com/products/add-ons/deploy/) or Umbraco Cloud instead? Add the optional Deploy addon — `dotnet add package Umbraco.Community.SchemeWeaver.Deploy` — and mappings deploy as `.uda` artifacts alongside your document types. See [Umbraco Deploy Integration](docs/deploy.md).

## Quick start

**1. Add the tag helper** to your master layout (e.g. `_Layout.cshtml`):

```html
@addTagHelper *, Umbraco.Community.SchemeWeaver

<head>
    ...
    <scheme-weaver content="@Model" />
</head>
```

**2. Map a content type:** open a document type in **Settings → Document Types**, click the **Schema.org** tab, choose **Map to Schema.org** and pick a type (e.g. Product, Article, Event), review the auto-suggested mappings, and **Save**. Publish content and the JSON-LD appears in the page source.

**3. Headless?** Fetch the per-page JSON-LD from the Delivery API and inject it as `<script type="application/ld+json">` tags:

```typescript
const response = await fetch(
  '/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?route=/my-blog-post',
  { headers: { 'Api-Key': process.env.UMBRACO_DELIVERY_API_KEY! } },
);
const { schemaOrg }: { schemaOrg: string[] } = await response.json();
```

Responses are cached and invalidated automatically on publish/unpublish/move/delete. See [Delivery API](docs/delivery-api.md) for the full endpoint surface and a Next.js example.

**4. Multi-language?** Nothing to configure — on variant content SchemeWeaver resolves values in the requested culture, sets `inLanguage`, and generates culture-correct URLs. Mappings stay invariant (one mapping, all cultures), and the backoffice preview follows the variant selector.

## Use it with an AI assistant (MCP)

SchemeWeaver ships an [MCP server](docs/mcp-server.md) that gives an AI assistant (Claude and others) tools to inspect your content types, reason about the best-fitting Schema.org type, save the mapping, and verify the JSON-LD — typically a better result than the name-matching auto-mapper alone.

**You need:** an Umbraco instance (v17/18) with SchemeWeaver installed and an [API user](docs/mcp-server.md#prerequisites) with client credentials.

### Quick start (Claude Code)

Install it as a Claude Code plugin — no clone, no build:

```text
/plugin marketplace add EnjoyDigital/Umbraco.Community.Schemeweaver
/plugin install schemeweaver-mcp@schemeweaver
/reload-plugins
```

Claude Code prompts for your **Umbraco Base URL** and the API user's **Client ID / Client Secret** (the secret is stored in your OS keychain), then starts the `schemeweaver` server automatically.

### The included skill

The plugin **also installs the `schemeweaver-map` skill** — a guided workflow that drives the tools end to end (inspect the content type → pick the most specific Schema.org type → improve on the heuristic → save → preview and validate until it passes Google's Rich Results rules). Nothing extra to install.

To use it, just ask in plain language and it triggers automatically:

> Map my `blogPost` document type to Schema.org and validate the JSON-LD.

…or invoke it explicitly with `/schemeweaver-mcp:schemeweaver-map`. See [MCP Server](docs/mcp-server.md) for the full tool list, the skill, and advanced options.

## How it works

Each mapping links one Umbraco **content type** to one **Schema.org type**, and each **property mapping** says where a schema property gets its value — the current node, a static value, a parent/ancestor/sibling, or block content. The auto-mapper proposes a starting point by matching names (exact, then synonym, then substring) with a confidence score; you refine and save. A `blogPost` mapped to `BlogPosting` might produce:

```json
{
  "@context": "https://schema.org",
  "@type": "BlogPosting",
  "headline": "10 Tips for Better SEO",
  "author": { "@type": "Person", "name": "Jane Smith" },
  "datePublished": "2024-01-15",
  "inLanguage": "en-US"
}
```

See [Mapping Content Types](docs/mapping-content-types.md) and [Property Mappings](docs/property-mappings.md) for the full model — source types, transforms, and confidence tiers.

## Documentation

- [Getting Started](docs/getting-started.md) — installation, tag helper, first mapping
- [Mapping Content Types](docs/mapping-content-types.md) — full mapping workflow
- [Property Mappings](docs/property-mappings.md) — source types, transforms, confidence tiers
- [Block Content](docs/block-content.md) — BlockList/BlockGrid mapping, nested types, `wrapInType`
- [Content Type Generation](docs/content-type-generation.md) — scaffold document types from Schema.org
- [Delivery API](docs/delivery-api.md) — headless integration
- [Extending](docs/extending.md) — custom property resolvers, replacing core services
- [uSync Integration](docs/usync.md) — sync mappings between environments
- [Umbraco Deploy Integration](docs/deploy.md) — deploy mappings between environments with Umbraco Deploy / Cloud
- [AI Integration](docs/ai-integration.md) — optional AI-powered mapping (Umbraco 17)
- [MCP Server](docs/mcp-server.md) — drive SchemeWeaver from an AI assistant; installable as a Claude Code plugin with the bundled `schemeweaver-map` skill
- [Advanced](docs/advanced.md) — inherited schemas, BreadcrumbList, validation, configuration, troubleshooting
- [API Reference](docs/api-reference.md) — REST API endpoints

## Notes

- **Block content nested types** — complex Schema.org properties (e.g. `acceptedAnswer`, `reviewRating`) need a wrapper type. The auto-mapper pre-configures the common patterns (FAQ, Product, Recipe); for custom ones see the [`wrapInType` guide](docs/block-content.md#wrapintype-configuration).
- **Media picker edge cases** — multi-crop scenarios with specific crop aliases may need manual URL configuration. See [Property Mappings](docs/property-mappings.md#property-value-resolvers).

## Contributing

Contributions are very welcome — bug reports, fixes, docs, new property resolvers, extra auto-mapper synonyms, whole features. Small PRs are fine. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to get set up, run the tests, and use an AI assistant, and [`CLAUDE.md`](CLAUDE.md) for architecture and conventions.

## Licence

MIT — see [LICENSE](LICENSE). By submitting a pull request you agree to license your contribution under the same terms.

## Author

Oliver Picton / [Enjoy Digital](https://www.enjoy-digital.co.uk)
