/**
 * Set SEO Page Override Tool — retype or exclude a single page (the Schema tab's controls).
 */

import { withStandardDecorators, executeGetApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { pageOverrideSchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

const inputSchema = {
  contentKey: z.string().describe("The content node's GUID (key)."),
  schemaTypeName: z
    .string()
    .nullish()
    .describe(
      "A subtype to emit for this page instead of the type its content type is mapped to — one of the kind's variants " +
        "(allowedPageTypes in get-seo-page-summary), e.g. 'NewsArticle' on an Article page. Null keeps the mapped type."
    ),
  isExcluded: z
    .boolean()
    .default(false)
    .describe("True to emit no main entity for this page (WebSite and breadcrumbs still render)."),
};

export const outputSchema = pageOverrideSchema;

const tool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "set-seo-page-override",
  description:
    "Stores a per-page override: swap the page's emitted Schema.org type for a subtype of its mapping (the property rows " +
    "stay the same; the default @id follows the new type) and/or exclude the page from structured data. Overrides are " +
    "invariant (one per node), take effect immediately without a publish, and are removed with clear-seo-page-override " +
    "or when the content is deleted. A null type with isExcluded=false clears any stored override. 404 when the page's " +
    "content type is not set up; 400 when the type is not an allowed page type. " +
    SEO_NOTE,
  inputSchema,
  outputSchema,
  slices: ["update"],
  annotations: { idempotentHint: true },
  handler: async ({ contentKey, schemaTypeName, isExcluded }) =>
    executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.putSchemeweaverSeoPagesByContentKey(
        contentKey,
        { schemaTypeName: schemaTypeName ?? null, isExcluded: isExcluded ?? false },
        CAPTURE_RAW_HTTP_RESPONSE
      )
    ),
};

export default withStandardDecorators(tool);
