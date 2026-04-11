# Umbraco.Community.SchemeWeaver

Map Umbraco Content Types to [Schema.org](https://schema.org) types and automatically generate [JSON-LD](https://json-ld.org/) structured data for your pages. SchemeWeaver provides a document type editor UI for configuring mappings, an auto-mapper that suggests property assignments, and runtime JSON-LD generation that works with both server-rendered templates and the headless Delivery API.

## Why structured data?

Search engines use JSON-LD to understand page content. A blog post tagged as `BlogPosting` with a `headline`, `author`, and `datePublished` can appear as a rich result in Google, Bing, and other search engines. Manually maintaining JSON-LD scripts is tedious and error-prone -- SchemeWeaver automates it from your existing content.

## Features

- **780 Schema.org types** -- discovers every type in the [Schema.NET.Pending](https://github.com/RehanSaeed/Schema.NET) library at startup, including pending types like `RealEstateListing`
- **Auto-mapping with confidence scores** -- suggests property mappings using exact matching, synonym dictionaries, and substring matching
- **Smart property UI** -- shows mapped properties first, with an "Add property" combobox to add more schema properties
- **Seven source types** -- pull values from the current node, a static value, the parent, an ancestor, a sibling, block content, or nested complex types
- **Transforms** -- strip HTML, convert to absolute URL, or format dates before output
- **Content Type generation** -- scaffold a new Umbraco document type from any Schema.org type
- **Delivery API integration** -- JSON-LD is automatically indexed and available via the `schemaOrg` field
- **Tag helper** -- drop `<scheme-weaver content="@Model" />` into any Razor template
- **Inherited schemas** -- mark a mapping as inherited and it outputs on all descendant pages
- **BreadcrumbList** -- automatically generated from the content's ancestor hierarchy
- **AI-powered mapping (optional)** -- install the companion [`Umbraco.Community.SchemeWeaver.AI`](docs/ai-integration.md) package for AI schema type suggestions, bulk analysis across all content types, and Umbraco Copilot integration

## Requirements

- Umbraco 17+
- .NET 10

## Installation

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

No additional configuration needed. The package registers all services, creates its database tables on first run, and adds the backoffice UI automatically.

### Optional: AI-Powered Mapping

For AI-assisted schema type suggestions and property mapping, install the companion package:

```bash
dotnet add package Umbraco.Community.SchemeWeaver.AI --prerelease
```

Requires [Umbraco.AI.Core](https://marketplace.umbraco.com/package/umbraco.ai.core) 1.7.0 or later and a configured chat provider (e.g. Azure OpenAI, Anthropic). See [AI Integration](docs/ai-integration.md) for details.

### Optional: uSync Integration

To sync schema mappings between environments via [uSync](https://jumoo.co.uk/usync/):

```bash
dotnet add package Umbraco.Community.SchemeWeaver.uSync --prerelease
```

See [uSync Integration](docs/usync.md) for details.

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
4. Review the auto-suggested property mappings in the modal and click **Save**
5. Publish content -- JSON-LD appears in the page source

### 3. Headless / Delivery API

JSON-LD is automatically indexed when content is published:

```typescript
const response = await fetch('/umbraco/delivery/api/v2/content/item/my-blog-post');
const data = await response.json();
const jsonLd = data.properties.schemaOrg;
```

## Documentation

- [Getting Started](docs/getting-started.md) -- installation, tag helper, first mapping
- [Mapping Content Types](docs/mapping-content-types.md) -- full mapping workflow
- [Property Mappings](docs/property-mappings.md) -- source types, transforms, confidence tiers
- [Block Content](docs/block-content.md) -- BlockList/BlockGrid mapping, nested types, wrapInType
- [Content Type Generation](docs/content-type-generation.md) -- scaffold document types from Schema.org
- [Delivery API](docs/delivery-api.md) -- headless integration
- [Extending](docs/extending.md) -- custom property resolvers, replacing core services
- [uSync Integration](docs/usync.md) -- sync schema mappings between environments
- [Advanced](docs/advanced.md) -- inherited schemas, BreadcrumbList, troubleshooting
- [API Reference](docs/api-reference.md) -- REST API endpoints
- [AI Integration](docs/ai-integration.md) -- optional AI-powered schema suggestions, bulk analysis, and Copilot tools

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

## Notes

- **Block content nested types** -- complex Schema.org properties (e.g. `acceptedAnswer`, `reviewRating`) require a wrapper type. The auto-mapper pre-configures this for common patterns (FAQ, Product, Recipe). For custom types, see the [`wrapInType` guide](docs/block-content.md#wrapintype-configuration).
- **Media picker edge cases** -- complex multi-crop scenarios with specific crop aliases may need manual URL configuration. See [Property Mappings](docs/property-mappings.md#property-value-resolvers).
- **AI package** -- the `Umbraco.Community.SchemeWeaver.AI` companion is optional and requires a configured Umbraco.AI chat provider. Without it, the heuristic auto-mapper handles all suggestions.

## Contributing

Contributions are very welcome — bug reports, fixes, docs, new property resolvers, extra auto-mapper synonyms, whole new features. Small PRs are fine.

### Getting set up

```bash
# C#
dotnet build
dotnet test

# Frontend
cd src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver
npm install
npm run build
npm test
npm run test:msw                 # component tests with MSW handlers
npm run test:mocked-backoffice   # Playwright drives the real backoffice UI with MSW — needs Umbraco-CMS clone
npm run test:e2e                 # Playwright against a running Umbraco + .env
npm run test:screenshots         # regenerate the docs screenshots (opt-in)

# Test host with 100+ sample content types
dotnet run --project src/Umbraco.Community.SchemeWeaver.TestHost
```

Read [`CLAUDE.md`](CLAUDE.md) for architecture, DI wiring, and naming conventions.

### Tests

Please add tests for behavioural changes, and a regression test for bug fixes. CI runs the full suite on every push.

| Layer | Framework | Location |
|---|---|---|
| C# Unit | xUnit + NSubstitute + FluentAssertions | `tests/Umbraco.Community.SchemeWeaver.Tests/Unit/` |
| C# Integration | xUnit + `WebApplicationFactory<Program>` against the SchemeWeaver TestHost, shared via an xUnit collection fixture so every test class reuses a single host (temp SQLite, one file per suite) | `tests/Umbraco.Community.SchemeWeaver.Tests/Integration/` |
| TS Unit / Component | `@open-wc/testing` + MSW | `App_Plugins/SchemeWeaver/src/**/*.test.ts` |
| Mocked Backoffice | Playwright drives the real Umbraco backoffice UI via `VITE_EXAMPLE_PATH`, with SchemeWeaver's MSW handlers serving all HTTP traffic — no .NET required. Requires a local `umbraco/Umbraco-CMS` clone plus a small `addMockHandlers` patch; see [`tests/mocked-backoffice/README.md`](src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/tests/mocked-backoffice/README.md) | `App_Plugins/SchemeWeaver/tests/mocked-backoffice/` |
| E2E | Playwright + `@umbraco/playwright-testhelpers` against a running Umbraco | `App_Plugins/SchemeWeaver/tests/e2e/` |

For backoffice UI changes, `npm run test:mocked-backoffice` verifies manifest wiring, workspace-view conditions, and modal plumbing without a running Umbraco; `npm run test:e2e` against a real instance is still the only thing that catches issues across the full .NET + backoffice stack.

### Using an AI assistant

AI tools (Claude, Copilot, Cursor, etc.) are welcome to help. The rules are short:

- **You review it.** Read every line before committing. You're accountable for the PR, not the assistant.
- **MIT-compatible only.** Don't submit code copied from incompatible sources.
- **Add tests**, same as any other contribution.
- **For UI work**, install the [Umbraco Backoffice Skills](https://github.com/umbraco/Umbraco-CMS-Backoffice-Skills) and add [`umbraco/Umbraco-CMS`](https://github.com/umbraco/Umbraco-CMS) (`src/Umbraco.Web.UI.Client`) and [`umbraco/Umbraco.UI`](https://github.com/umbraco/Umbraco.UI) (`packages/uui`) as working directories so the skills can grep the canonical backoffice source — see the skills repo for setup. Also run the `umbraco-extension-reviewer` agent on UI changes.
- **Tag the commit.** When an assistant materially helped, add a git trailer at the bottom of the commit message so we can see which model was used:

  ```
  Assisted-by: Claude:claude-opus-4-6
  ```

  Format is `Assisted-by: <agent>:<model> [optional tools]`, e.g. `Assisted-by: Copilot:gpt-5 playwright`. Just leave a blank line before it, or use `git commit --trailer "Assisted-by=..."`. Basic tools (`git`, `dotnet`, `npm`, editors) don't need listing. If your tool already adds a `Co-authored-by:` trailer for the assistant automatically, that's fine too — just don't bother adding both.

- **Don't add `Signed-off-by` on a human's behalf.** Only the human submitter can sign off their own contribution.

## Licence

MIT — see [LICENSE](LICENSE). By submitting a pull request you agree to license your contribution under the same terms.

## Author

Oliver Picton / [Enjoy Digital](https://www.enjoy-digital.co.uk)
