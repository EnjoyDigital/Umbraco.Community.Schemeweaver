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
| `get-block-element-types` | Element types inside a Block List/Grid property, recursively surfacing blocks nested inside blocks (and Block Grid areas) via `propertyInfos[].nestedBlockElementTypes` |
| `get-all-schema-mappings` / `get-schema-mapping` | Existing mappings |
| `suggest-property-mappings` | The heuristic auto-mapper baseline (confidence 0–100) |
| `save-schema-mapping` | Create/replace a mapping (full property-mapping model: property, static, parent, ancestor, sibling, blockContent, reference). `blockContent` uses a nestable `routes` resolverConfig that maps blocks — and blocks nested inside blocks / Block Grid areas — to nested Schema.org objects |
| `delete-schema-mapping` | Remove a mapping |
| `preview-json-ld` | Generate JSON-LD for a content node + Rich Results validation issues (backoffice-context preview; reports `context`/`resolvedBaseUrl`) |
| `get-rendered-json-ld` | Fetch the **live** JSON-LD a published page emits, straight from the anonymous Delivery API (ground truth; distinct from the backoffice preview) |
| `validate-mapping` | Severity-ranked "doctor" checklist for a mapping (`critical` > `warning` > `suggestion` > `info`); loop until `allClear` |
| `generate-content-type` | Scaffold a new document type from a Schema.org type |
| `export-mappings-to-usync` | Export mappings to uSync files |
| `get-usync-drift` | Report drift between the live mappings and the uSync files on disk |

The base SDK also registers a `get-server-info` tool (server version/runtime + the configured base
URL — handy as an auth smoke test), so `--list-tools` reports 17 in total (16 SchemeWeaver + the base tool).

The recommended AI workflow (also sent to clients as server `instructions`): inspect both sides → heuristic baseline → reason semantically → save → preview → fix validation issues.

### preview vs live render

`preview-json-ld` renders in the **backoffice/management context**, so its URL/`@id` resolution
(and therefore its `isValid` verdict) reflect the management host, not the public site —
it is not proof the live structured data is valid. For the **authoritative** output a public visitor
sees, use `get-rendered-json-ld`, which calls SchemeWeaver's anonymous Delivery API
(`/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route`). That endpoint is **OFF by default** in
Umbraco (enable the Delivery API; optionally protect it with an API key — see
`UMBRACO_DELIVERY_API_KEY` in `.env.example`). The tool always surfaces `requestUrl` and `httpStatus`
even on 404/401/empty, and flags the HTTP-200-but-zero-blocks case rather than claiming success.

**Self-signed TLS:** `get-rendered-json-ld` uses a plain `fetch`, so a self-signed localhost host
needs `NODE_TLS_REJECT_UNAUTHORIZED=0`. As a Claude Code plugin only the `UMBRACO_*` vars are
forwarded, so that var does not propagate — the same limitation the management-API tools have.

## Install as a Claude Code plugin

For [Claude Code](https://claude.com/claude-code) users, this is the no-clone, no-build path. This
repo is a Claude Code plugin marketplace and ships a pre-built, self-contained server, so it's two
commands:

```text
/plugin marketplace add EnjoyDigital/Umbraco.Community.Schemeweaver
/plugin install schemeweaver-mcp@schemeweaver
/reload-plugins
```

Claude Code prompts for your **Umbraco Base URL**, **API User Client ID**, and **API User Client
Secret** on install (the secret goes to your OS keychain), then launches the `schemeweaver` server
automatically. You still need the [Prerequisites](#prerequisites) below — an Umbraco instance with
SchemeWeaver and an API user. The sections after that (Setup / Running) are for **local development**
of the server itself, not for plugin users.

### The bundled skills

The plugin also ships three skills (declared via `"skills": "./skills/"` in
`.claude-plugin/plugin.json`, sources under [`skills/`](skills/)), so they install alongside the
server — no extra step. Each triggers automatically from plain language, or explicitly by slug:

- **`schemeweaver-setup`** (`/schemeweaver-mcp:schemeweaver-setup`) — connect Claude to your
  Umbraco site and prove it: API user creation, plugin configuration, a verification ladder, and
  a troubleshooting table for 401s, login-page-instead-of-JSON and friends.
- **`schemeweaver-map`** (`/schemeweaver-mcp:schemeweaver-map`) — the guided end-to-end mapping
  loop for one content type (inspect → pick the most specific type → rank props → beat the
  heuristic → save → preview + validate, looping until `allClear`). "Map my `blogPost` to
  Schema.org and validate it."
- **`schemeweaver-audit`** (`/schemeweaver-mcp:schemeweaver-audit`) — site-wide structured-data
  audit: coverage sweep, rich-results triage, validation pass, live output spot checks, uSync
  drift, a fixed-format report, then fixes driven through `schemeweaver-map`. "Why aren't my
  pages getting rich results?"

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
