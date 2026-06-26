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

// TODO(block-context): add pageNodeId/blockInstanceKey here after the backend
// Preview(...) signature change + Orval regen — adding them now would advertise
// params the generated client silently drops.
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

// Surface forward-compatible context fields via .extend() so the generated zod
// object does not strip them once the backend ships them. Both are optional, so
// this is inert today and survives Orval regen.
// NOTE (leader): remove this .extend() shim once regen folds context/
// resolvedBaseUrl into the generated postSchemeweaver...PreviewResponse schema.
const outputSchema = postSchemeweaverMappingsByContentTypeAliasPreviewResponse.extend({
  context: z.string().optional(),
  resolvedBaseUrl: z.string().nullable().optional(),
});

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
    "(base URL actually used).",
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
