/**
 * Set Site Identity Tool — saves the Organisation/Person every page is published by.
 */

import { withStandardDecorators, executeGetApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { siteIdentitySchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

const inputSchema = siteIdentitySchema.omit({ updatedDate: true, updatedByUserKey: true, isConfigured: true }).shape;

export const outputSchema = siteIdentitySchema;

const tool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "set-site-identity",
  description:
    "Replaces the SEO edition's site identity. `name` is required; `kind` is 'organisation' or 'person'; `sameAs` and `url` " +
    "must be absolute URLs; `businessType` must be LocalBusiness or a subtype (see businessTypes in get-seo-content-kinds); " +
    "`searchUrlTemplate` must contain the {search_term_string} placeholder (or the name given in searchQueryInputName). " +
    "`logoMediaKey` is the GUID of a media item. Saving flushes every cached JSON-LD block because the identity is embedded " +
    "in each page's graph as the publisher. Returns the stored identity; a 400 lists the validation problems. " +
    SEO_NOTE,
  inputSchema,
  outputSchema,
  slices: ["update"],
  annotations: { idempotentHint: true },
  handler: async (identity) =>
    executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.putSchemeweaverSeoSiteIdentity(identity as any, CAPTURE_RAW_HTTP_RESPONSE)
    ),
};

export default withStandardDecorators(tool);
