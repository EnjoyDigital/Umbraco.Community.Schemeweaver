/**
 * Save Schema Mapping Tool
 *
 * The central write tool: persists a complete content-type -> Schema.org mapping.
 * The input schema is hand-written (rather than the generated Zod body) so every
 * field carries the domain guidance an LLM needs to construct a valid mapping.
 */

import {
  withStandardDecorators,
  executeGetApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { postSchemeweaverMappingsResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const propertyMappingSchema = z.object({
  schemaPropertyName: z
    .string()
    .describe(
      "Schema.org property name, e.g. 'headline', 'datePublished', 'author'. Matched case-insensitively; " +
        "camelCase (as emitted in JSON-LD) is the convention even though get-schema-type-properties lists names in PascalCase."
    ),
  sourceType: z
    .enum(["property", "static", "parent", "ancestor", "sibling", "blockContent", "reference"])
    .describe(
      "Where the value comes from: " +
        "'property' = a property on the content node itself (set contentTypePropertyAlias); " +
        "'static' = a fixed value for all content of this type (set staticValue); " +
        "'parent' = a property on the direct parent node (set contentTypePropertyAlias); " +
        "'ancestor' = a property on the nearest ancestor of a given content type (set sourceContentTypeAlias + contentTypePropertyAlias); " +
        "'sibling' = a property on a sibling node of a given content type (set sourceContentTypeAlias + contentTypePropertyAlias); " +
        "'blockContent' = map Block List/Grid items to nested schema objects or string lists (set contentTypePropertyAlias to the block property, nestedSchemaTypeName, and resolverConfig); " +
        "'reference' = link to a shared graph piece by key (set targetPieceKey, e.g. 'organization' or 'website') — used for publisher/author organisation references."
    ),
  contentTypePropertyAlias: z
    .string()
    .nullish()
    .describe(
      "Umbraco property alias supplying the value. Built-in node aliases ('__name', '__url', '__createDate', '__updateDate') are allowed alongside editor-defined aliases."
    ),
  sourceContentTypeAlias: z
    .string()
    .nullish()
    .describe("For 'ancestor'/'sibling' source types: the content type alias of the node to read from"),
  transformType: z
    .enum(["stripHtml", "toAbsoluteUrl", "formatDate"])
    .nullish()
    .describe(
      "Optional value transform: 'stripHtml' (rich text -> plain text), 'toAbsoluteUrl' (relative URL -> absolute), " +
        "'formatDate' (-> yyyy-MM-dd). Use stripHtml for RichText editors mapped to text properties."
    ),
  isAutoMapped: z
    .boolean()
    .optional()
    .default(false)
    .describe("True when the mapping came from the heuristic auto-mapper unchanged; false for reasoned/manual mappings"),
  staticValue: z.string().nullish().describe("For 'static' source type: the fixed value"),
  nestedSchemaTypeName: z
    .string()
    .nullish()
    .describe(
      "For complex-type properties (isComplexType=true) and 'blockContent': the Schema.org type of the nested object, " +
        "e.g. 'Person' for author, 'ImageObject' for image, 'Question' for FAQ blocks"
    ),
  resolverConfig: z
    .string()
    .nullish()
    .describe(
      "JSON string with advanced nested-mapping config. " +
        "For 'blockContent': {\"nestedMappings\":[{\"blockAlias\":\"...\",\"schemaProperty\":\"...\",\"contentProperty\":\"...\",\"wrapInType\":\"...\",\"wrapInProperty\":\"...\"}]} " +
        "or {\"extractAs\":\"stringList\",\"contentProperty\":\"...\"} for string-array properties like recipeIngredient. " +
        "For complex types: {\"selectedSubType\":\"...\",\"complexTypeMappings\":[{\"schemaProperty\":\"...\",\"sourceType\":\"property|static\",\"contentTypePropertyAlias\":\"...\",\"staticValue\":\"...\"}]}"
    ),
  dynamicRootConfig: z
    .string()
    .nullish()
    .describe("JSON string for dynamic-root node selection (advanced; usually omit)"),
  targetPieceKey: z
    .string()
    .nullish()
    .describe("For 'reference' source type: the graph piece key to link to, e.g. 'organization', 'website'"),
});

const inputSchema = {
  contentTypeAlias: z.string().describe("Umbraco content type alias being mapped, e.g. 'blogPost'"),
  contentTypeKey: z
    .string()
    .uuid()
    .describe("The content type's GUID key — get it from list-content-types"),
  schemaTypeName: z.string().describe("Schema.org type name to map to, e.g. 'BlogPosting'"),
  isEnabled: z
    .boolean()
    .optional()
    .default(true)
    .describe("Whether JSON-LD is generated for this mapping (default true)"),
  isInherited: z
    .boolean()
    .optional()
    .default(false)
    .describe(
      "When true, this schema is also output on all descendant pages of content of this type " +
        "(e.g. an Organization mapping on the home page appearing site-wide). Default false."
    ),
  idOverride: z
    .string()
    .nullish()
    .describe(
      "Optional @id template overriding the default '{url}#{type}'. Supported tokens: " +
        "{url} (absolute page URL), {type} (schema type, lowercase), {key} (content GUID), {culture}, {siteUrl}. " +
        "Example: '{siteUrl}/#organization' for a stable site-wide entity id."
    ),
  propertyMappings: z
    .array(propertyMappingSchema)
    .describe(
      "The complete set of property mappings. Saving REPLACES the existing mapping wholesale — include every mapping " +
        "you want to keep, not just the changed ones."
    ),
};

const outputSchema = postSchemeweaverMappingsResponse;

const saveSchemaMappingTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "save-schema-mapping",
  description:
    "Creates or replaces the SchemeWeaver mapping for an Umbraco content type, defining how its content is expressed as " +
    "Schema.org JSON-LD. Recommended workflow: (1) get-content-type-properties and get-schema-type-properties (ranked=true) " +
    "to understand both sides, (2) suggest-property-mappings for the heuristic baseline, (3) reason about each schema property " +
    "semantically — correct bad suggestions, add mappings the heuristic missed, use nested types for complex values — " +
    "then save with this tool, and (4) verify with preview-json-ld and fix any validation issues it reports. " +
    "Note: this REPLACES any existing mapping for the content type; fetch it first with get-schema-mapping if you are amending.",
  inputSchema,
  outputSchema,
  slices: ["create", "update"],
  annotations: {
    destructiveHint: false,
    idempotentHint: true,
  },
  handler: async (mapping) => {
    return executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.postSchemeweaverMappings(mapping as any, CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(saveSchemaMappingTool);
