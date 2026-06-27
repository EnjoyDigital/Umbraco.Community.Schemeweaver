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
      "(6) verify with preview-json-ld against a real content node and resolve reported validation issues. " +
      "Prefer mapping the properties Google rich results require/recommend (high confidence in ranked results) " +
      "over mapping everything. " +
      "Block List/Grid properties (including blocks nested inside blocks and Block Grid areas) can be mapped to " +
      "nested Schema.org objects: inspect them with get-block-element-types — its propertyInfos[].nestedBlockElementTypes " +
      "surfaces blocks-within-blocks — and route them with the nestable `routes` resolverConfig on a 'blockContent' mapping.",
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
