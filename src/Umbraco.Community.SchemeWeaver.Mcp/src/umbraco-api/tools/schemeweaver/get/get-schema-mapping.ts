/**
 * Get Schema Mapping Tool
 */

import {
  withStandardDecorators,
  executeGetApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import {
  getSchemeweaverMappingsByContentTypeAliasParams,
  getSchemeweaverMappingsByContentTypeAliasResponse,
} from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = getSchemeweaverMappingsByContentTypeAliasParams.shape;
const outputSchema = getSchemeweaverMappingsByContentTypeAliasResponse;

const getSchemaMappingTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-schema-mapping",
  description:
    "Gets the SchemeWeaver mapping for one Umbraco content type (404 if none exists). " +
    "Returns the mapped Schema.org type, enabled/inherited flags, optional @id override template and the full list of property mappings. " +
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
