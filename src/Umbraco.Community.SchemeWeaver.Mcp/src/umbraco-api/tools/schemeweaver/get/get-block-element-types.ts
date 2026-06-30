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

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  contentTypeAlias: z.string().describe("Umbraco content type alias that owns the block property"),
  propertyAlias: z
    .string()
    .describe("Alias of a Block List / Block Grid property on that content type"),
};

// The Orval-generated zod schema for this response is stale: it only carries
// { alias, name, properties } and omits both the per-property `propertyInfos`
// and their recursive `nestedBlockElementTypes` (blocks nested inside blocks)
// that the backend now returns. A non-strict zod object SILENTLY STRIPS those
// unknown keys from the serialised tool output, which would hide
// blocks-inside-blocks from the assistant. Until `npm run generate` is re-run
// against a TestHost exposing the new fields, the full recursive shape is
// described by hand here. (Leader: drop this once codegen folds the fields in.)

type BlockElementTypeInfoShape = {
  alias: string;
  name: string;
  properties: string[];
  propertyInfos?: Array<{
    alias: string;
    name: string;
    editorAlias: string;
    valueSchema?: string | null;
    nestedBlockElementTypes?: BlockElementTypeInfoShape[];
  }>;
};

const blockElementTypeInfoSchema: z.ZodType<BlockElementTypeInfoShape> = z.lazy(() =>
  z.object({
    alias: z.string().describe("Element type alias"),
    name: z.string().describe("Element type display name"),
    properties: z
      .array(z.string())
      .describe("Property aliases on this element type (plain list, kept for backward compatibility)"),
    propertyInfos: z
      .array(blockElementPropertyInfoSchema)
      .optional()
      .describe(
        "Full per-property info (alias, name, editorAlias). A property whose editorAlias is a " +
          "Block List/Grid carries its own nestedBlockElementTypes — the blocks nested inside this block."
      ),
  })
);

const blockElementPropertyInfoSchema: z.ZodType<
  NonNullable<BlockElementTypeInfoShape["propertyInfos"]>[number]
> = z.object({
  alias: z.string().describe("Property alias on the element type"),
  name: z.string().describe("Property display name"),
  editorAlias: z.string().describe("Umbraco property editor alias (e.g. Umbraco.BlockList, Umbraco.BlockGrid)"),
  valueSchema: z
    .string()
    .nullish()
    .describe(
      "The property's value JSON Schema (Umbraco 17.4+) as a stringified JSON object — the actual stored-value " +
        "shape (types, maxLength, UUID/crop structure, ranges). Use it to map a block property precisely beyond " +
        "the editor alias (e.g. a RichText block property feeding a plain-text Schema.org property → stripHtml). " +
        "Null on Umbraco < 17.4 or for no-schema editors."
    ),
  nestedBlockElementTypes: z
    .array(blockElementTypeInfoSchema)
    .optional()
    .describe(
      "When this property is itself a Block List/Grid (a block nested inside a block), the element " +
        "types allowed within it — resolved recursively (depth-capped). Empty for non-block properties. " +
        "Use these to build nested `routes`-within-a-property-mapping configs in save-schema-mapping."
    ),
});

const outputSchema = z.object({
  items: z.array(blockElementTypeInfoSchema),
});

const getBlockElementTypesTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-block-element-types",
  description:
    "Gets the element types allowed inside a Block List or Block Grid property, with each element type's alias, name and " +
    "property aliases (both the flat `properties` list and richer `propertyInfos` with editor aliases). " +
    "Blocks can be nested inside blocks (and Block Grid areas): when an element-type property is itself a Block List/Grid, " +
    "its `propertyInfos[].nestedBlockElementTypes` lists the element types allowed one level deeper, resolved recursively " +
    "(depth-capped). " +
    "Needed when creating a property mapping with sourceType 'blockContent'. Use the element-type structure to pick the shape: " +
    "(a) extractAs stringList when each block carries ONE meaningful text prop (a label) — e.g. an 'ingredients' Block List " +
    "whose block has a single 'ingredient' text prop maps to Recipe.recipeIngredient as a flat string list; " +
    "(b) nestedMappings / routes when blocks have SEVERAL fields — e.g. a 'howToSteps' block (stepName, stepText) maps to " +
    "HowTo.step as HowToStep objects, and a 'faqBlock' element type maps to a Question for an FAQPage; " +
    "(c) a body-sections container (contentGrid/blocks/sections) maps to mainEntity or hasPart as WebPageElement covering " +
    "every element type's fields. When a block property is itself a Block List/Grid (propertyInfos[].nestedBlockElementTypes " +
    "is populated), route it deeper via nested `routes` in the resolverConfig.",
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
