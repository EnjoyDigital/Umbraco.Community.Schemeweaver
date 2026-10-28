/**
 * Set Up SEO Content Type Tool — step 1+2 of the wizard: pick a kind, get the pre-filled slots.
 */

import { withStandardDecorators, executeGetApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { projectionSchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

const inputSchema = {
  contentTypeAlias: z.string().describe("The Umbraco content type to set up."),
  templateId: z.string().describe("A kind id from get-seo-content-kinds, e.g. 'article', 'product', 'local-business'."),
  schemaTypeName: z
    .string()
    .optional()
    .describe("One of the kind's variants (e.g. 'BlogPosting' for 'article', 'Dentist' for 'local-business'). Omit for the kind's default type."),
};

export const outputSchema = projectionSchema;

const tool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "setup-seo-content-type",
  description:
    "Prepares a content type for a kind without saving anything: runs SchemeWeaver's auto-mapper (heuristic, or the " +
    "TypeSafe/AI satellites when installed) for the kind's Schema.org type and returns every slot with a proposed value — " +
    "from the existing mapping first, then the suggestions (with confidence), then the slot defaults (built-ins such as " +
    "__createDate, 'from site identity' for publisher/organiser/employer when an identity is configured). Rows a kind does " +
    "not own are returned as advancedRows and are preserved by save-seo-mapping. Review the slots (use " +
    "get-content-type-properties to see the fields), fix any 'none' required slot, then persist with save-seo-mapping. " +
    SEO_NOTE,
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: { readOnlyHint: true },
  handler: async ({ contentTypeAlias, templateId, schemaTypeName }) =>
    executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.postSchemeweaverSeoContentTypesByAliasSetup(
        contentTypeAlias,
        { templateId, schemaTypeName: schemaTypeName ?? null },
        CAPTURE_RAW_HTTP_RESPONSE
      )
    ),
};

export default withStandardDecorators(tool);
