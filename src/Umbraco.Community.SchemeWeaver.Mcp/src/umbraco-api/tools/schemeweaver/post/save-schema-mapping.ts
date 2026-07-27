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
    .enum(["property", "static", "complexType", "parent", "ancestor", "sibling", "blockContent", "reference"])
    .describe(
      "Where the value comes from. Choosing the right one is how a reasoned mapping beats the name-only heuristic. " +
        "Prefer the SIMPLEST valid choice — use 'property' whenever a single scalar feeds the schema property, even if " +
        "Schema.org permits a wrapper object (do NOT wrap a lone 'brand' scalar in a Brand object). Values: " +
        "'property' = a property on the content node itself (set contentTypePropertyAlias); the default for scalars and media. " +
        "Built-ins __name/__url/__createDate/__updateDate are always available here. " +
        "A content-picker property (Umbraco.ContentPicker or Umbraco.MultiNodeTreePicker) under 'property' renders the picked " +
        "node(s): by default the node name; with nestedSchemaTypeName set, the whole picked node via its own content type's " +
        "mapping; or DRILL INTO one property of the picked node via resolverConfig " +
        '{"pickedPropertyAlias":"...","pickedContentTypeAlias":"...?"} (drill-down wins over nestedSchemaTypeName; ' +
        "pickedContentTypeAlias is only a backoffice UI hint). MNTP emits a list when several nodes are picked. " +
        "'static' = a fixed value for all content of this type (set staticValue, contentTypePropertyAlias=null); " +
        "'complexType' = the schema property denotes a named ENTITY (Person, Organization, Place, PostalAddress, Offer…); " +
        "nest it even from a single field (e.g. author -> Person from one authorName text prop). Set nestedSchemaTypeName " +
        "and a `complexTypeMappings` resolverConfig; " +
        "'parent' = a property on the direct parent node (set contentTypePropertyAlias) — e.g. a 'category' grouping from the parent's title; " +
        "'ancestor' = a property on the nearest ancestor of a given content type (set sourceContentTypeAlias + contentTypePropertyAlias); " +
        "'sibling' = a property on a sibling node of a given content type (set sourceContentTypeAlias + contentTypePropertyAlias); " +
        "'blockContent' = map Block List/Grid items (including blocks nested inside blocks and Block Grid areas) to nested schema objects or string lists (set contentTypePropertyAlias to the block property, nestedSchemaTypeName, and a `routes` resolverConfig — nestable for blocks-within-blocks); " +
        "a body-sections container (contentGrid/blocks/sections/rows) is the page's structural content — map it to mainEntity or hasPart as WebPageElement, never leave it unmapped; " +
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
      "JSON string with advanced nested-mapping config (passed through verbatim to the C# resolvers). " +
        "For 'blockContent', the preferred shape is `routes` — one route per block element type: " +
        '{"routes":[{"blockAlias":"...","nestedSchemaType":"...","propertyMappings":[' +
        '{"schemaProperty":"...","contentProperty":"...","wrapInType":"...?","wrapInProperty":"...?",' +
        '"transformType":"stripHtml|toAbsoluteUrl|formatDate?","extractAs":"stringList?","nestedContentProperty":"...?","routes":[ ...same route shape... ]}]}]}. ' +
        "A nested propertyMappings entry supports the same `transformType` as a top-level mapping " +
        "(e.g. stripHtml on a nested RichText answer). " +
        "A propertyMappings entry whose `contentProperty` is itself a nested Block List/Grid (a block inside a " +
        "block, or a Block Grid area) carries its own `routes`, recursing one level deeper — discover the allowed " +
        "nested element types via get-block-element-types (`propertyInfos[].nestedBlockElementTypes`). " +
        "Nested-routes example — an FAQPage whose `sections` blocks each contain a nested `questions` Block List: " +
        '{"routes":[{"blockAlias":"faqSection","nestedSchemaType":"ItemList","propertyMappings":[' +
        '{"schemaProperty":"itemListElement","contentProperty":"questions","routes":[' +
        '{"blockAlias":"faqItem","nestedSchemaType":"Question","propertyMappings":[' +
        '{"schemaProperty":"name","contentProperty":"questionText"},' +
        '{"schemaProperty":"acceptedAnswer","contentProperty":"answerText","wrapInType":"Answer","wrapInProperty":"text"}]}]}]}]}. ' +
        "Ordered ItemList (opt-in): set \"wrapInListItem\":true to wrap each mapped block as a " +
        "ListItem{position,item} (auto-incremented position, or read one from a block property via " +
        "\"positionProperty\":\"...\") — use this for a numbered services/steps block feeding an " +
        "ItemList.itemListElement. The default (omitted) emits a bare Thing array. Valid both at root level " +
        "AND on a nested propertyMappings[] entry whose contentProperty is itself a block list feeding an " +
        "ItemList (e.g. services nested inside an ItemList) — set it on that nested entry for the nested list. " +
        "Drop empty blocks (opt-in): a route may set \"requiredProperties\":[\"name\",\"acceptedAnswer\"] so a nested " +
        "Thing missing any of those is omitted (a blank block row never emits an invalid empty node). " +
        "For a flat string array (e.g. recipeIngredient) use the top-level string-list mode: " +
        '{"extractAs":"stringList","contentProperty":"..."}. ' +
        "The legacy flat shape {\"nestedMappings\":[{\"blockAlias\":\"...\",\"schemaProperty\":\"...\",\"contentProperty\":\"...\",\"wrapInType\":\"...\",\"wrapInProperty\":\"...\"}]} " +
        "is still accepted for single-level blocks but `routes` is preferred and required for nesting. " +
        "For complex types: {\"selectedSubType\":\"...\",\"complexTypeMappings\":[{\"schemaProperty\":\"...\"," +
        "\"sourceType\":\"property|static|parent|ancestor|sibling|complexType\",\"contentTypePropertyAlias\":\"...\"," +
        "\"staticValue\":\"...\",\"sourceContentTypeAlias\":\"...?\",\"transformType\":\"...?\"}]} — a parent/ancestor/sibling " +
        "sub-row reads the property off the related node (relative to the PAGE, at any nesting depth; ancestor/sibling need " +
        "sourceContentTypeAlias), e.g. an inline Organization whose name/logo read the site root. " +
        "For a content-picker/MNTP 'property' row, drill into one property of the picked node with: " +
        "{\"pickedPropertyAlias\":\"...\",\"pickedContentTypeAlias\":\"...?\"} (wins over nestedSchemaTypeName)."
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

// The generated schema (regenerated from the C# DTO) already carries reachability,
// warnings, driftStatus and persistedTo, so no shim is needed — use it directly.
export const outputSchema = postSchemeweaverMappingsResponse;

const saveSchemaMappingTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "save-schema-mapping",
  description:
    "Creates or replaces the SchemeWeaver mapping for an Umbraco content type, defining how its content is expressed as " +
    "Schema.org JSON-LD. Recommended workflow: (1) get-content-type-properties and get-schema-type-properties (ranked=true) " +
    "to understand both sides, (2) suggest-property-mappings for the heuristic baseline, (3) reason about each schema property " +
    "semantically — correct bad suggestions, add mappings the heuristic missed, use nested types for complex values — " +
    "then save with this tool, and (4) verify with preview-json-ld + validate-mapping and LOOP fixing until allClear. " +
    "Worked rows: a single 'authorName' text prop under BlogPosting.author -> {sourceType:'complexType', " +
    "nestedSchemaTypeName:'Person', resolverConfig:'{\"complexTypeMappings\":[{\"schemaProperty\":\"Name\"," +
    "\"sourceType\":\"property\",\"contentTypePropertyAlias\":\"authorName\"}]}'}; an 'ingredients' Block List of one-field " +
    "blocks under Recipe.recipeIngredient -> {sourceType:'blockContent', resolverConfig:'{\"extractAs\":\"stringList\"," +
    "\"contentProperty\":\"ingredient\"}'}; a plain 'brand' scalar under Vehicle.brand -> {sourceType:'property', " +
    "contentTypePropertyAlias:'brand'} (NOT a Brand object). " +
    "IMPORTANT: inspect the `warnings` array on the response — it flags properties mapped outside their Schema.org range " +
    "that will be SILENTLY DROPPED from the JSON-LD (e.g. a non-CreativeWork type under hasPart); re-home those to a property " +
    "like about/mainEntity. Also check `reachability`: composed-from-block means this type only emits inside a containing " +
    "page's block mapping, never on its own URL. " +
    "The response reports `persistedTo`: by default a save lands in the DATABASE ONLY (`database`) — to reproduce it as " +
    "config-as-code, run export-mappings-to-usync (or check get-usync-drift). `database+usync` means export-on-save is " +
    "enabled and it also reached disk. " +
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
