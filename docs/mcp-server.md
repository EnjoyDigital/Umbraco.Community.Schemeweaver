# MCP Server

SchemeWeaver ships an optional [MCP (Model Context Protocol)](https://modelcontextprotocol.io)
server that exposes its Schema.org mapping capabilities to AI assistants (Claude, etc.),
built on the official [Umbraco MCP Server SDK](https://docs.umbraco.com/umbraco-in-ai/mcp/base-mcp/sdk).

The point: SchemeWeaver's built-in auto-mapper is a name-matching heuristic. An LLM with
these tools can reason **semantically** about a content type's properties and a Schema.org
type's vocabulary, produce a better mapping, save it, and verify the generated JSON-LD —
end to end.

> **Not part of the NuGet package.** The MCP server is a developer/AI tool that lives in the
> repository (`src/Umbraco.Community.SchemeWeaver.Mcp/`). It is **not** included in the
> `Umbraco.Community.SchemeWeaver` NuGet package and is **not** published to npm. You can either
> **install it as a Claude Code plugin** (below) or run it locally from a clone of the repo. Either
> way it runs against your own Umbraco instance. This is different from the in-product
> [AI satellite](ai-integration.md), which *is* a NuGet package.

## Install as a Claude Code plugin

The quickest way to use the server with [Claude Code](https://claude.com/claude-code) — no clone,
no build. The plugin is served straight from this GitHub repo and bundles a pre-built, self-contained
server, so installation is two commands:

```text
/plugin marketplace add EnjoyDigital/Umbraco.Community.Schemeweaver
/plugin install schemeweaver-mcp@schemeweaver
/reload-plugins
```

On install, Claude Code prompts you for three values — your **Umbraco Base URL**, **API User Client
ID**, and **API User Client Secret** (the secret is stored in your OS keychain). It then starts the
`schemeweaver` MCP server automatically and the tools listed below become available to the assistant.
The plugin **also installs the [`schemeweaver-map` skill](#the-schemeweaver-map-skill)** — no separate step.

You still need an Umbraco instance (v17/18) with SchemeWeaver installed and an **API user** with
client credentials — see [Prerequisites](#prerequisites). For advanced tool filtering (read-only mode,
excluding specific tools), set the `UMBRACO_READONLY` / `UMBRACO_EXCLUDE_TOOLS` environment variables
in your shell before launching Claude Code — see [Tool filtering](#tool-filtering).

## The `schemeweaver-map` skill

Installing the plugin also installs a [skill](https://docs.claude.com/en/docs/claude-code/skills) called
`schemeweaver-map` — a guided workflow that turns the raw tools into a reliable, end-to-end mapping loop,
so you don't have to prompt each step yourself.

**When it runs:** it triggers automatically whenever you ask Claude to map a content/document type to
Schema.org, structured data or JSON-LD, to build or improve a SchemeWeaver mapping, or to get a page
winning Google rich results. You can also invoke it explicitly:

```text
/schemeweaver-mcp:schemeweaver-map
```

**What it does:** drives the tools in the recommended order and loops until the result is clean —
inspect the content type → pick the **most specific** fitting Schema.org type → rank the target's
properties → take the heuristic baseline as a floor → reason **semantically** to draft a richer mapping
(the right `sourceType` for each property: scalar, nested entity, block content, related node…) → save →
`preview-json-ld` + `validate-mapping`, re-saving until `allClear` is true.

**Example prompt:**

> Map the `blogPost` content type to the best-fitting Schema.org type, improve on the heuristic
> suggestions, save it, and keep fixing until the validation passes for Google rich results.

The skill needs the `schemeweaver` MCP server (from this same plugin) connected to your Umbraco
instance — which the plugin sets up for you on install.

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
| `save-schema-mapping` | Create/replace a mapping |
| `delete-schema-mapping` | Remove a mapping |
| `preview-json-ld` | Generate JSON-LD for a content node + Rich Results validation issues (backoffice-context preview) |
| `get-rendered-json-ld` | Fetch the **live** JSON-LD a published page emits, from the anonymous Delivery API (ground truth) |
| `validate-mapping` | Severity-ranked "doctor" checklist for a mapping (`critical` > `warning` > `suggestion` > `info`) |
| `generate-content-type` | Scaffold a new document type from a Schema.org type |
| `export-mappings-to-usync` | Export mappings to uSync files |
| `get-usync-drift` | Report drift between live mappings and uSync files |
| `get-server-info` | Server version/runtime (auth smoke test, from the base SDK) |

## Prerequisites

- Node.js >= 22
- A running Umbraco instance (v17/18) with SchemeWeaver installed — the repo's TestHost works out of the box
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
