# MCP Server

SchemeWeaver ships an optional [MCP (Model Context Protocol)](https://modelcontextprotocol.io)
server that exposes its Schema.org mapping capabilities to AI assistants (Claude, etc.),
built on the official [Umbraco MCP Server SDK](https://docs.umbraco.com/umbraco-in-ai/mcp/base-mcp/sdk).

The point: SchemeWeaver's built-in auto-mapper is a name-matching heuristic. An LLM with
these tools can reason **semantically** about a content type's properties and a Schema.org
type's vocabulary, produce a better mapping, save it, and verify the generated JSON-LD,
end to end.

> **Not part of the NuGet package.** The MCP server is a developer/AI tool that lives in the
> repository (`src/Umbraco.Community.SchemeWeaver.Mcp/`). It is **not** included in the
> `Umbraco.Community.SchemeWeaver` NuGet package and is **not** published to npm. You can either
> **install it as a Claude Code plugin** (below) or run it locally from a clone of the repo. Either
> way it runs against your own Umbraco instance. This is different from the in-product
> [AI satellite](ai-integration.md), which *is* a NuGet package.

## Install as a Claude Code plugin

The quickest way to use the server with [Claude Code](https://claude.com/claude-code): no clone,
no build. The plugin is served straight from this GitHub repo and bundles a pre-built, self-contained
server, so installation is three commands:

```text
/plugin marketplace add EnjoyDigital/Umbraco.Community.Schemeweaver
/plugin install schemeweaver-mcp@schemeweaver
/reload-plugins
```

On install, Claude Code prompts you for three values: your **Umbraco Base URL**, **API User Client
ID**, and **API User Client Secret** (the secret is stored in your OS keychain). It then starts the
`schemeweaver` MCP server automatically and the tools listed below become available to the assistant.
The plugin **also installs the [three bundled skills](#bundled-skills)**; there is no separate step.

You still need an Umbraco instance (v17/18) with SchemeWeaver installed and an **API user** with
client credentials; see [Prerequisites](#prerequisites). For advanced tool filtering (read-only mode,
excluding specific tools), set the `UMBRACO_READONLY` / `UMBRACO_EXCLUDE_TOOLS` environment variables
in your shell before launching Claude Code; see [Tool filtering](#tool-filtering).

## Bundled skills

Installing the plugin also installs three [skills](https://docs.claude.com/en/docs/claude-code/skills):
guided workflows that turn the raw tools into reliable, end-to-end loops, so you don't have to prompt
each step yourself. Each triggers automatically from plain language, or explicitly by its slug.

### `schemeweaver-setup`: connect and verify

Gets Claude Code talking to your Umbraco site and **proves it**: creating the API user (and the
client-credential gotchas), configuring the plugin, then a verification ladder from `get-server-info`
to `list-content-types`, with a troubleshooting table for the classic failures (401s, an HTML login
page instead of JSON, certificate errors). It also triggers when any schemeweaver tool misbehaves.

> Connect Claude to my Umbraco site at https://www.example.com and check it works.

Explicit invocation: `/schemeweaver-mcp:schemeweaver-setup`

### `schemeweaver-map`: the mapping loop

Drives the tools in the recommended order for ONE content type and loops until the result is clean:
inspect the content type → pick the **most specific** fitting Schema.org type → rank the target's
properties → take the heuristic baseline as a floor → reason **semantically** to draft a richer mapping
(the right `sourceType` for each property: scalar, nested entity, block content, related node…) → save →
`preview-json-ld` + `validate-mapping`, re-saving until `allClear` is true.

> Map the `blogPost` content type to the best-fitting Schema.org type, improve on the heuristic
> suggestions, save it, and keep fixing until the validation passes for Google rich results.

Explicit invocation: `/schemeweaver-mcp:schemeweaver-map`

### `schemeweaver-audit`: site-wide coverage audit

Surveys the whole site rather than one type: inventories every mapping and unmapped content type,
triages the gaps by Google rich-result value, validates every existing mapping, spot-checks the live
JSON-LD output, checks uSync drift, and delivers a fixed-format report with a prioritised action plan,
then executes the agreed fixes one type at a time through `schemeweaver-map`.

> Audit my site's structured data: why aren't my pages getting rich results?

Explicit invocation: `/schemeweaver-mcp:schemeweaver-audit`

The skills need the `schemeweaver` MCP server (from this same plugin) connected to your Umbraco
instance, which the plugin sets up for you on install.

## What it exposes

Sixteen SchemeWeaver tools, plus a `get-server-info` smoke-test tool from the base SDK (17 in total):

| Tool | Purpose |
|---|---|
| `search-schema-types` | Search the Schema.org vocabulary |
| `get-schema-type-properties` | Properties of a type, optionally ranked by real-world importance |
| `list-content-types` | All Umbraco content types with aliases and keys |
| `get-content-type-properties` | Properties + editor aliases of a content type (incl. built-ins `__name`, `__url`, …) |
| `get-block-element-types` | Element types inside a Block List/Grid property, recursing into blocks nested in blocks / Block Grid areas |
| `get-all-schema-mappings` / `get-schema-mapping` | Existing mappings |
| `suggest-property-mappings` | The heuristic auto-mapper baseline (confidence 0–100) |
| `save-schema-mapping` | Create or replace a mapping; a save replaces the whole mapping, and supports the full source-type set, including `reference` rows and nestable `blockContent` routes |
| `delete-schema-mapping` | Remove a mapping |
| `preview-json-ld` | Generate JSON-LD for a content node + Rich Results validation issues (backoffice-context preview) |
| `get-rendered-json-ld` | Fetch the **live** JSON-LD a published page emits, from the anonymous Delivery API (ground truth) |
| `validate-mapping` | Severity-ranked "doctor" checklist for a mapping (`critical` > `warning` > `suggestion` > `info`) |
| `generate-content-type` | Scaffold a new document type from a Schema.org type |
| `export-mappings-to-usync` | Export mappings to uSync files |
| `get-usync-drift` | Report drift between live mappings and uSync files |
| `get-server-info` | Server version/runtime (auth smoke test, from the base SDK) |

### Preview vs live render

`preview-json-ld` renders from the saved mapping without publishing anything, in the backoffice management context; its URL and `@id` resolution reflect the management host, so treat it as a fast working preview rather than proof of the live output. `get-rendered-json-ld` fetches the JSON-LD a published page actually emits via the anonymous Delivery API, which is the ground truth (the Delivery API must be enabled in the Umbraco instance).

**Self-signed TLS:** `get-rendered-json-ld` uses a plain `fetch`, so a local HTTPS instance needs either a trusted development certificate or `NODE_TLS_REJECT_UNAUTHORIZED=0` set in the server's environment.

## Prerequisites

- Node.js >= 22
- A running Umbraco instance (v17/18) with SchemeWeaver installed: the repo's TestHost works out of the box
- An **API user** in that instance with client credentials. For the TestHost,
  `scripts/setup-api-user.mjs` creates one automatically:
  ```bash
  node --env-file=.env scripts/setup-api-user.mjs <admin-email> <admin-password>
  ```

## Setup

From `src/Umbraco.Community.SchemeWeaver.Mcp/`:

```bash
npm install
cp .env.example .env   # fill in UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET / UMBRACO_BASE_URL
npm run generate       # extract the OpenAPI subset from the running instance + regenerate the typed client
npm run build          # build dist/
```

The repo's `.mcp.json` registers the server for Claude Code automatically (`schemeweaver-mcp`).
To run or inspect it manually:

```bash
node --env-file=.env dist/index.js                 # start the server
node --env-file=.env dist/index.js --list-tools    # introspect without starting
npm run inspect                                     # MCP Inspector
```

## Tool filtering

Standard SDK filtering applies via env vars: `UMBRACO_READONLY=true` disables the write tools;
`UMBRACO_EXCLUDE_TOOLS=delete-schema-mapping,generate-content-type` removes specific ones;
`UMBRACO_INCLUDE_SLICES=read,list,search` keeps only reads. See `.env.example`.

## Example prompt

> Map the `blogPost` content type to the best-fitting Schema.org type using the SchemeWeaver
> tools. Improve on the heuristic suggestions, save the mapping, and verify the preview
> validates for Google rich results.

For developer detail (architecture, regenerating the API client, testing), see the MCP
project's own [README](../src/Umbraco.Community.SchemeWeaver.Mcp/README.md).
