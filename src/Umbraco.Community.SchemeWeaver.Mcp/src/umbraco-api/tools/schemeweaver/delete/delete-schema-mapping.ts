/**
 * Delete Schema Mapping Tool
 */

import {
  withStandardDecorators,
  executeVoidApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  contentTypeAlias: z.string().describe("Umbraco content type alias whose mapping should be deleted"),
};

const deleteSchemaMappingTool: ToolDefinition<typeof inputSchema> = {
  name: "delete-schema-mapping",
  description:
    "Permanently deletes the SchemeWeaver mapping for a content type, including all of its property mappings. " +
    "Content of this type will stop emitting JSON-LD. To temporarily switch a mapping off instead, " +
    "save it with isEnabled=false rather than deleting.",
  inputSchema,
  slices: ["delete"],
  annotations: {
    destructiveHint: true,
    idempotentHint: true,
  },
  handler: async ({ contentTypeAlias }) => {
    return executeVoidApiCall<SchemeWeaverApiClient>((client) =>
      client.deleteSchemeweaverMappingsByContentTypeAlias(contentTypeAlias, CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(deleteSchemaMappingTool);
