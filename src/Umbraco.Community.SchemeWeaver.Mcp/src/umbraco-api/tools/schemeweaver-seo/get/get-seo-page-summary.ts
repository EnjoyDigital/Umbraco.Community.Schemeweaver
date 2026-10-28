/**
 * Get SEO Page Summary Tool — what one page emits, as the Schema tab shows it.
 */

import { withStandardDecorators, executeGetApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { pageSummarySchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

const inputSchema = {
  contentKey: z.string().describe("The content node's GUID (key)."),
  culture: z.string().optional().describe("Culture for variant content, e.g. 'en-GB'. Omit for invariant."),
};

export const outputSchema = pageSummarySchema;

const tool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-seo-page-summary",
  description:
    "Summarises one content node for the SEO edition: whether its type is set up, the kind, the default and effective " +
    "Schema.org types (after any per-page override), the page types an editor may switch to, the stored override, whether " +
    "the page is excluded or unpublished, its public URL, and — when published and not excluded — the core preview " +
    "(JSON-LD plus Rich Results validation issues). Use get-rendered-json-ld for the live Delivery API output. " +
    SEO_NOTE,
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: { readOnlyHint: true },
  handler: async ({ contentKey, culture }) =>
    executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverSeoPagesByContentKeySummary(contentKey, culture ? { culture } : undefined, CAPTURE_RAW_HTTP_RESPONSE)
    ),
};

export default withStandardDecorators(tool);
