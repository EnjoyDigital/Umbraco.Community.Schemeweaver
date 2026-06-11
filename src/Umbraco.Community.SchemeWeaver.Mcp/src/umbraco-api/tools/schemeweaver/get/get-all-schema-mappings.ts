/**
 * Get All Schema Mappings Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { getSchemeweaverMappingsResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const outputSchema = z.object({ items: getSchemeweaverMappingsResponse });

const getAllSchemaMappingsTool: ToolDefinition<undefined, typeof outputSchema> = {
  name: "get-all-schema-mappings",
  description:
    "Lists every SchemeWeaver mapping in the site: which Umbraco content type maps to which Schema.org type, " +
    "whether it is enabled and inherited by descendant content, and all of its property mappings. " +
    "Useful for auditing existing structured-data coverage and for copying patterns from mappings that already work.",
  outputSchema,
  slices: ["list"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async () => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverMappings(CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(getAllSchemaMappingsTool);
