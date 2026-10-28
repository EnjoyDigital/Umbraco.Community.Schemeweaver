/**
 * Get Site Identity Tool — who the site represents (the SEO edition's Organisation/Person).
 */

import { withStandardDecorators, executeGetApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { siteIdentitySchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

export const outputSchema = siteIdentitySchema;

const tool: ToolDefinition<undefined, typeof outputSchema> = {
  name: "get-site-identity",
  description:
    "Reads the SEO edition's site identity: whether the site represents an organisation or a person, its name, logo " +
    "media key, social profiles (sameAs), contact details, optional local-business details (business type, address, " +
    "opening hours, geo, price range) and site search template. `isConfigured` is false when nothing is set; the " +
    "Organization/WebSite graph nodes then come from the schemaSiteSettings content node, if any. " +
    SEO_NOTE,
  outputSchema,
  slices: ["read"],
  annotations: { readOnlyHint: true },
  handler: async () =>
    executeGetApiCall<unknown, SchemeWeaverApiClient>((client) => client.getSchemeweaverSeoSiteIdentity(CAPTURE_RAW_HTTP_RESPONSE)),
};

export default withStandardDecorators(tool);
