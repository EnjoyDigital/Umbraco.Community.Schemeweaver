/**
 * Preview JSON-LD Tool
 */

import {
  withStandardDecorators,
  executeGetApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { postSchemeweaverMappingsByContentTypeAliasPreviewResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  contentTypeAlias: z.string().describe("Umbraco content type alias whose mapping should be previewed"),
  contentKey: z
    .string()
    .uuid()
    .optional()
    .describe(
      "GUID key of a published content node of this type. When provided, real JSON-LD is generated from that node's values; " +
        "when omitted, a mock preview with placeholder values is returned."
    ),
  culture: z
    .string()
    .optional()
    .describe("Optional culture code (e.g. 'en-US', 'de-DE') for language-variant content"),
};

const outputSchema = postSchemeweaverMappingsByContentTypeAliasPreviewResponse;

const previewJsonLdTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "preview-json-ld",
  description:
    "Generates the JSON-LD a content node would emit with the saved mapping, plus Rich Results validation. " +
    "This is the feedback loop after save-schema-mapping: check isValid and the issues array " +
    "(each issue has severity, schemaType, path and message — e.g. missing required/recommended properties for Google rich results) " +
    "and refine the mapping until the output is clean. Pass a contentKey of a real published node for a realistic preview; " +
    "without one you get placeholder values that only prove the structure.",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ contentTypeAlias, contentKey, culture }) => {
    return executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.postSchemeweaverMappingsByContentTypeAliasPreview(
        contentTypeAlias,
        { contentKey, culture },
        CAPTURE_RAW_HTTP_RESPONSE
      )
    );
  },
};

export default withStandardDecorators(previewJsonLdTool);
