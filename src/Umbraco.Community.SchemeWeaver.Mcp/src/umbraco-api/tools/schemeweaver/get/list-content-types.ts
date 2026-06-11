/**
 * List Umbraco Content Types Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

// The endpoint returns an anonymous projection, so the shape is declared here
// rather than generated (see SchemeWeaverApiController.GetContentTypes).
const outputSchema = z.object({
  items: z.array(
    z.object({
      alias: z.string(),
      name: z.string().nullish(),
      key: z.string(),
      propertyCount: z.number(),
    })
  ),
});

const listContentTypesTool: ToolDefinition<undefined, typeof outputSchema> = {
  name: "list-content-types",
  description:
    "Lists all Umbraco content types (document types) in the site with their alias, name, key and property count. " +
    "Use this to find the content type alias to map to a Schema.org type. " +
    "Element types used by block editors are included too — those matter for blockContent property mappings.",
  outputSchema,
  slices: ["list"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async () => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverContentTypes(CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(listContentTypesTool);
