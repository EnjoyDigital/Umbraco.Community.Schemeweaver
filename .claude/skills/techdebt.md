---
description: Find and fix technical debt in recently changed files — TypeScript, C#, Umbraco content modelling, and uSync. Run at the end of sessions to clean up.
user_invocable: true
---

# Tech Debt Scanner

Scan recently changed files for technical debt and fix issues found.

## Scope

By default, scan files changed since the last merge-base with `main`:

```bash
git diff --name-only $(git merge-base HEAD main)
```

If the user specifies a scope (file, directory, or glob), use that instead.

## What to Look For

### TypeScript / Next.js (`src/site/`)

- **Dead exports**: exported functions, types, or constants with zero imports elsewhere
- **`any` usage**: replace with proper types (check generated models in `@/api/model/`)
- **Missing return types**: add explicit return types to exported functions
- **Console statements**: remove `console.log`/`console.warn` left from debugging (keep intentional logging)
- **Inconsistent error handling**: missing try/catch in async functions that call external APIs
- **Unused imports**: imports that are no longer referenced
- **Hardcoded strings**: user-facing text that should use dictionary items (`@/helpers/dictionary.ts`)
- **Non-null assertions (`!`)**: replace with proper null checks or optional chaining
- **Hardcoded culture codes**: `"en-gb"`, `"de-de"` etc. should be constants, not scattered strings

### C# / .NET (`src/umbraco/`)

- **Deprecated Umbraco APIs** (v16+): `IPublishedContent.Parent()` → use `IDocumentNavigationQueryService.TryGetParentKey()`
- **Missing null checks**: especially on content property access and `Children()` / `Descendants()` calls
- **Unused `using` directives**
- **Inconsistent async patterns**: mixing `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` with `await` — use `await` in async contexts
- **Magic strings**: content type aliases or property names that should be constants in a dedicated class (e.g. `ContentTypeAliases.cs`)
- **Missing `ConfigureAwait(false)`** in library code (Core, Common layers)
- **Swallowed exceptions**: broad try-catch blocks that return null/default without logging
- **Over-injected controllers**: 8+ constructor parameters suggest the class is doing too much — consider extracting services

### Code Smells (All Languages)

Modelled on [ChernyCode](https://github.com/meleantonio/ChernyCode) patterns:

- **Code duplication**: functions with similar logic that could be consolidated, copy-pasted blocks
- **Dead code**: unused functions or classes, commented-out code blocks, unreachable code paths
- **Long functions**: functions longer than 50 lines — break into smaller, focused units
- **Too many parameters**: more than 5 parameters — consider a parameter object
- **Deep nesting**: more than 3 levels — flatten with early returns or guard clauses
- **Magic numbers**: unnamed numeric literals — extract to named constants
- **Outdated patterns**: old-style string formatting, deprecated APIs

### Umbraco Content Modelling

If uSync config files (`uSync/v16/`) are in scope, or if the user asks for a content model audit:

- **Orphaned content types**: document types with no content nodes using them — verify via Umbraco MCP (`document-type` tools) or by checking the content tree
- **Unused compositions**: composition types that are no longer inherited by any document type
- **Missing culture variants**: properties that should support `<Variations>Culture</Variations>` but don't, especially on block list items and SEO fields
- **Composition ripple risk**: changes to base compositions (e.g. `master.config`, `base.config`) affect 100+ content types — flag these for careful review
- **Block list dependency fragility**: if a block type referenced in a DataType is removed, the UI fails silently — verify block type references still exist
- **Property editor mismatches**: DataType definitions referencing property editors that have been renamed or removed in Umbraco 16+

### uSync File Hygiene

- **Duplicate keys**: two uSync config files defining the same alias or key
- **Orphaned configs**: `.config` files for content types or data types that no longer exist in the backoffice
- **Missing language variants**: content items that exist in one culture but are missing translations, causing blank listings
- **Circular composition references**: compositions that reference each other (uSync won't detect this)

## Using Umbraco MCP for Auditing

If the Umbraco MCP server is available (check `.mcp.json` for `umbraco-mcp`), use it to:

- **List document types** and cross-reference against uSync ContentTypes to find orphans
- **Query content nodes** to verify document types are in active use
- **Check data types** to confirm property editors are valid
- **Audit media** for unused or orphaned media items

This is particularly useful for content modelling debt that can't be detected from code alone.

## Workflow

1. **Identify files in scope**
2. **Read each file and catalogue issues**
3. **Prioritise by severity**: correctness > type safety > consistency > style
4. **Fix issues**, grouping related changes together
5. **Run verification**:
   - Frontend changes: `cd src/site && bun run lint && bun run build`
   - Backend changes: `cd src/umbraco && dotnet build`
   - If touching test-covered code: run tests
6. **Report** what was found and fixed, plus any remaining items for future sessions

## Output

Provide a summary of:
- Issues found (by category and severity: high/medium/low)
- Issues fixed
- Remaining items for future sessions

## Important

- Use `bun` for all frontend tooling (never `npm`)
- Use `bun run lint` and `bun run prettier:fix` for frontend linting
- Use `bun test` for frontend tests
- Use `dotnet build` for backend verification
- Do not refactor working code just for style — only fix genuine debt
- Do not touch generated files in `src/site/src/api/` or `src/site/src/api-site/`
- Use British English spelling conventions
