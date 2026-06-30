/**
 * Get Schema.org Type Properties Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { getSchemeweaverSchemaTypesByNamePropertiesResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  name: z.string().describe("Schema.org type name, e.g. 'Article' or 'Product' (case sensitive)"),
  ranked: z
    .boolean()
    .optional()
    .describe(
      "When true, results are ranked by real-world importance: 'confidence' (0-100) and 'isPopular' (confidence >= 60) " +
        "reflect how commonly the property is used in structured data and rich results. Recommended for choosing which properties to map."
    ),
};

const outputSchema = z.object({ items: getSchemeweaverSchemaTypesByNamePropertiesResponse });

const getSchemaTypePropertiesTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-schema-type-properties",
  description:
    "Gets the Schema.org properties of a type, including inherited ones (e.g. Article includes headline, author, datePublished from itself and its ancestors). " +
    "Each property has: name, propertyType, acceptedTypes (the Schema.org types the value may take), isComplexType " +
    "(true when the value is itself a structured object like Person or Organization, which needs a nested mapping via nestedSchemaTypeName), " +
    "and with ranked=true a popularity confidence score. " +
    "Names are returned in PascalCase (e.g. 'Headline'); use camelCase ('headline') in save-schema-mapping property names — matching is case-insensitive. " +
    "With ranked=true, map the high-confidence/isPopular/required properties FIRST — these are what Google rich results " +
    "require/recommend — and don't pad with obscure low-ranked ones. " +
    "Use this together with get-content-type-properties to decide which Umbraco property best supplies each schema property, " +
    "and which sourceType fits: a scalar/media prop -> 'property' (the simplest valid choice — don't wrap a lone scalar like " +
    "'brand' in a Brand object); a property flagged isComplexType (a named entity like Person/Organization/Place) -> " +
    "'complexType' with nestedSchemaTypeName, even from a single field (e.g. author -> Person); a Block List/Grid property -> " +
    "'blockContent'. Example: for BlogPosting, ranked surfaces headline/image/author/datePublished at the top — map those before " +
    "wordCount or thumbnailUrl.",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ name, ranked }) => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverSchemaTypesByNameProperties(name, { ranked }, CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(getSchemaTypePropertiesTool);
