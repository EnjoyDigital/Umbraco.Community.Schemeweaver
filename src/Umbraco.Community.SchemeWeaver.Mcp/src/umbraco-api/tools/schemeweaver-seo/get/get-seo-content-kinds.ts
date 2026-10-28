/**
 * Get SEO Content Kinds Tool — the curated catalogue the SEO edition offers.
 */

import { withStandardDecorators, executeGetApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { templateSchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

export const outputSchema = z
  .object({
    templates: z.array(templateSchema),
    retired: z.array(z.object({ schemaType: z.string(), name: z.string(), retiredOn: z.string(), docUrl: z.string() }).passthrough()),
    businessTypes: z.array(z.object({ schemaType: z.string(), label: z.string() })),
  })
  .passthrough();

const tool: ToolDefinition<undefined, typeof outputSchema> = {
  name: "get-seo-content-kinds",
  description:
    "Lists the SchemeWeaver SEO edition's curated content kinds (templates): each is a Schema.org type Google rewards " +
    "(article, web-page, profile-page, product, event, local-business, job-posting, recipe, course, video, software-app; " +
    "faq and how-to are marked retired) with its variants (subtypes an editor may pick per page) and its slots — the " +
    "Google-required/recommended properties with friendly labels, accepted editor aliases, built-ins, fixed-value options " +
    "and nested entity parts. Use the slot definitions to build the `slots` argument of save-seo-mapping. " +
    "A kind's `googleFeature.status` is rich-result, foundation or retired. " +
    SEO_NOTE,
  outputSchema,
  slices: ["read", "list"],
  annotations: { readOnlyHint: true },
  handler: async () =>
    executeGetApiCall<unknown, SchemeWeaverApiClient>((client) => client.getSchemeweaverSeoTemplates(CAPTURE_RAW_HTTP_RESPONSE)),
};

export default withStandardDecorators(tool);
