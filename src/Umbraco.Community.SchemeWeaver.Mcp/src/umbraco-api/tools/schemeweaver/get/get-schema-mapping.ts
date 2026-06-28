/**
 * Get Schema Mapping Tool
 */

import {
  withStandardDecorators,
  executeGetApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import {
  getSchemeweaverMappingsByContentTypeAliasParams,
  getSchemeweaverMappingsByContentTypeAliasResponse,
} from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = getSchemeweaverMappingsByContentTypeAliasParams.shape;

// Forward-compatible: the backend now enriches a single read with `reachability`
// (routed-page | composed-from-block | unknown) and structural `warnings` (a
// property mapped outside its Schema.org range, which would be silently dropped
// at generation time). Surface them via .extend() so the generated zod object
// does not strip them before the Orval client is regenerated. Both are optional
// → inert when absent.
// NOTE (leader): drop this shim once `npm run generate` folds the fields into the
// generated getSchemeweaverMappingsByContentTypeAliasResponse schema.
const outputSchema = getSchemeweaverMappingsByContentTypeAliasResponse.extend({
  reachability: z.string().optional(),
  // Disk/DB drift vs the mapping's uSync .config: in-sync | db-only | disk-only |
  // content-differs | usync-unavailable (when the uSync addon isn't installed).
  driftStatus: z.string().optional(),
  warnings: z
    .array(
      z.object({
        severity: z.string(),
        schemaType: z.string().nullish(),
        path: z.string().nullish(),
        message: z.string(),
      })
    )
    .optional(),
});

const getSchemaMappingTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-schema-mapping",
  description:
    "Gets the SchemeWeaver mapping for one Umbraco content type (404 if none exists). " +
    "Returns the mapped Schema.org type, enabled/inherited flags, optional @id override template and the full list of property mappings. " +
    "Also returns `reachability` (routed-page emits on its own URL; composed-from-block only emits inside a containing page's block " +
    "mapping) and `warnings` (properties mapped outside their Schema.org range that would be silently dropped from the JSON-LD). " +
    "Call this before save-schema-mapping when changing an existing mapping, so unchanged property mappings are preserved — " +
    "saving replaces the whole mapping, it does not merge.",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ contentTypeAlias }) => {
    return executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverMappingsByContentTypeAlias(contentTypeAlias, CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(getSchemaMappingTool);
