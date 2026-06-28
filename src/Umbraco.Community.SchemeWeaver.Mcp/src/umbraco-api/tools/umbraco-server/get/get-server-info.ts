/**
 * Get Server Information Tool
 *
 * Calls the real Umbraco Management API to fetch server version and runtime info.
 * Uses the custom transport layer (fetch with Bearer token) that's set up by
 * the hosted MCP server during per-request initialization.
 *
 * This tool proves the full authentication chain works:
 * OAuth flow → Umbraco token → Management API call → response.
 */

import {
  withStandardDecorators,
  createToolResult,
  UmbracoManagementClient,
  CAPTURE_RAW_HTTP_RESPONSE,
  type ToolDefinition,
  type HttpResponse,
} from "@umbraco-cms/mcp-server-sdk";
import { resolveBaseUrl } from "../../../base-url.js";

interface ServerInfo {
  version: string;
  assemblyVersion: string;
}

interface ServerContext {
  hasPublishedContent: boolean;
  isTestHost: boolean;
}

const getServerInfoTool: ToolDefinition = {
  name: "get-server-info",
  description:
    "Gets Umbraco server information including version and runtime details, " +
    "the configured Umbraco base URL that {siteUrl}/absolute-URL tokens derive from, and — when SchemeWeaver is " +
    "installed — whether the target actually has published content (`hasPublishedContent`) and whether it is the " +
    "SchemeWeaver TestHost sandbox (`isTestHost`). Check these before trusting a render: a sandbox/TestHost has the " +
    "content model but may have no published tree, so preview/render can't reflect real pages.",
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async () => {
    // Call the real Umbraco Management API endpoint.
    // UmbracoManagementClient routes through the custom transport (fetch with
    // the user's Bearer token) that was configured during server init.
    // CAPTURE_RAW_HTTP_RESPONSE returns HttpResponse<T> at runtime,
    // but the generic signature returns T — cast to access .data.
    const response = await UmbracoManagementClient<ServerInfo>(
      { url: "/umbraco/management/api/v1/server/information", method: "GET" },
      CAPTURE_RAW_HTTP_RESPONSE,
    ) as unknown as HttpResponse<ServerInfo>;

    // SchemeWeaver-specific context (sandbox vs populated). Best-effort: absent when
    // SchemeWeaver isn't installed (404) — don't let it break the core server info.
    let serverContext: ServerContext | undefined;
    try {
      const ctx = await UmbracoManagementClient<ServerContext>(
        { url: "/umbraco/management/api/v1/schemeweaver/server-context", method: "GET" },
        CAPTURE_RAW_HTTP_RESPONSE,
      ) as unknown as HttpResponse<ServerContext>;
      serverContext = ctx.data;
    } catch {
      serverContext = undefined;
    }

    return createToolResult({
      ...response.data,
      configuredBaseUrl: resolveBaseUrl(),
      hasPublishedContent: serverContext?.hasPublishedContent,
      isTestHost: serverContext?.isTestHost,
    });
  },
};

export default withStandardDecorators(getServerInfoTool);
