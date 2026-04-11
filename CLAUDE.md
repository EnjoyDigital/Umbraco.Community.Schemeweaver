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
`WebApplicationFactory<Program>` against a per-class temp SQLite database,
with authorization bypassed by a test `IPolicyEvaluator`. See
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
- **entity-actions/** — "Map to Schema.org", "Delete Schema.org Mapping", and "Generate from Schema.org" on the document type context menu
- **workspace-views/** — Schema.org tab on the document type editor and JSON-LD preview tab on content items
- **mocks/** — MSW handlers + in-memory mock DB for component tests

### Key Patterns
- Workspace views and entity actions use `UMB_MODAL_MANAGER_CONTEXT` to open modals
- Auto-map endpoint returns flat `PropertyMappingSuggestion[]` array (not wrapped)
- `PropertyMappingRow` is a UI-only type combining DTO fields with display-only fields (schemaPropertyType, confidence)
- Source type values are lowercase strings matching C#: `property`, `static`, `parent`, `ancestor`, `sibling`
- Confidence is integer 0–100 from C# auto-mapper (thresholds: ≥80 High, ≥50 Medium)
- Frontend builds to `wwwroot/dist/` which gets included as static web assets in the NuGet package

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
| C# Integration | xUnit + Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory<Program>` against the SchemeWeaver TestHost + per-class temp SQLite) | tests/Umbraco.Community.SchemeWeaver.Tests/Integration/ |
| TS Unit/Component | @open-wc/testing + MSW | App_Plugins/SchemeWeaver/src/**/*.test.ts |
| E2E | Playwright + @umbraco/playwright-testhelpers | App_Plugins/SchemeWeaver/tests/e2e/ |

**Known gap — Mocked Backoffice tier.** The Umbraco Backoffice Skills pyramid
includes a "Mocked Backoffice" tier (Playwright driving the real Umbraco
backoffice UI with MSW faking the backend). See the
[umbraco-mocked-backoffice skill](https://github.com/umbraco/Umbraco-CMS-Backoffice-Skills).
SchemeWeaver does not currently wire this up because the canonical harness
needs the `umbraco/Umbraco-CMS` source cloned locally plus a Vite + manifest
bootstrap recipe from the Umbraco-CMS `tree-example`. Contributors adopting
that tier should follow the skill repo and land the harness under
`App_Plugins/SchemeWeaver/tests/mocked-backoffice/`.

## Conventions
- British English spelling
- Package name is "SchemeWeaver" (intentional wordplay, not a typo)
- Frontend uses Umbraco backoffice patterns: `UmbLitElement`, `UmbModalBaseElement`, `UmbControllerBase`, `UmbContextToken`
- C# uses standard Umbraco patterns: `IComposer`, management API controllers, NPoco migrations

## Workflow
- Use **Umbraco backoffice skills** for UI/extension work (workspace views, modals, property editors, etc.)
- Run the **review agent** (`umbraco-extension-reviewer`) when finished with UI changes
- Run **E2E tests** (`npm run test:e2e`) to close the loop on UI changes before considering work complete
