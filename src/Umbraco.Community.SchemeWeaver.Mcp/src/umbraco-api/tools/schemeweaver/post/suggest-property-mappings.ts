/**
 * Suggest Property Mappings (Heuristic) Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { postSchemeweaverMappingsByContentTypeAliasAutoMapResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  contentTypeAlias: z.string().describe("Umbraco content type alias to suggest mappings for"),
  schemaTypeName: z.string().describe("Schema.org type name to map to, e.g. 'Article'"),
};

const outputSchema = z.object({
  items: postSchemeweaverMappingsByContentTypeAliasAutoMapResponse,
});

const suggestPropertyMappingsTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "suggest-property-mappings",
  description:
    "Runs SchemeWeaver's built-in heuristic auto-mapper (exact/synonym/substring name matching) and returns property mapping " +
    "suggestions with a confidence score per schema property (0-100; >=80 high, >=50 medium). " +
    "Treat this as a FLOOR, not the answer: keep its correct rows, fix the wrong ones, and ADD the mappings it cannot " +
    "express (it only does flat name matches — no meaning, units, nested entities or block structure). " +
    "Reason semantically about each schema property: 'strapline' -> alternativeName, 'standfirst'/'intro' -> description, " +
    "'bodyText' -> articleBody, 'heroImage' -> image. Then build the final mapping yourself, choosing the right sourceType " +
    "(property for a scalar/media — the simplest valid choice; complexType for a named entity like Person/Organization even " +
    "from one field; blockContent with extractAs stringList / nestedMappings / nestable routes for Block List/Grid; " +
    "parent/ancestor/sibling for related nodes; static; reference) and persist it with save-schema-mapping. " +
    "Example improvement: the heuristic leaves a 'authorName' text prop unmapped under BlogPosting.author (it expects a " +
    "Person); you map it as sourceType 'complexType', nestedSchemaTypeName 'Person'. " +
    "Suggestions with isAutoMapped=false are unmapped schema properties listed for completeness — good candidates for manual reasoning.",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
    idempotentHint: true,
  },
  handler: async ({ contentTypeAlias, schemaTypeName }) => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.postSchemeweaverMappingsByContentTypeAliasAutoMap(
        contentTypeAlias,
        { schemaTypeName },
        CAPTURE_RAW_HTTP_RESPONSE
      )
    );
  },
};

export default withStandardDecorators(suggestPropertyMappingsTool);
