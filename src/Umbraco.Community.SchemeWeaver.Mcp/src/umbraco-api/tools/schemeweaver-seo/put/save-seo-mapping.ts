/**
 * Save SEO Mapping Tool — step 3 of the wizard: persist the slots as an ordinary mapping.
 */

import { withStandardDecorators, executeGetApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { contentTypeStatusSchema, slotValueSchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

const inputSchema = {
  contentTypeAlias: z.string().describe("The Umbraco content type to save."),
  templateId: z.string().describe("The kind id (from get-seo-content-kinds / setup-seo-content-type)."),
  schemaTypeName: z.string().optional().describe("The kind's default type or one of its variants; omit for the default."),
  slots: z
    .record(z.string(), slotValueSchema)
    .describe(
      "Slot values keyed by the slot's schemaProperty (camelCase, e.g. 'headline'). Start from the `slots` returned by " +
        "setup-seo-content-type (each entry's `value`), change what you must, and pass them all back — a slot omitted here " +
        "is saved as empty. Entity slots take kind 'nested' with `nested` = { subProperty: value } (one more level is allowed, " +
        "e.g. jobLocation.address → PostalAddress), kind 'property' with a content-picker field, or kind 'property' with a text " +
        "field (saved as the entity's name). Keep kinds 'blocks'/'advanced' exactly as returned."
    ),
};

export const outputSchema = contentTypeStatusSchema;

const tool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "save-seo-mapping",
  description:
    "Saves a content type's kind and slot values as an ordinary SchemeWeaver mapping (the same rows the developer tools " +
    "and get-schema-mapping see). Slot values become property-mapping rows server-side; rows the kind does not own " +
    "(developer additions) are preserved; a developer-disabled mapping stays disabled. Returns the content type's new " +
    "status row (required/recommended counts, missing required slots). Verify with get-seo-page-summary on a published " +
    "page or run-seo-health-check. " +
    SEO_NOTE,
  inputSchema,
  outputSchema,
  slices: ["create", "update"],
  annotations: { idempotentHint: true },
  handler: async ({ contentTypeAlias, templateId, schemaTypeName, slots }) =>
    executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.putSchemeweaverSeoContentTypesByAliasMapping(
        contentTypeAlias,
        { templateId, schemaTypeName: schemaTypeName ?? null, slots: slots as any },
        CAPTURE_RAW_HTTP_RESPONSE
      )
    ),
};

export default withStandardDecorators(tool);
