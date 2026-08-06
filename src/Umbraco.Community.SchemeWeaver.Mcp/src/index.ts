#!/usr/bin/env node
/**
 * SchemeWeaver MCP Server Entry Point
 *
 * Exposes SchemeWeaver's Schema.org mapping capabilities (via the Umbraco
 * management API) as MCP tools, so AI assistants can reason about the best
 * mappings semantically rather than relying on the heuristic auto-mapper alone.
 */

import "dotenv/config";
import { McpServer, type ToolCallback } from "@modelcontextprotocol/sdk/server/mcp.js";
import packageJson from "../package.json" with { type: "json" };
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  configureApiClient,
  initializeUmbracoFetch,
  createToolAnnotations,
  createCollectionConfigLoader,
  shouldIncludeTool,
  handleCliCommands,
  type CollectionConfiguration,
} from "@umbraco-cms/mcp-server-sdk";

// Import the Orval-generated API client
import { getSchemeWeaverManagementAPI } from "./umbraco-api/api/generated/schemeWeaverApi.js";

// Import tool collections
import schemeWeaverCollection from "./umbraco-api/tools/schemeweaver/index.js";
import umbracoServerCollection from "./umbraco-api/tools/umbraco-server/index.js";

// Import registries for tool filtering
import { allModes, allModeNames, allSliceNames, loadServerConfig, clearConfigCache } from "./config/index.js";

// Shared base-URL resolution (single source of truth across tools)
import { resolveBaseUrl } from "./umbraco-api/base-url.js";

const SERVER_NAME = "schemeweaver-mcp";

// Initialize the SDK's fetch client for real Umbraco API calls.
// This enables the Orval-generated client to authenticate via client_credentials.
const baseUrl = resolveBaseUrl();
const clientId = process.env.UMBRACO_CLIENT_ID || "";
const clientSecret = process.env.UMBRACO_CLIENT_SECRET || "";
if (clientId) {
  initializeUmbracoFetch({ baseUrl, clientId, clientSecret });
}

// Configure the API client for use with toolkit helpers
// (connects the generated Orval client to executeGetApiCall, executeVoidApiCall, etc.)
configureApiClient(() => getSchemeWeaverManagementAPI());

// ============================================================================
// MCP Server Setup
// ============================================================================

const server = new McpServer(
  {
    name: SERVER_NAME,
    version: packageJson.version,
  },
  {
    instructions:
      "SchemeWeaver maps Umbraco content types to Schema.org types and emits JSON-LD structured data. " +
      "When asked to map a content type: (1) inspect it with get-content-type-properties, " +
      "(2) choose the most specific fitting Schema.org type via search-schema-types, " +
      "(3) review its properties with get-schema-type-properties ranked=true, " +
      "(4) get the heuristic baseline from suggest-property-mappings but improve on it semantically — " +
      "the heuristic only matches names, it cannot reason about meaning, " +
      "(5) persist with save-schema-mapping (it replaces the whole mapping), " +
      "(6) verify with preview-json-ld against a real content node, then run validate-mapping and LOOP " +
      "fixing+re-saving until allClear (no critical/warning/suggestion items). " +
      "Prefer mapping the properties Google rich results require/recommend (high confidence in ranked results) " +
      "over mapping everything; pick the MOST SPECIFIC fitting type (BlogPosting over Article, Recipe over CreativeWork). " +
      "Reason semantically (strapline->alternativeName, standfirst->description, heroImage->image). " +
      "SOURCE TYPES — choosing the right one is how you beat the name-only heuristic: " +
      "'property' (a scalar/media on this node — the DEFAULT, and the SIMPLEST valid choice: do NOT wrap a lone scalar " +
      "like Vehicle.brand or JobPosting.baseSalary in a Brand/QuantitativeValue object); " +
      "'static' (a fixed literal); " +
      "'complexType' (the schema prop denotes a named ENTITY — Person/Organization/Place/Offer — nest it even from a " +
      "single field, e.g. BlogPosting.author from one authorName text prop); " +
      "'blockContent' (a Block List/Grid: extractAs stringList for one-field label blocks like recipeIngredient, " +
      "nestedMappings for multi-field blocks, or the nestable `routes` for blocks-nested-in-blocks and Block Grid areas — " +
      "always map a body-sections container like contentGrid/blocks/sections to mainEntity or hasPart as WebPageElement, never leave it unmapped); " +
      "'parent'/'ancestor'/'sibling' (a related node up/around the tree — e.g. a 'category' grouping from the parent's title; " +
      "ALSO valid on complexTypeMappings sub-rows, so an inline Organization's name/logo can read the site root — those " +
      "sub-rows are page-relative, except inside a pickedComplexType where they walk from the PICKED node); " +
      "'reference' (a shared graph piece by key, e.g. organization). " +
      "Content pickers (ContentPicker/MNTP) under 'property' render the picked node(s) by precedence: resolverConfig " +
      "{\"pickedPropertyAlias\":\"...\"} drills ONE property off the picked node; else resolverConfig " +
      "{\"pickedComplexType\":{\"selectedSubType\":\"Person\",\"complexTypeMappings\":[…]}} (plus a matching nestedSchemaTypeName) " +
      "builds a per-usage inline object whose sub-rows read the PICKED node — the picked type needs no mapping of its own, so " +
      "the same type can be shaped differently per page; else nestedSchemaTypeName alone renders the whole node via the picked " +
      "type's OWN mapping; else the node's name. An MNTP fans several picks out to an array. A picker used as a complexType " +
      "SUB-ROW takes the same config in the SUB-ROW's own resolverConfig ({\"pickedPropertyAlias\":\"...\"} to drill, or " +
      "{\"nestedSchemaTypeName\":\"...\"} for the whole node) — without it the sub-row emits only the picked node's name. " +
      "Built-ins always available as 'property': __name, __url, __createDate, __updateDate. " +
      "Inspect blocks with get-block-element-types — its propertyInfos[].nestedBlockElementTypes surfaces blocks-within-blocks. " +
      "The schemeweaver-map skill carries the full source-type catalogue and worked examples; " +
      "schemeweaver-setup covers connection/auth problems and schemeweaver-audit drives a site-wide coverage audit.",
  }
);

// ============================================================================
// Tool Filtering Setup
// ============================================================================

// Clear config cache to ensure fresh config for each server start
clearConfigCache();

// Load server configuration (includes filtering settings from env vars)
const serverConfig = await loadServerConfig(true);

// Create collection config loader with our registries
const configLoader = createCollectionConfigLoader({
  modeRegistry: allModes,
  allModeNames,
  allSliceNames,
});

// Load filtering configuration from server config
const filterConfig: CollectionConfiguration = configLoader.loadFromConfig(serverConfig.umbraco);

// ============================================================================
// CLI Introspection (runs before server start, exits immediately)
// ============================================================================

const collections = [schemeWeaverCollection, umbracoServerCollection];

// handleCliCommands checks --list-tools, --describe-tool, --generate-context, --call.
// If any flag is set it prints output and calls process.exit(0).
// Otherwise it returns and the server continues to start.
await handleCliCommands(collections, {
  cliFlags: serverConfig.cliFlags,
  serverName: SERVER_NAME,
  serverVersion: packageJson.version,
  filterConfig,
  serverConfig: serverConfig.umbraco,
});

// ============================================================================
// Register Tools with Filtering
// ============================================================================

let registeredToolCount = 0;

for (const collection of collections) {
  const collectionName = collection.metadata.name;

  // Get tools for current user (pass user context if needed)
  const tools = collection.tools({});

  for (const tool of tools) {
    // Check if tool should be included based on filtering config
    if (!shouldIncludeTool(tool, { collectionName, config: filterConfig })) {
      continue;
    }

    // Build annotations from tool definition
    const annotations = createToolAnnotations(tool);

    // Register tool with MCP server using registerTool API
    server.registerTool(tool.name, {
      description: tool.description,
      inputSchema: tool.inputSchema,
      outputSchema: tool.outputSchema,
      annotations,
    }, tool.handler as ToolCallback<typeof tool.inputSchema>);

    registeredToolCount++;
  }
}

// Start the server
async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error(
    `${SERVER_NAME} started with ${registeredToolCount} tool(s) from ${collections.length} collection(s)`
  );
}

main().catch((error) => {
  console.error(`Failed to start ${SERVER_NAME}:`, error);
  process.exit(1);
});
