# SchemeWeaver MCP Server

An MCP (Model Context Protocol) server that exposes [SchemeWeaver](https://github.com/enjoy-digital/Umbraco.Community.SchemeWeaver)'s Schema.org mapping capabilities to AI assistants, built on the official [Umbraco MCP Server SDK](https://docs.umbraco.com/umbraco-in-ai/mcp/base-mcp/sdk).

The idea: SchemeWeaver's built-in auto-mapper is a name-matching heuristic. An LLM with these tools can reason **semantically** about a content type's properties and a Schema.org type's vocabulary, produce a better mapping, save it, and verify the generated JSON-LD — end to end. Pair it with the official Umbraco MCP (`@umbraco-cms/mcp-dev`) for full content-and-schema workflows.

## Tools

| Tool | Purpose |
|---|---|
| `search-schema-types` | Search the Schema.org vocabulary (~800 types) |
| `get-schema-type-properties` | Properties of a type, optionally ranked by real-world importance |
| `list-content-types` | All Umbraco content types with aliases and keys |
| `get-content-type-properties` | Properties + editor aliases of a content type (incl. built-ins `__name`, `__url`, …) |
| `get-block-element-types` | Element types inside a Block List/Grid property |
| `get-all-schema-mappings` / `get-schema-mapping` | Existing mappings |
| `suggest-property-mappings` | The heuristic auto-mapper baseline (confidence 0–100) |
| `save-schema-mapping` | Create/replace a mapping (full property-mapping model: property, static, parent, ancestor, sibling, blockContent, reference) |
| `delete-schema-mapping` | Remove a mapping |
| `preview-json-ld` | Generate JSON-LD for a content node + Rich Results validation issues |
| `generate-content-type` | Scaffold a new document type from a Schema.org type |

The base SDK also registers a `get-server-info` tool (server version/runtime — handy as an
auth smoke test), so `--list-tools` reports 13 in total.

The recommended AI workflow (also sent to clients as server `instructions`): inspect both sides → heuristic baseline → reason semantically → save → preview → fix validation issues.

## Prerequisites

- Node.js >= 22
- A running Umbraco instance (v17/18) with SchemeWeaver installed — the repo's TestHost (`https://localhost:44308`) works out of the box
- An **API user** in that instance (Users → Create → API user) with client credentials

For the TestHost, `scripts/setup-api-user.mjs` creates the API user automatically:

```bash
node --env-file=.env scripts/setup-api-user.mjs <admin-email> <admin-password>
```

## Setup

```bash
npm install
cp .env.example .env        # fill in UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET / UMBRACO_BASE_URL
npm run generate            # extract OpenAPI subset from the running instance + regenerate the typed client
npm run build               # build dist/ (also regenerates dist/tool-types.d.ts)
```

## Running

The repo's `.mcp.json` registers the server for Claude Code automatically (`schemeweaver-mcp`). To run it manually:

```bash
node --env-file=.env dist/index.js
```

CLI introspection without starting the server:

```bash
node --env-file=.env dist/index.js --list-tools
node --env-file=.env dist/index.js --call search-schema-types --call-args '{"search":"recipe"}'
```

Or use the MCP Inspector: `npm run inspect`.

### Example prompt

> Map the `blogPost` content type to the best-fitting Schema.org type using the SchemeWeaver tools. Improve on the heuristic suggestions, save the mapping, and verify the preview validates for Google rich results.

## Tool filtering

Standard SDK filtering applies via env vars / CLI flags: `UMBRACO_READONLY=true` disables the write tools; `UMBRACO_EXCLUDE_TOOLS=delete-schema-mapping,generate-content-type` removes specific ones; `UMBRACO_INCLUDE_SLICES=read,list,search` keeps only reads. See `.env.example`.

## Testing

```bash
npm test           # integration tests against the running instance from .env
npm run compile    # type-check
```

The mapping round-trip test picks a content type with **no existing mapping** and deletes its mapping afterwards, so fixture data is never disturbed.

## Regenerating the API client

`npm run generate` runs `scripts/extract-openapi.mjs` (pulls the SchemeWeaver-tagged subset out of `/umbraco/openapi/management.json` — the full document describes every management API) and then Orval. The extracted spec (`src/umbraco-api/api/schemeweaver-openapi.json`) is committed, so plain `npx orval --config orval.config.ts` works offline.
