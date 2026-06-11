/**
 * Get Block Element Types Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { getSchemeweaverContentTypesByContentTypeAliasPropertiesByPropertyAliasBlockTypesResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  contentTypeAlias: z.string().describe("Umbraco content type alias that owns the block property"),
  propertyAlias: z
    .string()
    .describe("Alias of a Block List / Block Grid property on that content type"),
};

const outputSchema = z.object({
  items: getSchemeweaverContentTypesByContentTypeAliasPropertiesByPropertyAliasBlockTypesResponse,
});

const getBlockElementTypesTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-block-element-types",
  description:
    "Gets the element types allowed inside a Block List or Block Grid property, with each element type's alias, name and property aliases. " +
    "Needed when creating a property mapping with sourceType 'blockContent': blocks of a given element type can be mapped " +
    "to a nested Schema.org object (e.g. a 'faqBlock' element type mapped to a Question for an FAQPage).",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ contentTypeAlias, propertyAlias }) => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverContentTypesByContentTypeAliasPropertiesByPropertyAliasBlockTypes(
        contentTypeAlias,
        propertyAlias,
        CAPTURE_RAW_HTTP_RESPONSE
      )
    );
  },
};

export default withStandardDecorators(getBlockElementTypesTool);
