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
  blockInstanceKey: z
    .string()
    .uuid()
    .optional()
    .describe(
      "GUID key of a single nested block element inside the page identified by contentKey. When set, renders the REAL " +
        "JSON-LD that one block instance contributes to the page (via the page mapping's route for that block type) — use " +
        "this to see real nested values (e.g. a Question's name/answer) instead of the page-level placeholder. Requires contentKey."
    ),
  culture: z
    .string()
    .optional()
    .describe("Optional culture code (e.g. 'en-US', 'de-DE') for language-variant content"),
};

// The generated schema (regenerated from the C# DTO) already carries context and
// resolvedBaseUrl, so no shim is needed — use it directly.
export const outputSchema = postSchemeweaverMappingsByContentTypeAliasPreviewResponse;

const previewJsonLdTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "preview-json-ld",
  description:
    "Generates the JSON-LD a content node would emit with the saved mapping, plus Rich Results validation. " +
    "This is the feedback loop after save-schema-mapping: check isValid and the issues array " +
    "(each issue has severity, schemaType, path and message — e.g. missing required/recommended properties for Google rich results) " +
    "and refine the mapping until the output is clean. Pass a contentKey of a real published node for a realistic preview; " +
    "without one you get placeholder values that only prove the structure. " +
    "This is a BACKOFFICE-CONTEXT preview: URL/@id resolution can differ from the live render because the " +
    "resolved base URL is the management host, not the public site. isValid here reflects backoffice-context " +
    "structural validity ONLY — it does NOT imply the live structured data is valid. For authoritative live " +
    "output use get-rendered-json-ld. The response reports context ('backoffice-preview') and resolvedBaseUrl " +
    "(base URL actually used). " +
    "To preview a single nested block in isolation, pass contentKey (the page) AND blockInstanceKey (the block's " +
    "GUID Key) — the response renders that block's real values and an info issue naming the page node it resolved from.",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ contentTypeAlias, contentKey, blockInstanceKey, culture }) => {
    return executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.postSchemeweaverMappingsByContentTypeAliasPreview(
        contentTypeAlias,
        { contentKey, blockInstanceKey, culture },
        CAPTURE_RAW_HTTP_RESPONSE
      )
    );
  },
};

export default withStandardDecorators(previewJsonLdTool);
