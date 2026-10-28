# AGENTS.md

Instructions for coding agents (Claude Code, Codex, Copilot, Cursor and friends) working in this repository. Claude Code 2.1.277+ reads this file directly; older builds need a local, uncommitted `CLAUDE.md` containing `@AGENTS.md`.

## Project Overview

Umbraco community package that maps Content Types to Schema.org types and generates JSON-LD structured data. Built for Umbraco 17 & 18 on .NET 10 with a Lit web components backoffice UI.

## Build & Test Commands

### C# Backend
```bash
dotnet build                    # Build solution
dotnet test                     # Run all C# tests (unit + integration)
dotnet test --filter "FullyQualifiedName~Unit"         # Unit only
dotnet test --filter "FullyQualifiedName~Integration"  # Integration only (WebApplicationFactory)
dotnet pack src/Umbraco.Community.SchemeWeaver/Umbraco.Community.SchemeWeaver.csproj --configuration Release --output ./artifacts
```

C# integration tests boot the SchemeWeaver TestHost via
`WebApplicationFactory<Program>` against a temp SQLite database, shared
across every test class through an xUnit collection fixture
(`SchemeWeaverIntegrationCollection`). One host per suite keeps Umbraco's
static state from colliding when multiple factories would otherwise boot
in parallel. Authorization is bypassed by a test `IPolicyEvaluator`. See
`tests/Umbraco.Community.SchemeWeaver.Tests/Integration/Fixtures/`.

### Frontend (in src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/)
```bash
npm install
npm run build                   # Vite build → ../../wwwroot/dist/
npm test                        # Web Test Runner unit + component tests
npm run test:msw                # Same suite with MSW enabled for HTTP mocks
npm run test:watch              # Watch mode
npm run test:e2e                # Playwright E2E (all specs in tests/e2e/)
npm run test:e2e:ui             # Playwright UI mode
```

E2E tests require `.env` with `UMBRACO_URL`, `UMBRACO_USER_LOGIN`, `UMBRACO_USER_PASSWORD`.

### SEO frontend (in src/Umbraco.Community.SchemeWeaver.Seo/App_Plugins/SchemeWeaverSeo/)
```bash
npm install                     # needs the core frontend's node_modules too (shared source resolves from there)
npm run typecheck               # tsc --noEmit (the only place the SEO TS is type-checked)
npm run build                   # Vite build → ../../wwwroot/dist/ (+ cache-bust stamp)
npm test                        # core-bridge guard + Web Test Runner (MSW-backed context/view tests)
npm run test:msw
```
E2E coverage for the SEO section lives in the core suite (`tests/e2e/seo-section.spec.ts` and the docs screenshot spec `seo-docs-screenshots.spec.ts`, both in the `e2e-seo` Playwright project; run them with `--project setup --project e2e-seo --no-deps`). The SEO bundle also has its own mocked-backoffice tier (`tests/mocked-backoffice/`, composite core+SEO mirror; same pinned Umbraco-CMS clone and patch as the core tier).

### MCP Server (in src/Umbraco.Community.SchemeWeaver.Mcp/)
```bash
npm install
npm run generate                # Extract SchemeWeaver OpenAPI subset from running TestHost + Orval client gen
npm run build                   # tsup → dist/ (the entry point .mcp.json points at)
npm test                        # Integration tests against the running TestHost
```

TypeScript MCP server on `@umbraco-cms/mcp-server-sdk` exposing 16 SchemeWeaver tools (schema type search, content type inspection, heuristic suggest, save/delete mapping, JSON-LD preview, validation, uSync drift/export, live Delivery-API render verification, doc type generation), 10 SchemeWeaver SEO tools (`tools/schemeweaver-seo/`: content kinds, per-type status, kind set-up + save, site identity, page summary/override, health check; `scripts/extract-openapi.mjs` keeps both the `SchemeWeaver` and `SchemeWeaverSeo` OpenAPI tags) plus a `get-server-info` smoke-test tool from the base SDK, so AI assistants can build better mappings than the heuristic auto-mapper. Ships three end-user skills (`schemeweaver-setup`, `schemeweaver-map`, `schemeweaver-audit`) from its `skills/` directory. Registered in `.mcp.json` as `schemeweaver-mcp`; authenticates as an Umbraco API user (client credentials in the project's gitignored `.env`; `scripts/setup-api-user.mjs` creates the user in a fresh TestHost DB).

It is also distributed as a **Claude Code plugin**: the repo-root `.claude-plugin/marketplace.json` lists the `schemeweaver-mcp` plugin (`src/Umbraco.Community.SchemeWeaver.Mcp/.claude-plugin/plugin.json`), which registers the server via `mcpServers` running the committed, self-contained `dist/index.js` from `${CLAUDE_PLUGIN_ROOT}` and collects Umbraco credentials via `userConfig`. End-users install with `/plugin marketplace add EnjoyDigital/Umbraco.Community.Schemeweaver` then `/plugin install schemeweaver-mcp@schemeweaver`. The bundled `dist/index.js` is committed (tsup `noExternal` makes it standalone) and **must be rebuilt and re-committed** whenever the server changes. See the project's own README/AGENTS.md for details.

## Architecture

### Backend (C#)
- **Controllers/** — `SchemeWeaverApiController` at `/umbraco/management/api/v1/schemeweaver`
- **Deploy satellite** (`src/Umbraco.Community.SchemeWeaver.Deploy/`) — Umbraco Deploy addon:
  `SchemaMappingServiceConnector` (+ `SchemaMappingArtifact`, UDI type `schemeweaver-mapping`
  seeded from `ContentTypeKey`) registers mappings as a disk entity type so they ship as
  `.uda` artifacts with the doc types. References only `Umbraco.Deploy.Infrastructure`;
  all licensed Deploy services are soft-resolved (satellite-without-OnPrem warns once and
  no-ops). The artifact class's namespace/assembly is a serialized wire contract — never
  rename it. It must NOT register the uSync seams (`IMappingDriftReporter`/`IMappingExporter`).
  Its tests live in `tests/Umbraco.Community.SchemeWeaver.Deploy.Tests` — the ONLY test
  project allowed to reference `Umbraco.Deploy.OnPrem` (dependency-context isolation keeps
  Deploy out of every other integration boot); its integration tests drive the real Deploy
  disk pipeline licence-free via Deploy's public `NullLicensing`. TestHost gets Deploy only
  via `dotnet run -p:SchemeWeaverIncludeDeploy=true` (run `dotnet clean` when toggling).
- **AI satellite** (`src/Umbraco.Community.SchemeWeaver.AI/`) — AI-powered schema mapping
  via Umbraco.AI. Ships per-major like the rest of the suite (17.x / 18.x via
  `-p:UmbracoMajor`). Depends on the umbrella `Umbraco.AI` package (never a provider);
  per-leg versions come from the `SchemeWeaverUmbracoAi*` properties in
  Directory.Build.props. The TestHost includes it for both majors with the Anthropic
  provider.
- **TypeSafe satellite** (`src/Umbraco.Community.SchemeWeaver.TypeSafe/`): calibrated
  auto-map suggestions from TypeSafe System One (model Jev, `POST /v1/systemone`, raw HTTP
  via `HttpClient`; there is no .NET SDK and no third-party AI framework dependency). The
  composer runs `[ComposeAfter(SchemeWeaverComposer)]` and DECORATES `ISchemaAutoMapper`
  (`TypeSafeSchemaAutoMapper` wraps whatever was registered before it: the heuristic, or the
  AI mapper when both are installed). Ordering against `.AI` is deterministic: the AI composer
  implements the core marker `ISchemaAutoMapperReplacingComposer` and the TypeSafe composer
  declares `[ComposeAfter]` on that interface (weak, satisfied when absent), so the cascade is
  always TypeSafe, then AI, then heuristic. Any new composer that REPLACES the seam must
  implement the marker. This wiring means the property-mapping modal and the MCP
  `suggest-property-mappings` tool get TypeSafe results with no UI change. Closed-set
  judgments in rounds (bind, shape, beam-search nested-type descent from the declared
  range, inner bind, then v2's cross-node round over the discovered neighbourhood and a
  per-block-type routes round for nested blocks); `PriorsMode` decides what the heuristic's
  rows do (default `None`); media pickers are never wrapped; cross-node
  parent/ancestor/sibling rows name the source content type (`NeighbourhoodDiscovery`
  finds neighbours from declared structure and/or sampled pages) and nested-block `routes`
  are emitted per `RoutesMode` (default `Auto`), so neither is left to the prior mapper any
  more; a bind answer's runner-up can become a second, never pre-ticked target for the same
  content property (gated on `SecondaryBindingMinProbability`, not the show threshold).
  Inert without `SchemeWeaver:TypeSafe:ApiKey` (user-secrets / env var, never appsettings)
  and on any API failure; one startup log line says active/inert and what it wraps; Health
  Check "SchemeWeaver TypeSafe". Ships per-major like the rest of the suite purely because
  the core does. Tests live under `tests/Umbraco.Community.SchemeWeaver.Tests/Unit/TypeSafe/`.
- **SEO satellite** (`src/Umbraco.Community.SchemeWeaver.Seo/`): the Yoast-style edition for
  non-developers. Adds an **SEO Schema** backoffice section (alias `SchemeWeaver.Seo.Section`,
  a wire contract between `SchemeWeaverSeoConstants.SectionAlias` and the frontend
  `constants.ts`; an integration test asserts they match), a `Templates/catalogue.json` of
  curated "kinds" (Schema.org type + slots = Google's required/recommended properties; the
  `SeoCatalogueRuleParityTests` keep required slots identical to the core `ITypeRule` Critical
  checks), a site identity stored as JSON in `IKeyValueService`, a per-page override table
  (`SchemeWeaverSeoPageOverride`, own `PackageMigrationPlan` "SchemeWeaver.Seo") and a
  controller at `/umbraco/management/api/v1/schemeweaver-seo` guarded by the
  `SchemeWeaverSeoSectionAccess` policy (section access read from `IUser.AllowedSections`).
  **The SEO edition has no model of its own**: `SeoSlotProjector` projects slots ↔ ordinary
  `PropertyMappingDto` rows server-side and saves through the core `ISchemeWeaverService`, so
  both UIs, the generator, the validator and the MCP always see the same mapping; rows a kind
  does not own are preserved. It plugs into two core seams, both inert by default:
  `ISiteIdentityProvider` (Organization/WebSite pieces consult it before the
  `schemaSiteSettings` node) and `IContentMappingOverrideProvider` (consulted by
  `JsonLdGenerator` right after the mapping loads: Excluded / Retyped clone). The section is
  granted to Administrators once on first start (`GrantSeoSectionToAdministrators`, key-value
  flag). Never references uSync. Frontend: own Vite bundle (`App_Plugins/SchemeWeaverSeo`),
  section + four `sectionView`s + wizard modal + a `workspaceView` "Schema" tab on documents;
  the entry point calls `umbExtensionsRegistry.exclude` on the core JSON-LD tab unless
  `SchemeWeaver:Seo:HideDeveloperJsonLdTab=false`. Shares core TS only through
  `src/core-bridge/` (relative imports into the core `src/`, pure modules only; the
  `scripts/check-core-bridge.mjs` guard runs before `npm test`); the core defines the shared
  `schemeweaver-jsonld-preview` / `schemeweaver-validation-panel` elements eagerly in its entry
  point for that reason. Tests: `Unit/Seo/`, `Integration/Seo/` (shared host; `ProbeContent`
  fixture publishes real content), WTR in the SEO folder, `tests/e2e/seo-section.spec.ts`.
- **Services/** — `SchemaTypeRegistry` (singleton, scans Schema.NET assembly), `JsonLdGenerator`, `SchemaAutoMapper`, `ContentTypeGenerator`
- **Persistence/** — `SchemaMappingRepository` using NPoco (two tables: `SchemeWeaverSchemaMapping`, `SchemeWeaverPropertyMapping`)
- **Models/Api/** — DTOs serialised as camelCase JSON: `SchemaMappingDto`, `PropertyMappingDto`, `SchemaTypeInfo`, `PropertyMappingSuggestion`, `JsonLdPreviewResponse`, `ContentTypeGenerationRequest`
- **Models/Entities/** — NPoco-mapped database entities
- **Composing/** — `SchemeWeaverComposer` registers all DI services
- **DeliveryApi/** — `SchemaJsonLdContentIndexHandler` adds JSON-LD to delivery API
- **Migrations/** — Database table creation migration
- **TagHelpers/** — Razor tag helper for template output

### Frontend (Lit Web Components)
- **api/types.ts** — TypeScript interfaces matching C# DTOs (camelCase)
- **repository/** — `SchemeWeaverServerDataSource` (fetch wrapper) → `SchemeWeaverRepository` (facade)
- **context/** — `SchemeWeaverContext` with Umbraco observables
- **modals/** — Schema picker, property mapping, generate doctype (each has `.element.ts` + `.token.ts`)
- **components/** — `property-mapping-table` (editable rows), `jsonld-preview` (formatted JSON-LD with validation)
- **entity-actions/** — "Map to Schema.org", "Delete Schema.org Mapping", and "Generate from Schema.org" on the document type actions menu
- **workspace-views/** — Schema.org tab on the document type editor and JSON-LD preview tab on content items
- **mocks/** — MSW handlers + in-memory mock DB for component tests

### Key Patterns
- Workspace views and entity actions use `UMB_MODAL_MANAGER_CONTEXT` to open modals
- Auto-map endpoint returns flat `PropertyMappingSuggestion[]` array (not wrapped)
- `PropertyMappingRow` is a UI-only type combining DTO fields with display-only fields (schemaPropertyType, confidence)
- Source type values are lowercase strings matching C#: `property`, `static`, `parent`, `ancestor`, `sibling`
- Confidence is integer 0–100 from C# auto-mapper (thresholds: ≥80 High, ≥50 Medium)
- Frontend builds to `wwwroot/dist/` which gets included as static web assets in the NuGet package
- Core satellite seams: `ISiteIdentityProvider` + `IContentMappingOverrideProvider` (see the SEO satellite). Core constructors gain seams only as optional trailing parameters so the unit tests that `new` them keep compiling.
- The core `fetchApi` (`repository/schemeweaver.server-data-source.ts`) is exported and accepts absolute `/umbraco/...` paths so satellite bundles share its session-expiry and error-toast behaviour

### Language Variants (Culture Support)
- `JsonLdGenerator.GenerateJsonLd(content, culture)` accepts an optional `string? culture`. When set, it pushes a `VariationContext(culture)` scope onto `IVariationContextAccessor` for the duration of the call, fixing URL generation and all transitive `GetValue()` calls.
- `PropertyResolverContext.Culture` threads the culture to every resolver; all resolvers pass `GetValue(culture: context.Culture)`.
- `SchemeWeaverTagHelper` reads culture from `IVariationContextAccessor.VariationContext?.Culture` automatically.
- `SchemaJsonLdExpansion` (Delivery API handler) sets `VariesByCulture = true` and passes the framework-supplied culture to the generator.
- Preview endpoint accepts `?culture=` query param; the frontend JSON-LD preview tab reads the active workspace variant via `splitView.activeVariantByIndex(0)`.
- `inLanguage` is auto-populated from the culture when the mapping doesn't explicitly map it.
- Mappings are invariant — no per-culture mapping rows. The same mapping applies to all cultures; values are resolved at generation time.
- Ancestor/sibling probes use invariant `GetValue()` for existence checks, culture for value resolution.

### API Endpoints
All under `/umbraco/management/api/v1/schemeweaver`, backoffice-authenticated:
- `GET /schema-types` — list/search Schema.org types
- `GET /schema-types/{name}/properties` — properties for a type
- `GET /content-types` — all Umbraco content types
- `GET /content-types/{alias}/properties` — properties for a content type
- `GET /mappings` — all mappings
- `POST /mappings` — save mapping
- `DELETE /mappings/{alias}` — delete mapping
- `POST /mappings/{alias}/auto-map?schemaTypeName=X` — suggest property mappings
- `POST /mappings/{alias}/preview?contentKey=X` — generate JSON-LD preview
- `POST /generate-content-type` — create Umbraco doc type from schema

SEO satellite, under `/umbraco/management/api/v1/schemeweaver-seo` (section-access policy): `GET templates`, `GET server-context`, `GET/PUT/DELETE site-identity`, `GET content-types`, `POST content-types/{alias}/setup`, `PUT|DELETE content-types/{alias}/mapping`, `GET content-types/{alias}/sample-pages`, `GET/PUT/DELETE pages/{key}`, `GET pages/{key}/summary`, `POST health-check`. See `docs/seo-edition.md`.

## Testing Pyramid
| Layer | Framework | Location |
|---|---|---|
| C# Unit | xUnit + NSubstitute + FluentAssertions | tests/Umbraco.Community.SchemeWeaver.Tests/Unit/ |
| C# Integration | xUnit + Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory<Program>` against the SchemeWeaver TestHost, shared via xUnit collection fixture + temp SQLite) | tests/Umbraco.Community.SchemeWeaver.Tests/Integration/ |
| TS Unit/Component | @open-wc/testing + MSW | App_Plugins/SchemeWeaver/src/**/*.test.ts and App_Plugins/SchemeWeaverSeo/src/**/*.test.ts (served from the repo `src/` root so the shared core modules resolve) |
| Mocked Backoffice | Playwright + the real Umbraco-CMS backoffice via `VITE_EXAMPLE_PATH`, all HTTP served by SchemeWeaver's own MSW handlers. Needs a local Umbraco-CMS clone and a tiny `addMockHandlers` patch — see `App_Plugins/SchemeWeaver/tests/mocked-backoffice/README.md` | App_Plugins/SchemeWeaver/tests/mocked-backoffice/ |
| E2E | Playwright + @umbraco/playwright-testhelpers | App_Plugins/SchemeWeaver/tests/e2e/ |

TypeSafe satellite unit tests (`Unit/TypeSafe/`) never hit the network: they use a fake
`ITypeSafeClient` and the gold-oracle pattern from `eval/oracle.mjs` (a stub that answers
every question the way gold implies, so the harness ceiling, not the model, is under test).

The eval harness for the satellite is `eval/run-typesafe.mjs` (scores the JS mapper vs
heuristic vs gold from the cached context; needs a TypeSafe key), `eval/oracle.mjs` (no key,
no network; prints the v1 ceiling and answers the v2 question kinds for unit tests), and
`eval/run-typesafe-live.mjs` (scores the C# package LIVE through the TestHost's auto-map
endpoint; this is where v2's cross-node rows and routes are measured, since
`eval/typesafe-mapper.mjs` is the frozen v1 specification and must not grow v2). The first
two read `eval/cache/schema-types.json`, which `eval/SchemaTypeDump` (a C# console app that
instantiates the production `SchemaTypeRegistry` directly, no Umbraco boot) generates.

The Mocked Backoffice tier currently requires the user to apply a
one-line patch to their local Umbraco-CMS clone, because Umbraco v17.2.2
does not yet expose a runtime `addMockHandlers` API on
`window.MockServiceWorker`. The patch uses `??=` so it becomes a no-op
once upstream ships the real API. See the harness README for the exact
`git apply` command.

## Error-handling policy
JSON-LD generation and rendering must **never break the host page**. Resolvers, graph pieces, the tag helper and the Delivery API handlers deliberately catch **all** exceptions at their boundaries, log them (Debug/Warning), and degrade to omitting output. API endpoints funnel unexpected exceptions through `HandlesServerErrorAttribute`, which returns 500 with the frozen `{ error: "An unexpected error occurred whilst <operation>." }` body the frontend parses — do not replace it with ProblemDetails. CodeQL "generic catch clause" findings at these boundaries are dismissed as won't-fix **by policy**; do not "fix" them by narrowing the catch or letting exceptions propagate into rendered pages.

## Conventions
- British English spelling
- Package name is "SchemeWeaver" (intentional wordplay, not a typo)
- Frontend uses Umbraco backoffice patterns: `UmbLitElement`, `UmbModalBaseElement`, `UmbControllerBase`, `UmbContextToken`
- C# uses standard Umbraco patterns: `IComposer`, management API controllers, NPoco migrations

## Workflow
- Use **Umbraco backoffice skills** for UI/extension work (workspace views, modals, property editors, etc.)
- Run the **review agent** (`umbraco-extension-reviewer`) when finished with UI changes
- Run **E2E tests** (`npm run test:e2e`) to close the loop on UI changes before considering work complete

## Releasing
The release workflow (`.github/workflows/release.yml`) uses `generate_release_notes: true`, which produces **empty or near-useless notes** because most commits land directly on `main` rather than via PRs. When cutting a release — or any time a version tag has just been pushed — **overwrite the auto-generated body with hand-crafted notes** before moving on.

Procedure:
1. After the tag is pushed and the release exists, run `git log --pretty=format:'%h %s' <previous-tag>..<this-tag>` to list what actually changed.
2. Group into **Features / Fixes / Refactors / Docs / Build / Chores** (omit empty sections). Lead each bullet with the user-visible effect, not the commit subject.
3. Call out **compatibility notes** explicitly — breaking changes, new defaults users will see, new config keys and their defaults. If there are none, say so in one line.
4. For any fix that changes behaviour (even subtly, like the `@id` change in v1.1.0), explain **what was wrong before** so users can judge whether it affects them.
5. Publish with:
   ```bash
   gh release edit vX.Y.Z --notes "$(cat <<'EOF'
   ...notes...
   EOF
   )"
   ```
6. Keep the trailing `**Full Changelog**: https://github.com/.../compare/<prev>...<this>` link — it's how people drill into the diff.

Do **not** leave a release with only the auto-generated "Full Changelog" line. Treat that as a bug to fix before the release is considered shipped.
