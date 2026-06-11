/**
 * Search Schema.org Types Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { getSchemeweaverSchemaTypesResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  search: z
    .string()
    .optional()
    .describe(
      "Optional search text matched against Schema.org type names and descriptions. Omit to list every available type (~800)."
    ),
};

const outputSchema = z.object({ items: getSchemeweaverSchemaTypesResponse });

const searchSchemaTypesTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "search-schema-types",
  description:
    "Searches the Schema.org vocabulary types available for mapping (e.g. Article, Product, Event, Recipe, LocalBusiness). " +
    "Each result includes the type name, description, parent type and property count. " +
    "Use this first to choose the most specific Schema.org type that fits an Umbraco content type — prefer a specific subtype " +
    "(e.g. BlogPosting over Article over CreativeWork) when the content clearly matches it, as specific types unlock richer Google rich results.",
  inputSchema,
  outputSchema,
  slices: ["search"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ search }) => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverSchemaTypes({ search }, CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(searchSchemaTypesTool);
