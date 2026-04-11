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

Contributions are very welcome -- bug reports, fixes, docs improvements, new property resolvers, extra auto-mapper synonyms, or whole new features. Please read this section before opening a pull request.

### Licence and legal

- SchemeWeaver is licensed under the **MIT Licence** -- see [LICENSE](LICENSE).
- By submitting a pull request you agree that your contribution is licensed under the same MIT Licence.
- Do not include code copied from incompatible sources (GPL, proprietary, unknown licence). If in doubt, ask first in an issue.
- Keep third-party dependencies to a minimum and prefer libraries that are already referenced by Umbraco or Schema.NET.

### Development workflow

1. **Fork** the repository and create a topic branch from `main`.
2. Read [`CLAUDE.md`](CLAUDE.md) -- it documents the architecture, DI registrations, and conventions used across the C# and frontend projects.
3. Build, run, and smoke-test your change against the bundled test host before opening a PR:

   ```bash
   # Build the solution
   dotnet build

   # C# tests (unit + skipped integration stubs)
   dotnet test
   dotnet test --filter "FullyQualifiedName~Unit"

   # Frontend tests
   cd src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver
   npm install
   npm run build
   npm test                       # Web Test Runner unit + component tests
   npm run test:e2e               # Playwright E2E (requires running Umbraco + .env)

   # Run the test host (100+ sample content types with Schema.org mappings)
   dotnet run --project src/Umbraco.Community.SchemeWeaver.TestHost
   ```

4. Keep commits focused, use British English in prose, and follow existing naming (`SchemeWeaver`, not `SchemaWeaver`).
5. Open a pull request describing **what** changed and **why**, and link any related issues.

### Tests are required

Every behavioural change **must** come with tests. PRs without tests will usually be asked to add them before review.

| Layer | Framework | Location |
|---|---|---|
| C# Unit | xUnit + NSubstitute + FluentAssertions | `tests/Umbraco.Community.SchemeWeaver.Tests/Unit/` |
| C# Integration | xUnit (stubs, marked `Skip`) | `tests/Umbraco.Community.SchemeWeaver.Tests/Integration/` |
| TS Unit / Component | `@open-wc/testing` + MSW | `App_Plugins/SchemeWeaver/src/**/*.test.ts` |
| E2E | Playwright + `@umbraco/playwright-testhelpers` | `App_Plugins/SchemeWeaver/tests/e2e/` |

Specifically:

- New or changed C# services, resolvers, auto-mapper rules, or repositories need xUnit tests in `tests/Umbraco.Community.SchemeWeaver.Tests/Unit/`.
- New or changed Lit components, modals, entity actions, or workspace views need `@open-wc/testing` component tests under `src/**/*.test.ts` (use the MSW handlers in `src/mocks/`).
- UI flows that cross the backoffice boundary (opening a modal, saving a mapping, generating a doc type) should get a Playwright spec under `tests/e2e/` and be run locally via `npm run test:e2e` before the PR is marked ready.
- Bug fixes need a regression test that fails without the fix.

All tests must pass before a PR can be merged. CI runs `dotnet test` and the frontend test suite on every push.

### Umbraco backoffice skills and review agents

If you are using Claude Code (or another agent runner) to help with a contribution, please lean on the Umbraco-specific tooling we already use in this repo:

- **Umbraco backoffice skills** -- prefer these for any UI / extension work (workspace views, modals, property editors, entity actions, context tokens). They encode the correct `UmbLitElement`, `UmbModalBaseElement`, `UmbControllerBase`, and manifest patterns so generated code slots into the backoffice cleanly. See the skills bundled with [Claude Code for Umbraco](https://github.com/umbraco/Umbraco-CMS) and the local skill definitions under [`.claude/skills/`](.claude/skills/) (`simplify-umbraco`, `review`, `git-workflow`, etc.).
- **`umbraco-extension-reviewer` agent** -- run this whenever you have finished a UI change. It audits manifests, context usage, modal wiring, and naming against the Umbraco 17+ backoffice conventions. CLAUDE.md requires this to be run before UI work is considered complete.
- **E2E Playwright run** -- close the loop on any UI change with `npm run test:e2e` against a running Umbraco instance. Type checks and unit tests verify correctness; only E2E verifies the feature actually works in the backoffice.

### Coding style

- C#: follow the existing style in the solution (file-scoped namespaces, nullable enabled, standard Umbraco composer / controller patterns, NPoco for persistence).
- Frontend: TypeScript + Lit, `camelCase` DTOs matching the C# API, Umbraco backoffice observables and context tokens. Run `npm run build` to confirm Vite compiles cleanly.
- Keep public APIs stable. If you need to break one, call it out explicitly in the PR description.

### Guidance for AI coding assistants

AI tools (Claude, Copilot, Cursor, etc.) are welcome to help with SchemeWeaver contributions. The same rules apply as for human contributors, with a few extras to keep the history honest and the project safe.

**Follow the existing process.** AI-assisted patches must follow the workflow above: read [`CLAUDE.md`](CLAUDE.md), match the architecture in `src/Umbraco.Community.SchemeWeaver/`, use the Umbraco backoffice skills for UI work, and run the `umbraco-extension-reviewer` agent on UI changes.

**Licensing.** All AI-generated code must be compatible with the MIT Licence. Do not submit code an assistant produced by quoting large blocks from incompatible sources. You, the human submitter, are responsible for confirming this.

**Tests are non-negotiable.** An AI assistant must add tests for the code it writes (C# unit, Lit component, and/or Playwright E2E as appropriate). PRs that say "tests to follow" or that skip the test pyramid described above will be sent back.

**Human review and accountability.** Only a human can sign off on a contribution. The submitter is responsible for:

- Reading and understanding every line of AI-generated code before committing it.
- Confirming licence compatibility and that no secrets / private data leaked into the diff.
- Running `dotnet build`, `dotnet test`, `npm test`, and (for UI) `npm run test:e2e` locally.
- Taking full responsibility for the contribution in the PR.

**Attribution.** When AI tooling materially contributed to a commit, add an `Assisted-by` trailer to the commit message so we can track how AI assistance evolves in the project. Basic developer tools (`git`, `dotnet`, `npm`, editors) do not need to be listed.

```
Assisted-by: AGENT_NAME:MODEL_VERSION [TOOL1] [TOOL2]
```

Example:

```
Assisted-by: Claude:claude-opus-4-6 umbraco-extension-reviewer playwright
```

Where:

- `AGENT_NAME` is the AI tool or framework (e.g. `Claude`, `Copilot`, `Cursor`).
- `MODEL_VERSION` is the specific model version used.
- `[TOOL1] [TOOL2]` are optional specialised analysis tools or agents used (e.g. `umbraco-extension-reviewer`, `playwright`, `roslyn-analyzers`).

**Do not add DCO `Signed-off-by` trailers on behalf of a human.** Only the human submitter may add their own sign-off (if required) to certify the contribution.

## Licence

MIT -- see [LICENSE](LICENSE).

## Author

Oliver Picton / [Enjoy Digital](https://www.enjoy-digital.co.uk)
