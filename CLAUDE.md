# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Umbraco community package that maps Content Types to Schema.org types and generates JSON-LD structured data. Built for Umbraco 17+ on .NET 10 with a Lit web components backoffice UI.

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

## Architecture

### Backend (C#)
- **Controllers/** — `SchemeWeaverApiController` at `/umbraco/management/api/v1/schemeweaver`
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

## Testing Pyramid
| Layer | Framework | Location |
|---|---|---|
| C# Unit | xUnit + NSubstitute + FluentAssertions | tests/Umbraco.Community.SchemeWeaver.Tests/Unit/ |
| C# Integration | xUnit + Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory<Program>` against the SchemeWeaver TestHost, shared via xUnit collection fixture + temp SQLite) | tests/Umbraco.Community.SchemeWeaver.Tests/Integration/ |
| TS Unit/Component | @open-wc/testing + MSW | App_Plugins/SchemeWeaver/src/**/*.test.ts |
| Mocked Backoffice | Playwright + the real Umbraco-CMS backoffice via `VITE_EXAMPLE_PATH`, all HTTP served by SchemeWeaver's own MSW handlers. Needs a local Umbraco-CMS clone and a tiny `addMockHandlers` patch — see `App_Plugins/SchemeWeaver/tests/mocked-backoffice/README.md` | App_Plugins/SchemeWeaver/tests/mocked-backoffice/ |
| E2E | Playwright + @umbraco/playwright-testhelpers | App_Plugins/SchemeWeaver/tests/e2e/ |

The Mocked Backoffice tier currently requires the user to apply a
one-line patch to their local Umbraco-CMS clone, because Umbraco v17.2.2
does not yet expose a runtime `addMockHandlers` API on
`window.MockServiceWorker`. The patch uses `??=` so it becomes a no-op
once upstream ships the real API. See the harness README for the exact
`git apply` command.

## Conventions
- British English spelling
- Package name is "SchemeWeaver" (intentional wordplay, not a typo)
- Frontend uses Umbraco backoffice patterns: `UmbLitElement`, `UmbModalBaseElement`, `UmbControllerBase`, `UmbContextToken`
- C# uses standard Umbraco patterns: `IComposer`, management API controllers, NPoco migrations

## Workflow
- Use **Umbraco backoffice skills** for UI/extension work (workspace views, modals, property editors, etc.)
- Run the **review agent** (`umbraco-extension-reviewer`) when finished with UI changes
- Run **E2E tests** (`npm run test:e2e`) to close the loop on UI changes before considering work complete
