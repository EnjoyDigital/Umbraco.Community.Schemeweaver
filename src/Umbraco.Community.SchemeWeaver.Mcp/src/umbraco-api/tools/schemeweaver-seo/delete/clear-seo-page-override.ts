/**
 * Clear SEO Page Override Tool
 */

import { withStandardDecorators, executeVoidApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

const inputSchema = {
  contentKey: z.string().describe("The content node's GUID (key)."),
};

const tool: ToolDefinition<typeof inputSchema> = {
  name: "clear-seo-page-override",
  description:
    "Removes a page's SEO override so it emits exactly what its content type's mapping says again. " +
    SEO_NOTE,
  inputSchema,
  slices: ["delete"],
  annotations: { destructiveHint: true, idempotentHint: true },
  handler: async ({ contentKey }) =>
    executeVoidApiCall<SchemeWeaverApiClient>((client) =>
      client.deleteSchemeweaverSeoPagesByContentKey(contentKey, CAPTURE_RAW_HTTP_RESPONSE)
    ),
};

export default withStandardDecorators(tool);
