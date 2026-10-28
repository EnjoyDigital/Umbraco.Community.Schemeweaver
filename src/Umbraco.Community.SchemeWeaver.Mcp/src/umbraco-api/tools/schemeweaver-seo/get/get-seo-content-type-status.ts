/**
 * Get SEO Content Type Status Tool — the Content types view, as data.
 */

import {
  withStandardDecorators,
  executeGetApiCall,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { contentTypeStatusSchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

const inputSchema = {
  contentTypeAlias: z
    .string()
    .optional()
    .describe("Omit to list every content type; pass an alias to get just that one (404 when it does not exist)."),
};

export const outputSchema = z.object({ items: z.array(contentTypeStatusSchema) });

const tool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-seo-content-type-status",
  description:
    "Reports every Umbraco content type through the SEO edition's eyes: status (not-set-up, needs-attention, set-up, " +
    "developer-managed), the kind and Schema.org type it is set up as, how many required/recommended slots are filled, " +
    "which required slots are missing, how many developer-managed advanced rows it carries, whether it is an element " +
    "type or has a template, and its published page count. Start a site-wide SEO review here: not-set-up page types " +
    "with published pages are the quickest wins; needs-attention rows name the missing required fields. " +
    SEO_NOTE,
  inputSchema,
  outputSchema,
  slices: ["read", "list"],
  annotations: { readOnlyHint: true },
  handler: async ({ contentTypeAlias }) => {
    if (contentTypeAlias) {
      const result = await executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
        client.getSchemeweaverSeoContentTypesByAlias(contentTypeAlias, CAPTURE_RAW_HTTP_RESPONSE)
      );
      if (result.isError || !result.structuredContent) return result;
      return { ...result, structuredContent: { items: [result.structuredContent] } };
    }
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverSeoContentTypes(CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(tool);
