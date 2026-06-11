/**
 * Suggest Property Mappings (Heuristic) Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { postSchemeweaverMappingsByContentTypeAliasAutoMapResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  contentTypeAlias: z.string().describe("Umbraco content type alias to suggest mappings for"),
  schemaTypeName: z.string().describe("Schema.org type name to map to, e.g. 'Article'"),
};

const outputSchema = z.object({
  items: postSchemeweaverMappingsByContentTypeAliasAutoMapResponse,
});

const suggestPropertyMappingsTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "suggest-property-mappings",
  description:
    "Runs SchemeWeaver's built-in heuristic auto-mapper (exact/synonym/substring name matching) and returns property mapping " +
    "suggestions with a confidence score per schema property (0-100; >=80 high, >=50 medium). " +
    "Treat this as a BASELINE, not the answer: review each suggestion semantically, drop bad matches, fill in the gaps " +
    "the heuristic missed (it cannot reason about meaning, units, nested objects or content structure), " +
    "then build the final mapping yourself and persist it with save-schema-mapping. " +
    "Suggestions with isAutoMapped=false are unmapped schema properties listed for completeness — good candidates for manual reasoning.",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
    idempotentHint: true,
  },
  handler: async ({ contentTypeAlias, schemaTypeName }) => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.postSchemeweaverMappingsByContentTypeAliasAutoMap(
        contentTypeAlias,
        { schemaTypeName },
        CAPTURE_RAW_HTTP_RESPONSE
      )
    );
  },
};

export default withStandardDecorators(suggestPropertyMappingsTool);
